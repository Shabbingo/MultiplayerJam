using UnityEngine;

namespace Unity.MP_FPS
{
    public class WeaponManager : MonoBehaviour
    {
        public const uint PrimaryWeaponId = 0;
        public const uint SecondaryWeaponId = 2;

        public static WeaponManager Instance { get; private set; }

        public struct TemporaryWeaponState
        {
            public uint EquippedWeaponId;
            public int CurrentAmmo;
            public float WeaponCooldown;
            public float ReloadTimer;
            public bool IsReloading;
        }

        public WeaponRegistry WeaponRegistry;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public bool TryEquipWeapon(ref PredictedPlayerGhost player, uint weaponId)
        {
            if (player.EquippedWeaponID == weaponId)
            {
                return false;
            }

            var weaponData = WeaponRegistry.GetWeaponData(weaponId);
            if (weaponData == null)
            {
                Debug.LogError($"Cannot equip unregistered weapon ID {weaponId.ToString()}.");
                return false;
            }

            if (player.EquippedWeaponID == PrimaryWeaponId)
            {
                player.ControllerState.PrimaryWeaponAmmo = player.CurrentAmmo;
            }
            else if (player.EquippedWeaponID == SecondaryWeaponId)
            {
                player.ControllerState.SecondaryWeaponAmmo = player.CurrentAmmo;
                player.SecondaryWeaponCooldown = player.WeaponCooldown;
            }

            player.EquippedWeaponID = weaponId;
            player.CurrentAmmo = weaponId == PrimaryWeaponId
                ? player.ControllerState.PrimaryWeaponAmmo
                : player.ControllerState.SecondaryWeaponAmmo;
            player.WeaponCooldown = weaponId == SecondaryWeaponId
                ? player.SecondaryWeaponCooldown
                : weaponData.CooldownInMs;
            player.ReloadTimer = 0f;
            player.ControllerState.IsReloadingState = false;
            return true;
        }

        public bool TryBeginTemporaryWeapon(ref PredictedPlayerGhost player, uint weaponId,
            out TemporaryWeaponState previousState)
        {
            previousState = new TemporaryWeaponState
            {
                EquippedWeaponId = player.EquippedWeaponID,
                CurrentAmmo = player.CurrentAmmo,
                WeaponCooldown = player.WeaponCooldown,
                ReloadTimer = player.ReloadTimer,
                IsReloading = player.ControllerState.IsReloadingState
            };

            return player.EquippedWeaponID != weaponId &&
                   TryEquipWeapon(ref player, weaponId);
        }

        public void EndTemporaryWeapon(ref PredictedPlayerGhost player,
            in TemporaryWeaponState previousState)
        {
            if (player.EquippedWeaponID == PrimaryWeaponId)
            {
                player.ControllerState.PrimaryWeaponAmmo = player.CurrentAmmo;
            }
            else if (player.EquippedWeaponID == SecondaryWeaponId)
            {
                player.ControllerState.SecondaryWeaponAmmo = player.CurrentAmmo;
            }

            player.EquippedWeaponID = previousState.EquippedWeaponId;
            player.CurrentAmmo = previousState.CurrentAmmo;
            player.WeaponCooldown = previousState.WeaponCooldown;
            player.ReloadTimer = previousState.ReloadTimer;
            player.ControllerState.IsReloadingState = previousState.IsReloading;
        }
    }
}
