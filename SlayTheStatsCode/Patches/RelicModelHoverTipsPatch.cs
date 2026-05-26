using HarmonyLib;
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
        if (__instance.IsCanonical) return;

        // Construct HoverTip
        var tip = new HoverTip();
        object boxed = tip;
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Test");
        
        RunDataManager runDataManager = RunDataManager.Instance;
        float winPercentage = runDataManager.GetRelicWinPercentage(__instance.Id) * 100.0f;
        float ancientPickPercentage = runDataManager.GetAncientPickPercentage(__instance.Title) * 100.0f;
        float purchasePercentage = runDataManager.GetRelicPurchasePercentage(__instance.Id) * 100.0f;

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed, $"Win Percentage {winPercentage}%\nAncient Pick Percentage {ancientPickPercentage}\nPurchase Percentage {purchasePercentage}%");

        tip = (HoverTip)boxed;
        
        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}