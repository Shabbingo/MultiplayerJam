using UnityEngine;
using Unity.Mathematics;

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
        [Min(0f)] public float WireGuidedTurnSpeed;
        [Range(0f, 0.5f)] public float ReloadTimeReductionPerStack;
        [Min(0)] public int AmmoPerStack;

        public static ModifierGameplayTuning Default => new()
        {
            DamagePerStack = 0.1f,
            ExplosionRadiusPerStack = 1f,
            ScatterPelletCount = 10,
            ScatterHalfAngleDegrees = 7.5f,
            SlowPercent = 0.5f,
            SlowDuration = 3f,
            AddedHomingTurnSpeed = 15f,
            WireGuidedTurnSpeed = 120f,
            ReloadTimeReductionPerStack = 0.15f,
            AmmoPerStack = 10
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
        public readonly bool TracksPlayers;
        public readonly bool TracksAim;
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
            bool isHoming, bool tracksPlayers, bool tracksAim, float homingTurnSpeed,
            bool hasExplosion,
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
            TracksPlayers = tracksPlayers;
            TracksAim = tracksAim;
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
            int homingStacks = modifiers.GetStacks(CardModifierKind.HomingProjectiles, target);
            int wireGuidedStacks = modifiers.GetStacks(CardModifierKind.WireGuided, target);
            int reloadStacks = modifiers.GetStacks(CardModifierKind.ReloadSpeed, target);
            int ammoStacks = modifiers.GetStacks(CardModifierKind.MagazineCapacity, target);
            float damage = source.Damage * (1f + damageStacks * Mathf.Max(0f, tuning.DamagePerStack));
            float baseExplosionRadius = source.Behavior == ProjectileBehavior.AreaOfEffect
                ? Mathf.Max(0f, source.AoeRadius)
                : 0f;
            float explosionRadius = baseExplosionRadius + explosionStacks *
                Mathf.Max(0f, tuning.ExplosionRadiusPerStack);
            bool tracksPlayers = homingStacks > 0;
            bool tracksAim = wireGuidedStacks > 0;
            bool isHoming = source.IsGuidedProjectile || tracksPlayers || tracksAim;
            // Once the Homing card is present it owns player-tracking strength for
            // either hand. Do not add the right weapon's strong live-aim guidance
            // rate on top, otherwise its first Homing stack becomes nearly perfect.
            float homingTurnSpeed = tracksPlayers
                ? homingStacks * Mathf.Max(0f, tuning.AddedHomingTurnSpeed)
                : tracksAim
                    ? Mathf.Max(0f, tuning.WireGuidedTurnSpeed)
                : source.IsGuidedProjectile
                    ? Mathf.Max(0f, source.GuidedTurnSpeed)
                    : 0f;
            bool appliesSlow = modifiers.GetStacks(CardModifierKind.SlowTarget, target) > 0;
            float reloadMultiplier = Mathf.Max(0.25f,
                1f - reloadStacks * Mathf.Clamp(tuning.ReloadTimeReductionPerStack, 0f, 0.5f));
            int magazineSize = Mathf.Max(1, source.MagazineSize +
                ammoStacks * Mathf.Max(0, tuning.AmmoPerStack));

            return new RuntimeWeaponProfile(
                source,
                hand,
                scatter ? WeaponFireMode.Scatter : WeaponFireMode.Standard,
                damage,
                Mathf.Max(0.001f, source.CooldownInMs),
                Mathf.Max(0f, source.ProjectileReleaseDelay),
                magazineSize,
                Mathf.Max(0f, source.ReloadTime * reloadMultiplier),
                Mathf.Max(0f, source.ProjectileSpeed),
                scatter ? Mathf.Max(1, tuning.ScatterPelletCount) : 1,
                scatter ? Mathf.Max(0f, tuning.ScatterHalfAngleDegrees) : 0f,
                isHoming,
                tracksPlayers,
                tracksAim,
                homingTurnSpeed,
                explosionRadius > 0f,
                explosionRadius,
                appliesSlow,
                appliesSlow ? Mathf.Clamp01(tuning.SlowPercent) : 0f,
                appliesSlow ? Mathf.Max(0f, tuning.SlowDuration) : 0f);
        }

        public static Vector3 ApplyScatterDirection(Vector3 forward,
            float halfAngleDegrees, uint shotTick, int shooterNetworkId,
            int pelletIndex)
        {
            Vector3 normalizedForward = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : Vector3.forward;
            float maximumAngle = Mathf.Max(0f, halfAngleDegrees);
            if (maximumAngle <= 0f)
            {
                return normalizedForward;
            }

            uint seed = math.hash(new uint4(
                shotTick,
                unchecked((uint)shooterNetworkId),
                unchecked((uint)pelletIndex),
                0x9E3779B9u)) | 1u;
            var random = Unity.Mathematics.Random.CreateFromIndex(seed);
            float coneRadius = Mathf.Sqrt(random.NextFloat());
            float angle = Mathf.Deg2Rad * maximumAngle * coneRadius;
            float azimuth = random.NextFloat(0f, Mathf.PI * 2f);
            Vector3 referenceUp = Mathf.Abs(Vector3.Dot(normalizedForward, Vector3.up)) > 0.99f
                ? Vector3.right
                : Vector3.up;
            Vector3 right = Vector3.Cross(referenceUp, normalizedForward).normalized;
            Vector3 up = Vector3.Cross(normalizedForward, right).normalized;
            Vector3 radial = right * Mathf.Cos(azimuth) + up * Mathf.Sin(azimuth);
            return (normalizedForward * Mathf.Cos(angle) + radial * Mathf.Sin(angle)).normalized;
        }
    }
}
