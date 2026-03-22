using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void EnsureHarpoonMenu()
    {
        if (root == null || harpoonMenuBackdrop != null)
        {
            return;
        }

        harpoonMenuBackdrop = new VisualElement { name = "HarpoonMenuBackdrop" };
        harpoonMenuBackdrop.AddToClassList("ammo-menu-backdrop");
        harpoonMenuBackdrop.pickingMode = PickingMode.Position;
        harpoonMenuBackdrop.style.display = DisplayStyle.None;

        harpoonMenuPanel = new VisualElement { name = "HarpoonMenuPanel" };
        harpoonMenuPanel.AddToClassList("ammo-menu-panel");
        harpoonMenuPanel.pickingMode = PickingMode.Position;
        harpoonMenuPanel.style.position = Position.Absolute;

        harpoonMenuPanel.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        harpoonMenuPanel.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());

        harpoonMenuBackdrop.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == (int)MouseButton.LeftMouse)
            {
                evt.StopPropagation();
            }
        });

        harpoonMenuBackdrop.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != (int)MouseButton.LeftMouse)
            {
                return;
            }

            CloseHarpoonMenu();
            evt.StopPropagation();
        });

        harpoonMenuBackdrop.Add(harpoonMenuPanel);
        root.Add(harpoonMenuBackdrop);
    }

    private void OpenHarpoonMenu(Player localPlayer)
    {
        if (localPlayer == null)
        {
            return;
        }

        EnsureHarpoonMenu();
        if (harpoonMenuBackdrop == null || harpoonMenuPanel == null)
        {
            return;
        }

        harpoonMenuPanel.Clear();

        IReadOnlyList<HarpoonAmmoDefinition> harpoonOptions = localPlayer.GetHarpoonAmmoOptions();
        if (harpoonOptions == null || harpoonOptions.Count == 0)
        {
            harpoonMenuPanel.Add(new Label("No harpoons configured") { pickingMode = PickingMode.Ignore });
        }
        else
        {
            int selectedIndex = Mathf.Clamp(localPlayer.SelectedHarpoonAmmoIndex, 0, harpoonOptions.Count - 1);

            for (int i = 0; i < harpoonOptions.Count; i++)
            {
                HarpoonAmmoDefinition harpoon = harpoonOptions[i];
                if (harpoon == null)
                {
                    continue;
                }

                int optionIndex = i;
                Button optionButton = new Button(() =>
                {
                    if (localPlayer.TrySelectHarpoonAmmo(optionIndex))
                    {
                        RefreshHarpoonSkillTooltip(localPlayer);
                    }

                    CloseHarpoonMenu();
                })
                {
                    text = $"{harpoon.DisplayName} ({harpoon.Damage})"
                };

                optionButton.AddToClassList("ammo-menu-button");
                if (optionIndex == selectedIndex)
                {
                    optionButton.AddToClassList("ammo-menu-button-selected");
                }

                harpoonMenuPanel.Add(optionButton);
            }
        }

        PositionHarpoonMenu(harpoonOptions);
        harpoonMenuBackdrop.style.display = DisplayStyle.Flex;
        isHarpoonMenuOpen = true;
    }

    private void PositionHarpoonMenu(IReadOnlyList<HarpoonAmmoDefinition> harpoonOptions)
    {
        if (root == null || harpoonMenuPanel == null)
        {
            return;
        }

        Rect rootRect = root.worldBound;
        Rect anchorRect = default;
        if (HarpoonSourceSlotIndex >= 0 && HarpoonSourceSlotIndex < bottomSlotViews.Length && bottomSlotViews[HarpoonSourceSlotIndex]?.Root != null)
        {
            anchorRect = bottomSlotViews[HarpoonSourceSlotIndex].Root.worldBound;
        }
        else
        {
            anchorRect = new Rect(rootRect.center.x, rootRect.center.y, 0f, 0f);
        }

        int optionCount = harpoonOptions != null ? Mathf.Max(1, harpoonOptions.Count) : 1;
        float estimatedHeight = 24f + (optionCount * 46f);
        float estimatedWidth = 280f;

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

        harpoonMenuPanel.style.left = x;
        harpoonMenuPanel.style.top = y;
        harpoonMenuPanel.style.minWidth = estimatedWidth;
    }

    private void CloseHarpoonMenu()
    {
        if (harpoonMenuBackdrop != null)
        {
            harpoonMenuBackdrop.style.display = DisplayStyle.None;
        }

        isHarpoonMenuOpen = false;
    }

    private void ToggleHarpoonMenu()
    {
        if (isHarpoonMenuOpen)
        {
            CloseHarpoonMenu();
            return;
        }

        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            Debug.LogWarning("GameUIController: Local player not available for harpoon selection.");
            return;
        }

        CloseAmmoMenu();
        CloseActionItemMenu();
        RefreshHarpoonSkillTooltip(localPlayer);
        OpenHarpoonMenu(localPlayer);
    }

    private void RefreshHarpoonSkillTooltip(Player localPlayer)
    {
        if (localPlayer == null)
        {
            return;
        }

        if (HarpoonSourceSlotIndex < 0 || HarpoonSourceSlotIndex >= bottomSlotViews.Length)
        {
            return;
        }

        SlotView harpoonSlot = bottomSlotViews[HarpoonSourceSlotIndex];
        if (harpoonSlot?.Root == null)
        {
            return;
        }

        IReadOnlyList<HarpoonAmmoDefinition> harpoonOptions = localPlayer.GetHarpoonAmmoOptions();
        if (harpoonOptions == null || harpoonOptions.Count == 0)
        {
            harpoonSlot.Root.tooltip = "No harpoons configured";
            return;
        }

        int selectedIndex = Mathf.Clamp(localPlayer.SelectedHarpoonAmmoIndex, 0, harpoonOptions.Count - 1);
        HarpoonAmmoDefinition selectedHarpoon = harpoonOptions[selectedIndex];
        harpoonSlot.Root.tooltip = selectedHarpoon != null
            ? $"Harpoon: {selectedHarpoon.DisplayName} ({selectedHarpoon.Damage})"
            : "Select harpoon";
    }
}
