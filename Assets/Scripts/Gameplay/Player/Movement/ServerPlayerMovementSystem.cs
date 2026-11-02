using Gameplay.Leaderboard;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class PlayerControllerLink : IComponentData
    {
        public FirstPersonController Controller;
    }

    struct ProjectileSpawnData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
        public int OwnerNetworkId;
        public uint SpawnTick;
        public uint WeaponId;
    }

    struct VfxSpawnData
    {
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
    }

    struct ShotCollision
    {
        public Entity PlayerEntity;
        public Collider Collider;
        public Vector3 Point;
        public Vector3 Normal;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    public partial class ServerPlayerMovementSystem : SingletonSystem<ServerPlayerMovementSystem>
    {
        private const int k_NumHistoryTicks = 20;
        private static readonly int s_ShootableLayerMask = LayerMask.GetMask("Ground", "Default");
        private NativeList<ClientCommandInput> m_ProcessedClientInputCommands;
        private ComponentLookup<PredictedClientInput> m_PredictedClientInputComponentLookup;
        private ComponentLookup<PredictedPlayerGhost> m_PlayerGhostLookup;
        private readonly RaycastHit[] m_HitscanCastResults = new RaycastHit[32];
        private readonly Dictionary<Entity, LagCompensatedHitboxHistory> m_LagCompensatedHitboxes = new();
        private readonly Dictionary<Entity, float> m_ForcedFullSizeTimers = new();
        private readonly List<Entity> m_ForcedFullSizeEntitiesScratch = new();
        private readonly HashSet<Entity> m_PendingSelfGrowthChecks = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_PlayerMovementActive = false;
            s_PlayerMovementTick = 0;
        }

        // logging and diagnostics
        private static bool s_PlayerMovementActive = false;
        private static uint s_PlayerMovementTick;
        public static bool PlayerMovementActive => s_PlayerMovementActive;
        public static uint PlayerMovementTick => s_PlayerMovementTick;

        private ComponentLookup<GhostOwner> ghostOwnerLookup;

        public PlayerMovementHistory MovementHistory { get; private set; } =
            new PlayerMovementHistory(k_NumHistoryTicks);

        private struct LagCompensatedHitbox
        {
            public uint Tick;
            public Vector3 Point0;
            public Vector3 Point1;
            public float Radius;
        }

        private sealed class LagCompensatedHitboxHistory
        {
            private readonly LagCompensatedHitbox[] m_Buffer =
                new LagCompensatedHitbox[k_NumHistoryTicks];
            private int m_WriteIndex;
            private int m_Count;

            public void Add(in LagCompensatedHitbox hitbox)
            {
                for (int i = 0; i < m_Count; i++)
                {
                    if (m_Buffer[i].Tick == hitbox.Tick)
                    {
                        m_Buffer[i] = hitbox;
                        return;
                    }
                }

                m_Buffer[m_WriteIndex] = hitbox;
                m_WriteIndex = (m_WriteIndex + 1) % m_Buffer.Length;
                m_Count = Mathf.Min(m_Count + 1, m_Buffer.Length);
            }

            public bool TryGetAtOrBefore(uint tick, out LagCompensatedHitbox hitbox)
            {
                hitbox = default;
                bool foundAtOrBefore = false;
                bool foundAny = false;
                uint newestTickAtOrBefore = 0;
                uint oldestTick = uint.MaxValue;
                LagCompensatedHitbox oldest = default;

                for (int i = 0; i < m_Count; i++)
                {
                    var candidate = m_Buffer[i];
                    if (!foundAny || candidate.Tick < oldestTick)
                    {
                        foundAny = true;
                        oldestTick = candidate.Tick;
                        oldest = candidate;
                    }

                    if (candidate.Tick <= tick &&
                        (!foundAtOrBefore || candidate.Tick > newestTickAtOrBefore))
                    {
                        foundAtOrBefore = true;
                        newestTickAtOrBefore = candidate.Tick;
                        hitbox = candidate;
                    }
                }

                if (!foundAtOrBefore && foundAny)
                {
                    hitbox = oldest;
                }

                return foundAtOrBefore || foundAny;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<NetworkTime>();

            m_PredictedClientInputComponentLookup = GetComponentLookup<PredictedClientInput>(true);
            m_PlayerGhostLookup = GetComponentLookup<PredictedPlayerGhost>();

            const int maxLocalPlayers = 4; //PlayerGhostManager.k_MaxLocalPlayers
            m_ProcessedClientInputCommands =
                new NativeList<ClientCommandInput>(maxLocalPlayers * 32, Allocator.Persistent);

            ghostOwnerLookup = GetComponentLookup<GhostOwner>(true);
        }

        protected override void OnDestroy()
        {
            m_ProcessedClientInputCommands.Dispose();
            m_LagCompensatedHitboxes.Clear();
            m_ForcedFullSizeTimers.Clear();
            m_ForcedFullSizeEntitiesScratch.Clear();
            m_PendingSelfGrowthChecks.Clear();

            var query = GetEntityQuery(typeof(PlayerControllerLink));
            foreach (var entity in query.ToEntityArray(Allocator.Temp))
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller = null;
                EntityManager.SetComponentData(entity, controllerLink);
            }

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            float deltaTime = World.Time.DeltaTime;

            var networkTime = SystemAPI.GetSingleton<NetworkTime>();
            if (!networkTime.ServerTick.IsValid)
            {
                return;
            }

            uint serverTick = networkTime.ServerTick.TickIndexForValidTick;
            UpdateForcedFullSizeEffects(deltaTime);

            s_PlayerMovementActive = true;
            s_PlayerMovementTick = serverTick;

            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

            foreach (var (predictedPlayer, localTransform, entity) in
                     SystemAPI.Query<RefRW<PredictedPlayerGhost>, RefRO<LocalTransform>>()
                         .WithEntityAccess()
                         .WithAll<PlayerInputComponent, GhostGameObjectLink>()
                         .WithNone<PlayerControllerLink>())
            {
                var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                if (gameObjectLink.LinkedInstance != null)
                {
                    if (gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller))
                    {
                        ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
                        predictedPlayer.ValueRW.ControllerState.Init(localTransform.ValueRO.Position,
                            localTransform.ValueRO.Rotation);
                    }
                }
            }

            // for each client
            // we process the input and create a list of commandInputs that need to be processed by the movement code
            // the movement code processes the inputs in order
            m_ProcessedClientInputCommands.Clear();
            var commands = m_ProcessedClientInputCommands;
            var unityFrameCount = UnityEngine.Time.frameCount;

            var logPredictionWarnings = PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;
            var clientCommandInputBufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>(true);

            foreach (var (predictedClient, entity)
                     in SystemAPI.Query<RefRW<PredictedClientInput>>()
                         .WithEntityAccess()
                         .WithAll<GhostOwner, Simulate>())
            {
                if (!clientCommandInputBufferLookup.TryGetBuffer(entity, out var buffer) || buffer.Length == 0)
                {
                    continue;
                }

                predictedClient.ValueRW.BeginInputIndex = commands.Length;

                if (predictedClient.ValueRO.LastProcessedServerTick == 0)
                {
                    // first frame, let's just get the latest data and set it
                    if (buffer.GetDataAtTick(new NetworkTick(serverTick), out var commandInput))
                    {
                        ClientCommandInput input = commandInput;
                        commands.Add(input);
                        predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                    }
                }
                else
                {
                    if (logPredictionWarnings && serverTick - predictedClient.ValueRO.LastProcessedServerTick > 1)
                    {
                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                         $"[ServerPlayerMovementSystem] Server tick has skipped a frame of input. " +
                                         $"Current server tick {serverTick.ToString()}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }

                    // we need to check we aren't missing data
                    // so let's check for any data from the ticks in between
                    for (uint tick = predictedClient.ValueRO.LastProcessedServerTick + 1; tick <= serverTick; tick++)
                    {
                        if (buffer.GetDataAtTick(new NetworkTick(tick), out var commandInput)
                            && commandInput.Tick.TickIndexForValidTick !=
                            predictedClient.ValueRO.LastProcessedServerTick)
                        {
                            ClientCommandInput input = commandInput;
                            commands.Add(input);

                            if (commandInput.Tick.TickIndexForValidTick >
                                predictedClient.ValueRO.LastProcessedServerTick + 1)
                            {
                                // we are missing input from the client which means we might mispredict
                                for (uint i = predictedClient.ValueRO.LastProcessedServerTick + 1;
                                     i < commandInput.Tick.TickIndexForValidTick;
                                     i++)
                                {
                                    // if we have missing ticks here, it means the server did not (and will not) receive it.
                                    // So the best we can do is assume the input is the same and accumulate our movement based on that
                                    if (logPredictionWarnings)
                                    {
                                        Debug.LogWarning($"[{unityFrameCount.ToString()}] " +
                                                         $"[ServerPlayerMovementSystem] Missing client input for tick {i.ToString()} - " +
                                                         $"using the input from tick {commandInput.Tick.TickIndexForValidTick.ToString()}");
                                    }

                                    commands.Add(commandInput);
                                }
                            }

                            predictedClient.ValueRW.LastProcessedServerTick = commandInput.Tick.TickIndexForValidTick;
                        }
                    }
                }

                predictedClient.ValueRW.InputCount = commands.Length - predictedClient.ValueRO.BeginInputIndex;

                if (logPredictionWarnings)
                {
                    var unityFrameCountString = unityFrameCount.ToString();
                    var serverTickString = serverTick.ToString();
                    if (predictedClient.ValueRO.InputCount == 0)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] No input to process for tick {serverTickString}.");
                    }
                    else if (predictedClient.ValueRO.LastProcessedServerTick != serverTick)
                    {
                        Debug.LogWarning(
                            $"[{unityFrameCountString}] [ServerPlayerMovementSystem] Last processed tick not set to current. Current server tick {serverTickString}, last processed tick {predictedClient.ValueRO.LastProcessedServerTick.ToString()}");
                    }
                }
            }

            ghostOwnerLookup.Update(this);
            m_PlayerGhostLookup.Update(this);
            m_PredictedClientInputComponentLookup.Update(this);

            var predictedClientInputComponentLookup = m_PredictedClientInputComponentLookup;
            var playerGhostLookup = m_PlayerGhostLookup;

            var projectileSpawnList = new NativeList<ProjectileSpawnData>(Allocator.Temp);
            var vfxSpawnList = new NativeList<VfxSpawnData>(Allocator.Temp);

            foreach (var predictedPlayer in SystemAPI.Query<RefRW<PredictedPlayerGhost>>()
                         .WithAll<Simulate>())
            {
                if (predictedPlayer.ValueRO.WeaponCooldown < float.MaxValue)
                {
                    predictedPlayer.ValueRW.WeaponCooldown += deltaTime;
                }

                if (predictedPlayer.ValueRO.ControllerState.IsReloadingState)
                {
                    predictedPlayer.ValueRW.ReloadTimer -= deltaTime;
                    if (predictedPlayer.ValueRO.ReloadTimer <= 0f)
                    {
                        predictedPlayer.ValueRW.ControllerState.IsReloadingState = false;
                        var weaponData =
                            WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                                predictedPlayer.ValueRO.EquippedWeaponID);
                        if (weaponData != null)
                        {
                            predictedPlayer.ValueRW.CurrentAmmo = weaponData.MagazineSize;
                        }
                    }
                }
            }

            foreach (var (predictedPlayer, inputLookup,
                         entity)
                     in SystemAPI.Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>>()
                         .WithEntityAccess()
                         .WithAll<Simulate, GhostGameObjectLink>())
            {
                var ghostLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
                var playerGhost = ghostLink.LinkedInstance.GetComponent<PlayerGhost>();
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0)
                {
                    // get movement index for the last frame we are going to process
                    var commandInput = commands[predictedClient.BeginInputIndex + predictedClient.InputCount - 1];
                    if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input))
                    {
                        playerGhost.ServerMovementInput = input;
                    }

                    predictedPlayer.ValueRW.ControllerState.Shoot = false;

                    // Apply weapon selection before reload/fire processing so the authoritative
                    // server and client prediction use the same weapon for this input batch.
                    for (int i = 0; i < predictedClient.InputCount; i++)
                    {
                        var selectionCommand = commands[i + predictedClient.BeginInputIndex];
                        if (!selectionCommand.TryGetPlayerMovementInput(
                                predictedPlayer.ValueRO.InputIndex, out var selectionInput))
                        {
                            continue;
                        }

                        if (selectionInput.SelectPrimaryWeapon)
                        {
                            WeaponManager.Instance.TryEquipWeapon(
                                ref predictedPlayer.ValueRW, WeaponManager.PrimaryWeaponId);
                        }
                        else if (selectionInput.SelectSecondaryWeapon)
                        {
                            WeaponManager.Instance.TryEquipWeapon(
                                ref predictedPlayer.ValueRW, WeaponManager.SecondaryWeaponId);
                        }
                    }

                    var weaponData =
                        WeaponManager.Instance.WeaponRegistry.GetWeaponData(predictedPlayer.ValueRO.EquippedWeaponID);
                    if (weaponData != null)
                    {
                        bool wantsToReload = commandInput.PlayerInput.Reload;
                        bool wantsToShoot = commandInput.PlayerInput.Shoot;
                        bool mustReload = wantsToShoot && predictedPlayer.ValueRO.CurrentAmmo <= 0;

                        if ((wantsToReload || mustReload) &&
                            !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                            predictedPlayer.ValueRO.CurrentAmmo < weaponData.MagazineSize)
                        {
                            predictedPlayer.ValueRW.ControllerState.IsReloadingState = true;
                            predictedPlayer.ValueRW.ReloadTimer = weaponData.ReloadTime;
                            predictedPlayer.ValueRW.LastReloadTick = serverTick;
                        }

                        if (wantsToShoot &&
                            !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                            predictedPlayer.ValueRO.CurrentAmmo > 0 &&
                            predictedPlayer.ValueRO.WeaponCooldown >= weaponData.CooldownInMs)
                        {
                            predictedPlayer.ValueRW.WeaponCooldown = 0f;
                            predictedPlayer.ValueRW.CurrentAmmo--;
                            predictedPlayer.ValueRW.LastShotTick = serverTick;

                            var shooterNetworkId = ghostOwnerLookup[entity].NetworkId;
                            var controllerState = predictedPlayer.ValueRO.ControllerState;
                            quaternion aimRotation = quaternion.Euler(
                                math.radians(controllerState.PitchDegrees),
                                math.radians(controllerState.YawDegrees),
                                0f);

                            float3 eyePosition = playerGhost.CameraTarget.position;
                            float3 aimDirection = math.mul(aimRotation, new float3(0, 0, 1));
                            float3 shotOriginPosition = eyePosition + aimDirection * 0.5f;

                            if (VisualEffectManager.ServerInstance != null)
                            {
                                VisualEffectManager.ServerInstance.Server_RequestVfx(shooterNetworkId,
                                    predictedPlayer.ValueRO.EquippedWeaponID);
                            }

                            switch (weaponData.Type)
                            {
                                case WeaponType.Hitscan:
                                {
                                    uint rewindTick = commandInput.ClientInterpolationTick.IsValid
                                        ? commandInput.ClientInterpolationTick.TickIndexForValidTick
                                        : serverTick;
                                    if (TryGetClosestHitscanCollision(eyePosition, aimDirection,
                                            weaponData.HitscanRange, shooterNetworkId, rewindTick,
                                            out var hit))
                                    {
                                        if (hit.PlayerEntity != Entity.Null &&
                                            playerGhostLookup.HasComponent(hit.PlayerEntity))
                                        {
                                            var targetPredictedPlayer =
                                                playerGhostLookup.GetRefRW(hit.PlayerEntity);
                                            var targetNetworkId = ghostOwnerLookup[hit.PlayerEntity].NetworkId;

                                            if (shooterNetworkId == targetNetworkId)
                                            {
                                                //skip hitting self
                                                continue;
                                            }

                                            var healthBeforeDamage = targetPredictedPlayer.ValueRO.CurrentHealth;
                                            var appliedDamage = weaponData.PlayerDamage;
                                            targetPredictedPlayer.ValueRW.CurrentHealth -= appliedDamage;
                                            ApplyWeaponResizeEffect(hit.PlayerEntity, weaponData,
                                                ref targetPredictedPlayer.ValueRW);
                                            targetPredictedPlayer.ValueRW.ControllerState.IsHit = true;

                                            targetPredictedPlayer.ValueRW.LastDamageAmount = appliedDamage;
                                            targetPredictedPlayer.ValueRW.LastHitTick = serverTick;

                                            if (healthBeforeDamage > 0 &&
                                                targetPredictedPlayer.ValueRO.CurrentHealth <= 0)
                                            {
                                                if (LeaderboardManager.Instance != null)
                                                {
                                                    LeaderboardManager.Instance.AddKill(shooterNetworkId,
                                                        targetNetworkId);
                                                    Debug.Log(
                                                        $"[Server] Player {shooterNetworkId.ToString()} killed player {targetNetworkId.ToString()}.");
                                                }
                                                else
                                                {
                                                    Debug.LogWarning(
                                                        "[Server] LeaderboardManager instance not found. Cannot add kill.");
                                                }
                                            }
                                        }

                                        if (weaponData.ProjectileHitVfxPrefab != null)
                                        {
                                            vfxSpawnList.Add(new VfxSpawnData()
                                            {
                                                Position = hit.Point,
                                                Rotation = Quaternion.LookRotation(hit.Normal),
                                                Prefab = GhostSpawner.FindGhostPrefabEntity(weaponData
                                                    .ProjectileHitVfxPrefab.GhostGuid)
                                            });
                                        }
                                    }

                                    break;
                                }
                                case WeaponType.Projectile:
                                {
                                    var prefabEntity =
                                        GhostSpawner.FindGhostPrefabEntity(weaponData.ProjectileGhostPrefab.GhostGuid);
                                    if (prefabEntity != Entity.Null)
                                    {
                                        // Determine target point
                                        Vector3 targetPoint;
                                        // Use calculated eyePosition and aimDirection for the raycast
                                        if (UnityEngine.Physics.Raycast(eyePosition, aimDirection,
                                                out RaycastHit aimHit, 1000f,
                                                s_ShootableLayerMask))
                                        {
                                            targetPoint = aimHit.point;
                                        }
                                        else
                                        {
                                            targetPoint = eyePosition + aimDirection * 1000f;
                                        }

                                        // Use calculated shotOriginPosition for direction calculation
                                        Vector3 directionToTarget =
                                            (targetPoint - (Vector3)shotOriginPosition).normalized;
                                        Quaternion spawnRotation = Quaternion.LookRotation(directionToTarget);

                                        projectileSpawnList.Add(new ProjectileSpawnData
                                        {
                                            Prefab = prefabEntity,
                                            Position = shotOriginPosition,
                                            Rotation = spawnRotation,
                                            OwnerNetworkId = ghostOwnerLookup[entity].NetworkId,
                                            SpawnTick = commandInput.Tick.TickIndexForValidTick,
                                            WeaponId = predictedPlayer.ValueRO.EquippedWeaponID
                                        });
                                    }
                                    else
                                    {
                                        Debug.LogError(
                                            $"[Server] Projectile ghost for weapon '{weaponData.WeaponName}' is not registered in GhostPrefabsAuthoring.");
                                    }

                                    break;
                                }
                            }
                        }
                    }
                }
            }

            foreach (var spawnData in projectileSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    spawnData.Prefab,
                    spawnData.Position,
                    spawnData.Rotation,
                    GhostGameObject.GenerateRandomHash(),
                    1.0f,
                    (spawnedEntity, ecb2) =>
                    {
                        ecb2.SetComponent(spawnedEntity, new Projectile.ProjectileData
                        {
                            OwnerNetworkId = spawnData.OwnerNetworkId,
                            SpawnTick = spawnData.SpawnTick,
                            WeaponID = spawnData.WeaponId
                        });
                    });
            }

            foreach (var vfxData in vfxSpawnList)
            {
                GhostSpawner.SpawnGhostPrefab(
                    vfxData.Prefab,
                    vfxData.Position,
                    vfxData.Rotation,
                    GhostGameObject.GenerateRandomHash());
            }

            predictedClientInputComponentLookup.Update(this);

            foreach (var (predictedPlayer, inputLookup, controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRW<PredictedPlayerGhost>, RefRO<PlayerClientCommandInputLookup>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink>()
                         .WithEntityAccess())
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller.InitializeShrinkResource(ref predictedPlayer.ValueRW);
                var predictedClient = predictedClientInputComponentLookup[inputLookup.ValueRO.ClientCommandInputEntity];
                if (predictedClient.InputCount > 0)
                {
                    float movementDt = deltaTime / predictedClient.InputCount;
                    for (int i = 0; i < predictedClient.InputCount; i++)
                    {
                        var commandInput = commands[i + predictedClient.BeginInputIndex];
                        bool wasManuallyShrunk =
                            predictedPlayer.ValueRO.ControllerState.IsManualShrinkActive;
                        if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRO.InputIndex, out var input))
                        {
                            controllerLink.Controller.ProcessShrinkInput(
                                ref predictedPlayer.ValueRW, input);

                            controllerLink.Controller.GetSizeMovementMultipliers(
                                in predictedPlayer.ValueRO,
                                out float speedMultiplier,
                                out float jumpHeightMultiplier);
                            FirstPersonController.AccumulateMovement(ref predictedPlayer.ValueRW.ControllerState,
                                ref predictedPlayer.ValueRW.AccumulatedMovement,
                                input,
                                controllerConsts.ValueRO.ControllerConsts,
                                speedMultiplier,
                                jumpHeightMultiplier,
                                movementDt);
                        }

                        bool selfInstagibbed = TryHandleSelfGrowthInObstruction(
                            entity, wasManuallyShrunk, ref predictedPlayer.ValueRW);
                        if (selfInstagibbed)
                        {
                            break;
                        }

                        wasManuallyShrunk =
                            predictedPlayer.ValueRO.ControllerState.IsManualShrinkActive;
                        controllerLink.Controller.UpdateShrinkResource(
                            ref predictedPlayer.ValueRW, movementDt);
                        if (TryHandleSelfGrowthInObstruction(
                                entity, wasManuallyShrunk, ref predictedPlayer.ValueRW))
                        {
                            break;
                        }
                    }

                    if (predictedPlayer.ValueRW.ControllerState.JumpTriggered)
                    {
                        predictedPlayer.ValueRW.LastJumpTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.JumpTriggered = false;
                    }

                    if (predictedPlayer.ValueRW.ControllerState.LandTriggered)
                    {
                        predictedPlayer.ValueRW.LastLandTick = serverTick;
                        predictedPlayer.ValueRW.ControllerState.LandTriggered = false;
                    }
                }
                else
                {
                    bool wasManuallyShrunk =
                        predictedPlayer.ValueRO.ControllerState.IsManualShrinkActive;
                    controllerLink.Controller.UpdateShrinkResource(ref predictedPlayer.ValueRW, deltaTime);
                    TryHandleSelfGrowthInObstruction(
                        entity, wasManuallyShrunk, ref predictedPlayer.ValueRW);
                }
            }

            foreach (var (transform,
                         predictedPlayer,
                         controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>()
                         .WithNone<GhostGameObjectDeferredActivation>()
                         .WithEntityAccess())
            {
                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                if (math.lengthsq(predictedPlayer.ValueRO.AccumulatedMovement) > 0f)
                {
                    controllerLink.Controller.ApplyMovementUpdate(ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts.ValueRO.ControllerConsts,
                        predictedPlayer.ValueRO.AccumulatedMovement,
                        deltaTime);
                }

                predictedPlayer.ValueRW.AccumulatedMovement = float3.zero;

                if (m_PendingSelfGrowthChecks.Remove(entity) &&
                    TryInstagibBlockedGrowth(entity, ref predictedPlayer.ValueRW))
                {
                    RecordSelfCrushDeath(entity);
                }
            }

            bool checkForPredictionErrors = PlayerPredictionSystem.CheckForPredictionErrors.IsEnabled;

            foreach (var (transform, predictedPlayer, controllerConsts, entity)
                     in SystemAPI
                         .Query<RefRW<LocalTransform>, RefRW<PredictedPlayerGhost>,
                             RefRO<PredictedPlayerControllerConsts>>()
                         .WithAll<Simulate, PlayerControllerLink, GhostGameObjectLink>()
                         .WithEntityAccess())
            {
                transform.ValueRW.Position = predictedPlayer.ValueRO.ControllerState.CurrentPosition;
                transform.ValueRW.Rotation = predictedPlayer.ValueRO.ControllerState.CurrentRotation;

                var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
                controllerLink.Controller.GetCurrentHitboxCapsule(
                    (Vector3)predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                    (Quaternion)predictedPlayer.ValueRO.ControllerState.CurrentRotation,
                    out Vector3 hitboxPoint0,
                    out Vector3 hitboxPoint1,
                    out float hitboxRadius);
                if (!m_LagCompensatedHitboxes.TryGetValue(entity, out var hitboxHistory))
                {
                    hitboxHistory = new LagCompensatedHitboxHistory();
                    m_LagCompensatedHitboxes.Add(entity, hitboxHistory);
                }

                hitboxHistory.Add(new LagCompensatedHitbox
                {
                    Tick = serverTick,
                    Point0 = hitboxPoint0,
                    Point1 = hitboxPoint1,
                    Radius = hitboxRadius
                });

                if (checkForPredictionErrors)
                {
                    if (MovementHistory.TryGetTick(serverTick, out var pos, out var rot))
                    {
                        // compare our calculation with the recorded
                        Debug.Assert(PlayerMovementHistory.HistoryMatches(pos,
                                rot,
                                predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                predictedPlayer.ValueRO.ControllerState.CurrentRotation),
                            $"[ServerPlayerMovementSystem] Mispredict detected at tick {serverTick} predicted pos {predictedPlayer.ValueRO.ControllerState.CurrentPosition} recorded {(float3)pos}");
                    }
                    else
                    {
                        // this is the first time we've processed this tick
                        // let's add our result
                        MovementHistory.Add(serverTick, predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                            predictedPlayer.ValueRO.ControllerState.CurrentRotation);
                    }
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();

            s_PlayerMovementActive = false;
        }

        private bool TryGetClosestHitscanCollision(Vector3 origin, Vector3 direction, float range,
            int shooterNetworkId, uint rewindTick, out ShotCollision collision)
        {
            int hitCount = UnityEngine.Physics.RaycastNonAlloc(
                origin,
                direction,
                m_HitscanCastResults,
                range,
                s_ShootableLayerMask,
                QueryTriggerInteraction.Ignore);

            float closestDistance = float.MaxValue;
            collision = default;
            bool foundCollision = false;
            for (int i = 0; i < hitCount; i++)
            {
                var candidate = m_HitscanCastResults[i];
                if (candidate.collider == null ||
                    candidate.collider.GetComponentInParent<PlayerGhost>() != null ||
                    candidate.distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = candidate.distance;
                collision = new ShotCollision
                {
                    PlayerEntity = Entity.Null,
                    Collider = candidate.collider,
                    Point = candidate.point,
                    Normal = candidate.normal
                };
                foundCollision = true;
            }

            foreach (var pair in m_LagCompensatedHitboxes)
            {
                Entity targetEntity = pair.Key;
                if (!EntityManager.Exists(targetEntity) ||
                    !ghostOwnerLookup.HasComponent(targetEntity) ||
                    !m_PlayerGhostLookup.HasComponent(targetEntity) ||
                    ghostOwnerLookup[targetEntity].NetworkId == shooterNetworkId ||
                    m_PlayerGhostLookup[targetEntity].CurrentHealth <= 0f ||
                    !pair.Value.TryGetAtOrBefore(rewindTick, out var hitbox) ||
                    !RayIntersectsCapsule(origin, direction, hitbox.Point0, hitbox.Point1,
                        hitbox.Radius, out float distance, out Vector3 normal) ||
                    distance > range || distance >= closestDistance)
                {
                    continue;
                }

                closestDistance = distance;
                collision = new ShotCollision
                {
                    PlayerEntity = targetEntity,
                    Collider = null,
                    Point = origin + direction * distance,
                    Normal = normal
                };
                foundCollision = true;
            }

            return foundCollision;
        }

        private static bool RayIntersectsCapsule(Vector3 origin, Vector3 direction,
            Vector3 point0, Vector3 point1, float radius, out float distance, out Vector3 normal)
        {
            direction.Normalize();
            var capsuleAxis = point1 - point0;
            float axisLengthSq = Vector3.Dot(capsuleAxis, capsuleAxis);
            Vector3 closestAtOrigin = axisLengthSq > Mathf.Epsilon
                ? point0 + capsuleAxis * Mathf.Clamp01(
                    Vector3.Dot(origin - point0, capsuleAxis) / axisLengthSq)
                : point0;

            if ((origin - closestAtOrigin).sqrMagnitude <= radius * radius)
            {
                distance = 0f;
                normal = -direction;
                return true;
            }

            distance = float.MaxValue;
            bool found = false;

            if (axisLengthSq > Mathf.Epsilon)
            {
                var originFromPoint0 = origin - point0;
                float axisDotDirection = Vector3.Dot(capsuleAxis, direction);
                float axisDotOrigin = Vector3.Dot(capsuleAxis, originFromPoint0);
                float directionDotOrigin = Vector3.Dot(direction, originFromPoint0);
                float originLengthSq = Vector3.Dot(originFromPoint0, originFromPoint0);
                float a = axisLengthSq - axisDotDirection * axisDotDirection;
                float b = axisLengthSq * directionDotOrigin -
                          axisDotOrigin * axisDotDirection;
                float c = axisLengthSq * originLengthSq -
                          axisDotOrigin * axisDotOrigin - radius * radius * axisLengthSq;
                float discriminant = b * b - a * c;

                if (Mathf.Abs(a) > Mathf.Epsilon && discriminant >= 0f)
                {
                    float cylinderDistance = (-b - Mathf.Sqrt(discriminant)) / a;
                    float axisPosition = axisDotOrigin + cylinderDistance * axisDotDirection;
                    if (cylinderDistance >= 0f && axisPosition >= 0f &&
                        axisPosition <= axisLengthSq)
                    {
                        distance = cylinderDistance;
                        found = true;
                    }
                }
            }

            if (TryRaySphere(origin, direction, point0, radius, out float capDistance0) &&
                capDistance0 < distance)
            {
                distance = capDistance0;
                found = true;
            }

            if (TryRaySphere(origin, direction, point1, radius, out float capDistance1) &&
                capDistance1 < distance)
            {
                distance = capDistance1;
                found = true;
            }

            if (!found)
            {
                normal = Vector3.zero;
                return false;
            }

            var hitPoint = origin + direction * distance;
            Vector3 closestOnAxis = axisLengthSq > Mathf.Epsilon
                ? point0 + capsuleAxis * Mathf.Clamp01(
                    Vector3.Dot(hitPoint - point0, capsuleAxis) / axisLengthSq)
                : point0;
            normal = (hitPoint - closestOnAxis).normalized;
            if (normal.sqrMagnitude <= Mathf.Epsilon)
            {
                normal = -direction;
            }

            return true;
        }

        private static bool TryRaySphere(Vector3 origin, Vector3 direction, Vector3 center,
            float radius, out float distance)
        {
            var offset = origin - center;
            float projected = Vector3.Dot(offset, direction);
            float discriminant = projected * projected -
                                 (Vector3.Dot(offset, offset) - radius * radius);
            if (discriminant < 0f)
            {
                distance = 0f;
                return false;
            }

            distance = -projected - Mathf.Sqrt(discriminant);
            return distance >= 0f;
        }

        public void ApplyWeaponResizeEffect(Entity targetEntity, WeaponData weaponData,
            ref PredictedPlayerGhost player)
        {
            if (weaponData.ResizeEffect != PlayerResizeEffect.RestoreFullSize ||
                player.CurrentHealth <= 0f)
            {
                return;
            }

            if (TryInstagibBlockedGrowth(targetEntity, ref player))
            {
                return;
            }

            player.ControllerState.IsManualShrinkActive = false;
            player.ControllerState.IsForcedFullSize = true;
            m_ForcedFullSizeTimers[targetEntity] = Mathf.Max(0.1f, weaponData.ResizeEffectDuration);
        }

        private bool TryHandleSelfGrowthInObstruction(Entity playerEntity,
            bool wasManuallyShrunk, ref PredictedPlayerGhost player)
        {
            if (!wasManuallyShrunk ||
                player.ControllerState.IsManualShrinkActive ||
                player.ControllerState.IsForcedFullSize)
            {
                return false;
            }

            if (!TryInstagibBlockedGrowth(playerEntity, ref player))
            {
                // Movement is applied later in this tick. Check again afterwards so
                // jumping into a low frame during the growth tick cannot clip through.
                m_PendingSelfGrowthChecks.Add(playerEntity);
                return false;
            }

            RecordSelfCrushDeath(playerEntity);

            return true;
        }

        private void RecordSelfCrushDeath(Entity playerEntity)
        {
            if (LeaderboardManager.Instance == null || !ghostOwnerLookup.HasComponent(playerEntity))
            {
                return;
            }

            int playerNetworkId = ghostOwnerLookup[playerEntity].NetworkId;
            LeaderboardManager.Instance.AddDeath(playerNetworkId);
            Debug.Log($"[Server] Player {playerNetworkId} was crushed while returning to full size.");
        }

        private bool TryInstagibBlockedGrowth(Entity playerEntity,
            ref PredictedPlayerGhost player)
        {
            if (player.CurrentHealth <= 0f ||
                !EntityManager.HasComponent<PlayerControllerLink>(playerEntity))
            {
                return false;
            }

            var controllerLink = EntityManager.GetComponentObject<PlayerControllerLink>(playerEntity);
            if (controllerLink?.Controller == null)
            {
                return false;
            }

            if (controllerLink.Controller.TryFindFullSizeClearance(
                    (Vector3)player.ControllerState.CurrentPosition,
                    (Quaternion)player.ControllerState.CurrentRotation,
                    out var positionAdjustment))
            {
                if (positionAdjustment.sqrMagnitude > 0f)
                {
                    controllerLink.Controller.transform.position += positionAdjustment;
                    player.ControllerState.CurrentPosition += (float3)positionAdjustment;
                }

                return false;
            }

            player.CurrentHealth = 0f;
            player.ControllerState.IsManualShrinkActive = false;
            player.ControllerState.IsForcedFullSize = false;
            player.ControllerState.IsHit = true;
            m_ForcedFullSizeTimers.Remove(playerEntity);

            if (VisualEffectManager.ServerInstance != null)
            {
                var explosionPosition = controllerLink.Controller.transform.position +
                                        controllerLink.Controller.transform.up;
                VisualEffectManager.ServerInstance.Server_RequestPlayerExplosion(
                    explosionPosition);
            }

            return true;
        }

        private void UpdateForcedFullSizeEffects(float deltaTime)
        {
            if (m_ForcedFullSizeTimers.Count == 0)
            {
                return;
            }

            m_ForcedFullSizeEntitiesScratch.Clear();
            m_ForcedFullSizeEntitiesScratch.AddRange(m_ForcedFullSizeTimers.Keys);
            foreach (var entity in m_ForcedFullSizeEntitiesScratch)
            {
                float remainingTime = m_ForcedFullSizeTimers[entity] - deltaTime;
                if (remainingTime > 0f)
                {
                    m_ForcedFullSizeTimers[entity] = remainingTime;

                    // Reassert the authoritative flag every server tick so prediction replay
                    // cannot cancel the effect while the timer is active.
                    if (EntityManager.Exists(entity) && EntityManager.HasComponent<PredictedPlayerGhost>(entity))
                    {
                        var activePlayer = EntityManager.GetComponentData<PredictedPlayerGhost>(entity);
                        activePlayer.ControllerState.IsManualShrinkActive = false;
                        activePlayer.ControllerState.IsForcedFullSize = true;
                        EntityManager.SetComponentData(entity, activePlayer);
                    }

                    continue;
                }

                if (EntityManager.Exists(entity) && EntityManager.HasComponent<PredictedPlayerGhost>(entity))
                {
                    var player = EntityManager.GetComponentData<PredictedPlayerGhost>(entity);
                    player.ControllerState.IsForcedFullSize = false;
                    EntityManager.SetComponentData(entity, player);
                }

                m_ForcedFullSizeTimers.Remove(entity);
            }
        }

    }
}
