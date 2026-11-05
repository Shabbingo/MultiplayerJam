using Unity.Entities;
using Unity.MP_FPS;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial class ClientInputReaderSystem : SystemBase
{
    private float2 _accumulatedLook;

    private Entity _lastKnownPlayerEntity = Entity.Null;

    protected override void OnUpdate()
    {
        Entity currentLocalPlayer = Entity.Null;
        float3 playerPosition = float3.zero;
        PredictedPlayerGhost currentLocalPlayerData = default;

        // 1. Find the local player entity and its position
        foreach (var (transform, ghost, owner, entity) in SystemAPI.Query<
                         RefRO<LocalTransform>,
                         RefRO<PredictedPlayerGhost>,
                         RefRO<GhostOwnerIsLocal>>()
                     .WithEntityAccess())
        {
            currentLocalPlayer = entity;
            playerPosition = transform.ValueRO.Position;
            currentLocalPlayerData = ghost.ValueRO;
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

                ProcessGameplayInput(controls, ref playerInput);

                // The pulse uses the same per-player resources as self-resizing.
                // Empty shrink/grow charges therefore cannot be used to spam the
                // camera effect while the server rejects the actual resize.
                if (!resizePulseHandled && currentLocalPlayer != Entity.Null)
                {
                    if (playerInput.SelfShrinkPressed &&
                        currentLocalPlayerData.SelfShrinkCharges > 0)
                    {
                        CameraResizePulseEffect.TriggerConfigured(-1);
                        resizePulseHandled = true;
                    }
                    else if (playerInput.SelfGrowPressed &&
                             currentLocalPlayerData.SelfGrowCharges > 0)
                    {
                        CameraResizePulseEffect.TriggerConfigured(1);
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

                input.ValueRW.SetInput(0, playerInput);
                movementInput.ValueRW.SetInput(0, playerInput);
            }
            else
            {
                Debug.LogWarning($"[ClientInputReaderSystem] Input user is invalid");
            }
        }
    }

    private void ProcessGameplayInput(in InputSystem_Actions controls, ref PlayerInput playerInput)
    {
        playerInput.SetFlag(PlayerInput.InputFlag.Jump, controls.Player.Jump.triggered);
        playerInput.SetFlag(PlayerInput.InputFlag.Shoot, controls.FPS.ShootSingle.IsPressed());
        playerInput.SetFlag(PlayerInput.InputFlag.Reload, controls.FPS.Reload.triggered);
        playerInput.SetFlag(PlayerInput.InputFlag.TossGrenade,
            controls.FPS.ShootGrenade.triggered);
        // Right click is exclusively the growth projectile. Manual self-resizing has
        // deliberately been removed so firing can never alter the shooter's own size.
        playerInput.SetFlag(PlayerInput.InputFlag.GrowShot,
            controls.UI.RightClick.IsPressed());

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        bool gameplayKeysEnabled = keyboard != null && !GameSettings.Instance.IsPauseMenuOpen;
        playerInput.SetFlag(PlayerInput.InputFlag.SelfShrinkPressed,
            gameplayKeysEnabled &&
            (keyboard.leftCtrlKey.wasPressedThisFrame || keyboard.rightCtrlKey.wasPressedThisFrame));
        playerInput.SetFlag(PlayerInput.InputFlag.SelfGrowPressed,
            gameplayKeysEnabled &&
            (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame));
    }
}
