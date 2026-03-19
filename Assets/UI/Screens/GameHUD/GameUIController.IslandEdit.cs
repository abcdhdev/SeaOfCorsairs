using UnityEngine.UIElements;

public partial class GameUIController
{
    private VisualElement islandEditRoot;
    private VisualElement islandBuildCatalog;
    private Label islandEditSelectedNameLabel;
    private Label islandEditSelectedHealthLabel;
    private Label islandEditSelectedOwnerLabel;
    private Label islandEditSelectedStatusLabel;
    private Label islandEditGoldLabel;
    private Label islandEditCapacityLabel;
    private Label islandEditModeLabel;
    private Label islandEditStatusLabel;
    private Button islandEditBuildButton;
    private Button islandEditSelectButton;
    private Button islandEditMoveButton;
    private Button islandEditRepairButton;
    private Button islandEditUpgradeButton;
    private Button islandEditDestroyButton;
    private Button islandEditBuildCannonButton;
    private Button islandEditConfirmDestroyButton;
    private Button islandEditCancelButton;
    private Button islandEditExitButton;

    private void BindIslandEditElements()
    {
        if (root == null)
        {
            return;
        }

        islandEditRoot = root.Q<VisualElement>("IslandEditRoot");
        islandBuildCatalog = root.Q<VisualElement>("IslandBuildCatalog");
        islandEditSelectedNameLabel = root.Q<Label>("IslandEditSelectedNameLabel");
        islandEditSelectedHealthLabel = root.Q<Label>("IslandEditSelectedHealthLabel");
        islandEditSelectedOwnerLabel = root.Q<Label>("IslandEditSelectedOwnerLabel");
        islandEditSelectedStatusLabel = root.Q<Label>("IslandEditSelectedStatusLabel");
        islandEditGoldLabel = root.Q<Label>("IslandEditGoldLabel");
        islandEditCapacityLabel = root.Q<Label>("IslandEditCapacityLabel");
        islandEditModeLabel = root.Q<Label>("IslandEditModeLabel");
        islandEditStatusLabel = root.Q<Label>("IslandEditStatusLabel");
        islandEditBuildButton = root.Q<Button>("IslandEditBuildButton");
        islandEditSelectButton = root.Q<Button>("IslandEditSelectButton");
        islandEditMoveButton = root.Q<Button>("IslandEditMoveButton");
        islandEditRepairButton = root.Q<Button>("IslandEditRepairButton");
        islandEditUpgradeButton = root.Q<Button>("IslandEditUpgradeButton");
        islandEditDestroyButton = root.Q<Button>("IslandEditDestroyButton");
        islandEditBuildCannonButton = root.Q<Button>("IslandEditBuildCannonButton");
        islandEditConfirmDestroyButton = root.Q<Button>("IslandEditConfirmDestroyButton");
        islandEditCancelButton = root.Q<Button>("IslandEditCancelButton");
        islandEditExitButton = root.Q<Button>("IslandEditExitButton");

        if (islandEditRepairButton != null)
        {
            islandEditRepairButton.tooltip = "Repair mode is not implemented yet.";
        }

        if (islandEditUpgradeButton != null)
        {
            islandEditUpgradeButton.tooltip = "Upgrade mode is not implemented yet.";
        }
    }

    private void ClearIslandEditReferences()
    {
        islandEditRoot = null;
        islandBuildCatalog = null;
        islandEditSelectedNameLabel = null;
        islandEditSelectedHealthLabel = null;
        islandEditSelectedOwnerLabel = null;
        islandEditSelectedStatusLabel = null;
        islandEditGoldLabel = null;
        islandEditCapacityLabel = null;
        islandEditModeLabel = null;
        islandEditStatusLabel = null;
        islandEditBuildButton = null;
        islandEditSelectButton = null;
        islandEditMoveButton = null;
        islandEditRepairButton = null;
        islandEditUpgradeButton = null;
        islandEditDestroyButton = null;
        islandEditBuildCannonButton = null;
        islandEditConfirmDestroyButton = null;
        islandEditCancelButton = null;
        islandEditExitButton = null;
    }

    private void RegisterIslandEditCallbacks()
    {
        if (islandEditBuildButton != null)
        {
            islandEditBuildButton.clicked += OnIslandEditBuildClicked;
        }

        if (islandEditSelectButton != null)
        {
            islandEditSelectButton.clicked += OnIslandEditSelectClicked;
        }

        if (islandEditMoveButton != null)
        {
            islandEditMoveButton.clicked += OnIslandEditMoveClicked;
        }

        if (islandEditDestroyButton != null)
        {
            islandEditDestroyButton.clicked += OnIslandEditDestroyClicked;
        }

        if (islandEditBuildCannonButton != null)
        {
            islandEditBuildCannonButton.clicked += OnIslandEditBuildCannonClicked;
        }

        if (islandEditConfirmDestroyButton != null)
        {
            islandEditConfirmDestroyButton.clicked += OnIslandEditConfirmDestroyClicked;
        }

        if (islandEditCancelButton != null)
        {
            islandEditCancelButton.clicked += OnIslandEditCancelClicked;
        }

        if (islandEditExitButton != null)
        {
            islandEditExitButton.clicked += OnIslandEditExitClicked;
        }
    }

    private void UnregisterIslandEditCallbacks()
    {
        if (islandEditBuildButton != null)
        {
            islandEditBuildButton.clicked -= OnIslandEditBuildClicked;
        }

        if (islandEditSelectButton != null)
        {
            islandEditSelectButton.clicked -= OnIslandEditSelectClicked;
        }

        if (islandEditMoveButton != null)
        {
            islandEditMoveButton.clicked -= OnIslandEditMoveClicked;
        }

        if (islandEditDestroyButton != null)
        {
            islandEditDestroyButton.clicked -= OnIslandEditDestroyClicked;
        }

        if (islandEditBuildCannonButton != null)
        {
            islandEditBuildCannonButton.clicked -= OnIslandEditBuildCannonClicked;
        }

        if (islandEditConfirmDestroyButton != null)
        {
            islandEditConfirmDestroyButton.clicked -= OnIslandEditConfirmDestroyClicked;
        }

        if (islandEditCancelButton != null)
        {
            islandEditCancelButton.clicked -= OnIslandEditCancelClicked;
        }

        if (islandEditExitButton != null)
        {
            islandEditExitButton.clicked -= OnIslandEditExitClicked;
        }
    }

    private void RefreshIslandEditUi()
    {
        if (islandEditRoot == null)
        {
            return;
        }

        IslandBuildManager buildManager = IslandBuildManager.Instance;
        bool isEditModeActive = buildManager != null && buildManager.IsEditModeActive;
        islandEditRoot.style.display = isEditModeActive ? DisplayStyle.Flex : DisplayStyle.None;

        if (!isEditModeActive || buildManager == null)
        {
            return;
        }

        Player localPlayer = GetLocalPlayerForMarket();
        IslandTurret selectedTurret = buildManager.GetSelectedTurret();
        IslandTurret selectedOwnedTurret = buildManager.GetSelectedOwnedTurret();

        if (islandEditSelectedNameLabel != null)
        {
            islandEditSelectedNameLabel.text = selectedTurret != null ? selectedTurret.name : "No turret selected";
        }

        if (islandEditSelectedHealthLabel != null)
        {
            islandEditSelectedHealthLabel.text = selectedTurret != null
                ? $"HP: {selectedTurret.CurrentHealth:N0} / {selectedTurret.MaxHealth:N0}"
                : "HP: -";
        }

        if (islandEditSelectedOwnerLabel != null)
        {
            islandEditSelectedOwnerLabel.text = selectedTurret == null
                ? "Owner: -"
                : selectedOwnedTurret != null
                    ? "Owner: You"
                    : "Owner: Another crew";
        }

        if (islandEditSelectedStatusLabel != null)
        {
            islandEditSelectedStatusLabel.text = selectedTurret == null
                ? "Status: Waiting for selection"
                : $"Status: {BuildTurretStatusLabel(selectedTurret, buildManager)}";
        }

        if (islandEditGoldLabel != null)
        {
            islandEditGoldLabel.text = localPlayer != null ? $"Gold: {localPlayer.Gold:N0}" : "Gold: 0";
        }

        if (islandEditCapacityLabel != null)
        {
            islandEditCapacityLabel.text = $"Capacity: {buildManager.GetLocalOwnedTurretCount()} / {buildManager.MaxTurrets}";
        }

        if (islandEditModeLabel != null)
        {
            islandEditModeLabel.text = $"Mode: {BuildEditModeLabel(buildManager.EditState)}";
        }

        if (islandEditStatusLabel != null)
        {
            islandEditStatusLabel.text = buildManager.StatusMessage;
        }

        if (islandBuildCatalog != null)
        {
            islandBuildCatalog.style.display = buildManager.IsBuildCatalogVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (islandEditBuildButton != null)
        {
            islandEditBuildButton.SetEnabled(!buildManager.IsPlacementActive);
        }

        if (islandEditSelectButton != null)
        {
            islandEditSelectButton.SetEnabled(buildManager.EditState != IslandEditState.Selecting || selectedTurret != null);
        }

        if (islandEditMoveButton != null)
        {
            islandEditMoveButton.SetEnabled(selectedOwnedTurret != null && !buildManager.IsPlacementActive);
        }

        if (islandEditRepairButton != null)
        {
            islandEditRepairButton.SetEnabled(false);
        }

        if (islandEditUpgradeButton != null)
        {
            islandEditUpgradeButton.SetEnabled(false);
        }

        if (islandEditDestroyButton != null)
        {
            islandEditDestroyButton.SetEnabled(selectedOwnedTurret != null && !buildManager.IsPlacementActive);
        }

        if (islandEditBuildCannonButton != null)
        {
            islandEditBuildCannonButton.text = $"Cannon Tower  {buildManager.TurretCost}g";
            islandEditBuildCannonButton.SetEnabled(!buildManager.IsPlacementActive);
        }

        if (islandEditConfirmDestroyButton != null)
        {
            islandEditConfirmDestroyButton.style.display = buildManager.EditState == IslandEditState.DestroyConfirm
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            islandEditConfirmDestroyButton.SetEnabled(selectedOwnedTurret != null);
        }

        if (islandEditCancelButton != null)
        {
            islandEditCancelButton.style.display = buildManager.EditState == IslandEditState.Selecting
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }
    }

    private void OnIslandEditBuildClicked()
    {
        IslandBuildManager.Instance?.OpenBuildCatalog();
        RefreshIslandEditUi();
    }

    private void OnIslandEditSelectClicked()
    {
        IslandBuildManager.Instance?.ReturnToSelectionMode("Selection mode active.");
        RefreshIslandEditUi();
    }

    private void OnIslandEditMoveClicked()
    {
        IslandBuildManager buildManager = IslandBuildManager.Instance;
        if (buildManager != null)
        {
            buildManager.BeginMovePlacement(buildManager.GetSelectedOwnedTurret());
        }

        RefreshIslandEditUi();
    }

    private void OnIslandEditDestroyClicked()
    {
        IslandBuildManager.Instance?.BeginDestroyConfirmation();
        RefreshIslandEditUi();
    }

    private void OnIslandEditBuildCannonClicked()
    {
        IslandBuildManager.Instance?.BeginBuildPlacement();
        RefreshIslandEditUi();
    }

    private void OnIslandEditConfirmDestroyClicked()
    {
        IslandBuildManager.Instance?.DeleteSelectedTurret();
        RefreshIslandEditUi();
    }

    private void OnIslandEditCancelClicked()
    {
        IslandBuildManager.Instance?.CancelCurrentAction("Action canceled.");
        RefreshIslandEditUi();
    }

    private void OnIslandEditExitClicked()
    {
        IslandBuildManager.Instance?.ExitEditMode("Defense edit mode closed.");
        RefreshIslandEditUi();
    }

    private static string BuildEditModeLabel(IslandEditState editState)
    {
        return editState switch
        {
            IslandEditState.BuildChooseType => "Build",
            IslandEditState.BuildPlacing => "Placing",
            IslandEditState.Moving => "Moving",
            IslandEditState.DestroyConfirm => "Demolish",
            _ => "Select"
        };
    }

    private static string BuildTurretStatusLabel(IslandTurret selectedTurret, IslandBuildManager buildManager)
    {
        if (selectedTurret == null)
        {
            return "Waiting";
        }

        if (buildManager != null && buildManager.EditState == IslandEditState.DestroyConfirm)
        {
            return "Awaiting demolish confirmation";
        }

        if (selectedTurret.CurrentHealth < selectedTurret.MaxHealth)
        {
            return "Damaged";
        }

        return "Ready";
    }
}
