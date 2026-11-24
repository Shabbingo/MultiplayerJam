using System;
using System.Linq;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.MP_FPS.Cards;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed class CardTestingPanelController : IDisposable
    {
        public static bool IsOpen { get; private set; }

        private readonly VisualElement m_Host;
        private readonly VisualElement m_Overlay;
        private readonly ScrollView m_List;
        private readonly TextField m_Search;
        private readonly Label m_Status;
        private readonly Button m_Close;
        private InputAction m_ToggleAction;
        private float m_RefreshTimer;
        private string m_LastFilter = string.Empty;
        private bool m_Disposed;

        public CardTestingPanelController(VisualElement hudRoot)
        {
            if (!LeaderboardManager.CardTestingEnabled || hudRoot == null)
            {
                return;
            }

            VisualTreeAsset template = Resources.Load<VisualTreeAsset>(
                "UI/CardTestingPanel");
            if (template == null)
            {
                Debug.LogError("CardTestingPanel.uxml could not be loaded.");
                return;
            }

            TemplateContainer view = template.Instantiate();
            view.AddToClassList("card-test-host");
            view.pickingMode = PickingMode.Ignore;
            hudRoot.Add(view);
            m_Host = view;
            m_Overlay = view.Q<VisualElement>("cardTestingOverlay");
            m_Overlay.pickingMode = PickingMode.Ignore;
            m_List = view.Q<ScrollView>("cardTestingList");
            m_Search = view.Q<TextField>("cardTestingSearch");
            m_Status = view.Q<Label>("cardTestingStatus");
            m_Close = view.Q<Button>("cardTestingClose");
            m_Close.clicked += Close;
            m_Search.RegisterValueChangedCallback(OnSearchChanged);

            EnsureToggleBinding();
        }

        public void Update()
        {
            EnsureToggleBinding();
            if (!IsOpen || m_Overlay == null)
            {
                return;
            }

            m_RefreshTimer -= Time.unscaledDeltaTime;
            if (m_RefreshTimer <= 0f)
            {
                Rebuild();
                m_RefreshTimer = 0.2f;
            }
        }

        private void EnsureToggleBinding()
        {
            if (m_Disposed || m_ToggleAction != null ||
                !InputSystemManager.TryGetFirstInputUser(out var user) ||
                user.actions is not InputSystem_Actions controls)
            {
                return;
            }

            m_ToggleAction = InputSystemManager.GetGameplayAction(controls,
                InputSystemManager.CardTestingActionName);
            if (m_ToggleAction != null)
            {
                m_ToggleAction.performed += OnTogglePerformed;
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;
            if (m_ToggleAction != null)
            {
                m_ToggleAction.performed -= OnTogglePerformed;
            }
            if (m_Close != null)
            {
                m_Close.clicked -= Close;
            }
            if (m_Search != null)
            {
                m_Search.UnregisterValueChangedCallback(OnSearchChanged);
            }
            if (IsOpen)
            {
                Close();
            }
            m_Host?.RemoveFromHierarchy();
        }

        private void OnTogglePerformed(InputAction.CallbackContext context)
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void Open()
        {
            if (m_Overlay == null ||
                GameSettings.Instance.GameState != GlobalGameState.InGame)
            {
                return;
            }

            IsOpen = true;
            m_Overlay.pickingMode = PickingMode.Position;
            m_Overlay.AddToClassList("card-test-open");
            Utils.SetCursorVisible(true);
            m_RefreshTimer = 0f;
            Rebuild();
            m_Search?.Focus();
        }

        private void Close()
        {
            IsOpen = false;
            if (m_Overlay != null)
            {
                m_Overlay.pickingMode = PickingMode.Ignore;
            }
            m_Overlay?.RemoveFromClassList("card-test-open");
            if (GameSettings.Instance.GameState == GlobalGameState.InGame &&
                !GameSettings.Instance.IsPauseMenuOpen)
            {
                Utils.SetCursorVisible(false);
            }
        }

        private void OnSearchChanged(ChangeEvent<string> evt)
        {
            m_LastFilter = evt.newValue?.Trim() ?? string.Empty;
            m_RefreshTimer = 0f;
        }

        private void Rebuild()
        {
            if (m_List == null || m_Status == null)
            {
                return;
            }

            CardModifierCatalog catalog = WeaponManager.Instance?
                .CardModifierCatalog;
            if (catalog == null || !TryGetLocalModifiers(out var modifiers))
            {
                m_Status.text = "Waiting for local player card state...";
                m_List.Clear();
                return;
            }

            m_Status.text =
                "Use − and + to remove or add one stack. Server confirmation updates the counts.";
            m_List.Clear();
            var cards = catalog.Cards.Where(card => card != null &&
                MatchesFilter(card, m_LastFilter))
                .OrderBy(card => card.Category)
                .ThenBy(card => card.DisplayName);
            foreach (CardModifierDefinition card in cards)
            {
                m_List.Add(BuildCardRow(card, in modifiers));
            }
        }

        private static bool MatchesFilter(CardModifierDefinition card,
            string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return card.DisplayName.IndexOf(filter,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   card.Description.IndexOf(filter,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   card.CategoryDisplayName.IndexOf(filter,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private VisualElement BuildCardRow(CardModifierDefinition card,
            in PlayerModifierStacks modifiers)
        {
            var row = new VisualElement();
            row.AddToClassList("card-test-row");
            if (card.Rarity == CardRarity.Rare)
            {
                row.AddToClassList("card-rarity-rare");
            }
            else if (card.Rarity == CardRarity.Epic)
            {
                row.AddToClassList("card-rarity-epic");
            }

            var details = new VisualElement();
            details.AddToClassList("card-test-details");
            var name = new Label(
                $"{card.DisplayName}  •  {card.CategoryDisplayName}  •  {card.Rarity}");
            name.AddToClassList("card-test-name");
            details.Add(name);
            var description = new Label(card.Description);
            description.AddToClassList("card-test-description");
            details.Add(description);
            row.Add(details);

            var targets = new VisualElement();
            targets.AddToClassList("card-test-targets");
            AddTargetControls(targets, card, ModifierTarget.LeftHand,
                "LEFT", in modifiers);
            AddTargetControls(targets, card, ModifierTarget.RightHand,
                "RIGHT", in modifiers);
            AddTargetControls(targets, card, ModifierTarget.Self,
                "SELF", in modifiers);
            row.Add(targets);
            return row;
        }

        private static void AddTargetControls(VisualElement parent,
            CardModifierDefinition card, ModifierTarget target, string label,
            in PlayerModifierStacks modifiers)
        {
            if (!card.Allows(target))
            {
                return;
            }

            var group = new VisualElement();
            group.AddToClassList("card-test-target");
            var targetLabel = new Label(label);
            targetLabel.AddToClassList("card-test-target-label");
            group.Add(targetLabel);

            var remove = new Button(() => RequestChange(card, target, -1))
                { text = "−" };
            remove.AddToClassList("card-test-step");
            remove.SetEnabled(modifiers.GetStacks(card.Kind, target) > 0);
            group.Add(remove);

            var stack = new Label(
                modifiers.GetStacks(card.Kind, target).ToString());
            stack.AddToClassList("card-test-stack");
            group.Add(stack);

            var add = new Button(() => RequestChange(card, target, 1))
                { text = "+" };
            add.AddToClassList("card-test-step");
            group.Add(add);
            parent.Add(group);
        }

        private static void RequestChange(CardModifierDefinition card,
            ModifierTarget target, int delta)
        {
            LeaderboardManager.ClientInstance?.RequestCardTestChange(
                card.CardId, target, delta);
        }

        private static bool TryGetLocalModifiers(
            out PlayerModifierStacks modifiers)
        {
            foreach (World world in World.All)
            {
                if (!world.IsCreated || !world.IsClient())
                {
                    continue;
                }

                using EntityQuery query = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerModifierStacks>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>());
                if (query.TryGetSingleton(out modifiers))
                {
                    return true;
                }
            }

            modifiers = default;
            return false;
        }
    }
}
