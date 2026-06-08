using MegaCrit.Sts2.Core.Models;

namespace SlayTheStats.SlayTheStatsCode.LiveStats;

/// <summary>
/// Accumulates live in-game stats during a single run. Resets when a new run starts.
/// Write here from patches; read from SupplementaryStatsManager at run end.
/// </summary>
public static class InRunStatsTracker
{
    private static readonly Dictionary<string, CardDrawPlayData> _cardStats = new();

    public static void Reset() => _cardStats.Clear();

    /// <summary>Restore saved counts from a resumed run's in-progress file.</summary>
    public static void RestoreFrom(Dictionary<string, CardDrawPlayData> saved)
    {
        _cardStats.Clear();
        foreach (var (key, data) in saved)
            _cardStats[key] = new CardDrawPlayData { Drawn = data.Drawn, Played = data.Played };
    }

    public static void RecordDraw(CardModel card) => GetOrCreate(card.Id.Entry).Drawn++;
    public static void RecordPlay(CardModel card) => GetOrCreate(card.Id.Entry).Played++;

    public static IReadOnlyDictionary<string, CardDrawPlayData> GetCardStats() => _cardStats;

    private static CardDrawPlayData GetOrCreate(string key)
    {
        if (!_cardStats.TryGetValue(key, out var data))
        {
            data = new CardDrawPlayData();
            _cardStats[key] = data;
        }
        return data;
    }
}
