using System.Collections.Generic;
using System;
using Gameplay.Leaderboard;
using Unity.Entities;
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
        private readonly HashSet<Entity> _aoeHitEntities = new HashSet<Entity>();

        // Holds the projectile's networked state
        public struct ProjectileData : IComponentData
        {
            [GhostField] public int OwnerNetworkId;
            [GhostField] public uint SpawnTick;
            [GhostField] public uint WeaponID;
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
                Move(Time.deltaTime, weaponData.ProjectileSpeed);
            }
        }

        public void SetWeaponId(uint weaponId)
        {
            _weaponId = weaponId;
        }

        private float _localTime;
        private uint _weaponId;

        public override void OnGhostLinked()
        {
            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            _weaponId = projectileData.WeaponID;
        }

        public void UpdateServer(float deltaTime)
        {
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);

            // Test the segment the projectile is about to travel. The previous implementation
            // moved first and cast forward from the new position, which skipped close targets.
            if (CheckForCollision(weaponData, deltaTime))
            {
                return;
            }

            Move(deltaTime, weaponData.ProjectileSpeed);

            _localTime += deltaTime;
            if (_localTime > 5f)
            {
                GhostGameObject.DestroyEntity();
                return;
            }
        }

        private void Move(float deltaTime, float speed)
        {
            transform.position += transform.forward * (speed * deltaTime);
        }

        public void UpdateClient(float deltaTime)
        {
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(_weaponId);
            Move(deltaTime, weaponData.ProjectileSpeed);
        }

        private bool CheckForCollision(WeaponData weaponData, float deltaTime)
        {
            const float projectileRadius = 0.2f;
            float distanceThisFrame = weaponData.ProjectileSpeed * deltaTime;
            var projectileData = GhostGameObject.ReadGhostComponentData<ProjectileData>();
            var world = GhostGameObject.World;
            var movementSystem = world.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            var playerGhostLookup = movementSystem.GetComponentLookup<PredictedPlayerGhost>();
            var ghostOwnerLookup = movementSystem.GetComponentLookup<GhostOwner>();

            // A cast does not reliably report a collider that already contains its origin.
            // Check initial overlaps first, skipping the shooter's own controller.
            int overlapCount = UnityEngine.Physics.OverlapSphereNonAlloc(
                transform.position,
                projectileRadius,
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
                    HandleCollision(hitCollider, impactPosition, -transform.forward, weaponData,
                        projectileData, world, playerGhostLookup, ghostOwnerLookup);
                    return true;
                }

                overlappingGeometry ??= hitCollider;
            }

            if (overlappingGeometry != null)
            {
                var impactPosition = overlappingGeometry.ClosestPoint(transform.position);
                HandleCollision(overlappingGeometry, impactPosition, -transform.forward, weaponData,
                    projectileData, world, playerGhostLookup, ghostOwnerLookup);
                return true;
            }

            int castCount = UnityEngine.Physics.SphereCastNonAlloc(
                transform.position,
                projectileRadius,
                transform.forward,
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

            HandleCollision(closestHit.collider, closestHit.point, closestHit.normal, weaponData,
                projectileData, world, playerGhostLookup, ghostOwnerLookup);
            return true;
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
            if (weaponData.Behavior == ProjectileBehavior.AreaOfEffect)
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
                GhostSpawner.SpawnGhostPrefab(weaponData.ProjectileHitVfxPrefab, impactPosition,
                    Quaternion.LookRotation(normal), GhostGameObject.GenerateRandomHash());
            }
        }

        private void HandleAreaOfEffectImpact(Vector3 impactPosition, WeaponData weaponData,
            in ProjectileData projectileData, World world,
            ComponentLookup<PredictedPlayerGhost> playerGhostLookup,
            ComponentLookup<GhostOwner> ghostOwnerLookup)
        {
            var playersInRadius = UnityEngine.Physics.OverlapSphere(
                impactPosition,
                weaponData.AoeRadius,
                LayerMask.GetMask("ServerPlayer"),
                QueryTriggerInteraction.Ignore);

            _aoeHitEntities.Clear();
            var movementSystem = world.GetExistingSystemManaged<ServerPlayerMovementSystem>();
            var serverCurrentTick = GhostGameObject.GetCurrentTick();

            foreach (var playerCollider in playersInRadius)
            {
                if (!GhostGameObject.TryFindGhostGameObject(playerCollider.gameObject, out var hitGhostObject))
                {
                    continue;
                }

                var targetEntity = hitGhostObject.LinkedEntity;
                if (!_aoeHitEntities.Add(targetEntity) ||
                    !playerGhostLookup.HasComponent(targetEntity) ||
                    !ghostOwnerLookup.HasComponent(targetEntity))
                {
                    continue;
                }

                int targetNetworkId = ghostOwnerLookup[targetEntity].NetworkId;
                if (targetNetworkId == projectileData.OwnerNetworkId)
                {
                    continue;
                }

                var targetPlayer = playerGhostLookup.GetRefRW(targetEntity);
                float healthBeforeDamage = targetPlayer.ValueRO.CurrentHealth;
                float appliedDamage = weaponData.PlayerDamage;
                targetPlayer.ValueRW.CurrentHealth -= appliedDamage;
                movementSystem.ApplyWeaponResizeEffect(targetEntity, weaponData, ref targetPlayer.ValueRW);
                targetPlayer.ValueRW.ControllerState.IsHit = true;
                targetPlayer.ValueRW.LastDamageAmount = appliedDamage;
                targetPlayer.ValueRW.LastHitTick = serverCurrentTick;

                if (healthBeforeDamage > 0f && targetPlayer.ValueRO.CurrentHealth <= 0f &&
                    LeaderboardManager.Instance != null)
                {
                    LeaderboardManager.Instance.AddKill(projectileData.OwnerNetworkId, targetNetworkId);
                }
            }

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
                    var targetPredictedPlayer = playerGhostLookup.GetRefRW(hitGhostObject.LinkedEntity);
                    var healthBeforeDamage = targetPredictedPlayer.ValueRO.CurrentHealth;
                    var appliedDamage = weaponData.PlayerDamage;
                    targetPredictedPlayer.ValueRW.CurrentHealth -= appliedDamage;
                    world.GetExistingSystemManaged<ServerPlayerMovementSystem>()
                        .ApplyWeaponResizeEffect(hitGhostObject.LinkedEntity, weaponData,
                            ref targetPredictedPlayer.ValueRW);
                    targetPredictedPlayer.ValueRW.ControllerState.IsHit = true;
                    targetPredictedPlayer.ValueRW.LastDamageAmount = appliedDamage;
                    targetPredictedPlayer.ValueRW.LastHitTick = serverCurrentTick;

                    if (healthBeforeDamage > 0 && targetPredictedPlayer.ValueRO.CurrentHealth <= 0)
                    {
                        if (LeaderboardManager.Instance != null)
                        {
                            LeaderboardManager.Instance.AddKill(shooterNetworkId, targetNetworkId);
                            Debug.Log(
                                $"[Server] Player {shooterNetworkId.ToString()} killed player {targetNetworkId.ToString()} (Direct).");
                        }
                        else
                        {
                            Debug.LogWarning("[Server] LeaderboardManager instance not found. Cannot add kill.");
                        }
                    }

                    DrawGizmoAtPosition(impactPosition);
                    GhostGameObject.DestroyEntity();
                }
            }
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
