using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class WorldMapController : IDisposable
{
    private const string SharedPanelStyleResourcePath = "Shared/OverlayPanel";
    private const string WorldMapUxmlResourcePath = "WorldMap/WorldMap";
    private const string WorldMapStyleResourcePath = "WorldMap/WorldMap";
    private const string CurrentTileClass = "world-map-tile-current";

    private readonly VisualElement attachTarget;
    private readonly Func<Player> localPlayerProvider;
    private readonly Dictionary<string, VisualElement> tileRootsByMapId = new(StringComparer.OrdinalIgnoreCase);

    private VisualElement overlayRoot;
    private VisualElement panelRoot;
    private VisualElement headerRoot;
    private VisualElement gridRoot;
    private Label currentMapLabel;
    private Button closeButton;
    private DraggableWindowController panelDragController;

    private int renderedCatalogInstanceId;
    private int renderedMapCount = -1;
    private string highlightedMapId = string.Empty;

    public WorldMapController(VisualElement attachTarget, Func<Player> localPlayerProvider)
    {
        this.attachTarget = attachTarget;
        this.localPlayerProvider = localPlayerProvider;
    }

    public bool IsVisible => overlayRoot != null && overlayRoot.resolvedStyle.display != DisplayStyle.None;
    public VisualElement OverlayRoot => overlayRoot;

    public void Attach()
    {
        if (attachTarget == null || overlayRoot != null)
        {
            return;
        }

        VisualTreeAsset worldMapTree = Resources.Load<VisualTreeAsset>(WorldMapUxmlResourcePath);
        if (worldMapTree == null)
        {
            Debug.LogWarning($"WorldMapController: Missing UXML resource '{WorldMapUxmlResourcePath}'.");
            return;
        }

        TemplateContainer worldMapContainer = worldMapTree.Instantiate();
        overlayRoot = worldMapContainer.Q<VisualElement>("WorldMapOverlay") ?? worldMapContainer;
        if (!ReferenceEquals(overlayRoot, worldMapContainer))
        {
            overlayRoot.RemoveFromHierarchy();
        }

        overlayRoot.pickingMode = PickingMode.Position;

        StyleSheet sharedPanelStyle = Resources.Load<StyleSheet>(SharedPanelStyleResourcePath);
        if (sharedPanelStyle != null)
        {
            overlayRoot.styleSheets.Add(sharedPanelStyle);
        }

        StyleSheet worldMapStyle = Resources.Load<StyleSheet>(WorldMapStyleResourcePath);
        if (worldMapStyle != null)
        {
            overlayRoot.styleSheets.Add(worldMapStyle);
        }

        attachTarget.Add(overlayRoot);
        overlayRoot.BlockRaycasts();

        BindUiElements();
        panelDragController = new DraggableWindowController(overlayRoot, panelRoot, headerRoot, closeButton);
        RegisterCallbacks();
        RebuildGridIfNeeded(force: true);
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

        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
    }

    public void Show()
    {
        RebuildGridIfNeeded(force: true);
        Refresh();
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

        RebuildGridIfNeeded(force: false);

        string currentMapId = ResolveCurrentMapId();
        if (currentMapLabel != null)
        {
            currentMapLabel.text = string.IsNullOrWhiteSpace(currentMapId)
                ? "Current map: unknown"
                : $"Current map: {currentMapId}";
        }

        if (string.Equals(highlightedMapId, currentMapId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (KeyValuePair<string, VisualElement> pair in tileRootsByMapId)
        {
            pair.Value?.EnableInClassList(
                CurrentTileClass,
                string.Equals(pair.Key, currentMapId, StringComparison.OrdinalIgnoreCase));
        }

        highlightedMapId = currentMapId;
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

        tileRootsByMapId.Clear();
        overlayRoot = null;
        panelRoot = null;
        headerRoot = null;
        gridRoot = null;
        currentMapLabel = null;
        closeButton = null;
        highlightedMapId = string.Empty;
        renderedCatalogInstanceId = 0;
        renderedMapCount = -1;
    }

    private void BindUiElements()
    {
        if (overlayRoot == null)
        {
            return;
        }

        panelRoot = overlayRoot.Q<VisualElement>("WorldMapPanel");
        headerRoot = overlayRoot.Q<VisualElement>("WorldMapHeader");
        gridRoot = overlayRoot.Q<VisualElement>("WorldMapGrid");
        currentMapLabel = overlayRoot.Q<Label>("WorldMapCurrentMapLabel");
        closeButton = overlayRoot.Q<Button>("WorldMapCloseButton");
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
            panelDragController?.CenterInBounds();
        }
        else
        {
            panelDragController?.StopDragging();
        }
    }

    private void RebuildGridIfNeeded(bool force)
    {
        WorldMapCatalog catalog = WorldMapManager.Instance != null ? WorldMapManager.Instance.Catalog : null;
        int catalogInstanceId = catalog != null ? catalog.GetInstanceID() : 0;
        int mapCount = catalog != null && catalog.Maps != null ? catalog.Maps.Count : 0;

        if (!force &&
            catalogInstanceId == renderedCatalogInstanceId &&
            mapCount == renderedMapCount &&
            tileRootsByMapId.Count == mapCount)
        {
            return;
        }

        renderedCatalogInstanceId = catalogInstanceId;
        renderedMapCount = mapCount;
        RebuildGrid(catalog);
    }

    private void RebuildGrid(WorldMapCatalog catalog)
    {
        if (gridRoot == null)
        {
            return;
        }

        gridRoot.Clear();
        tileRootsByMapId.Clear();

        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            Label emptyLabel = new("No world map data is available yet.");
            emptyLabel.AddToClassList("world-map-empty-state");
            gridRoot.Add(emptyLabel);
            return;
        }

        List<WorldMapDefinition> orderedMaps = new(catalog.Maps.Count);
        for (int index = 0; index < catalog.Maps.Count; index++)
        {
            WorldMapDefinition definition = catalog.Maps[index];
            if (definition != null)
            {
                orderedMaps.Add(definition);
            }
        }

        orderedMaps.Sort(static (left, right) =>
        {
            int rowComparison = right.Row.CompareTo(left.Row);
            return rowComparison != 0 ? rowComparison : left.Column.CompareTo(right.Column);
        });

        int currentRow = int.MinValue;
        VisualElement rowContainer = null;

        for (int index = 0; index < orderedMaps.Count; index++)
        {
            WorldMapDefinition definition = orderedMaps[index];
            if (definition.Row != currentRow || rowContainer == null)
            {
                currentRow = definition.Row;
                rowContainer = new VisualElement();
                rowContainer.AddToClassList("world-map-grid-row");
                gridRoot.Add(rowContainer);
            }

            VisualElement tileRoot = CreateTile(definition);
            rowContainer.Add(tileRoot);
            tileRootsByMapId[definition.MapId] = tileRoot;
        }
    }

    private static VisualElement CreateTile(WorldMapDefinition definition)
    {
        var tileRoot = new VisualElement
        {
            name = $"WorldMapTile_{definition.MapId}",
            pickingMode = PickingMode.Ignore
        };
        tileRoot.AddToClassList("world-map-tile");

        Label headerLabel = new(string.IsNullOrWhiteSpace(definition.HeaderLabel) ? string.Empty : definition.HeaderLabel);
        headerLabel.AddToClassList("world-map-tile-header");
        headerLabel.style.display = string.IsNullOrWhiteSpace(definition.HeaderLabel) ? DisplayStyle.None : DisplayStyle.Flex;
        tileRoot.Add(headerLabel);

        VisualElement iconElement = new()
        {
            pickingMode = PickingMode.Ignore
        };
        iconElement.AddToClassList("world-map-tile-icon");
        if (definition.TileIcon != null)
        {
            iconElement.style.backgroundImage = new StyleBackground(definition.TileIcon);
        }
        else
        {
            iconElement.style.display = DisplayStyle.None;
        }

        tileRoot.Add(iconElement);

        Label mapIdLabel = new(definition.MapId);
        mapIdLabel.AddToClassList("world-map-tile-id");
        tileRoot.Add(mapIdLabel);

        return tileRoot;
    }

    private string ResolveCurrentMapId()
    {
        Player localPlayer = localPlayerProvider != null ? localPlayerProvider.Invoke() : null;
        if (localPlayer != null && localPlayer.IsOwner && localPlayer.IsSpawned)
        {
            return localPlayer.CurrentWorldMapId;
        }

        return WorldMapManager.Instance != null ? WorldMapManager.Instance.StartingMapId : string.Empty;
    }

    private void OnCloseClicked()
    {
        Hide();
    }

    private void OnOverlayPointerUp(PointerUpEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse || !ReferenceEquals(evt.target, overlayRoot))
        {
            return;
        }

        if (panelDragController != null && panelDragController.IsDragging)
        {
            return;
        }

        Hide();
        evt.StopPropagation();
    }

    private static void OnPanelPointerDown(PointerDownEvent evt)
    {
        evt.StopPropagation();
    }

    private static void OnPanelPointerUp(PointerUpEvent evt)
    {
        evt.StopPropagation();
    }
}
