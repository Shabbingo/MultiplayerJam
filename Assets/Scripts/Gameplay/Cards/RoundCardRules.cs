using UnityEngine;
using UnityEngine.Serialization;

namespace Unity.MP_FPS.Cards
{
    public enum RoundPhase : byte
    {
        WaitingForPlayers,
        PreparingRound,
        Combat,
        Drafting,
        MatchEnded
    }

    [CreateAssetMenu(fileName = "RoundCardRules", menuName = "Multiplayer Jam/Round Card Rules")]
    public sealed class RoundCardRules : ScriptableObject
    {
        public const int MaximumCardsPerOffer = 5;

        [FormerlySerializedAs("m_RoundsPerMatch")]
        [SerializeField, Min(1)] private int m_RoundWinsToWin = 5;
        [SerializeField, Range(1, MaximumCardsPerOffer)] private int m_CardsPerOffer = 5;
        [SerializeField, Min(1), Tooltip("Maximum combined rarity-point cost of every card displayed in an offer.")]
        private int m_OfferPointBudget = 10;
        [SerializeField, Min(1f)] private float m_SecondsPerDraftTurn = 20f;
        [SerializeField, Min(0f)] private float m_RoundStartDelay = 2f;

        [Header("Opening Draft Weighting")]
        [SerializeField, Min(0), Tooltip("Opening rounds that favour left/right weapon cards. After these rounds, offers are driven only by rarity and finishing position.")]
        private int m_OpeningHandBiasRounds = 2;
        [SerializeField, Min(0f), Tooltip("Offer-weight multiplier for left/right weapon cards during the opening rounds.")]
        private float m_OpeningHandWeightMultiplier = 3f;
        [SerializeField, Min(0f), Tooltip("Offer-weight multiplier for self cards during the opening rounds.")]
        private float m_OpeningSelfWeightMultiplier = 0.5f;

        public int RoundWinsToWin => m_RoundWinsToWin;
        public int CardsPerOffer => Mathf.Clamp(m_CardsPerOffer, 1,
            MaximumCardsPerOffer);
        public int OfferPointBudget => Mathf.Max(CardsPerOffer,
            m_OfferPointBudget);
        public float SecondsPerDraftTurn => m_SecondsPerDraftTurn;
        public float RoundStartDelay => m_RoundStartDelay;
        public int OpeningHandBiasRounds => m_OpeningHandBiasRounds;
        public float OpeningHandWeightMultiplier =>
            m_OpeningHandWeightMultiplier;
        public float OpeningSelfWeightMultiplier =>
            m_OpeningSelfWeightMultiplier;
        private void OnValidate()
        {
            m_CardsPerOffer = Mathf.Clamp(m_CardsPerOffer, 1,
                MaximumCardsPerOffer);
            m_OfferPointBudget = Mathf.Max(m_CardsPerOffer,
                m_OfferPointBudget);
            m_OpeningHandBiasRounds = Mathf.Max(0, m_OpeningHandBiasRounds);
            m_OpeningHandWeightMultiplier = Mathf.Max(0f,
                m_OpeningHandWeightMultiplier);
            m_OpeningSelfWeightMultiplier = Mathf.Max(0f,
                m_OpeningSelfWeightMultiplier);
        }
    }
}
