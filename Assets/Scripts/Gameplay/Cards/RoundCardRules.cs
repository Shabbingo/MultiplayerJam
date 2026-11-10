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

        public int RoundWinsToWin => m_RoundWinsToWin;
        public int CardsPerOffer => m_CardsPerOffer;
        public float SecondsPerDraftTurn => m_SecondsPerDraftTurn;
        public float RoundStartDelay => m_RoundStartDelay;
    }
}
