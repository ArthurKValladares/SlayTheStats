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

        RunDataManager rdm = RunDataManager.GetInstance(RunDataManager.CurrentAscension);
        var averages = rdm.GetEncounterAverages(room.ModelId, room.MonsterIds);
        if (averages == null) return;

        float killRate   = rdm.GetEncounterKillRate(room.ModelId, room.MonsterIds) * 100f;
        int?  rankByRate  = rdm.GetEncounterLethalityRankByRate(room.ModelId, room.MonsterIds);
        int?  rankByCount = rdm.GetEncounterLethalityRankByCount(room.ModelId, room.MonsterIds);
        string rankByRateStr  = rankByRate.HasValue  ? $"#{rankByRate.Value}"  : "N/A";
        string rankByCountStr = rankByCount.HasValue ? $"#{rankByCount.Value}" : "N/A";

        var tip = new HoverTip();
        object boxed = tip;

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Encounter Stats");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
            $"Kill Rate: {killRate:F1}% (rank by rate: {rankByRateStr}, rank by count: {rankByCountStr})\n" +
            $"Avg Entry HP: {averages.EntryHp}\n" +
            $"Avg Turns: {averages.TurnsTaken}\n" +
            $"Avg Damage Taken: {averages.DamageTaken}\n" +
            $"Avg Gold Gained: {averages.GoldGained}\n" +
            $"Avg Gold Stolen: {averages.GoldStolen}\n" +
            $"Avg Max HP Lost: {averages.MaxHpLost}\n" +
            $"Avg HP Healed: {averages.HpHealed}\n" +
            $"Avg Max HP Gained: {averages.MaxHpGained}");

        tip = (HoverTip)boxed;

        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}
