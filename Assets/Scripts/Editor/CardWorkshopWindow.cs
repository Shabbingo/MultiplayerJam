using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.MP_FPS.Cards.EditorTools
{
    public sealed class CardWorkshopWindow : EditorWindow
    {
        private const string DefaultCardFolder = "Assets/Data/Cards";
        // Explicit labels keep this static initializer free of Unity API calls.
        private static readonly string[] CategoryOptions =
        {
            "All", "General", "Foundational", "Build Around", "Synergy",
            "Utility", "Wildcard", "Shield Effects", "Self Effects"
        };
        private static readonly string[] RarityOptions = BuildEnumOptions<
            CardRarity>();

        private readonly List<CardModifierDefinition> m_AllCards = new();
        private readonly List<CardModifierDefinition> m_FilteredCards = new();
        private List<CardWorkshopIssue> m_Issues = new();
        private CardModifierCatalog m_Catalog;
        private CardModifierDefinition m_SelectedCard;
        private SerializedObject m_SelectedSerializedObject;
        private Vector2 m_ListScroll;
        private Vector2 m_DetailScroll;
        private Vector2 m_IssueScroll;
        private string m_Search = string.Empty;
        private int m_CategoryFilter;
        private int m_RarityFilter;
        private bool m_UtilityOnly;
        private bool m_ShowAllTuning;
        private GUIStyle m_TitleStyle;
        private GUIStyle m_CardNameStyle;
        private GUIStyle m_MutedStyle;
        private GUIStyle m_RarityStyle;

        [MenuItem("Tools/Multiplayer Jam/Card Workshop")]
        public static void Open()
        {
            CardWorkshopWindow window = GetWindow<CardWorkshopWindow>();
            window.titleContent = new GUIContent("Card Workshop");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Card Workshop");
            minSize = new Vector2(760f, 520f);
            EditorApplication.projectChanged += HandleProjectChanged;
            FindCatalog();
            ReloadCards();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= HandleProjectChanged;
        }

        private void HandleProjectChanged()
        {
            ReloadCards();
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();
            DrawFilters();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(
                           GUILayout.Width(Mathf.Clamp(position.width * 0.34f,
                               250f, 340f))))
                {
                    DrawCardList();
                }

                DrawVerticalSeparator();

                using (new EditorGUILayout.VerticalScope())
                {
                    DrawSelectedCard();
                }
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("CARD WORKSHOP", m_TitleStyle,
                    GUILayout.Width(180f));
                GUILayout.FlexibleSpace();

                CardModifierCatalog nextCatalog =
                    (CardModifierCatalog)EditorGUILayout.ObjectField(
                        m_Catalog, typeof(CardModifierCatalog), false,
                        GUILayout.Width(220f));
                if (nextCatalog != m_Catalog)
                {
                    m_Catalog = nextCatalog;
                    ReloadCards();
                }

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton,
                        GUILayout.Width(64f)))
                {
                    ReloadCards();
                }

                if (GUILayout.Button("New Card", EditorStyles.toolbarButton,
                        GUILayout.Width(72f)))
                {
                    CreateCardTemplate();
                }
            }

            if (m_Catalog == null)
            {
                EditorGUILayout.HelpBox(
                    "Select or create a Card Modifier Catalog before adding " +
                    "cards to a draft pool.", MessageType.Warning);
            }
        }

        private void DrawFilters()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Search", GUILayout.Width(42f));
                string nextSearch = EditorGUILayout.TextField(m_Search,
                    GUILayout.MinWidth(118f));
                if (!string.Equals(nextSearch, m_Search,
                        StringComparison.Ordinal))
                {
                    m_Search = nextSearch;
                    ApplyFilters();
                }

                GUILayout.Label("Category", GUILayout.Width(55f));
                int nextCategory = EditorGUILayout.Popup(m_CategoryFilter,
                    CategoryOptions, GUILayout.Width(105f));
                GUILayout.Label("Rarity", GUILayout.Width(38f));
                int nextRarity = EditorGUILayout.Popup(m_RarityFilter,
                    RarityOptions, GUILayout.Width(90f));
                bool nextUtilityOnly = GUILayout.Toggle(m_UtilityOnly,
                    "Utilities only", GUILayout.Width(95f));

                if (nextCategory != m_CategoryFilter ||
                    nextRarity != m_RarityFilter ||
                    nextUtilityOnly != m_UtilityOnly)
                {
                    m_CategoryFilter = nextCategory;
                    m_RarityFilter = nextRarity;
                    m_UtilityOnly = nextUtilityOnly;
                    ApplyFilters();
                }
            }
        }

        private void DrawCardList()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{m_FilteredCards.Count} of " +
                                $"{m_AllCards.Count} cards",
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (m_Catalog != null && GUILayout.Button("Add safe missing",
                        GUILayout.Width(110f)))
                {
                    AddSafeMissingCards();
                }
            }

            m_ListScroll = EditorGUILayout.BeginScrollView(m_ListScroll,
                EditorStyles.helpBox);
            foreach (CardModifierDefinition card in m_FilteredCards)
            {
                bool isSelected = card == m_SelectedCard;
                GUIStyle rowStyle = isSelected
                    ? GUI.skin.FindStyle("SelectionRect") ?? GUIStyle.none
                    : GUIStyle.none;
                using (new EditorGUILayout.HorizontalScope(
                           rowStyle,
                           GUILayout.Height(42f)))
                {
                    DrawRarityBadge(card.Rarity);

                    using (new EditorGUILayout.VerticalScope())
                    {
                        GUILayout.Label(card.DisplayName, m_CardNameStyle);
                        GUILayout.Label(
                            $"ID {card.CardId}  •  " +
                            card.CategoryDisplayName, m_MutedStyle);
                    }

                    Rect rowRect = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown &&
                        Event.current.button == 0 &&
                        rowRect.Contains(Event.current.mousePosition))
                    {
                        SelectCard(card);
                        Event.current.Use();
                    }

                    if (GUILayout.Button("Edit", GUILayout.Width(44f),
                            GUILayout.Height(30f)))
                    {
                        SelectCard(card);
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            DrawValidationSummary();
        }

        private void DrawSelectedCard()
        {
            if (m_SelectedCard == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Select a card to tune it.",
                    EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                return;
            }

            if (m_SelectedSerializedObject == null ||
                m_SelectedSerializedObject.targetObject != m_SelectedCard)
            {
                m_SelectedSerializedObject = new SerializedObject(
                    m_SelectedCard);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(m_SelectedCard.DisplayName, m_TitleStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                {
                    EditorGUIUtility.PingObject(m_SelectedCard);
                }
                if (GUILayout.Button("Inspector", GUILayout.Width(70f)))
                {
                    Selection.activeObject = m_SelectedCard;
                }
                using (new EditorGUI.DisabledGroupScope(
                           !IsInCatalog(m_SelectedCard)))
                {
                    if (GUILayout.Button("Remove from draft",
                            GUILayout.Width(116f)))
                    {
                        RemoveFromCatalog(m_SelectedCard);
                    }
                }
            }

            m_DetailScroll = EditorGUILayout.BeginScrollView(m_DetailScroll);
            m_SelectedSerializedObject.Update();

            DrawSection("Identity", "m_CardId", "m_DisplayName",
                "m_Description", "m_Icon");
            DrawSection("Drafting", "m_Rarity", "m_BaseOfferWeight",
                "m_Category");

            EditorGUILayout.Space(4f);
            GUILayout.Label("Effect", EditorStyles.boldLabel);
            DrawProperty("m_Kind", "Modifier Kind");
            DrawProperty("m_AllowedTargets", "Allowed Targets");
            DrawProperty("m_ValuePerStack", GetValueLabel(
                m_SelectedCard.Kind));
            DrawRelevantTuning(m_SelectedCard.Kind);

            EditorGUILayout.Space(4f);
            m_ShowAllTuning = EditorGUILayout.Foldout(m_ShowAllTuning,
                "All advanced tuning fields", true);
            if (m_ShowAllTuning)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    DrawAllAdvancedTuning();
                }
            }

            bool changed = m_SelectedSerializedObject.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();

            if (changed)
            {
                EditorUtility.SetDirty(m_SelectedCard);
                ApplyFilters();
                RefreshValidation();
            }

            DrawSelectedCardIssues();
        }

        private void DrawRelevantTuning(CardModifierKind kind)
        {
            switch (kind)
            {
                case CardModifierKind.SlowTarget:
                    DrawProperty("m_EffectDurationSeconds", "Slow Duration");
                    DrawProperty("m_ReloadTimeMultiplierPerStack",
                        "Target Reload Multiplier");
                    break;
                case CardModifierKind.ExplosionRadius:
                    DrawProperty("m_AoeRadiusAdjustmentPerStack",
                        "Explosion Radius / Stack");
                    break;
                case CardModifierKind.ScatterWeapon:
                    DrawProperty("m_ScatterHalfAngleDegrees",
                        "Scatter Half Angle");
                    DrawProperty("m_MagazineDeltaPerStack",
                        "Magazine Delta / Stack");
                    break;
                case CardModifierKind.HomingProjectiles:
                    DrawProperty("m_ProjectileSpeedMultiplierPerStack",
                        "Projectile Speed Multiplier");
                    break;
                case CardModifierKind.ReloadSpeed:
                    DrawProperty("m_MinimumReloadMultiplier",
                        "Minimum Reload Multiplier");
                    break;
                case CardModifierKind.MagazineCapacity:
                    DrawProperty("m_ReloadTimeMultiplierPerStack",
                        "Reload Time Multiplier");
                    break;
                case CardModifierKind.Bleed:
                    DrawProperty("m_EffectDurationSeconds", "Bleed Duration");
                    break;
                case CardModifierKind.LifeSiphon:
                case CardModifierKind.ArmourPiercing:
                    DrawProperty("m_BaseDamageMultiplier",
                        "Base Damage Multiplier");
                    break;
                case CardModifierKind.BulletSize:
                    DrawProperty("m_DamagePercentPerStack",
                        "Damage Percent / Stack");
                    DrawProperty("m_ProjectileScalePerStack",
                        "Projectile Scale / Stack");
                    break;
                case CardModifierKind.BulletSpeed:
                    DrawProperty("m_ProjectileSpeedMultiplierPerStack",
                        "Projectile Speed Multiplier");
                    DrawProperty("m_ProjectileScaleMultiplierPerStack",
                        "Projectile Scale Multiplier");
                    DrawProperty("m_ExplosionRadiusMultiplierPerStack",
                        "Explosion Radius Multiplier");
                    break;
                case CardModifierKind.Bounce:
                    DrawProperty("m_BounceVelocityRetention",
                        "Bounce Velocity Retention");
                    break;
                case CardModifierKind.RapidFire:
                    DrawProperty("m_BaseDamageMultiplier",
                        "Base Damage Multiplier");
                    DrawProperty("m_FireIntervalMultiplierPerStack",
                        "Fire Interval Multiplier");
                    DrawProperty("m_MagazineDeltaPerStack",
                        "Magazine Delta / Stack");
                    break;
                case CardModifierKind.GrenadeLauncher:
                    DrawProperty("m_ProjectileSpeedMultiplierPerStack",
                        "Projectile Speed Multiplier");
                    DrawProperty("m_EffectRadiusMetres", "Blast Radius");
                    DrawProperty("m_LobUpwardSpeedPerStack",
                        "Lob Upward Speed");
                    DrawProperty("m_ProjectileGravityPerStack",
                        "Projectile Gravity");
                    break;
                case CardModifierKind.ConservationOfMass:
                    DrawProperty("m_EffectRadiusMetres", "Shockwave Radius");
                    DrawProperty("m_AreaDamagePerStack",
                        "Area Damage / Stack");
                    DrawProperty("m_EffectDurationSeconds", "Cooldown");
                    break;
                case CardModifierKind.HeavyFooted:
                    DrawProperty("m_EffectRadiusMetres", "Outer Radius");
                    DrawProperty("m_SecondaryEffectRadiusMetres",
                        "Inner Radius");
                    break;
                case CardModifierKind.TinyTerror:
                    DrawProperty("m_IncomingDamageMultiplierPerStack",
                        "Incoming Damage Multiplier");
                    break;
                case CardModifierKind.SlowShield:
                    DrawProperty("m_EffectDurationSeconds", "Slow Duration");
                    break;
                case CardModifierKind.SwapOnReflect:
                    DrawProperty("m_TeleportFreezeSeconds",
                        "Teleport Freeze");
                    break;
                case CardModifierKind.BigHealthCapacity:
                case CardModifierKind.SmallHealthCapacity:
                    DrawProperty("m_DamageReductionPercent",
                        "Damage Reduction");
                    break;
                case CardModifierKind.ActiveCamoUtility:
                    DrawUtilityCommon();
                    DrawProperty("m_EffectDurationSeconds", "Camo Duration");
                    DrawProperty("m_UtilityMirageTint", "Mirage Tint");
                    DrawProperty("m_UtilityMirageDistortion", "Distortion");
                    DrawProperty("m_UtilityMirageOpacity", "Opacity");
                    DrawProperty("m_UtilityMirageWobble", "Wobble");
                    break;
                case CardModifierKind.ForwardChargeUtility:
                    DrawUtilityCommon();
                    DrawProperty("m_EffectDurationSeconds", "Charge Duration");
                    DrawProperty("m_UtilityForwardSpeed", "Forward Speed");
                    DrawProperty("m_UtilityPushForce", "Push Force");
                    DrawProperty("m_UtilityUpForce", "Up Force");
                    DrawProperty("m_UtilityFrontDot", "Front Hit Dot");
                    break;
                case CardModifierKind.MegaJumpUtility:
                    DrawUtilityCommon();
                    break;
                case CardModifierKind.SmallDashUtility:
                    DrawUtilityCommon();
                    DrawProperty("m_EffectDurationSeconds", "Dash Duration");
                    break;
            }
        }

        private void DrawUtilityCommon()
        {
            DrawProperty("m_UtilityCooldownSeconds",
                "Cooldown Override (0 = default)");
        }

        private void DrawAllAdvancedTuning()
        {
            string[] names =
            {
                "m_EffectDurationSeconds", "m_EffectRadiusMetres",
                "m_SecondaryEffectRadiusMetres", "m_DamagePercentPerStack",
                "m_AreaDamagePerStack", "m_BaseDamageMultiplier",
                "m_FireIntervalMultiplierPerStack",
                "m_MovementSpeedMultiplierPerStack",
                "m_IncomingDamageMultiplierPerStack",
                "m_ReloadTimeMultiplierPerStack",
                "m_MinimumReloadMultiplier", "m_MagazineDeltaPerStack",
                "m_ProjectileScalePerStack",
                "m_ProjectileSpeedMultiplierPerStack",
                "m_LobUpwardSpeedPerStack", "m_ProjectileGravityPerStack",
                "m_ProjectileScaleMultiplierPerStack",
                "m_AoeRadiusAdjustmentPerStack",
                "m_ExplosionRadiusMultiplierPerStack",
                "m_ScatterHalfAngleDegrees", "m_BounceVelocityRetention",
                "m_DamageReductionPercent", "m_TeleportFreezeSeconds",
                "m_UtilityCooldownSeconds", "m_UtilityForwardSpeed",
                "m_UtilityPushForce", "m_UtilityUpForce",
                "m_UtilityFrontDot", "m_UtilityMirageTint",
                "m_UtilityMirageDistortion", "m_UtilityMirageOpacity",
                "m_UtilityMirageWobble"
            };
            foreach (string name in names)
            {
                DrawProperty(name, null);
            }
        }

        private void DrawSection(string title, params string[] propertyNames)
        {
            EditorGUILayout.Space(4f);
            GUILayout.Label(title, EditorStyles.boldLabel);
            foreach (string propertyName in propertyNames)
            {
                DrawProperty(propertyName, null);
            }
        }

        private void DrawProperty(string propertyName, string label)
        {
            SerializedProperty property = m_SelectedSerializedObject
                .FindProperty(propertyName);
            if (property == null)
            {
                EditorGUILayout.HelpBox(
                    $"Missing serialized field '{propertyName}'.",
                    MessageType.Error);
                return;
            }

            if (string.IsNullOrEmpty(label))
            {
                EditorGUILayout.PropertyField(property, true);
            }
            else
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label),
                    true);
            }
        }

        private void DrawValidationSummary()
        {
            int errors = m_Issues.Count(issue =>
                issue.Severity == CardWorkshopIssueSeverity.Error);
            int warnings = m_Issues.Count(issue =>
                issue.Severity == CardWorkshopIssueSeverity.Warning);
            MessageType type = errors > 0
                ? MessageType.Error
                : warnings > 0
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(
                errors > 0 || warnings > 0
                    ? $"Validation: {errors} error(s), {warnings} warning(s)."
                    : "Validation passed.", type);

            m_IssueScroll = EditorGUILayout.BeginScrollView(m_IssueScroll,
                GUILayout.MaxHeight(118f));
            foreach (CardWorkshopIssue issue in m_Issues.Take(6))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(GetIssueGlyph(issue.Severity),
                        GUILayout.Width(18f));
                    GUILayout.Label(issue.Message, EditorStyles.wordWrappedMiniLabel);
                    if (issue.Card != null && GUILayout.Button("Open",
                            GUILayout.Width(42f)))
                    {
                        SelectCard(issue.Card);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSelectedCardIssues()
        {
            CardWorkshopIssue[] selectedIssues = m_Issues
                .Where(issue => issue.Card == m_SelectedCard).ToArray();
            foreach (CardWorkshopIssue issue in selectedIssues)
            {
                EditorGUILayout.HelpBox(issue.Message,
                    ToMessageType(issue.Severity));
            }
        }

        private void FindCatalog()
        {
            string guid = AssetDatabase.FindAssets("t:CardModifierCatalog")
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(guid))
            {
                m_Catalog = AssetDatabase.LoadAssetAtPath<
                    CardModifierCatalog>(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        private void ReloadCards()
        {
            m_AllCards.Clear();
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:CardModifierDefinition", new[] { "Assets" }))
            {
                CardModifierDefinition card = AssetDatabase.LoadAssetAtPath<
                    CardModifierDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (card != null)
                {
                    m_AllCards.Add(card);
                }
            }

            m_AllCards.Sort(CompareCards);
            ApplyFilters();
            RefreshValidation();

            if (m_SelectedCard == null && m_FilteredCards.Count > 0)
            {
                SelectCard(m_FilteredCards[0]);
            }
        }

        private void ApplyFilters()
        {
            m_FilteredCards.Clear();
            foreach (CardModifierDefinition card in m_AllCards)
            {
                if (!MatchesSearch(card) ||
                    m_CategoryFilter > 0 &&
                    card.Category != (CardCategory)(m_CategoryFilter - 1) ||
                    m_RarityFilter > 0 &&
                    card.Rarity != (CardRarity)m_RarityFilter ||
                    m_UtilityOnly && !CardWorkshopValidator.IsUtility(card.Kind))
                {
                    continue;
                }

                m_FilteredCards.Add(card);
            }
        }

        private bool MatchesSearch(CardModifierDefinition card)
        {
            if (string.IsNullOrWhiteSpace(m_Search)) return true;
            return card.DisplayName.IndexOf(m_Search,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (card.Description ?? string.Empty).IndexOf(m_Search,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   card.Kind.ToString().IndexOf(m_Search,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   card.CardId.ToString().Contains(m_Search);
        }

        private void SelectCard(CardModifierDefinition card)
        {
            m_SelectedCard = card;
            m_SelectedSerializedObject = card != null
                ? new SerializedObject(card)
                : null;
            m_DetailScroll = Vector2.zero;
            Repaint();
        }

        private void CreateCardTemplate()
        {
            if (!AssetDatabase.IsValidFolder(DefaultCardFolder))
            {
                Debug.LogError($"Card folder '{DefaultCardFolder}' does not " +
                               "exist.");
                return;
            }

            var card = CreateInstance<CardModifierDefinition>();
            var serializedCard = new SerializedObject(card);
            serializedCard.FindProperty("m_CardId").intValue = NextCardId();
            serializedCard.FindProperty("m_DisplayName").stringValue =
                "New Card";
            serializedCard.ApplyModifiedPropertiesWithoutUndo();

            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{DefaultCardFolder}/New Card.asset");
            AssetDatabase.CreateAsset(card, path);
            AssetDatabase.SaveAssets();
            ReloadCards();
            SelectCard(card);
            Selection.activeObject = card;
        }

        private int NextCardId()
        {
            return m_AllCards.Count == 0
                ? 1
                : m_AllCards.Max(card => card.CardId) + 1;
        }

        private void AddSafeMissingCards()
        {
            if (m_Catalog == null) return;

            var usedIds = new HashSet<int>(m_Catalog.Cards
                .Where(card => card != null).Select(card => card.CardId));
            var usedKinds = new HashSet<CardModifierKind>(m_Catalog.Cards
                .Where(card => card != null).Select(card => card.Kind));
            var existing = new HashSet<CardModifierDefinition>(m_Catalog.Cards);
            List<CardModifierDefinition> safe = m_AllCards.Where(card =>
                !existing.Contains(card) && !usedIds.Contains(card.CardId) &&
                !usedKinds.Contains(card.Kind)).ToList();
            if (safe.Count == 0) return;

            Undo.RecordObject(m_Catalog, "Add Cards To Draft Catalog");
            var serializedCatalog = new SerializedObject(m_Catalog);
            SerializedProperty cardsProperty = serializedCatalog.FindProperty(
                "m_Cards");
            foreach (CardModifierDefinition card in safe)
            {
                int index = cardsProperty.arraySize;
                cardsProperty.InsertArrayElementAtIndex(index);
                cardsProperty.GetArrayElementAtIndex(index)
                    .objectReferenceValue = card;
            }
            serializedCatalog.ApplyModifiedProperties();
            EditorUtility.SetDirty(m_Catalog);
            AssetDatabase.SaveAssets();
            RefreshValidation();
        }

        private void RemoveFromCatalog(CardModifierDefinition card)
        {
            if (m_Catalog == null || card == null) return;
            Undo.RecordObject(m_Catalog, "Remove Card From Draft Catalog");
            var serializedCatalog = new SerializedObject(m_Catalog);
            SerializedProperty cardsProperty = serializedCatalog.FindProperty(
                "m_Cards");
            for (int i = cardsProperty.arraySize - 1; i >= 0; i--)
            {
                if (cardsProperty.GetArrayElementAtIndex(i)
                        .objectReferenceValue == card)
                {
                    cardsProperty.DeleteArrayElementAtIndex(i);
                }
            }
            serializedCatalog.ApplyModifiedProperties();
            EditorUtility.SetDirty(m_Catalog);
            AssetDatabase.SaveAssets();
            RefreshValidation();
        }

        private bool IsInCatalog(CardModifierDefinition card)
        {
            return m_Catalog != null && m_Catalog.Cards.Contains(card);
        }

        private void RefreshValidation()
        {
            m_Issues = CardWorkshopValidator.Validate(m_Catalog, m_AllCards);
        }

        private void EnsureStyles()
        {
            m_TitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14
            };
            m_CardNameStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                clipping = TextClipping.Clip
            };
            m_MutedStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.62f, 0.65f, 0.7f)
                    : new Color(0.35f, 0.38f, 0.42f) }
            };
            m_RarityStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9
            };
        }

        private void DrawRarityBadge(CardRarity rarity)
        {
            Color previousContentColor = GUI.contentColor;
            try
            {
                GUI.contentColor = GetRarityColor(rarity);
                GUILayout.Label($"● {rarity.ToString().ToUpperInvariant()}",
                    m_RarityStyle, GUILayout.Width(62f),
                    GUILayout.Height(30f));
            }
            finally
            {
                GUI.contentColor = previousContentColor;
            }
        }

        private static Color GetRarityColor(CardRarity rarity)
        {
            return rarity switch
            {
                CardRarity.Rare => new Color(0.25f, 0.65f, 1f),
                CardRarity.Epic => new Color(0.82f, 0.43f, 1f),
                _ => EditorGUIUtility.isProSkin
                    ? new Color(0.72f, 0.74f, 0.78f)
                    : new Color(0.38f, 0.4f, 0.44f)
            };
        }

        private static string GetValueLabel(CardModifierKind kind)
        {
            return kind switch
            {
                CardModifierKind.ForwardChargeUtility => "Impact Damage",
                CardModifierKind.MegaJumpUtility => "Jump Height Multiplier",
                CardModifierKind.SmallDashUtility => "Speed Multiplier",
                CardModifierKind.DamagePercent => "Damage Percent / Stack",
                CardModifierKind.MagazineCapacity => "Rounds / Stack",
                CardModifierKind.ScatterWeapon => "Projectile Count / Stack",
                _ => "Primary Value / Stack"
            };
        }

        private static int CompareCards(CardModifierDefinition left,
            CardModifierDefinition right)
        {
            int category = left.Category.CompareTo(right.Category);
            return category != 0
                ? category
                : string.Compare(left.DisplayName, right.DisplayName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string[] BuildEnumOptions<T>() where T : Enum
        {
            return new[] { "All" }.Concat(Enum.GetNames(typeof(T))).ToArray();
        }

        private static string GetIssueGlyph(CardWorkshopIssueSeverity severity)
        {
            return severity switch
            {
                CardWorkshopIssueSeverity.Error => "●",
                CardWorkshopIssueSeverity.Warning => "▲",
                _ => "✓"
            };
        }

        private static MessageType ToMessageType(
            CardWorkshopIssueSeverity severity)
        {
            return severity switch
            {
                CardWorkshopIssueSeverity.Error => MessageType.Error,
                CardWorkshopIssueSeverity.Warning => MessageType.Warning,
                _ => MessageType.Info
            };
        }

        private static void DrawVerticalSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f,
                GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
        }
    }
}
