using System.Diagnostics;
using System.Reflection.PortableExecutable;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SlayTheStats.SlayTheStatsCode.RunData;

file static class LocalConstants
{
    public const int DefaultPlayerId = 1;    
}

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

    public void Record(TK key, bool success)
    {
        Attempted(key);
        if (success)
        {
            Succeeded(key);
        }
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

 public class MonsterEncounterData
 {
     public int turnsTaken { get; init; }
     public int damageTaken { get; init; }
     public int goldGained { get; init; }
     public int goldStolen { get; init; }
     public int maxHpLost { get; init; }
     public int hpHealed { get; init; }
     public int maxHpGained { get; init; }
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
    
    private readonly Dictionary<(ModelId, List<ModelId>), List<MonsterEncounterData>> _monsterEncounters = new();
    
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

    private static IEnumerable<(MapPointHistoryEntry entry, PlayerMapPointHistoryEntry playerStat)> IterateMapHistory(
        RunHistory runHistory,
        ulong playerId,
        Func<MapPointHistoryEntry, bool>? filter = null)
    {
        foreach (List<MapPointHistoryEntry> actHistory in runHistory.MapPointHistory)
        {
            foreach (MapPointHistoryEntry entry in actHistory)
            {
                if (filter != null && !filter(entry)) continue;

                PlayerMapPointHistoryEntry? playerStat = entry.PlayerStats.Find(ps => ps.PlayerId == playerId);
                if (playerStat == null) continue;

                yield return (entry, playerStat);
            }
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
            
            _wonWithCard.Record(cardId, runHistory.Win);
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
            
            _wonWithRelic.Record(relicId, runHistory.Win);
        }   
    }
    
    private void RecordAncientPickData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId, 
                     e => e.MapPointType == MapPointType.Ancient))
        {
            foreach (AncientChoiceHistoryEntry ancientChoice in playerStat.AncientChoices)
            {
                _pickedAncientRelic.Record(ancientChoice.Title.LocEntryKey, ancientChoice.WasChosen);
            }
        }
    }
    
    private void RecordCardRewardPickData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId,
                     e => e.MapPointType != MapPointType.Shop))
        {
            foreach (CardChoiceHistoryEntry cardChoice in playerStat.CardChoices)
            {
                ModelId? id = cardChoice.Card.Id;
                if (id == null) continue;

                _pickedFromCardReward.Record(id, cardChoice.wasPicked);
            }   
        }
    }
    
    public void RecordShopCardPurchaseData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId, 
                     e => e.MapPointType == MapPointType.Shop))
        {
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
    
    public void RecordShopRelicPurchaseData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId, e => e.MapPointType == MapPointType.Shop))
        {
            foreach (ModelChoiceHistoryEntry relicOffered in playerStat.RelicChoices)
            {
                _boughtRelicFromShop.Attempted(relicOffered.choice);
            }

            foreach (ModelId id in playerStat.BoughtRelics)
            {
                _boughtRelicFromShop.Attempted(id);
                _boughtRelicFromShop.Succeeded(id);
            }
        }
    }

    private void RecordMonsterEncouterData(RunHistory runHistory, ulong playerId)
    {
        // TODO: The filter should maybe be for monsters, elites, and bosses?
        foreach (var (entry, playerStat) in IterateMapHistory(runHistory, playerId, e => e.MapPointType == MapPointType.Monster))
        {
            if (entry.Rooms.Count == 0) continue;
            
            // NOTE: hard-coding this to always use the first room make the code a lot simpler,
            // and for now every map point entry always has a single room so the behavior is always still correct 
            MapPointRoomHistoryEntry room = entry.Rooms[0];
            
            int turnsTaken = room.TurnsTaken;
            
            int damageTaken = playerStat.DamageTaken;
            int goldGained =  playerStat.GoldGained;
            int goldStolen =  playerStat.GoldStolen;
            int maxHpLost = playerStat.MaxHpLost;
            int hpHealed = playerStat.HpHealed;
            int maxHpGained = playerStat.MaxHpGained;
            
            var encounterData = new MonsterEncounterData
            {
                turnsTaken  = turnsTaken,
                damageTaken = damageTaken,
                goldGained  = goldGained,
                goldStolen  = goldStolen,
                maxHpLost   = maxHpLost,
                hpHealed    = hpHealed,
                maxHpGained = maxHpGained
            };
            
            ModelId? roomId = room.ModelId;
            if(roomId == null) continue;
            List<ModelId> monstersId = room.MonsterIds;
            
            (ModelId, List<ModelId>) key = (roomId, monstersId);
            
            if (!_monsterEncounters.TryGetValue(key, out var values))
            {
                values = [];
                _monsterEncounters[key] = values;
            }
            values.Add(encounterData);
        }    
    }
    
    public void AddRunToHistory(RunHistory runHistory)
    {
        foreach (var player in runHistory.Players)
        {
            if (player.Id != LocalConstants.DefaultPlayerId && player.Id != PlatformUtil.GetLocalPlayerId(PlatformType.Steam))
                continue;
            
            string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, player.Id);
            Log.Info($"Adding run {runHistory.StartTime} to history for player {playerName} id: {player.Id}");

            RecordCardWinData(runHistory, player);
            RecordRelicWinData(runHistory, player);

            RecordAncientPickData(runHistory, player.Id);
            
            RecordCardRewardPickData(runHistory, player.Id);
            
            RecordShopCardPurchaseData(runHistory, player.Id);
            RecordShopRelicPurchaseData(runHistory, player.Id);
            
            RecordMonsterEncouterData(runHistory, player.Id);
        }
    }
    
    public void LoadAllRuns()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        
        SaveManager saveManager = SaveManager.Instance;
        List<string> runHistoryFileNames;

        try
        {
            runHistoryFileNames = saveManager.GetAllRunHistoryNames();
            Log.Info($"Loaded {runHistoryFileNames.Count} run history files");
        }
        catch (Exception ex)
        {
            Log.Error($"Error loading run history files: {ex.Message}");
            return;
        }
        
        foreach (String name in runHistoryFileNames)
        {
            ReadSaveResult<RunHistory> readSaveResult = SaveManager.Instance.LoadRunHistory(name);
            
            if (!readSaveResult.Success || readSaveResult.SaveData == null)
                continue;
            
            AddRunToHistory(readSaveResult.SaveData);
        }
        
        TimeSpan ts = stopwatch.Elapsed;
        Log.Info($"Loaded {runHistoryFileNames.Count} run history files in {ts.TotalMilliseconds}ms - {ts.Minutes:00}:{ts.Seconds:00}:{ts.Milliseconds:00}");
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