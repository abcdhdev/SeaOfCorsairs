using System;
using System.Collections.Generic;
using UnityEngine;

public static class MarketShipCatalogRuntime
{
    private const string MarketShipCatalogResourcePath = "Market/MarketShipCatalog";
    public const string DefaultShipId = "elite27";

    private static MarketShipCatalog cachedCatalog;
    private static Dictionary<string, MarketShipData> cachedLookup;

    public static MarketShipCatalog LoadCatalog()
    {
        if (cachedCatalog != null)
        {
            return cachedCatalog;
        }

        cachedCatalog = Resources.Load<MarketShipCatalog>(MarketShipCatalogResourcePath);
        BuildLookup();
        return cachedCatalog;
    }

    public static IReadOnlyList<MarketShipData> GetShips()
    {
        MarketShipCatalog catalog = LoadCatalog();
        return catalog != null ? catalog.Ships : Array.Empty<MarketShipData>();
    }

    public static bool TryGetShip(string shipId, out MarketShipData ship)
    {
        ship = null;
        string normalizedId = NormalizeShipId(shipId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        if (cachedLookup == null)
        {
            LoadCatalog();
        }

        return cachedLookup != null && cachedLookup.TryGetValue(normalizedId, out ship) && ship != null;
    }

    public static string NormalizeShipId(string shipId)
    {
        return string.IsNullOrWhiteSpace(shipId)
            ? string.Empty
            : shipId.Trim().ToLowerInvariant();
    }

    private static void BuildLookup()
    {
        cachedLookup = new Dictionary<string, MarketShipData>(StringComparer.OrdinalIgnoreCase);
        if (cachedCatalog == null || cachedCatalog.Ships == null)
        {
            return;
        }

        for (int index = 0; index < cachedCatalog.Ships.Count; index++)
        {
            MarketShipData ship = cachedCatalog.Ships[index];
            if (ship == null || string.IsNullOrWhiteSpace(ship.Id))
            {
                continue;
            }

            cachedLookup[ship.Id] = ship;
        }
    }
}
