using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void EnsureActionItemMenu()
    {
        if (root == null || actionItemMenuBackdrop != null)
        {
            return;
        }

        actionItemMenuBackdrop = new VisualElement { name = "ActionItemMenuBackdrop" };
        actionItemMenuBackdrop.AddToClassList("ammo-menu-backdrop");
        actionItemMenuBackdrop.pickingMode = PickingMode.Position;
        actionItemMenuBackdrop.style.display = DisplayStyle.None;

        actionItemMenuPanel = new VisualElement { name = "ActionItemMenuPanel" };
        actionItemMenuPanel.AddToClassList("ammo-menu-panel");
        actionItemMenuPanel.pickingMode = PickingMode.Position;
        actionItemMenuPanel.style.position = Position.Absolute;

        actionItemMenuPanel.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        actionItemMenuPanel.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());

        actionItemMenuBackdrop.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == (int)MouseButton.LeftMouse)
            {
                evt.StopPropagation();
            }
        });

        actionItemMenuBackdrop.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != (int)MouseButton.LeftMouse)
            {
                return;
            }

            CloseActionItemMenu();
            evt.StopPropagation();
        });

        actionItemMenuBackdrop.Add(actionItemMenuPanel);
        root.Add(actionItemMenuBackdrop);
    }

    private void OpenActionItemMenu(Player localPlayer)
    {
        if (localPlayer == null)
        {
            return;
        }

        EnsureActionItemMenu();
        if (actionItemMenuBackdrop == null || actionItemMenuPanel == null)
        {
            return;
        }

        actionItemMenuPanel.Clear();

        AddActionItemOption(localPlayer, PlayerActionItemType.BlackGunpowder, "Black Gunpowder");
        AddActionItemOption(localPlayer, PlayerActionItemType.AgwesArmorPlating, "Agwe's Armor Plating");

        PositionActionItemMenu();
        actionItemMenuBackdrop.style.display = DisplayStyle.Flex;
        isActionItemMenuOpen = true;
        renderedActionItemMenuMask = (int)localPlayer.ActiveActionItems;
    }

    private void AddActionItemOption(Player localPlayer, PlayerActionItemType actionItemType, string label)
    {
        Button optionButton = new Button(() =>
        {
            if (localPlayer.TryToggleActionItem(actionItemType))
            {
                RefreshActionItemSkillTooltip(localPlayer);
            }

            CloseActionItemMenu();
        })
        {
            text = string.Empty
        };

        optionButton.AddToClassList("ammo-menu-button");
        optionButton.AddToClassList("action-item-menu-button");
        if (localPlayer.HasActionItem(actionItemType))
        {
            optionButton.AddToClassList("ammo-menu-button-selected");
        }

        VisualElement content = new VisualElement();
        content.pickingMode = PickingMode.Ignore;
        content.AddToClassList("action-item-menu-button-content");

        VisualElement iconElement = new VisualElement();
        iconElement.pickingMode = PickingMode.Ignore;
        iconElement.AddToClassList("action-item-menu-icon");
        Texture2D iconTexture = ActionItemIconCatalog.GetHudIcon(actionItemType);
        if (iconTexture != null)
        {
            iconElement.style.backgroundImage = new StyleBackground(iconTexture);
        }

        Label labelElement = new Label(label);
        labelElement.pickingMode = PickingMode.Ignore;
        labelElement.AddToClassList("action-item-menu-label");

        content.Add(iconElement);
        content.Add(labelElement);
        optionButton.Add(content);
        actionItemMenuPanel.Add(optionButton);
    }

    private void PositionActionItemMenu()
    {
        if (root == null || actionItemMenuPanel == null)
        {
            return;
        }

        Rect rootRect = root.worldBound;
        Rect anchorRect = default;
        if (ActionItemSourceSlotIndex >= 0 &&
            ActionItemSourceSlotIndex < bottomSlotViews.Length &&
            bottomSlotViews[ActionItemSourceSlotIndex]?.Root != null)
        {
            anchorRect = bottomSlotViews[ActionItemSourceSlotIndex].Root.worldBound;
        }
        else
        {
            anchorRect = new Rect(rootRect.center.x, rootRect.center.y, 0f, 0f);
        }

        const float estimatedHeight = 176f;
        const float estimatedWidth = 320f;

        float x = anchorRect.xMin;
        float y = anchorRect.yMin - estimatedHeight - 10f;
        if (y < rootRect.yMin + 16f)
        {
            y = anchorRect.yMax + 10f;
        }

        float minX = rootRect.xMin + 16f;
        float maxX = Mathf.Max(minX, rootRect.xMax - estimatedWidth - 16f);
        x = Mathf.Clamp(x, minX, maxX);

        float minY = rootRect.yMin + 16f;
        float maxY = Mathf.Max(minY, rootRect.yMax - estimatedHeight - 16f);
        y = Mathf.Clamp(y, minY, maxY);

        actionItemMenuPanel.style.left = x;
        actionItemMenuPanel.style.top = y;
        actionItemMenuPanel.style.minWidth = estimatedWidth;
    }

    private void CloseActionItemMenu()
    {
        if (actionItemMenuBackdrop != null)
        {
            actionItemMenuBackdrop.style.display = DisplayStyle.None;
        }

        isActionItemMenuOpen = false;
        renderedActionItemMenuMask = -1;
    }

    private void ToggleActionItemMenu()
    {
        if (isActionItemMenuOpen)
        {
            CloseActionItemMenu();
            return;
        }

        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            Debug.LogWarning("GameUIController: Local player not available for action item selection.");
            return;
        }

        CloseAmmoMenu();
        CloseHarpoonMenu();
        RefreshActionItemSkillTooltip(localPlayer);
        OpenActionItemMenu(localPlayer);
    }

    private void RefreshActionItemSkillTooltip(Player localPlayer)
    {
        if (localPlayer == null)
        {
            return;
        }

        if (ActionItemSourceSlotIndex < 0 || ActionItemSourceSlotIndex >= bottomSlotViews.Length)
        {
            return;
        }

        SlotView actionItemSlot = bottomSlotViews[ActionItemSourceSlotIndex];
        if (actionItemSlot?.Root == null)
        {
            return;
        }

        bool hasBlackGunpowder = localPlayer.HasActionItem(PlayerActionItemType.BlackGunpowder);
        bool hasArmorPlating = localPlayer.HasActionItem(PlayerActionItemType.AgwesArmorPlating);

        actionItemSlot.Root.tooltip = (hasBlackGunpowder, hasArmorPlating) switch
        {
            (true, true) => "Action Items: Black Gunpowder (+10% attack damage), Agwe's Armor Plating (-10% received attack damage)",
            (true, false) => "Action Item: Black Gunpowder (+10% attack damage)",
            (false, true) => "Action Item: Agwe's Armor Plating (-10% received attack damage)",
            _ => "Action Items"
        };
    }

    private void RefreshActiveActionItemHud(Player localPlayer)
    {
        if (activeActionItemHudRoot == null ||
            activeActionItemHudBlackGunpowder == null ||
            activeActionItemHudAgwesArmorPlating == null)
        {
            return;
        }

        int currentMask = localPlayer != null ? (int)localPlayer.ActiveActionItems : 0;
        if (displayedActionItemMask == currentMask)
        {
            return;
        }

        displayedActionItemMask = currentMask;
        bool hasBlackGunpowder = localPlayer != null && localPlayer.HasActionItem(PlayerActionItemType.BlackGunpowder);
        bool hasArmorPlating = localPlayer != null && localPlayer.HasActionItem(PlayerActionItemType.AgwesArmorPlating);

        if (currentMask == 0)
        {
            activeActionItemHudRoot.style.display = DisplayStyle.None;
            activeActionItemHudRoot.tooltip = null;
            activeActionItemHudBlackGunpowder.style.display = DisplayStyle.None;
            activeActionItemHudAgwesArmorPlating.style.display = DisplayStyle.None;
            return;
        }

        activeActionItemHudBlackGunpowder.style.display = hasBlackGunpowder ? DisplayStyle.Flex : DisplayStyle.None;
        activeActionItemHudAgwesArmorPlating.style.display = hasArmorPlating ? DisplayStyle.Flex : DisplayStyle.None;
        activeActionItemHudRoot.style.display = DisplayStyle.Flex;
        activeActionItemHudRoot.tooltip = (hasBlackGunpowder, hasArmorPlating) switch
        {
            (true, true) => "Black Gunpowder, Agwe's Armor Plating",
            (true, false) => "Black Gunpowder",
            (false, true) => "Agwe's Armor Plating",
            _ => null
        };
    }
}
