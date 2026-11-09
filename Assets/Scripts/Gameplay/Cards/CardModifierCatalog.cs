using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS.Cards
{
    [CreateAssetMenu(fileName = "CardModifierCatalog", menuName = "Multiplayer Jam/Card Modifier Catalog")]
    public sealed class CardModifierCatalog : ScriptableObject
    {
        [SerializeField] private List<CardModifierDefinition> m_Cards = new();

        public IReadOnlyList<CardModifierDefinition> Cards => m_Cards;

        public CardModifierDefinition FindById(int cardId)
        {
            foreach (var card in m_Cards)
            {
                if (card != null && card.CardId == cardId)
                {
                    return card;
                }
            }

            return null;
        }

        public CardModifierDefinition FindByKind(CardModifierKind kind)
        {
            foreach (var card in m_Cards)
            {
                if (card != null && card.Kind == kind)
                {
                    return card;
                }
            }

            return null;
        }

        private void OnValidate()
        {
            var usedIds = new HashSet<int>();
            foreach (var card in m_Cards)
            {
                if (card != null && !usedIds.Add(card.CardId))
                {
                    Debug.LogError($"Duplicate card ID {card.CardId} in catalog '{name}'.", this);
                }
            }
        }
    }
}
