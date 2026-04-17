using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private const string MinimapWorldMapButtonActiveClass = "minimap-world-map-button-active";

    [Header("World Map Debug")]
    [SerializeField] private bool showWorldMapTravelDebug = true;

    private Button minimapWorldMapButton;
    private VisualElement worldMapTravelPromptRoot;
    private Button worldMapTravelPromptButton;
    private VisualElement worldMapTravelDebugRoot;
    private Label worldMapTravelDebugLocationLabel;
    private Label worldMapTravelDebugLocalLocationLabel;
    private Label worldMapTravelDebugPromptLabel;
    private Label worldMapTravelDebugReasonLabel;
    private Label worldMapTravelDebugLastTransitionLabel;
    private Label worldMapTravelDebugResolutionLabel;
    private Label worldMapTravelDebugMovementProbeLabel;
    private WorldMapController worldMapController;

    private bool isTravelPromptVisible;
    private MapTransitionDirection currentTravelPromptDirection;
    private string currentTravelPromptDestinationMapId = string.Empty;

    private void EnsureWorldMapSection()
    {
        if (root == null || worldMapController != null)
        {
            return;
        }

        VisualElement attachTarget = root.Q<VisualElement>("MetaRoot") ?? root;
        worldMapController = new WorldMapController(attachTarget, GetLocalPlayerForWorldMap);
        worldMapController.Attach();
        WorldMapManager.Instance?.RegisterWorldMapOverlayController(worldMapController);
    }

    private void BindWorldMapUiElements()
    {
        if (root == null)
        {
            return;
        }

        WorldMapManager.Instance?.RegisterHudController(this);

        minimapWorldMapButton = root.Q<Button>("MinimapWorldMapButton");
        worldMapTravelPromptRoot = root.Q<VisualElement>("WorldMapTravelPromptRoot");
        worldMapTravelPromptButton = root.Q<Button>("WorldMapTravelPromptButton");
        worldMapTravelDebugRoot = root.Q<VisualElement>("WorldMapTravelDebugRoot");
        worldMapTravelDebugLocationLabel = root.Q<Label>("WorldMapTravelDebugLocationLabel");
        worldMapTravelDebugLocalLocationLabel = root.Q<Label>("WorldMapTravelDebugLocalLocationLabel");
        worldMapTravelDebugPromptLabel = root.Q<Label>("WorldMapTravelDebugPromptLabel");
        worldMapTravelDebugReasonLabel = root.Q<Label>("WorldMapTravelDebugReasonLabel");
        worldMapTravelDebugLastTransitionLabel = root.Q<Label>("WorldMapTravelDebugLastTransitionLabel");
        worldMapTravelDebugResolutionLabel = root.Q<Label>("WorldMapTravelDebugResolutionLabel");
        worldMapTravelDebugMovementProbeLabel = root.Q<Label>("WorldMapTravelDebugMovementProbeLabel");

        if (worldMapTravelPromptRoot != null)
        {
            worldMapTravelPromptRoot.style.display = DisplayStyle.None;
        }

        if (worldMapTravelPromptButton != null)
        {
            worldMapTravelPromptButton.text = string.Empty;
        }

        RefreshWorldMapButtonState();
        ResetTravelPromptState();
        RefreshWorldMapTravelDebug();
    }

    private void RegisterWorldMapBlockingUiElements()
    {
        if (minimapWorldMapButton != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(minimapWorldMapButton);
        }

        if (worldMapTravelPromptButton != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(worldMapTravelPromptButton);
        }

        if (worldMapController != null && worldMapController.OverlayRoot != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(worldMapController.OverlayRoot);
        }
    }

    private void UnregisterWorldMapBlockingUiElements()
    {
        if (minimapWorldMapButton != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(minimapWorldMapButton);
        }

        if (worldMapTravelPromptButton != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(worldMapTravelPromptButton);
        }

        if (worldMapController != null && worldMapController.OverlayRoot != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(worldMapController.OverlayRoot);
        }
    }

    private void RegisterWorldMapCallbacks()
    {
        if (minimapWorldMapButton != null)
        {
            minimapWorldMapButton.clicked += OnMinimapWorldMapClicked;
        }

        if (worldMapTravelPromptButton != null)
        {
            worldMapTravelPromptButton.clicked += OnWorldMapTravelPromptClicked;
        }
    }

    private void UnregisterWorldMapCallbacks()
    {
        if (minimapWorldMapButton != null)
        {
            minimapWorldMapButton.clicked -= OnMinimapWorldMapClicked;
        }

        if (worldMapTravelPromptButton != null)
        {
            worldMapTravelPromptButton.clicked -= OnWorldMapTravelPromptClicked;
        }
    }

    private void RefreshWorldMapUi()
    {
        worldMapController?.Refresh();
        RefreshWorldMapButtonState();
        RefreshTravelPrompt();
        RefreshWorldMapTravelDebug();
    }

    private void HandleWorldMapDebugInput()
    {
        if (!Application.isPlaying || !showWorldMapTravelDebug)
        {
            return;
        }

        if (!TryGetLocalPlayer(out Player localPlayer) || localPlayer == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.f6Key.wasPressedThisFrame)
        {
            TriggerWorldMapDebugTransition(localPlayer, MapTransitionDirection.North);
        }
        else if (keyboard.f7Key.wasPressedThisFrame)
        {
            TriggerWorldMapDebugTransition(localPlayer, MapTransitionDirection.East);
        }
        else if (keyboard.f8Key.wasPressedThisFrame)
        {
            TriggerWorldMapDebugTransition(localPlayer, MapTransitionDirection.South);
        }
        else if (keyboard.f9Key.wasPressedThisFrame)
        {
            TriggerWorldMapDebugTransition(localPlayer, MapTransitionDirection.West);
        }
    }

    private void DisposeWorldMapUi()
    {
        WorldMapManager.Instance?.UnregisterWorldMapOverlayController(worldMapController);
        WorldMapManager.Instance?.UnregisterHudController(this);
        worldMapController?.Dispose();
        worldMapController = null;
        ClearWorldMapUiElementReferences();
    }

    private void ClearWorldMapUiElementReferences()
    {
        minimapWorldMapButton = null;
        worldMapTravelPromptRoot = null;
        worldMapTravelPromptButton = null;
        worldMapTravelDebugRoot = null;
        worldMapTravelDebugLocationLabel = null;
        worldMapTravelDebugLocalLocationLabel = null;
        worldMapTravelDebugPromptLabel = null;
        worldMapTravelDebugReasonLabel = null;
        worldMapTravelDebugLastTransitionLabel = null;
        worldMapTravelDebugResolutionLabel = null;
        worldMapTravelDebugMovementProbeLabel = null;
        ResetTravelPromptState();
    }

    private Player GetLocalPlayerForWorldMap()
    {
        return TryGetLocalPlayer(out Player localPlayer) ? localPlayer : null;
    }

    private void OnMinimapWorldMapClicked()
    {
        SetTopMenuShieldDropdownVisible(false);
        CloseGuildManagement();
        CloseArubaCauldron();
        CloseSettingsMenu();
        CloseMarket();
        CloseIslandBuilding();
        shipSectionController?.Hide();
        CloseAmmoMenu();
        CloseHarpoonMenu();
        CloseActionItemMenu();
        StopSkillDrag();
        ClearPendingSourcePress();
        ToggleWorldMap();
    }

    private void OnWorldMapTravelPromptClicked()
    {
        if (!TryGetLocalPlayer(out Player localPlayer) ||
            localPlayer == null ||
            !isTravelPromptVisible)
        {
            return;
        }

        localPlayer.RequestMapTransition(currentTravelPromptDirection);
    }

    private void ToggleWorldMap()
    {
        EnsureWorldMapSection();
        worldMapController?.ToggleVisibility();
        RefreshWorldMapButtonState();
    }

    private void CloseWorldMap()
    {
        worldMapController?.Hide();
        RefreshWorldMapButtonState();
    }

    private void RefreshWorldMapButtonState()
    {
        if (minimapWorldMapButton == null)
        {
            return;
        }

        minimapWorldMapButton.EnableInClassList(
            MinimapWorldMapButtonActiveClass,
            worldMapController != null && worldMapController.IsVisible);
    }

    private void RefreshTravelPrompt()
    {
        if (worldMapTravelPromptRoot == null || worldMapTravelPromptButton == null)
        {
            return;
        }

        if (!TryGetLocalPlayer(out Player localPlayer) ||
            localPlayer == null ||
            localPlayer.IsDead ||
            WorldMapManager.Instance == null ||
            !WorldMapManager.Instance.TryGetTravelPrompt(localPlayer, out MapTransitionDirection direction, out string destinationMapId))
        {
            HideTravelPrompt();
            return;
        }

        ShowTravelPrompt(direction, destinationMapId);
    }

    private void ShowTravelPrompt(MapTransitionDirection direction, string destinationMapId)
    {
        string buttonText = string.IsNullOrWhiteSpace(destinationMapId)
            ? string.Empty
            : $"Sail to {destinationMapId}";

        isTravelPromptVisible = !string.IsNullOrWhiteSpace(buttonText);
        currentTravelPromptDirection = direction;
        currentTravelPromptDestinationMapId = destinationMapId ?? string.Empty;

        if (worldMapTravelPromptRoot != null)
        {
            worldMapTravelPromptRoot.style.display = isTravelPromptVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (worldMapTravelPromptButton != null)
        {
            worldMapTravelPromptButton.text = buttonText;
        }
    }

    private void HideTravelPrompt()
    {
        if (worldMapTravelPromptRoot != null)
        {
            worldMapTravelPromptRoot.style.display = DisplayStyle.None;
        }

        if (worldMapTravelPromptButton != null)
        {
            worldMapTravelPromptButton.text = string.Empty;
        }

        ResetTravelPromptState();
    }

    private void ResetTravelPromptState()
    {
        isTravelPromptVisible = false;
        currentTravelPromptDirection = default;
        currentTravelPromptDestinationMapId = string.Empty;
    }

    private void RefreshWorldMapTravelDebug()
    {
        if (worldMapTravelDebugRoot == null)
        {
            return;
        }

        if (!showWorldMapTravelDebug)
        {
            worldMapTravelDebugRoot.style.display = DisplayStyle.None;
            return;
        }

        worldMapTravelDebugRoot.style.display = DisplayStyle.Flex;

        if (worldMapTravelDebugLocationLabel == null ||
            worldMapTravelDebugLocalLocationLabel == null ||
            worldMapTravelDebugPromptLabel == null ||
            worldMapTravelDebugReasonLabel == null ||
            worldMapTravelDebugLastTransitionLabel == null ||
            worldMapTravelDebugResolutionLabel == null ||
            worldMapTravelDebugMovementProbeLabel == null)
        {
            return;
        }

        if (!TryGetLocalPlayer(out Player localPlayer) || localPlayer == null)
        {
            worldMapTravelDebugLocationLabel.text = "Location: no local player";
            worldMapTravelDebugLocalLocationLabel.text = "Map local: --";
            SetWorldMapTravelDebugPrompt(false, "Waiting for Player.LocalPlayer.");
            worldMapTravelDebugLastTransitionLabel.text = "Last travel: --";
            worldMapTravelDebugResolutionLabel.text = "Resolution: --";
            worldMapTravelDebugMovementProbeLabel.text = "Move probe: --";
            return;
        }

        Vector3 worldPosition = localPlayer.transform.position;
        string currentMapId = string.IsNullOrWhiteSpace(localPlayer.CurrentWorldMapId)
            ? "--"
            : localPlayer.CurrentWorldMapId;
        worldMapTravelDebugLocationLabel.text = $"Map: {currentMapId} | World: {FormatWorldMapTravelDebugVector(worldPosition)}";

        WorldMapManager manager = WorldMapManager.Instance;
        if (manager == null)
        {
            worldMapTravelDebugLocalLocationLabel.text = "Map local: --";
            SetWorldMapTravelDebugPrompt(false, "WorldMapManager is missing.");
            RefreshWorldMapTravelDebugTransition(localPlayer);
            return;
        }

        if (!manager.TryGetCurrentScene(localPlayer, out WorldMapSceneAuthoring currentScene) || currentScene == null)
        {
            worldMapTravelDebugLocalLocationLabel.text = "Map local: no registered scene";
            SetWorldMapTravelDebugPrompt(false, $"Current map scene is not loaded or registered for '{currentMapId}'.");
            RefreshWorldMapTravelDebugTransition(localPlayer);
            return;
        }

        Vector3 localPosition = currentScene.WorldToGameplayLocal(worldPosition);
        worldMapTravelDebugLocalLocationLabel.text = $"Map local: {FormatWorldMapTravelDebugVector(localPosition)}";

        if (localPlayer.IsDead)
        {
            SetWorldMapTravelDebugPrompt(false, "Local player is dead.");
            RefreshWorldMapTravelDebugTransition(localPlayer);
            return;
        }

        if (!currentScene.TryGetPromptDirection(worldPosition, out MapTransitionDirection direction))
        {
            SetWorldMapTravelDebugPrompt(false, "Player is outside every map travel zone.");
            RefreshWorldMapTravelDebugTransition(localPlayer);
            return;
        }

        if (!manager.TryGetAdjacentDefinition(localPlayer.CurrentWorldMapId, direction, out WorldMapDefinition destination) ||
            destination == null ||
            string.IsNullOrWhiteSpace(destination.MapId))
        {
            SetWorldMapTravelDebugPrompt(false, $"Player is in the {direction} travel zone, but that edge has no adjacent map.");
            RefreshWorldMapTravelDebugTransition(localPlayer);
            return;
        }

        SetWorldMapTravelDebugPrompt(true, $"Inside {direction} travel zone. Destination: {destination.MapId}.");
        RefreshWorldMapTravelDebugTransition(localPlayer);
    }

    private void SetWorldMapTravelDebugPrompt(bool shouldAppear, string reason)
    {
        string actualState = isTravelPromptVisible
            ? $"visible ({currentTravelPromptDestinationMapId})"
            : "hidden";
        worldMapTravelDebugPromptLabel.text = $"Button should appear: {(shouldAppear ? "YES" : "NO")} | Actual: {actualState}";
        worldMapTravelDebugReasonLabel.text = $"Reason: {reason}";
    }

    private static string FormatWorldMapTravelDebugVector(Vector3 value)
    {
        return $"x {value.x:0.0}, y {value.y:0.0}, z {value.z:0.0}";
    }

    private void RefreshWorldMapTravelDebugTransition(Player localPlayer)
    {
        if (worldMapTravelDebugLastTransitionLabel == null ||
            worldMapTravelDebugResolutionLabel == null ||
            worldMapTravelDebugMovementProbeLabel == null)
        {
            return;
        }

        WorldMapTravelDebugInfo debugInfo = localPlayer != null
            ? localPlayer.LastWorldMapTravelDebugInfo
            : default;
        if (!debugInfo.HasData)
        {
            worldMapTravelDebugLastTransitionLabel.text = "Last travel: none yet";
            worldMapTravelDebugResolutionLabel.text = "Resolution: --";
            worldMapTravelDebugMovementProbeLabel.text = "Move probe: --";
            return;
        }

        worldMapTravelDebugLastTransitionLabel.text =
            $"Last travel: {debugInfo.Trigger} | {debugInfo.Direction} | {debugInfo.SourceMapId} -> {debugInfo.DestinationMapId}";

        string requestedLocal = FormatWorldMapTravelDebugVector(debugInfo.RequestedLocalPosition);
        string finalLocal = FormatWorldMapTravelDebugVector(debugInfo.FinalLocalPosition);
        string resolution = string.IsNullOrWhiteSpace(debugInfo.ResolutionStrategy)
            ? "--"
            : debugInfo.ResolutionStrategy;
        string note = string.IsNullOrWhiteSpace(debugInfo.Note)
            ? string.Empty
            : $" | {debugInfo.Note}";
        worldMapTravelDebugResolutionLabel.text =
            $"Resolution: {resolution} | Requested local: {requestedLocal} | Final local: {finalLocal} | In bounds: {(debugInfo.FinalInBounds ? "YES" : "NO")}{note}";

        string movementTarget = FormatWorldMapTravelDebugVector(debugInfo.MovementProbeTargetLocalPosition);
        string movementNote = string.IsNullOrWhiteSpace(debugInfo.MovementProbeNote)
            ? string.Empty
            : $" | {debugInfo.MovementProbeNote}";
        worldMapTravelDebugMovementProbeLabel.text =
            $"Move probe: {(debugInfo.MovementProbeSucceeded ? "OK" : "FAIL")} | Agent on NavMesh: {(debugInfo.AgentOnNavMeshAfterTeleport ? "YES" : "NO")} | Target local: {movementTarget}{movementNote}";
    }

    private static void TriggerWorldMapDebugTransition(Player localPlayer, MapTransitionDirection direction)
    {
        if (localPlayer == null)
        {
            return;
        }

        if (!localPlayer.DebugForceAdjacentMapTransition(direction))
        {
            Debug.LogWarning($"WorldMap debug input: failed to force adjacent transition {direction}.", localPlayer);
        }
    }
}
