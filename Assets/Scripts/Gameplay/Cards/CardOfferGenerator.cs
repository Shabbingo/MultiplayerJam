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
        // Strength score serves two purposes: high-score cards are globally rarer,
        // but the first players eliminated receive a strong-card comeback bias.
        private const float GlobalRarityBase = 1.6f;
        private const float PlacementBiasBase = 1.5f;

        public static List<CardModifierDefinition> Generate(
            CardModifierCatalog catalog,
            in PlayerModifierStacks owned,
            int finishingPosition,
            int playerCount,
            int offerCount,
            int seed,
            int roundNumber = 1,
            int foundationalBoostRounds = 0,
            float foundationalStartingWeightMultiplier = 1f)
        {
            var result = new List<CardModifierDefinition>(Mathf.Max(0, offerCount));
            if (catalog == null || offerCount <= 0)
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
            for (int draw = 0; draw < drawCount; draw++)
            {
                int selectedIndex = DrawWeightedIndex(candidates, finishingPosition,
                    playerCount, roundNumber, foundationalBoostRounds,
                    foundationalStartingWeightMultiplier, random);
                result.Add(candidates[selectedIndex]);
                candidates.RemoveAt(selectedIndex);
            }

            return result;
        }

        public static bool HasAvailableTarget(CardModifierDefinition card,
            in PlayerModifierStacks owned)
        {
            return IsAvailable(card, ModifierTarget.LeftHand, in owned) ||
                   IsAvailable(card, ModifierTarget.RightHand, in owned) ||
                   IsAvailable(card, ModifierTarget.Self, in owned) ||
                   IsAvailable(card, ModifierTarget.BothHands, in owned);
        }

        private static bool IsAvailable(CardModifierDefinition card,
            ModifierTarget target, in PlayerModifierStacks owned)
        {
            return card.Allows(target);
        }

        private static int DrawWeightedIndex(
            IReadOnlyList<CardModifierDefinition> candidates,
            int finishingPosition,
            int playerCount,
            int roundNumber,
            int foundationalBoostRounds,
            float foundationalStartingWeightMultiplier,
            System.Random random)
        {
            float placement01 = playerCount <= 1
                ? 0.5f
                : Mathf.Clamp01(finishingPosition / (float)(playerCount - 1));
            // +1 for first eliminated, -1 for the round winner.
            float comebackBias = 1f - placement01 * 2f;
            double totalWeight = 0d;
            for (int i = 0; i < candidates.Count; i++)
            {
                totalWeight += GetWeight(candidates[i], comebackBias,
                    roundNumber, foundationalBoostRounds,
                    foundationalStartingWeightMultiplier);
            }

            double roll = random.NextDouble() * totalWeight;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= GetWeight(candidates[i], comebackBias, roundNumber,
                    foundationalBoostRounds,
                    foundationalStartingWeightMultiplier);
                if (roll <= 0d)
                {
                    return i;
                }
            }

            return candidates.Count - 1;
        }

        private static double GetWeight(CardModifierDefinition card,
            float comebackBias,
            int roundNumber,
            int foundationalBoostRounds,
            float foundationalStartingWeightMultiplier)
        {
            int centeredStrength = card.StrengthScore - 3;
            double rarityWeight = Math.Pow(GlobalRarityBase, -centeredStrength);
            double placementWeight = Math.Pow(
                PlacementBiasBase, comebackBias * centeredStrength);
            double categoryWeight = GetCategoryWeight(card, roundNumber,
                foundationalBoostRounds,
                foundationalStartingWeightMultiplier);
            return card.BaseOfferWeight * rarityWeight * placementWeight *
                   categoryWeight;
        }

        private static double GetCategoryWeight(CardModifierDefinition card,
            int roundNumber,
            int foundationalBoostRounds,
            float foundationalStartingWeightMultiplier)
        {
            if (card.Category != CardCategory.Foundational ||
                foundationalBoostRounds <= 0 ||
                foundationalStartingWeightMultiplier <= 1f)
            {
                return 1d;
            }

            float earlyGameProgress = Mathf.Clamp01(
                (Mathf.Max(1, roundNumber) - 1f) / foundationalBoostRounds);
            return Mathf.Lerp(foundationalStartingWeightMultiplier, 1f,
                earlyGameProgress);
        }
    }
}
