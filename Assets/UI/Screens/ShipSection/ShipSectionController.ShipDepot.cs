using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class ShipSectionController
{
    private const string ShipDepotCategoryId = "ship-depot";

    private bool IsShipDepotCategorySelected()
    {
        return string.Equals(selectedCategoryId, ShipDepotCategoryId, StringComparison.Ordinal);
    }

    private void RefreshShipDepotItems()
    {
        if (itemList == null)
        {
            return;
        }

        Player localPlayer = Player.LocalPlayer;
        if (localPlayer == null || !localPlayer.IsOwner || !localPlayer.IsSpawned)
        {
            itemList.Add(CreateShipDepotEmptyState("Your ship depot will appear once your captain finishes spawning."));
            return;
        }

        string selectedShipId = NormalizeShipId(localPlayer.SelectedShipId);
        if (string.IsNullOrWhiteSpace(selectedShipId) || !MarketShipCatalogRuntime.TryGetShip(selectedShipId, out MarketShipData selectedShip) || selectedShip == null)
        {
            itemList.Add(CreateShipDepotEmptyState("Select an owned ship to manage its cannon loadout."));
            return;
        }

        int capacity = localPlayer.GetShipCannonCapacity(selectedShipId);
        int equippedCannons = localPlayer.GetShipEquippedCannonTotal(selectedShipId);

        VisualElement depotRoot = new VisualElement();
        depotRoot.AddToClassList("ship-depot-root");
        depotRoot.Add(CreateShipDepotHeader(selectedShip, equippedCannons, capacity));

        VisualElement columns = new VisualElement();
        columns.AddToClassList("ship-depot-columns");
        depotRoot.Add(columns);

        columns.Add(CreateWarehouseColumn(localPlayer));
        columns.Add(CreateEquippedColumn(localPlayer, selectedShipId));
        columns.Add(CreateSummaryColumn(localPlayer, selectedShip, equippedCannons, capacity));

        itemList.Add(depotRoot);
    }

    private VisualElement CreateShipDepotHeader(MarketShipData selectedShip, int equippedCannons, int capacity)
    {
        var header = new VisualElement();
        header.AddToClassList("ship-depot-header");

        var title = new Label($"Ship Depot: {selectedShip.DisplayName}");
        title.AddToClassList("ship-depot-header-title");
        header.Add(title);

        var body = new Label($"Manage warehouse cannons and equip up to {Mathf.Max(0, capacity):N0} cannons on your current ship.");
        body.AddToClassList("ship-depot-header-copy");
        header.Add(body);

        var status = new Label($"Equipped {Mathf.Max(0, equippedCannons):N0} / {Mathf.Max(0, capacity):N0}");
        status.AddToClassList("ship-depot-header-status");
        header.Add(status);

        return header;
    }

    private VisualElement CreateWarehouseColumn(Player localPlayer)
    {
        var column = CreateDepotColumn("Warehouse", "Available cannons waiting to be mounted.");
        VisualElement body = column.Q<VisualElement>("ShipDepotColumnBody");
        if (body == null)
        {
            return column;
        }

        IReadOnlyList<MarketCannonData> cannons = MarketCannonCatalogRuntime.GetCannons();
        var hasRows = false;
        bool canEquipMore = localPlayer.GetCurrentShipEquippedCannonTotal() < localPlayer.GetCurrentShipCannonCapacity();
        for (int index = 0; index < cannons.Count; index++)
        {
            MarketCannonData cannon = cannons[index];
            if (cannon == null)
            {
                continue;
            }

            int availableAmount = localPlayer.GetAvailableWarehouseCannonAmount(cannon.Id);
            if (availableAmount <= 0)
            {
                continue;
            }

            hasRows = true;
            body.Add(CreateDepotCannonCard(
                cannon,
                availableAmount,
                "Equip",
                "ship-depot-card-button-primary",
                () => localPlayer.RequestEquipCannonToSelectedShip(cannon.Id),
                canEquipMore));
        }

        if (!hasRows)
        {
            body.Add(CreateShipDepotEmptyState("No cannons are currently waiting in the warehouse."));
        }

        return column;
    }

    private VisualElement CreateEquippedColumn(Player localPlayer, string selectedShipId)
    {
        var column = CreateDepotColumn("Equipped", "Cannons mounted on the selected hull.");
        VisualElement body = column.Q<VisualElement>("ShipDepotColumnBody");
        if (body == null)
        {
            return column;
        }

        IReadOnlyList<MarketCannonData> cannons = MarketCannonCatalogRuntime.GetCannons();
        var hasRows = false;
        for (int index = 0; index < cannons.Count; index++)
        {
            MarketCannonData cannon = cannons[index];
            if (cannon == null)
            {
                continue;
            }

            int equippedAmount = localPlayer.GetShipEquippedCannonAmount(selectedShipId, cannon.Id);
            if (equippedAmount <= 0)
            {
                continue;
            }

            hasRows = true;
            body.Add(CreateDepotCannonCard(
                cannon,
                equippedAmount,
                "Unequip",
                "ship-depot-card-button-secondary",
                () => localPlayer.RequestUnequipCannonFromSelectedShip(cannon.Id)));
        }

        if (!hasRows)
        {
            body.Add(CreateShipDepotEmptyState("No cannons are mounted on this ship yet."));
        }

        return column;
    }

    private VisualElement CreateSummaryColumn(Player localPlayer, MarketShipData selectedShip, int equippedCannons, int capacity)
    {
        var column = CreateDepotColumn("Equipped Values", "The current loadout, ship capacity, and average cannon stats.");
        column.AddToClassList("ship-depot-column-summary");

        VisualElement body = column.Q<VisualElement>("ShipDepotColumnBody");
        if (body == null)
        {
            return column;
        }

        string averageReload = equippedCannons > 0
            ? $"{localPlayer.GetCurrentShipAverageCannonReloadTime():0.#}s"
            : "-";
        string averageRange = equippedCannons > 0
            ? $"{localPlayer.GetCurrentShipAverageCannonRange():0.#}"
            : "-";
        string averageHitProbability = equippedCannons > 0
            ? $"{localPlayer.GetCurrentShipAverageCannonHitProbability():0.#}%"
            : "-";

        body.Add(CreateSummaryRow("Current ship", selectedShip.DisplayName));
        body.Add(CreateSummaryRow("Cannon capacity", $"{Mathf.Max(0, capacity):N0}"));
        body.Add(CreateSummaryRow("Equipped cannons", $"{Mathf.Max(0, equippedCannons):N0} / {Mathf.Max(0, capacity):N0}"));
        body.Add(CreateSummaryRow("Avg reload time", averageReload));
        body.Add(CreateSummaryRow("Avg range", averageRange));
        body.Add(CreateSummaryRow("Avg hit probability", averageHitProbability));
        body.Add(CreateSummaryRow("Harpoons / attack", "1"));
        body.Add(CreateSummaryRow("Selected cannon ammo", $"{Mathf.Max(0, localPlayer.GetSelectedCannonAmmoAmount()):N0}"));
        body.Add(CreateSummaryRow("Selected harpoons", $"{Mathf.Max(0, localPlayer.GetSelectedHarpoonAmmoAmount()):N0}"));

        return column;
    }

    private VisualElement CreateDepotColumn(string title, string subtitle)
    {
        var column = new VisualElement();
        column.AddToClassList("ship-depot-column");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("ship-depot-column-title");
        column.Add(titleLabel);

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var subtitleLabel = new Label(subtitle);
            subtitleLabel.AddToClassList("ship-depot-column-subtitle");
            column.Add(subtitleLabel);
        }

        var body = new VisualElement { name = "ShipDepotColumnBody" };
        body.AddToClassList("ship-depot-column-body");
        column.Add(body);

        return column;
    }

    private VisualElement CreateDepotCannonCard(
        MarketCannonData cannon,
        int amount,
        string actionLabel,
        string actionButtonClass,
        Func<bool> action,
        bool isEnabled = true)
    {
        var card = new VisualElement();
        card.AddToClassList("ship-depot-card");

        var header = new VisualElement();
        header.AddToClassList("ship-depot-card-header");
        card.Add(header);

        var title = new Label(cannon.DisplayName);
        title.AddToClassList("ship-depot-card-title");
        header.Add(title);

        var count = new Label($"{Mathf.Max(0, amount):N0}");
        count.AddToClassList("ship-depot-card-count");
        header.Add(count);

        var description = new Label(cannon.Description);
        description.AddToClassList("ship-depot-card-description");
        card.Add(description);

        var stats = new VisualElement();
        stats.AddToClassList("ship-depot-card-stats");
        stats.Add(CreateCardStat($"Hit {cannon.HitProbability}%"));
        stats.Add(CreateCardStat($"Range {cannon.CannonRange:0.#}"));
        stats.Add(CreateCardStat($"Reload {cannon.ReloadTimeSeconds:0.#}s"));
        if (cannon.CriticalHitProbability > 0f)
        {
            stats.Add(CreateCardStat($"Crit {cannon.CriticalHitProbability:0.#}%"));
        }

        if (cannon.CriticalHitDamage > 0f)
        {
            stats.Add(CreateCardStat($"Crit Dmg +{cannon.CriticalHitDamage:0.#}%"));
        }

        if (cannon.BonusDamageFlat > 0)
        {
            stats.Add(CreateCardStat($"+{cannon.BonusDamageFlat} Dmg"));
        }

        if (cannon.BonusDamagePercentage > 0f)
        {
            stats.Add(CreateCardStat($"+{cannon.BonusDamagePercentage:0.#}% Dmg"));
        }

        card.Add(stats);

        var actions = new VisualElement();
        actions.AddToClassList("ship-depot-card-actions");
        card.Add(actions);

        var actionButton = new Button(() =>
        {
            action?.Invoke();
        })
        {
            text = actionLabel
        };
        actionButton.AddToClassList("ship-depot-card-button");
        actionButton.AddToClassList(actionButtonClass);
        actionButton.SetEnabled(isEnabled);
        actions.Add(actionButton);

        return card;
    }

    private VisualElement CreateCardStat(string text)
    {
        var label = new Label(text);
        label.AddToClassList("ship-depot-card-stat");
        return label;
    }

    private VisualElement CreateSummaryRow(string label, string value)
    {
        var row = new VisualElement();
        row.AddToClassList("ship-depot-summary-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("ship-depot-summary-label");
        row.Add(labelElement);

        var valueElement = new Label(value);
        valueElement.AddToClassList("ship-depot-summary-value");
        row.Add(valueElement);

        return row;
    }

    private VisualElement CreateShipDepotEmptyState(string message)
    {
        var emptyState = new Label(message);
        emptyState.AddToClassList("ship-depot-empty");
        return emptyState;
    }
}
