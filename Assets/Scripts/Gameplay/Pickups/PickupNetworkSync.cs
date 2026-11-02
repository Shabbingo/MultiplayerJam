using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    public interface INetworkPickupState
    {
        void ApplyNetworkAvailability(bool available);
    }

    public struct PickupAvailabilityRpc : IRpcCommand
    {
        public uint PickupId;
        public byte Available;
    }

    public static class PickupNetworkSync
    {
        private static readonly Dictionary<uint, INetworkPickupState> Pickups = new();
        private static readonly Queue<PickupAvailabilityRpc> ServerBroadcastQueue = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Pickups.Clear();
            ServerBroadcastQueue.Clear();
        }

        public static uint CreateStableId(Transform pickupTransform)
        {
            // Scene path plus sibling-index hierarchy is identical in every player's
            // copy of the scene and remains stable when display names are changed.
            uint hash = 2166136261u;
            AppendHash(ref hash, pickupTransform.gameObject.scene.path);

            var hierarchy = new Stack<int>();
            for (Transform current = pickupTransform; current != null; current = current.parent)
            {
                hierarchy.Push(current.GetSiblingIndex());
            }

            while (hierarchy.Count > 0)
            {
                uint siblingIndex = unchecked((uint)hierarchy.Pop());
                hash = (hash ^ siblingIndex) * 16777619u;
            }

            return hash == 0 ? 1u : hash;
        }

        public static void Register(uint pickupId, INetworkPickupState pickup)
        {
            if (Pickups.TryGetValue(pickupId, out var existing) && !ReferenceEquals(existing, pickup))
            {
                Debug.LogError($"Duplicate network pickup ID {pickupId}. Check scene hierarchy placement.");
                return;
            }

            Pickups[pickupId] = pickup;
        }

        public static void Unregister(uint pickupId, INetworkPickupState pickup)
        {
            if (Pickups.TryGetValue(pickupId, out var existing) && ReferenceEquals(existing, pickup))
            {
                Pickups.Remove(pickupId);
            }
        }

        public static void BroadcastFromServer(uint pickupId, bool available)
        {
            ServerBroadcastQueue.Enqueue(new PickupAvailabilityRpc
            {
                PickupId = pickupId,
                Available = available ? (byte)1 : (byte)0
            });
        }

        internal static bool TryDequeueServerBroadcast(out PickupAvailabilityRpc state)
        {
            return ServerBroadcastQueue.TryDequeue(out state);
        }

        internal static void Apply(in PickupAvailabilityRpc state)
        {
            if (Pickups.TryGetValue(state.PickupId, out var pickup))
            {
                pickup.ApplyNetworkAvailability(state.Available != 0);
            }
        }

        private static void AppendHash(ref uint hash, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                hash = (hash ^ value[i]) * 16777619u;
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PickupStateBroadcastSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            while (PickupNetworkSync.TryDequeueServerBroadcast(out var pickupState))
            {
                Entity rpcEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(rpcEntity, pickupState);
                EntityManager.AddComponentData(rpcEntity, new SendRpcCommandRequest());
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GhostGameObjectUpdateClientSystem))]
    public partial class PickupStateReceiveSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (pickupState, entity) in
                     SystemAPI.Query<RefRO<PickupAvailabilityRpc>>()
                         .WithAll<ReceiveRpcCommandRequest>()
                         .WithEntityAccess())
            {
                PickupNetworkSync.Apply(pickupState.ValueRO);
                commandBuffer.DestroyEntity(entity);
            }

            commandBuffer.Playback(EntityManager);
            commandBuffer.Dispose();
        }
    }
}
