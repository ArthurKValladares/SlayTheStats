using SlayTheStats.SlayTheStatsCode.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs.History;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(Creature), nameof(Creature.HoverTips), MethodType.Getter)]
public static class CreatureHoverTipsPatch
{
    public static void Postfix(Creature __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SlayTheStatsConfig.ShowStatsHoverTips) return;
        if (!__instance.IsMonster) return;
        if (__instance.CombatState == null) return;

        var history = __instance.CombatState.RunState.CurrentMapPointHistoryEntry;
        if (history == null || history.Rooms.Count == 0) return;

        MapPointRoomHistoryEntry room = history.Rooms[^1];
        if (room.ModelId == null) return;

        RunDataManager rdm    = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.CurrentBuildId);
        RunDataManager rdmAll = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.AllPatches);

        var averages = rdm.GetEncounterAverages(room.ModelId, room.MonsterIds);
        if (averages == null) return;

        var averagesAll = rdmAll.GetEncounterAverages(room.ModelId, room.MonsterIds);

        float killRate    = rdm.GetEncounterKillRate(room.ModelId, room.MonsterIds)    * 100f;
        float killRateAll = rdmAll.GetEncounterKillRate(room.ModelId, room.MonsterIds) * 100f;
        int?  rankByRate  = rdm.GetEncounterLethalityRankByRate(room.ModelId, room.MonsterIds);
        int?  rankByCount = rdm.GetEncounterLethalityRankByCount(room.ModelId, room.MonsterIds);
        string encounterTypeStr = history.MapPointType switch
        {
            MegaCrit.Sts2.Core.Map.MapPointType.Elite => "elite",
            MegaCrit.Sts2.Core.Map.MapPointType.Boss  => "boss",
            _                                          => "monster",
        };
        string rankByRateStr  = rankByRate.HasValue  ? $"#{rankByRate.Value} among {encounterTypeStr}s"  : "N/A";
        string rankByCountStr = rankByCount.HasValue ? $"#{rankByCount.Value} among {encounterTypeStr}s" : "N/A";

        string AllAvg(int patchVal, MonsterEncounterData? all, Func<MonsterEncounterData, int> selector)
            => all != null ? $"{patchVal} (all: {selector(all)})" : $"{patchVal}";

        var tip = new HoverTip();
        object boxed = tip;

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Encounter Stats");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
            $"Kill Rate: {killRate:F1}% (all patches: {killRateAll:F1}%) (rank by rate: {rankByRateStr}, rank by count: {rankByCountStr})\n" +
            $"Avg Entry HP: {AllAvg(averages.EntryHp, averagesAll, e => e.EntryHp)}\n" +
            $"Avg Turns: {AllAvg(averages.TurnsTaken, averagesAll, e => e.TurnsTaken)}\n" +
            $"Avg Damage Taken: {AllAvg(averages.DamageTaken, averagesAll, e => e.DamageTaken)}\n" +
            $"Avg Gold Gained: {AllAvg(averages.GoldGained, averagesAll, e => e.GoldGained)}\n" +
            $"Avg Gold Stolen: {AllAvg(averages.GoldStolen, averagesAll, e => e.GoldStolen)}\n" +
            $"Avg Max HP Lost: {AllAvg(averages.MaxHpLost, averagesAll, e => e.MaxHpLost)}\n" +
            $"Avg HP Healed: {AllAvg(averages.HpHealed, averagesAll, e => e.HpHealed)}\n" +
            $"Avg Max HP Gained: {AllAvg(averages.MaxHpGained, averagesAll, e => e.MaxHpGained)}");

        tip = (HoverTip)boxed;

        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}
