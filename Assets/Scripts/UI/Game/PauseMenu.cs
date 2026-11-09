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
            public const string CameraFovSlider = "CameraFovSlider";
            public const string SelfShrinkBindingButton = "SelfShrinkBindingButton";
            public const string SelfGrowBindingButton = "SelfGrowBindingButton";
            public const string ReflectBindingButton = "ReflectBindingButton";
            public const string GrenadeBindingButton = "GrenadeBindingButton";
        }

        Button m_ResumeButton;
        Button m_MainMenuButton;
        Button m_QuitButton;
        Slider m_SensitivitySlider;
        Slider m_CameraFovSlider;
        Button m_SelfShrinkBindingButton;
        Button m_SelfGrowBindingButton;
        Button m_ReflectBindingButton;
        Button m_GrenadeBindingButton;
        InputActionRebindingExtensions.RebindingOperation m_RebindOperation;
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

            m_CameraFovSlider = root.Q<Slider>(UIElementNames.CameraFovSlider);
            RefreshCameraFovSlider();
            m_CameraFovSlider.RegisterValueChangedCallback(OnCameraFovChanged);

            m_SelfShrinkBindingButton = root.Q<Button>(UIElementNames.SelfShrinkBindingButton);
            m_SelfGrowBindingButton = root.Q<Button>(UIElementNames.SelfGrowBindingButton);
            m_ReflectBindingButton = root.Q<Button>(UIElementNames.ReflectBindingButton);
            m_GrenadeBindingButton = root.Q<Button>(UIElementNames.GrenadeBindingButton);
            m_SelfShrinkBindingButton.clicked += StartSelfShrinkRebind;
            m_SelfGrowBindingButton.clicked += StartSelfGrowRebind;
            m_ReflectBindingButton.clicked += StartReflectRebind;
            m_GrenadeBindingButton.clicked += StartGrenadeRebind;
            RefreshBindings();
        }

        void OnDisable()
        {
            GameInput.Actions.UI.TogglePauseMenu.performed -= TogglePauseMenuVisibility;
            m_ResumeButton.clicked -= OnResumePressed;
            m_MainMenuButton.clicked -= OnMainMenuPressed;
            m_QuitButton.clicked -= OnQuitPressed;
            m_SensitivitySlider.UnregisterValueChangedCallback(OnSensitivityChanged);
            m_CameraFovSlider.UnregisterValueChangedCallback(OnCameraFovChanged);
            m_SelfShrinkBindingButton.clicked -= StartSelfShrinkRebind;
            m_SelfGrowBindingButton.clicked -= StartSelfGrowRebind;
            m_ReflectBindingButton.clicked -= StartReflectRebind;
            m_GrenadeBindingButton.clicked -= StartGrenadeRebind;

            if (m_RebindOperation != null)
            {
                m_RebindOperation.Cancel();
            }
        }

        void TogglePauseMenuVisibility(InputAction.CallbackContext obj)
        {
            if (m_RebindOperation != null ||
                m_IgnorePauseToggleFrame == Time.frameCount)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(transform.parent.GetComponentInChildren<PanelRaycaster>().gameObject);
            if (!GameSettings.Instance.IsPauseMenuOpen)
            {
                RefreshSensitivitySlider();
                RefreshCameraFovSlider();
                RefreshBindings();
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

        void RefreshCameraFovSlider()
        {
            float fov = GameSettings.Instance.CameraFov;
            m_CameraFovSlider.SetValueWithoutNotify(fov);
            m_CameraFovSlider.label = $"Camera FOV: {fov:0}";
        }

        void OnCameraFovChanged(ChangeEvent<float> evt)
        {
            GameSettings.Instance.CameraFov = evt.newValue;
            m_CameraFovSlider.label = $"Camera FOV: {GameSettings.Instance.CameraFov:0}";
        }

        void StartSelfShrinkRebind() => StartRebind(
            InputSystemManager.SelfShrinkActionName, m_SelfShrinkBindingButton);

        void StartSelfGrowRebind() => StartRebind(
            InputSystemManager.SelfGrowActionName, m_SelfGrowBindingButton);

        void StartReflectRebind() => StartRebind(
            InputSystemManager.ReflectActionName, m_ReflectBindingButton);

        void StartGrenadeRebind() => StartRebind("ShootGrenade", m_GrenadeBindingButton);

        void StartRebind(string actionName, Button bindingButton)
        {
            if (m_RebindOperation != null)
            {
                return;
            }

            if (!InputSystemManager.TryGetFirstInputUser(out var user))
            {
                return;
            }

            var controls = (InputSystem_Actions)user.actions;
            var action = GetAction(controls, actionName);
            action.Disable();
            bindingButton.text = "Press a key or mouse button...";

            m_RebindOperation = action.PerformInteractiveRebinding(0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnCancel(_ => FinishRebind(false))
                .OnComplete(_ => FinishRebind(true))
                .Start();
        }

        void FinishRebind(bool saveBinding)
        {
            if (m_RebindOperation == null)
            {
                return;
            }

            var action = m_RebindOperation.action;
            if (!saveBinding)
            {
                m_IgnorePauseToggleFrame = Time.frameCount;
            }

            m_RebindOperation.Dispose();
            m_RebindOperation = null;
            action.Enable();

            if (saveBinding && InputSystemManager.TryGetFirstInputUser(out var user))
            {
                var controls = (InputSystem_Actions)user.actions;
                if (action == controls.FPS.ShootGrenade)
                {
                    InputSystemManager.SaveGrenadeBinding(controls);
                }
                else
                {
                    InputSystemManager.SaveGameplayBinding(action);
                }
            }

            RefreshBindings();
        }

        void RefreshBindings()
        {
            if (!InputSystemManager.TryGetFirstInputUser(out var user))
            {
                m_SelfShrinkBindingButton.text = "Self Shrink Binding: Unavailable";
                m_SelfGrowBindingButton.text = "Self Grow Binding: Unavailable";
                m_ReflectBindingButton.text = "Reflect Shield Binding: Unavailable";
                m_GrenadeBindingButton.text = "Growth Missile Alt Binding: Unavailable";
                return;
            }

            var controls = (InputSystem_Actions)user.actions;
            SetBindingButtonText(m_SelfShrinkBindingButton, "Self Shrink Binding",
                InputSystemManager.GetGameplayAction(controls,
                    InputSystemManager.SelfShrinkActionName));
            SetBindingButtonText(m_SelfGrowBindingButton, "Self Grow Binding",
                InputSystemManager.GetGameplayAction(controls,
                    InputSystemManager.SelfGrowActionName));
            SetBindingButtonText(m_ReflectBindingButton, "Reflect Shield Binding",
                InputSystemManager.GetGameplayAction(controls,
                    InputSystemManager.ReflectActionName));
            SetBindingButtonText(m_GrenadeBindingButton, "Growth Missile Alt Binding",
                controls.FPS.ShootGrenade);
        }

        static InputAction GetAction(InputSystem_Actions controls, string actionName)
        {
            return actionName == "ShootGrenade"
                ? controls.FPS.ShootGrenade
                : InputSystemManager.GetGameplayAction(controls, actionName);
        }

        static void SetBindingButtonText(Button button, string label, InputAction action)
        {
            button.text = $"{label}: {InputSystemManager.GetBindingDisplayName(action)}";
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
