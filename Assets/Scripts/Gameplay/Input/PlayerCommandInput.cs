using NUnit.Framework.Constraints;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public struct PlayerInput
{
    public enum InputFlag
    {
        Jump = 1 << 0,
        Shoot = 1 << 1,
        Sprint = 1 << 2,
        Reload = 1 << 3,
        ToggleShrink = 1 << 4,
        SelectPrimaryWeapon = 1 << 5,
        SelectSecondaryWeapon = 1 << 6,
        ShrinkHeld = 1 << 7,
        HoldShrinkMode = 1 << 8,
        TossGrenade = 1 << 9
    }

    public float2 MoveInput;
    public float2 LookYawPitchDegrees;

    public uint InputFlags; // 4 bytes (32 flags)

    public bool Jump => (InputFlags & (uint)InputFlag.Jump) != 0;
    public bool Shoot => (InputFlags & (uint)InputFlag.Shoot) != 0;
    public bool Sprint => (InputFlags & (uint)InputFlag.Sprint) != 0;
    public bool Reload => (InputFlags & (uint)InputFlag.Reload) != 0;
    public bool ToggleShrink => (InputFlags & (uint)InputFlag.ToggleShrink) != 0;
    public bool ShrinkHeld => (InputFlags & (uint)InputFlag.ShrinkHeld) != 0;
    public bool HoldShrinkMode => (InputFlags & (uint)InputFlag.HoldShrinkMode) != 0;
    public bool TossGrenade => (InputFlags & (uint)InputFlag.TossGrenade) != 0;
    public bool SelectPrimaryWeapon => (InputFlags & (uint)InputFlag.SelectPrimaryWeapon) != 0;
    public bool SelectSecondaryWeapon => (InputFlags & (uint)InputFlag.SelectSecondaryWeapon) != 0;

    public void SetFlag(InputFlag flag, bool set)
    {
        if (set)
        {
            InputFlags |= (uint)flag;
        }
        else
        {
            InputFlags &= ~(uint)flag;
        }
    }

    public void UpdateFrom(in PlayerInput input)
    {
        // Event flags accumulate within a network tick, but held-state flags must
        // reflect the latest frame so releasing shrink is never lost.
        const uint shrinkStateMask =
            (uint)InputFlag.ShrinkHeld | (uint)InputFlag.HoldShrinkMode;
        InputFlags = (InputFlags & ~shrinkStateMask) |
                     (input.InputFlags & shrinkStateMask);
        InputFlags |= input.InputFlags & ~shrinkStateMask;
    }
}

public struct PlayerInputComponent : IComponentData
{
    public PlayerInput Input;
}

public struct PlayerClientCommandInputLookup : IComponentData
{
    public Entity ClientCommandInputEntity;
}

public struct ClientInput : IComponentData
{
    public PlayerInput PlayerInput;

    public void SetInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput;
    }
}

public struct ClientMovementInput : IComponentData
{
    public PlayerInput PlayerInput;

    public void SetInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput;
    }

    public void UpdateFrom(in ClientMovementInput clientInput)
    {
        PlayerInput.UpdateFrom(clientInput.PlayerInput);
    }
}

public struct ClientCommandInput : ICommandData
{
    public NetworkTick Tick { get; set; }

    public NetworkTick ClientInterpolationTick;

    public PlayerInput PlayerInput;

    public void SetPlayerMovementInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput = playerInput;
    }

    public void UpdatePlayerInput(int playerIndex, in PlayerInput playerInput)
    {
        PlayerInput.UpdateFrom(playerInput);
    }

    public bool TryGetPlayerMovementInput(int playerIndex, out PlayerInput playerInput)
    {
        playerInput = PlayerInput;
        return true;
    }

    public void UpdateFrom(in ClientMovementInput clientInput)
    {
        PlayerInput.UpdateFrom(clientInput.PlayerInput);
    }

    public void SetFrom(in ClientMovementInput clientInput)
    {
        PlayerInput = clientInput.PlayerInput;
    }
}
