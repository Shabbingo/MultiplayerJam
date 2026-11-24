using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.Cards
{
    /// <summary>
    /// Server-side deterministic offer generation. Finishing position zero is the
    /// first player eliminated; the final position is the round winner.
    /// </summary>
    public static class CardOfferGenerator
    {
        // Rarity serves two purposes: stronger cards are globally rarer, but the
        // first players eliminated receive a stronger-card comeback bias.
        private const float GlobalRarityBase = 1.6f;
        private const float PlacementBiasBase = 2f;
        private const float HandScoreBiasBase = 2f;

        public static List<CardModifierDefinition> Generate(
            CardModifierCatalog catalog,
            in PlayerModifierStacks owned,
            int finishingPosition,
            int playerCount,
            int offerCount,
            int offerPointBudget,
            int seed,
            int ownedHandScore,
            float averageHandScore,
            int roundNumber = 1,
            int openingHandBiasRounds = 2,
            float openingHandWeightMultiplier = 3f,
            float openingSelfWeightMultiplier = 0.5f)
        {
            var result = new List<CardModifierDefinition>(Mathf.Max(0, offerCount));
            if (catalog == null || offerCount <= 0 || offerPointBudget <= 0)
            {
                return result;
            }

            var candidates = new List<CardModifierDefinition>();
            foreach (var card in catalog.Cards)
            {
                if (card != null && HasAvailableTarget(card, in owned))
                {
                    candidates.Add(card);
                }
            }

            var random = new System.Random(seed);
            int drawCount = Mathf.Min(offerCount, candidates.Count);
            int remainingBudget = offerPointBudget;
            for (int draw = 0; draw < drawCount; draw++)
            {
                int remainingSlots = drawCount - draw - 1;
                var feasibleCandidates = GetFeasibleCandidates(candidates,
                    remainingSlots, remainingBudget);
                if (feasibleCandidates.Count == 0)
                {
                    break;
                }

                int selectedIndex = DrawWeightedIndex(feasibleCandidates,
                    finishingPosition,
                    playerCount, ownedHandScore, averageHandScore,
                    roundNumber, openingHandBiasRounds,
                    openingHandWeightMultiplier,
                    openingSelfWeightMultiplier, random);
                CardModifierDefinition selected = feasibleCandidates[selectedIndex];
                result.Add(selected);
                remainingBudget -= selected.DraftPointCost;
                candidates.Remove(selected);
            }

            return result;
        }

        public static bool HasAvailableTarget(CardModifierDefinition card,
            in PlayerModifierStacks owned)
        {
            return IsAvailable(card, ModifierTarget.LeftHand, in owned) ||
                   IsAvailable(card, ModifierTarget.RightHand, in owned) ||
                   IsAvailable(card, ModifierTarget.Self, in owned);
        }

        public static int CalculateHandScore(CardModifierCatalog catalog,
            in PlayerModifierStacks owned)
        {
            if (catalog == null) return 0;

            int score = 0;
            foreach (CardModifierDefinition card in catalog.Cards)
            {
                if (card == null) continue;
                if (card.Allows(ModifierTarget.LeftHand))
                {
                    score += owned.GetStacks(card.Kind,
                        ModifierTarget.LeftHand) * card.DraftPointCost;
                }
                if (card.Allows(ModifierTarget.RightHand))
                {
                    score += owned.GetStacks(card.Kind,
                        ModifierTarget.RightHand) * card.DraftPointCost;
                }
                if (card.Allows(ModifierTarget.Self))
                {
                    score += owned.GetStacks(card.Kind,
                        ModifierTarget.Self) * card.DraftPointCost;
                }
            }

            return score;
        }

        private static bool IsAvailable(CardModifierDefinition card,
            ModifierTarget target, in PlayerModifierStacks owned)
        {
            if (!card.Allows(target))
            {
                return false;
            }

            // Utility unlocks replace a size-specific slot and are intentionally
            // one-time draft choices rather than stackable stat modifiers.
            if (target == ModifierTarget.Self &&
                card.Kind is CardModifierKind.ActiveCamoUtility or
                    CardModifierKind.ForwardChargeUtility or
                    CardModifierKind.MegaJumpUtility or
                    CardModifierKind.SmallDashUtility)
            {
                return owned.GetStacks(card.Kind, target) == 0;
            }

            return true;
        }

        private static List<CardModifierDefinition> GetFeasibleCandidates(
            IReadOnlyList<CardModifierDefinition> candidates,
            int remainingSlots, int remainingBudget)
        {
            var feasible = new List<CardModifierDefinition>(candidates.Count);
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                CardModifierDefinition candidate = candidates[candidateIndex];
                int budgetAfterPick = remainingBudget - candidate.DraftPointCost;
                if (budgetAfterPick < 0 ||
                    !CanFillRemainingSlots(candidates, candidateIndex,
                        remainingSlots, budgetAfterPick))
                {
                    continue;
                }

                feasible.Add(candidate);
            }

            return feasible;
        }

        private static bool CanFillRemainingSlots(
            IReadOnlyList<CardModifierDefinition> candidates,
            int excludedIndex, int remainingSlots, int remainingBudget)
        {
            if (remainingSlots <= 0) return true;
            if (candidates.Count - 1 < remainingSlots) return false;

            var remainingCosts = new List<int>(candidates.Count - 1);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i == excludedIndex) continue;
                remainingCosts.Add(candidates[i].DraftPointCost);
            }

            remainingCosts.Sort();
            int minimumRequiredBudget = 0;
            for (int i = 0; i < remainingSlots; i++)
            {
                minimumRequiredBudget += remainingCosts[i];
            }

            return minimumRequiredBudget <= remainingBudget;
        }

        private static int DrawWeightedIndex(
            IReadOnlyList<CardModifierDefinition> candidates,
            int finishingPosition,
            int playerCount,
            int ownedHandScore,
            float averageHandScore,
            int roundNumber,
            int openingHandBiasRounds,
            float openingHandWeightMultiplier,
            float openingSelfWeightMultiplier,
            System.Random random)
        {
            float placement01 = playerCount <= 1
                ? 0.5f
                : Mathf.Clamp01(finishingPosition / (float)(playerCount - 1));
            // +1 for first eliminated, -1 for the round winner.
            float comebackBias = 1f - placement01 * 2f;
            float handScoreBias = Mathf.Clamp(
                (averageHandScore - ownedHandScore) /
                Mathf.Max(1f, averageHandScore), -1f, 1f);
            double totalWeight = 0d;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += GetWeight(candidates[i], comebackBias,
                    handScoreBias,
                    roundNumber, openingHandBiasRounds,
                    openingHandWeightMultiplier,
                    openingSelfWeightMultiplier);
            }

            double roll = random.NextDouble() * totalWeight;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= GetWeight(candidates[i], comebackBias, handScoreBias,
                    roundNumber,
                    openingHandBiasRounds, openingHandWeightMultiplier,
                    openingSelfWeightMultiplier);
                if (roll <= 0d)
                {
                    return i;
                }
            }

            return candidates.Count - 1;
        }

        private static double GetWeight(CardModifierDefinition card,
            float comebackBias,
            float handScoreBias,
            int roundNumber,
            int openingHandBiasRounds,
            float openingHandWeightMultiplier,
            float openingSelfWeightMultiplier)
        {
            int centeredRarity = (int)card.Rarity - (int)CardRarity.Rare;
            double rarityWeight = Math.Pow(GlobalRarityBase, -centeredRarity);
            double placementWeight = Math.Pow(
                PlacementBiasBase, comebackBias * centeredRarity);
            double handScoreWeight = Math.Pow(
                HandScoreBiasBase, handScoreBias * centeredRarity);
            double openingTargetWeight = GetOpeningTargetWeight(card, roundNumber,
                openingHandBiasRounds, openingHandWeightMultiplier,
                openingSelfWeightMultiplier);
            return card.BaseOfferWeight * rarityWeight * placementWeight *
                   handScoreWeight * openingTargetWeight;
        }

        private static double GetOpeningTargetWeight(
            CardModifierDefinition card, int roundNumber,
            int openingHandBiasRounds, float openingHandWeightMultiplier,
            float openingSelfWeightMultiplier)
        {
            if (roundNumber > Mathf.Max(0, openingHandBiasRounds))
            {
                return 1d;
            }

            bool canTargetAHand = card.Allows(ModifierTarget.LeftHand) ||
                                  card.Allows(ModifierTarget.RightHand);
            return canTargetAHand
                ? Mathf.Max(0f, openingHandWeightMultiplier)
                : Mathf.Max(0f, openingSelfWeightMultiplier);
        }
    }
}
