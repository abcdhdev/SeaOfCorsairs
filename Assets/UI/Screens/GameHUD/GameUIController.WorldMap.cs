using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private const string MinimapWorldMapButtonActiveClass = "minimap-world-map-button-active";

    private Button minimapWorldMapButton;
    private VisualElement worldMapTravelPromptRoot;
    private Button worldMapTravelPromptButton;
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
}
