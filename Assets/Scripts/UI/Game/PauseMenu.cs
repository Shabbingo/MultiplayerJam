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
            public const string GrenadeBindingButton = "GrenadeBindingButton";
        }

        Button m_ResumeButton;
        Button m_MainMenuButton;
        Button m_QuitButton;
        Slider m_SensitivitySlider;
        Slider m_CameraFovSlider;
        Button m_GrenadeBindingButton;
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

            m_CameraFovSlider = root.Q<Slider>(UIElementNames.CameraFovSlider);
            RefreshCameraFovSlider();
            m_CameraFovSlider.RegisterValueChangedCallback(OnCameraFovChanged);

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
            m_CameraFovSlider.UnregisterValueChangedCallback(OnCameraFovChanged);
            m_GrenadeBindingButton.clicked -= StartGrenadeRebind;

            if (m_GrenadeRebindOperation != null)
            {
                m_GrenadeRebindOperation.Cancel();
            }
        }

        void TogglePauseMenuVisibility(InputAction.CallbackContext obj)
        {
            if (m_GrenadeRebindOperation != null ||
                m_IgnorePauseToggleFrame == Time.frameCount)
            {
                return;
            }

            EventSystem.current.SetSelectedGameObject(transform.parent.GetComponentInChildren<PanelRaycaster>().gameObject);
            if (!GameSettings.Instance.IsPauseMenuOpen)
            {
                RefreshSensitivitySlider();
                RefreshCameraFovSlider();
                RefreshGrenadeBinding();
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

        void StartGrenadeRebind()
        {
            if (m_GrenadeRebindOperation != null)
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
                .WithControlsExcluding("<Keyboard>/f")
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
                m_GrenadeBindingButton.text = "Growth Missile Alt Binding: Unavailable";
                return;
            }

            var controls = (InputSystem_Actions)user.actions;
            string bindingPath = controls.FPS.ShootGrenade.bindings[0].effectivePath;
            string displayName = InputControlPath.ToHumanReadableString(bindingPath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            m_GrenadeBindingButton.text = $"Growth Missile Alt Binding: {displayName}";
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
