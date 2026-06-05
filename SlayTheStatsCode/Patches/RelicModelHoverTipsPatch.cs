using SlayTheStats.SlayTheStatsCode.Config;
﻿using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTips), MethodType.Getter)]
public static class RelicModelHoverTipsPatch
{
    public static void Postfix(RelicModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SlayTheStatsConfig.ShowStatsHoverTips) return;
        if (__instance.IsCanonical) return;

        // Construct HoverTip
        var tip = new HoverTip();
        object boxed = tip;
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Stats");

        RunDataManager rdm = RunDataManager.GetInstance(RunDataManager.CurrentAscension);
        float winPct         = rdm.GetRelicWinPercentage(__instance.Id)          * 100f;
        float ancientPickPct = rdm.GetAncientPickPercentage(__instance.Title)    * 100f;
        float purchasePct    = rdm.GetRelicPurchasePercentage(__instance.Id)     * 100f;
        float? avgFloor      = rdm.GetRelicAvgFloor(__instance.Id);
        string avgFloorStr   = avgFloor.HasValue ? $"{avgFloor.Value:F1}" : "N/A";

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
            $"Avg Floor Acquired: {avgFloorStr}\n" +
            $"Win Rate: {winPct:F1}%\n" +
            $"Ancient Pick Rate: {ancientPickPct:F1}%\n" +
            $"Shop Buy Rate: {purchasePct:F1}%");

        tip = (HoverTip)boxed;
        
        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}