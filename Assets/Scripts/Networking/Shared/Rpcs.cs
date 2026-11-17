using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{

    /// <summary>
    /// Client to server to request joinning current session. 
    /// </summary>
    public struct ClientJoinRequestRpc : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
        public FixedString64Bytes BuildVersion;
        public int GameProtocolVersion;
        public int CharacterIndex;
    }

    /// <summary>
    /// A reliable, human-readable rejection sent before the server closes a
    /// connection. This covers full servers, duplicate names, version checks,
    /// and administrator kicks.
    /// </summary>
    public struct ServerDisconnectNoticeRpc : IRpcCommand
    {
        public FixedString128Bytes Reason;
    }

    public static class NetworkCompatibility
    {
        // Increment this when gameplay/network rules change in a way that should
        // prevent older builds from joining, even if their NetCode schema matches.
        public const int GameProtocolVersion = 1;
    }

    /// <summary>
    /// Add this to a server connection entity to kick it with a supplied reason.
    /// <see cref="ServerConnectionActions.Kick"/> is the convenient entry point.
    /// </summary>
    public struct ServerKickRequest : IComponentData
    {
        public FixedString128Bytes Reason;
    }

    public struct PendingServerDisconnect : IComponentData
    {
        public float SecondsRemaining;
    }

    public static class ServerConnectionActions
    {
        public static void Kick(EntityManager entityManager, Entity connection,
            string reason)
        {
            if (!entityManager.Exists(connection))
            {
                Debug.LogWarning("[Server] Ignored kick request for a missing connection entity.");
                return;
            }

            var request = new ServerKickRequest();
            request.Reason.CopyFromTruncated(string.IsNullOrWhiteSpace(reason)
                ? "Kicked by server"
                : reason.Trim());

            if (entityManager.HasComponent<ServerKickRequest>(connection))
                entityManager.SetComponentData(connection, request);
            else
                entityManager.AddComponentData(connection, request);
        }
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

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NetcodeClientConnectionSystem))]
    public partial struct ServerDisconnectNoticeReceiveSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            bool returnToMenu = false;

            foreach (var (notice, entity) in
                     SystemAPI.Query<RefRO<ServerDisconnectNoticeRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                string reason = notice.ValueRO.Reason.ToString();
                ConnectionSettings.Instance.ShowDisconnectMessage(
                    string.IsNullOrWhiteSpace(reason) ? "Disconnected by server" : reason);
                ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
                returnToMenu = true;
                ecb.DestroyEntity(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            if (returnToMenu && GameManager.Instance != null)
            {
                GameManager.Instance.ReturnToMainMenuAsync();
            }
        }
    }
}
