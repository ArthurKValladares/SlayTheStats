using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SlayTheStats.SlayTheStatsCode.RunData;


public class RunDataManager
{
    private static RunDataManager? _instance;
    
    // TODO: This is very temp, just sketching stuff out
    private readonly Dictionary<ModelId, int> _numberOfTimesEndedInDeck = new();
    private readonly Dictionary<ModelId, int> _numberOfWins = new();
    
    private readonly Dictionary<string, int> _ancientNumberOfTimesShown = new();
    private readonly Dictionary<string, int> _ancientNumberOfTimesChosen = new();
    
    private readonly Dictionary<ModelId, int> _cardNumberOfTimesShownInReward = new();
    private readonly Dictionary<ModelId, int> _cardNumberOfTimesChosenInReward = new();
    
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
            if (cardId == null || cardId == ModelId.none || !alreadySeenCards.Add(cardId))
                continue;

            _numberOfTimesEndedInDeck.TryGetValue(cardId, out int cardCount);
            _numberOfTimesEndedInDeck[cardId] = cardCount + 1;

            if (runHistory.Win)
            {
                _numberOfWins.TryGetValue(cardId, out int winCount);
                _numberOfWins[cardId] = winCount + 1;
            }
        }   
    }
    
    private void AddRelicDataToRunHistory(RunHistory runHistory, RunHistoryPlayer player)
    {
        IEnumerable<SerializableRelic>? relics = player?.Relics;
        if (relics == null) return;

        // TODO: This logic here is very temporary, just for testing.
        HashSet<ModelId> alreadySeenRelics = new();
        foreach (SerializableRelic relic in relics)
        {
            ModelId? relicId = relic?.Id;
            if (relicId == null || relicId == ModelId.none || !alreadySeenRelics.Add(relicId))
                continue;

            _numberOfTimesEndedInDeck.TryGetValue(relicId, out int cardCount);
            _numberOfTimesEndedInDeck[relicId] = cardCount + 1;

            if (runHistory.Win)
            {
                _numberOfWins.TryGetValue(relicId, out int winCount);
                _numberOfWins[relicId] = winCount + 1;
            }
        }   
    }

    private void AddAncientDataToRunHistory(RunHistory runHistory, ulong playerId)
    {
        List<List<MapPointHistoryEntry>> mapHistory = runHistory.MapPointHistory;

        foreach (List<MapPointHistoryEntry> actHistory in mapHistory)
        {
            var ancient = actHistory.Find(entry => entry.MapPointType == MapPointType.Ancient);
            if (ancient == null) continue;

            PlayerMapPointHistoryEntry? playerStat = ancient.PlayerStats.Find(playerStat => playerStat.PlayerId == playerId);
            if (playerStat == null) continue;
            
            foreach (AncientChoiceHistoryEntry ancientChoice in playerStat.AncientChoices)
            {
                string key = ancientChoice.Title.LocEntryKey;

                _ancientNumberOfTimesShown.TryGetValue(key, out int shownCount);
                _ancientNumberOfTimesShown[key] = shownCount + 1;

                if (ancientChoice.WasChosen)
                {
                    _ancientNumberOfTimesChosen.TryGetValue(key, out int chosenCount);
                    _ancientNumberOfTimesChosen[key] = chosenCount + 1;
                }
            }
        }
    }
    
    private void AddCardRewardDataToRunHistory(RunHistory runHistory, ulong playerId)
    {
        List<List<MapPointHistoryEntry>> mapHistory = runHistory.MapPointHistory;

        foreach (List<MapPointHistoryEntry> actHistory in mapHistory)
        {
            foreach (MapPointHistoryEntry entry in actHistory)
            {
                PlayerMapPointHistoryEntry? playerStat = entry.PlayerStats.Find(playerStat => playerStat.PlayerId == playerId);
                if (playerStat == null) continue;

                if (playerStat.CardChoices.Count() != 0 && entry.MapPointType != MapPointType.Shop)
                {
                    foreach (CardChoiceHistoryEntry cardChoice in playerStat.CardChoices)
                    {
                        ModelId? id = cardChoice.Card.Id;
                        if (id == null) continue;
                        
                        _cardNumberOfTimesShownInReward.TryGetValue(id, out int shownCount);
                        _cardNumberOfTimesShownInReward[id] = shownCount + 1;

                        if (cardChoice.wasPicked)
                        {
                            _cardNumberOfTimesChosenInReward.TryGetValue(id, out int chosenCount);
                            _cardNumberOfTimesChosenInReward[id] = chosenCount + 1;
                        }
                    }
                }
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
            AddRelicDataToRunHistory(runHistory, player);

            AddAncientDataToRunHistory(runHistory, player.Id);
            
            AddCardRewardDataToRunHistory(runHistory, player.Id);
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
    
    public float GetAncientPickPercentage(LocString title)
    {
        string key = title.LocEntryKey;
        
        int timesShown = _ancientNumberOfTimesShown.GetValueOrDefault(key);
        if (timesShown == 0) return 0.0f;
        
        int pickCount = _ancientNumberOfTimesChosen.GetValueOrDefault(key);

        return pickCount / (float) timesShown;
    }

    public float GetCardRewardPickPercentage(ModelId id)
    {
        int numberOfTimesShown = _cardNumberOfTimesShownInReward.GetValueOrDefault(id);
        if(numberOfTimesShown == 0) return 0.0f;

        int numberOfTimesChosen = _cardNumberOfTimesChosenInReward.GetValueOrDefault(id);
        
        return numberOfTimesChosen / (float) numberOfTimesShown;
    }
}