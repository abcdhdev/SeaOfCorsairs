using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public partial class GameUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private IsometricCameraController cameraController;
    [SerializeField] private InputActionReference centerCameraAction;
    [SerializeField] private VisualTreeAsset actionSlotTemplate;

    [Header("EXP UI")]
    [SerializeField, Min(0)] private int playerExperience = 10;
    [SerializeField, Min(1)] private int playerExperienceToNextLevel = 3900;

    private const int SlotCount = 10;
    private const string ActionSlotTemplateAssetPath = "Assets/UI/Components/ActionSlot/ActionSlot.uxml";
    private const string HealthTemplateResourcePath = "GameHUD/Fragments/HealthBox";
    private const string ActionSlotRootName = "ActionSlotRoot";
    private const string ActionSlotTopLabelName = "ActionSlotTopLabel";
    private const string ActionSlotMainLabelName = "ActionSlotMainLabel";
    private const string ActionSlotIconName = "ActionSlotIcon";
    private const string EmptySlotText = "Drag skill here";
    private const string TopSlotEmptyClass = "action-slot-empty";
    private const string TopSlotFilledClass = "action-slot-has-skill";
    private const string DropTargetClass = "action-slot-drop-target";
    private const string ActionBarClosedClass = "action-bar-closed";
    private const string ToggleClosedClass = "action-bar-toggle-closed";
    private const string DisabledSlotClass = "action-slot-disabled";
    private static readonly string[] SlotHotkeys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
    private const float SourceSlotDragThreshold = 12f;
    private const int AmmoSourceSlotIndex = 3;
    private const int HarpoonSourceSlotIndex = 4;

    private sealed class SkillDefinition
    {
        public string Id;
        public string DisplayName;
        public string IconClass;
        public Action Execute;
    }

    private sealed class SlotView
    {
        public VisualElement Root;
        public Label MainLabel;
        public VisualElement Icon;
        public string AppliedIconClass;
    }

    private VisualElement root;
    private Button actionBarToggleButton;
    private VisualElement actionBarBody;
    private Button centerCameraButton;
    private VisualElement playerHpBarFill;
    private Label playerHpLabel;
    private VisualElement playerExpBarFill;
    private Label playerExpLabel;
    private VisualElement topActionSlotsRow;
    private VisualElement bottomSkillSlotsRow;
    private VisualElement cameraZoomRoot;
    private Slider cameraZoomSlider;
    private VisualElement cameraZoomThumb;

    private VisualElement topMenuBar;
    private Label topMenuFpsPingLabel;
    private Label resourceGoldLabel;
    private Label resourceDiamondLabel;
    private Button topMenuChatButton;
    private Button topMenuBagButton;
    private Button topMenuShieldButton;
    private VisualElement topMenuShieldAnchor;
    private VisualElement topMenuShieldDropdown;
    private Button topMenuIslandBuildingButton;
    private Button topMenuGuildsButton;
    private Button topMenuShipButton;
    private Button topMenuLogoutButton;
    private float fpsDeltaTime;
    private float fpsPingTimer;
    private bool isTopMenuShieldDropdownOpen;

    private MarketController marketController;
    private GuildManagementController guildManagementController;

    private VisualElement combatOverlayLayer;
    private VisualElement healthBox;
    private Label healthLabel;
    private Label targetNameLabel;
    private VisualElement healthBarFill;
    private VisualElement deadOverlayRoot;
    private Label deadOverlayTimerLabel;

    private readonly SlotView[] topSlotViews = new SlotView[SlotCount];
    private readonly SlotView[] bottomSlotViews = new SlotView[SlotCount];
    private readonly SkillDefinition[] topSlotAssignments = new SkillDefinition[SlotCount];
    private readonly List<SkillDefinition> sourceSkills = new List<SkillDefinition>(SlotCount);
    private readonly Dictionary<VisualElement, int> topSlotLookup = new Dictionary<VisualElement, int>(SlotCount);

    private bool isActionBarOpen = true;
    private bool isDraggingSkill;
    private int draggingPointerId = -1;
    private SkillDefinition draggingSkill;
    private Label dragGhostLabel;
    private int currentDropSlotIndex = -1;

    private bool isSourceSlotPressPending;
    private int pendingSourcePointerId = -1;
    private Vector2 pendingSourcePointerStart;
    private SkillDefinition pendingSourceSkill;

    private VisualElement ammoMenuBackdrop;
    private VisualElement ammoMenuPanel;
    private bool isAmmoMenuOpen;
    private VisualElement harpoonMenuBackdrop;
    private VisualElement harpoonMenuPanel;
    private bool isHarpoonMenuOpen;
    private ShipSectionController shipSectionController;

    private IHealthSystem trackedHealthTarget;
    private Component trackedHealthTargetComponent;
    private Player cachedLocalPlayer;
    private Player observedLocalPlayer;
    private bool isLocalPlayerDead;
    private int displayedPlayerHealth = -1;
    private int displayedPlayerMaxHealth = -1;
    private int displayedPlayerGold = -1;
    private int displayedPlayerDiamonds = -1;
    private int displayedPlayerExperience = -1;
    private int displayedPlayerExperienceToNext = -1;
    private float displayedCameraZoom = float.NaN;
    private bool missingHealthTemplateLogged;

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            BuildEditorActionBarPreview();
            return;
        }

        if (uiDocument == null)
        {
            Debug.LogWarning("GameUIController: UIDocument reference is missing.");
            return;
        }

        root = uiDocument.rootVisualElement;
        EnsureHudLayoutComposed();
        BindUiElements();
        Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        Player.LocalPlayerSpawned += OnLocalPlayerSpawned;
        BindLocalPlayerDeathEvents(Player.LocalPlayer);
        EnsureShipSection();
        SetCombatOverlayVisible(true);
        EnsureAmmoMenu();
        EnsureMarketSection();
        EnsureGuildManagementSection();
        RegisterBlockingUiElements();
        BuildSourceSkills();
        BuildActionBarSlots();
        InitializeCameraZoomControl();
        RegisterUiCallbacks();
        RefreshActionBarVisibility();
        RefreshIslandEditUi();

        SetHealthVisible(false);
        SetDeadOverlayVisible(false);
        TrackHealthTarget(GetSelectedHealthTarget());
        UpdatePlayerHealthBar();
        UpdatePlayerExpBar();

        if (centerCameraAction != null && centerCameraAction.action != null)
        {
            centerCameraAction.action.Enable();
            centerCameraAction.action.performed += OnCenterCameraAction;
        }
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
        {
            StopSkillDrag();
            topSlotLookup.Clear();
            ClearUiElementReferences();
            return;
        }

        UnregisterBlockingUiElements();
        UnregisterUiCallbacks();
        Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        UnbindLocalPlayerDeathEvents();
        CloseAmmoMenu();
        CloseHarpoonMenu();
        marketController?.Dispose();
        marketController = null;
        guildManagementController?.Dispose();
        guildManagementController = null;
        StopSkillDrag();
        ClearPendingSourcePress();
        TrackHealthTarget(null);
        DisposeRewardNotifications();

        if (centerCameraAction != null && centerCameraAction.action != null)
        {
            centerCameraAction.action.performed -= OnCenterCameraAction;
            centerCameraAction.action.Disable();
        }

        shipSectionController?.Dispose();
        shipSectionController = null;
        ClearUiElementReferences();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (actionSlotTemplate == null)
        {
            actionSlotTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ActionSlotTemplateAssetPath);
        }

        if (Application.isPlaying)
        {
            return;
        }

        EditorApplication.delayCall -= RefreshEditorPreviewDelayed;
        EditorApplication.delayCall += RefreshEditorPreviewDelayed;
    }

    private void RefreshEditorPreviewDelayed()
    {
        if (this == null || !isActiveAndEnabled || Application.isPlaying)
        {
            return;
        }

        BuildEditorActionBarPreview();
    }
#endif

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!isLocalPlayerDead)
        {
            TrackHealthTarget(GetSelectedHealthTarget());
        }

        if (trackedHealthTargetComponent != null)
        {
            UpdateHealthDisplay();
        }

        UpdateDeathOverlay();
        UpdateRewardNotifications();
        UpdatePlayerHealthBar();
        UpdatePlayerWalletLabels();
        UpdatePlayerExpBar();
        RefreshWeaponSelectionTooltips();
        UpdateCameraZoomControl();
        RefreshCoordinateRuler();
        UpdateFpsAndPing();
        marketController?.Refresh();
        guildManagementController?.Refresh();
        shipSectionController?.Refresh();
        RefreshIslandEditUi();
        RefreshActionBarVisibility();
        UpdateTopMenuButtonStates();
    }

    public void SetPlayerExperience(int currentExperience, int requiredExperience)
    {
        playerExperience = Mathf.Max(0, currentExperience);
        playerExperienceToNextLevel = Mathf.Max(1, requiredExperience);
        displayedPlayerExperience = -1;
        displayedPlayerExperienceToNext = -1;
        UpdatePlayerExpBar();
    }

    private void BuildEditorActionBarPreview()
    {
        if (uiDocument == null)
        {
            return;
        }

        root = uiDocument.rootVisualElement;
        if (root == null)
        {
            return;
        }

        EnsureHudLayoutComposed();
        BindUiElements();
        SetCombatOverlayVisible(false);
        BuildSourceSkills();
        BuildActionBarPreviewSlots();
        InitializeCameraZoomControl();
        RefreshIslandEditUi();

        isActionBarOpen = true;
        RefreshActionBarVisibility();
    }

    private void BindUiElements()
    {
        if (root == null)
        {
            return;
        }

        EnsureHudLayoutComposed();

        actionBarToggleButton = root.Q<Button>("ActionBarToggleButton");
        actionBarBody = root.Q<VisualElement>("ActionBarBody");
        centerCameraButton = root.Q<Button>("CenterCameraButton");
        playerHpBarFill = root.Q<VisualElement>("PlayerHpBarFill");
        playerHpLabel = root.Q<Label>("PlayerHpLabel");
        playerExpBarFill = root.Q<VisualElement>("PlayerExpBarFill");
        playerExpLabel = root.Q<Label>("PlayerExpLabel");
        topActionSlotsRow = root.Q<VisualElement>("TopActionSlotsRow");
        bottomSkillSlotsRow = root.Q<VisualElement>("BottomSkillSlotsRow");
        cameraZoomRoot = root.Q<VisualElement>("CameraZoomRoot");
        cameraZoomSlider = root.Q<Slider>("CameraZoomSlider");
        cameraZoomThumb = root.Q<VisualElement>("CameraZoomThumb");

        combatOverlayLayer = root.Q<VisualElement>("CombatOverlayLayer");
        EnsureHealthTemplateInstance();
        healthBox = combatOverlayLayer != null ? combatOverlayLayer.Q<VisualElement>("HealthBox") : null;
        healthLabel = combatOverlayLayer != null ? combatOverlayLayer.Q<Label>("HealthLabel") : null;
        targetNameLabel = combatOverlayLayer != null ? combatOverlayLayer.Q<Label>("TargetNameLabel") : null;
        healthBarFill = combatOverlayLayer != null ? combatOverlayLayer.Q<VisualElement>("HealthBarFill") : null;
        BindRewardNotificationElements();
        deadOverlayRoot = root.Q<VisualElement>("DeadOverlayRoot");
        deadOverlayTimerLabel = root.Q<Label>("DeadOverlayTimerLabel");

        topMenuBar = root.Q<VisualElement>("TopMenuBar");
        topMenuFpsPingLabel = root.Q<Label>("TopMenuFpsPingLabel");
        resourceGoldLabel = root.Q<Label>("ResourceGoldLabel");
        resourceDiamondLabel = root.Q<Label>("ResourceDiamondLabel");
        topMenuChatButton = root.Q<Button>("TopMenuChatButton");
        topMenuBagButton = root.Q<Button>("TopMenuBagButton");
        topMenuShieldButton = root.Q<Button>("TopMenuShieldButton");
        topMenuShieldAnchor = root.Q<VisualElement>("TopMenuShieldAnchor");
        topMenuShieldDropdown = root.Q<VisualElement>("TopMenuShieldDropdown");
        topMenuIslandBuildingButton = root.Q<Button>("TopMenuIslandBuildingButton");
        topMenuGuildsButton = root.Q<Button>("TopMenuGuildsButton");
        topMenuShipButton = root.Q<Button>("TopMenuShipButton");
        topMenuLogoutButton = root.Q<Button>("TopMenuLogoutButton");
        BindCoordinateRulerElements();
        if (topMenuShieldDropdown != null)
        {
            topMenuShieldDropdown.pickingMode = PickingMode.Position;
            topMenuShieldDropdown.style.display = DisplayStyle.None;
        }
        BindIslandEditElements();
    }

    private void ClearUiElementReferences()
    {
        cachedLocalPlayer = null;
        observedLocalPlayer = null;
        isLocalPlayerDead = false;
        displayedPlayerHealth = -1;
        displayedPlayerMaxHealth = -1;
        displayedPlayerGold = -1;
        displayedPlayerDiamonds = -1;
        displayedPlayerExperience = -1;
        displayedPlayerExperienceToNext = -1;
        displayedCameraZoom = float.NaN;
        ammoMenuBackdrop = null;
        ammoMenuPanel = null;
        harpoonMenuBackdrop = null;
        harpoonMenuPanel = null;
        isAmmoMenuOpen = false;
        isHarpoonMenuOpen = false;
        topMenuBar = null;
        topMenuFpsPingLabel = null;
        resourceGoldLabel = null;
        resourceDiamondLabel = null;
        topMenuChatButton = null;
        topMenuBagButton = null;
        topMenuShieldButton = null;
        topMenuShieldAnchor = null;
        topMenuShieldDropdown = null;
        topMenuIslandBuildingButton = null;
        topMenuGuildsButton = null;
        topMenuShipButton = null;
        topMenuLogoutButton = null;
        isTopMenuShieldDropdownOpen = false;
        marketController = null;
        guildManagementController = null;
        actionBarToggleButton = null;
        actionBarBody = null;
        ClearRewardNotificationState();
        centerCameraButton = null;
        playerHpBarFill = null;
        playerHpLabel = null;
        playerExpBarFill = null;
        playerExpLabel = null;
        topActionSlotsRow = null;
        bottomSkillSlotsRow = null;
        combatOverlayLayer = null;
        trackedHealthTarget = null;
        trackedHealthTargetComponent = null;
        healthBox = null;
        healthLabel = null;
        targetNameLabel = null;
        healthBarFill = null;
        deadOverlayRoot = null;
        deadOverlayTimerLabel = null;
        cameraZoomRoot = null;
        cameraZoomSlider = null;
        cameraZoomThumb = null;
        ClearCoordinateRulerReferences();
        ClearIslandEditReferences();
        root = null;

        topSlotLookup.Clear();
        Array.Clear(topSlotViews, 0, topSlotViews.Length);
        Array.Clear(bottomSlotViews, 0, bottomSlotViews.Length);
    }

    private void EnsureShipSection()
    {
        if (root == null || shipSectionController != null)
        {
            return;
        }

        VisualElement attachTarget = root.Q<VisualElement>("MetaRoot") ?? root;
        shipSectionController = new ShipSectionController(attachTarget, ShipSectionDummyData.Create());
        shipSectionController.Attach();
    }

    private void EnsureMarketSection()
    {
        if (root == null || marketController != null)
        {
            return;
        }

        VisualElement attachTarget = root.Q<VisualElement>("MetaRoot") ?? root;
        marketController = new MarketController(attachTarget, GetLocalPlayerForMarket);
        marketController.Attach();
    }

    private void EnsureGuildManagementSection()
    {
        if (root == null || guildManagementController != null)
        {
            return;
        }

        VisualElement attachTarget = root.Q<VisualElement>("MetaRoot") ?? root;
        guildManagementController = new GuildManagementController(
            attachTarget);
        guildManagementController.Attach();
    }

    private void RefreshWeaponSelectionTooltips()
    {
        if (!TryGetLocalPlayer(out Player localPlayer) || localPlayer == null)
        {
            return;
        }

        RefreshAmmoSkillTooltip(localPlayer);
        RefreshHarpoonSkillTooltip(localPlayer);
    }
}
