using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using SlayTheStats.SlayTheStatsCode.LiveStats;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardDrawn))]
public static class CardDrawnPatch
{
    public static void Postfix(CardModel card)
    {
        if (card.Owner == null) return;
        InRunStatsTracker.RecordDraw(card);
    }
}

[HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayStarted))]
public static class CardPlayStartedPatch
{
    public static void Postfix(CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner == null) return;
        InRunStatsTracker.RecordPlay(cardPlay.Card);
    }
}
