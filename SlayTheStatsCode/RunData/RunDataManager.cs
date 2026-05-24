using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SlayTheStats.SlayTheStatsCode.RunData;


public class RunDataManager
{
    private static RunDataManager? _instance;
    

    // TODO: This is very temp, just sketching stuff out
    private readonly Dictionary<ModelId, int> _numberOfTimesEndedInDeck = new();
    private readonly Dictionary<ModelId, int> _numberOfWins = new();

    private static RunDataManager ConstructDefault()
    {
        RunDataManager runDataManager = new RunDataManager();
        return runDataManager;
    }

    public static RunDataManager Instance
    {
        get
        {
            RunDataManager._instance ??= RunDataManager.ConstructDefault();
            return RunDataManager._instance;
        }
    }

    private void AddDeckDataToRunHistory(RunHistory runHistory, RunHistoryPlayer player)
    {
        IEnumerable<SerializableCard>? deck = player?.Deck;
        if (deck == null) return;

        // TODO: This logic here is very temporary, just for testing.
        HashSet<ModelId> alreadySeenCards = new();
        foreach (SerializableCard card in deck)
        {
            ModelId? cardId = card?.Id;
            if (cardId == null || cardId == ModelId.none || alreadySeenCards.Contains(cardId))
                continue;
                
            alreadySeenCards.Add(cardId);
                
            _numberOfTimesEndedInDeck.TryGetValue(cardId, out int cardCount);
            _numberOfTimesEndedInDeck[cardId] = cardCount + 1;

            if (runHistory.Win)
            {
                _numberOfWins.TryGetValue(cardId, out int winCount);
                _numberOfWins[cardId] = winCount + 1;
            }
        }   
    }
    
    public void AddRunToHistory(RunHistory runHistory)
    {
        foreach (var player in runHistory.Players)
        {
            if (player.Id != 1 && player.Id != PlatformUtil.GetLocalPlayerId(PlatformType.Steam))
                continue;
            
            string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, player.Id);
            Log.Info($"Adding run {runHistory.StartTime} to history for player {playerName} id: {player.Id}");

            AddDeckDataToRunHistory(runHistory, player);
        }
    }
    
    public void LoadAllRuns()
    {
        SaveManager saveManager = SaveManager.Instance;
        List<string> runHistoryFileNames;

        try
        {
            runHistoryFileNames = saveManager.GetAllRunHistoryNames();
            Log.Info($"Loaded {runHistoryFileNames.Count} run history files");
        }
        catch (Exception ex)
        {
            return;
        }
        
        foreach (String name in runHistoryFileNames)
        {
            ReadSaveResult<RunHistory> readSaveResult = SaveManager.Instance.LoadRunHistory(name);
            
            if (!readSaveResult.Success || readSaveResult.SaveData == null)
                continue;
            
            AddRunToHistory(readSaveResult.SaveData);
        }
    }

    public float GetWinPercentage(ModelId id)
    {
        int endedInDeckCount = _numberOfTimesEndedInDeck.GetValueOrDefault(id);
        if (endedInDeckCount == 0) return 0.0f;
        
        int winCount = _numberOfWins.GetValueOrDefault(id);
        
        return winCount / (float) endedInDeckCount;        
    }
}