using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(UIDocument))]
    public class InGameHUD : MonoBehaviour
    {
        [Header("Reticle Configuration")] [SerializeField]
        private float reticleBaseSize = 80f;

        // UI Element references
        private VisualElement m_RootElement;
        private ProgressBar m_HealthBar;
        private VisualElement m_PlayerHealthBarFill;
        private VisualElement m_ReflectShieldFrame;
        private VisualElement m_ReflectCooldownWheel;
        private VisualElement m_ReflectCooldownTop;
        private VisualElement m_ReflectCooldownRight;
        private VisualElement m_ReflectCooldownBottom;
        private VisualElement m_ReflectCooldownLeft;
        private VisualElement m_ReflectReadyGlow;
        private ProgressBar m_SelfResizeChargeBar;
        private ProgressBar m_ShrinkAmmoBar;
        private ProgressBar m_GrowAmmoBar;
        private Label m_ShrinkAmmoLabel;
        private Label m_GrowAmmoLabel;
        private Label m_SelfShrinkUsesLabel;
        private Label m_SelfGrowUsesLabel;
        private Label m_ReloadingLabel;
        private VisualElement m_Reticle;

        // UI-side timer to ensure shot feedback is visible for a minimum duration.
        private float m_shotFeedbackTimer = 0f;

        // The reticle will stay white for at least 100ms after a shot.
        private const float k_ShotFeedbackDuration = 0.1f;
        private static readonly Color k_HealthBarColor = new Color(0.29f, 0.83f, 0.43f, 0.75f);
        private const string k_CrossReticleClass = "kits-reticle-cross";
        private const string k_TCrossReticleClass = "kits-reticle-tcross";
        private const string k_CircularCrossClass = "kits-reticle-circularcross";
        private const string k_OpenCircularReticleClass = "kits-reticle-opencircular";

        // ECS query fields
        private World m_ClientWorld;
        private EntityManager m_EntityManager;
        private EntityQuery m_LocalPlayerQuery;

        void OnEnable()
        {
            m_RootElement = GetComponent<UIDocument>().rootVisualElement;

            // Find the UI elements by name
            m_HealthBar = m_RootElement.Q<ProgressBar>("player-health-bar");
            m_ReflectShieldFrame = m_RootElement.Q<VisualElement>("reflect-shield-frame");
            m_ReflectCooldownWheel = m_RootElement.Q<VisualElement>("reflect-cooldown-wheel");
            m_ReflectCooldownTop = m_RootElement.Q<VisualElement>("reflect-cooldown-top");
            m_ReflectCooldownRight = m_RootElement.Q<VisualElement>("reflect-cooldown-right");
            m_ReflectCooldownBottom = m_RootElement.Q<VisualElement>("reflect-cooldown-bottom");
            m_ReflectCooldownLeft = m_RootElement.Q<VisualElement>("reflect-cooldown-left");
            m_ReflectReadyGlow = m_RootElement.Q<VisualElement>("reflect-ready-glow");
            if (m_HealthBar != null)
            {
                m_PlayerHealthBarFill = m_HealthBar.Q<VisualElement>(null, "unity-progress-bar__progress");
            }

            m_SelfResizeChargeBar = m_RootElement.Q<ProgressBar>("player-stamina-bar");
            if (m_SelfResizeChargeBar != null)
            {
                m_SelfResizeChargeBar.style.display = DisplayStyle.None;
            }

            m_ShrinkAmmoLabel = m_RootElement.Q<Label>("shrink-ammo-label");
            m_GrowAmmoLabel = m_RootElement.Q<Label>("grow-ammo-label");
            m_ShrinkAmmoBar = m_RootElement.Q<ProgressBar>("shrink-ammo-bar");
            m_GrowAmmoBar = m_RootElement.Q<ProgressBar>("grow-ammo-bar");
            m_SelfShrinkUsesLabel = m_RootElement.Q<Label>("self-shrink-uses-label");
            m_SelfGrowUsesLabel = m_RootElement.Q<Label>("self-grow-uses-label");
            m_ReloadingLabel = m_RootElement.Q<Label>("reloading-label");
            m_Reticle = m_RootElement.Q<VisualElement>("player-reticle");
        }

        private void InitializeEcs()
        {
            // Find the active client world
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    m_ClientWorld = world;
                    m_EntityManager = world.EntityManager;
                    break;
                }
            }

            if (m_EntityManager != null)
            {
                // This query finds the single entity that is both a predicted player ghost
                // and is owned by the local client.
                m_LocalPlayerQuery = m_EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>()
                );
            }
        }

        void LateUpdate()
        {
            // Toggle HUD visibility based on the overall game state
            bool isInGame = GameSettings.Instance.GameState == GlobalGameState.InGame;
            if (m_RootElement.style.display != (isInGame ? DisplayStyle.Flex : DisplayStyle.None))
            {
                m_RootElement.style.display = isInGame ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!isInGame) return;

            // If the world was destroyed (e.g., returned to main menu), try to re-initialize
            if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
            {
                InitializeEcs();
            }

            // Ensure the ECS systems are ready
            if (m_EntityManager == null || !m_LocalPlayerQuery.HasSingleton<PredictedPlayerGhost>())
            {
                // No local player entity found, hide the HUD. This occurs when the player is dead.
                m_RootElement.style.display = DisplayStyle.None;
                return;
            }

            // Get the player's data directly from the ECS component
            PredictedPlayerGhost playerData = m_LocalPlayerQuery.GetSingleton<PredictedPlayerGhost>();

            m_ReflectShieldFrame?.EnableInClassList(
                "reflect-shield-active", playerData.ReflectShieldTimeRemaining > 0f);
            UpdateReflectCooldownWheel(in playerData);

            // Update Health Bar
            if (m_HealthBar != null)
            {
                m_HealthBar.highValue = playerData.MaxHealth > 0 ? playerData.MaxHealth : 100;
                m_HealthBar.value = playerData.CurrentHealth;

                float sizeHealth = playerData.CurrentHealth;
                Color healthColor = sizeHealth <= 100f
                    ? Color.Lerp(Color.red, k_HealthBarColor, Mathf.Clamp01(sizeHealth / 100f))
                    : Color.Lerp(k_HealthBarColor, new Color(1f, 0.35f, 0.05f, 0.9f),
                        Mathf.Clamp01((sizeHealth - 100f) / 100f));

                // Apply the calculated color to the fill element's background
                m_PlayerHealthBarFill.style.backgroundColor = healthColor;
            }

            if (m_SelfResizeChargeBar != null)
            {
                m_SelfResizeChargeBar.style.display = DisplayStyle.None;
            }

            if (m_SelfShrinkUsesLabel != null && m_SelfGrowUsesLabel != null)
            {
                string shrinkBinding = "CTRL";
                string growBinding = "SHIFT";
                if (InputSystemManager.TryGetFirstInputUser(out var inputUser))
                {
                    var controls = (InputSystem_Actions)inputUser.actions;
                    shrinkBinding = InputSystemManager.GetBindingDisplayName(
                        InputSystemManager.GetGameplayAction(controls,
                            InputSystemManager.SelfShrinkActionName)).ToUpperInvariant();
                    growBinding = InputSystemManager.GetBindingDisplayName(
                        InputSystemManager.GetGameplayAction(controls,
                            InputSystemManager.SelfGrowActionName)).ToUpperInvariant();
                }

                m_SelfShrinkUsesLabel.text =
                    $"{shrinkBinding} SELF SHRINK: {playerData.SelfShrinkCharges} / {playerData.MaxSelfShrinkCharges}";
                m_SelfGrowUsesLabel.text =
                    $"{growBinding} SELF GROW: {playerData.SelfGrowCharges} / {playerData.MaxSelfGrowCharges}";
                m_SelfShrinkUsesLabel.style.color = playerData.SelfShrinkCharges > 0
                    ? Color.white
                    : Color.red;
                m_SelfGrowUsesLabel.style.color = playerData.SelfGrowCharges > 0
                    ? Color.white
                    : Color.red;
            }


            // Both abilities are always available, so show both persistent magazines.
            if (m_ShrinkAmmoLabel != null && m_GrowAmmoLabel != null)
            {
                var shrinkWeapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                    WeaponManager.PrimaryWeaponId);
                var growWeapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(
                    WeaponManager.SecondaryWeaponId);
                int shrinkCapacity = shrinkWeapon != null ? shrinkWeapon.MagazineSize : 0;
                int growCapacity = growWeapon != null ? growWeapon.MagazineSize : 0;
                int shrinkAmmo = playerData.EquippedWeaponID == WeaponManager.PrimaryWeaponId
                    ? playerData.CurrentAmmo
                    : playerData.ControllerState.PrimaryWeaponAmmo;
                int growAmmo = playerData.EquippedWeaponID == WeaponManager.SecondaryWeaponId
                    ? playerData.CurrentAmmo
                    : playerData.ControllerState.SecondaryWeaponAmmo;

                UpdateAbilityAmmoDisplay(m_ShrinkAmmoLabel, m_ShrinkAmmoBar,
                    "LMB SHRINK", shrinkAmmo, shrinkCapacity);
                UpdateAbilityAmmoDisplay(m_GrowAmmoLabel, m_GrowAmmoBar,
                    "RMB GROW", growAmmo, growCapacity);
            }

            // Update Reloading Indicator
            if (m_ReloadingLabel != null)
            {
                m_ReloadingLabel.style.display =
                    playerData.ControllerState.IsReloadingState ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Update Reticle
            UpdateReticleVisual(playerData);
        }

        private static void UpdateAbilityAmmoDisplay(Label label, ProgressBar bar,
            string abilityName, int ammo, int capacity)
        {
            label.text = $"{abilityName}  {ammo} / {capacity}";
            label.style.color = ammo <= 0
                ? Color.red
                : ammo <= capacity * 0.3f
                    ? Color.yellow
                    : Color.white;

            if (bar != null)
            {
                bar.highValue = Mathf.Max(1, capacity);
                bar.value = ammo;
            }
        }

        private void UpdateReflectCooldownWheel(in PredictedPlayerGhost playerData)
        {
            if (m_ReflectCooldownWheel == null)
            {
                return;
            }

            float cooldownDuration = Mathf.Max(
                0.01f, playerData.ReflectCooldownDuration);
            float cooldownProgress = 1f - Mathf.Clamp01(
                playerData.ReflectCooldownTimeRemaining / cooldownDuration);
            SetSegmentWidth(m_ReflectCooldownTop,
                Mathf.Clamp01(cooldownProgress * 4f));
            SetSegmentHeight(m_ReflectCooldownRight,
                Mathf.Clamp01(cooldownProgress * 4f - 1f));
            SetSegmentWidth(m_ReflectCooldownBottom,
                Mathf.Clamp01(cooldownProgress * 4f - 2f));
            SetSegmentHeight(m_ReflectCooldownLeft,
                Mathf.Clamp01(cooldownProgress * 4f - 3f));

            bool isReady = playerData.ReflectCooldownTimeRemaining <= 0f;
            float pulse = 0.55f + 0.45f *
                (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f));
            m_ReflectCooldownWheel.style.opacity = isReady ? pulse : 0.85f;

            if (m_ReflectReadyGlow != null)
            {
                m_ReflectReadyGlow.style.display = isReady
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                m_ReflectReadyGlow.style.opacity = pulse;
            }
        }

        private static void SetSegmentWidth(VisualElement segment, float progress)
        {
            if (segment != null)
            {
                segment.style.width = Length.Percent(progress * 100f);
            }
        }

        private static void SetSegmentHeight(VisualElement segment, float progress)
        {
            if (segment != null)
            {
                segment.style.height = Length.Percent(progress * 100f);
            }
        }

        private void UpdateReticleVisual(PredictedPlayerGhost playerData)
        {
            if (m_Reticle == null)
                return;

            if (m_shotFeedbackTimer > 0)
            {
                m_shotFeedbackTimer -= Time.deltaTime;
            }

            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(playerData.EquippedWeaponID);

            if (weaponData == null)
            {
                m_Reticle.style.display = DisplayStyle.None;
                return;
            }

            // Update Reticle Type
            string desiredClass = GetReticleClassName(weaponData.ReticleType);

            // Remove all reticle classes that are NOT the desired one.
            if (desiredClass != k_CrossReticleClass) m_Reticle.RemoveFromClassList(k_CrossReticleClass);
            if (desiredClass != k_TCrossReticleClass) m_Reticle.RemoveFromClassList(k_TCrossReticleClass);
            if (desiredClass != k_OpenCircularReticleClass) m_Reticle.RemoveFromClassList(k_OpenCircularReticleClass);
            if (desiredClass != k_CircularCrossClass) m_Reticle.RemoveFromClassList(k_CircularCrossClass);

            // Now, add the correct class if it's not already present.
            if (!m_Reticle.ClassListContains(desiredClass))
            {
                m_Reticle.AddToClassList(desiredClass);
            }

            // Update Reticle Visibility
            if (m_Reticle.style.display == DisplayStyle.None)
            {
                if (m_Reticle.style.position != StyleKeyword.Initial)
                {
                    m_Reticle.style.position = StyleKeyword.Initial;
                    m_Reticle.style.left = StyleKeyword.Initial;
                    m_Reticle.style.top = StyleKeyword.Initial;
                }

                m_Reticle.style.display = DisplayStyle.Flex;
            }

            m_Reticle.style.width = reticleBaseSize;
            m_Reticle.style.height = reticleBaseSize;

            // Determine if the player can shoot
            bool isReloading = playerData.ControllerState.IsReloadingState;
            bool justFired = playerData.ControllerState.Shoot;
            bool isWeaponOnCooldown = playerData.WeaponCooldown < weaponData.CooldownInMs;

            if (justFired)
            {
                m_shotFeedbackTimer = k_ShotFeedbackDuration;
            }

            bool greyReticleVisual = false;

            // Weapon reloading
            if (isReloading)
            {
                greyReticleVisual = true;
                m_shotFeedbackTimer = 0f;
            }
            // Weapon is on cooldown AND we are not in the shot feedback window.
            else if (isWeaponOnCooldown && m_shotFeedbackTimer <= 0)
            {
                greyReticleVisual = true;
            }

            // Update Reticle Color
            m_Reticle.style.unityBackgroundImageTintColor = greyReticleVisual
                ? new StyleColor(Color.grey)
                : new StyleColor(Color.white);
        }

        private string GetReticleClassName(ReticleType reticleType)
        {
            switch (reticleType)
            {
                case ReticleType.Cross: return k_CrossReticleClass;
                case ReticleType.TCross: return k_TCrossReticleClass;
                case ReticleType.OpenCircular: return k_OpenCircularReticleClass;
                case ReticleType.CircularCross: return k_CircularCrossClass;
                default: return "";
            }
        }
    }
}
