using Unity.Mathematics;
using Unity.MP_FPS.Cards;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Immutable motion data for one projectile pellet. Both prediction and the
    /// authoritative server build this value through ProjectileShotBuilder.
    /// </summary>
    public readonly struct ProjectileShotSpec
    {
        public readonly int OwnerNetworkId;
        public readonly uint ShotTick;
        public readonly uint WeaponId;
        public readonly int PelletIndex;
        public readonly Vector3 Direction;
        public readonly float3 InitialVelocity;
        public readonly float3 Acceleration;

        public ProjectileShotSpec(int ownerNetworkId, uint shotTick, uint weaponId,
            int pelletIndex, Vector3 direction, float3 initialVelocity,
            float3 acceleration)
        {
            OwnerNetworkId = ownerNetworkId;
            ShotTick = shotTick;
            WeaponId = weaponId;
            PelletIndex = pelletIndex;
            Direction = direction;
            InitialVelocity = initialVelocity;
            Acceleration = acceleration;
        }
    }

    /// <summary>
    /// The single launch calculation for projectile weapons. Card modifiers
    /// contribute values to RuntimeWeaponProfile; they do not move the visual
    /// projectile through a separate code path.
    /// </summary>
    public static class ProjectileShotBuilder
    {
        public static Vector3 ResolvePelletDirection(in RuntimeWeaponProfile profile,
            Vector3 aimDirection, uint shotTick, int ownerNetworkId, int pelletIndex)
        {
            Vector3 normalizedAim = aimDirection.sqrMagnitude > 0.000001f
                ? aimDirection.normalized
                : Vector3.forward;
            return Mathf.Max(1, profile.PelletCount) > 1
                ? WeaponModifierResolver.ApplyScatterDirection(normalizedAim,
                    profile.ScatterHalfAngleDegrees, shotTick, ownerNetworkId,
                    pelletIndex)
                : normalizedAim;
        }

        public static ProjectileShotSpec Build(in RuntimeWeaponProfile profile,
            Vector3 launchDirection, uint shotTick, int ownerNetworkId,
            uint weaponId, int pelletIndex)
        {
            return Build(launchDirection, profile.ProjectileSpeed,
                profile.ProjectileDownwardAcceleration, shotTick,
                ownerNetworkId, weaponId, pelletIndex);
        }

        public static ProjectileShotSpec Build(Vector3 launchDirection,
            float projectileSpeed, float downwardAcceleration, uint shotTick,
            int ownerNetworkId, uint weaponId, int pelletIndex)
        {
            Vector3 direction = launchDirection.sqrMagnitude > 0.000001f
                ? launchDirection.normalized
                : Vector3.forward;
            float3 initialVelocity = (float3)direction *
                                     Mathf.Max(0f, projectileSpeed);
            float3 acceleration = float3.zero;

            if (downwardAcceleration > 0f)
            {
                acceleration = new float3(0f,
                    -downwardAcceleration, 0f);
            }

            return new ProjectileShotSpec(ownerNetworkId, shotTick, weaponId,
                pelletIndex, direction, initialVelocity, acceleration);
        }
    }
}
