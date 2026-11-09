using System.Collections.Generic;
using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class Projectile : GhostMonoBehaviour, IUpdateServer, IUpdateClient
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            PredictedProjectiles = new List<PredictedProjectileInfo>();
        }

        public static List<PredictedProjectileInfo> PredictedProjectiles = new List<PredictedProjectileInfo>();
        private int _hitLayerMask;
        private readonly Collider[] _overlapResults = new Collider[16];
        private readonly RaycastHit[] _castResults = new RaycastHit[16];
        private const float ProjectileRadius = 0.2f;
        private const float BounceSurfaceOffset = 0.025f;
        private const float BounceCollisionGrace = 0.04f;
        private const float HardReconciliationDistance = 2f;

        private Vector3 _velocity;
        private bool _motionInitialized;
        private int _bounceCount;
        private float _collisionGraceTimer;

        // Holds the projectile's networked state
        public struct ProjectileData : IComponentData
        {
            [GhostField] public int OwnerNetworkId;
            [GhostField] public uint SpawnTick;
            [GhostField] public uint WeaponID;
            [GhostField] public float3 Position;
            [GhostField] public float3 Velocity;
            [GhostField] public float3 TargetPosition;
            [GhostField] public int BounceCount;
            [GhostField] public float Age;
            [GhostField] public uint MotionRevision;
            [GhostField] public float SignedSizeDelta;
            [GhostField] public byte IsHoming;
            [GhostField] public byte TracksPlayers;
            [GhostField] public byte TracksAim;
            [GhostField] public float HomingTurnSpeed;
            [GhostField] public byte HasExplosion;
            [GhostField] public float ExplosionRadius;
            [GhostField] public byte AppliesSlow;
            [GhostField] public float SlowPercent;
            [GhostField] public float SlowDuration;
        }

        public class PredictedProjectileInfo
        {
            public GameObject Instance;
            public uint SpawnTick;
        }

        private void Awake()
        {
            _hitLayerMask = LayerMask.GetMask("ServerPlayer", "Ground", "Default");
        }

        private void Update()
        {
            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked())
            {
                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);
                if (weaponData != null)
                {
                    SimulateCosmeticProjectile(Time.deltaTime, weaponData);
                }
            }
        }

        public void SetWeaponId(uint weaponId)
        {
            _weaponId = weaponId;
            _motionInitialized = false;
        }

        private float _localTime;
        private uint _weaponId;
        private uint _lastServerMotionRevision;
        private Vector3 _guidedTarget;
        private bool _hasGuidedTarget;
        private bool _runtimeHoming;
        private float _runtimeHomingTurnSpeed;

        public void SetGuidedTarget(Vector3 targetPosition)
        {
            _guidedTarget = targetPosition;
            _hasGuidedTarget = true;
        }

        public void SetRuntimeMotion(bool isHoming, float homingTurnSpeed)
        {
            _runtimeHoming = isHoming;
            _runtimeHomingTurnSpeed = Mathf.Max(0f, homingTurnSpeed);
        }

        public override void OnGhostLinked()
        {
            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            _weaponId = projectileData.WeaponID;
            SetGuidedTarget(projectileData.TargetPosition);
            SetRuntimeMotion(projectileData.IsHoming != 0,
                projectileData.HomingTurnSpeed);
            ApplyServerMotionState(projectileData);
        }

        public void UpdateServer(float deltaTime)
        {
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);
            if (weaponData == null)
            {
                return;
            }

            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            SetRuntimeMotion(projectileData.IsHoming != 0,
                projectileData.HomingTurnSpeed);
            EnsureMotionInitialized(weaponData);
            if (_runtimeHoming)
            {
                var movementSystem = GhostGameObject.World
                    .GetExistingSystemManaged<ServerPlayerMovementSystem>();
                Vector3 liveAimPoint = default;
                bool foundTarget = false;
                if (movementSystem != null && projectileData.TracksPlayers != 0)
                {
                    foundTarget = movementSystem.TryGetNearestOpponentAimPoint(
                            projectileData.OwnerNetworkId,
                            transform.position,
                            weaponData.GuidedTargetRange,
                            out liveAimPoint);
                }
                else if (movementSystem != null && projectileData.TracksAim != 0)
                {
                    foundTarget = movementSystem.TryGetGuidedAimPoint(
                            projectileData.OwnerNetworkId,
                            weaponData.GuidedTargetRange,
                            out liveAimPoint);
                }
                if (foundTarget)
                {
                    SetGuidedTarget(liveAimPoint);
                }
            }
            UpdateVelocity(deltaTime, weaponData);
            _collisionGraceTimer = Mathf.Max(0f, _collisionGraceTimer - deltaTime);
            Vector3 displacement = _velocity * deltaTime;

            // Test the segment the projectile is about to travel. The previous implementation
            // moved first and cast forward from the new position, which skipped close targets.
            if (CheckForCollision(weaponData, displacement))
            {
                return;
            }

            Move(displacement);

            _localTime += deltaTime;
            PublishServerMotionState();
            if (_localTime > weaponData.ProjectileFuseTime)
            {
                DetonateFromFuse(weaponData);
                return;
            }
        }

        private void EnsureMotionInitialized(WeaponData weaponData)
        {
            if (_motionInitialized)
            {
                return;
            }

            _velocity = transform.forward * weaponData.ProjectileSpeed;
            if (_runtimeHoming)
            {
                _velocity += Vector3.up * weaponData.GuidedLaunchUpwardSpeed;
            }
            else if (weaponData.IsLobbedProjectile)
            {
                _velocity += Vector3.up * weaponData.LobUpwardSpeed;
            }

            _motionInitialized = true;
        }

        private void UpdateVelocity(float deltaTime, WeaponData weaponData)
        {
            if (_runtimeHoming && _hasGuidedTarget)
            {
                Vector3 toTarget = _guidedTarget - transform.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    Vector3 currentDirection = _velocity.sqrMagnitude > 0.0001f
                        ? _velocity.normalized
                        : transform.forward;
                    Vector3 steeredDirection = Vector3.RotateTowards(
                        currentDirection,
                        toTarget.normalized,
                        Mathf.Deg2Rad * Mathf.Max(0f, _runtimeHomingTurnSpeed) * deltaTime,
                        0f);
                    float currentSpeed = Mathf.Max(_velocity.magnitude, 0.01f);
                    float targetSpeed = Mathf.Max(0.01f, weaponData.ProjectileSpeed);
                    float speed = weaponData.GuidedAcceleration > 0f
                        ? Mathf.MoveTowards(currentSpeed, targetSpeed,
                            weaponData.GuidedAcceleration * deltaTime)
                        : currentSpeed;
                    _velocity = steeredDirection * speed;
                }
            }
            else if (weaponData.IsLobbedProjectile)
            {
                _velocity += Vector3.down * (weaponData.ProjectileGravity * deltaTime);
            }
        }

        private void Move(Vector3 displacement)
        {
            transform.position += displacement;
            if (_velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(_velocity.normalized);
            }
        }

        public void UpdateClient(float deltaTime)
        {
            if (GhostGameObject != null && GhostGameObject.IsGhostLinked())
            {
                var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
                SetRuntimeMotion(projectileData.IsHoming != 0,
                    projectileData.HomingTurnSpeed);
                ApplyServerMotionState(projectileData);
            }

            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);
            if (weaponData != null)
            {
                SimulateCosmeticProjectile(deltaTime, weaponData);
            }
        }

        private void SimulateCosmeticProjectile(float deltaTime, WeaponData weaponData)
        {
            EnsureMotionInitialized(weaponData);
            UpdateVelocity(deltaTime, weaponData);
            _collisionGraceTimer = Mathf.Max(0f, _collisionGraceTimer - deltaTime);
            Vector3 displacement = _velocity * deltaTime;

            if (weaponData.IsLobbedProjectile && _collisionGraceTimer <= 0f &&
                displacement.sqrMagnitude > 0.000001f &&
                UnityEngine.Physics.SphereCast(
                    transform.position,
                    ProjectileRadius,
                    displacement.normalized,
                    out RaycastHit hit,
                    displacement.magnitude,
                    LayerMask.GetMask("Ground", "Default"),
                    QueryTriggerInteraction.Ignore))
            {
                if (TryBounce(hit.point, hit.normal, weaponData))
                {
                    return;
                }

                transform.position = hit.point + hit.normal *
                    (ProjectileRadius + BounceSurfaceOffset);
                _velocity = Vector3.zero;
                return;
            }

            Move(displacement);
            _localTime += deltaTime;
        }

        private bool CheckForCollision(WeaponData weaponData, Vector3 displacement)
        {
            if (_collisionGraceTimer > 0f || displacement.sqrMagnitude <= 0.000001f)
            {
                return false;
            }

            Vector3 travelDirection = displacement.normalized;
            float distanceThisFrame = displacement.magnitude;
            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            var world = GhostGameObject.World;
            var movementSystem = world.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            var playerGhostLookup = movementSystem.GetComponentLookup<PredictedPlayerGhost>();
            var ghostOwnerLookup = movementSystem.GetComponentLookup<GhostOwner>();

            // A cast does not reliably report a collider that already contains its origin.
            // Check initial overlaps first, skipping the shooter's own controller.
            int overlapCount = UnityEngine.Physics.OverlapSphereNonAlloc(
                transform.position,
                ProjectileRadius,
                _overlapResults,
                _hitLayerMask,
                QueryTriggerInteraction.Ignore);

            Collider overlappingGeometry = null;
            for (int i = 0; i < overlapCount; i++)
            {
                var hitCollider = _overlapResults[i];
                if (hitCollider == null || IsOwnerCollider(hitCollider, projectileData, ghostOwnerLookup))
                {
                    continue;
                }

                // When the projectile overlaps both the floor and a nearby player, prefer the
                // player. This avoids a small player's low muzzle clipping the floor first.
                if (hitCollider.gameObject.layer == LayerMask.NameToLayer("ServerPlayer"))
                {
                    var impactPosition = hitCollider.ClosestPoint(transform.position);
                    HandleCollision(hitCollider, impactPosition, -travelDirection, weaponData,
                        projectileData, world, playerGhostLookup, ghostOwnerLookup);
                    return true;
                }

                overlappingGeometry ??= hitCollider;
            }

            if (overlappingGeometry != null)
            {
                var impactPosition = overlappingGeometry.ClosestPoint(transform.position);
                Vector3 impactNormal = (transform.position - impactPosition).normalized;
                if (TryBounce(impactPosition, impactNormal, weaponData))
                {
                    return true;
                }

                HandleCollision(overlappingGeometry, impactPosition, -travelDirection, weaponData,
                    projectileData, world, playerGhostLookup, ghostOwnerLookup);
                return true;
            }

            int castCount = UnityEngine.Physics.SphereCastNonAlloc(
                transform.position,
                ProjectileRadius,
                travelDirection,
                _castResults,
                distanceThisFrame,
                _hitLayerMask,
                QueryTriggerInteraction.Ignore);

            RaycastHit closestHit = default;
            bool foundHit = false;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < castCount; i++)
            {
                var candidate = _castResults[i];
                if (candidate.collider == null ||
                    IsOwnerCollider(candidate.collider, projectileData, ghostOwnerLookup) ||
                    candidate.distance >= closestDistance)
                {
                    continue;
                }

                closestHit = candidate;
                closestDistance = candidate.distance;
                foundHit = true;
            }

            if (!foundHit)
            {
                return false;
            }

            if (closestHit.collider.gameObject.layer != LayerMask.NameToLayer("ServerPlayer") &&
                TryBounce(closestHit.point, closestHit.normal, weaponData))
            {
                return true;
            }

            HandleCollision(closestHit.collider, closestHit.point, closestHit.normal, weaponData,
                projectileData, world, playerGhostLookup, ghostOwnerLookup);
            return true;
        }

        private bool TryBounce(Vector3 impactPosition, Vector3 impactNormal, WeaponData weaponData)
        {
            if (!weaponData.IsLobbedProjectile || _bounceCount >= weaponData.ProjectileBounces)
            {
                return false;
            }

            Vector3 normal = impactNormal.sqrMagnitude > 0.0001f
                ? impactNormal.normalized
                : -_velocity.normalized;
            transform.position = impactPosition + normal * (ProjectileRadius + BounceSurfaceOffset);
            _velocity = Vector3.Reflect(_velocity, normal) *
                        weaponData.GetBounceVelocityRetention(_bounceCount);
            _bounceCount++;
            _collisionGraceTimer = BounceCollisionGrace;

            if (_velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(_velocity.normalized);
            }

            PublishServerMotionState();

            return true;
        }

        private void PublishServerMotionState()
        {
            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked() ||
                GhostGameObject.Role != MultiplayerRole.Server)
            {
                return;
            }

            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            projectileData.Position = transform.position;
            projectileData.Velocity = _velocity;
            projectileData.TargetPosition = _guidedTarget;
            projectileData.BounceCount = _bounceCount;
            projectileData.Age = _localTime;
            projectileData.MotionRevision++;
            GhostGameObject.WriteGhostComponentData(projectileData);
        }

        private void ApplyServerMotionState(in ProjectileData projectileData)
        {
            SetGuidedTarget(projectileData.TargetPosition);

            if (GhostGameObject == null || GhostGameObject.Role == MultiplayerRole.Server ||
                projectileData.MotionRevision == 0 ||
                projectileData.MotionRevision == _lastServerMotionRevision)
            {
                return;
            }

            Vector3 serverPosition = projectileData.Position;
            bool isFirstServerState = _lastServerMotionRevision == 0;
            bool isLocallyPredicted = GhostGameObject.Role == MultiplayerRole.ClientOwned;

            // The owning client is deliberately ahead of received snapshots by its
            // network latency. Treating that lead as position error causes repeated
            // backwards snaps at projectile speeds. Its launch data now matches the
            // server, so let prediction remain continuous and use the server only for
            // authoritative collision, damage, explosion and despawn.
            if (isLocallyPredicted)
            {
                _lastServerMotionRevision = projectileData.MotionRevision;
                return;
            }

            bool bounceStateChanged = projectileData.BounceCount != _bounceCount;
            bool hasLargePositionError =
                (transform.position - serverPosition).sqrMagnitude >
                HardReconciliationDistance * HardReconciliationDistance;

            // Normal snapshots are usually an interpolation tick or two behind the
            // locally simulated visual. Applying every one causes visible backwards
            // snaps. Only correct meaningful divergence or an authoritative bounce.
            if (isFirstServerState || bounceStateChanged || hasLargePositionError)
            {
                transform.position = serverPosition;
                _velocity = projectileData.Velocity;
                _bounceCount = projectileData.BounceCount;
                _localTime = projectileData.Age;
                _motionInitialized = true;

                if (_velocity.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(_velocity.normalized);
                }
            }

            _lastServerMotionRevision = projectileData.MotionRevision;
        }

        private void DetonateFromFuse(WeaponData weaponData)
        {
            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            var world = GhostGameObject.World;
            var movementSystem = world.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            var playerGhostLookup = movementSystem.GetComponentLookup<PredictedPlayerGhost>();
            var ghostOwnerLookup = movementSystem.GetComponentLookup<GhostOwner>();
            Vector3 detonationPosition = transform.position;

            if (HasExplosion(weaponData, projectileData))
            {
                HandleAreaOfEffectImpact(detonationPosition, weaponData, projectileData, world,
                    playerGhostLookup, ghostOwnerLookup);
            }
            else
            {
                DrawGizmoAtPosition(detonationPosition);
                GhostGameObject.DestroyEntity();
            }

            if (weaponData.ProjectileHitVfxPrefab != null)
            {
                Vector3 effectDirection = _velocity.sqrMagnitude > 0.0001f
                    ? -_velocity.normalized
                    : Vector3.up;
                SpawnImpactEffect(weaponData, detonationPosition, effectDirection,
                    GetExplosionRadius(weaponData, projectileData),
                    GetSignedSizeDelta(weaponData, projectileData));
            }
        }

        private static bool IsOwnerCollider(Collider collider, in ProjectileData projectileData,
            ComponentLookup<GhostOwner> ghostOwnerLookup)
        {
            if (collider.gameObject.layer != LayerMask.NameToLayer("ServerPlayer") ||
                !GhostGameObject.TryFindGhostGameObject(collider.gameObject, out var hitGhostObject) ||
                !ghostOwnerLookup.HasComponent(hitGhostObject.LinkedEntity))
            {
                return false;
            }

            return ghostOwnerLookup[hitGhostObject.LinkedEntity].NetworkId == projectileData.OwnerNetworkId;
        }

        private void HandleCollision(Collider hitCollider, Vector3 impactPosition, Vector3 impactNormal,
            WeaponData weaponData, in ProjectileData projectileData, World world,
            ComponentLookup<PredictedPlayerGhost> playerGhostLookup,
            ComponentLookup<GhostOwner> ghostOwnerLookup)
        {
            var hitObject = hitCollider.gameObject;
            if (HasExplosion(weaponData, projectileData))
            {
                HandleAreaOfEffectImpact(impactPosition, weaponData, projectileData, world,
                    playerGhostLookup, ghostOwnerLookup);
            }
            else if (hitObject.layer == LayerMask.NameToLayer("ServerPlayer"))
            {
                HandlePlayerImpact(hitObject, impactPosition, world, projectileData, playerGhostLookup,
                    ghostOwnerLookup);
            }
            else
            {
                HandleGeometryImpact(impactPosition);
            }

            if (weaponData.ProjectileHitVfxPrefab != null)
            {
                var normal = impactNormal.sqrMagnitude > 0.0001f ? impactNormal : -transform.forward;
                SpawnImpactEffect(weaponData, impactPosition, normal,
                    GetExplosionRadius(weaponData, projectileData),
                    GetSignedSizeDelta(weaponData, projectileData));
            }
        }

        private static void SpawnImpactEffect(WeaponData weaponData, Vector3 position,
            Vector3 effectDirection, float explosionRadius, float signedSizeDelta)
        {
            float effectScale = explosionRadius > 0f
                ? Mathf.Max(0.01f, explosionRadius)
                : 1f;
            GhostSpawner.GhostReference effectPrefab = weaponData.ProjectileHitVfxPrefab;
            if (explosionRadius > 0f && WeaponManager.Instance != null)
            {
                // Explosive Payload uses the existing expanding AOE-range effect so
                // every player can read the card's actual gameplay radius.
                effectPrefab = WeaponManager.Instance.GetExplosionImpactVfx(signedSizeDelta) ??
                               effectPrefab;
            }

            if (effectPrefab == null) return;
            GhostSpawner.SpawnGhostPrefab(effectPrefab,
                position,
                Quaternion.LookRotation(effectDirection),
                GhostGameObject.GenerateRandomHash(),
                effectScale);
        }

        private void HandleAreaOfEffectImpact(Vector3 impactPosition, WeaponData weaponData,
            in ProjectileData projectileData, World world,
            ComponentLookup<PredictedPlayerGhost> playerGhostLookup,
            ComponentLookup<GhostOwner> ghostOwnerLookup)
        {
            var movementSystem = world.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            var serverCurrentTick = GhostGameObject.GetCurrentTick();
            movementSystem.ApplyRuntimeAreaDamage(
                impactPosition,
                GetExplosionRadius(weaponData, projectileData),
                projectileData.OwnerNetworkId,
                GetSignedSizeDelta(weaponData, projectileData),
                weaponData.ResizeEffect,
                weaponData.ResizeEffectDuration,
                projectileData.AppliesSlow != 0 ? projectileData.SlowPercent : 0f,
                projectileData.AppliesSlow != 0 ? projectileData.SlowDuration : 0f,
                serverCurrentTick,
                playerGhostLookup,
                ghostOwnerLookup);

            DrawGizmoAtPosition(impactPosition);
            GhostGameObject.DestroyEntity();
        }

        private void HandleGeometryImpact(Vector3 impactPosition)
        {
            DrawGizmoAtPosition(impactPosition);
            GhostGameObject.DestroyEntity();
        }

        private void HandlePlayerImpact(GameObject hitObject, Vector3 impactPosition, World world,
            ProjectileData projectileData,
            ComponentLookup<PredictedPlayerGhost> playerGhostLookup, ComponentLookup<GhostOwner> ghostOwnerLookup)
        {
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(projectileData.WeaponID);
            int shooterNetworkId = projectileData.OwnerNetworkId;

            var serverCurrentTick = GhostGameObject.GetCurrentTick();

            if (GhostGameObject.TryFindGhostGameObject(hitObject, out var hitGhostObject) &&
                playerGhostLookup.HasComponent(hitGhostObject.LinkedEntity))
            {
                // Get the owner of the hit player and compare it to the projectile's owner.
                var hitPlayerOwner = ghostOwnerLookup[hitGhostObject.LinkedEntity];
                int targetNetworkId = hitPlayerOwner.NetworkId;

                if (targetNetworkId != shooterNetworkId)
                {
                    Debug.Log(
                        $"2 hitPlayerOwner.NetworkId: {hitPlayerOwner.NetworkId.ToString()}, projectileData.OwnerNetworkId: {projectileData.OwnerNetworkId.ToString()}");
                    world.GetExistingSystemManaged<ServerPlayerMovementSystem>()
                        .ApplyIncomingWeaponDamage(hitGhostObject.LinkedEntity,
                            shooterNetworkId,
                            GetSignedSizeDelta(weaponData, projectileData),
                            weaponData.ResizeEffect,
                            weaponData.ResizeEffectDuration,
                            projectileData.AppliesSlow != 0 ? projectileData.SlowPercent : 0f,
                            projectileData.AppliesSlow != 0 ? projectileData.SlowDuration : 0f,
                            serverCurrentTick,
                            playerGhostLookup,
                            ghostOwnerLookup);

                    DrawGizmoAtPosition(impactPosition);
                    GhostGameObject.DestroyEntity();
                }
            }
        }

        private static bool HasExplosion(WeaponData weaponData,
            in ProjectileData projectileData)
        {
            return projectileData.HasExplosion != 0 ||
                   weaponData.Behavior == ProjectileBehavior.AreaOfEffect;
        }

        private static float GetExplosionRadius(WeaponData weaponData,
            in ProjectileData projectileData)
        {
            return projectileData.HasExplosion != 0
                ? Mathf.Max(0f, projectileData.ExplosionRadius)
                : weaponData.Behavior == ProjectileBehavior.AreaOfEffect
                    ? Mathf.Max(0f, weaponData.AoeRadius)
                    : 0f;
        }

        private static float GetSignedSizeDelta(WeaponData weaponData,
            in ProjectileData projectileData)
        {
            return Mathf.Abs(projectileData.SignedSizeDelta) > Mathf.Epsilon
                ? projectileData.SignedSizeDelta
                : weaponData.PlayerSizeDelta;
        }

        private static void DrawGizmoAtPosition(Vector3 position)
        {
            var color = Color.yellow;
            const float duration = 4.0f; // How long the gizmo will be visible in seconds
            const float size = 0.25f; // The length of the lines for the cross marker

            Debug.DrawRay(position - Vector3.up * size, Vector3.up * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.right * size, Vector3.right * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.forward * size, Vector3.forward * size * 2, color, duration);
        }

    }
}
