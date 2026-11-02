using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.UI
{
    public class LeaderboardUi : MonoBehaviour
    {
        public VisualTreeAsset ScoreItemTemplate;

        EntityManager _entityManager;

        World _currentClientWorld;

        EntityQuery _inGameQuery;

        VisualElement _rootElement;

        ListView _listView;
        Label _matchResultLabel;
        Label _matchRestartLabel;
        Label _matchTimerLabel;
        readonly List<ScoreUiPlayerInfo> _items = new();

        private float _timer;
        private bool _scoreboardHeld;
        private bool _wasMatchEnded;

        void Awake()
        {
            var uiDocument = GetComponent<UIDocument>();
            _rootElement = uiDocument.rootVisualElement;
            _rootElement.style.display = DisplayStyle.None;

            _listView = _rootElement.Q<ListView>("score-items");
            _matchResultLabel = _rootElement.Q<Label>("match-result");
            _matchRestartLabel = _rootElement.Q<Label>("match-restart");
            _matchTimerLabel = _rootElement.Q<Label>("match-timer");

            _listView.itemsSource = _items;
            _listView.makeItem = () => ScoreItemTemplate.Instantiate();
            _listView.bindItem = (element, i) =>
            {
                var player = _items[i];
                var nameLabel = element.Q<Label>("name");
                var killsLabel = element.Q<Label>("kills");
                var deathsLabel = element.Q<Label>("deaths");
                nameLabel.text = player.PlayerName;
                killsLabel.text = player.Kills.ToString();
                deathsLabel.text = player.Deaths.ToString();
            };
            _listView.fixedItemHeight = 20;
            _listView.selectionType = SelectionType.None;

            _listView.RefreshItems();
        }

        void OnEnable()
        {
            GameInput.Actions.UI.ShowLeaderboard.started += OnShowLeaderboard;
            GameInput.Actions.UI.ShowLeaderboard.canceled += OnHideLeaderboard;
        }
        
        void OnDisable()
        {
            GameInput.Actions.UI.ShowLeaderboard.started -= OnShowLeaderboard;
            GameInput.Actions.UI.ShowLeaderboard.canceled -= OnHideLeaderboard;
        }
        
        private void OnShowLeaderboard(InputAction.CallbackContext context)
        {
            _scoreboardHeld = true;
            _rootElement.style.display = DisplayStyle.Flex;
        }
        
        private void OnHideLeaderboard(InputAction.CallbackContext context)
        {
            _scoreboardHeld = false;
            if (LeaderboardManager.Instance == null ||
                !LeaderboardManager.Instance.IsMatchEnded)
            {
                _rootElement.style.display = DisplayStyle.None;
            }
        }
        
        void InitializeClientWorld(World clientWorld)
        {
            if (clientWorld != null)
            {
                _entityManager = clientWorld.EntityManager;
                
                _inGameQuery = _entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<NetworkStreamInGame>(),
                    ComponentType.ReadOnly<NetworkStreamConnection>()
                );
                
                _rootElement.style.display = DisplayStyle.None;
                _wasMatchEnded = false;
            }
            else
            {
                _entityManager = default;
            }

            _currentClientWorld = clientWorld;
        }

        static World GetClientWorld()
        {
            const string clientWorldName = "ClientWorld";
            for (var i = 0; i < World.All.Count; i++)
            {
                if (World.All[i].Name == clientWorldName && World.All[i].IsCreated)
                {
                    var clientWorld = World.All[i];
                    return clientWorld;
                }
            }

            return null;
        }

        void Update()
        {
            // Find the current ClientWorld, if any
            var clientWorld = GetClientWorld();
            // If the client world changed (new, destroyed, or swapped), (re)initialize
            if (clientWorld != _currentClientWorld)
            {
                InitializeClientWorld(clientWorld);
            }

            var worldIsInitialized = _currentClientWorld != null;
            if (!worldIsInitialized)
            {
                _rootElement.style.display = DisplayStyle.None;
                return;
            }
            
            var isInGame = _inGameQuery.CalculateEntityCount() != 0;
            if (!isInGame)
            {
                _rootElement.style.display = DisplayStyle.None;
                return;
            }

            bool matchEnded = LeaderboardManager.Instance != null &&
                              LeaderboardManager.Instance.IsMatchEnded;
            if (LeaderboardManager.Instance != null)
            {
                float remaining = LeaderboardManager.Instance.MatchTimeRemaining;
                int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                _matchTimerLabel.text = matchEnded
                    ? "MATCH ENDED"
                    : $"MATCH TIME: {minutes:00}:{seconds:00}";
            }
            if (matchEnded)
            {
                _rootElement.style.display = DisplayStyle.Flex;
                _matchResultLabel.style.display = DisplayStyle.Flex;
                _matchRestartLabel.style.display = DisplayStyle.Flex;
                _matchResultLabel.text =
                    $"WINNER: {LeaderboardManager.Instance.WinnerName}";
                _matchRestartLabel.text =
                    $"NEXT ROUND IN {Mathf.CeilToInt(LeaderboardManager.Instance.MatchRestartTimeRemaining)}";

                if (!_wasMatchEnded)
                {
                    UpdateLeaderboardUI();
                }
            }
            else if (_wasMatchEnded)
            {
                _matchResultLabel.style.display = DisplayStyle.None;
                _matchRestartLabel.style.display = DisplayStyle.None;
                if (!_scoreboardHeld)
                {
                    _rootElement.style.display = DisplayStyle.None;
                }
            }

            _wasMatchEnded = matchEnded;

            if (_rootElement.style.display == DisplayStyle.None)
            {
                return;
            }
            
            _timer += Time.deltaTime;
            if (_timer < 0.5f) return;
            _timer = 0.0f;
            
            UpdateLeaderboardUI();
        }
        
        private void UpdateLeaderboardUI()
        {
            if (LeaderboardManager.Instance == null) return;

            var scores = LeaderboardManager.Instance.GetScores();

            // Sort scores: Kills descending, then Deaths ascending
            scores.Sort((a, b) =>
            {
                // Primary sort: Kills descending
                int killComparison = b.Kills.CompareTo(a.Kills);
                if (killComparison != 0)
                {
                    return killComparison;
                }
                // Secondary sort: Deaths ascending
                return a.Deaths.CompareTo(b.Deaths);
            });

            // Clear previous entries
            _items.Clear();

            // Add new entries
            foreach (var score in scores)
            {
                _items.Add(new ScoreUiPlayerInfo(score.NetworkId, score.PlayerName.ToString(), score.Kills, score.Deaths));
            }
            
            _listView.RefreshItems();
        }
    }
    
    public class ScoreUiPlayerInfo
    {
        public int PlayerId;
        public string PlayerName;
        public int Kills;
        public int Deaths;

        public ScoreUiPlayerInfo(int id, string name, int kills, int deaths)
        {
            PlayerId = id;
            PlayerName = name;
            Kills = kills;
            Deaths = deaths;
        }
    }
}
