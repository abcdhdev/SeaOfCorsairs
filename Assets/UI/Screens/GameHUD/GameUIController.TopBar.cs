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
    }

    private void OnTopMenuChatClicked()
    {
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
        shipSectionController?.Hide();
        ToggleMarket();
    }

    private void OnTopMenuShipClicked()
    {
        CloseMarket();
        shipSectionController?.ToggleVisibility();
    }

    private void OnTopMenuLogoutClicked()
    {
        CloseMarket();
        NetworkManager.Singleton?.Shutdown();
    }
}
