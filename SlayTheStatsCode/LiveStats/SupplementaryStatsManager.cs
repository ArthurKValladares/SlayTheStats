using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Runs;
using FileAccess = Godot.FileAccess;
using MegaCrit.Sts2.Core.Saves;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.LiveStats;

public static class SupplementaryStatsManager
{
    private const string SubDir        = "saves/slaythestats";
    private const string InProgressFile = "current_run.irs";

    // Aggregated totals: (ascension, buildId) → cardIdEntry → (drawn, played)
    private static readonly Dictionary<(int, string), Dictionary<string, (int drawn, int played)>> _data = new();

    private static string GetDir() =>
        UserDataPathProvider.GetProfileScopedPath(SaveManager.Instance.CurrentProfileId, SubDir);

    private static string InProgressPath => $"{GetDir()}/{InProgressFile}";

    // ── Called by patches ────────────────────────────────────────────────────

    /// <summary>
    /// Called from RunStarted after Reset(). If current_run.irs exists this is a continued
    /// run; restore saved partial data into the tracker. No-op for new runs.
    /// </summary>
    public static void TryRestoreIfContinued(int ascension, string buildId)
    {
        var data = TryLoad(InProgressPath);
        if (data == null) return;
        InRunStatsTracker.RestoreFrom(data.CardStats);
        MainFile.Logger.Info($"[SupplementaryStats] Restored in-progress data ({data.CardStats.Count} cards)");
    }

    /// <summary>Quit mid-run: persist current tracker state as the in-progress file.</summary>
    public static void OnQuit(int ascension, string buildId)
    {
        var cardStats = InRunStatsTracker.GetCardStats();
        if (cardStats.Count == 0) return;

        var data = BuildData(ascension, buildId, cardStats);
        TryWrite(InProgressPath, data);
        MainFile.Logger.Info("[SupplementaryStats] Wrote in-progress file on quit");
    }

    /// <summary>Run completed: write the final file, delete the in-progress file, reset.</summary>
    public static void SaveForRun(RunHistory history)
    {
        var cardStats = InRunStatsTracker.GetCardStats();
        if (cardStats.Count == 0)
        {
            TryDelete(InProgressPath);
            return;
        }

        var data = BuildData(history.Ascension, history.BuildId, cardStats);

        string dir  = GetDir();
        EnsureDir(dir);
        string path = $"{dir}/{history.StartTime}.irs";

        if (TryWrite(path, data))
        {
            TryDelete(InProgressPath);
            Merge(data);
            MainFile.Logger.Info($"[SupplementaryStats] Saved final file: {history.StartTime}.irs");
        }
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    public static void LoadAll()
    {
        _data.Clear();

        string dir = GetDir();
        if (!DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(dir))) return;

        string[] files = DirAccess.GetFilesAt(dir);
        if (files == null) return;

        int loaded = 0;
        foreach (string fileName in files)
        {
            // Skip the in-progress file — it's partial and not yet complete
            if (!fileName.EndsWith(".irs") || fileName == InProgressFile) continue;

            var data = TryLoad($"{dir}/{fileName}");
            if (data == null) continue;

            Merge(data);
            loaded++;
        }

        MainFile.Logger.Info($"[SupplementaryStats] Loaded {loaded} supplementary files");
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public static (float drawn, float played, float playRate)? GetCardDrawPlayStats(
        int ascension, string buildId, string cardIdEntry)
    {
        if (!TryGet(ascension, buildId, cardIdEntry, out int drawn, out int played))
            return null;
        float playRate = drawn == 0 ? 0f : played / (float)drawn;
        return (drawn, played, playRate);
    }

    public static (float drawn, float played, float playRate)? GetCardDrawPlayStatsAllPatches(
        int ascension, string cardIdEntry) =>
        GetCardDrawPlayStats(ascension, RunDataManager.AllPatches, cardIdEntry);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SupplementaryRunData BuildData(int ascension, string buildId,
        IReadOnlyDictionary<string, CardDrawPlayData> cardStats) =>
        new()
        {
            Ascension = ascension,
            BuildId   = buildId,
            CardStats = cardStats.ToDictionary(
                kvp => kvp.Key,
                kvp => new CardDrawPlayData { Drawn = kvp.Value.Drawn, Played = kvp.Value.Played })
        };

    private static void Merge(SupplementaryRunData data)
    {
        AddTo((data.Ascension, data.BuildId),         data.CardStats);
        AddTo((data.Ascension, RunDataManager.AllPatches), data.CardStats);
    }

    private static void AddTo((int, string) key, Dictionary<string, CardDrawPlayData> cardStats)
    {
        if (!_data.TryGetValue(key, out var bucket))
        {
            bucket = new Dictionary<string, (int, int)>();
            _data[key] = bucket;
        }
        foreach (var (cardId, stats) in cardStats)
        {
            bucket.TryGetValue(cardId, out var existing);
            bucket[cardId] = (existing.drawn + stats.Drawn, existing.played + stats.Played);
        }
    }

    private static bool TryGet(int asc, string buildId, string cardId, out int drawn, out int played)
    {
        drawn = played = 0;
        if (!_data.TryGetValue((asc, buildId), out var bucket)) return false;
        if (!bucket.TryGetValue(cardId, out var t)) return false;
        drawn  = t.drawn;
        played = t.played;
        return drawn > 0 || played > 0;
    }

    private static bool TryWrite(string path, SupplementaryRunData data)
    {
        try
        {
            EnsureDir(GetDir());
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                MainFile.Logger.Error($"[SupplementaryStats] Cannot write {path}: {FileAccess.GetOpenError()}");
                return false;
            }
            file.StoreString(json);
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"[SupplementaryStats] Write failed ({path}): {ex.Message}");
            return false;
        }
    }

    private static SupplementaryRunData? TryLoad(string path)
    {
        try
        {
            if (!FileAccess.FileExists(path)) return null;
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (file == null) return null;
            return JsonSerializer.Deserialize<SupplementaryRunData>(file.GetAsText());
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[SupplementaryStats] Load failed ({path}): {ex.Message}");
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)); }
        catch { /* ignore */ }
    }

    private static void EnsureDir(string dir) =>
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
}
