using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Cards;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial class ClientInputReaderSystem : SystemBase
{
    private float2 _accumulatedLook;
    private bool _shrinkInputHeld;
    private bool _growInputHeld;

    private Entity _lastKnownPlayerEntity = Entity.Null;

    protected override void OnUpdate()
    {
        Entity currentLocalPlayer = Entity.Null;
        float3 playerPosition = float3.zero;
        PredictedPlayerGhost currentLocalPlayerData = default;
        PlayerModifierStacks currentLocalModifiers = default;
        FirstPersonController currentLocalPlayerController = null;

        // 1. Find the local player entity and its position
        foreach (var (transform, ghost, modifiers, owner, entity) in SystemAPI.Query<
                         RefRO<LocalTransform>,
                         RefRO<PredictedPlayerGhost>,
                         RefRO<PlayerModifierStacks>,
                         RefRO<GhostOwnerIsLocal>>()
                     .WithEntityAccess())
        {
            currentLocalPlayer = entity;
            playerPosition = transform.ValueRO.Position;
            currentLocalPlayerData = ghost.ValueRO;
            currentLocalModifiers = modifiers.ValueRO;
            if (EntityManager.HasComponent<PlayerControllerLink>(entity))
            {
                currentLocalPlayerController = EntityManager
                    .GetComponentObject<PlayerControllerLink>(entity).Controller;
            }
            break; // Found local player, stop searching
        }

        // 2. Check for Respawn (Entity ID changed)
        if (currentLocalPlayer != Entity.Null)
        {
            if (currentLocalPlayer != _lastKnownPlayerEntity)
            {
                float3 directionToOrigin = math.normalizesafe(new float3(0, 0, -12) - playerPosition);

                // Calculate Yaw (rotation around Y axis)
                // atan2(x, z) gives the angle in radians from the forward (Z) axis
                float yawRadians = math.atan2(directionToOrigin.x, directionToOrigin.z);

                // RESET LOOK: Yaw to face center, Pitch to 0 (Horizontal)
                _accumulatedLook = new float2(math.degrees(yawRadians), 0f);

                // Update tracker so we don't reset again while this character is alive
                _lastKnownPlayerEntity = currentLocalPlayer;
            }
        }
        else
        {
            // Player is dead or not yet spawned.
            // Reset the tracker so the *next* spawn triggers the logic.
            _lastKnownPlayerEntity = Entity.Null;
        }

        bool resizePulseHandled = false;
        foreach (var (input, movementInput) in SystemAPI.Query<RefRW<ClientInput>, RefRW<ClientMovementInput>>())
        {
            input.ValueRW = new ClientInput();
            movementInput.ValueRW = new ClientMovementInput();

            var user = InputSystemManager.GetFirstInputUser();

            if (user.valid)
            {
                var controls = (InputSystem_Actions)user.actions;

                var playerInput = new PlayerInput();

                if (LeaderboardManager.ClientInstance?.BlocksGameplayInput == true)
                {
                    // UI Toolkit still receives mouse input, but no gameplay input
                    // reaches prediction or the server while the lobby is open.
                    _shrinkInputHeld = false;
                    _growInputHeld = false;
                    playerInput.LookYawPitchDegrees = _accumulatedLook;
                    input.ValueRW.SetInput(0, playerInput);
                    movementInput.ValueRW.SetInput(0, playerInput);
                    continue;
                }

                ProcessGameplayInput(controls, ref playerInput);

                bool gameplayKeysEnabled = !GameSettings.Instance.IsPauseMenuOpen &&
                                           !CardTestingPanelController.IsOpen;
                bool shrinkHeld = gameplayKeysEnabled &&
                    InputSystemManager.GetGameplayAction(controls,
                        InputSystemManager.SelfShrinkActionName).IsPressed();
                bool growHeld = gameplayKeysEnabled &&
                    InputSystemManager.GetGameplayAction(controls,
                        InputSystemManager.SelfGrowActionName).IsPressed();
                bool shrinkPressedThisHold = shrinkHeld && !_shrinkInputHeld;
                bool growPressedThisHold = growHeld && !_growInputHeld;
                bool shrinkReleasedThisHold = !shrinkHeld && _shrinkInputHeld;
                bool growReleasedThisHold = !growHeld && _growInputHeld;
                _shrinkInputHeld = shrinkHeld;
                _growInputHeld = growHeld;

                // Resize is driven by transitions rather than a continuously sampled
                // held value. Prediction can replay these commands safely without
                // toggling the stance on every network tick.
                playerInput.SetFlag(PlayerInput.InputFlag.SelfShrinkPressed,
                    shrinkPressedThisHold);
                playerInput.SetFlag(PlayerInput.InputFlag.SelfGrowPressed,
                    growPressedThisHold);
                playerInput.SetFlag(PlayerInput.InputFlag.SelfShrinkReleased,
                    shrinkReleasedThisHold);
                playerInput.SetFlag(PlayerInput.InputFlag.SelfGrowReleased,
                    growReleasedThisHold);
                playerInput.SetFlag(PlayerInput.InputFlag.ToggleSelfResizeMode,
                    GameSettings.Instance.ToggleSelfResize);

                // Visual feedback uses the same persistent press edge. This ECS
                // group may update more than once during one rendered frame.
                if (!resizePulseHandled && currentLocalPlayerController != null)
                {
                    int resizeDirection = currentLocalPlayerController
                        .GetSelfResizeDirection(in currentLocalPlayerData,
                            growPressedThisHold, shrinkPressedThisHold,
                            GameSettings.Instance.ToggleSelfResize);

                    // Do not pulse when both stance keys are physically held.
                    if (shrinkHeld && growHeld)
                    {
                        resizeDirection = 0;
                    }

                    if (resizeDirection != 0)
                    {
                        CameraResizePulseEffect.TriggerConfigured(resizeDirection);
                        resizePulseHandled = true;
                    }
                }

                // movement
                float2 moveVector = controls.Player.Move.ReadValue<Vector2>();
                playerInput.MoveInput = moveVector;

                var addedDelta = (float2)controls.Player.LookDelta.ReadValue<Vector2>();

                var lookDelta = addedDelta * GameSettings.Instance.MouseSensitivity;

                // Accumulate the delta to our persistent rotation value
                _accumulatedLook.x += lookDelta.x;
                _accumulatedLook.y -= lookDelta.y; // Pitch is typically inverted

                // Clamp the vertical angle to prevent looking straight up/down and flipping
                _accumulatedLook.y = math.clamp(_accumulatedLook.y, -85f, 85f);

                // Assign the full, accumulated angle to the input struct
                playerInput.LookYawPitchDegrees = _accumulatedLook;

                // Capture the launch pose on the client at input-sampling time.
                // Prediction and the server will consume these exact values from
                // the tick command instead of independently rebuilding the shot.
                if (playerInput.Shoot && currentLocalPlayerController != null)
                {
                    PlayerGhost playerGhost = currentLocalPlayerController
                        .GetComponent<PlayerGhost>();
                    if (playerGhost != null && playerGhost.CameraTarget != null)
                    {
                        quaternion launchRotation = playerGhost.CameraTarget.rotation;
                        float3 launchDirection = playerGhost.CameraTarget.forward;
                        float3 launchPosition = (float3)playerGhost.CameraTarget.position +
                            math.mul(launchRotation,
                                playerGhost.GetScaledHandShotOffset(
                                    WeaponManager.PrimaryWeaponId));
                        launchPosition = playerGhost.ApplyShotOriginClearance(
                            launchPosition, WeaponManager.PrimaryWeaponId);

                        playerInput.HasProjectileLaunchRequest = 1;
                        playerInput.ProjectileLaunchPosition = launchPosition;
                        playerInput.ProjectileAimDirection = launchDirection;
                        if (WeaponManager.Instance != null)
                        {
                            RuntimeWeaponProfile launchProfile = WeaponManager.Instance
                                .ResolveRuntimeProfile(
                                    WeaponManager.PrimaryWeaponId,
                                    in currentLocalModifiers);
                            playerInput.ProjectileLaunchSpeed =
                                launchProfile.ProjectileSpeed;
                            playerInput.ProjectileDownwardAcceleration =
                                launchProfile.ProjectileDownwardAcceleration;
                        }
                    }
                }

                input.ValueRW.SetInput(0, playerInput);
                movementInput.ValueRW.SetInput(0, playerInput);
            }
            else
            {
                _shrinkInputHeld = false;
                _growInputHeld = false;
                Debug.LogWarning($"[ClientInputReaderSystem] Input user is invalid");
            }
        }
    }

    private void ProcessGameplayInput(in InputSystem_Actions controls, ref PlayerInput playerInput)
    {
        bool gameplayKeysEnabled = !GameSettings.Instance.IsPauseMenuOpen &&
                                   !CardTestingPanelController.IsOpen;
        bool reflectPressed = gameplayKeysEnabled &&
            InputSystemManager.GetGameplayAction(controls,
                InputSystemManager.ReflectActionName).WasPressedThisFrame();
        bool utilityPressed = gameplayKeysEnabled &&
            InputSystemManager.GetGameplayAction(controls,
                InputSystemManager.UtilityActionName).WasPressedThisFrame();

        playerInput.SetFlag(PlayerInput.InputFlag.Jump, controls.Player.Jump.triggered);
        bool primaryFireHeld = gameplayKeysEnabled &&
                               controls.FPS.ShootSingle.IsPressed();
        playerInput.SetFlag(PlayerInput.InputFlag.Shoot, primaryFireHeld);
        playerInput.SetFlag(PlayerInput.InputFlag.Reload, controls.FPS.Reload.triggered);
        // Legacy secondary-fire flags remain in the network layout so old command
        // buffers deserialize, but the right hand is now utility-only.
        playerInput.SetFlag(PlayerInput.InputFlag.TossGrenade, false);
        playerInput.SetFlag(PlayerInput.InputFlag.GrowShot, false);

        playerInput.SetFlag(PlayerInput.InputFlag.ReflectPressed,
            reflectPressed);
        playerInput.SetFlag(PlayerInput.InputFlag.UtilityPressed,
            utilityPressed);
    }
}
