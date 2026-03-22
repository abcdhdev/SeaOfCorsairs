using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void RegisterBlockingUiElements()
    {
        if (actionBarToggleButton != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(actionBarToggleButton);
        }

        if (actionBarBody != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(actionBarBody);
        }

        if (healthBox != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(healthBox);
        }

        if (ammoMenuBackdrop != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(ammoMenuBackdrop);
        }

        if (actionItemMenuBackdrop != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(actionItemMenuBackdrop);
        }

        if (marketController != null && marketController.OverlayRoot != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(marketController.OverlayRoot);
        }

        if (guildManagementController != null && guildManagementController.OverlayRoot != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(guildManagementController.OverlayRoot);
        }

        if (topMenuShieldDropdown != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(topMenuShieldDropdown);
        }

        if (topMenuBar != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(topMenuBar);
        }

        if (deadOverlayRoot != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(deadOverlayRoot);
        }

        if (cameraZoomRoot != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(cameraZoomRoot);
        }

        if (islandEditRoot != null)
        {
            UIToolkitRaycastChecker.RegisterBlockingElement(islandEditRoot);
        }
    }

    private void UnregisterBlockingUiElements()
    {
        if (actionBarToggleButton != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(actionBarToggleButton);
        }

        if (actionBarBody != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(actionBarBody);
        }

        if (healthBox != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(healthBox);
        }

        if (ammoMenuBackdrop != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(ammoMenuBackdrop);
        }

        if (actionItemMenuBackdrop != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(actionItemMenuBackdrop);
        }

        if (marketController != null && marketController.OverlayRoot != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(marketController.OverlayRoot);
        }

        if (guildManagementController != null && guildManagementController.OverlayRoot != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(guildManagementController.OverlayRoot);
        }

        if (topMenuShieldDropdown != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(topMenuShieldDropdown);
        }

        if (topMenuBar != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(topMenuBar);
        }

        if (deadOverlayRoot != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(deadOverlayRoot);
        }

        if (cameraZoomRoot != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(cameraZoomRoot);
        }

        if (islandEditRoot != null)
        {
            UIToolkitRaycastChecker.UnregisterBlockingElement(islandEditRoot);
        }
    }

    private void RegisterUiCallbacks()
    {
        if (actionBarToggleButton != null)
        {
            actionBarToggleButton.clicked += OnActionBarToggleClicked;
        }

        if (centerCameraButton != null)
        {
            centerCameraButton.clicked += OnCenterCameraClicked;
        }

        if (cameraZoomSlider != null)
        {
            cameraZoomSlider.RegisterValueChangedCallback(OnCameraZoomChanged);
            cameraZoomSlider.RegisterCallback<GeometryChangedEvent>(OnCameraZoomGeometryChanged);
        }

        if (cameraZoomRoot != null)
        {
            cameraZoomRoot.RegisterCallback<GeometryChangedEvent>(OnCameraZoomGeometryChanged);
        }

        if (topMenuChatButton != null)
        {
            topMenuChatButton.clicked += OnTopMenuChatClicked;
        }

        if (topMenuBagButton != null)
        {
            topMenuBagButton.clicked += OnTopMenuBagClicked;
        }

        if (topMenuShieldButton != null)
        {
            topMenuShieldButton.clicked += OnTopMenuShieldClicked;
        }

        if (topMenuIslandBuildingButton != null)
        {
            topMenuIslandBuildingButton.clicked += OnTopMenuIslandBuildingClicked;
        }

        if (topMenuGuildsButton != null)
        {
            topMenuGuildsButton.clicked += OnTopMenuGuildsClicked;
        }

        if (topMenuShipButton != null)
        {
            topMenuShipButton.clicked += OnTopMenuShipClicked;
        }

        if (topMenuLogoutButton != null)
        {
            topMenuLogoutButton.clicked += OnTopMenuLogoutClicked;
        }

        if (root != null)
        {
            root.RegisterCallback<PointerMoveEvent>(OnRootPointerMove);
            root.RegisterCallback<PointerUpEvent>(OnRootPointerUp);
            root.RegisterCallback<PointerCancelEvent>(OnRootPointerCancel);
        }

        RegisterIslandEditCallbacks();
    }

    private void UnregisterUiCallbacks()
    {
        if (actionBarToggleButton != null)
        {
            actionBarToggleButton.clicked -= OnActionBarToggleClicked;
        }

        if (centerCameraButton != null)
        {
            centerCameraButton.clicked -= OnCenterCameraClicked;
        }

        if (cameraZoomSlider != null)
        {
            cameraZoomSlider.UnregisterValueChangedCallback(OnCameraZoomChanged);
            cameraZoomSlider.UnregisterCallback<GeometryChangedEvent>(OnCameraZoomGeometryChanged);
        }

        if (cameraZoomRoot != null)
        {
            cameraZoomRoot.UnregisterCallback<GeometryChangedEvent>(OnCameraZoomGeometryChanged);
        }

        if (topMenuChatButton != null)
        {
            topMenuChatButton.clicked -= OnTopMenuChatClicked;
        }

        if (topMenuBagButton != null)
        {
            topMenuBagButton.clicked -= OnTopMenuBagClicked;
        }

        if (topMenuShieldButton != null)
        {
            topMenuShieldButton.clicked -= OnTopMenuShieldClicked;
        }

        if (topMenuIslandBuildingButton != null)
        {
            topMenuIslandBuildingButton.clicked -= OnTopMenuIslandBuildingClicked;
        }

        if (topMenuGuildsButton != null)
        {
            topMenuGuildsButton.clicked -= OnTopMenuGuildsClicked;
        }

        if (topMenuShipButton != null)
        {
            topMenuShipButton.clicked -= OnTopMenuShipClicked;
        }

        if (topMenuLogoutButton != null)
        {
            topMenuLogoutButton.clicked -= OnTopMenuLogoutClicked;
        }

        if (root != null)
        {
            root.UnregisterCallback<PointerMoveEvent>(OnRootPointerMove);
            root.UnregisterCallback<PointerUpEvent>(OnRootPointerUp);
            root.UnregisterCallback<PointerCancelEvent>(OnRootPointerCancel);
        }

        UnregisterIslandEditCallbacks();
    }
}
