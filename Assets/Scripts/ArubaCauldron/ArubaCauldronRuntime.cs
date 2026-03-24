using System;
using System.Collections.Generic;
using UnityEngine;

public static class ArubaCauldronRuntime
{
    public const string PortraitResourcePath = "ArubaCauldron/CaptainBarakVanePortrait";
    public const string BonusMapsResourcePath = "ArubaCauldron/BonusMaps";
    public const int DiamondCostPerMissingMojo = 1;
    private const byte PortraitBackgroundFadeStart = 236;
    private const byte PortraitBackgroundFadeEnd = 250;
    private const byte PortraitBackgroundVarianceTolerance = 14;

    private static readonly int[] RitualQuantityOptions = { 1, 5, 25, 100, 500, 1000, 5000 };
    private static readonly PlayerInventoryItemState[] PreviewRewards =
    {
        new(PlayerInventoryState.StandardCannonAmmoItemId, 400),
        new(PlayerInventoryState.StandardHarpoonItemId, 50),
        new(PlayerInventoryState.AgwesArmorPlatingItemId, 10),
        new(PlayerInventoryState.BlackGunpowderItemId, 10)
    };

    private static ArubaBonusMapDefinition[] cachedBonusMaps;

    public static IReadOnlyList<int> GetRitualQuantityOptions() => RitualQuantityOptions;

    public static bool IsValidRitualQuantity(int quantity)
    {
        for (int index = 0; index < RitualQuantityOptions.Length; index++)
        {
            if (RitualQuantityOptions[index] == quantity)
            {
                return true;
            }
        }

        return false;
    }

    public static Texture2D LoadPortrait()
    {
        Texture2D sourcePortrait = Resources.Load<Texture2D>(PortraitResourcePath);
        return CreatePortraitTexture(sourcePortrait) ?? sourcePortrait;
    }

    public static int GetDiamondFallbackCost(int quantity, int availableMojo)
    {
        int normalizedQuantity = Mathf.Max(0, quantity);
        int normalizedMojo = Mathf.Max(0, availableMojo);
        int missingMojo = Mathf.Max(0, normalizedQuantity - normalizedMojo);
        return missingMojo * DiamondCostPerMissingMojo;
    }

    public static int GetPreviewMojoSpend(int quantity, int availableMojo)
    {
        return Mathf.Min(Mathf.Max(0, quantity), Mathf.Max(0, availableMojo));
    }

    public static IReadOnlyList<PlayerInventoryItemState> GetPreviewRewards() => PreviewRewards;

    public static string GetRewardDisplayName(string itemId)
    {
        return PlayerInventoryState.NormalizeItemId(itemId) switch
        {
            PlayerInventoryState.StandardCannonAmmoItemId => "Cannonballs",
            PlayerInventoryState.StandardHarpoonItemId => "Harpoons",
            PlayerInventoryState.AgwesArmorPlatingItemId => "Agwe's Armor Plates",
            PlayerInventoryState.BlackGunpowderItemId => "Black Gunpowder",
            _ => "Unknown Reward"
        };
    }

    public static string GetRewardShortCode(string itemId)
    {
        return PlayerInventoryState.NormalizeItemId(itemId) switch
        {
            PlayerInventoryState.StandardCannonAmmoItemId => "CB",
            PlayerInventoryState.StandardHarpoonItemId => "HP",
            PlayerInventoryState.AgwesArmorPlatingItemId => "AP",
            PlayerInventoryState.BlackGunpowderItemId => "BG",
            _ => "?"
        };
    }

    public static Texture2D GetRewardIcon(string itemId)
    {
        return PlayerInventoryState.NormalizeItemId(itemId) switch
        {
            PlayerInventoryState.AgwesArmorPlatingItemId => ActionItemIconCatalog.GetHudIcon(PlayerActionItemType.AgwesArmorPlating),
            PlayerInventoryState.BlackGunpowderItemId => ActionItemIconCatalog.GetHudIcon(PlayerActionItemType.BlackGunpowder),
            _ => null
        };
    }

    public static string GetRewardAccentClass(string itemId)
    {
        return PlayerInventoryState.NormalizeItemId(itemId) switch
        {
            PlayerInventoryState.StandardCannonAmmoItemId => "aruba-cauldron-reward-accent-cannon",
            PlayerInventoryState.StandardHarpoonItemId => "aruba-cauldron-reward-accent-harpoon",
            PlayerInventoryState.AgwesArmorPlatingItemId => "aruba-cauldron-reward-accent-armor",
            PlayerInventoryState.BlackGunpowderItemId => "aruba-cauldron-reward-accent-powder",
            _ => string.Empty
        };
    }

    public static IReadOnlyList<ArubaBonusMapDefinition> LoadBonusMaps()
    {
        if (cachedBonusMaps != null && cachedBonusMaps.Length > 0)
        {
            return cachedBonusMaps;
        }

        ArubaBonusMapDefinition[] loadedMaps = Resources.LoadAll<ArubaBonusMapDefinition>(BonusMapsResourcePath);
        if (loadedMaps != null && loadedMaps.Length > 0)
        {
            Array.Sort(loadedMaps, static (left, right) =>
            {
                if (left == null && right == null)
                {
                    return 0;
                }

                if (left == null)
                {
                    return 1;
                }

                if (right == null)
                {
                    return -1;
                }

                int sortOrderComparison = left.SortOrder.CompareTo(right.SortOrder);
                if (sortOrderComparison != 0)
                {
                    return sortOrderComparison;
                }

                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            cachedBonusMaps = loadedMaps;
            return cachedBonusMaps;
        }

        cachedBonusMaps = CreateFallbackBonusMaps();
        return cachedBonusMaps;
    }

    private static ArubaBonusMapDefinition[] CreateFallbackBonusMaps()
    {
        return new[]
        {
            CreateFallbackMap("virgo-map", "Virgo Map", "VI", 1, 20, 479, 0),
            CreateFallbackMap("capricorn-map", "Capricorn Map", "CP", 2, 32, 240, 1),
            CreateFallbackMap("sagittarius-map", "Sagittarius Map", "SG", 16, 48, 123, 2),
            CreateFallbackMap("cancer-map", "Cancer Map", "CN", 49, 64, 79, 3)
        };
    }

    private static ArubaBonusMapDefinition CreateFallbackMap(
        string id,
        string displayName,
        string badgeText,
        int collectedPieces,
        int requiredPieces,
        int completedMaps,
        int sortOrder)
    {
        ArubaBonusMapDefinition map = ScriptableObject.CreateInstance<ArubaBonusMapDefinition>();
        map.hideFlags = HideFlags.HideAndDontSave;
        map.SetEditorValues(id, displayName, badgeText, collectedPieces, requiredPieces, completedMaps, sortOrder);
        return map;
    }

    private static Texture2D CreatePortraitTexture(Texture2D sourcePortrait)
    {
        if (sourcePortrait == null)
        {
            return null;
        }

        if (!sourcePortrait.isReadable)
        {
            return sourcePortrait;
        }

        Color32[] sourcePixels = sourcePortrait.GetPixels32();
        if (sourcePixels == null || sourcePixels.Length == 0)
        {
            return sourcePortrait;
        }

        var maskedPixels = new Color32[sourcePixels.Length];
        for (int index = 0; index < sourcePixels.Length; index++)
        {
            Color32 pixel = sourcePixels[index];
            byte preservedAlpha = pixel.a;
            byte computedAlpha = EvaluatePortraitAlpha(pixel);
            pixel.a = (byte)((preservedAlpha * computedAlpha) / byte.MaxValue);
            maskedPixels[index] = pixel;
        }

        var portraitTexture = new Texture2D(sourcePortrait.width, sourcePortrait.height, TextureFormat.RGBA32, sourcePortrait.mipmapCount > 1);
        portraitTexture.name = $"{sourcePortrait.name}_Masked";
        portraitTexture.filterMode = sourcePortrait.filterMode;
        portraitTexture.wrapMode = sourcePortrait.wrapMode;
        portraitTexture.anisoLevel = sourcePortrait.anisoLevel;
        portraitTexture.SetPixels32(maskedPixels);
        portraitTexture.Apply(updateMipmaps: sourcePortrait.mipmapCount > 1, makeNoLongerReadable: false);
        return portraitTexture;
    }

    private static byte EvaluatePortraitAlpha(Color32 pixel)
    {
        byte minimumChannel = Math.Min(pixel.r, Math.Min(pixel.g, pixel.b));
        byte maximumChannel = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
        if (maximumChannel - minimumChannel > PortraitBackgroundVarianceTolerance)
        {
            return byte.MaxValue;
        }

        int average = (pixel.r + pixel.g + pixel.b) / 3;
        if (average <= PortraitBackgroundFadeStart)
        {
            return byte.MaxValue;
        }

        if (average >= PortraitBackgroundFadeEnd)
        {
            return 0;
        }

        float alpha = Mathf.InverseLerp(PortraitBackgroundFadeEnd, PortraitBackgroundFadeStart, average);
        return (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * byte.MaxValue), 0, byte.MaxValue);
    }
}

public sealed class ArubaCauldronRitualResultData
{
    public ArubaCauldronRitualResultData(bool success, string message, int quantity, int mojoSpent, int diamondSpent, string rewardSnapshot)
    {
        Success = success;
        Message = string.IsNullOrWhiteSpace(message)
            ? (success ? "Ritual completed." : "Ritual failed.")
            : message.Trim();
        Quantity = Mathf.Max(0, quantity);
        MojoSpent = Mathf.Max(0, mojoSpent);
        DiamondSpent = Mathf.Max(0, diamondSpent);
        RewardSnapshot = rewardSnapshot ?? string.Empty;
    }

    public bool Success { get; }

    public string Message { get; }

    public int Quantity { get; }

    public int MojoSpent { get; }

    public int DiamondSpent { get; }

    public string RewardSnapshot { get; }

    public IReadOnlyList<PlayerInventoryItemState> GetRewards()
    {
        return PlayerInventoryState.ParseInventorySnapshot(RewardSnapshot);
    }
}
