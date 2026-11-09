using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS.Cards
{
    /// <summary>
    /// Compact round/draft state for systems that need an ECS representation. The
    /// leaderboard round director owns live transitions and broadcasts presentation RPCs.
    /// </summary>
    public struct RoundCardState : IComponentData
    {
        [GhostField] public RoundPhase Phase;
        [GhostField] public int CurrentRound;
        [GhostField] public int RoundsPerMatch;
        [GhostField] public int ActiveDraftNetworkId;
        [GhostField] public float DraftSecondsRemaining;
    }
}
