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
        [FormerlySerializedAs("m_RoundsPerMatch")]
        [SerializeField, Min(1)] private int m_RoundWinsToWin = 5;
        [SerializeField, Min(1)] private int m_CardsPerOffer = 3;
        [SerializeField, Min(1f)] private float m_SecondsPerDraftTurn = 20f;
        [SerializeField, Min(0f)] private float m_RoundStartDelay = 2f;

        [Header("Opening Draft Weighting")]
        [SerializeField, Min(0), Tooltip("Opening rounds that favour left/right weapon cards. After these rounds, offers are driven only by rarity and finishing position.")]
        private int m_OpeningHandBiasRounds = 2;
        [SerializeField, Min(0f), Tooltip("Offer-weight multiplier for left/right weapon cards during the opening rounds.")]
        private float m_OpeningHandWeightMultiplier = 3f;
        [SerializeField, Min(0f), Tooltip("Offer-weight multiplier for self cards during the opening rounds.")]
        private float m_OpeningSelfWeightMultiplier = 0.5f;

        [Header("Alternating Hand Drafts")]
        [SerializeField, Min(1), Tooltip("Every Nth late-join catch-up pick may be applied to either hand instead of only the round hand.")]
        private int m_CatchUpAdaptationPickInterval = 3;

        public int RoundWinsToWin => m_RoundWinsToWin;
        public int CardsPerOffer => m_CardsPerOffer;
        public float SecondsPerDraftTurn => m_SecondsPerDraftTurn;
        public float RoundStartDelay => m_RoundStartDelay;
        public int OpeningHandBiasRounds => m_OpeningHandBiasRounds;
        public float OpeningHandWeightMultiplier =>
            m_OpeningHandWeightMultiplier;
        public float OpeningSelfWeightMultiplier =>
            m_OpeningSelfWeightMultiplier;
        public int CatchUpAdaptationPickInterval =>
            Mathf.Max(1, m_CatchUpAdaptationPickInterval);

        private void OnValidate()
        {
            m_OpeningHandBiasRounds = Mathf.Max(0, m_OpeningHandBiasRounds);
            m_OpeningHandWeightMultiplier = Mathf.Max(0f,
                m_OpeningHandWeightMultiplier);
            m_OpeningSelfWeightMultiplier = Mathf.Max(0f,
                m_OpeningSelfWeightMultiplier);
            m_CatchUpAdaptationPickInterval = Mathf.Max(1,
                m_CatchUpAdaptationPickInterval);
        }
    }
}
