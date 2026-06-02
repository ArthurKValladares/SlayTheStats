using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
public static class CardModelHoverTipsPatch
{
    public static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance.IsCanonical) return;

        // Construct HoverTip
        var tip = new HoverTip();
        object boxed = tip;
        
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Stats");

        RunDataManager rdm = RunDataManager.Instance;
        var variantKey = new CardVariantKey(__instance.Id, __instance.CurrentUpgradeLevel, __instance.Enchantment?.Id);

        float? avgFloor       = rdm.GetCardAvgFloorAdded(variantKey);
        float? avgDeckSize    = rdm.GetCardAvgDeckSizeAtPick(variantKey);
        float winPct         = rdm.GetCardWinPercentage(variantKey)        * 100f;
        float rewardPickPct  = rdm.GetCardRewardPickPercentage(variantKey) * 100f;
        float purchasePct    = rdm.GetCardBuyPercentage(variantKey)        * 100f;
        float removalPct     = rdm.GetCardRemovalRate(variantKey)          * 100f;
        float upgradePct     = rdm.GetCardUpgradeRate(__instance.Id)       * 100f;
        float? removePriority  = rdm.GetCardAvgRemovePriority(variantKey);
        float? upgradePriority = rdm.GetCardAvgUpgradePriority(__instance.Id);

        string removePriorityStr  = removePriority.HasValue  ? $"{removePriority.Value:F1}"  : "N/A";
        string upgradePriorityStr = upgradePriority.HasValue ? $"{upgradePriority.Value:F1}" : "N/A";

        var topEnchant = rdm.GetMostCommonEnchantment(__instance.Id);
        string enchantStr = topEnchant.HasValue
            ? $"{topEnchant.Value.EnchantmentId.Entry} ({topEnchant.Value.Rate * 100f:F1}%)"
            : "None";

        string avgFloorStr    = avgFloor.HasValue    ? $"{avgFloor.Value:F1}"    : "N/A";
        string avgDeckSizeStr = avgDeckSize.HasValue ? $"{avgDeckSize.Value:F1}" : "N/A";

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
            $"Avg Floor Added: {avgFloorStr} (avg deck size: {avgDeckSizeStr})\n" +
            $"Win Rate: {winPct:F1}%\n" +
            $"Reward Pick Rate: {rewardPickPct:F1}%\n" +
            $"Shop Buy Rate: {purchasePct:F1}%\n" +
            $"Removal Rate: {removalPct:F1}% (avg priority: {removePriorityStr})\n" +
            $"Upgrade Rate: {upgradePct:F1}% (avg priority: {upgradePriorityStr})\n" +
            $"Most Common Enchantment: {enchantStr}");

        tip = (HoverTip)boxed;
        
        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}