using UnityEngine;

using UnityEngine.Serialization;

namespace Unity.MP_FPS
{
    public enum WeaponType
    {
        Hitscan,
        Projectile
    }

    public enum ReticleType
    {
        Cross,
        TCross,
        OpenCircular,
        CircularCross
    }

    public enum PlayerResizeEffect
    {
        None,
        RestoreFullSize,
        Shrink,
        Grow
    }

    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "FPS Sample/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("General")] public string WeaponName = "Assault Rifle";
        public WeaponType Type = WeaponType.Hitscan;
        public ReticleType ReticleType = ReticleType.TCross;

        [Header("Firing Mechanics")] [Tooltip("Shots per second")]
        public float CooldownInMs = 10f;

        [Min(0f)]
        [Tooltip("Delay from starting the firing animation until the shot is released. Used to synchronize projectiles with a hand-cast pose.")]
        public float ProjectileReleaseDelay;

        public float Damage = 15f;

        [Header("Hitscan Properties")] [Tooltip("Max range for raycast-based weapons.")]
        public float HitscanRange = 100f;

        [Header("Ammo & Reloading")] public int MagazineSize = 30;
        public float ReloadTime = 2.0f; // Time in seconds

        [Header("Projectile Properties")] [Tooltip("The ghost prefab for the projectile to be spawned.")]
        public GhostSpawner.GhostReference ProjectileGhostPrefab;

        public GhostSpawner.GhostReference ProjectileHitVfxPrefab;
        public GhostSpawner.GhostReference MuzzleFlashVfxPrefab;
        public SoundDef WeaponFireSfx;
        public SoundDef WeaponReloadSfx;

        [Header("Player Effects")]
        [Tooltip("Optional size effect applied to each living player hit. Restore Full Size temporarily forces a full-size body and hitbox without changing health.")]
        public PlayerResizeEffect ResizeEffect = PlayerResizeEffect.None;

        [Min(0.1f)]
        [Tooltip("How many seconds the resize effect lasts before the player returns to the size permitted by their unchanged health.")]
        public float ResizeEffectDuration = 5f;

        public float PlayerSizeDelta => ResizeEffect switch
        {
            PlayerResizeEffect.RestoreFullSize => 0f,
            PlayerResizeEffect.Grow => Mathf.Abs(Damage),
            _ => -Mathf.Abs(Damage)
        };

        public ProjectileBehavior Behavior = ProjectileBehavior.DirectDamage;
        public float AoeRadius = 5f;
        public float ProjectileSpeed = 30f;

        [Header("Guided Projectile")]
        [Tooltip("Steers the projectile toward the point selected by the firing raycast.")]
        public bool IsGuidedProjectile;

        [Min(0f)]
        [Tooltip("Maximum homing turn rate in degrees per second. Lower values produce wider arcs.")]
        public float GuidedTurnSpeed = 120f;

        [Min(0f)]
        [Tooltip("How quickly the missile approaches Projectile Speed. Use 0 for constant launch speed.")]
        public float GuidedAcceleration = 40f;

        [Min(0f)]
        [Tooltip("Upward velocity applied at launch before guidance curves toward the raycast target.")]
        public float GuidedLaunchUpwardSpeed = 6f;

        [Min(1f)]
        [Tooltip("Maximum distance used by the targeting raycast.")]
        public float GuidedTargetRange = 250f;

        [Header("Grenade Projectile")]
        [Tooltip("Launches this projectile in an arc and allows it to bounce before impact detonation.")]
        public bool IsLobbedProjectile;

        [Min(0f)]
        [Tooltip("Extra upward launch velocity added to the projectile.")]
        public float LobUpwardSpeed = 8f;

        [Min(0f)]
        [Tooltip("Downward acceleration applied to a lobbed projectile.")]
        public float ProjectileGravity = 20f;

        [Min(0)]
        [Tooltip("Number of map-geometry impacts that bounce before the next impact detonates.")]
        public int ProjectileBounces = 1;

        [FormerlySerializedAs("BounceVelocityRetention")]
        [Range(0f, 1f)]
        [Tooltip("Percentage of the projectile's current velocity retained after its first bounce.")]
        public float FirstBounceVelocityRetention = 0.65f;

        [Range(0f, 1f)]
        [Tooltip("Per-bounce velocity retention starting at bounce 2. Element 0 controls bounce 2, element 1 controls bounce 3, and so on. Extra bounces reuse the final value.")]
        public float[] ConsecutiveBounceVelocityRetentions = { 0.65f };

        [Min(0.1f)]
        [Tooltip("Maximum flight time before the projectile automatically detonates.")]
        public float ProjectileFuseTime = 5f;

        public float GetBounceVelocityRetention(int completedBounceCount)
        {
            if (completedBounceCount <= 0)
            {
                return Mathf.Clamp01(FirstBounceVelocityRetention);
            }

            if (ConsecutiveBounceVelocityRetentions == null ||
                ConsecutiveBounceVelocityRetentions.Length == 0)
            {
                return Mathf.Clamp01(FirstBounceVelocityRetention);
            }

            int retentionIndex = Mathf.Min(
                completedBounceCount - 1,
                ConsecutiveBounceVelocityRetentions.Length - 1);
            return Mathf.Clamp01(ConsecutiveBounceVelocityRetentions[retentionIndex]);
        }

    }

    public enum ProjectileBehavior
    {
        DirectDamage,
        AreaOfEffect
    }
}
