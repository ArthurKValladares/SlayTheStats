using System.Diagnostics;
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
    
    public void Merge(SuccessRateTracker<TK> other)
    {
        foreach (var (key, count) in other._numAttempt)
        {
            _numAttempt.TryGetValue(key, out int existing);
            _numAttempt[key] = existing + count;
        }
        foreach (var (key, count) in other._numSuccess)
        {
            _numSuccess.TryGetValue(key, out int existing);
            _numSuccess[key] = existing + count;
        }
    }
    
    private readonly Dictionary<TK, int> _numAttempt = new();
    private readonly Dictionary<TK, int> _numSuccess = new();   
}

 public class MonsterEncounterData
 {
     public int TurnsTaken { get; init; }
     public int DamageTaken { get; init; }
     public int GoldGained { get; init; }
     public int GoldStolen { get; init; }
     public int MaxHpLost { get; init; }
     public int HpHealed { get; init; }
     public int MaxHpGained { get; init; }
 }

public class RunDataManager
{
    private static readonly Lazy<RunDataManager> Lazy = new(ConstructDefault);
    
    private readonly SuccessRateTracker<ModelId> _wonWithCard = new();
    private readonly SuccessRateTracker<ModelId> _wonWithRelic = new();
    private readonly SuccessRateTracker<string> _pickedAncientRelic = new();
    private readonly SuccessRateTracker<ModelId> _pickedFromCardReward = new();
    private readonly SuccessRateTracker<ModelId> _boughtCardFromShop = new();
    private readonly SuccessRateTracker<ModelId> _boughtRelicFromShop = new();
    
    private readonly Dictionary<(ModelId, List<ModelId>), List<MonsterEncounterData>> _monsterEncounters = new(EncounterKeyComparer.Instance);
    
    private static RunDataManager ConstructDefault()
    {
        RunDataManager runDataManager = new RunDataManager();
        return runDataManager;
    }

    public static RunDataManager Instance => Lazy.Value;

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
        IEnumerable<SerializableCard> deck = player.Deck;
        
        HashSet<ModelId> alreadySeenCards = new();
        foreach (SerializableCard card in deck)
        {
            ModelId? cardId = card.Id;
            if (cardId == null || cardId == ModelId.none || !alreadySeenCards.Add(cardId))
                continue;
            
            _wonWithCard.Record(cardId, runHistory.Win);
        }   
    }
    
    private void RecordRelicWinData(RunHistory runHistory, RunHistoryPlayer player)
    {
        IEnumerable<SerializableRelic> relics = player.Relics;
        
        HashSet<ModelId> alreadySeenRelics = new();
        foreach (SerializableRelic relic in relics)
        {
            ModelId? relicId = relic.Id;
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
            // We are not double-counting here, cards bough do not appear in CardChoices, only in CardGained
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
            // We are not double-counting here, relics bough do not appear in RelicChoices, only in BoughtRelics
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

    private void RecordMonsterEncounterData(RunHistory runHistory, ulong playerId)
    {
        // TODO: The filter should maybe be for monsters, elites, and bosses?
        foreach (var (entry, playerStat) in IterateMapHistory(runHistory, playerId, e => e.MapPointType == MapPointType.Monster))
        {
            if (entry.Rooms.Count == 0) continue;
            
            // NOTE: hard-coding this to always use the first room make the code a lot simpler,
            // and for now every map point entry always has a single room so the behavior is always still correct 
            MapPointRoomHistoryEntry room = entry.Rooms[0];
            
            var encounterData = new MonsterEncounterData
            {
                TurnsTaken  = room.TurnsTaken,
                DamageTaken = playerStat.DamageTaken,
                GoldGained  = playerStat.GoldGained,
                GoldStolen  = playerStat.GoldStolen,
                MaxHpLost   = playerStat.MaxHpLost,
                HpHealed    = playerStat.HpHealed,
                MaxHpGained = playerStat.MaxHpGained
            };
            
            ModelId? roomId = room.ModelId;
            if(roomId == null) continue;
            List<ModelId> monstersId = room.MonsterIds;
            
            (ModelId, List<ModelId>) key = (roomId, monstersId.ToList());
            
            if (!_monsterEncounters.TryGetValue(key, out var values))
            {
                values = [];
                _monsterEncounters[key] = values;
            }
            values.Add(encounterData);
        }    
    }
    
    public void AddRunToHistory(RunHistory runHistory, ulong localPlayerId)
    {
        foreach (var player in runHistory.Players)
        {
            if (player.Id != LocalConstants.DefaultPlayerId && player.Id != localPlayerId)
                continue;
            
            string playerName = PlatformUtil.GetPlayerName(PlatformType.Steam, player.Id);
            Log.Info($"Adding run {runHistory.StartTime} to history for player {playerName} id: {player.Id}");

            RecordCardWinData(runHistory, player);
            RecordRelicWinData(runHistory, player);

            RecordAncientPickData(runHistory, player.Id);
            
            RecordCardRewardPickData(runHistory, player.Id);
            
            RecordShopCardPurchaseData(runHistory, player.Id);
            RecordShopRelicPurchaseData(runHistory, player.Id);
            
            RecordMonsterEncounterData(runHistory, player.Id);
        }
    }
    
    private void Merge(RunDataManager other)
    {
        _wonWithCard.Merge(other._wonWithCard);
        _wonWithRelic.Merge(other._wonWithRelic);
        _pickedAncientRelic.Merge(other._pickedAncientRelic);
        _pickedFromCardReward.Merge(other._pickedFromCardReward);
        _boughtCardFromShop.Merge(other._boughtCardFromShop);
        _boughtRelicFromShop.Merge(other._boughtRelicFromShop);

        foreach (var (key, encounters) in other._monsterEncounters)
        {
            if (!_monsterEncounters.TryGetValue(key, out var existing))
            {
                existing = [];
                _monsterEncounters[key] = existing;
            }
            existing.AddRange(encounters);
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
            Log.Info($"Found {runHistoryFileNames.Count} run history files");
        }
        catch (Exception ex)
        {
            Log.Error($"Error getting run history files: {ex.Message}");
            return;
        }
        
        int loaded = 0;
        object mergeLock = new object();

        ulong localPlayerId = PlatformUtil.GetLocalPlayerId(PlatformType.Steam);
        Parallel.ForEach(
            runHistoryFileNames,
            // each thread gets its own local instance
            () => new RunDataManager(),
            (name, _, localManager) =>
            {
                ReadSaveResult<RunHistory> result = SaveManager.Instance.LoadRunHistory(name);
                if (result.Success && result.SaveData != null)
                {
                    localManager.AddRunToHistory(result.SaveData, localPlayerId);
                    Interlocked.Increment(ref loaded);
                }
                return localManager;
            },
            // called once per thread when it finishes
            localManager =>
            {
                lock (mergeLock)
                {
                    Merge(localManager);
                }
            }
        );
        
        TimeSpan ts = stopwatch.Elapsed;
        Log.Info($"Loaded {loaded} run history files in {ts.TotalMilliseconds}ms - {ts.Minutes:00}:{ts.Seconds:00}:{ts.Milliseconds:00}");
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
    
    private sealed class EncounterKeyComparer : IEqualityComparer<(ModelId, List<ModelId>)>
    {
        public static readonly EncounterKeyComparer Instance = new();

        public bool Equals((ModelId, List<ModelId>) x, (ModelId, List<ModelId>) y)
        {
            return x.Item1 == y.Item1 && x.Item2.SequenceEqual(y.Item2);
        }

        public int GetHashCode((ModelId, List<ModelId>) obj)
        {
            HashCode hash = new HashCode();
            hash.Add(obj.Item1);
            foreach (ModelId id in obj.Item2)
                hash.Add(id);
            return hash.ToHashCode();
        }
    }
}