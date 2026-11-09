using Unity.MP_FPS;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;
using UnityEngine;

public class InputSystemManager : Singleton<InputSystemManager>
{
    private const string k_GrenadeBindingOverridesKey = "GrenadeBindingOverrides";
    private const string k_SelfShrinkBindingOverridesKey = "SelfShrinkBindingOverrides";
    private const string k_SelfGrowBindingOverridesKey = "SelfGrowBindingOverrides";
    private const string k_ReflectBindingOverridesKey = "ReflectBindingOverrides";

    public const string SelfShrinkActionName = "SelfShrink";
    public const string SelfGrowActionName = "SelfGrow";
    public const string ReflectActionName = "Reflect";

    protected override bool Persistent => true;

    private bool m_HasPairedInitialUserDevices;

    public static InputUser GetUserByIndex(int index)
    {
        Debug.Assert(index >= 0 && index < InputUser.all.Count);
        return InputUser.all[index];
    }

    public static InputUser GetUserById(uint id)
    {
        foreach (var user in InputUser.all)
        {
            if (user.id == id)
            {
                return user;
            }
        }

        Debug.LogError($"[InputSystemManager] requested invalid user id: {id}");
        return default;
    }

    public static InputUser GetFirstInputUser()
    {
        var allPlayers = InputUser.all;
        return allPlayers[0];
    }

    public static bool TryGetFirstInputUser(out InputUser user)
    {
        if (InputUser.all.Count > 0)
        {
            user = InputUser.all[0];
            return user.valid;
        }

        user = default;
        return false;
    }

    public static bool TryGetUserById(int id, out InputUser user)
    {
        foreach (var u in InputUser.all)
        {
            if (u.id == id)
            {
                user = u;
                return true;
            }
        }

        user = default;
        return false;
    }

    public override void Awake()
    {
        base.Awake();

        InputSystem.onDeviceChange += OnDeviceChange;

        PairInitialUsers();
    }

    public override void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;

        foreach (var user in InputUser.all)
        {
            var controls = (InputSystem_Actions)user.actions;
            controls.Disable();
        }

        base.OnDestroy();
    }


    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (change == InputDeviceChange.Disconnected)
        {
            // placeholder
        }
        else if (change == InputDeviceChange.Reconnected)
        {
            // placeholder
        }
        else if (change == InputDeviceChange.Added)
        {
            // placeholder
            EnsureDevicePaired(device);
        }
        else if (change == InputDeviceChange.ConfigurationChanged)
        {
            // placeholder
            EnsureDevicePaired(device);
        }
    }

    private void EnsureDevicePaired(InputDevice device)
    {
        if (m_HasPairedInitialUserDevices
            && InputUser.GetUnpairedInputDevices().Contains(device))
        {
            if (TryFindUserWithLostDevice(device, out InputUser user))
            {
                Debug.Log($"[InputSystemManager]: Re-pairing device {device.name} user index {user.index}");
            }
            else
            {
                PairDeviceWithUser(device);
            }
        }
    }

    private InputUser PairDeviceWithUser(InputDevice device)
    {
        var user = InputUser.PerformPairingWithDevice(device);
        Debug.Log($"[InputSystemManager]: Pairing device {device.name} user index {user.index} id {user.id}");

        var gameControls = new InputSystem_Actions();
        ConfigureGameplayBindings(gameControls);
        gameControls.Enable();
        user.AssociateActionsWithUser(gameControls);

        var scheme = InputControlScheme.FindControlSchemeForDevice(device, gameControls.asset.controlSchemes);
        if (scheme.HasValue)
        {
            user.ActivateControlScheme(scheme.Value);
        }
        else
        {
            Debug.Log($"[InputSystemManager]: No control scheme is paired with device {device.name}");
        }

        return user;
    }

    public static void ApplySavedGrenadeBinding(InputSystem_Actions controls)
    {
        string bindingOverrides = PlayerPrefs.GetString(
            k_GrenadeBindingOverridesKey, string.Empty);
        if (!string.IsNullOrEmpty(bindingOverrides))
        {
            controls.FPS.ShootGrenade.LoadBindingOverridesFromJson(bindingOverrides);
            if (controls.FPS.ShootGrenade.bindings.Count > 0 &&
                string.Equals(controls.FPS.ShootGrenade.bindings[0].effectivePath,
                    "<Keyboard>/f", System.StringComparison.OrdinalIgnoreCase))
            {
                controls.FPS.ShootGrenade.RemoveBindingOverride(0);
                PlayerPrefs.DeleteKey(k_GrenadeBindingOverridesKey);
            }
        }
    }

    public static void SaveGrenadeBinding(InputSystem_Actions controls)
    {
        PlayerPrefs.SetString(k_GrenadeBindingOverridesKey,
            controls.FPS.ShootGrenade.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    public static void ConfigureGameplayBindings(InputSystem_Actions controls)
    {
        var fpsMap = controls.asset.FindActionMap("FPS", true);
        EnsureAction(fpsMap, SelfShrinkActionName, "<Keyboard>/leftCtrl");
        EnsureAction(fpsMap, SelfGrowActionName, "<Keyboard>/leftShift");
        EnsureAction(fpsMap, ReflectActionName, "<Keyboard>/f");

        ApplySavedGrenadeBinding(controls);
        ApplySavedBinding(GetGameplayAction(controls, SelfShrinkActionName),
            k_SelfShrinkBindingOverridesKey);
        ApplySavedBinding(GetGameplayAction(controls, SelfGrowActionName),
            k_SelfGrowBindingOverridesKey);
        ApplySavedBinding(GetGameplayAction(controls, ReflectActionName),
            k_ReflectBindingOverridesKey);
    }

    public static InputAction GetGameplayAction(InputSystem_Actions controls,
        string actionName)
    {
        return controls.asset.FindAction($"FPS/{actionName}", true);
    }

    public static void SaveGameplayBinding(InputAction action)
    {
        string preferencesKey = GetPreferencesKey(action.name);
        PlayerPrefs.SetString(preferencesKey,
            action.bindings[0].effectivePath);
        PlayerPrefs.Save();
    }

    public static string GetBindingDisplayName(InputAction action)
    {
        if (action == null || action.bindings.Count == 0)
        {
            return "Unavailable";
        }

        return InputControlPath.ToHumanReadableString(
            action.bindings[0].effectivePath,
            InputControlPath.HumanReadableStringOptions.OmitDevice);
    }

    private static void EnsureAction(InputActionMap map, string actionName,
        string defaultBinding)
    {
        if (map.FindAction(actionName, false) != null)
        {
            return;
        }

        map.AddAction(actionName, InputActionType.Button)
            .AddBinding(defaultBinding, groups: "Keyboard&Mouse");
    }

    private static void ApplySavedBinding(InputAction action, string preferencesKey)
    {
        string bindingPath = PlayerPrefs.GetString(preferencesKey, string.Empty);
        if (!string.IsNullOrEmpty(bindingPath))
        {
            action.ApplyBindingOverride(0, bindingPath);
        }
    }

    private static string GetPreferencesKey(string actionName)
    {
        return actionName switch
        {
            SelfShrinkActionName => k_SelfShrinkBindingOverridesKey,
            SelfGrowActionName => k_SelfGrowBindingOverridesKey,
            ReflectActionName => k_ReflectBindingOverridesKey,
            _ => throw new System.ArgumentOutOfRangeException(nameof(actionName), actionName,
                "No saved binding key is registered for this action.")
        };
    }

    private bool TryFindUserWithLostDevice(InputDevice desiredDevice, out InputUser existingUser)
    {
        foreach (var user in InputUser.all)
        {
            foreach (var device in user.lostDevices)
            {
                if (device == desiredDevice)
                {
                    existingUser = user;
                    return true;
                }
            }
        }

        existingUser = default;
        return false;
    }

    private void PairInitialUsers()
    {
        if (Keyboard.current != null)
        {
            var newUser = PairDeviceWithUser(Keyboard.current);

            if (Mouse.current != null)
            {
                InputUser.PerformPairingWithDevice(Mouse.current, newUser);
            }
        }

        m_HasPairedInitialUserDevices = true;
    }
}
