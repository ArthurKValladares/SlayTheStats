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

 public class SuccessRateTracker<TK> where TK : notnull
 {
    public void Attempted(TK key)
    {
        _numAttempt.TryGetValue(key, out int count);
        _numAttempt[key] = count + 1;
    }
    
    public void Succeeded(TK key)
    {
        _numSuccess.TryGetValue(key, out int count);
        _numSuccess[key] = count + 1;
    }

    public float SuccessRate(TK key)
    {
        int numAttempts = _numAttempt.GetValueOrDefault(key);
        if(numAttempts == 0) return 0.0f;

        int numSuccess = _numSuccess.GetValueOrDefault(key);
        
        return numSuccess / (float) numAttempts;
    }
    
    private readonly Dictionary<TK, int> _numAttempt = new();
    private readonly Dictionary<TK, int> _numSuccess = new();   
}

public class RunDataManager
{
    private static RunDataManager? _instance;
    
    private readonly SuccessRateTracker<ModelId> _wonWithCard = new();
    private readonly SuccessRateTracker<ModelId> _wonWithRelic = new();
    private readonly SuccessRateTracker<string> _pickedAncientRelic = new();
    private readonly SuccessRateTracker<ModelId> _pickedFromCardReward = new();
    private readonly SuccessRateTracker<ModelId> _boughtCardFromShop = new();
    private readonly SuccessRateTracker<ModelId> _boughtRelicFromShop = new();
    
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

    private void RecordCardWinData(RunHistory runHistory, RunHistoryPlayer player)
    {
        IEnumerable<SerializableCard>? deck = player?.Deck;
        if (deck == null) return;
        
        HashSet<ModelId> alreadySeenCards = new();
        foreach (SerializableCard card in deck)
        {
            ModelId? cardId = card?.Id;
            if (cardId == null || cardId == ModelId.none || !alreadySeenCards.Add(cardId))
                continue;

            _wonWithCard.Attempted(cardId);
            
            if (runHistory.Win)
            {
                _wonWithCard.Succeeded(cardId);
            }
        }   
    }
    
    private void RecordRelicWinData(RunHistory runHistory, RunHistoryPlayer player)
    {
        IEnumerable<SerializableRelic>? relics = player?.Relics;
        if (relics == null) return;
        
        HashSet<ModelId> alreadySeenRelics = new();
        foreach (SerializableRelic relic in relics)
        {
            ModelId? relicId = relic?.Id;
            if (relicId == null || relicId == ModelId.none || !alreadySeenRelics.Add(relicId))
                continue;
            
            _wonWithRelic.Attempted(relicId);

            if (runHistory.Win)
            {
                _wonWithRelic.Succeeded(relicId);
            }
        }   
    }

    private void RecordAncientPickData(RunHistory runHistory, ulong playerId)
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
                
                _pickedAncientRelic.Attempted(key);

                if (ancientChoice.WasChosen)
                {
                    _pickedAncientRelic.Succeeded(key);
                }
            }
        }
    }
    
    private void RecordCardRewardPickData(RunHistory runHistory, ulong playerId)
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
                        
                        _pickedFromCardReward.Attempted(id);

                        if (cardChoice.wasPicked)
                        {
                            _pickedFromCardReward.Succeeded(id);
                        }
                    }
                }
            }
        }
    }

    public void RecordShopCardPurchaseData(RunHistory runHistory, ulong playerId)
    {
        List<List<MapPointHistoryEntry>> mapHistory = runHistory.MapPointHistory;

        foreach (List<MapPointHistoryEntry> actHistory in mapHistory)
        {
            foreach (MapPointHistoryEntry entry in actHistory)
            {
                if (entry.MapPointType != MapPointType.Shop) continue;
                
                PlayerMapPointHistoryEntry? playerStat = entry.PlayerStats.Find(playerStat => playerStat.PlayerId == playerId);
                if (playerStat == null) continue;


                foreach (CardChoiceHistoryEntry cardOffered in playerStat.CardChoices)
                {
                    ModelId? id = cardOffered.Card.Id;
                    if (id == null) continue;
                    
                    _boughtCardFromShop.Attempted(id);
                }

                foreach (SerializableCard cardBought in playerStat.CardsGained)
                {
                    ModelId? id = cardBought.Id;
                    if (id == null) continue;
                    
                    _boughtCardFromShop.Attempted(id);
                    _boughtCardFromShop.Succeeded(id);
                }
            }
        }
    }
    
    public void RecordShopRelicPurchaseData(RunHistory runHistory, ulong playerId)
    {
        List<List<MapPointHistoryEntry>> mapHistory = runHistory.MapPointHistory;

        foreach (List<MapPointHistoryEntry> actHistory in mapHistory)
        {
            foreach (MapPointHistoryEntry entry in actHistory)
            {
                if (entry.MapPointType != MapPointType.Shop) continue;
                
                PlayerMapPointHistoryEntry? playerStat = entry.PlayerStats.Find(playerStat => playerStat.PlayerId == playerId);
                if (playerStat == null) continue;


                foreach (ModelChoiceHistoryEntry relicOffered in playerStat.RelicChoices)
                {
                    ModelId id = relicOffered.choice;
                    
                    _boughtRelicFromShop.Attempted(id);
                }

                foreach (ModelId id in playerStat.BoughtRelics)
                {
                    _boughtRelicFromShop.Attempted(id);
                    _boughtRelicFromShop.Succeeded(id);
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

            RecordCardWinData(runHistory, player);
            RecordRelicWinData(runHistory, player);

            RecordAncientPickData(runHistory, player.Id);
            
            RecordCardRewardPickData(runHistory, player.Id);
            
            RecordShopCardPurchaseData(runHistory, player.Id);
            RecordShopRelicPurchaseData(runHistory, player.Id);
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

    public float GetCardWinPercentage(ModelId id)
    {
        return _wonWithCard.SuccessRate(id);
    }
    
    public float GetRelicWinPercentage(ModelId id)
    {
        return _wonWithRelic.SuccessRate(id);
    }
    
    public float GetAncientPickPercentage(LocString title)
    {
        string key = title.LocEntryKey;
        
        return _pickedAncientRelic.SuccessRate(key);
    }

    public float GetCardRewardPickPercentage(ModelId id)
    {
        return _pickedFromCardReward.SuccessRate(id);
    }
    
    public float GetCardBuyPercentage(ModelId id)
    {
        return _boughtCardFromShop.SuccessRate(id);
    }
    
    public float GetRelicPurchasePercentage(ModelId id)
    {
        return _boughtRelicFromShop.SuccessRate(id);
    }
}