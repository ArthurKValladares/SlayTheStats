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

/// <summary>Uniquely identifies a card variant: base, upgraded, enchanted, or upgraded+enchanted.</summary>
public readonly record struct CardVariantKey(ModelId CardId, int UpgradeLevel, ModelId? EnchantmentId);

public class CardLifecycleTracker
{
    // Keyed by variant — removal stats can distinguish base vs upgraded vs enchanted copies
    private readonly Dictionary<CardVariantKey, int> _timesInDeck  = new();
    private readonly Dictionary<CardVariantKey, int> _timesRemoved = new();
    private readonly Dictionary<CardVariantKey, List<int>> _removePriorityDeltas = new();

    // Floor tracking — only counts entries where FloorAddedToDeck was present
    private readonly Dictionary<CardVariantKey, int> _floorAddedSums   = new();
    private readonly Dictionary<CardVariantKey, int> _floorAddedCounts = new();

    // Deck size at time of picking
    private readonly Dictionary<CardVariantKey, int> _deckSizeAtPickSums   = new();
    private readonly Dictionary<CardVariantKey, int> _deckSizeAtPickCounts = new();

    // UpgradedCards only carries ModelId, so upgrade stats stay at card-id granularity
    private readonly Dictionary<ModelId, int> _timesUpgraded = new();
    private readonly Dictionary<ModelId, List<int>> _upgradePriorityDeltas = new();

    public void RecordInDeck(CardVariantKey key, int? floor = null)
    {
        IncrementV(_timesInDeck, key);
        if (floor.HasValue)
        {
            _floorAddedSums.TryGetValue(key, out int sum);
            _floorAddedSums[key] = sum + floor.Value;
            IncrementV(_floorAddedCounts, key);
        }
    }

    public void RecordRemoved(CardVariantKey key, int priorityDelta)
    {
        IncrementV(_timesRemoved, key);
        PushDeltaV(_removePriorityDeltas, key, priorityDelta);
    }

    public void RecordUpgraded(ModelId id, int priorityDelta)
    {
        Increment(_timesUpgraded, id);
        PushDelta(_upgradePriorityDeltas, id, priorityDelta);
    }

    // Fraction of copies of this variant that were removed
    public float RemovalRate(CardVariantKey key)
    {
        int inDeck = _timesInDeck.GetValueOrDefault(key);
        return inDeck == 0 ? 0f : _timesRemoved.GetValueOrDefault(key) / (float)inDeck;
    }

    public float? AvgFloorAdded(CardVariantKey key)
    {
        if (!_floorAddedCounts.TryGetValue(key, out int count) || count == 0) return null;
        return _floorAddedSums[key] / (float)count;
    }

    public void RecordPickedAtDeckSize(CardVariantKey key, int deckSize)
    {
        _deckSizeAtPickSums.TryGetValue(key, out int sum);
        _deckSizeAtPickSums[key] = sum + deckSize;
        IncrementV(_deckSizeAtPickCounts, key);
    }

    public float? AvgDeckSizeAtPick(CardVariantKey key)
    {
        if (!_deckSizeAtPickCounts.TryGetValue(key, out int count) || count == 0) return null;
        return _deckSizeAtPickSums[key] / (float)count;
    }

    // Total copies of this card (any variant) seen across all runs
    public int TotalInDeck(ModelId id) =>
        _timesInDeck.Where(kvp => kvp.Key.CardId == id).Sum(kvp => kvp.Value);

    // Fraction of all copies (any variant) of this card that were upgraded at a rest site
    public float UpgradeRate(ModelId id)
    {
        int inDeck = _timesInDeck.Where(kvp => kvp.Key.CardId == id).Sum(kvp => kvp.Value);
        return inDeck == 0 ? 0f : _timesUpgraded.GetValueOrDefault(id) / (float)inDeck;
    }

    public float? AvgRemovePriority(CardVariantKey key)
    {
        if (!_removePriorityDeltas.TryGetValue(key, out var list) || list.Count == 0) return null;
        return (float)list.Average();
    }

    public float? AvgUpgradePriority(ModelId id)
    {
        if (!_upgradePriorityDeltas.TryGetValue(id, out var list) || list.Count == 0) return null;
        return (float)list.Average();
    }

    public void Merge(CardLifecycleTracker other)
    {
        MergeDictsV(_timesInDeck,          other._timesInDeck);
        MergeDictsV(_floorAddedSums,       other._floorAddedSums);
        MergeDictsV(_floorAddedCounts,     other._floorAddedCounts);
        MergeDictsV(_deckSizeAtPickSums,   other._deckSizeAtPickSums);
        MergeDictsV(_deckSizeAtPickCounts, other._deckSizeAtPickCounts);
        MergeDictsV(_timesRemoved, other._timesRemoved);
        MergeDeltasV(_removePriorityDeltas, other._removePriorityDeltas);
        MergeDicts(_timesUpgraded, other._timesUpgraded);
        MergeDeltas(_upgradePriorityDeltas, other._upgradePriorityDeltas);
    }

    private static void IncrementV(Dictionary<CardVariantKey, int> dict, CardVariantKey key)
    {
        dict.TryGetValue(key, out int count);
        dict[key] = count + 1;
    }

    private static void Increment(Dictionary<ModelId, int> dict, ModelId key)
    {
        dict.TryGetValue(key, out int count);
        dict[key] = count + 1;
    }

    private static void PushDeltaV(Dictionary<CardVariantKey, List<int>> dict, CardVariantKey key, int delta)
    {
        if (!dict.TryGetValue(key, out var list)) { list = new(); dict[key] = list; }
        list.Add(delta);
    }

    private static void PushDelta(Dictionary<ModelId, List<int>> dict, ModelId key, int delta)
    {
        if (!dict.TryGetValue(key, out var list)) { list = new(); dict[key] = list; }
        list.Add(delta);
    }

    private static void MergeDictsV(Dictionary<CardVariantKey, int> target, Dictionary<CardVariantKey, int> source)
    {
        foreach (var (key, count) in source)
        {
            target.TryGetValue(key, out int existing);
            target[key] = existing + count;
        }
    }

    private static void MergeDicts(Dictionary<ModelId, int> target, Dictionary<ModelId, int> source)
    {
        foreach (var (key, count) in source)
        {
            target.TryGetValue(key, out int existing);
            target[key] = existing + count;
        }
    }

    private static void MergeDeltasV(Dictionary<CardVariantKey, List<int>> target, Dictionary<CardVariantKey, List<int>> source)
    {
        foreach (var (key, deltas) in source)
        {
            if (!target.TryGetValue(key, out var list)) { list = new(); target[key] = list; }
            list.AddRange(deltas);
        }
    }

    private static void MergeDeltas(Dictionary<ModelId, List<int>> target, Dictionary<ModelId, List<int>> source)
    {
        foreach (var (key, deltas) in source)
        {
            if (!target.TryGetValue(key, out var list)) { list = new(); target[key] = list; }
            list.AddRange(deltas);
        }
    }
}

 public class MonsterEncounterData
 {
     public int EntryHp { get; init; }
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
    
    private readonly SuccessRateTracker<CardVariantKey> _wonWithCard = new();
    private readonly SuccessRateTracker<ModelId> _wonWithRelic = new();
    private readonly SuccessRateTracker<string> _pickedAncientRelic = new();
    private readonly SuccessRateTracker<CardVariantKey> _pickedFromCardReward = new();
    private readonly SuccessRateTracker<CardVariantKey> _boughtCardFromShop = new();
    // enchantmentId → count, nested under cardId
    private readonly Dictionary<ModelId, Dictionary<ModelId, int>> _enchantmentCounts = new();
    private readonly SuccessRateTracker<ModelId> _boughtRelicFromShop = new();
    
    private readonly Dictionary<string, int> _restSiteChoiceCounts  = new();
    private readonly Dictionary<string, int> _restSiteChoiceHpSums  = new(); // sum of current hp at time of choice
    private int _restSiteVisits;
    private readonly PotionLifecycleTracker _potionLifecycle = new();
    private readonly CardLifecycleTracker _cardLifecycle = new();
    
    private readonly Dictionary<ModelId, int> _relicFloorSums   = new();
    private readonly Dictionary<ModelId, int> _relicFloorCounts = new();

    private readonly Dictionary<(ModelId, List<ModelId>), List<MonsterEncounterData>> _monsterEncounters = new(EncounterKeyComparer.Instance);
    private readonly Dictionary<(ModelId, List<ModelId>), int> _encounterSeen  = new(EncounterKeyComparer.Instance);
    private readonly Dictionary<(ModelId, List<ModelId>), int> _encounterKills = new(EncounterKeyComparer.Instance);
    
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
        // Record once per unique variant (base/upgraded/enchanted/upgraded+enchanted)
        HashSet<CardVariantKey> seen = new();
        foreach (SerializableCard card in player.Deck)
        {
            if (card.Id == null || card.Id == ModelId.none) continue;
            var key = VariantKey(card);
            if (seen.Add(key))
                _wonWithCard.Record(key, runHistory.Win);
        }
    }
    
    private void RecordRelicWinData(RunHistory runHistory, RunHistoryPlayer player)
    {
        HashSet<ModelId> alreadySeenRelics = new();
        foreach (SerializableRelic relic in player.Relics)
        {
            ModelId? relicId = relic.Id;
            if (relicId == null || relicId == ModelId.none || !alreadySeenRelics.Add(relicId))
                continue;

            _wonWithRelic.Record(relicId, runHistory.Win);

            if (relic.FloorAddedToDeck.HasValue)
            {
                _relicFloorSums.TryGetValue(relicId, out int sum);
                _relicFloorSums[relicId] = sum + relic.FloorAddedToDeck.Value;
                Increment(_relicFloorCounts, relicId);
            }
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
                if (cardChoice.Card.Id == null) continue;
                _pickedFromCardReward.Record(VariantKey(cardChoice.Card), cardChoice.wasPicked);
            }
        }
    }

    public void RecordShopCardPurchaseData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId,
                     e => e.MapPointType == MapPointType.Shop))
        {
            // Cards bought do not appear in CardChoices, only in CardsGained
            foreach (CardChoiceHistoryEntry cardOffered in playerStat.CardChoices)
            {
                if (cardOffered.Card.Id == null) continue;
                _boughtCardFromShop.Attempted(VariantKey(cardOffered.Card));
            }

            foreach (SerializableCard cardBought in playerStat.CardsGained)
            {
                if (cardBought.Id == null) continue;
                var key = VariantKey(cardBought);
                _boughtCardFromShop.Attempted(key);
                _boughtCardFromShop.Succeeded(key);
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
                EntryHp     = playerStat.CurrentHp + playerStat.DamageTaken, // HP before combat = end HP + damage taken
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

            _encounterSeen.TryGetValue(key, out int seen);
            _encounterSeen[key] = seen + 1;
        }

        // Record a kill for the encounter that ended the run, identified by matching room ModelId.
        // Use the last matching entry in history (in case the same encounter type appears multiple times).
        if (runHistory.KilledByEncounter != ModelId.none)
        {
            (ModelId, List<ModelId>)? killKey = null;
            foreach (var (entry, _) in IterateMapHistory(runHistory, playerId,
                         e => e.MapPointType is MapPointType.Monster or MapPointType.Elite or MapPointType.Boss))
            {
                if (entry.Rooms.Count == 0) continue;
                MapPointRoomHistoryEntry room = entry.Rooms[0];
                if (room.ModelId == runHistory.KilledByEncounter)
                    killKey = (room.ModelId, room.MonsterIds.ToList());
            }

            if (killKey.HasValue)
            {
                _encounterKills.TryGetValue(killKey.Value, out int kills);
                _encounterKills[killKey.Value] = kills + 1;
            }
        }
    }
    
    private void RecordCardLifecycleData(RunHistory runHistory, ulong playerId)
    {
        RunHistoryPlayer? player = runHistory.Players.FirstOrDefault(p => p.Id == playerId);
        if (player == null) return;

        // Count every copy that existed this run: final deck + anything removed mid-run.
        // Count directly (not via a floor-keyed dict) so multiple starter cards sharing
        // FloorAddedToDeck = 1 are each counted individually as separate copies.
        foreach (SerializableCard card in player.Deck)
            if (card.Id != null)
                _cardLifecycle.RecordInDeck(VariantKey(card), card.FloorAddedToDeck);

        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId))
            foreach (SerializableCard removed in playerStat.CardsRemoved)
                if (removed.Id != null)
                    _cardLifecycle.RecordInDeck(VariantKey(removed), removed.FloorAddedToDeck);

        // Separate floor->variant map used only for priority delta tracking.
        // Collision on floor 1 is fine: all starter copies were obtained at removal-count 0.
        // Priority tracking: floor -> variant at time of gaining (for removal priority)
        // Collision on floor 1 is fine — all starter copies were obtained at removal-count 0.
        var copyFloorToVariant = new Dictionary<int, CardVariantKey>();
        foreach (SerializableCard card in player.Deck)
            if (card.Id != null && card.FloorAddedToDeck.HasValue)
                copyFloorToVariant[card.FloorAddedToDeck.Value] = VariantKey(card);
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId))
            foreach (SerializableCard removed in playerStat.CardsRemoved)
                if (removed.Id != null && removed.FloorAddedToDeck.HasValue)
                    copyFloorToVariant[removed.FloorAddedToDeck.Value] = VariantKey(removed);

        var obtainedAtRemoval    = new Dictionary<int, int>();       // floorAdded -> removalCount when gained
        var firstObtainedAtSmith = new Dictionary<ModelId, int>();   // cardId -> smithCount when first gained
        int removalCount = 0;
        int smithCount   = 0;

        // Compute initial deck size: total copies ever in the run minus those gained mid-run.
        int totalGainedDuringRun = 0;
        foreach (var (_, ps) in IterateMapHistory(runHistory, playerId))
            totalGainedDuringRun += ps.CardsGained.Count;
        int totalRemoved = 0;
        foreach (var (_, ps) in IterateMapHistory(runHistory, playerId))
            totalRemoved += ps.CardsRemoved.Count;
        int deckSize = player.Deck.Count() + totalRemoved - totalGainedDuringRun;

        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId))
        {
            // Record deck size at time of pick (before this floor's cards are added).
            foreach (CardChoiceHistoryEntry choice in playerStat.CardChoices)
            {
                if (choice.wasPicked && choice.Card.Id != null)
                    _cardLifecycle.RecordPickedAtDeckSize(VariantKey(choice.Card), deckSize);
            }

            // Register newly gained cards before processing removals/upgrades on the same floor.
            foreach (SerializableCard gained in playerStat.CardsGained)
            {
                if (gained.Id == null) continue;
                if (gained.FloorAddedToDeck.HasValue)
                    obtainedAtRemoval.TryAdd(gained.FloorAddedToDeck.Value, removalCount);
                firstObtainedAtSmith.TryAdd(gained.Id, smithCount);
                deckSize++;
            }

            foreach (SerializableCard removed in playerStat.CardsRemoved)
            {
                if (removed.Id == null || !removed.FloorAddedToDeck.HasValue) continue;
                int obtainedAt = obtainedAtRemoval.GetValueOrDefault(removed.FloorAddedToDeck.Value, 0);
                _cardLifecycle.RecordRemoved(VariantKey(removed), removalCount - obtainedAt);
                removalCount++;
                deckSize--;
            }

            foreach (ModelId upgradedId in playerStat.UpgradedCards)
            {
                int obtainedAt = firstObtainedAtSmith.GetValueOrDefault(upgradedId, 0);
                _cardLifecycle.RecordUpgraded(upgradedId, smithCount - obtainedAt);
                smithCount++;
            }
        }
    }

    private void RecordCardEnchantmentData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId))
        {
            foreach (CardEnchantmentHistoryEntry entry in playerStat.CardsEnchanted)
            {
                if (entry.Card.Id == null || entry.Enchantment == ModelId.none) continue;
                ModelId cardId = entry.Card.Id;
                if (!_enchantmentCounts.TryGetValue(cardId, out var enchantCounts))
                {
                    enchantCounts = new Dictionary<ModelId, int>();
                    _enchantmentCounts[cardId] = enchantCounts;
                }
                enchantCounts.TryGetValue(entry.Enchantment, out int existing);
                enchantCounts[entry.Enchantment] = existing + 1;
            }
        }
    }

    private void RecordRestSiteChoiceData(RunHistory runHistory, ulong playerId)
    {
        foreach (var (_, playerStat) in IterateMapHistory(runHistory, playerId,
                     e => e.MapPointType == MapPointType.RestSite))
        {
            _restSiteVisits++;
            foreach (string choiceId in playerStat.RestSiteChoices)
            {
                Increment(_restSiteChoiceCounts, choiceId);
                _restSiteChoiceHpSums.TryGetValue(choiceId, out int hpSum);
                _restSiteChoiceHpSums[choiceId] = hpSum + playerStat.CurrentHp;
            }
        }
    }

    private static void Increment(Dictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out int count);
        dict[key] = count + 1;
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

            RecordRestSiteChoiceData(runHistory, player.Id);
            RecordPotionLifecycleData(runHistory, player.Id);
            RecordCardLifecycleData(runHistory, player.Id);
            RecordCardEnchantmentData(runHistory, player.Id);
        }
    }
    
    private void Merge(RunDataManager other)
    {
        _wonWithCard.Merge(other._wonWithCard);
        _wonWithRelic.Merge(other._wonWithRelic);
        _pickedAncientRelic.Merge(other._pickedAncientRelic);
        foreach (var (key, val) in other._relicFloorSums)   { _relicFloorSums.TryGetValue(key, out int e);   _relicFloorSums[key]   = e + val; }
        foreach (var (key, val) in other._relicFloorCounts) { _relicFloorCounts.TryGetValue(key, out int e); _relicFloorCounts[key] = e + val; }
        _pickedFromCardReward.Merge(other._pickedFromCardReward);
        _boughtCardFromShop.Merge(other._boughtCardFromShop);
        _boughtRelicFromShop.Merge(other._boughtRelicFromShop);

        foreach (var (key, count) in other._encounterSeen)
        {
            _encounterSeen.TryGetValue(key, out int existing);
            _encounterSeen[key] = existing + count;
        }
        foreach (var (key, count) in other._encounterKills)
        {
            _encounterKills.TryGetValue(key, out int existing);
            _encounterKills[key] = existing + count;
        }

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
        
        _restSiteVisits += other._restSiteVisits;
        MergeStringDicts(_restSiteChoiceCounts, other._restSiteChoiceCounts);
        MergeStringDicts(_restSiteChoiceHpSums, other._restSiteChoiceHpSums);
        _potionLifecycle.Merge(other._potionLifecycle);
        _cardLifecycle.Merge(other._cardLifecycle);
        foreach (var (cardId, enchantCounts) in other._enchantmentCounts)
        {
            if (!_enchantmentCounts.TryGetValue(cardId, out var existing))
            {
                existing = new Dictionary<ModelId, int>();
                _enchantmentCounts[cardId] = existing;
            }
            foreach (var (enchantId, count) in enchantCounts)
            {
                existing.TryGetValue(enchantId, out int e);
                existing[enchantId] = e + count;
            }
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

    private static CardVariantKey VariantKey(SerializableCard card) =>
        new(card.Id!, card.CurrentUpgradeLevel, card.Enchantment?.Id);

    public float GetCardWinPercentage(CardVariantKey key)         => _wonWithCard.SuccessRate(key);
    public float GetRelicWinPercentage(ModelId id)                => _wonWithRelic.SuccessRate(id);

    public float? GetRelicAvgFloor(ModelId id)
    {
        _relicFloorCounts.TryGetValue(id, out int count);
        if (count == 0) return null;
        return _relicFloorSums[id] / (float)count;
    }
    public float GetAncientPickPercentage(LocString title)        => _pickedAncientRelic.SuccessRate(title.LocEntryKey);
    public float GetCardRewardPickPercentage(CardVariantKey key)  => _pickedFromCardReward.SuccessRate(key);
    public float GetCardBuyPercentage(CardVariantKey key)         => _boughtCardFromShop.SuccessRate(key);
    public float GetRelicPurchasePercentage(ModelId id)=> _boughtRelicFromShop.SuccessRate(id);
    public float GetPotionRewardPickRate(ModelId id) => _potionLifecycle.RewardPickRate(id);
    public float GetPotionShopBuyRate(ModelId id)    => _potionLifecycle.ShopBuyRate(id);
    public float GetPotionUseRate(ModelId id)        => _potionLifecycle.UseRate(id);
    public float GetPotionDiscardRate(ModelId id)    => _potionLifecycle.DiscardRate(id);

    public float? GetCardAvgFloorAdded(CardVariantKey key)      => _cardLifecycle.AvgFloorAdded(key);
    public float? GetCardAvgDeckSizeAtPick(CardVariantKey key)  => _cardLifecycle.AvgDeckSizeAtPick(key);
    public float  GetCardRemovalRate(CardVariantKey key)        => _cardLifecycle.RemovalRate(key);
    public float  GetCardUpgradeRate(ModelId id)                => _cardLifecycle.UpgradeRate(id);
    public float? GetCardAvgRemovePriority(CardVariantKey key)  => _cardLifecycle.AvgRemovePriority(key);
    public float? GetCardAvgUpgradePriority(ModelId id)         => _cardLifecycle.AvgUpgradePriority(id);

    // Fraction of campfire visits where this option was chosen (e.g. "HEAL", "SMITH")
    public float GetRestSiteChoiceRate(string optionId)
    {
        if (_restSiteVisits == 0) return 0f;
        _restSiteChoiceCounts.TryGetValue(optionId, out int count);
        return count / (float)_restSiteVisits;
    }

    // Average current HP of the player when they made this choice
    public float? GetRestSiteChoiceAvgHp(string optionId)
    {
        _restSiteChoiceCounts.TryGetValue(optionId, out int count);
        if (count == 0) return null;
        _restSiteChoiceHpSums.TryGetValue(optionId, out int hpSum);
        return hpSum / (float)count;
    }

    private static void MergeStringDicts(Dictionary<string, int> target, Dictionary<string, int> source)
    {
        foreach (var (key, count) in source)
        {
            target.TryGetValue(key, out int existing);
            target[key] = existing + count;
        }
    }

    public (ModelId EnchantmentId, float Rate)? GetMostCommonEnchantment(ModelId cardId)
    {
        if (!_enchantmentCounts.TryGetValue(cardId, out var enchantCounts) || enchantCounts.Count == 0)
            return null;

        var top = enchantCounts.MaxBy(kvp => kvp.Value);

        // Denominator: total times this card was enchanted (any enchantment)
        int totalEnchants = enchantCounts.Values.Sum();
        float rate = totalEnchants == 0 ? 0f : top.Value / (float)totalEnchants;

        return (top.Key, rate);
    }

    public float GetEncounterKillRate(ModelId roomId, List<ModelId> monsterIds)
    {
        var key = (roomId, monsterIds);
        int seen = _encounterSeen.GetValueOrDefault(key);
        return seen == 0 ? 0f : _encounterKills.GetValueOrDefault(key) / (float)seen;
    }

    // 1 = most lethal. Returns null if this encounter has never been seen.
    public int? GetEncounterLethalityRank(ModelId roomId, List<ModelId> monsterIds)
    {
        var key = (roomId, monsterIds);
        if (!_encounterSeen.ContainsKey(key)) return null;

        float myRate = GetEncounterKillRate(roomId, monsterIds);
        int rank = 1;
        foreach (var (otherKey, otherSeen) in _encounterSeen)
        {
            if (EncounterKeyComparer.Instance.Equals(otherKey, key)) continue;
            float otherRate = otherSeen == 0 ? 0f : _encounterKills.GetValueOrDefault(otherKey) / (float)otherSeen;
            if (otherRate > myRate) rank++;
        }
        return rank;
    }

    public MonsterEncounterData? GetEncounterAverages(ModelId roomId, List<ModelId> monsterIds)
    {
        var key = (roomId, monsterIds);
        if (!_monsterEncounters.TryGetValue(key, out var entries) || entries.Count == 0)
            return null;

        return new MonsterEncounterData
        {
            EntryHp     = (int)Math.Round(entries.Average(e => e.EntryHp)),
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