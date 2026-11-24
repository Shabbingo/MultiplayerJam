using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

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
        private readonly Dictionary<int, int> m_CardPickCounts = new();

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
            PlayerModifierStacks previous = updated;
            if (!updated.TryAdd(card, target))
            {
                return false;
            }

            m_ByNetworkId[networkId] = updated;
            m_CardPickCounts.TryGetValue(networkId, out int pickedCards);
            m_CardPickCounts[networkId] = pickedCards + 1;
            ApplyToLivingPlayer(networkId, in previous, in updated);
            return true;
        }

        public bool TryRemoveCard(int networkId, CardModifierDefinition card,
            ModifierTarget target, out PlayerModifierStacks updated)
        {
            updated = GetOrCreate(networkId);
            PlayerModifierStacks previous = updated;
            if (!updated.TryRemove(card, target))
            {
                return false;
            }

            m_ByNetworkId[networkId] = updated;
            if (m_CardPickCounts.TryGetValue(networkId, out int pickedCards))
            {
                m_CardPickCounts[networkId] = Mathf.Max(0, pickedCards - 1);
            }
            ApplyToLivingPlayer(networkId, in previous, in updated);
            return true;
        }

        public int GetCardPickCount(int networkId)
        {
            return m_CardPickCounts.TryGetValue(networkId, out int count)
                ? count
                : 0;
        }

        public void Remove(int networkId)
        {
            m_ByNetworkId.Remove(networkId);
            m_CardPickCounts.Remove(networkId);
        }

        public void ClearMatch()
        {
            m_ByNetworkId.Clear();
            m_CardPickCounts.Clear();
        }

        private void ApplyToLivingPlayer(int networkId,
            in PlayerModifierStacks previousModifiers,
            in PlayerModifierStacks modifiers)
        {
            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadWrite<PlayerModifierStacks>(),
                ComponentType.ReadWrite<PredictedPlayerGhost>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                if (EntityManager.GetComponentData<GhostOwner>(entity).NetworkId == networkId)
                {
                    var player = EntityManager.GetComponentData<PredictedPlayerGhost>(
                        entity);
                    ReconcileMagazineCapacity(ref player, in previousModifiers,
                        in modifiers);
                    EntityManager.SetComponentData(entity, player);
                    EntityManager.SetComponentData(entity, modifiers);
                    return;
                }
            }
        }

        private static void ReconcileMagazineCapacity(
            ref PredictedPlayerGhost player,
            in PlayerModifierStacks previousModifiers,
            in PlayerModifierStacks currentModifiers)
        {
            if (WeaponManager.Instance == null)
            {
                return;
            }

            ReconcileWeaponMagazine(ref player, WeaponManager.PrimaryWeaponId,
                in previousModifiers, in currentModifiers);
            ReconcileWeaponMagazine(ref player, WeaponManager.SecondaryWeaponId,
                in previousModifiers, in currentModifiers);
        }

        private static void ReconcileWeaponMagazine(
            ref PredictedPlayerGhost player, uint weaponId,
            in PlayerModifierStacks previousModifiers,
            in PlayerModifierStacks currentModifiers)
        {
            int previousCapacity = WeaponManager.Instance.ResolveRuntimeProfile(
                weaponId, in previousModifiers).MagazineSize;
            int currentCapacity = WeaponManager.Instance.ResolveRuntimeProfile(
                weaponId, in currentModifiers).MagazineSize;
            int capacityDelta = currentCapacity - previousCapacity;
            if (capacityDelta == 0)
            {
                return;
            }

            if (weaponId == WeaponManager.SecondaryWeaponId)
            {
                player.ControllerState.SecondaryWeaponAmmo = Mathf.Clamp(
                    player.ControllerState.SecondaryWeaponAmmo + capacityDelta,
                    0, currentCapacity);
            }
            else
            {
                player.ControllerState.PrimaryWeaponAmmo = Mathf.Clamp(
                    player.ControllerState.PrimaryWeaponAmmo + capacityDelta,
                    0, currentCapacity);
            }

            if (player.EquippedWeaponID == weaponId)
            {
                player.CurrentAmmo = Mathf.Clamp(player.CurrentAmmo + capacityDelta,
                    0, currentCapacity);
            }
        }
    }
}
