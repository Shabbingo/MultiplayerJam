using UnityEngine;

namespace Unity.MP_FPS.Cards
{
    public enum WeaponFireMode : byte
    {
        Standard,
        Scatter
    }

    [System.Serializable]
    public struct ModifierGameplayTuning
    {
        [Min(0f)] public float DamagePerStack;
        [Min(0f)] public float ExplosionRadiusPerStack;
        [Min(1)] public int ScatterPelletCount;
        [Min(0f)] public float ScatterHalfAngleDegrees;
        [Range(0f, 1f)] public float SlowPercent;
        [Min(0f)] public float SlowDuration;
        [Min(0f)] public float AddedHomingTurnSpeed;

        public static ModifierGameplayTuning Default => new()
        {
            DamagePerStack = 0.1f,
            ExplosionRadiusPerStack = 1f,
            ScatterPelletCount = 10,
            ScatterHalfAngleDegrees = 7.5f,
            SlowPercent = 0.2f,
            SlowDuration = 2f,
            AddedHomingTurnSpeed = 120f
        };
    }

    /// <summary>
    /// A resolved snapshot. Base ScriptableObjects remain immutable and can be shared
    /// by every player; gameplay consumes this per-player result instead.
    /// </summary>
    public readonly struct RuntimeWeaponProfile
    {
        public readonly WeaponData SourceProfile;
        public readonly WeaponHand Hand;
        public readonly WeaponFireMode FireMode;
        public readonly float Damage;
        public readonly float FireInterval;
        public readonly float ProjectileReleaseDelay;
        public readonly int MagazineSize;
        public readonly float ReloadTime;
        public readonly float ProjectileSpeed;
        public readonly int PelletCount;
        public readonly float ScatterHalfAngleDegrees;
        public readonly bool IsHoming;
        public readonly float HomingTurnSpeed;
        public readonly bool HasExplosion;
        public readonly float ExplosionRadius;
        public readonly bool AppliesSlow;
        public readonly float SlowPercent;
        public readonly float SlowDuration;

        public float SignedSizeDelta => Hand == WeaponHand.Right
            ? Mathf.Abs(Damage)
            : -Mathf.Abs(Damage);

        public RuntimeWeaponProfile(WeaponData sourceProfile, WeaponHand hand,
            WeaponFireMode fireMode, float damage, float fireInterval,
            float projectileReleaseDelay, int magazineSize, float reloadTime,
            float projectileSpeed, int pelletCount, float scatterHalfAngleDegrees,
            bool isHoming, float homingTurnSpeed, bool hasExplosion,
            float explosionRadius, bool appliesSlow, float slowPercent,
            float slowDuration)
        {
            SourceProfile = sourceProfile;
            Hand = hand;
            FireMode = fireMode;
            Damage = damage;
            FireInterval = fireInterval;
            ProjectileReleaseDelay = projectileReleaseDelay;
            MagazineSize = magazineSize;
            ReloadTime = reloadTime;
            ProjectileSpeed = projectileSpeed;
            PelletCount = pelletCount;
            ScatterHalfAngleDegrees = scatterHalfAngleDegrees;
            IsHoming = isHoming;
            HomingTurnSpeed = homingTurnSpeed;
            HasExplosion = hasExplosion;
            ExplosionRadius = explosionRadius;
            AppliesSlow = appliesSlow;
            SlowPercent = slowPercent;
            SlowDuration = slowDuration;
        }
    }

    public static class WeaponModifierResolver
    {
        public static RuntimeWeaponProfile Resolve(WeaponData baseProfile,
            WeaponData scatterProfile, WeaponHand hand,
            in PlayerModifierStacks modifiers,
            in ModifierGameplayTuning tuning)
        {
            if (baseProfile == null)
            {
                return default;
            }

            ModifierTarget target = hand == WeaponHand.Left
                ? ModifierTarget.LeftHand
                : ModifierTarget.RightHand;
            bool scatter = modifiers.GetStacks(CardModifierKind.ScatterWeapon, target) > 0;
            WeaponData source = scatter && scatterProfile != null
                ? scatterProfile
                : baseProfile;
            int damageStacks = modifiers.GetStacks(CardModifierKind.DamagePercent, target);
            int explosionStacks = modifiers.GetStacks(CardModifierKind.ExplosionRadius, target);
            float damage = source.Damage * (1f + damageStacks * Mathf.Max(0f, tuning.DamagePerStack));
            float baseExplosionRadius = source.Behavior == ProjectileBehavior.AreaOfEffect
                ? Mathf.Max(0f, source.AoeRadius)
                : 0f;
            float explosionRadius = baseExplosionRadius + explosionStacks *
                Mathf.Max(0f, tuning.ExplosionRadiusPerStack);
            bool isHoming = source.IsGuidedProjectile ||
                            modifiers.GetStacks(CardModifierKind.HomingProjectiles, target) > 0;
            float homingTurnSpeed = source.IsGuidedProjectile
                ? source.GuidedTurnSpeed
                : Mathf.Max(0f, tuning.AddedHomingTurnSpeed);
            bool appliesSlow = modifiers.GetStacks(CardModifierKind.SlowTarget, target) > 0;

            return new RuntimeWeaponProfile(
                source,
                hand,
                scatter ? WeaponFireMode.Scatter : WeaponFireMode.Standard,
                damage,
                Mathf.Max(0.001f, source.CooldownInMs),
                Mathf.Max(0f, source.ProjectileReleaseDelay),
                Mathf.Max(1, source.MagazineSize),
                Mathf.Max(0f, source.ReloadTime),
                Mathf.Max(0f, source.ProjectileSpeed),
                scatter ? Mathf.Max(1, tuning.ScatterPelletCount) : 1,
                scatter ? Mathf.Max(0f, tuning.ScatterHalfAngleDegrees) : 0f,
                isHoming,
                homingTurnSpeed,
                explosionRadius > 0f,
                explosionRadius,
                appliesSlow,
                appliesSlow ? Mathf.Clamp01(tuning.SlowPercent) : 0f,
                appliesSlow ? Mathf.Max(0f, tuning.SlowDuration) : 0f);
        }
    }
}
