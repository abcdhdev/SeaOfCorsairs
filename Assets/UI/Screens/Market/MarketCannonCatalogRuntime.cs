using System;
using System.Collections.Generic;
using UnityEngine;

public static class MarketCannonCatalogRuntime
{
    private const string MarketCannonCatalogResourcePath = "Market/MarketCannonCatalog";

    private static MarketCannonCatalog cachedCatalog;
    private static Dictionary<string, MarketCannonData> cachedLookup;

    public static MarketCannonCatalog LoadCatalog()
    {
        if (cachedCatalog != null)
        {
            return cachedCatalog;
        }

        cachedCatalog = Resources.Load<MarketCannonCatalog>(MarketCannonCatalogResourcePath);
        BuildLookup();
        return cachedCatalog;
    }

    public static IReadOnlyList<MarketCannonData> GetCannons()
    {
        MarketCannonCatalog catalog = LoadCatalog();
        return catalog != null ? catalog.Cannons : Array.Empty<MarketCannonData>();
    }

    public static bool TryGetCannon(string cannonId, out MarketCannonData cannon)
    {
        cannon = null;
        string normalizedId = NormalizeCannonId(cannonId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return false;
        }

        if (cachedLookup == null)
        {
            LoadCatalog();
        }

        return cachedLookup != null && cachedLookup.TryGetValue(normalizedId, out cannon) && cannon != null;
    }

    private static void BuildLookup()
    {
        cachedLookup = new Dictionary<string, MarketCannonData>(StringComparer.OrdinalIgnoreCase);
        if (cachedCatalog == null || cachedCatalog.Cannons == null)
        {
            return;
        }

        for (int index = 0; index < cachedCatalog.Cannons.Count; index++)
        {
            MarketCannonData cannon = cachedCatalog.Cannons[index];
            if (cannon == null || string.IsNullOrWhiteSpace(cannon.Id))
            {
                continue;
            }

            cachedLookup[cannon.Id] = cannon;
        }
    }

    private static string NormalizeCannonId(string cannonId)
    {
        return string.IsNullOrWhiteSpace(cannonId)
            ? string.Empty
            : cannonId.Trim().ToLowerInvariant();
    }
}
