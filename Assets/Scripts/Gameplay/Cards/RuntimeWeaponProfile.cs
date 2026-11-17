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
        public float ExplosionRadiusPerStack;
        [Min(1)] public int ScatterPelletCount;
        [Min(0f)] public float ScatterHalfAngleDegrees;
        [Range(0f, 1f)] public float SlowPercent;
        [Min(0f)] public float SlowDuration;
        [Min(0f)] public float AddedHomingTurnSpeed;
        [Min(0f)] public float WireGuidedTurnSpeed;
        [Range(0f, 0.5f)] public float ReloadTimeReductionPerStack;
        [Min(0)] public int AmmoPerStack;
        [Min(0f)] public float BleedDamagePerSecond;
        [Min(0f)] public float BleedDuration;
        [Range(0f, 1f)] public float LifeStealPercent;
        [Min(0f)] public float SlowReloadMultiplier;
        [Min(0f)] public float AmmoReloadMultiplier;
        public int ScatterMagazineDelta;
        [Min(0f)] public float BulletSizeDamagePerStack;
        public float BulletSizeScalePerStack;
        [Min(0f)] public float BulletSpeedMultiplierPerStack;
        [Min(0f)] public float BulletSpeedScaleMultiplier;
        [Min(0f)] public float BulletSpeedExplosionMultiplier;
        [Min(0f)] public float LifeSiphonDamageMultiplier;
        [Min(0f)] public float BulletGrowthPerMetre;
        [Range(0f, 1f)] public float BounceVelocityRetention;
        [Min(0f)] public float BounceCountPerStack;
        [Range(0.01f, 1f)] public float MinimumReloadMultiplier;
        [Range(0f, 1f)] public float WallDamageRetention;
        [Min(0f)] public float RapidFireDamageMultiplier;
        [Min(0f)] public float RapidFireIntervalMultiplier;
        public int RapidFireMagazineDelta;
        [Min(0f)] public float GravityWellPullPerStack;
        [Min(0f)] public float LastWordDamagePerStack;
        [Min(0f)] public float GrenadeSpeedMultiplier;
        [Min(0f)] public float GrenadeExplosionRadius;
        [Min(0f)] public float LobUpwardSpeedPerStack;
        [Min(0f)] public float ProjectileGravityPerStack;

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
            AmmoPerStack = 2,
            BleedDamagePerSecond = 10f,
            BleedDuration = 3f,
            LifeStealPercent = 0.5f,
            SlowReloadMultiplier = 1.25f,
            AmmoReloadMultiplier = 1.15f,
            ScatterMagazineDelta = -1,
            BulletSizeDamagePerStack = 0.1f,
            BulletSizeScalePerStack = 0.5f,
            BulletSpeedMultiplierPerStack = 1.3f,
            BulletSpeedScaleMultiplier = 0.8f,
            BulletSpeedExplosionMultiplier = 0.5f,
            LifeSiphonDamageMultiplier = 0.75f,
            BulletGrowthPerMetre = 0.01f,
            BounceVelocityRetention = 1f,
            BounceCountPerStack = 1f,
            MinimumReloadMultiplier = 0.25f,
            WallDamageRetention = 0.4f,
            RapidFireDamageMultiplier = 0.3f,
            RapidFireIntervalMultiplier = 0.35f,
            RapidFireMagazineDelta = 0,
            GravityWellPullPerStack = 8f,
            LastWordDamagePerStack = 0.2f,
            GrenadeSpeedMultiplier = 0.45f,
            GrenadeExplosionRadius = 2f,
            LobUpwardSpeedPerStack = 8f,
            ProjectileGravityPerStack = 20f
        };
    }

    /// <summary>
    /// A resolved snapshot. Base ScriptableObjects remain immutable and can be shared
    /// by every player; gameplay consumes this per-player result instead.
    /// </summary>
    public readonly struct SizeScaledWeaponOutput
    {
        public readonly float SignedSizeDelta;
        public readonly float ExplosionRadius;
        public readonly float BleedDamagePerSecond;
        public readonly float ProjectileScale;
        public readonly float GravityWellPullStrength;

        public SizeScaledWeaponOutput(float signedSizeDelta,
            float explosionRadius, float bleedDamagePerSecond,
            float projectileScale, float gravityWellPullStrength)
        {
            SignedSizeDelta = signedSizeDelta;
            ExplosionRadius = explosionRadius;
            BleedDamagePerSecond = bleedDamagePerSecond;
            ProjectileScale = projectileScale;
            GravityWellPullStrength = gravityWellPullStrength;
        }
    }

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
        public readonly int MaxBounces;
        public readonly float BleedDamagePerSecond;
        public readonly float BleedDuration;
        public readonly float LifeStealPercent;
        public readonly float ProjectileScale;
        public readonly bool GrowsOverDistance;
        public readonly float GrowthPerMetre;
        public readonly float BounceVelocityRetention;
        public readonly int WallPenetrations;
        public readonly float WallDamageRetention;
        public readonly float GravityWellPullStrength;
        public readonly float LastWordDamageMultiplier;
        public readonly bool IsLobbedProjectile;
        public readonly float LobUpwardSpeed;
        public readonly float ProjectileGravity;

        public float SignedSizeDelta => Hand == WeaponHand.Right
            ? Mathf.Abs(Damage)
            : -Mathf.Abs(Damage);

        /// <summary>
        /// Snapshots the physical output of a shot at the shooter's current size.
        /// Card modifiers are already resolved into this profile, so size is the
        /// final baseline multiplier and later resizing cannot alter the shot.
        /// Timing values such as speed, fire interval, reload and duration are not
        /// physical output and intentionally remain unchanged.
        /// </summary>
        public SizeScaledWeaponOutput CreateSizeScaledOutput(
            float playerSizeMultiplier, float explosionSizeMultiplier)
        {
            float sizeMultiplier = Mathf.Max(0.01f, playerSizeMultiplier);
            float aoeMultiplier = Mathf.Max(0.01f, explosionSizeMultiplier);
            return new SizeScaledWeaponOutput(
                SignedSizeDelta * sizeMultiplier,
                ExplosionRadius * aoeMultiplier,
                BleedDamagePerSecond * sizeMultiplier,
                ProjectileScale * sizeMultiplier,
                GravityWellPullStrength * sizeMultiplier);
        }

        public RuntimeWeaponProfile(WeaponData sourceProfile, WeaponHand hand,
            WeaponFireMode fireMode, float damage, float fireInterval,
            float projectileReleaseDelay, int magazineSize, float reloadTime,
            float projectileSpeed, int pelletCount, float scatterHalfAngleDegrees,
            bool isHoming, bool tracksPlayers, bool tracksAim, float homingTurnSpeed,
            bool hasExplosion,
            float explosionRadius, bool appliesSlow, float slowPercent,
            float slowDuration, int maxBounces, float bleedDamagePerSecond,
            float bleedDuration, float lifeStealPercent, float projectileScale,
            bool growsOverDistance, float growthPerMetre,
            float bounceVelocityRetention, int wallPenetrations,
            float wallDamageRetention, float gravityWellPullStrength,
            float lastWordDamageMultiplier, bool isLobbedProjectile,
            float lobUpwardSpeed, float projectileGravity)
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
            MaxBounces = maxBounces;
            BleedDamagePerSecond = bleedDamagePerSecond;
            BleedDuration = bleedDuration;
            LifeStealPercent = lifeStealPercent;
            ProjectileScale = projectileScale;
            GrowsOverDistance = growsOverDistance;
            GrowthPerMetre = growthPerMetre;
            BounceVelocityRetention = bounceVelocityRetention;
            WallPenetrations = wallPenetrations;
            WallDamageRetention = wallDamageRetention;
            GravityWellPullStrength = gravityWellPullStrength;
            LastWordDamageMultiplier = lastWordDamageMultiplier;
            IsLobbedProjectile = isLobbedProjectile;
            LobUpwardSpeed = lobUpwardSpeed;
            ProjectileGravity = projectileGravity;
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
            int scatterStacks = modifiers.GetStacks(CardModifierKind.ScatterWeapon, target);
            bool scatter = scatterStacks > 0;
            WeaponData source = scatter && scatterProfile != null
                ? scatterProfile
                : baseProfile;
            int damageStacks = modifiers.GetStacks(CardModifierKind.DamagePercent, target);
            int explosionStacks = modifiers.GetStacks(CardModifierKind.ExplosionRadius, target);
            int homingStacks = modifiers.GetStacks(CardModifierKind.HomingProjectiles, target);
            int wireGuidedStacks = modifiers.GetStacks(CardModifierKind.WireGuided, target);
            int reloadStacks = modifiers.GetStacks(CardModifierKind.ReloadSpeed, target);
            int ammoStacks = modifiers.GetStacks(CardModifierKind.MagazineCapacity, target);
            int bleedStacks = modifiers.GetStacks(CardModifierKind.Bleed, target);
            int bounceStacks = modifiers.GetStacks(CardModifierKind.Bounce, target);
            int siphonStacks = modifiers.GetStacks(CardModifierKind.LifeSiphon, target);
            int armourPiercingStacks = modifiers.GetStacks(
                CardModifierKind.ArmourPiercing, target);
            int rapidFireStacks = modifiers.GetStacks(CardModifierKind.RapidFire,
                target);
            int gravityWellStacks = modifiers.GetStacks(CardModifierKind.GravityWell,
                target);
            int lastWordStacks = modifiers.GetStacks(CardModifierKind.LastWord,
                target);
            int grenadeLauncherStacks = modifiers.GetStacks(
                CardModifierKind.GrenadeLauncher, target);
            int bulletSizeStacks = modifiers.GetStacks(CardModifierKind.BulletSize,
                ModifierTarget.BothHands);
            int bulletSpeedStacks = modifiers.GetStacks(CardModifierKind.BulletSpeed,
                ModifierTarget.BothHands);
            int growthStacks = modifiers.GetStacks(CardModifierKind.BulletGrowth,
                ModifierTarget.BothHands);
            bool growsOverDistance = growthStacks > 0;
            float damage = bleedStacks > 0
                ? 0f
                : source.Damage * (1f + damageStacks * Mathf.Max(0f, tuning.DamagePerStack));
            damage *= 1f + bulletSizeStacks * Mathf.Max(0f,
                tuning.BulletSizeDamagePerStack);
            if (siphonStacks > 0)
            {
                // Every Siphon stack increases the healing ceiling, but its damage
                // drawback compounds so the weapon becomes a dedicated sustain tool.
                damage *= Mathf.Pow(Mathf.Max(0f,
                    tuning.LifeSiphonDamageMultiplier), siphonStacks);
            }
            if (rapidFireStacks > 0)
            {
                damage *= Mathf.Pow(Mathf.Max(0f,
                    tuning.RapidFireDamageMultiplier), rapidFireStacks);
            }
            float baseExplosionRadius = source.Behavior == ProjectileBehavior.AreaOfEffect
                ? Mathf.Max(0f, source.AoeRadius)
                : 0f;
            float explosionRadius = Mathf.Max(0f,
                baseExplosionRadius + explosionStacks *
                tuning.ExplosionRadiusPerStack);
            bool tracksPlayers = homingStacks > 0;
            bool tracksAim = wireGuidedStacks > 0;
            bool isHoming = source.IsGuidedProjectile || tracksPlayers || tracksAim;
            // Once the Homing card is present it owns player-tracking strength for
            // either hand. Do not add the right weapon's strong live-aim guidance
            // rate on top, otherwise its first Homing stack becomes nearly perfect.
            float homingTurnSpeed = tracksPlayers
                ? homingStacks * Mathf.Max(0f, tuning.AddedHomingTurnSpeed)
                : tracksAim
                    ? wireGuidedStacks * Mathf.Max(0f,
                        tuning.WireGuidedTurnSpeed)
                : source.IsGuidedProjectile
                    ? Mathf.Max(0f, source.GuidedTurnSpeed)
                    : 0f;
            int slowStacks = modifiers.GetStacks(CardModifierKind.SlowTarget, target);
            bool appliesSlow = slowStacks > 0;
            float slowPercent = appliesSlow
                ? 1f - Mathf.Pow(1f - Mathf.Clamp01(tuning.SlowPercent), slowStacks)
                : 0f;
            float reloadMultiplier = Mathf.Max(tuning.MinimumReloadMultiplier,
                1f - reloadStacks * Mathf.Clamp(tuning.ReloadTimeReductionPerStack, 0f, 0.5f));
            if (appliesSlow)
            {
                reloadMultiplier *= Mathf.Pow(
                    Mathf.Max(0f, tuning.SlowReloadMultiplier), slowStacks);
            }
            reloadMultiplier *= Mathf.Pow(Mathf.Max(0f, tuning.AmmoReloadMultiplier),
                ammoStacks);
            int magazineSize = Mathf.Max(1, source.MagazineSize +
                ammoStacks * Mathf.Max(0, tuning.AmmoPerStack));
            if (scatter) magazineSize = Mathf.Max(1,
                magazineSize + scatterStacks * tuning.ScatterMagazineDelta);
            magazineSize = Mathf.Max(1, magazineSize +
                rapidFireStacks * tuning.RapidFireMagazineDelta);
            int pelletCount = scatter
                ? 1 + scatterStacks * Mathf.Max(0, tuning.ScatterPelletCount - 1)
                : 1;
            float scatterHalfAngle = scatter
                ? Mathf.Max(0f, tuning.ScatterHalfAngleDegrees) *
                  Mathf.Sqrt(scatterStacks)
                : 0f;
            float projectileScale = (1f + bulletSizeStacks *
                                    tuning.BulletSizeScalePerStack) *
                                    Mathf.Pow(tuning.BulletSpeedScaleMultiplier,
                                        bulletSpeedStacks);
            float projectileSpeed = source.ProjectileSpeed *
                                    Mathf.Pow(tuning.BulletSpeedMultiplierPerStack,
                                        bulletSpeedStacks);
            if (grenadeLauncherStacks > 0)
            {
                // The conversion happens once; additional stacks shape the arc
                // without making the projectile exponentially slower.
                projectileSpeed *= Mathf.Max(0f,
                    tuning.GrenadeSpeedMultiplier);
                // Preserve grenade behaviour when combined with a source-changing
                // card such as Scatter, whose base projectiles are not explosive.
                explosionRadius = Mathf.Max(explosionRadius,
                    tuning.GrenadeExplosionRadius);
            }
            explosionRadius *= Mathf.Pow(tuning.BulletSpeedExplosionMultiplier,
                bulletSpeedStacks);

            bool isLobbedProjectile = source.IsLobbedProjectile ||
                                      grenadeLauncherStacks > 0;
            float lobUpwardSpeed = (source.IsLobbedProjectile
                ? Mathf.Max(0f, source.LobUpwardSpeed)
                : 0f) + grenadeLauncherStacks *
                Mathf.Max(0f, tuning.LobUpwardSpeedPerStack);
            float projectileGravity = (source.IsLobbedProjectile
                ? Mathf.Max(0f, source.ProjectileGravity)
                : 0f) + grenadeLauncherStacks *
                Mathf.Max(0f, tuning.ProjectileGravityPerStack);

            return new RuntimeWeaponProfile(
                source,
                hand,
                scatter ? WeaponFireMode.Scatter : WeaponFireMode.Standard,
                damage,
                Mathf.Max(0.001f, source.CooldownInMs *
                    Mathf.Pow(Mathf.Max(0f,
                        tuning.RapidFireIntervalMultiplier), rapidFireStacks)),
                Mathf.Max(0f, source.ProjectileReleaseDelay),
                magazineSize,
                Mathf.Max(0f, source.ReloadTime * reloadMultiplier),
                Mathf.Max(0f, projectileSpeed),
                Mathf.Max(1, pelletCount),
                scatterHalfAngle,
                isHoming,
                tracksPlayers,
                tracksAim,
                homingTurnSpeed,
                explosionRadius > 0f,
                explosionRadius,
                appliesSlow,
                slowPercent,
                appliesSlow ? Mathf.Max(0f, tuning.SlowDuration) : 0f,
                Mathf.Max(0, Mathf.RoundToInt(bounceStacks *
                    tuning.BounceCountPerStack)),
                (hand == WeaponHand.Right ? 1f : -1f) * bleedStacks *
                Mathf.Max(0f, tuning.BleedDamagePerSecond),
                bleedStacks > 0 ? Mathf.Max(0f, tuning.BleedDuration) : 0f,
                siphonStacks > 0
                    ? 1f - Mathf.Pow(1f - Mathf.Clamp01(tuning.LifeStealPercent),
                        siphonStacks)
                    : 0f,
                Mathf.Max(0.1f, projectileScale), growsOverDistance,
                growsOverDistance
                    ? growthStacks * Mathf.Max(0f, tuning.BulletGrowthPerMetre)
                    : 0f,
                Mathf.Clamp01(tuning.BounceVelocityRetention),
                armourPiercingStacks,
                Mathf.Clamp01(tuning.WallDamageRetention),
                gravityWellStacks * Mathf.Max(0f,
                    tuning.GravityWellPullPerStack),
                1f + lastWordStacks * Mathf.Max(0f,
                    tuning.LastWordDamagePerStack),
                isLobbedProjectile,
                lobUpwardSpeed,
                projectileGravity);
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
