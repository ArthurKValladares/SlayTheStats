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

        RunDataManager rdm    = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.CurrentBuildId);
        RunDataManager rdmAll = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.AllPatches);

        float rewardPickRate = rdm.GetPotionRewardPickRate(__instance.Id) * 100f;
        float shopBuyRate    = rdm.GetPotionShopBuyRate(__instance.Id)    * 100f;
        float useRate        = rdm.GetPotionUseRate(__instance.Id)        * 100f;
        float discardRate    = rdm.GetPotionDiscardRate(__instance.Id)    * 100f;

        float rewardPickRateAll = rdmAll.GetPotionRewardPickRate(__instance.Id) * 100f;
        float shopBuyRateAll    = rdmAll.GetPotionShopBuyRate(__instance.Id)    * 100f;
        float useRateAll        = rdmAll.GetPotionUseRate(__instance.Id)        * 100f;
        float discardRateAll    = rdmAll.GetPotionDiscardRate(__instance.Id)    * 100f;

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Stats");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
            $"Reward Pick Rate: {rewardPickRate:F1}% (all patches: {rewardPickRateAll:F1}%)\n" +
            $"Shop Buy Rate: {shopBuyRate:F1}% (all: {shopBuyRateAll:F1}%)\n" +
            $"Use Rate: {useRate:F1}% (all: {useRateAll:F1}%)\n" +
            $"Discard Rate: {discardRate:F1}% (all: {discardRateAll:F1}%)");

        tip = (HoverTip)boxed;

        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}
