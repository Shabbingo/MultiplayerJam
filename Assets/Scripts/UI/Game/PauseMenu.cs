using Unity.Properties;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(UIDocument))]
    public class PauseMenu : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string ResumeButton = "ResumeButton";
            public const string MainMenuButton = "MainMenuButton";
            public const string QuitButton = "QuitButton";
            public const string SensitivitySlider = "MouseSensitivitySlider";
            public const string HoldToShrinkToggle = "HoldToShrinkToggle";
            public const string ShrinkBindingButton = "ShrinkBindingButton";
            public const string GrenadeBindingButton = "GrenadeBindingButton";
        }

        Button m_ResumeButton;
        Button m_MainMenuButton;
        Button m_QuitButton;
        Slider m_SensitivitySlider;
        Toggle m_HoldToShrinkToggle;
        Button m_ShrinkBindingButton;
        Button m_GrenadeBindingButton;
        InputActionRebindingExtensions.RebindingOperation m_ShrinkRebindOperation;
        InputActionRebindingExtensions.RebindingOperation m_GrenadeRebindOperation;
        int m_IgnorePauseToggleFrame = -1;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            GameInput.Actions.UI.TogglePauseMenu.performed += TogglePauseMenuVisibility;

            root.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.PauseMenuStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });
          
            m_ResumeButton = root.Q<Button>(UIElementNames.ResumeButton);
            m_ResumeButton.clicked += OnResumePressed;

            m_MainMenuButton = root.Q<Button>(UIElementNames.MainMenuButton);
            m_MainMenuButton.clicked += OnMainMenuPressed;
            m_MainMenuButton.SetEnabled(GameManager.CanUseMainMenu);

            m_QuitButton = root.Q<Button>(UIElementNames.QuitButton);
            m_QuitButton.clicked += OnQuitPressed;

            m_SensitivitySlider = root.Q<Slider>(UIElementNames.SensitivitySlider);
            RefreshSensitivitySlider();
            m_SensitivitySlider.RegisterValueChangedCallback(OnSensitivityChanged);

            m_HoldToShrinkToggle = root.Q<Toggle>(UIElementNames.HoldToShrinkToggle);
            m_HoldToShrinkToggle.SetValueWithoutNotify(GameSettings.Instance.HoldToShrink);
            m_HoldToShrinkToggle.RegisterValueChangedCallback(OnHoldToShrinkChanged);

            m_ShrinkBindingButton = root.Q<Button>(UIElementNames.ShrinkBindingButton);
            m_ShrinkBindingButton.clicked += StartShrinkRebind;
            RefreshShrinkBinding();

            m_GrenadeBindingButton = root.Q<Button>(UIElementNames.GrenadeBindingButton);
            m_GrenadeBindingButton.clicked += StartGrenadeRebind;
            RefreshGrenadeBinding();
        }

        void OnDisable()
        {
            GameInput.Actions.UI.TogglePauseMenu.performed -= TogglePauseMenuVisibility;
            m_ResumeButton.clicked -= OnResumePressed;
            m_MainMenuButton.clicked -= OnMainMenuPressed;
            m_QuitButton.clicked -= OnQuitPressed;
            m_SensitivitySlider.UnregisterValueChangedCallback(OnSensitivityChanged);
            m_HoldToShrinkToggle.UnregisterValueChangedCallback(OnHoldToShrinkChanged);
            m_ShrinkBindingButton.clicked -= StartShrinkRebind;
            m_GrenadeBindingButton.clicked -= StartGrenadeRebind;

            if (m_ShrinkRebindOperation != null)
            {
                m_ShrinkRebindOperation.Cancel();
            }

            if (m_GrenadeRebindOperation != null)
            {
                m_GrenadeRebindOperation.Cancel();
            }
        }

        void TogglePauseMenuVisibility(InputAction.CallbackContext obj)
        {
            if (m_ShrinkRebindOperation != null || m_GrenadeRebindOperation != null ||
                m_IgnorePauseToggleFrame == Time.frameCount)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(transform.parent.GetComponentInChildren<PanelRaycaster>().gameObject);
            if (!GameSettings.Instance.IsPauseMenuOpen)
            {
                RefreshSensitivitySlider();
                RefreshShrinkControls();
            }
            GameSettings.Instance.IsPauseMenuOpen = !GameSettings.Instance.IsPauseMenuOpen;
        }

        void RefreshSensitivitySlider()
        {
            float sensitivity = GameSettings.Instance.MouseSensitivity;
            m_SensitivitySlider.SetValueWithoutNotify(sensitivity);
            m_SensitivitySlider.label = $"Mouse Sensitivity: {sensitivity:0.00}";
        }

        void OnSensitivityChanged(ChangeEvent<float> evt)
        {
            GameSettings.Instance.MouseSensitivity = evt.newValue;
            m_SensitivitySlider.label = $"Mouse Sensitivity: {GameSettings.Instance.MouseSensitivity:0.00}";
        }

        void RefreshShrinkControls()
        {
            m_HoldToShrinkToggle.SetValueWithoutNotify(GameSettings.Instance.HoldToShrink);
            RefreshShrinkBinding();
            RefreshGrenadeBinding();
        }

        void OnHoldToShrinkChanged(ChangeEvent<bool> evt)
        {
            GameSettings.Instance.HoldToShrink = evt.newValue;
        }

        void StartShrinkRebind()
        {
            if (m_ShrinkRebindOperation != null || m_GrenadeRebindOperation != null)
            {
                return;
            }

            if (!InputSystemManager.TryGetFirstInputUser(out var user))
            {
                return;
            }

            var controls = (InputSystem_Actions)user.actions;
            var shrinkAction = controls.UI.RightClick;
            shrinkAction.Disable();
            m_ShrinkBindingButton.text = "Press a key or mouse button...";

            m_ShrinkRebindOperation = shrinkAction.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(_ => FinishShrinkRebind(false))
                .OnComplete(_ => FinishShrinkRebind(true))
                .Start();
        }

        void FinishShrinkRebind(bool saveBinding)
        {
            if (m_ShrinkRebindOperation == null)
            {
                return;
            }

            var shrinkAction = m_ShrinkRebindOperation.action;
            if (!saveBinding)
            {
                m_IgnorePauseToggleFrame = Time.frameCount;
            }
            m_ShrinkRebindOperation.Dispose();
            m_ShrinkRebindOperation = null;
            shrinkAction.Enable();

            if (saveBinding)
            {
                if (InputSystemManager.TryGetFirstInputUser(out var user))
                {
                    var controls = (InputSystem_Actions)user.actions;
                    InputSystemManager.SaveShrinkBinding(controls);
                }
            }

            RefreshShrinkBinding();
        }

        void RefreshShrinkBinding()
        {
            if (!InputSystemManager.TryGetFirstInputUser(out var user))
            {
                m_ShrinkBindingButton.text = "Shrink Binding: Unavailable";
                return;
            }

            var controls = (InputSystem_Actions)user.actions;
            string bindingPath = controls.UI.RightClick.bindings[0].effectivePath;
            string displayName = InputControlPath.ToHumanReadableString(bindingPath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            m_ShrinkBindingButton.text = $"Shrink Binding: {displayName}";
        }

        void StartGrenadeRebind()
        {
            if (m_GrenadeRebindOperation != null || m_ShrinkRebindOperation != null)
            {
                return;
            }

            if (!InputSystemManager.TryGetFirstInputUser(out var user))
            {
                return;
            }

            var controls = (InputSystem_Actions)user.actions;
            var grenadeAction = controls.FPS.ShootGrenade;
            grenadeAction.Disable();
            m_GrenadeBindingButton.text = "Press a key or mouse button...";

            m_GrenadeRebindOperation = grenadeAction.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(_ => FinishGrenadeRebind(false))
                .OnComplete(_ => FinishGrenadeRebind(true))
                .Start();
        }

        void FinishGrenadeRebind(bool saveBinding)
        {
            if (m_GrenadeRebindOperation == null)
            {
                return;
            }

            var grenadeAction = m_GrenadeRebindOperation.action;
            if (!saveBinding)
            {
                m_IgnorePauseToggleFrame = Time.frameCount;
            }

            m_GrenadeRebindOperation.Dispose();
            m_GrenadeRebindOperation = null;
            grenadeAction.Enable();

            if (saveBinding && InputSystemManager.TryGetFirstInputUser(out var user))
            {
                var controls = (InputSystem_Actions)user.actions;
                InputSystemManager.SaveGrenadeBinding(controls);
            }

            RefreshGrenadeBinding();
        }

        void RefreshGrenadeBinding()
        {
            if (!InputSystemManager.TryGetFirstInputUser(out var user))
            {
                m_GrenadeBindingButton.text = "Toss Grenade Binding: Unavailable";
                return;
            }

            var controls = (InputSystem_Actions)user.actions;
            string bindingPath = controls.FPS.ShootGrenade.bindings[0].effectivePath;
            string displayName = InputControlPath.ToHumanReadableString(bindingPath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            m_GrenadeBindingButton.text = $"Toss Grenade Binding: {displayName}";
        }

        static void OnResumePressed() => GameSettings.Instance.IsPauseMenuOpen = false;

        static void OnMainMenuPressed()
        {
            GameManager.Instance.ReturnToMainMenuAsync();
            Utils.SetCursorVisible(true);            
        }

        static void OnQuitPressed() => GameManager.Instance.QuitAsync();
    }
}
