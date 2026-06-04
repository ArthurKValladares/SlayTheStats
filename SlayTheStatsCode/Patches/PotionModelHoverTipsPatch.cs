using SlayTheStats.SlayTheStatsCode.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.HoverTips), MethodType.Getter)]
public static class PotionModelHoverTipsPatch
{
    public static void Postfix(PotionModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (!SlayTheStatsConfig.ShowStatsHoverTips) return;
        if (__instance.IsCanonical) return;

        var tip = new HoverTip();
        object boxed = tip;

        RunDataManager runDataManager = RunDataManager.Instance;
        float rewardPickRate = runDataManager.GetPotionRewardPickRate(__instance.Id) * 100.0f;
        float shopBuyRate    = runDataManager.GetPotionShopBuyRate(__instance.Id)    * 100.0f;
        float useRate        = runDataManager.GetPotionUseRate(__instance.Id)        * 100.0f;
        float discardRate    = runDataManager.GetPotionDiscardRate(__instance.Id)    * 100.0f;

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Stats");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed, $"Reward Pick Rate: {rewardPickRate:F1}%\nShop Buy Rate: {shopBuyRate:F1}%\nUse Rate: {useRate:F1}%\nDiscard Rate: {discardRate:F1}%");

        tip = (HoverTip)boxed;

        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}
