using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS.Cards
{
    /// <summary>
    /// Server-world persistence for a player's accumulated cards. Player ghosts are
    /// destroyed on death, while this store survives until disconnect or match reset.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class PlayerRunModifierStoreSystem : SystemBase
    {
        private readonly Dictionary<int, PlayerModifierStacks> m_ByNetworkId = new();

        protected override void OnUpdate()
        {
        }

        public PlayerModifierStacks GetOrCreate(int networkId)
        {
            if (!m_ByNetworkId.TryGetValue(networkId, out var modifiers))
            {
                modifiers = default;
                m_ByNetworkId.Add(networkId, modifiers);
            }

            return modifiers;
        }

        public void Set(int networkId, in PlayerModifierStacks modifiers)
        {
            m_ByNetworkId[networkId] = modifiers;
        }

        public bool TryApplyCard(int networkId, CardModifierDefinition card,
            ModifierTarget target, out PlayerModifierStacks updated)
        {
            updated = GetOrCreate(networkId);
            if (!updated.TryAdd(card, target))
            {
                return false;
            }

            m_ByNetworkId[networkId] = updated;
            ApplyToLivingPlayer(networkId, in updated);
            return true;
        }

        public void Remove(int networkId)
        {
            m_ByNetworkId.Remove(networkId);
        }

        public void ClearMatch()
        {
            m_ByNetworkId.Clear();
        }

        private void ApplyToLivingPlayer(int networkId,
            in PlayerModifierStacks modifiers)
        {
            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadWrite<PlayerModifierStacks>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                if (EntityManager.GetComponentData<GhostOwner>(entity).NetworkId == networkId)
                {
                    EntityManager.SetComponentData(entity, modifiers);
                    return;
                }
            }
        }
    }
}
