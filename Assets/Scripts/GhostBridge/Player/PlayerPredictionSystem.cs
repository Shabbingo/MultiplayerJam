// #define PREDICTION_DEBUG_LOGGING

using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.MP_FPS;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
public partial class PlayerPredictionSystem : SingletonSystem<PlayerPredictionSystem>
{
    // logging and diagnostics
    private static uint s_PlayerMovementTick;
    private static readonly int s_HitscanLayerMask = ~LayerMask.GetMask("ClientPlayer");
    private static readonly int s_ProjectileTargetLayerMask = ~LayerMask.GetMask( "ClientPlayer", "ServerPlayer" );
    
    public static uint PlayerMovementTick => s_PlayerMovementTick;

    private int m_LastSeenUnityFrame;

    [FeatureToggle(Name = "prediction_checkforpredictionerrors", Default = false)]
    public static readonly FeatureToggle CheckForPredictionErrors;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        s_PlayerMovementTick = 0;
    }
    
    private NativeList<ClientCommandInput> m_ProcessedClientInputCommands;

    protected override void OnCreate()
    {
        base.OnCreate();

        m_ProcessedClientInputCommands = new NativeList<ClientCommandInput>(32, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        m_ProcessedClientInputCommands.Dispose();

        var query = GetEntityQuery(typeof(PlayerControllerLink));
        foreach (var entity in query.ToEntityArray(Allocator.Temp))
        {
            var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
            controllerLink.Controller = null;
            EntityManager.SetComponentData(entity, controllerLink);
        }
    }

    protected override void OnUpdate()
    {
        var networkTime = SystemAPI.GetSingleton<NetworkTime>();
        uint tick = networkTime.ServerTick.TickIndexForValidTick;

        var logPredictionWarnings = CheckForPredictionErrors.IsEnabled;

        if (logPredictionWarnings
            && UnityEngine.Time.frameCount > m_LastSeenUnityFrame
            && m_LastSeenUnityFrame != 0
            && (UnityEngine.Time.frameCount - m_LastSeenUnityFrame) > 1)
        {
            var frameCount = UnityEngine.Time.frameCount.ToString();
            Debug.LogWarning($"[{frameCount}] PlayerPredictionSystem missed a Unity frame. Current frame {frameCount} last processed frame {m_LastSeenUnityFrame.ToString()} - server tick {tick.ToString()}");
        }

        m_LastSeenUnityFrame = UnityEngine.Time.frameCount;

        // if (UILoadingScreen.Instance.IsLoading())
        // {
        //     return;
        // }


        bool isFirstPredictionTick = networkTime.IsFirstPredictionTick;
        bool isFinalPredictionTick = networkTime.IsFinalPredictionTick;
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        float dt = World.Time.DeltaTime;
        float frameDT = UnityEngine.Time.deltaTime;

        s_PlayerMovementTick = tick;

        foreach (var (predictedPlayer, modifiers) in
                 SystemAPI.Query<RefRW<PredictedPlayerGhost>,
                     RefRO<Unity.MP_FPS.Cards.PlayerModifierStacks>>().WithAll<Simulate>())
        {
            predictedPlayer.ValueRW.CrossWeaponCooldownRemaining = Mathf.Max(0f,
                predictedPlayer.ValueRO.CrossWeaponCooldownRemaining - dt);
            predictedPlayer.ValueRW.UtilityCooldownTimeRemaining = Mathf.Max(0f,
                predictedPlayer.ValueRO.UtilityCooldownTimeRemaining - dt);
            predictedPlayer.ValueRW.UtilityEffectTimeRemaining = Mathf.Max(0f,
                predictedPlayer.ValueRO.UtilityEffectTimeRemaining - dt);
            if (predictedPlayer.ValueRO.WeaponCooldown < float.MaxValue)
            {
                predictedPlayer.ValueRW.WeaponCooldown += dt;
            }

            if (predictedPlayer.ValueRO.EquippedWeaponID != WeaponManager.SecondaryWeaponId &&
                predictedPlayer.ValueRO.SecondaryWeaponCooldown < float.MaxValue)
            {
                predictedPlayer.ValueRW.SecondaryWeaponCooldown += dt;
            }

            if (predictedPlayer.ValueRO.SecondaryIsReloading)
            {
                predictedPlayer.ValueRW.SecondaryReloadTimer -= dt;
                if (predictedPlayer.ValueRO.SecondaryReloadTimer <= 0f)
                {
                    var secondaryWeapon = WeaponManager.Instance.ResolveRuntimeProfile(
                        WeaponManager.SecondaryWeaponId, in modifiers.ValueRO);
                    predictedPlayer.ValueRW.ControllerState.SecondaryWeaponAmmo =
                        secondaryWeapon.SourceProfile != null
                            ? secondaryWeapon.MagazineSize
                            : 0;
                    predictedPlayer.ValueRW.SecondaryReloadTimer = 0f;
                    predictedPlayer.ValueRW.SecondaryIsReloading = false;
                }
            }

            if (predictedPlayer.ValueRO.ControllerState.IsReloadingState)
            {
                predictedPlayer.ValueRW.ReloadTimer -= dt;
                if (predictedPlayer.ValueRO.ReloadTimer <= 0f)
                {
                    predictedPlayer.ValueRW.ControllerState.IsReloadingState = false;
                    var weaponProfile = WeaponManager.Instance.ResolveRuntimeProfile(
                        predictedPlayer.ValueRO.EquippedWeaponID, in modifiers.ValueRO);
                    if (weaponProfile.SourceProfile != null)
                    {
                        predictedPlayer.ValueRW.CurrentAmmo = weaponProfile.MagazineSize;
                    }
                }
            }
        }

        // ensure controller has been initialised
        foreach (var (predictedPlayer, entity) in
                 SystemAPI.Query<RefRW<PredictedPlayerGhost>>()
                     .WithEntityAccess()
                     .WithAll<Simulate, GhostGameObjectLink>()
                     .WithNone<PlayerControllerLink>())
        {
            var gameObjectLink = SystemAPI.ManagedAPI.GetComponent<GhostGameObjectLink>(entity);
            if (gameObjectLink.LinkedInstance != null && gameObjectLink.LinkedInstance.TryGetComponent<FirstPersonController>(out var controller))
            {
                ecb.AddComponent(entity, new PlayerControllerLink { Controller = controller });
            }
        }

        // pull out the client input
        // we process the input and create a list of commandInputs that need to be processed by the movement code
        // the movement code processes the inputs in order
        m_ProcessedClientInputCommands.Clear();
        var commands = m_ProcessedClientInputCommands;
        var clientCommandInputBufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>(true);
        
        foreach(var (predictedClient, entity) in
                SystemAPI.Query<RefRW<PredictedClientInput>>()
                    .WithEntityAccess()
                    .WithAll<GhostOwnerIsLocal, Simulate>())
        {
            // GameDebug.BurstLog("[PlayerPredictionSystem] PredictedClientInput is Simulate");
            if (!clientCommandInputBufferLookup.TryGetBuffer(entity, out var inputCommands) || inputCommands.Length == 0)
                continue;
            
            if (isFirstPredictionTick)
            {
                predictedClient.ValueRW.LastProcessedServerTick = tick - 1;

                // the server might have missed some input if the snapshot is running behind
                // we need to re-process some of the input the server has missed in this snapshot
                for (uint i = predictedClient.ValueRO.LastProcessedServerTick + 1; i < tick; i++)
                {
                    if (inputCommands.GetDataAtTick(new NetworkTick(i), out var missedCommand))
                    {
                        if (logPredictionWarnings)
                        {
                            Debug.LogWarning(
                                $"[PlayerPredictionSystem] Adding missed command for tick {missedCommand.Tick.TickIndexForValidTick.ToString()} (current is {tick.ToString()})");
                        }

                        commands.Add(missedCommand);
                    }
                }

                // and we need to get the latest tick to process
                // if (inputCommands.GetDataAtTick(new NetworkTick(tick), out var inputCommand))
                // {
                //     ClientCommandInput input = inputCommand;
                //     commands.Add(input);
                // }
            }

            // else
            // {
            if (inputCommands.GetDataAtTick(new NetworkTick(tick), out var inputCommand))
            {
                ClientCommandInput input = inputCommand;
                commands.Add(input);
                // commands.Add(inputCommand);
            }
            //}
        }

        if (LeaderboardManager.ClientInstance?.BlocksGameplayInput == true)
        {
            for (int commandIndex = 0; commandIndex < commands.Length;
                 commandIndex++)
            {
                ClientCommandInput commandInput = commands[commandIndex];
                commandInput.SuppressGameplayInput();
                commands[commandIndex] = commandInput;
            }
        }

        var allowTickBatching = AllowPredictedTickBatching();

        foreach(var (predictedPlayer,
                    controllerConsts, modifiers, entity)
                in SystemAPI.Query<RefRW<PredictedPlayerGhost>,
                    RefRO<PredictedPlayerControllerConsts>,
                    RefRO<Unity.MP_FPS.Cards.PlayerModifierStacks>>()
                    .WithEntityAccess()
                    .WithAll<Simulate, PlayerControllerLink>())
        {
            var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
            var playerGhost = controllerLink.Controller.GetComponent<PlayerGhost>();
            UtilityAbility resolvedUtility = playerGhost.ResolveUtilityAbility(
                predictedPlayer.ValueRO.SizeState, in modifiers.ValueRO);
            if (predictedPlayer.ValueRO.ActiveUtilityAbility !=
                (int)resolvedUtility)
            {
                predictedPlayer.ValueRW.UtilityEffectTimeRemaining = 0f;
            }
            predictedPlayer.ValueRW.ActiveUtilityAbility = (int)resolvedUtility;
            predictedPlayer.ValueRW.MoveSlowTimeRemaining = Mathf.Max(0f,
                predictedPlayer.ValueRO.MoveSlowTimeRemaining - dt);
            if (predictedPlayer.ValueRW.MoveSlowTimeRemaining <= 0f)
                predictedPlayer.ValueRW.MoveSlowPercent = 0f;
            // GameDebug.BurstLog("[PlayerPredictionSystem] PredictedPlayerGhost is Simulate");

            predictedPlayer.ValueRW.RequestApplyMovement = false;

            if (commands.Length > 0)
            {
                float accumulateDT = dt;

                // process input
                for (int i = 0; i < commands.Length; i++)
                {
                    var commandInput = commands[i];
                    if (commandInput.TryGetPlayerMovementInput(predictedPlayer.ValueRW.InputIndex, out var input))
                    {
                        predictedPlayer.ValueRW.LocalLookYawPitchDegrees = input.LookYawPitchDegrees;

                        if (input.UtilityPressed)
                        {
                            TryActivateUtility(ref predictedPlayer.ValueRW,
                                playerGhost,
                                in modifiers.ValueRO,
                                in controllerConsts.ValueRO.ControllerConsts,
                                commandInput.Tick.TickIndexForValidTick);
                        }

                        if (input.Reload && controllerLink.Controller.TryReloadBothWeapons(
                                ref predictedPlayer.ValueRW, in modifiers.ValueRO))
                        {
                            predictedPlayer.ValueRW.LastReloadTick =
                                commandInput.Tick.TickIndexForValidTick;
                        }
                        // Clear obsolete secondary-fire state retained for snapshot compatibility.
                        predictedPlayer.ValueRW.GrowShotPending = false;
                        predictedPlayer.ValueRW.GrowShotReleaseTimer = 0f;

                        predictedPlayer.ValueRW.ShrinkCastHeld = input.Shoot &&
                            controllerLink.Controller.CanUseWeapon(
                                in predictedPlayer.ValueRO,
                                WeaponManager.PrimaryWeaponId) &&
                            predictedPlayer.ValueRO.CurrentAmmo > 0 &&
                            !predictedPlayer.ValueRO.ControllerState.IsReloadingState;
                        var weaponBeforeAbility = default(WeaponManager.TemporaryWeaponState);
                        uint abilityWeaponId = WeaponManager.PrimaryWeaponId;
                        bool wantsToShoot = input.Shoot;
                        bool restoreWeaponAfterAbility = wantsToShoot &&
                            WeaponManager.Instance.TryBeginTemporaryWeapon(
                                ref predictedPlayer.ValueRW,
                                abilityWeaponId,
                                out weaponBeforeAbility);

                        // Resolve the cast and its muzzle from the input hand directly.
                        // This keeps left-click projectiles attached to the left-hand
                        // origin even while the temporary weapon state is being restored.
                        var runtimeProfile = WeaponManager.Instance.ResolveRuntimeProfile(
                            abilityWeaponId, in modifiers.ValueRO);
                        var weaponData = runtimeProfile.SourceProfile;
                        bool autoReloadRequested = false;
                        if (weaponData != null)
                        {
                            // Defer automatic reload until the temporary weapon state
                            // has been restored.
                            autoReloadRequested = wantsToShoot &&
                                predictedPlayer.ValueRO.CurrentAmmo <= 0;
                            
                            if (wantsToShoot &&
                                controllerLink.Controller.CanUseWeapon(
                                    in predictedPlayer.ValueRO, abilityWeaponId) &&
                                !predictedPlayer.ValueRO.ControllerState.IsReloadingState &&
                                predictedPlayer.ValueRO.CurrentAmmo > 0 &&
                                predictedPlayer.ValueRO.WeaponCooldown >= runtimeProfile.FireInterval)
                            {
                                predictedPlayer.ValueRW.WeaponCooldown = 0f;
                                predictedPlayer.ValueRW.CurrentAmmo--;
                                autoReloadRequested =
                                    predictedPlayer.ValueRO.CurrentAmmo <= 0;
                                controllerLink.Controller.StartCrossWeaponCooldown(
                                    ref predictedPlayer.ValueRW,
                                    abilityWeaponId);
                                predictedPlayer.ValueRW.LastShotTick =
                                    commandInput.Tick.TickIndexForValidTick;

                                float sizeOutputMultiplier = controllerLink.Controller
                                    .GetSizeOutputMultiplier(in predictedPlayer.ValueRO);
                                float explosionOutputMultiplier = controllerLink.Controller
                                    .GetExplosionOutputMultiplier(
                                        in predictedPlayer.ValueRO);
                                float weaponDamageMultiplier = controllerLink.Controller
                                    .GetWeaponDamageMultiplier(
                                        in predictedPlayer.ValueRO);
                                Unity.MP_FPS.Cards.SizeScaledWeaponOutput shotOutput =
                                    runtimeProfile.CreateSizeScaledOutput(
                                        sizeOutputMultiplier,
                                        explosionOutputMultiplier,
                                        weaponDamageMultiplier);
                                var controllerState = predictedPlayer.ValueRO.ControllerState;
                                var aimRotation = quaternion.Euler(
                                    math.radians(controllerState.PitchDegrees),
                                    math.radians(controllerState.YawDegrees),
                                    0f);

                                float3 eyePosition = playerGhost.CameraTarget.position;
                                var aimDirection = math.mul(aimRotation, new float3(0, 0, 1));
                                uint shotTick = commandInput.Tick.TickIndexForValidTick;
                                int localNetworkId = EntityManager.HasComponent<GhostOwner>(entity)
                                    ? EntityManager.GetComponentData<GhostOwner>(entity).NetworkId
                                    : 0;
                                Vector3 cosmeticDirection = ProjectileShotBuilder.ResolvePelletDirection(
                                    in runtimeProfile, aimDirection, shotTick,
                                    localNetworkId, 0);
                                // Match the server's deterministic hand origin exactly so predicted
                                // projectiles reconcile without a sideways correction.
                                float3 handShotOffset = playerGhost.GetScaledHandShotOffset(
                                    abilityWeaponId);
                                var shotOriginPosition = eyePosition +
                                                         math.mul(aimRotation, handShotOffset);
                                shotOriginPosition = playerGhost.ApplyShotOriginClearance(
                                    shotOriginPosition,
                                    abilityWeaponId);
                                if (input.HasProjectileLaunchRequest != 0 &&
                                    math.all(math.isfinite(
                                        input.ProjectileLaunchPosition)) &&
                                    math.all(math.isfinite(
                                        input.ProjectileAimDirection)) &&
                                    math.lengthsq(input.ProjectileAimDirection) > 0.000001f)
                                {
                                    shotOriginPosition = input.ProjectileLaunchPosition;
                                    aimDirection = math.normalize(
                                        input.ProjectileAimDirection);
                                }
                                controllerLink.Controller.ApplyAirborneShotThrust(
                                    ref predictedPlayer.ValueRW, aimDirection);
                                    
                                if (VisualEffectManager.ClientInstance != null)
                                {
                                    VisualEffectManager.ClientInstance.SpawnMuzzleFlash(playerGhost,
                                        abilityWeaponId, true);
                                }

                                if (weaponData.Type == WeaponType.Hitscan)
                                {
                                    if (Physics.Raycast(eyePosition, cosmeticDirection, out RaycastHit cosmeticHit,
                                        weaponData.HitscanRange, s_HitscanLayerMask))
                                    {
                                        Debug.DrawLine(shotOriginPosition, cosmeticHit.point, Color.yellow, 0.3f);
                                    }
                                    else
                                    {
                                        Vector3 endPoint = eyePosition +
                                                           (float3)cosmeticDirection * weaponData.HitscanRange;
                                        Debug.DrawLine(shotOriginPosition, endPoint, Color.cyan, 0.3f);
                                    }
                                }
                                else if (weaponData.Type == WeaponType.Projectile)
                                {
                                    uint sourceWeaponId = WeaponManager.Instance.GetWeaponId(
                                        runtimeProfile.SourceProfile, abilityWeaponId);
                                    int pelletCount = Mathf.Max(1, runtimeProfile.PelletCount);
                                    float targetRange = runtimeProfile.IsHoming
                                        ? weaponData.GuidedTargetRange
                                        : 1000f;

                                    for (int pelletIndex = 0; pelletIndex < pelletCount; pelletIndex++)
                                    {
                                        Vector3 pelletDirection = ProjectileShotBuilder
                                            .ResolvePelletDirection(in runtimeProfile, aimDirection,
                                                shotTick, localNetworkId, pelletIndex);
                                        Vector3 targetPoint;
                                        if (Physics.Raycast(eyePosition, pelletDirection,
                                                out RaycastHit aimHit, targetRange,
                                                s_ProjectileTargetLayerMask))
                                        {
                                            targetPoint = aimHit.point;
                                        }
                                        else
                                        {
                                            targetPoint = eyePosition +
                                                (float3)pelletDirection * targetRange;
                                        }

                                        Vector3 directionToTarget =
                                            runtimeProfile.ProjectileDownwardAcceleration > 0f
                                            ? pelletDirection
                                            : (targetPoint - (Vector3)shotOriginPosition).normalized;
                                        float launchSpeed =
                                            input.HasProjectileLaunchRequest != 0
                                                ? input.ProjectileLaunchSpeed
                                                : runtimeProfile.ProjectileSpeed;
                                        float downwardAcceleration =
                                            input.HasProjectileLaunchRequest != 0
                                                ? input.ProjectileDownwardAcceleration
                                                : runtimeProfile.ProjectileDownwardAcceleration;
                                        ProjectileShotSpec shotSpec = ProjectileShotBuilder.Build(
                                            directionToTarget, launchSpeed,
                                            downwardAcceleration, shotTick, localNetworkId,
                                            sourceWeaponId, pelletIndex);
                                        if (pelletIndex == 0 &&
                                            (runtimeProfile.HasExplosion ||
                                             downwardAcceleration > 0f))
                                        {
                                            // Projectile trajectory launch diagnostic disabled.
                                        }

                                        controllerLink.Controller.SpawnPredictedProjectile(
                                            shotTick,
                                            sourceWeaponId,
                                            shotOriginPosition,
                                            Quaternion.LookRotation(shotSpec.Direction),
                                            targetPoint,
                                            shotSpec.InitialVelocity,
                                            shotSpec.Acceleration,
                                            pelletIndex,
                                            runtimeProfile.IsHoming,
                                            runtimeProfile.HomingTurnSpeed,
                                            runtimeProfile.ProjectileSpeed,
                                            shotOutput.ProjectileScale,
                                            runtimeProfile.GrowsOverDistance,
                                            runtimeProfile.MaxBounces,
                                            runtimeProfile.GrowthPerMetre,
                                            runtimeProfile.BounceVelocityRetention,
                                            runtimeProfile.WallPenetrations,
                                            runtimeProfile.WallDamageRetention,
                                            !runtimeProfile.TracksPlayers,
                                            shotOutput.GravityWellPullStrength);
                                    }
                                }
                            }

                            if (restoreWeaponAfterAbility)
                            {
                                WeaponManager.Instance.EndTemporaryWeapon(
                                    ref predictedPlayer.ValueRW,
                                    in weaponBeforeAbility);
                            }

                            if (autoReloadRequested &&
                                controllerLink.Controller.TryReloadBothWeapons(
                                    ref predictedPlayer.ValueRW, in modifiers.ValueRO))
                            {
                                predictedPlayer.ValueRW.LastReloadTick =
                                    commandInput.Tick.TickIndexForValidTick;
                            }

                            FirstPersonController.ProcessInputs(ref predictedPlayer.ValueRW.ControllerState, input, accumulateDT);
                            controllerLink.Controller.ProcessSelfResize(
                                ref predictedPlayer.ValueRW, input, accumulateDT,
                                in modifiers.ValueRO);
                            controllerLink.Controller.ProcessResizeBounce(
                                ref predictedPlayer.ValueRW, input, accumulateDT);
                            controllerLink.Controller.GetSizeMovementMultipliers(
                                in predictedPlayer.ValueRO,
                                in modifiers.ValueRO,
                                out float speedMultiplier,
                                out float jumpHeightMultiplier);
                            if (modifiers.ValueRO.ShieldSpeed > 0 &&
                                predictedPlayer.ValueRO.ReflectShieldTimeRemaining > 0f)
                            {
                                var shieldSpeedCard = WeaponManager.Instance?.CardModifierCatalog?
                                    .FindByKind(Unity.MP_FPS.Cards.CardModifierKind.ShieldSpeed);
                                speedMultiplier *= 1f + modifiers.ValueRO.ShieldSpeed *
                                    Mathf.Max(0f, shieldSpeedCard?.ValuePerStack ?? 0.5f);
                            }
                            speedMultiplier *= playerGhost
                                .GetUtilityMovementSpeedMultiplier(
                                    in predictedPlayer.ValueRO);
                            FirstPersonController.AccumulateMovement(ref predictedPlayer.ValueRW.ControllerState,
                                ref predictedPlayer.ValueRW.AccumulatedMovement,
                                input,
                                controllerConsts.ValueRO.ControllerConsts,
                                predictedPlayer.ValueRO.LookYawOffsetDegrees,
                                speedMultiplier,
                                jumpHeightMultiplier,
                                accumulateDT);
                            playerGhost.AccumulateUtilityMovement(
                                ref predictedPlayer.ValueRW, accumulateDT);
                            controllerLink.Controller.AccumulateAirborneThrust(
                                ref predictedPlayer.ValueRW, accumulateDT);
                            
                            if (predictedPlayer.ValueRW.ControllerState.JumpTriggered)
                            {
                                predictedPlayer.ValueRW.LastJumpTick = commandInput.Tick.TickIndexForValidTick;
                                predictedPlayer.ValueRW.ControllerState.JumpTriggered = false;
                            }
                            if (predictedPlayer.ValueRW.ControllerState.LandTriggered)
                            {
                                predictedPlayer.ValueRW.LastLandTick = commandInput.Tick.TickIndexForValidTick;
                                predictedPlayer.ValueRW.ControllerState.LandTriggered = false;
                            }
                        }
                    }

                    // Size is controlled by weapon hits.
                }

                // apply
                // if tick batching is not allowed then we need to request an apply every tick
                // otherwise it will happen on the last tick anyway
                predictedPlayer.ValueRW.RequestApplyMovement = !allowTickBatching;
            }
            else if (logPredictionWarnings)
            {
                Debug.LogWarning("[PlayerPredictionSystem] Player is simulated but there are no commands to process");
            }

        }

        var applyDT = allowTickBatching ? frameDT : dt;

        bool checkForPredictionErrors = CheckForPredictionErrors.IsEnabled;
        var predictedPlayerGhostStatesBufferLookup = SystemAPI.GetBufferLookup<PredictedPlayerGhostState>(true);
        
        foreach (var (predictedPlayer,
                     transform,
                     playerControllerConsts,
                     entity)
                 in SystemAPI.Query<
                         RefRW<PredictedPlayerGhost>,
                         RefRO<LocalTransform>,
                         RefRO<PredictedPlayerControllerConsts>>() 
                     .WithAll<PlayerControllerLink>().WithEntityAccess())
        {
            var controllerLink = SystemAPI.ManagedAPI.GetComponent<PlayerControllerLink>(entity);
            var controllerConsts = playerControllerConsts.ValueRO.ControllerConsts;
            var predictedPlayerGhostStates = predictedPlayerGhostStatesBufferLookup[entity];
            
            if (isFinalPredictionTick || predictedPlayer.ValueRO.RequestApplyMovement)
            {
                if (math.lengthsq(predictedPlayer.ValueRO.AccumulatedMovement) > 0f)
                {
                    controllerLink.Controller.ApplyMovementUpdate(ref predictedPlayer.ValueRW.ControllerState,
                        controllerConsts,
                        predictedPlayer.ValueRO.AccumulatedMovement,
                        applyDT);

                    if (checkForPredictionErrors && !isFinalPredictionTick)
                    {
                        // check for prediction accuracy
                        // we avoid the final tick as it's almost certainly partial
                        // we want to check we are reconstructing the server ticks correctly
                        if (ServerPlayerMovementSystem.TryGetInstance(out var serverPlayerMovementSystem))
                        {
                            if (serverPlayerMovementSystem.MovementHistory.TryGetTick(tick, out var pos, out var rot))
                            {
                                // compare our calculation with the recorded
                                Debug.Assert(PlayerMovementHistory.HistoryMatches(pos, rot, predictedPlayer.ValueRO.ControllerState.CurrentPosition, predictedPlayer.ValueRO.ControllerState.CurrentRotation),
                                    $"[PlayerPredictionSystem] Mispredict detected at tick {tick} predicted pos {predictedPlayer.ValueRO.ControllerState.CurrentPosition} recorded {(float3)pos}");
                            }
                            else
                            {
                                // this is the first time we've processed this tick
                                // let's add our result
                                serverPlayerMovementSystem.MovementHistory.Add(tick,
                                    predictedPlayer.ValueRO.ControllerState.CurrentPosition,
                                    predictedPlayer.ValueRO.ControllerState.CurrentRotation);
                            }
                        }
                    }

                    predictedPlayer.ValueRW.AccumulatedMovement = float3.zero;
                }
                else
                {
                    // we should just apply our current state
                    controllerLink.Controller.ApplyPosRotImmediate(predictedPlayer.ValueRO.ControllerState);
                    controllerLink.Controller.GroundedCheck(ref predictedPlayer.ValueRW.ControllerState, controllerConsts);
                }
            }

            if (isFinalPredictionTick)
            {
                controllerLink.Controller.ApplyAnimatorState(in predictedPlayer.ValueRO.ControllerState, controllerConsts, frameDT);
            }
        }

        ecb.Playback(EntityManager);
    }

    private static void TryActivateUtility(ref PredictedPlayerGhost player,
        PlayerGhost playerGhost,
        in Unity.MP_FPS.Cards.PlayerModifierStacks modifiers,
        in FirstPersonController.ControllerConsts controllerConsts, uint tick)
    {
        UtilityAbility ability = playerGhost.ResolveUtilityAbility(
            player.SizeState, in modifiers);
        player.ActiveUtilityAbility = (int)ability;
        if (player.CurrentHealth <= 0f ||
            player.UtilityCooldownTimeRemaining > 0f ||
            ability == UtilityAbility.None ||
            !playerGhost.CanActivateUtility(ability, in player))
        {
            return;
        }

        player.UtilityCooldownDuration = playerGhost.UtilityCooldown;
        player.UtilityCooldownTimeRemaining = playerGhost.UtilityCooldown;
        player.UtilityEffectTimeRemaining = playerGhost
            .ResolveUtilityEffectDuration(ability);
        playerGhost.ApplyUtilityActivation(ref player, ability, in modifiers,
            in controllerConsts);
        player.LastUtilityAbilityTick = tick;
    }
    
    [FeatureToggle(Name = "prediction_enabletickbatching", Default = false)]
    private static readonly FeatureToggle EnableTickBatching;

    private static bool AllowPredictedTickBatching()
    {
        return EnableTickBatching.IsEnabled;
    }

    // private void DisablePrediction()
    // {
    //     // Fetch the singleton as RW as we're modifying singleton collection data.
    //     ref var ghostPredictionSwitchingQueues = ref SystemAPI.GetSingletonRW<GhostOwnerPredictedSwitchingQueue>().ValueRW;
    //
    //     if (PlayerGhostManager.TryGetClientInstance(out var playerGhostManager) && playerGhostManager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players))
    //     {
    //         foreach (var player in players)
    //         {
    //             ghostPredictionSwitchingQueues.SwitchOwnerQueue.Enqueue(new OwnerSwithchingEntry {TargetEntity = player.GhostGameObject.LinkedEntity, CurrentOwner = player.GhostGameObject.Owner, NewOwner = -1});
    //         }
    //     }
    // }
    //
    // private void EnablePrediction()
    // {
    //     // Fetch the singleton as RW as we're modifying singleton collection data.
    //     ref var ghostPredictionSwitchingQueues = ref SystemAPI.GetSingletonRW<GhostOwnerPredictedSwitchingQueue>().ValueRW;
    //
    //     if (PlayerGhostManager.TryGetClientInstance(out var playerGhostManager) && playerGhostManager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players))
    //     {
    //         foreach (var player in players)
    //         {
    //             ghostPredictionSwitchingQueues.SwitchOwnerQueue.Enqueue(new OwnerSwithchingEntry {TargetEntity = player.GhostGameObject.LinkedEntity, CurrentOwner = -1, NewOwner = player.GhostGameObject.Owner});
    //         }
    //     }
    // }

#if !NGS_SUBMISSION_BUILD
    [UnityEngine.Scripting.Preserve]
    public static void DisablePrediction(string[] args)
    {
        //Instance.DisablePrediction();
    }

    [UnityEngine.Scripting.Preserve]
    public static void EnablePrediction(string[] args)
    {
        //Instance.EnablePrediction();
    }
#endif
}
