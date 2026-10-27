using UnityEngine;

namespace Unity.MP_FPS
{
    public class WeaponManager : MonoBehaviour
    {
        public const uint PrimaryWeaponId = 0;
        public const uint SecondaryWeaponId = 2;

        public static WeaponManager Instance { get; private set; }

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
            }

            player.EquippedWeaponID = weaponId;
            player.CurrentAmmo = weaponId == PrimaryWeaponId
                ? player.ControllerState.PrimaryWeaponAmmo
                : player.ControllerState.SecondaryWeaponAmmo;
            player.WeaponCooldown = weaponData.CooldownInMs;
            player.ReloadTimer = 0f;
            player.ControllerState.IsReloadingState = false;
            return true;
        }
    }
}
