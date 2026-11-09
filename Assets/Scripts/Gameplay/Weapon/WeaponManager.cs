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

        [Header("Card Modifier Profiles")]
        [SerializeField] private Cards.CardModifierCatalog m_CardModifierCatalog;

        [SerializeField, Tooltip("Firing profile used when the Scatter card transforms either hand. Defaults to registered weapon ID 1 when unassigned.")]
        private WeaponData m_ScatterWeaponProfile;

        [SerializeField]
        private Cards.ModifierGameplayTuning m_ModifierTuning = new()
        {
            DamagePerStack = 0.1f,
            ExplosionRadiusPerStack = 1f,
            ScatterPelletCount = 10,
            ScatterHalfAngleDegrees = 7.5f,
            SlowPercent = 0.2f,
            SlowDuration = 2f,
            AddedHomingTurnSpeed = 120f
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public Cards.RuntimeWeaponProfile ResolveRuntimeProfile(uint weaponId,
            in Cards.PlayerModifierStacks modifiers)
        {
            WeaponData baseProfile = WeaponRegistry.GetWeaponData(weaponId);
            WeaponData scatterProfile = m_ScatterWeaponProfile != null
                ? m_ScatterWeaponProfile
                : WeaponRegistry.GetWeaponData(1);
            Cards.WeaponHand hand = weaponId == SecondaryWeaponId
                ? Cards.WeaponHand.Right
                : Cards.WeaponHand.Left;
            Cards.ModifierGameplayTuning tuning = GetModifierTuning();
            return Cards.WeaponModifierResolver.Resolve(baseProfile, scatterProfile, hand,
                in modifiers, in tuning);
        }

        private Cards.ModifierGameplayTuning GetModifierTuning()
        {
            Cards.ModifierGameplayTuning tuning = m_ModifierTuning;
            if (m_CardModifierCatalog == null)
            {
                return tuning;
            }

            var damage = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.DamagePercent);
            var explosion = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.ExplosionRadius);
            var scatter = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.ScatterWeapon);
            var slow = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.SlowTarget);
            if (damage != null) tuning.DamagePerStack = damage.ValuePerStack;
            if (explosion != null) tuning.ExplosionRadiusPerStack = explosion.ValuePerStack;
            if (scatter != null) tuning.ScatterPelletCount = Mathf.Max(1,
                Mathf.RoundToInt(scatter.ValuePerStack));
            if (slow != null) tuning.SlowPercent = slow.ValuePerStack;
            return tuning;
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
            player.ReloadTimer = weaponId == SecondaryWeaponId
                ? player.SecondaryReloadTimer
                : 0f;
            player.ControllerState.IsReloadingState =
                weaponId == SecondaryWeaponId && player.SecondaryIsReloading;
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
                player.SecondaryWeaponCooldown = player.WeaponCooldown;
                player.SecondaryReloadTimer = player.ReloadTimer;
                player.SecondaryIsReloading = player.ControllerState.IsReloadingState;
            }

            player.EquippedWeaponID = previousState.EquippedWeaponId;
            player.CurrentAmmo = previousState.CurrentAmmo;
            player.WeaponCooldown = previousState.WeaponCooldown;
            player.ReloadTimer = previousState.ReloadTimer;
            player.ControllerState.IsReloadingState = previousState.IsReloading;
        }
    }
}
