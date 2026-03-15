using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void EnsureAmmoMenu()
    {
        if (root == null || ammoMenuBackdrop != null)
        {
            return;
        }

        ammoMenuBackdrop = new VisualElement { name = "AmmoMenuBackdrop" };
        ammoMenuBackdrop.AddToClassList("ammo-menu-backdrop");
        ammoMenuBackdrop.pickingMode = PickingMode.Position;
        ammoMenuBackdrop.style.display = DisplayStyle.None;

        ammoMenuPanel = new VisualElement { name = "AmmoMenuPanel" };
        ammoMenuPanel.AddToClassList("ammo-menu-panel");
        ammoMenuPanel.pickingMode = PickingMode.Position;
        ammoMenuPanel.style.position = Position.Absolute;

        ammoMenuPanel.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        ammoMenuPanel.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());

        ammoMenuBackdrop.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button == (int)MouseButton.LeftMouse)
            {
                evt.StopPropagation();
            }
        });

        ammoMenuBackdrop.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != (int)MouseButton.LeftMouse)
            {
                return;
            }

            CloseAmmoMenu();
            evt.StopPropagation();
        });

        ammoMenuBackdrop.Add(ammoMenuPanel);
        root.Add(ammoMenuBackdrop);
    }

    private void OpenAmmoMenu(Player localPlayer)
    {
        if (localPlayer == null)
        {
            return;
        }

        EnsureAmmoMenu();
        if (ammoMenuBackdrop == null || ammoMenuPanel == null)
        {
            return;
        }

        ammoMenuPanel.Clear();

        IReadOnlyList<CannonAmmoDefinition> ammoOptions = localPlayer.GetCannonAmmoOptions();
        if (ammoOptions == null || ammoOptions.Count == 0)
        {
            ammoMenuPanel.Add(new Label("No ammo configured") { pickingMode = PickingMode.Ignore });
        }
        else
        {
            int selectedIndex = Mathf.Clamp(localPlayer.SelectedCannonAmmoIndex, 0, ammoOptions.Count - 1);

            for (int i = 0; i < ammoOptions.Count; i++)
            {
                CannonAmmoDefinition ammo = ammoOptions[i];
                if (ammo == null)
                {
                    continue;
                }

                int optionIndex = i;
                Button optionButton = new Button(() =>
                {
                    if (localPlayer.TrySelectCannonAmmo(optionIndex))
                    {
                        RefreshAmmoSkillTooltip(localPlayer);
                    }

                    CloseAmmoMenu();
                })
                {
                    text = $"{ammo.DisplayName} ({ammo.Damage})"
                };

                optionButton.AddToClassList("ammo-menu-button");
                if (optionIndex == selectedIndex)
                {
                    optionButton.AddToClassList("ammo-menu-button-selected");
                }

                ammoMenuPanel.Add(optionButton);
            }
        }

        PositionAmmoMenu(ammoOptions);
        ammoMenuBackdrop.style.display = DisplayStyle.Flex;
        isAmmoMenuOpen = true;
    }

    private void PositionAmmoMenu(IReadOnlyList<CannonAmmoDefinition> ammoOptions)
    {
        if (root == null || ammoMenuPanel == null)
        {
            return;
        }

        Rect rootRect = root.worldBound;
        Rect anchorRect = default;
        if (AmmoSourceSlotIndex >= 0 && AmmoSourceSlotIndex < bottomSlotViews.Length && bottomSlotViews[AmmoSourceSlotIndex]?.Root != null)
        {
            anchorRect = bottomSlotViews[AmmoSourceSlotIndex].Root.worldBound;
        }
        else
        {
            anchorRect = new Rect(rootRect.center.x, rootRect.center.y, 0f, 0f);
        }

        int optionCount = ammoOptions != null ? Mathf.Max(1, ammoOptions.Count) : 1;
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

        ammoMenuPanel.style.left = x;
        ammoMenuPanel.style.top = y;
        ammoMenuPanel.style.minWidth = estimatedWidth;
    }

    private void CloseAmmoMenu()
    {
        if (ammoMenuBackdrop != null)
        {
            ammoMenuBackdrop.style.display = DisplayStyle.None;
        }

        isAmmoMenuOpen = false;
    }

    private void ToggleAmmoMenu()
    {
        if (isAmmoMenuOpen)
        {
            CloseAmmoMenu();
            return;
        }

        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            Debug.LogWarning("GameUIController: Local player not available for ammo selection.");
            return;
        }

        RefreshAmmoSkillTooltip(localPlayer);
        OpenAmmoMenu(localPlayer);
    }

    private void RefreshAmmoSkillTooltip(Player localPlayer)
    {
        if (localPlayer == null)
        {
            return;
        }

        if (AmmoSourceSlotIndex < 0 || AmmoSourceSlotIndex >= bottomSlotViews.Length)
        {
            return;
        }

        SlotView ammoSlot = bottomSlotViews[AmmoSourceSlotIndex];
        if (ammoSlot?.Root == null)
        {
            return;
        }

        IReadOnlyList<CannonAmmoDefinition> ammoOptions = localPlayer.GetCannonAmmoOptions();
        if (ammoOptions == null || ammoOptions.Count == 0)
        {
            ammoSlot.Root.tooltip = "No ammo configured";
            return;
        }

        int selectedIndex = Mathf.Clamp(localPlayer.SelectedCannonAmmoIndex, 0, ammoOptions.Count - 1);
        CannonAmmoDefinition selectedAmmo = ammoOptions[selectedIndex];
        ammoSlot.Root.tooltip = selectedAmmo != null
            ? $"Ammo: {selectedAmmo.DisplayName} ({selectedAmmo.Damage})"
            : "Select ammo";
    }
}
