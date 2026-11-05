using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{

    /// <summary>
    /// Client to server to request joinning current session. 
    /// </summary>
    public struct ClientJoinRequestRpc : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
        public int CharacterIndex;
    }

    /// <summary>
    /// Client request a death + respawn from the server. For testing purposes.
    /// </summary>
    public struct ClientRequestRespawnRpc : IRpcCommand
    {
    }

    /// <summary>
    /// Server-to-client notification for the local respawn overlay. This is
    /// targeted at the connection that died so other players are unaffected.
    /// </summary>
    public struct PlayerRespawnStateRpc : IRpcCommand
    {
        public byte IsRespawning;
        public float Duration;
    }

    public static class ClientRespawnState
    {
        public static bool IsRespawning { get; private set; }
        public static float Duration { get; private set; } = 5f;
        public static uint Sequence { get; private set; }

        public static void Set(bool isRespawning, float duration)
        {
            IsRespawning = isRespawning;
            Duration = duration > 0f ? duration : 5f;
            Sequence++;
        }

        public static void Clear()
        {
            IsRespawning = false;
            Duration = 5f;
            Sequence++;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ClientRespawnStateReceiveSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            ClientRespawnState.Clear();
        }

        public void OnDestroy(ref SystemState state)
        {
            ClientRespawnState.Clear();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, entity) in
                     SystemAPI.Query<RefRO<PlayerRespawnStateRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                ClientRespawnState.Set(rpc.ValueRO.IsRespawning != 0,
                    rpc.ValueRO.Duration);
                ecb.DestroyEntity(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
