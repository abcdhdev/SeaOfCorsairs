using System;
using System.Collections.Generic;
using UnityEngine;

public static class MarketInventoryCatalogRuntime
{
    public const string AmmoCategoryId = "ammo";
    public const string HarpoonsCategoryId = "harpoons";
    public const string ActionItemsCategoryId = "action-items";

    private static readonly MarketInventoryItemData[] AmmoItems =
    {
        CreateAmmoItem("standard", "Standard Cannonballs", "Reliable iron shot for everyday broadsides.", 20, 1000, 1000),
        CreateAmmoItem("heavy", "Heavy Cannonballs", "Heavier rounds for captains who want a harder hit.", 30, 2000, 1000),
        CreateAmmoItem("chain", "Chain Cannonballs", "Lighter chain shot for specialized volleys.", 15, 1500, 1000)
    };

    private static readonly MarketInventoryItemData[] HarpoonItems =
    {
        CreateHarpoonItem("harpoon-25", "Harpoon 25", "Standard hunting harpoons for monster engagements.", 25, 750, 50),
        CreateHarpoonItem("harpoon-50", "Harpoon 50", "Sharper barbs for tougher monsters.", 50, 1500, 50),
        CreateHarpoonItem("harpoon-250", "Harpoon 250", "A heavy-duty harpoon reserved for brutal strikes.", 250, 7500, 50)
    };

    private static readonly MarketInventoryItemData[] ActionItems =
    {
        CreateActionItem(
            PlayerInventoryState.BlackGunpowderItemId,
            "Black Gunpowder",
            "Improves outgoing attack damage while active.",
            "Attack +10%",
            "Bundle 100",
            "Consumes 1 / attack",
            1000,
            100,
            () => ActionItemIconCatalog.GetHudIcon(PlayerActionItemType.BlackGunpowder)),
        CreateActionItem(
            PlayerInventoryState.AgwesArmorPlatingItemId,
            "Agwe's Armor Plating",
            "Reduces incoming attack damage while active.",
            "Incoming -10%",
            "Bundle 100",
            "Consumes 1 / hit",
            1000,
            100,
            () => ActionItemIconCatalog.GetHudIcon(PlayerActionItemType.AgwesArmorPlating))
    };

    private static readonly Dictionary<string, MarketInventoryItemData> Lookup = BuildLookup();

    public static IReadOnlyList<MarketInventoryItemData> GetItemsForCategory(string categoryId)
    {
        string normalizedCategoryId = NormalizeCategoryId(categoryId);
        return normalizedCategoryId switch
        {
            AmmoCategoryId => AmmoItems,
            HarpoonsCategoryId => HarpoonItems,
            ActionItemsCategoryId => ActionItems,
            _ => Array.Empty<MarketInventoryItemData>()
        };
    }

    public static bool TryGetItem(string itemId, out MarketInventoryItemData item)
    {
        item = null;
        string normalizedItemId = PlayerInventoryState.NormalizeItemId(itemId);
        return !string.IsNullOrWhiteSpace(normalizedItemId) &&
               Lookup.TryGetValue(normalizedItemId, out item) &&
               item != null;
    }

    public static string NormalizeCategoryId(string categoryId)
    {
        return string.IsNullOrWhiteSpace(categoryId)
            ? string.Empty
            : categoryId.Trim().ToLowerInvariant();
    }

    private static MarketInventoryItemData CreateAmmoItem(
        string itemId,
        string displayName,
        string description,
        int damage,
        int goldCost,
        int purchaseAmount)
    {
        return new MarketInventoryItemData(
            itemId,
            displayName,
            description,
            AmmoCategoryId,
            PlayerInventoryItemKind.CannonAmmo,
            null,
            $"Damage +{Mathf.Max(0, damage)}",
            $"Bundle {Mathf.Max(1, purchaseAmount):N0}",
            string.Empty,
            CreateCost(goldCost),
            purchaseAmount,
            PlayerInventoryState.GetInventorySortOrder(itemId));
    }

    private static MarketInventoryItemData CreateHarpoonItem(
        string itemId,
        string displayName,
        string description,
        int damage,
        int goldCost,
        int purchaseAmount)
    {
        return new MarketInventoryItemData(
            itemId,
            displayName,
            description,
            HarpoonsCategoryId,
            PlayerInventoryItemKind.Harpoon,
            null,
            $"Damage {Mathf.Max(0, damage)}",
            $"Bundle {Mathf.Max(1, purchaseAmount):N0}",
            "Consumes 1 / attack",
            CreateCost(goldCost),
            purchaseAmount,
            PlayerInventoryState.GetInventorySortOrder(itemId));
    }

    private static MarketInventoryItemData CreateActionItem(
        string itemId,
        string displayName,
        string description,
        string statLine1,
        string statLine2,
        string statLine3,
        int goldCost,
        int purchaseAmount,
        Func<Texture2D> iconResolver)
    {
        return new MarketInventoryItemData(
            itemId,
            displayName,
            description,
            ActionItemsCategoryId,
            PlayerInventoryItemKind.ActionItem,
            iconResolver,
            statLine1,
            statLine2,
            statLine3,
            CreateCost(goldCost),
            purchaseAmount,
            PlayerInventoryState.GetInventorySortOrder(itemId));
    }

    private static MarketCost CreateCost(int goldCost)
    {
        var cost = new MarketCost();
        cost.SetEntries(new[] { new MarketCostValue(MarketCurrencyType.Gold, Mathf.Max(0, goldCost)) });
        return cost;
    }

    private static Dictionary<string, MarketInventoryItemData> BuildLookup()
    {
        var lookup = new Dictionary<string, MarketInventoryItemData>(StringComparer.OrdinalIgnoreCase);
        AddItems(AmmoItems, lookup);
        AddItems(HarpoonItems, lookup);
        AddItems(ActionItems, lookup);
        return lookup;
    }

    private static void AddItems(IEnumerable<MarketInventoryItemData> items, IDictionary<string, MarketInventoryItemData> lookup)
    {
        if (items == null || lookup == null)
        {
            return;
        }

        foreach (MarketInventoryItemData item in items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
            {
                continue;
            }

            lookup[item.Id] = item;
        }
    }
}

public sealed class MarketInventoryItemData
{
    private readonly Func<Texture2D> _iconResolver;

    public MarketInventoryItemData(
        string id,
        string displayName,
        string description,
        string categoryId,
        PlayerInventoryItemKind itemKind,
        Func<Texture2D> iconResolver,
        string statLine1,
        string statLine2,
        string statLine3,
        MarketCost cost,
        int purchaseAmount,
        int sortOrder)
    {
        Id = PlayerInventoryState.NormalizeItemId(id);
        DisplayName = displayName ?? string.Empty;
        Description = description ?? string.Empty;
        CategoryId = MarketInventoryCatalogRuntime.NormalizeCategoryId(categoryId);
        ItemKind = itemKind;
        _iconResolver = iconResolver;
        StatLine1 = statLine1 ?? string.Empty;
        StatLine2 = statLine2 ?? string.Empty;
        StatLine3 = statLine3 ?? string.Empty;
        Cost = cost ?? new MarketCost();
        PurchaseAmount = Mathf.Max(1, purchaseAmount);
        SortOrder = Mathf.Max(0, sortOrder);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public string CategoryId { get; }

    public PlayerInventoryItemKind ItemKind { get; }

    public Texture2D Icon => _iconResolver != null ? _iconResolver.Invoke() : null;

    public string StatLine1 { get; }

    public string StatLine2 { get; }

    public string StatLine3 { get; }

    public MarketCost Cost { get; }

    public int PurchaseAmount { get; }

    public int SortOrder { get; }
}
