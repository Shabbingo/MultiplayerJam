using UnityEngine;

namespace Unity.MP_FPS
{
    public class WeaponManager : MonoBehaviour
    {
        public const uint PrimaryWeaponId = 0;
        public const uint ScatterWeaponId = 1;
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

        [SerializeField] private Cards.RoundCardRules m_RoundCardRules;

        [SerializeField, Tooltip("Firing profile used when the Scatter card transforms either hand. Defaults to registered weapon ID 1 when unassigned.")]
        private WeaponData m_ScatterWeaponProfile;

        public Cards.CardModifierCatalog CardModifierCatalog => m_CardModifierCatalog;
        public Cards.RoundCardRules RoundCardRules => m_RoundCardRules;

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

        public uint GetWeaponId(WeaponData weaponData, uint fallbackId)
        {
            if (weaponData == null || WeaponRegistry?.Weapons == null)
            {
                return fallbackId;
            }

            int index = WeaponRegistry.Weapons.IndexOf(weaponData);
            return index >= 0 ? (uint)index : fallbackId;
        }

        private Cards.ModifierGameplayTuning GetModifierTuning()
        {
            Cards.ModifierGameplayTuning tuning = Cards.ModifierGameplayTuning.Default;
            if (m_CardModifierCatalog == null)
            {
                return tuning;
            }

            var damage = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.DamagePercent);
            var explosion = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.ExplosionRadius);
            var scatter = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.ScatterWeapon);
            var slow = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.SlowTarget);
            var homing = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.HomingProjectiles);
            var wireGuided = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.WireGuided);
            var reload = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.ReloadSpeed);
            var ammo = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.MagazineCapacity);
            var bleed = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.Bleed);
            var siphon = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.LifeSiphon);
            var bulletSize = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.BulletSize);
            var bulletSpeed = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.BulletSpeed);
            var bulletGrowth = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.BulletGrowth);
            var bounce = m_CardModifierCatalog.FindByKind(Cards.CardModifierKind.Bounce);
            if (damage != null) tuning.DamagePerStack = damage.ValuePerStack;
            if (explosion != null) tuning.ExplosionRadiusPerStack = explosion.ValuePerStack;
            if (scatter != null)
            {
                tuning.ScatterPelletCount = Mathf.Max(1,
                    Mathf.RoundToInt(scatter.ValuePerStack));
                tuning.ScatterHalfAngleDegrees = scatter.ScatterHalfAngleDegrees;
                tuning.ScatterMagazineDelta = scatter.MagazineDeltaPerStack;
            }
            if (slow != null)
            {
                tuning.SlowPercent = slow.ValuePerStack;
                tuning.SlowDuration = slow.EffectDurationSeconds;
                tuning.SlowReloadMultiplier = slow.ReloadTimeMultiplierPerStack;
            }
            if (homing != null) tuning.AddedHomingTurnSpeed = homing.ValuePerStack;
            if (wireGuided != null) tuning.WireGuidedTurnSpeed = wireGuided.ValuePerStack;
            if (reload != null)
            {
                tuning.ReloadTimeReductionPerStack = reload.ValuePerStack;
                tuning.MinimumReloadMultiplier = reload.MinimumReloadMultiplier;
            }
            if (ammo != null)
            {
                tuning.AmmoPerStack = Mathf.Max(0, Mathf.RoundToInt(ammo.ValuePerStack));
                tuning.AmmoReloadMultiplier = ammo.ReloadTimeMultiplierPerStack;
            }
            if (bleed != null)
            {
                tuning.BleedDamagePerSecond = bleed.ValuePerStack;
                tuning.BleedDuration = bleed.EffectDurationSeconds;
            }
            if (siphon != null)
            {
                tuning.LifeStealPercent = siphon.ValuePerStack;
                tuning.LifeSiphonDamageMultiplier = siphon.BaseDamageMultiplier;
            }
            if (bulletSize != null)
            {
                tuning.BulletSizeDamagePerStack = bulletSize.DamagePercentPerStack;
                tuning.BulletSizeScalePerStack = bulletSize.ProjectileScalePerStack;
            }
            if (bulletSpeed != null)
            {
                tuning.BulletSpeedMultiplierPerStack =
                    bulletSpeed.ProjectileSpeedMultiplierPerStack;
                tuning.BulletSpeedScaleMultiplier =
                    bulletSpeed.ProjectileScaleMultiplierPerStack;
                tuning.BulletSpeedExplosionMultiplier =
                    bulletSpeed.ExplosionRadiusMultiplierPerStack;
            }
            if (bulletGrowth != null)
                tuning.BulletGrowthPerMetre = bulletGrowth.ValuePerStack;
            if (bounce != null)
            {
                tuning.BounceCountPerStack = bounce.ValuePerStack;
                tuning.BounceVelocityRetention = bounce.BounceVelocityRetention;
            }
            return tuning;
        }

        public GhostSpawner.GhostReference GetExplosionImpactVfx(float signedSizeDelta)
        {
            // Runtime profiles preserve the hand in the sign even when a card swaps
            // the underlying weapon profile (for example Scatter). Shrink/left is
            // negative and grow/right is positive, so each hand keeps its own colour.
            uint effectWeaponId = signedSizeDelta > 0f
                ? SecondaryWeaponId
                : PrimaryWeaponId;
            return WeaponRegistry.GetWeaponData(effectWeaponId)?.ProjectileHitVfxPrefab;
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
