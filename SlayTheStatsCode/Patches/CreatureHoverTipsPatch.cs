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
        if (!__instance.IsMonster) return;
        if (__instance.CombatState == null) return;

        var history = __instance.CombatState.RunState.CurrentMapPointHistoryEntry;
        if (history == null || history.Rooms.Count == 0) return;

        MapPointRoomHistoryEntry room = history.Rooms[^1];
        if (room.ModelId == null) return;

        var averages = RunDataManager.Instance.GetEncounterAverages(room.ModelId, room.MonsterIds);
        if (averages == null) return;

        var tip = new HoverTip();
        object boxed = tip;

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Encounter Stats");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
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
