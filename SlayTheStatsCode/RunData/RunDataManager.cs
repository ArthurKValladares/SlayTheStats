using System;
using System.Diagnostics;
using System.Linq;
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

public class PotionLifecycleTracker
{
    // Non-shop (reward) offer/pick
    private readonly Dictionary<ModelId, int> _timesOfferedReward = new();
    private readonly Dictionary<ModelId, int> _timesPickedReward  = new();

    // Shop offer/buy
    private readonly Dictionary<ModelId, int> _timesOfferedShop   = new();
    private readonly Dictionary<ModelId, int> _timesPickedShop    = new();

    private readonly Dictionary<ModelId, int> _timesUsed      = new();
    private readonly Dictionary<ModelId, int> _timesDiscarded = new();

    public void RecordOfferedReward(ModelId id) => Increment(_timesOfferedReward, id);
    public void RecordPickedReward(ModelId id)  => Increment(_timesPickedReward, id);
    public void RecordOfferedShop(ModelId id)   => Increment(_timesOfferedShop, id);
    public void RecordPickedShop(ModelId id)    => Increment(_timesPickedShop, id);
    public void RecordUsed(ModelId id)          => Increment(_timesUsed, id);
    public void RecordDiscarded(ModelId id)     => Increment(_timesDiscarded, id);

    // How often this potion is taken when offered as a reward
    public float RewardPickRate(ModelId id)
    {
        int offered = _timesOfferedReward.GetValueOrDefault(id);
        return offered == 0 ? 0f : _timesPickedReward.GetValueOrDefault(id) / (float)offered;
    }

    // How often this potion is bought when available in the shop
    public float ShopBuyRate(ModelId id)
    {
        int offered = _timesOfferedShop.GetValueOrDefault(id);
        return offered == 0 ? 0f : _timesPickedShop.GetValueOrDefault(id) / (float)offered;
    }

    // How often this potion is actually used after being picked up (any source)
    public float UseRate(ModelId id)
    {
        int picked = _timesPickedReward.GetValueOrDefault(id) + _timesPickedShop.GetValueOrDefault(id);
        return picked == 0 ? 0f : _timesUsed.GetValueOrDefault(id) / (float)picked;
    }

    // How often this potion is thrown away after being picked up (any source)
    public float DiscardRate(ModelId id)
    {
        int picked = _timesPickedReward.GetValueOrDefault(id) + _timesPickedShop.GetValueOrDefault(id);
        return picked == 0 ? 0f : _timesDiscarded.GetValueOrDefault(id) / (float)picked;
    }

    public void Merge(PotionLifecycleTracker other)
    {
        MergeDicts(_timesOfferedReward, other._timesOfferedReward);
        MergeDicts(_timesPickedReward,  other._timesPickedReward);
        MergeDicts(_timesOfferedShop,   other._timesOfferedShop);
        MergeDicts(_timesPickedShop,    other._timesPickedShop);
        MergeDicts(_timesUsed,      other._timesUsed);
        MergeDicts(_timesDiscarded, other._timesDiscarded);
    }

    private static void Increment(Dictionary<ModelId, int> dict, ModelId key)
    {
        dict.TryGetValue(key, out int count);
        dict[key] = count + 1;
    }

    private static void MergeDicts(Dictionary<ModelId, int> target, Dictionary<ModelId, int> source)
    {
        foreach (var (key, count) in source)
        {
            target.TryGetValue(key, out int existing);
            target[key] = existing + count;
        }
    }
}

public class CardLifecycleTracker
{
    private readonly Dictionary<ModelId, int> _timesInDeck    = new();
    private readonly Dictionary<ModelId, int> _timesRemoved   = new();
    private readonly Dictionary<ModelId, int> _timesUpgraded  = new();

    // Per-removal/upgrade: "how many removes/smiths had happened since obtaining the card"
    private readonly Dictionary<ModelId, List<int>> _removePriorityDeltas  = new();
    private readonly Dictionary<ModelId, List<int>> _upgradePriorityDeltas = new();

    public void RecordInDeck(ModelId id) => Increment(_timesInDeck, id);

    public void RecordRemoved(ModelId id, int priorityDelta)
    {
        Increment(_timesRemoved, id);
        PushDelta(_removePriorityDeltas, id, priorityDelta);
    }

    public void RecordUpgraded(ModelId id, int priorityDelta)
    {
        Increment(_timesUpgraded, id);
        PushDelta(_upgradePriorityDeltas, id, priorityDelta);
    }

    // Fraction of deck copies that were removed
    public float RemovalRate(ModelId id)
    {
        int inDeck = _timesInDeck.GetValueOrDefault(id);
        return inDeck == 0 ? 0f : _timesRemoved.GetValueOrDefault(id) / (float)inDeck;
    }

    // Fraction of deck copies that were upgraded at a rest site
    public float UpgradeRate(ModelId id)
    {
        int inDeck = _timesInDeck.GetValueOrDefault(id);
        return inDeck == 0 ? 0f : _timesUpgraded.GetValueOrDefault(id) / (float)inDeck;
    }

    // Average removes-since-obtained before this card was removed (lower = higher priority)
    public float? AvgRemovePriority(ModelId id)
    {
        if (!_removePriorityDeltas.TryGetValue(id, out var list) || list.Count == 0) return null;
        return (float)list.Average();
    }

    // Average smiths-since-obtained before this card was upgraded (lower = higher priority)
    public float? AvgUpgradePriority(ModelId id)
    {
        if (!_upgradePriorityDeltas.TryGetValue(id, out var list) || list.Count == 0) return null;
        return (float)list.Average();
    }

    public void Merge(CardLifecycleTracker other)
    {
        MergeDicts(_timesInDeck,   other._timesInDeck);
        MergeDicts(_timesRemoved,  other._timesRemoved);
        MergeDicts(_timesUpgraded, other._timesUpgraded);
        MergeDeltas(_removePriorityDeltas,  other._removePriorityDeltas);
        MergeDeltas(_upgradePriorityDeltas, other._upgradePriorityDeltas);
    }

    private static void Increment(Dictionary<ModelId, int> dict, ModelId key)
    {
        dict.TryGetValue(key, out int count);
        dict[key] = count + 1;
    }

    private static void PushDelta(Dictionary<ModelId, List<int>> dict, ModelId key, int delta)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<int>();
            dict[key] = list;
        }
        list.Add(delta);
    }

    private static void MergeDicts(Dictionary<ModelId, int> target, Dictionary<ModelId, int> source)
    {
        foreach (var (key, count) in source)
        {
            target.TryGetValue(key, out int existing);
            target[key] = existing + count;
        }
    }

    private static void MergeDeltas(Dictionary<ModelId, List<int>> target, Dictionary<ModelId, List<int>> source)
    {
        foreach (var (key, deltas) in source)
        {
            if (!target.TryGetValue(key, out var list))
            {
                list = new List<int>();
                target[key] = list;
            }
            list.AddRange(deltas);
        }
    }
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
    
    private readonly PotionLifecycleTracker _potionLifecycle = new();
    private readonly CardLifecycleTracker _cardLifecycle = new();
    
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
        foreach (var (entry, playerStat) in IterateMapHistory(runHistory, playerId, e => e.MapPointType is MapPointType.Monster or MapPointType.Elite or MapPointType.Boss))
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
    
    private void RecordCardLifecycleData(RunHistory runHistory, ulong playerId)
    {
        RunHistoryPlayer? player = runHistory.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null) return;

        // Count every copy that existed this run: final deck + anything removed mid-run.
        // We count directly (not via a floor-keyed dict) so multiple starter cards sharing
        // FloorAddedToDeck = 1 are each counted individually.
        foreach (SerializableCard card in player.Deck)
            if (card.Id != null)
                _cardLifecycle.RecordInDeck(card.Id);

        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId))
            foreach (SerializableCard removed in playerStat.CardsRemoved)
                if (removed.Id != null)
                    _cardLifecycle.RecordInDeck(removed.Id);

        // Separate floor->id map used only for priority delta tracking.
        // Collision on floor 1 is fine here: all starter copies were obtained at the same time,
        // so obtainedAtRemoval[1] = 0 regardless of which copy we track.
        var copyFloorToId = new Dictionary<int, ModelId>();
        foreach (SerializableCard card in player.Deck)
            if (card.Id != null && card.FloorAddedToDeck.HasValue)
                copyFloorToId[card.FloorAddedToDeck.Value] = card.Id;
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId))
            foreach (SerializableCard removed in playerStat.CardsRemoved)
                if (removed.Id != null && removed.FloorAddedToDeck.HasValue)
                    copyFloorToId[removed.FloorAddedToDeck.Value] = removed.Id;

        // Walk floors in order to build priority deltas.
        // - obtainedAtRemoval: floorAddedToDeck -> removalCount when that copy was gained
        // - firstObtainedAtSmith: cardId -> smithCount when first copy was gained
        // Starter cards (not seen in CardsGained) default to obtained-at-0 for both.
        var obtainedAtRemoval  = new Dictionary<int, int>();
        var firstObtainedAtSmith = new Dictionary<ModelId, int>();
        int removalCount = 0;
        int smithCount   = 0;

        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId))
        {
            // Register newly gained cards before processing removals/upgrades on the same floor.
            foreach (SerializableCard gained in playerStat.CardsGained)
            {
                if (gained.Id == null) continue;
                if (gained.FloorAddedToDeck.HasValue)
                    obtainedAtRemoval.TryAdd(gained.FloorAddedToDeck.Value, removalCount);
                firstObtainedAtSmith.TryAdd(gained.Id, smithCount);
            }

            foreach (SerializableCard removed in playerStat.CardsRemoved)
            {
                if (removed.Id == null || !removed.FloorAddedToDeck.HasValue) continue;
                int obtainedAt = obtainedAtRemoval.GetValueOrDefault(removed.FloorAddedToDeck.Value, 0);
                _cardLifecycle.RecordRemoved(removed.Id, removalCount - obtainedAt);
                removalCount++;
            }

            foreach (ModelId upgradedId in playerStat.UpgradedCards)
            {
                int obtainedAt = firstObtainedAtSmith.GetValueOrDefault(upgradedId, 0);
                _cardLifecycle.RecordUpgraded(upgradedId, smithCount - obtainedAt);
                smithCount++;
            }
        }
    }

    private void RecordPotionLifecycleData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (entry, playerStat) in IterateMapHistory(runHistory, playerId))
        {
            bool isShop = entry.MapPointType == MapPointType.Shop;

            foreach (ModelChoiceHistoryEntry potionChoice in playerStat.PotionChoices)
            {
                if (isShop)
                {
                    _potionLifecycle.RecordOfferedShop(potionChoice.choice);
                    if (potionChoice.wasPicked)
                        _potionLifecycle.RecordPickedShop(potionChoice.choice);
                }
                else
                {
                    _potionLifecycle.RecordOfferedReward(potionChoice.choice);
                    if (potionChoice.wasPicked)
                        _potionLifecycle.RecordPickedReward(potionChoice.choice);
                }
            }

            foreach (ModelId potionId in playerStat.PotionUsed)
            {
                _potionLifecycle.RecordUsed(potionId);
            }

            foreach (ModelId potionId in playerStat.PotionDiscarded)
            {
                _potionLifecycle.RecordDiscarded(potionId);
            }
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

            RecordPotionLifecycleData(runHistory, player.Id);
            RecordCardLifecycleData(runHistory, player.Id);
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

        // TODO: Custom structure for _monsterEncounters with its own merge function
        foreach (var (key, encounters) in other._monsterEncounters)
        {
            if (!_monsterEncounters.TryGetValue(key, out var existing))
            {
                existing = [];
                _monsterEncounters[key] = existing;
            }
            existing.AddRange(encounters);
        }
        
        _potionLifecycle.Merge(other._potionLifecycle);
        _cardLifecycle.Merge(other._cardLifecycle);
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

    public float GetCardWinPercentage(ModelId id) => _wonWithCard.SuccessRate(id);
    public float GetRelicWinPercentage(ModelId id) => _wonWithRelic.SuccessRate(id);
    public float GetAncientPickPercentage(LocString title) => _pickedAncientRelic.SuccessRate(title.LocEntryKey);
    public float GetCardRewardPickPercentage(ModelId id) => _pickedFromCardReward.SuccessRate(id);
    public float GetCardBuyPercentage(ModelId id) => _boughtCardFromShop.SuccessRate(id);
    public float GetRelicPurchasePercentage(ModelId id)=> _boughtRelicFromShop.SuccessRate(id);
    public float GetPotionRewardPickRate(ModelId id) => _potionLifecycle.RewardPickRate(id);
    public float GetPotionShopBuyRate(ModelId id)    => _potionLifecycle.ShopBuyRate(id);
    public float GetPotionUseRate(ModelId id)        => _potionLifecycle.UseRate(id);
    public float GetPotionDiscardRate(ModelId id)    => _potionLifecycle.DiscardRate(id);

    public float  GetCardRemovalRate(ModelId id)         => _cardLifecycle.RemovalRate(id);
    public float  GetCardUpgradeRate(ModelId id)         => _cardLifecycle.UpgradeRate(id);
    public float? GetCardAvgRemovePriority(ModelId id)   => _cardLifecycle.AvgRemovePriority(id);
    public float? GetCardAvgUpgradePriority(ModelId id)  => _cardLifecycle.AvgUpgradePriority(id);

    public MonsterEncounterData? GetEncounterAverages(ModelId roomId, List<ModelId> monsterIds)
    {
        var key = (roomId, monsterIds);
        if (!_monsterEncounters.TryGetValue(key, out var entries) || entries.Count == 0)
            return null;

        float n = entries.Count;
        return new MonsterEncounterData
        {
            TurnsTaken  = (int)Math.Round(entries.Average(e => e.TurnsTaken)),
            DamageTaken = (int)Math.Round(entries.Average(e => e.DamageTaken)),
            GoldGained  = (int)Math.Round(entries.Average(e => e.GoldGained)),
            GoldStolen  = (int)Math.Round(entries.Average(e => e.GoldStolen)),
            MaxHpLost   = (int)Math.Round(entries.Average(e => e.MaxHpLost)),
            HpHealed    = (int)Math.Round(entries.Average(e => e.HpHealed)),
            MaxHpGained = (int)Math.Round(entries.Average(e => e.MaxHpGained)),
        };
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