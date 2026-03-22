using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void UpdateFpsAndPing()
    {
        if (topMenuFpsPingLabel == null)
        {
            return;
        }

        fpsDeltaTime += (Time.unscaledDeltaTime - fpsDeltaTime) * 0.1f;
        fpsPingTimer += Time.unscaledDeltaTime;

        if (fpsPingTimer < 0.25f)
        {
            return;
        }

        fpsPingTimer = 0f;
        float fps = 1.0f / fpsDeltaTime;

        ulong rtt = 0;
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsClient &&
            NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport transport)
        {
            rtt = transport.GetCurrentRtt(NetworkManager.ServerClientId);
        }

        topMenuFpsPingLabel.text = $"FPS: {Mathf.CeilToInt(fps)}\nPing: {rtt}ms";
    }

    private Player GetLocalPlayerForMarket()
    {
        return TryGetLocalPlayer(out Player localPlayer) ? localPlayer : null;
    }

    private void ToggleMarket()
    {
        marketController?.ToggleVisibility();
    }

    private void CloseMarket()
    {
        marketController?.Hide();
    }

    private void ToggleIslandBuildingMode()
    {
        IslandBuildManager buildManager = IslandBuildManager.Instance;
        if (buildManager == null)
        {
            return;
        }

        if (buildManager.IsEditModeActive)
        {
            buildManager.ExitEditMode("Defense edit mode closed.");
        }
        else
        {
            buildManager.EnterEditMode();
        }
    }

    private void CloseIslandBuilding()
    {
        IslandBuildManager.Instance?.ExitEditMode();
    }

    private void ShowGuildManagement()
    {
        EnsureGuildManagementSection();
        guildManagementController?.Show();
    }

    private void CloseGuildManagement()
    {
        guildManagementController?.Hide();
    }

    private void ToggleTopMenuShieldDropdown()
    {
        SetTopMenuShieldDropdownVisible(!isTopMenuShieldDropdownOpen);
    }

    private void SetTopMenuShieldDropdownVisible(bool isVisible)
    {
        isTopMenuShieldDropdownOpen = isVisible && topMenuShieldDropdown != null;

        if (topMenuShieldDropdown != null)
        {
            topMenuShieldDropdown.style.display = isTopMenuShieldDropdownOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void DismissShieldDropdownIfClickedAway(Vector2 panelPosition)
    {
        if (!isTopMenuShieldDropdownOpen)
        {
            return;
        }

        if (IsPanelPositionWithinShieldMenu(panelPosition))
        {
            return;
        }

        SetTopMenuShieldDropdownVisible(false);
    }

    private bool IsPanelPositionWithinShieldMenu(Vector2 panelPosition)
    {
        if (root?.panel == null)
        {
            return false;
        }

        VisualElement picked = root.panel.Pick(panelPosition);
        while (picked != null)
        {
            if (picked == topMenuShieldAnchor ||
                picked == topMenuShieldButton ||
                picked == topMenuShieldDropdown ||
                picked == topMenuIslandBuildingButton ||
                picked == topMenuGuildsButton)
            {
                return true;
            }

            picked = picked.parent;
        }

        return false;
    }

    private void UpdateTopMenuButtonStates()
    {
        if (topMenuBagButton != null)
        {
            bool marketVisible = marketController != null && marketController.IsVisible;
            topMenuBagButton.EnableInClassList("top-menu-slot-button-active", marketVisible);
        }

        if (topMenuShipButton != null)
        {
            bool shipPanelVisible = shipSectionController != null && shipSectionController.IsVisible;
            topMenuShipButton.EnableInClassList("top-menu-slot-button-active", shipPanelVisible);
        }

        if (topMenuShieldButton != null)
        {
            bool islandEditActive = IslandBuildManager.Instance != null && IslandBuildManager.Instance.IsEditModeActive;
            bool guildVisible = guildManagementController != null && guildManagementController.IsVisible;
            topMenuShieldButton.EnableInClassList(
                "top-menu-slot-button-active",
                islandEditActive || guildVisible || isTopMenuShieldDropdownOpen);
        }

        if (topMenuShieldDropdown != null)
        {
            topMenuShieldDropdown.style.display = isTopMenuShieldDropdownOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void OnTopMenuChatClicked()
    {
        SetTopMenuShieldDropdownVisible(false);

        if (root == null)
        {
            return;
        }

        Button chatToggle = root.Q<Button>("ChatToggleButton");
        if (chatToggle == null)
        {
            return;
        }

        using (ClickEvent clickEvent = ClickEvent.GetPooled())
        {
            clickEvent.target = chatToggle;
            chatToggle.SendEvent(clickEvent);
        }
    }

    private void OnTopMenuBagClicked()
    {
        SetTopMenuShieldDropdownVisible(false);
        CloseGuildManagement();
        CloseIslandBuilding();
        shipSectionController?.Hide();
        ToggleMarket();
    }

    private void OnTopMenuShipClicked()
    {
        SetTopMenuShieldDropdownVisible(false);
        CloseGuildManagement();
        CloseMarket();
        CloseIslandBuilding();
        shipSectionController?.ToggleVisibility();
    }

    private void OnTopMenuLogoutClicked()
    {
        SetTopMenuShieldDropdownVisible(false);
        CloseGuildManagement();
        CloseMarket();
        CloseIslandBuilding();
        NetworkManager.Singleton?.Shutdown();
    }

    private void OnTopMenuShieldClicked()
    {
        CloseMarket();
        shipSectionController?.Hide();
        CloseAmmoMenu();
        CloseActionItemMenu();
        StopSkillDrag();
        ClearPendingSourcePress();
        ToggleTopMenuShieldDropdown();
    }

    private void OnTopMenuIslandBuildingClicked()
    {
        SetTopMenuShieldDropdownVisible(false);
        CloseGuildManagement();
        CloseMarket();
        shipSectionController?.Hide();
        CloseAmmoMenu();
        CloseActionItemMenu();
        StopSkillDrag();
        ClearPendingSourcePress();
        ToggleIslandBuildingMode();
    }

    private void OnTopMenuGuildsClicked()
    {
        SetTopMenuShieldDropdownVisible(false);
        CloseMarket();
        shipSectionController?.Hide();
        CloseAmmoMenu();
        CloseActionItemMenu();
        StopSkillDrag();
        ClearPendingSourcePress();
        CloseIslandBuilding();
        ShowGuildManagement();
    }
}
