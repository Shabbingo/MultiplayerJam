using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenu : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string HidingBackground = "HidingBackground";
            public const string NameInputField = "PlayerNameField";
            public const string ChooseCharacterOption = "ChooseCharacterOption";
            public const string ConnectionModeOption = "ConnetionModeOption";
            public const string SessionNameLabel = "SessionName";
            public const string SessionInputField = "SessionNameField";
            public const string CreateGame = "CreateJoinGame";
            public const string StartHost = "StartHost";
            public const string ConnectToServer = "ConnectToServer";
            public const string ConnectionPanel = "ConnectionPanel";
            public const string OptionsPanel = "OptionsPanel";
            public const string OptionsButton = "OptionsButton";
            public const string OptionsBackButton = "OptionsBackButton";
            public const string SensitivitySlider = "MouseSensitivitySlider";
            public const string CameraFovSlider = "CameraFovSlider";
            public const string KillLimitSlider = "KillLimitSlider";
            public const string TimeLimitSlider = "TimeLimitSlider";
            public const string MapDropdown = "MapDropdown";
            public const string QuitGame = "QuitButton";
        }

        VisualElement m_MainMenu;
        RadioButtonGroup m_ChosseCharacterGroup;
        RadioButtonGroup m_ConnectionModeGroup;
        Label m_SessionNameLabel;
        TextField m_SessionNameField;
        Button m_CreateGameButton;
        Button m_StartHostButton;
        Button m_ConnectToServerButton;
        VisualElement m_ConnectionPanel;
        VisualElement m_OptionsPanel;
        Button m_OptionsButton;
        Button m_OptionsBackButton;
        Slider m_SensitivitySlider;
        Slider m_CameraFovSlider;
        SliderInt m_KillLimitSlider;
        SliderInt m_TimeLimitSlider;
        DropdownField m_MapDropdown;
        Button m_QuitButton;

        void OnEnable()
        {
            m_MainMenu = GetComponent<UIDocument>().rootVisualElement;

            m_MainMenu.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuStylePropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            var nameInputField = m_MainMenu.Q<TextField>(UIElementNames.NameInputField);
            nameInputField.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.PlayerName)),
                bindingMode = BindingMode.TwoWay,
            });

            m_ChosseCharacterGroup = m_MainMenu.Q<RadioButtonGroup>(UIElementNames.ChooseCharacterOption);
            m_ChosseCharacterGroup.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.PlayerCharacter)),
                bindingMode = BindingMode.TwoWay,
            });
            
            m_SessionNameLabel = m_MainMenu.Q<Label>(UIElementNames.SessionNameLabel);
            var connectionMode = m_ConnectionModeGroup = m_MainMenu.Q<RadioButtonGroup>(UIElementNames.ConnectionModeOption);
            connectionMode.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.ConnectionMode)),
                bindingMode = BindingMode.TwoWay,
            });
            m_ConnectionModeGroup.RegisterValueChangedCallback(OnConnectionModeChanged);

            var sessionInputField = m_SessionNameField = m_MainMenu.Q<TextField>(UIElementNames.SessionInputField);
            sessionInputField.SetBinding("value", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(nameof(GameSettings.SessionName)),
                bindingMode = BindingMode.TwoWay,
            });

            m_CreateGameButton = m_MainMenu.Q<Button>(UIElementNames.CreateGame);
            m_CreateGameButton.clicked += OnCreateGamePressed;

            m_StartHostButton = m_MainMenu.Q<Button>(UIElementNames.StartHost);
            m_StartHostButton.clicked += OnStartHostPressed;

            m_ConnectToServerButton = m_MainMenu.Q<Button>(UIElementNames.ConnectToServer);
            m_ConnectToServerButton.clicked += OnConnectToServerPressed;

            m_ConnectionPanel = m_MainMenu.Q<VisualElement>(UIElementNames.ConnectionPanel);
            m_OptionsPanel = m_MainMenu.Q<VisualElement>(UIElementNames.OptionsPanel);

            m_OptionsButton = m_MainMenu.Q<Button>(UIElementNames.OptionsButton);
            m_OptionsButton.clicked += OnOptionsPressed;

            m_OptionsBackButton = m_MainMenu.Q<Button>(UIElementNames.OptionsBackButton);
            m_OptionsBackButton.clicked += OnOptionsBackPressed;

            m_SensitivitySlider = m_MainMenu.Q<Slider>(UIElementNames.SensitivitySlider);
            m_SensitivitySlider.SetValueWithoutNotify(GameSettings.Instance.MouseSensitivity);
            UpdateSensitivityLabel(m_SensitivitySlider.value);
            m_SensitivitySlider.RegisterValueChangedCallback(OnSensitivityChanged);

            m_CameraFovSlider = m_MainMenu.Q<Slider>(UIElementNames.CameraFovSlider);
            RefreshCameraFovSlider();
            m_CameraFovSlider.RegisterValueChangedCallback(OnCameraFovChanged);

            m_KillLimitSlider = m_MainMenu.Q<SliderInt>(UIElementNames.KillLimitSlider);
            m_KillLimitSlider.RegisterValueChangedCallback(OnKillLimitChanged);
            m_TimeLimitSlider = m_MainMenu.Q<SliderInt>(UIElementNames.TimeLimitSlider);
            m_TimeLimitSlider.RegisterValueChangedCallback(OnTimeLimitChanged);
            m_MapDropdown = m_MainMenu.Q<DropdownField>(UIElementNames.MapDropdown);
            m_MapDropdown.RegisterValueChangedCallback(OnMapChanged);
            RefreshMatchSettings();
            RefreshMapChoices();

            m_QuitButton = m_MainMenu.Q<Button>(UIElementNames.QuitGame);
            m_QuitButton.clicked += OnQuitPressed;

            var hidingBackground = m_MainMenu.Q<VisualElement>(UIElementNames.HidingBackground);
            hidingBackground.SetBinding("style.display", new DataBinding
            {
                dataSource = GameSettings.Instance,
                dataSourcePath = new PropertyPath(GameSettings.MainMenuSceneLoadedPropertyName),
                bindingMode = BindingMode.ToTarget,
            });

            ToggleConnectionModeDisplay();
        }

        void OnDisable()
        {
            m_CreateGameButton.clicked -= OnCreateGamePressed;
            m_ConnectionModeGroup.UnregisterValueChangedCallback(OnConnectionModeChanged);
            m_ConnectToServerButton.clicked -= OnConnectToServerPressed;
            m_OptionsButton.clicked -= OnOptionsPressed;
            m_OptionsBackButton.clicked -= OnOptionsBackPressed;
            m_SensitivitySlider.UnregisterValueChangedCallback(OnSensitivityChanged);
            m_CameraFovSlider.UnregisterValueChangedCallback(OnCameraFovChanged);
            m_KillLimitSlider.UnregisterValueChangedCallback(OnKillLimitChanged);
            m_TimeLimitSlider.UnregisterValueChangedCallback(OnTimeLimitChanged);
            m_MapDropdown.UnregisterValueChangedCallback(OnMapChanged);
            m_QuitButton.clicked -= OnQuitPressed;
        }

        void OnConnectionModeChanged(ChangeEvent<int> evt)
        {
            GameSettings.Instance.ConnectionMode = evt.newValue;
            ToggleConnectionModeDisplay();
        }

        void ToggleConnectionModeDisplay()
        {
            if (GameSettings.Instance.ConnectionMode == 0)
            {
                m_SessionNameLabel.style.display = m_SessionNameField.style.display = DisplayStyle.Flex;
                m_CreateGameButton.style.display = DisplayStyle.Flex;
                m_StartHostButton.style.display = m_ConnectToServerButton.style.display = DisplayStyle.None;
            }
            else
            {
                m_SessionNameLabel.style.display = m_SessionNameField.style.display = DisplayStyle.None;
                m_CreateGameButton.style.display = DisplayStyle.None;;
                m_StartHostButton.style.display = m_ConnectToServerButton.style.display = DisplayStyle.Flex;
            }
        }

        void Start()
        {

        }

        static void OnCreateGamePressed() => GameManager.Instance.StartGameAsync(CreationType.CreateOrJoin);

        static void OnStartHostPressed() => GameManager.Instance.StartGameAsync(CreationType.Host);

        static void OnConnectToServerPressed() => GameManager.Instance.StartGameAsync(CreationType.ConnectAndJoin);

        void OnOptionsPressed()
        {
            m_SensitivitySlider.SetValueWithoutNotify(GameSettings.Instance.MouseSensitivity);
            UpdateSensitivityLabel(m_SensitivitySlider.value);
            RefreshCameraFovSlider();
            RefreshMatchSettings();
            RefreshMapChoices();
            m_ConnectionPanel.style.display = DisplayStyle.None;
            m_OptionsPanel.style.display = DisplayStyle.Flex;
        }

        void OnOptionsBackPressed()
        {
            m_OptionsPanel.style.display = DisplayStyle.None;
            m_ConnectionPanel.style.display = DisplayStyle.Flex;
        }

        void OnSensitivityChanged(ChangeEvent<float> evt)
        {
            GameSettings.Instance.MouseSensitivity = evt.newValue;
            UpdateSensitivityLabel(GameSettings.Instance.MouseSensitivity);
        }

        void UpdateSensitivityLabel(float value)
        {
            m_SensitivitySlider.label = $"Mouse Sensitivity: {value:0.00}";
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

        void RefreshMatchSettings()
        {
            int killLimit = GameSettings.Instance.MatchKillLimit;
            int timeLimit = GameSettings.Instance.MatchTimeLimitMinutes;
            m_KillLimitSlider.SetValueWithoutNotify(killLimit);
            m_TimeLimitSlider.SetValueWithoutNotify(timeLimit);
            m_KillLimitSlider.label = $"Kill Limit: {killLimit}";
            m_TimeLimitSlider.label = $"Time Limit: {timeLimit} Minutes";
        }

        void OnKillLimitChanged(ChangeEvent<int> evt)
        {
            GameSettings.Instance.MatchKillLimit = evt.newValue;
            m_KillLimitSlider.label = $"Kill Limit: {GameSettings.Instance.MatchKillLimit}";
        }

        void OnTimeLimitChanged(ChangeEvent<int> evt)
        {
            GameSettings.Instance.MatchTimeLimitMinutes = evt.newValue;
            m_TimeLimitSlider.label =
                $"Time Limit: {GameSettings.Instance.MatchTimeLimitMinutes} Minutes";
        }

        void RefreshMapChoices()
        {
            var choices = new List<string>();
            if (GameManager.Instance != null)
            {
                foreach (string sceneName in GameManager.Instance.AvailableMapScenes)
                {
                    if (!string.IsNullOrWhiteSpace(sceneName))
                    {
                        choices.Add(sceneName);
                    }
                }
            }

            if (choices.Count == 0)
            {
                choices.Add(GameManager.GameSceneName);
            }

            m_MapDropdown.choices = choices;
            string selectedMap = GameManager.Instance != null
                ? GameManager.Instance.GetValidatedMapScene(
                    GameSettings.Instance.SelectedMapScene)
                : GameManager.GameSceneName;
            GameSettings.Instance.SelectedMapScene = selectedMap;
            m_MapDropdown.SetValueWithoutNotify(selectedMap);
        }

        void OnMapChanged(ChangeEvent<string> evt)
        {
            if (!string.IsNullOrWhiteSpace(evt.newValue))
            {
                GameSettings.Instance.SelectedMapScene = evt.newValue;
            }
        }

        static void OnQuitPressed() => GameManager.Instance.QuitAsync();
    }
}
