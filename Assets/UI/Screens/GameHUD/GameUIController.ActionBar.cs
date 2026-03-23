using System;
using UnityEngine;
using UnityEngine.UIElements;

public partial class GameUIController
{
    private void BuildSourceSkills()
    {
        sourceSkills.Clear();
        sourceSkills.Add(CreateSkill("shoot-start", "Shoot", 1, OnAttackClicked));
        sourceSkills.Add(CreateSkill("shoot-stop", "Stop", 2, OnStopAttackClicked));
        sourceSkills.Add(CreateSkill("board", "Board", 3, OnBoardClicked));
        sourceSkills.Add(CreateSkill("ammo", "Cannonball", 4, OnSelectAmmoClicked));
        sourceSkills.Add(CreateSkill("harpoon", "Harpoon", 5, OnSelectHarpoonClicked));
        sourceSkills.Add(CreateSkill("empty-6", string.Empty, 6, null));
        sourceSkills.Add(CreateSkill("action-item", "Action Items", 7, OnSelectActionItemClicked));
        sourceSkills.Add(CreateSkill("empty-8", string.Empty, 8, null));
        sourceSkills.Add(CreateSkill("empty-9", string.Empty, 9, null));
        sourceSkills.Add(CreateSkill("repair-toggle", "Repair", 10, OnRepairClicked));
    }

    private void BuildActionBarSlots()
    {
        if (topActionSlotsRow == null || bottomSkillSlotsRow == null)
        {
            return;
        }

        topActionSlotsRow.Clear();
        bottomSkillSlotsRow.Clear();
        topSlotLookup.Clear();
        Array.Clear(topSlotViews, 0, topSlotViews.Length);
        Array.Clear(bottomSlotViews, 0, bottomSlotViews.Length);

        for (int i = 0; i < SlotCount; i++)
        {
            int slotIndex = i;
            SlotView topSlot = CreateSlotView(SlotHotkeys[i], EmptySlotText, null, true, i == SlotCount - 1);
            topSlot.Root.RegisterCallback<PointerUpEvent>(evt => OnTopSlotPointerUp(slotIndex, evt));
            topActionSlotsRow.Add(topSlot.Root);
            topSlotViews[i] = topSlot;
            topSlotLookup[topSlot.Root] = i;
            RefreshTopSlotVisual(i);
        }

        for (int i = 0; i < SlotCount && i < sourceSkills.Count; i++)
        {
            int sourceIndex = i;
            SkillDefinition skill = sourceSkills[i];
            SlotView sourceSlot = CreateSlotView(string.Empty, skill.DisplayName, skill.IconClass, false, i == SlotCount - 1);
            sourceSlot.Root.tooltip = string.IsNullOrEmpty(skill.DisplayName) ? null : skill.DisplayName;

            if (string.IsNullOrEmpty(skill.DisplayName) && skill.Execute == null)
            {
                sourceSlot.Root.AddToClassList(DisabledSlotClass);
                sourceSlot.Root.pickingMode = PickingMode.Ignore;
            }
            else
            {
                sourceSlot.Root.RemoveFromClassList(DisabledSlotClass);
                sourceSlot.Root.pickingMode = PickingMode.Position;
                sourceSlot.Root.RegisterCallback<PointerDownEvent>(evt => OnSourceSlotPointerDown(sourceIndex, evt));
            }

            bottomSkillSlotsRow.Add(sourceSlot.Root);
            bottomSlotViews[sourceIndex] = sourceSlot;
        }
    }

    private void BuildActionBarPreviewSlots()
    {
        if (topActionSlotsRow == null || bottomSkillSlotsRow == null)
        {
            return;
        }

        topActionSlotsRow.Clear();
        bottomSkillSlotsRow.Clear();
        topSlotLookup.Clear();
        Array.Clear(topSlotViews, 0, topSlotViews.Length);
        Array.Clear(bottomSlotViews, 0, bottomSlotViews.Length);
        Array.Clear(topSlotAssignments, 0, topSlotAssignments.Length);

        for (int i = 0; i < SlotCount; i++)
        {
            SlotView topSlot = CreateSlotView(SlotHotkeys[i], EmptySlotText, null, true, i == SlotCount - 1);
            topSlot.Root.AddToClassList(TopSlotEmptyClass);
            topActionSlotsRow.Add(topSlot.Root);
            topSlotViews[i] = topSlot;
        }

        for (int i = 0; i < SlotCount && i < sourceSkills.Count; i++)
        {
            SkillDefinition skill = sourceSkills[i];
            SlotView sourceSlot = CreateSlotView(string.Empty, skill.DisplayName, skill.IconClass, false, i == SlotCount - 1);
            sourceSlot.Root.tooltip = string.IsNullOrEmpty(skill.DisplayName) ? null : skill.DisplayName;

            if (string.IsNullOrEmpty(skill.DisplayName) && skill.Execute == null)
            {
                sourceSlot.Root.AddToClassList(DisabledSlotClass);
            }
            else
            {
                sourceSlot.Root.RemoveFromClassList(DisabledSlotClass);
            }

            bottomSkillSlotsRow.Add(sourceSlot.Root);
            bottomSlotViews[i] = sourceSlot;
        }
    }

    private SlotView CreateSlotView(string topText, string mainText, string iconClass, bool isTopSlot, bool isLast)
    {
        VisualElement slotRoot = CreateSlotRootFromTemplate();
        slotRoot.pickingMode = PickingMode.Position;
        slotRoot.AddToClassList("action-slot");
        slotRoot.AddToClassList(isTopSlot ? "top-action-slot" : "skill-source-slot");

        if (isLast)
        {
            slotRoot.AddToClassList("action-slot-last");
        }
        else
        {
            slotRoot.RemoveFromClassList("action-slot-last");
        }

        VisualElement iconElement = slotRoot.Q<VisualElement>(ActionSlotIconName);
        if (iconElement == null)
        {
            iconElement = new VisualElement { name = ActionSlotIconName };
            slotRoot.Insert(0, iconElement);
        }

        iconElement.pickingMode = PickingMode.Ignore;
        iconElement.AddToClassList("action-slot-icon");

        Label topLabel = slotRoot.Q<Label>(ActionSlotTopLabelName);
        if (topLabel == null)
        {
            topLabel = new Label { name = ActionSlotTopLabelName };
            slotRoot.Add(topLabel);
        }

        topLabel.pickingMode = PickingMode.Ignore;
        topLabel.AddToClassList("action-slot-label-top");
        topLabel.text = topText;

        Label mainLabel = slotRoot.Q<Label>(ActionSlotMainLabelName);
        if (mainLabel == null)
        {
            mainLabel = new Label { name = ActionSlotMainLabelName };
            slotRoot.Add(mainLabel);
        }

        mainLabel.pickingMode = PickingMode.Ignore;
        mainLabel.AddToClassList("action-slot-label-main");

        Label amountLabel = slotRoot.Q<Label>(ActionSlotAmountLabelName);
        if (amountLabel == null)
        {
            amountLabel = new Label { name = ActionSlotAmountLabelName };
            slotRoot.Add(amountLabel);
        }

        amountLabel.pickingMode = PickingMode.Ignore;
        amountLabel.AddToClassList("action-slot-label-amount");
        amountLabel.style.display = DisplayStyle.None;

        SlotView slotView = new SlotView
        {
            Root = slotRoot,
            MainLabel = mainLabel,
            AmountLabel = amountLabel,
            Icon = iconElement
        };

        ApplySlotMainContent(slotView, mainText, iconClass);
        return slotView;
    }

    private VisualElement CreateSlotRootFromTemplate()
    {
        if (actionSlotTemplate == null)
        {
            return new VisualElement { name = ActionSlotRootName };
        }

        TemplateContainer templateContainer = actionSlotTemplate.Instantiate();
        VisualElement templateRoot = templateContainer.Q<VisualElement>(ActionSlotRootName);
        if (templateRoot != null)
        {
            templateRoot.RemoveFromHierarchy();
            return templateRoot;
        }

        return templateContainer;
    }

    private static SkillDefinition CreateSkill(string id, string displayName, int iconIndex, Action executeAction)
    {
        return new SkillDefinition
        {
            Id = id,
            DisplayName = displayName,
            IconClass = iconIndex > 0 ? $"action-slot-icon-{iconIndex}" : null,
            Execute = executeAction
        };
    }

    private static void ApplySlotMainContent(SlotView slotView, string mainText, string iconClass)
    {
        if (slotView == null)
        {
            return;
        }

        bool hasIcon = !string.IsNullOrEmpty(iconClass);

        if (slotView.MainLabel != null)
        {
            slotView.MainLabel.text = mainText;
            slotView.MainLabel.style.display = hasIcon ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (slotView.Icon == null)
        {
            return;
        }

        if (!string.Equals(slotView.AppliedIconClass, iconClass, StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(slotView.AppliedIconClass))
            {
                slotView.Icon.RemoveFromClassList(slotView.AppliedIconClass);
            }

            slotView.AppliedIconClass = iconClass;
            if (hasIcon)
            {
                slotView.Icon.AddToClassList(iconClass);
            }
        }

        slotView.Icon.style.display = hasIcon ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnSourceSlotPointerDown(int sourceIndex, PointerDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
        {
            return;
        }

        if (sourceIndex < 0 || sourceIndex >= sourceSkills.Count)
        {
            return;
        }

        BeginSourceSlotPress(sourceSkills[sourceIndex], evt.position, evt.pointerId);
        evt.StopPropagation();
    }

    private void BeginSourceSlotPress(SkillDefinition skill, Vector2 pointerPosition, int pointerId)
    {
        ClearPendingSourcePress();

        if (root == null || skill == null)
        {
            return;
        }

        isSourceSlotPressPending = true;
        pendingSourcePointerId = pointerId;
        pendingSourcePointerStart = pointerPosition;
        pendingSourceSkill = skill;

        root.CapturePointer(pointerId);
    }

    private void ClearPendingSourcePress(bool releasePointerCapture = true)
    {
        if (releasePointerCapture && root != null && pendingSourcePointerId >= 0 && root.HasPointerCapture(pendingSourcePointerId))
        {
            root.ReleasePointer(pendingSourcePointerId);
        }

        isSourceSlotPressPending = false;
        pendingSourcePointerId = -1;
        pendingSourcePointerStart = default;
        pendingSourceSkill = null;
    }

    private void StartSkillDrag(SkillDefinition skill, Vector2 pointerPosition, int pointerId)
    {
        StopSkillDrag();

        if (skill == null || root == null)
        {
            return;
        }

        isDraggingSkill = true;
        draggingSkill = skill;
        draggingPointerId = pointerId;

        dragGhostLabel = new Label(skill.DisplayName);
        dragGhostLabel.AddToClassList("drag-ghost");
        dragGhostLabel.pickingMode = PickingMode.Ignore;
        root.Add(dragGhostLabel);

        root.CapturePointer(pointerId);
        UpdateDragGhostPosition(pointerPosition);
        UpdateDropTarget(pointerPosition);
    }

    private void OnRootPointerMove(PointerMoveEvent evt)
    {
        if (isSourceSlotPressPending && !isDraggingSkill && evt.pointerId == pendingSourcePointerId)
        {
            Vector2 delta = (Vector2)evt.position - pendingSourcePointerStart;
            if (delta.sqrMagnitude >= SourceSlotDragThreshold * SourceSlotDragThreshold && pendingSourceSkill != null)
            {
                SkillDefinition skillToDrag = pendingSourceSkill;
                ClearPendingSourcePress(releasePointerCapture: false);
                StartSkillDrag(skillToDrag, evt.position, evt.pointerId);
                evt.StopPropagation();
                return;
            }
        }

        if (!isDraggingSkill || evt.pointerId != draggingPointerId)
        {
            return;
        }

        UpdateDragGhostPosition(evt.position);
        UpdateDropTarget(evt.position);
        evt.StopPropagation();
    }

    private void OnRootPointerUp(PointerUpEvent evt)
    {
        DismissShieldDropdownIfClickedAway(evt.position);

        if (isSourceSlotPressPending && !isDraggingSkill && evt.pointerId == pendingSourcePointerId)
        {
            SkillDefinition clickedSkill = pendingSourceSkill;
            ClearPendingSourcePress();
            clickedSkill?.Execute?.Invoke();
            evt.StopPropagation();
            return;
        }

        if (!isDraggingSkill || evt.pointerId != draggingPointerId)
        {
            return;
        }

        int dropSlotIndex = ResolveTopSlotIndex(evt.position);
        if (dropSlotIndex >= 0 && draggingSkill != null)
        {
            topSlotAssignments[dropSlotIndex] = draggingSkill;
            RefreshTopSlotVisual(dropSlotIndex);
        }

        StopSkillDrag();
        evt.StopPropagation();
    }

    private void OnRootPointerCancel(PointerCancelEvent evt)
    {
        if (isSourceSlotPressPending && !isDraggingSkill && evt.pointerId == pendingSourcePointerId)
        {
            ClearPendingSourcePress();
            evt.StopPropagation();
            return;
        }

        if (!isDraggingSkill || evt.pointerId != draggingPointerId)
        {
            return;
        }

        StopSkillDrag();
        evt.StopPropagation();
    }

    private void UpdateDragGhostPosition(Vector2 pointerPosition)
    {
        if (dragGhostLabel == null)
        {
            return;
        }

        dragGhostLabel.style.left = pointerPosition.x + 10f;
        dragGhostLabel.style.top = pointerPosition.y - 16f;
    }

    private void UpdateDropTarget(Vector2 pointerPosition)
    {
        int candidateIndex = ResolveTopSlotIndex(pointerPosition);
        if (candidateIndex == currentDropSlotIndex)
        {
            return;
        }

        if (currentDropSlotIndex >= 0 && currentDropSlotIndex < topSlotViews.Length && topSlotViews[currentDropSlotIndex] != null)
        {
            topSlotViews[currentDropSlotIndex].Root.RemoveFromClassList(DropTargetClass);
        }

        currentDropSlotIndex = candidateIndex;

        if (currentDropSlotIndex >= 0 && currentDropSlotIndex < topSlotViews.Length && topSlotViews[currentDropSlotIndex] != null)
        {
            topSlotViews[currentDropSlotIndex].Root.AddToClassList(DropTargetClass);
        }
    }

    private int ResolveTopSlotIndex(Vector2 panelPosition)
    {
        if (!isActionBarOpen || root == null || root.panel == null)
        {
            return -1;
        }

        VisualElement picked = root.panel.Pick(panelPosition);
        while (picked != null)
        {
            if (topSlotLookup.TryGetValue(picked, out int slotIndex))
            {
                return slotIndex;
            }

            picked = picked.parent;
        }

        return -1;
    }

    private void StopSkillDrag()
    {
        if (currentDropSlotIndex >= 0 && currentDropSlotIndex < topSlotViews.Length && topSlotViews[currentDropSlotIndex] != null)
        {
            topSlotViews[currentDropSlotIndex].Root.RemoveFromClassList(DropTargetClass);
        }

        currentDropSlotIndex = -1;

        if (root != null && draggingPointerId >= 0 && root.HasPointerCapture(draggingPointerId))
        {
            root.ReleasePointer(draggingPointerId);
        }

        if (dragGhostLabel?.parent != null)
        {
            dragGhostLabel.parent.Remove(dragGhostLabel);
        }

        dragGhostLabel = null;
        isDraggingSkill = false;
        draggingPointerId = -1;
        draggingSkill = null;
    }

    private void OnTopSlotPointerUp(int slotIndex, PointerUpEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
        {
            return;
        }

        OnTopSlotClicked(slotIndex);
        evt.StopPropagation();
    }

    private void OnTopSlotClicked(int slotIndex)
    {
        if (isDraggingSkill)
        {
            return;
        }

        if (slotIndex < 0 || slotIndex >= topSlotAssignments.Length)
        {
            return;
        }

        SkillDefinition assignedSkill = topSlotAssignments[slotIndex];
        if (assignedSkill == null)
        {
            Debug.Log($"GameUIController: Slot {slotIndex + 1} has no assigned skill.");
            return;
        }

        assignedSkill.Execute?.Invoke();
    }

    private void RefreshTopSlotVisual(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= topSlotViews.Length)
        {
            return;
        }

        SlotView slotView = topSlotViews[slotIndex];
        if (slotView == null)
        {
            return;
        }

        SkillDefinition assignedSkill = topSlotAssignments[slotIndex];
        if (assignedSkill == null)
        {
            ApplySlotMainContent(slotView, EmptySlotText, null);
            slotView.Root.tooltip = null;
            slotView.Root.AddToClassList(TopSlotEmptyClass);
            slotView.Root.RemoveFromClassList(TopSlotFilledClass);
            return;
        }

        ApplySlotMainContent(slotView, assignedSkill.DisplayName, assignedSkill.IconClass);
        slotView.Root.tooltip = assignedSkill.DisplayName;
        slotView.Root.RemoveFromClassList(TopSlotEmptyClass);
        slotView.Root.AddToClassList(TopSlotFilledClass);
    }

    private void RefreshActionBarAmountBadges(Player localPlayer)
    {
        RefreshSlotAmountBadge(GetSlotView(bottomSlotViews, AmmoSourceSlotIndex), localPlayer != null, localPlayer != null ? localPlayer.GetSelectedCannonAmmoAmount() : 0);
        RefreshSlotAmountBadge(GetSlotView(bottomSlotViews, HarpoonSourceSlotIndex), localPlayer != null, localPlayer != null ? localPlayer.GetSelectedHarpoonAmmoAmount() : 0);
        RefreshSlotAmountBadge(GetSlotView(bottomSlotViews, ActionItemSourceSlotIndex), localPlayer != null, localPlayer != null ? localPlayer.GetActionItemBadgeAmount() : 0);

        for (int index = 0; index < topSlotAssignments.Length; index++)
        {
            SkillDefinition assignedSkill = topSlotAssignments[index];
            SlotView slotView = GetSlotView(topSlotViews, index);
            if (assignedSkill == null || slotView == null)
            {
                RefreshSlotAmountBadge(slotView, false, 0);
                continue;
            }

            int amount = assignedSkill.Id switch
            {
                "ammo" => localPlayer != null ? localPlayer.GetSelectedCannonAmmoAmount() : 0,
                "harpoon" => localPlayer != null ? localPlayer.GetSelectedHarpoonAmmoAmount() : 0,
                "action-item" => localPlayer != null ? localPlayer.GetActionItemBadgeAmount() : 0,
                _ => 0
            };

            bool shouldDisplay = localPlayer != null &&
                                 (string.Equals(assignedSkill.Id, "ammo", StringComparison.Ordinal) ||
                                  string.Equals(assignedSkill.Id, "harpoon", StringComparison.Ordinal) ||
                                  string.Equals(assignedSkill.Id, "action-item", StringComparison.Ordinal));
            RefreshSlotAmountBadge(slotView, shouldDisplay, amount);
        }
    }

    private static SlotView GetSlotView(SlotView[] slots, int index)
    {
        if (slots == null || index < 0 || index >= slots.Length)
        {
            return null;
        }

        return slots[index];
    }

    private static void RefreshSlotAmountBadge(SlotView slotView, bool shouldDisplay, int amount)
    {
        if (slotView?.AmountLabel == null)
        {
            return;
        }

        if (!shouldDisplay)
        {
            slotView.AmountLabel.text = string.Empty;
            slotView.AmountLabel.style.display = DisplayStyle.None;
            return;
        }

        slotView.AmountLabel.text = Mathf.Max(0, amount).ToString("N0");
        slotView.AmountLabel.style.display = DisplayStyle.Flex;
    }

    private void OnActionBarToggleClicked()
    {
        isActionBarOpen = !isActionBarOpen;
        if (!isActionBarOpen)
        {
            StopSkillDrag();
            ClearPendingSourcePress();
            CloseAmmoMenu();
            CloseHarpoonMenu();
            CloseActionItemMenu();
        }
        else
        {
            CloseAmmoMenu();
            CloseHarpoonMenu();
            CloseActionItemMenu();
        }

        RefreshActionBarVisibility();
    }

    private void RefreshActionBarVisibility()
    {
        bool islandEditActive = IslandBuildManager.Instance != null && IslandBuildManager.Instance.IsEditModeActive;

        if (actionBarBody != null)
        {
            if (isActionBarOpen && !islandEditActive)
            {
                actionBarBody.RemoveFromClassList(ActionBarClosedClass);
            }
            else
            {
                actionBarBody.AddToClassList(ActionBarClosedClass);
            }
        }

        if (actionBarToggleButton != null)
        {
            actionBarToggleButton.style.display = islandEditActive ? DisplayStyle.None : DisplayStyle.Flex;
            actionBarToggleButton.text = isActionBarOpen ? "<" : ">";

            if (isActionBarOpen)
            {
                actionBarToggleButton.RemoveFromClassList(ToggleClosedClass);
            }
            else
            {
                actionBarToggleButton.AddToClassList(ToggleClosedClass);
            }
        }
    }

    private void OnAttackClicked()
    {
        if (!TryGetLocalPlayer(out Player player) || player == null)
        {
            Debug.LogWarning("GameUIController: Local player not found.");
            return;
        }

        if (player.IsDead)
        {
            return;
        }

        GameObject selectedTarget = GetSelectedTarget();
        if (selectedTarget == null)
        {
            Debug.LogWarning("GameUIController: Select a target before attacking.");
            return;
        }

        player.StartAttack(selectedTarget);
    }

    private void OnStopAttackClicked()
    {
        if (TryGetLocalPlayer(out Player player) && player != null)
        {
            player.StopAttack();
        }
    }

    private void OnBoardClicked()
    {
        NPC selectedNpc = GetSelectedNpc();
        if (selectedNpc == null)
        {
            Debug.LogWarning("GameUIController: Select an NPC before boarding.");
            return;
        }

        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            Debug.LogWarning("GameUIController: Local player not available for boarding.");
            return;
        }

        localPlayer.RequestBoardNpc(selectedNpc);
    }

    private void OnSelectAmmoClicked()
    {
        ToggleAmmoMenu();
    }

    private void OnSelectHarpoonClicked()
    {
        ToggleHarpoonMenu();
    }

    private void OnRepairClicked()
    {
        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            Debug.LogWarning("GameUIController: Local player not available for repair.");
            return;
        }

        localPlayer.ToggleRepairing();
    }

    private void OnSelectActionItemClicked()
    {
        ToggleActionItemMenu();
    }

    private static void LogSkillNotImplemented(string skillName)
    {
        Debug.Log($"GameUIController: '{skillName}' skill is not implemented yet.");
    }
}
