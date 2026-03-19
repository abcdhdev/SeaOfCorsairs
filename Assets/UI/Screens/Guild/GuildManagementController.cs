using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class GuildManagementController : IDisposable
{
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string UxmlResourcePath = "Guild/GuildManagement";
    private const string UssResourcePath = "Guild/GuildManagement";

    private readonly VisualElement attachTarget;
    private readonly Func<Player> localPlayerProvider;
    private readonly Func<IslandBuildManager> buildManagerProvider;

    private VisualElement overlayRoot;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private Label goldValueLabel;
    private Label builtCountLabel;
    private Label selectedTurretLabel;
    private Label selectedTurretHealthLabel;
    private Label statusLabel;
    private Button buildIslandButton;
    private Button moveTurretButton;
    private Button deleteTurretButton;
    private Button closeButton;
    private DraggableWindowController panelDragController;

    public GuildManagementController(
        VisualElement attachTarget,
        Func<Player> localPlayerProvider,
        Func<IslandBuildManager> buildManagerProvider)
    {
        this.attachTarget = attachTarget;
        this.localPlayerProvider = localPlayerProvider;
        this.buildManagerProvider = buildManagerProvider;
    }

    public bool IsVisible => overlayRoot != null && overlayRoot.resolvedStyle.display != DisplayStyle.None;
    public VisualElement OverlayRoot => overlayRoot;

    public void Attach()
    {
        if (attachTarget == null || overlayRoot != null)
        {
            return;
        }

        VisualTreeAsset visualTree = Resources.Load<VisualTreeAsset>(UxmlResourcePath);
        if (visualTree == null)
        {
            Debug.LogWarning($"GuildManagementController: Missing UXML resource '{UxmlResourcePath}'.");
            return;
        }

        TemplateContainer container = visualTree.Instantiate();
        overlayRoot = container.Q<VisualElement>("GuildManagementOverlay") ?? container;
        if (!ReferenceEquals(overlayRoot, container))
        {
            overlayRoot.RemoveFromHierarchy();
        }

        overlayRoot.pickingMode = PickingMode.Position;

        StyleSheet sharedPanelStyle = Resources.Load<StyleSheet>(SharedPanelStyleResourcePath);
        if (sharedPanelStyle != null)
        {
            overlayRoot.styleSheets.Add(sharedPanelStyle);
        }

        StyleSheet panelStyle = Resources.Load<StyleSheet>(UssResourcePath);
        if (panelStyle != null)
        {
            overlayRoot.styleSheets.Add(panelStyle);
        }

        attachTarget.Add(overlayRoot);
        overlayRoot.BlockRaycasts();

        BindUiElements();
        panelDragController = new DraggableWindowController(overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        Refresh();
        SetVisible(false);
    }

    public void ToggleVisibility()
    {
        if (overlayRoot == null)
        {
            Attach();
        }

        if (overlayRoot == null)
        {
            return;
        }

        SetVisible(!IsVisible);
    }

    public void Show()
    {
        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void Refresh()
    {
        if (overlayRoot == null)
        {
            return;
        }

        Player localPlayer = localPlayerProvider?.Invoke();
        IslandBuildManager buildManager = buildManagerProvider?.Invoke();
        IslandTurret selectedTurret = buildManager != null ? buildManager.GetSelectedTurret() : null;
        IslandTurret selectedOwnedTurret = buildManager != null ? buildManager.GetSelectedOwnedTurret() : null;

        if (goldValueLabel != null)
        {
            goldValueLabel.text = localPlayer != null ? localPlayer.Gold.ToString("N0") : "0";
        }

        if (builtCountLabel != null)
        {
            int builtCount = buildManager != null ? buildManager.GetLocalOwnedTurretCount() : 0;
            builtCountLabel.text = $"{builtCount} / {IslandBuildManager.Instance?.MaxTurrets ?? 6}";
        }

        if (selectedTurretLabel != null)
        {
            if (selectedTurret == null)
            {
                selectedTurretLabel.text = "No turret selected";
            }
            else if (selectedOwnedTurret != null)
            {
                selectedTurretLabel.text = $"Selected: {selectedTurret.name}";
            }
            else
            {
                selectedTurretLabel.text = $"Selected: {selectedTurret.name} (not yours)";
            }
        }

        if (selectedTurretHealthLabel != null)
        {
            selectedTurretHealthLabel.text = selectedTurret != null
                ? $"Health: {selectedTurret.CurrentHealth:N0} / {selectedTurret.MaxHealth:N0}"
                : "Health: -";
        }

        if (statusLabel != null && buildManager != null)
        {
            statusLabel.text = buildManager.StatusMessage;
        }

        if (buildIslandButton != null)
        {
            buildIslandButton.text = "Build Island";
            buildIslandButton.SetEnabled(localPlayer != null && buildManager != null && !buildManager.IsPlacementActive);
        }

        if (moveTurretButton != null)
        {
            moveTurretButton.SetEnabled(selectedOwnedTurret != null && buildManager != null && !buildManager.IsPlacementActive);
        }

        if (deleteTurretButton != null)
        {
            deleteTurretButton.SetEnabled(selectedOwnedTurret != null && buildManager != null && !buildManager.IsPlacementActive);
        }
    }

    public void Dispose()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelDragController?.Dispose();
        panelDragController = null;
        UnregisterCallbacks();
        overlayRoot.AllowRaycasts();

        if (overlayRoot.parent != null)
        {
            overlayRoot.parent.Remove(overlayRoot);
        }

        overlayRoot = null;
        panelRoot = null;
        headerRoot = null;
        goldValueLabel = null;
        builtCountLabel = null;
        selectedTurretLabel = null;
        selectedTurretHealthLabel = null;
        statusLabel = null;
        buildIslandButton = null;
        moveTurretButton = null;
        deleteTurretButton = null;
        closeButton = null;
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelRoot = overlayRoot.Q<VisualElement>("GuildManagementPanel");
        headerRoot = overlayRoot.Q<VisualElement>("GuildManagementHeader");
        goldValueLabel = overlayRoot.Q<Label>("GuildGoldValueLabel");
        builtCountLabel = overlayRoot.Q<Label>("GuildBuiltCountLabel");
        selectedTurretLabel = overlayRoot.Q<Label>("GuildSelectedTurretLabel");
        selectedTurretHealthLabel = overlayRoot.Q<Label>("GuildSelectedTurretHealthLabel");
        statusLabel = overlayRoot.Q<Label>("GuildStatusLabel");
        buildIslandButton = overlayRoot.Q<Button>("GuildBuildIslandButton");
        moveTurretButton = overlayRoot.Q<Button>("GuildMoveTurretButton");
        deleteTurretButton = overlayRoot.Q<Button>("GuildDeleteTurretButton");
        closeButton = overlayRoot.Q<Button>("GuildCloseButton");
    }

    private void RegisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.RegisterCallback<PointerUpEvent>(OnOverlayPointerUp);
        }

        if (panelRoot != null)
        {
            panelRoot.RegisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.RegisterCallback<PointerUpEvent>(OnPanelPointerUp);
        }

        if (closeButton != null)
        {
            closeButton.clicked += OnCloseClicked;
        }

        if (buildIslandButton != null)
        {
            buildIslandButton.clicked += OnBuildIslandClicked;
        }

        if (moveTurretButton != null)
        {
            moveTurretButton.clicked += OnMoveTurretClicked;
        }

        if (deleteTurretButton != null)
        {
            deleteTurretButton.clicked += OnDeleteTurretClicked;
        }
    }

    private void UnregisterCallbacks()
    {
        if (overlayRoot != null)
        {
            overlayRoot.UnregisterCallback<PointerUpEvent>(OnOverlayPointerUp);
        }

        if (panelRoot != null)
        {
            panelRoot.UnregisterCallback<PointerDownEvent>(OnPanelPointerDown);
            panelRoot.UnregisterCallback<PointerUpEvent>(OnPanelPointerUp);
        }

        if (closeButton != null)
        {
            closeButton.clicked -= OnCloseClicked;
        }

        if (buildIslandButton != null)
        {
            buildIslandButton.clicked -= OnBuildIslandClicked;
        }

        if (moveTurretButton != null)
        {
            moveTurretButton.clicked -= OnMoveTurretClicked;
        }

        if (deleteTurretButton != null)
        {
            deleteTurretButton.clicked -= OnDeleteTurretClicked;
        }
    }

    private void SetVisible(bool isVisible)
    {
        if (overlayRoot == null)
        {
            return;
        }

        overlayRoot.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        if (isVisible)
        {
            Refresh();
            panelDragController?.CenterInBounds();
        }
        else
        {
            panelDragController?.StopDragging();
        }
    }

    private void OnBuildIslandClicked()
    {
        IslandBuildManager buildManager = buildManagerProvider?.Invoke();
        if (buildManager != null && buildManager.BeginBuildPlacement())
        {
            Hide();
        }

        Refresh();
    }

    private void OnMoveTurretClicked()
    {
        IslandBuildManager buildManager = buildManagerProvider?.Invoke();
        if (buildManager != null && buildManager.BeginMovePlacement(buildManager.GetSelectedOwnedTurret()))
        {
            Hide();
        }

        Refresh();
    }

    private void OnDeleteTurretClicked()
    {
        buildManagerProvider?.Invoke()?.DeleteSelectedTurret();
        Refresh();
    }

    private void OnCloseClicked()
    {
        Hide();
    }

    private static void OnPanelPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private static void OnPanelPointerUp(PointerUpEvent evt)
    {
        evt.StopPropagation();
    }

    private void OnOverlayPointerUp(PointerUpEvent evt)
    {
        if (panelDragController != null && panelDragController.IsDragging)
        {
            evt.StopPropagation();
            return;
        }

        Hide();
        evt.StopPropagation();
    }
}
