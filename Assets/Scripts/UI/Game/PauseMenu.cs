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
        }

        Button m_ResumeButton;
        Button m_MainMenuButton;
        Button m_QuitButton;
        Slider m_SensitivitySlider;

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
        }

        void OnDisable()
        {
            GameInput.Actions.UI.TogglePauseMenu.performed -= TogglePauseMenuVisibility;
            m_ResumeButton.clicked -= OnResumePressed;
            m_MainMenuButton.clicked -= OnMainMenuPressed;
            m_QuitButton.clicked -= OnQuitPressed;
            m_SensitivitySlider.UnregisterValueChangedCallback(OnSensitivityChanged);
        }

        void TogglePauseMenuVisibility(InputAction.CallbackContext obj)
        {
            EventSystem.current.SetSelectedGameObject(transform.parent.GetComponentInChildren<PanelRaycaster>().gameObject);
            if (!GameSettings.Instance.IsPauseMenuOpen)
            {
                RefreshSensitivitySlider();
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

        static void OnResumePressed() => GameSettings.Instance.IsPauseMenuOpen = false;

        static void OnMainMenuPressed()
        {
            GameManager.Instance.ReturnToMainMenuAsync();
            Utils.SetCursorVisible(true);            
        }

        static void OnQuitPressed() => GameManager.Instance.QuitAsync();
    }
}
