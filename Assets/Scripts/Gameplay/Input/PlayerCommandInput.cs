using NUnit.Framework.Constraints;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

public struct PlayerInput
{
    private const uint HeldStateMask = (uint)InputFlag.GrowShot;

    public enum InputFlag
    {
        Jump = 1 << 0,
        Shoot = 1 << 1,
        Reload = 1 << 3,
        TossGrenade = 1 << 9,
        GrowShot = 1 << 10,
        SelfShrinkPressed = 1 << 11,
        SelfGrowPressed = 1 << 12,
        ReflectPressed = 1 << 13,
        SelfShrinkReleased = 1 << 14,
        SelfGrowReleased = 1 << 15
    }

    public float2 MoveInput;
    public float2 LookYawPitchDegrees;

    public uint InputFlags; // 4 bytes (32 flags)

    public bool Jump => (InputFlags & (uint)InputFlag.Jump) != 0;
    public bool Shoot => (InputFlags & (uint)InputFlag.Shoot) != 0;
    public bool Reload => (InputFlags & (uint)InputFlag.Reload) != 0;
    public bool TossGrenade => (InputFlags & (uint)InputFlag.TossGrenade) != 0;
    public bool GrowShot => (InputFlags & (uint)InputFlag.GrowShot) != 0;
    public bool SelfShrinkPressed => (InputFlags & (uint)InputFlag.SelfShrinkPressed) != 0;
    public bool SelfGrowPressed => (InputFlags & (uint)InputFlag.SelfGrowPressed) != 0;
    public bool SelfShrinkReleased => (InputFlags & (uint)InputFlag.SelfShrinkReleased) != 0;
    public bool SelfGrowReleased => (InputFlags & (uint)InputFlag.SelfGrowReleased) != 0;
    public bool ReflectPressed => (InputFlags & (uint)InputFlag.ReflectPressed) != 0;

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
        // Held-state flags must also transmit their release instead of accumulating
        // like one-frame events.
        InputFlags = (InputFlags & ~HeldStateMask) |
                     (input.InputFlags & HeldStateMask);
        MergeTransientFlagsFrom(input);
    }

    public void MergeTransientFlagsFrom(in PlayerInput input)
    {
        // An older sample may contain a one-frame action that must survive until
        // the next command tick, but its held keys must not replace the newest
        // physical key state.
        InputFlags |= input.InputFlags & ~HeldStateMask;
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

    public void MergeTransientFlagsFrom(in ClientMovementInput clientInput)
    {
        PlayerInput.MergeTransientFlagsFrom(clientInput.PlayerInput);
    }

    public void SetFrom(in ClientMovementInput clientInput)
    {
        PlayerInput = clientInput.PlayerInput;
    }
}
