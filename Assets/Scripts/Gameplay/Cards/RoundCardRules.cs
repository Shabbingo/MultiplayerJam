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

        [Header("Early Draft Weighting")]
        [SerializeField, Min(0), Tooltip("Number of opening rounds in which Foundational cards receive an offer-weight bonus.")]
        private int m_FoundationalBoostRounds = 3;
        [SerializeField, Min(1f), Tooltip("Foundational offer-weight multiplier in round one. It smoothly returns to 1 after the boost rounds.")]
        private float m_FoundationalStartingWeightMultiplier = 3f;

        [Header("Alternating Hand Drafts")]
        [SerializeField, Min(1), Tooltip("First round where self-targeted cards begin entering offers. Earlier rounds contain weapon-hand cards only.")]
        private int m_SelfCardRampStartRound = 3;
        [SerializeField, Min(2), Tooltip("Round where the late-game hand/self offer weighting reaches its maximum.")]
        private int m_SelfCardFullWeightRound = 10;
        [SerializeField, Min(0f), Tooltip("Offer-weight multiplier for self cards before the ramp begins. Set to 0 for hand-only opening rounds.")]
        private float m_SelfCardStartingWeightMultiplier;
        [SerializeField, Min(0f), Tooltip("Offer-weight multiplier for self cards at the end of the ramp.")]
        private float m_SelfCardLateWeightMultiplier = 3f;
        [SerializeField, Min(0f), Tooltip("Offer-weight multiplier for left/right weapon cards at the end of the ramp.")]
        private float m_HandCardLateWeightMultiplier = 0.5f;
        [SerializeField, Min(1), Tooltip("Every Nth late-join catch-up pick may be applied to either hand instead of only the round hand.")]
        private int m_CatchUpAdaptationPickInterval = 3;

        public int RoundWinsToWin => m_RoundWinsToWin;
        public int CardsPerOffer => m_CardsPerOffer;
        public float SecondsPerDraftTurn => m_SecondsPerDraftTurn;
        public float RoundStartDelay => m_RoundStartDelay;
        public int FoundationalBoostRounds => m_FoundationalBoostRounds;
        public float FoundationalStartingWeightMultiplier =>
            m_FoundationalStartingWeightMultiplier;
        public int SelfCardRampStartRound => m_SelfCardRampStartRound;
        public int SelfCardFullWeightRound => m_SelfCardFullWeightRound;
        public float SelfCardStartingWeightMultiplier =>
            m_SelfCardStartingWeightMultiplier;
        public float SelfCardLateWeightMultiplier => m_SelfCardLateWeightMultiplier;
        public float HandCardLateWeightMultiplier => m_HandCardLateWeightMultiplier;
        public int CatchUpAdaptationPickInterval =>
            Mathf.Max(1, m_CatchUpAdaptationPickInterval);

        private void OnValidate()
        {
            m_SelfCardRampStartRound = Mathf.Max(1, m_SelfCardRampStartRound);
            m_SelfCardFullWeightRound = Mathf.Max(
                m_SelfCardRampStartRound + 1, m_SelfCardFullWeightRound);
            m_SelfCardStartingWeightMultiplier = Mathf.Max(0f,
                m_SelfCardStartingWeightMultiplier);
            m_SelfCardLateWeightMultiplier = Mathf.Max(0f,
                m_SelfCardLateWeightMultiplier);
            m_HandCardLateWeightMultiplier = Mathf.Max(0f,
                m_HandCardLateWeightMultiplier);
            m_CatchUpAdaptationPickInterval = Mathf.Max(1,
                m_CatchUpAdaptationPickInterval);
        }
    }
}
