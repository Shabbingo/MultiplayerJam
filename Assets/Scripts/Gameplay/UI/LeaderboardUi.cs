using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Unity.MP_FPS.Cards;

namespace Unity.MP_FPS.UI
{
    public class LeaderboardUi : MonoBehaviour
    {
        public VisualTreeAsset ScoreItemTemplate;

        private EntityManager _entityManager;
        private World _currentClientWorld;
        private EntityQuery _inGameQuery;
        private VisualElement _rootElement;
        private VisualElement _scoreTable;
        private VisualElement _draftPanel;
        private ListView _listView;
        private Label _matchResultLabel;
        private Label _matchRestartLabel;
        private Label _matchTimerLabel;
        private Label _draftTitleLabel;
        private Label _draftPlayerLabel;
        private Label _draftTimerLabel;
        private Label _draftHelpLabel;
        private Label _draftPickResultLabel;
        private readonly Button[] _cardButtons = new Button[3];
        private Button _leftTargetButton;
        private Button _selfTargetButton;
        private Button _rightTargetButton;
        private Button _bothTargetButton;
        private readonly List<ScoreUiPlayerInfo> _items = new();

        private float _timer;
        private bool _scoreboardHeld;
        private int _selectedCardId = -1;
        private int _lastActiveDraftPlayerId = int.MinValue;
        private int _lastOfferHash;
        private bool _cursorReleasedForDraft;

        private void Awake()
        {
            var uiDocument = GetComponent<UIDocument>();
            _rootElement = uiDocument.rootVisualElement;
            _rootElement.style.display = DisplayStyle.None;

            _scoreTable = _rootElement.Q<VisualElement>("score-table");
            _draftPanel = _rootElement.Q<VisualElement>("draft-panel");
            _listView = _rootElement.Q<ListView>("score-items");
            _matchResultLabel = _rootElement.Q<Label>("match-result");
            _matchRestartLabel = _rootElement.Q<Label>("match-restart");
            _matchTimerLabel = _rootElement.Q<Label>("match-timer");
            _draftTitleLabel = _rootElement.Q<Label>("draft-title");
            _draftPlayerLabel = _rootElement.Q<Label>("draft-player");
            _draftTimerLabel = _rootElement.Q<Label>("draft-timer");
            _draftHelpLabel = _rootElement.Q<Label>("draft-help");
            _draftPickResultLabel = _rootElement.Q<Label>("draft-pick-result");

            for (int i = 0; i < _cardButtons.Length; i++)
            {
                int capturedIndex = i;
                _cardButtons[i] = _rootElement.Q<Button>($"card-{i}");
                _cardButtons[i].clicked += () => SelectCard(capturedIndex);
            }

            _leftTargetButton = _rootElement.Q<Button>("target-left");
            _selfTargetButton = _rootElement.Q<Button>("target-self");
            _rightTargetButton = _rootElement.Q<Button>("target-right");
            _bothTargetButton = _rootElement.Q<Button>("target-both");
            _leftTargetButton.clicked += () => SubmitTarget(ModifierTarget.LeftHand);
            _selfTargetButton.clicked += () => SubmitTarget(ModifierTarget.Self);
            _rightTargetButton.clicked += () => SubmitTarget(ModifierTarget.RightHand);
            _bothTargetButton.clicked += () => SubmitTarget(ModifierTarget.BothHands);

            _listView.itemsSource = _items;
            _listView.makeItem = () => ScoreItemTemplate.Instantiate();
            _listView.bindItem = (element, i) =>
            {
                var player = _items[i];
                element.Q<Label>("name").text = player.PlayerName;
                element.Q<Label>("kills").text = player.RoundWins.ToString();
                element.Q<Label>("deaths").text = player.Kills.ToString();
            };
            _listView.fixedItemHeight = 20;
            _listView.selectionType = SelectionType.None;
            _listView.RefreshItems();
        }

        private void OnEnable()
        {
            GameInput.Actions.UI.ShowLeaderboard.started += OnShowLeaderboard;
            GameInput.Actions.UI.ShowLeaderboard.canceled += OnHideLeaderboard;
        }

        private void OnDisable()
        {
            GameInput.Actions.UI.ShowLeaderboard.started -= OnShowLeaderboard;
            GameInput.Actions.UI.ShowLeaderboard.canceled -= OnHideLeaderboard;
            if (_cursorReleasedForDraft)
            {
                Utils.SetCursorVisible(false);
                _cursorReleasedForDraft = false;
            }
        }

        private void OnShowLeaderboard(InputAction.CallbackContext context)
        {
            _scoreboardHeld = true;
            _rootElement.style.display = DisplayStyle.Flex;
        }

        private void OnHideLeaderboard(InputAction.CallbackContext context)
        {
            _scoreboardHeld = false;
            var manager = LeaderboardManager.ClientInstance;
            if (manager == null ||
                (!manager.IsMatchEnded && !manager.IsDrafting && !manager.IsPreparingRound))
            {
                _rootElement.style.display = DisplayStyle.None;
            }
        }

        private void InitializeClientWorld(World clientWorld)
        {
            if (clientWorld != null)
            {
                _entityManager = clientWorld.EntityManager;
                _inGameQuery = _entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<NetworkStreamInGame>(),
                    ComponentType.ReadOnly<NetworkStreamConnection>());
                _rootElement.style.display = DisplayStyle.None;
            }
            else
            {
                _entityManager = default;
            }

            _currentClientWorld = clientWorld;
        }

        private static World GetClientWorld()
        {
            const string clientWorldName = "ClientWorld";
            for (int i = 0; i < World.All.Count; i++)
            {
                if (World.All[i].Name == clientWorldName && World.All[i].IsCreated)
                    return World.All[i];
            }

            return null;
        }

        private void Update()
        {
            var clientWorld = GetClientWorld();
            if (clientWorld != _currentClientWorld) InitializeClientWorld(clientWorld);
            if (_currentClientWorld == null || _inGameQuery.CalculateEntityCount() == 0)
            {
                _rootElement.style.display = DisplayStyle.None;
                return;
            }

            var manager = LeaderboardManager.ClientInstance;
            if (manager == null) return;

            _matchTimerLabel.text =
                $"ROUND {manager.CurrentRound} — FIRST TO {manager.RoundWinsToWin} WINS";
            if (manager.IsDrafting)
            {
                if (!_cursorReleasedForDraft)
                {
                    Utils.SetCursorVisible(true);
                    _cursorReleasedForDraft = true;
                }
                ShowDraft(manager);
            }
            else if (manager.IsPreparingRound)
            {
                RestoreGameplayCursor();
                ShowRoundPreparing(manager);
            }
            else if (manager.IsMatchEnded)
            {
                RestoreGameplayCursor();
                ShowMatchEnded(manager);
            }
            else
            {
                RestoreGameplayCursor();
                ShowCombat();
            }

            if (_rootElement.style.display == DisplayStyle.None) return;
            _timer += Time.deltaTime;
            if (_timer < 0.5f) return;
            _timer = 0f;
            UpdateLeaderboardUI();
        }

        private void ShowDraft(LeaderboardManager manager)
        {
            _rootElement.style.display = DisplayStyle.Flex;
            _scoreTable.style.display = DisplayStyle.None;
            _draftPanel.style.display = DisplayStyle.Flex;
            _draftTitleLabel.text = $"ROUND {manager.CurrentRound} COMPLETE — CARD DRAFT";

            int offerHash = manager.GetOfferedCardId(0) * 397 ^
                            manager.GetOfferedCardId(1) * 31 ^
                            manager.GetOfferedCardId(2);
            if (_lastActiveDraftPlayerId != manager.ActiveDraftNetworkId ||
                _lastOfferHash != offerHash)
            {
                _selectedCardId = -1;
                _lastActiveDraftPlayerId = manager.ActiveDraftNetworkId;
                _lastOfferHash = offerHash;
            }

            bool activeTurn = manager.ActiveDraftNetworkId >= 0;
            if (activeTurn)
            {
                _draftPlayerLabel.text = manager.IsLocalDraftTurn
                    ? "YOUR PICK"
                    : $"{manager.ActiveDraftPlayerName.ToUpperInvariant()} IS CHOOSING";
                _draftTimerLabel.text =
                    $"{Mathf.CeilToInt(manager.DraftSecondsRemaining)} SECONDS";
                _draftHelpLabel.text = manager.IsLocalDraftTurn
                    ? "CHOOSE A CARD, THEN CHOOSE WHERE TO APPLY IT"
                    : "EVERYONE CAN SEE THIS OFFER";
            }
            else
            {
                _draftPlayerLabel.text = "PICK LOCKED IN";
                _draftTimerLabel.text = string.Empty;
                _draftHelpLabel.text = "NEXT PLAYER...";
            }

            _draftPickResultLabel.text = manager.LastPickMessage;
            for (int i = 0; i < _cardButtons.Length; i++)
            {
                int cardId = manager.GetOfferedCardId(i);
                var definition = manager.GetCardDefinition(cardId);
                bool visible = definition != null;
                _cardButtons[i].style.display = visible
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                if (!visible) continue;
                _cardButtons[i].text =
                    $"{definition.DisplayName.ToUpperInvariant()}\n\n{definition.Description}\n\nSTACK LIMIT: {definition.StackLimit}";
                _cardButtons[i].SetEnabled(manager.IsLocalDraftTurn && activeTurn);
                _cardButtons[i].style.backgroundColor = cardId == _selectedCardId
                    ? new Color(0.18f, 0.48f, 0.68f, 0.98f)
                    : new Color(0.14f, 0.22f, 0.30f, 0.96f);
            }

            RefreshTargetButtons(manager);
        }

        private void ShowRoundPreparing(LeaderboardManager manager)
        {
            _rootElement.style.display = DisplayStyle.Flex;
            _scoreTable.style.display = DisplayStyle.Flex;
            _draftPanel.style.display = DisplayStyle.None;
            _matchResultLabel.style.display = DisplayStyle.Flex;
            _matchRestartLabel.style.display = DisplayStyle.Flex;
            _matchResultLabel.text = $"ROUND {manager.CurrentRound}";
            _matchRestartLabel.text =
                $"STARTING IN {Mathf.CeilToInt(manager.RoundStartTimeRemaining)}";
            UpdateLeaderboardUI();
        }

        private void ShowMatchEnded(LeaderboardManager manager)
        {
            _rootElement.style.display = DisplayStyle.Flex;
            _scoreTable.style.display = DisplayStyle.Flex;
            _draftPanel.style.display = DisplayStyle.None;
            _matchResultLabel.style.display = DisplayStyle.Flex;
            _matchRestartLabel.style.display = DisplayStyle.Flex;
            _matchResultLabel.text = $"MATCH WINNER: {manager.WinnerName}";
            _matchRestartLabel.text =
                $"NEW MATCH IN {Mathf.CeilToInt(manager.MatchRestartTimeRemaining)}";
        }

        private void ShowCombat()
        {
            _scoreTable.style.display = DisplayStyle.Flex;
            _draftPanel.style.display = DisplayStyle.None;
            _matchResultLabel.style.display = DisplayStyle.None;
            _matchRestartLabel.style.display = DisplayStyle.None;
            if (!_scoreboardHeld) _rootElement.style.display = DisplayStyle.None;
        }

        private void RestoreGameplayCursor()
        {
            if (!_cursorReleasedForDraft) return;
            Utils.SetCursorVisible(false);
            _cursorReleasedForDraft = false;
        }

        private void SelectCard(int index)
        {
            var manager = LeaderboardManager.ClientInstance;
            if (manager == null || !manager.IsLocalDraftTurn) return;
            int cardId = manager.GetOfferedCardId(index);
            if (manager.GetCardDefinition(cardId) == null) return;
            _selectedCardId = cardId;
            RefreshTargetButtons(manager);
        }

        private void SubmitTarget(ModifierTarget target)
        {
            var manager = LeaderboardManager.ClientInstance;
            if (manager == null || !manager.IsLocalDraftTurn || _selectedCardId <= 0)
                return;
            var card = manager.GetCardDefinition(_selectedCardId);
            if (card == null || !card.Allows(target)) return;
            manager.RequestDraftPick(_selectedCardId, target);
            _draftHelpLabel.text = "SUBMITTING PICK...";
            _leftTargetButton.SetEnabled(false);
            _selfTargetButton.SetEnabled(false);
            _rightTargetButton.SetEnabled(false);
            _bothTargetButton.SetEnabled(false);
        }

        private void RefreshTargetButtons(LeaderboardManager manager)
        {
            var card = manager.GetCardDefinition(_selectedCardId);
            bool canChoose = manager.IsLocalDraftTurn && card != null;
            _leftTargetButton.SetEnabled(canChoose && card.Allows(ModifierTarget.LeftHand));
            _selfTargetButton.SetEnabled(canChoose && card.Allows(ModifierTarget.Self));
            _rightTargetButton.SetEnabled(canChoose && card.Allows(ModifierTarget.RightHand));
            _bothTargetButton.SetEnabled(canChoose && card.Allows(ModifierTarget.BothHands));
        }

        private void UpdateLeaderboardUI()
        {
            var manager = LeaderboardManager.ClientInstance;
            if (manager == null) return;
            var scores = manager.GetScores();
            scores.Sort((a, b) =>
            {
                int result = b.RoundWins.CompareTo(a.RoundWins);
                if (result != 0) return result;
                result = b.Kills.CompareTo(a.Kills);
                return result != 0 ? result : a.Deaths.CompareTo(b.Deaths);
            });

            _items.Clear();
            foreach (var score in scores)
            {
                _items.Add(new ScoreUiPlayerInfo(score.NetworkId,
                    score.PlayerName.ToString(), score.Kills, score.Deaths,
                    score.RoundWins));
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
        public int RoundWins;

        public ScoreUiPlayerInfo(int id, string name, int kills, int deaths,
            int roundWins)
        {
            PlayerId = id;
            PlayerName = name;
            Kills = kills;
            Deaths = deaths;
            RoundWins = roundWins;
        }
    }
}
