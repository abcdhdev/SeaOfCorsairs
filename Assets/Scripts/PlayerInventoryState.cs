using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerInventoryItemKind
{
    Unknown = 0,
    Cannon = 1,
    CannonAmmo = 2,
    Harpoon = 3,
    ActionItem = 4
}

public readonly struct PlayerInventoryItemState
{
    public PlayerInventoryItemState(string itemId, int amount)
    {
        ItemId = PlayerInventoryState.NormalizeItemId(itemId);
        Amount = Mathf.Max(0, amount);
    }

    public string ItemId { get; }

    public int Amount { get; }
}

public sealed class ShipCannonLoadoutState
{
    public ShipCannonLoadoutState(string shipId, IReadOnlyList<PlayerInventoryItemState> cannonStacks)
    {
        ShipId = MarketShipCatalogRuntime.NormalizeShipId(shipId);
        CannonStacks = cannonStacks ?? Array.Empty<PlayerInventoryItemState>();
    }

    public string ShipId { get; }

    public IReadOnlyList<PlayerInventoryItemState> CannonStacks { get; }
}

public static class PlayerInventoryState
{
    public const string BlackGunpowderItemId = "black_gunpowder";
    public const string AgwesArmorPlatingItemId = "agwes_armor_plating";

    private const char InventoryEntrySeparator = ',';
    private const char InventoryKeyValueSeparator = '=';
    private const char LoadoutShipSeparator = ';';
    private const char LoadoutHeaderSeparator = ':';
    private const char LoadoutEntrySeparator = '|';

    public static string NormalizeItemId(string itemId)
    {
        return string.IsNullOrWhiteSpace(itemId)
            ? string.Empty
            : itemId.Trim().ToLowerInvariant();
    }

    public static PlayerInventoryItemKind GetItemKind(string itemId)
    {
        string normalizedItemId = NormalizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(normalizedItemId))
        {
            return PlayerInventoryItemKind.Unknown;
        }

        if (MarketCannonCatalogRuntime.TryGetCannon(normalizedItemId, out _))
        {
            return PlayerInventoryItemKind.Cannon;
        }

        return normalizedItemId switch
        {
            "standard" => PlayerInventoryItemKind.CannonAmmo,
            "heavy" => PlayerInventoryItemKind.CannonAmmo,
            "chain" => PlayerInventoryItemKind.CannonAmmo,
            "harpoon-25" => PlayerInventoryItemKind.Harpoon,
            "harpoon-50" => PlayerInventoryItemKind.Harpoon,
            "harpoon-250" => PlayerInventoryItemKind.Harpoon,
            BlackGunpowderItemId => PlayerInventoryItemKind.ActionItem,
            AgwesArmorPlatingItemId => PlayerInventoryItemKind.ActionItem,
            _ => PlayerInventoryItemKind.Unknown
        };
    }

    public static bool IsCannon(string itemId)
    {
        return GetItemKind(itemId) == PlayerInventoryItemKind.Cannon;
    }

    public static bool IsCannonAmmo(string itemId)
    {
        return GetItemKind(itemId) == PlayerInventoryItemKind.CannonAmmo;
    }

    public static bool IsHarpoon(string itemId)
    {
        return GetItemKind(itemId) == PlayerInventoryItemKind.Harpoon;
    }

    public static bool IsActionItem(string itemId)
    {
        return GetItemKind(itemId) == PlayerInventoryItemKind.ActionItem;
    }

    public static string GetActionItemInventoryId(PlayerActionItemType actionItemType)
    {
        return actionItemType switch
        {
            PlayerActionItemType.BlackGunpowder => BlackGunpowderItemId,
            PlayerActionItemType.AgwesArmorPlating => AgwesArmorPlatingItemId,
            _ => string.Empty
        };
    }

    public static PlayerActionItemType GetActionItemType(string itemId)
    {
        return NormalizeItemId(itemId) switch
        {
            BlackGunpowderItemId => PlayerActionItemType.BlackGunpowder,
            AgwesArmorPlatingItemId => PlayerActionItemType.AgwesArmorPlating,
            _ => PlayerActionItemType.None
        };
    }

    public static string BuildInventorySnapshot(IEnumerable<PlayerInventoryItemState> inventoryItems)
    {
        List<PlayerInventoryItemState> normalizedItems = NormalizeInventory(inventoryItems);
        if (normalizedItems.Count == 0)
        {
            return string.Empty;
        }

        var parts = new string[normalizedItems.Count];
        for (int index = 0; index < normalizedItems.Count; index++)
        {
            PlayerInventoryItemState item = normalizedItems[index];
            parts[index] = $"{item.ItemId}{InventoryKeyValueSeparator}{item.Amount}";
        }

        return string.Join(InventoryEntrySeparator.ToString(), parts);
    }

    public static List<PlayerInventoryItemState> ParseInventorySnapshot(string snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return new List<PlayerInventoryItemState>();
        }

        string[] rawEntries = snapshot.Split(new[] { InventoryEntrySeparator }, StringSplitOptions.RemoveEmptyEntries);
        var items = new List<PlayerInventoryItemState>(rawEntries.Length);
        for (int index = 0; index < rawEntries.Length; index++)
        {
            string rawEntry = rawEntries[index];
            int splitIndex = rawEntry.IndexOf(InventoryKeyValueSeparator);
            if (splitIndex <= 0 || splitIndex >= rawEntry.Length - 1)
            {
                continue;
            }

            string itemId = NormalizeItemId(rawEntry.Substring(0, splitIndex));
            if (GetItemKind(itemId) == PlayerInventoryItemKind.Unknown)
            {
                continue;
            }

            if (!int.TryParse(rawEntry.Substring(splitIndex + 1), out int amount))
            {
                continue;
            }

            if (amount <= 0)
            {
                continue;
            }

            items.Add(new PlayerInventoryItemState(itemId, amount));
        }

        return NormalizeInventory(items);
    }

    public static string BuildShipCannonLoadoutsSnapshot(IEnumerable<ShipCannonLoadoutState> loadouts)
    {
        List<ShipCannonLoadoutState> normalizedLoadouts = NormalizeShipCannonLoadouts(loadouts);
        if (normalizedLoadouts.Count == 0)
        {
            return string.Empty;
        }

        var shipParts = new string[normalizedLoadouts.Count];
        for (int shipIndex = 0; shipIndex < normalizedLoadouts.Count; shipIndex++)
        {
            ShipCannonLoadoutState loadout = normalizedLoadouts[shipIndex];
            IReadOnlyList<PlayerInventoryItemState> cannonStacks = loadout.CannonStacks ?? Array.Empty<PlayerInventoryItemState>();
            if (cannonStacks.Count == 0)
            {
                shipParts[shipIndex] = loadout.ShipId;
                continue;
            }

            var entryParts = new string[cannonStacks.Count];
            for (int entryIndex = 0; entryIndex < cannonStacks.Count; entryIndex++)
            {
                PlayerInventoryItemState stack = cannonStacks[entryIndex];
                entryParts[entryIndex] = $"{stack.ItemId}{InventoryKeyValueSeparator}{stack.Amount}";
            }

            shipParts[shipIndex] = $"{loadout.ShipId}{LoadoutHeaderSeparator}{string.Join(LoadoutEntrySeparator.ToString(), entryParts)}";
        }

        return string.Join(LoadoutShipSeparator.ToString(), shipParts);
    }

    public static List<ShipCannonLoadoutState> ParseShipCannonLoadoutsSnapshot(string snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return new List<ShipCannonLoadoutState>();
        }

        string[] rawShips = snapshot.Split(new[] { LoadoutShipSeparator }, StringSplitOptions.RemoveEmptyEntries);
        var loadouts = new List<ShipCannonLoadoutState>(rawShips.Length);
        for (int shipIndex = 0; shipIndex < rawShips.Length; shipIndex++)
        {
            string rawShip = rawShips[shipIndex];
            int splitIndex = rawShip.IndexOf(LoadoutHeaderSeparator);
            string shipId = splitIndex >= 0
                ? MarketShipCatalogRuntime.NormalizeShipId(rawShip.Substring(0, splitIndex))
                : MarketShipCatalogRuntime.NormalizeShipId(rawShip);
            if (string.IsNullOrWhiteSpace(shipId) || !MarketShipCatalogRuntime.TryGetShip(shipId, out _))
            {
                continue;
            }

            var cannonStacks = new List<PlayerInventoryItemState>();
            if (splitIndex >= 0 && splitIndex < rawShip.Length - 1)
            {
                string rawEntries = rawShip.Substring(splitIndex + 1);
                string[] splitEntries = rawEntries.Split(new[] { LoadoutEntrySeparator }, StringSplitOptions.RemoveEmptyEntries);
                for (int entryIndex = 0; entryIndex < splitEntries.Length; entryIndex++)
                {
                    string rawEntry = splitEntries[entryIndex];
                    int entrySplitIndex = rawEntry.IndexOf(InventoryKeyValueSeparator);
                    if (entrySplitIndex <= 0 || entrySplitIndex >= rawEntry.Length - 1)
                    {
                        continue;
                    }

                    string cannonId = NormalizeItemId(rawEntry.Substring(0, entrySplitIndex));
                    if (!IsCannon(cannonId))
                    {
                        continue;
                    }

                    if (!int.TryParse(rawEntry.Substring(entrySplitIndex + 1), out int amount) || amount <= 0)
                    {
                        continue;
                    }

                    cannonStacks.Add(new PlayerInventoryItemState(cannonId, amount));
                }
            }

            loadouts.Add(new ShipCannonLoadoutState(shipId, cannonStacks));
        }

        return NormalizeShipCannonLoadouts(loadouts);
    }

    public static List<PlayerInventoryItemState> NormalizeInventory(IEnumerable<PlayerInventoryItemState> inventoryItems)
    {
        var amountsByItemId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (inventoryItems != null)
        {
            foreach (PlayerInventoryItemState item in inventoryItems)
            {
                string normalizedItemId = NormalizeItemId(item.ItemId);
                if (GetItemKind(normalizedItemId) == PlayerInventoryItemKind.Unknown || item.Amount <= 0)
                {
                    continue;
                }

                amountsByItemId.TryGetValue(normalizedItemId, out int currentAmount);
                long combinedAmount = (long)currentAmount + item.Amount;
                amountsByItemId[normalizedItemId] = combinedAmount >= int.MaxValue ? int.MaxValue : (int)combinedAmount;
            }
        }

        var normalizedItems = new List<PlayerInventoryItemState>(amountsByItemId.Count);
        foreach (KeyValuePair<string, int> entry in amountsByItemId)
        {
            if (entry.Value <= 0)
            {
                continue;
            }

            normalizedItems.Add(new PlayerInventoryItemState(entry.Key, entry.Value));
        }

        normalizedItems.Sort(CompareInventoryItems);
        return normalizedItems;
    }

    public static List<ShipCannonLoadoutState> NormalizeShipCannonLoadouts(IEnumerable<ShipCannonLoadoutState> loadouts)
    {
        var normalizedLoadouts = new List<ShipCannonLoadoutState>();
        if (loadouts == null)
        {
            return normalizedLoadouts;
        }

        foreach (ShipCannonLoadoutState loadout in loadouts)
        {
            if (loadout == null)
            {
                continue;
            }

            string shipId = MarketShipCatalogRuntime.NormalizeShipId(loadout.ShipId);
            if (string.IsNullOrWhiteSpace(shipId) || !MarketShipCatalogRuntime.TryGetShip(shipId, out _))
            {
                continue;
            }

            List<PlayerInventoryItemState> cannonStacks = NormalizeInventory(loadout.CannonStacks);
            cannonStacks.RemoveAll(static stack => !IsCannon(stack.ItemId));
            normalizedLoadouts.Add(new ShipCannonLoadoutState(shipId, cannonStacks));
        }

        normalizedLoadouts.Sort(static (left, right) =>
        {
            if (MarketShipCatalogRuntime.TryGetShip(left.ShipId, out MarketShipData leftShip) &&
                MarketShipCatalogRuntime.TryGetShip(right.ShipId, out MarketShipData rightShip))
            {
                int sortOrderComparison = leftShip.SortOrder.CompareTo(rightShip.SortOrder);
                if (sortOrderComparison != 0)
                {
                    return sortOrderComparison;
                }
            }

            return string.Compare(left.ShipId, right.ShipId, StringComparison.OrdinalIgnoreCase);
        });

        return normalizedLoadouts;
    }

    public static Dictionary<string, int> CreateAmountLookup(IEnumerable<PlayerInventoryItemState> inventoryItems)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (inventoryItems == null)
        {
            return lookup;
        }

        foreach (PlayerInventoryItemState item in inventoryItems)
        {
            string normalizedItemId = NormalizeItemId(item.ItemId);
            if (GetItemKind(normalizedItemId) == PlayerInventoryItemKind.Unknown || item.Amount <= 0)
            {
                continue;
            }

            lookup[normalizedItemId] = item.Amount;
        }

        return lookup;
    }

    private static int CompareInventoryItems(PlayerInventoryItemState left, PlayerInventoryItemState right)
    {
        int leftOrder = GetInventorySortOrder(left.ItemId);
        int rightOrder = GetInventorySortOrder(right.ItemId);
        int orderComparison = leftOrder.CompareTo(rightOrder);
        if (orderComparison != 0)
        {
            return orderComparison;
        }

        return string.Compare(left.ItemId, right.ItemId, StringComparison.OrdinalIgnoreCase);
    }

    public static int GetInventorySortOrder(string itemId)
    {
        string normalizedItemId = NormalizeItemId(itemId);
        if (MarketCannonCatalogRuntime.TryGetCannon(normalizedItemId, out MarketCannonData cannon) && cannon != null)
        {
            return cannon.SortOrder;
        }

        return normalizedItemId switch
        {
            "standard" => 1000,
            "heavy" => 1001,
            "chain" => 1002,
            "harpoon-25" => 1100,
            "harpoon-50" => 1101,
            "harpoon-250" => 1102,
            BlackGunpowderItemId => 1200,
            AgwesArmorPlatingItemId => 1201,
            _ => int.MaxValue
        };
    }
}
