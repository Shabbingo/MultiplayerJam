using System;
using System.Collections.Generic;

namespace Unity.MP_FPS.Cards.EditorTools
{
    public enum CardWorkshopIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct CardWorkshopIssue
    {
        public CardWorkshopIssue(CardWorkshopIssueSeverity severity,
            string message, CardModifierDefinition card = null)
        {
            Severity = severity;
            Message = message;
            Card = card;
        }

        public CardWorkshopIssueSeverity Severity { get; }
        public string Message { get; }
        public CardModifierDefinition Card { get; }
    }

    public static class CardWorkshopValidator
    {
        public static List<CardWorkshopIssue> Validate(
            CardModifierCatalog catalog,
            IReadOnlyList<CardModifierDefinition> projectCards)
        {
            var issues = new List<CardWorkshopIssue>();
            if (catalog == null)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Error,
                    "No Card Modifier Catalog is selected."));
                return issues;
            }

            var catalogCards = new HashSet<CardModifierDefinition>();
            var ids = new Dictionary<int, CardModifierDefinition>();
            var kinds = new Dictionary<CardModifierKind,
                CardModifierDefinition>();

            for (int i = 0; i < catalog.Cards.Count; i++)
            {
                CardModifierDefinition card = catalog.Cards[i];
                if (card == null)
                {
                    issues.Add(new CardWorkshopIssue(
                        CardWorkshopIssueSeverity.Error,
                        $"Catalog entry {i + 1} is empty."));
                    continue;
                }

                catalogCards.Add(card);
                ValidateCard(card, issues);

                if (ids.TryGetValue(card.CardId,
                        out CardModifierDefinition duplicateId))
                {
                    issues.Add(new CardWorkshopIssue(
                        CardWorkshopIssueSeverity.Error,
                        $"Card ID {card.CardId} is shared by " +
                        $"'{duplicateId.DisplayName}' and " +
                        $"'{card.DisplayName}'.", card));
                }
                else
                {
                    ids.Add(card.CardId, card);
                }

                if (kinds.TryGetValue(card.Kind,
                        out CardModifierDefinition duplicateKind))
                {
                    issues.Add(new CardWorkshopIssue(
                        CardWorkshopIssueSeverity.Error,
                        $"Modifier kind {card.Kind} is assigned to both " +
                        $"'{duplicateKind.DisplayName}' and " +
                        $"'{card.DisplayName}'. Runtime lookups can only " +
                        "use one definition per kind.", card));
                }
                else
                {
                    kinds.Add(card.Kind, card);
                }
            }

            if (projectCards != null)
            {
                foreach (CardModifierDefinition card in projectCards)
                {
                    if (card != null && !catalogCards.Contains(card))
                    {
                        issues.Add(new CardWorkshopIssue(
                            CardWorkshopIssueSeverity.Warning,
                            $"'{card.DisplayName}' exists in the project but " +
                            "is not included in the selected catalog.", card));
                    }
                }
            }

            if (issues.Count == 0)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Info,
                    $"Catalog is healthy: {catalog.Cards.Count} cards, " +
                    "with unique IDs and modifier kinds."));
            }

            return issues;
        }

        private static void ValidateCard(CardModifierDefinition card,
            ICollection<CardWorkshopIssue> issues)
        {
            if (card.CardId < 1)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Error,
                    "Card ID must be at least 1.", card));
            }

            if (string.IsNullOrWhiteSpace(card.DisplayName))
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Error,
                    "Card display name is empty.", card));
            }

            if (card.AllowedTargets == ModifierTarget.None)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Error,
                    $"'{card.DisplayName}' has no allowed target.", card));
            }

            if (!IsUtility(card.Kind))
            {
                return;
            }

            if (card.Category != CardCategory.Utility)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Warning,
                    $"Utility card '{card.DisplayName}' is not in the " +
                    "Utility category.", card));
            }

            if (card.AllowedTargets != ModifierTarget.Self)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Error,
                    $"Utility card '{card.DisplayName}' must target Self " +
                    "only.", card));
            }

            if ((card.Kind is CardModifierKind.ActiveCamoUtility or
                CardModifierKind.ForwardChargeUtility or
                CardModifierKind.SmallDashUtility) &&
                card.EffectDurationSeconds <= 0f)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Error,
                    $"'{card.DisplayName}' needs an effect duration greater " +
                    "than zero.", card));
            }

            if ((card.Kind is CardModifierKind.MegaJumpUtility or
                CardModifierKind.SmallDashUtility) && card.ValuePerStack < 1f)
            {
                issues.Add(new CardWorkshopIssue(
                    CardWorkshopIssueSeverity.Error,
                    $"'{card.DisplayName}' needs a multiplier of at least 1.",
                    card));
            }
        }

        public static bool IsUtility(CardModifierKind kind)
        {
            return kind is CardModifierKind.ActiveCamoUtility or
                CardModifierKind.ForwardChargeUtility or
                CardModifierKind.MegaJumpUtility or
                CardModifierKind.SmallDashUtility;
        }
    }
}
