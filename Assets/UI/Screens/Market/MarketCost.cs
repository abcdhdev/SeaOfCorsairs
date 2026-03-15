using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class MarketCost
{
    [SerializeField] private List<MarketCostEntry> entries = new List<MarketCostEntry>();

    public IReadOnlyList<MarketCostEntry> Entries => entries;

    public bool HasEntries => entries != null && entries.Count > 0;

    public int GetAmount(MarketCurrencyType currencyType)
    {
        if (entries == null || entries.Count == 0)
        {
            return 0;
        }

        int total = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            MarketCostEntry entry = entries[index];
            if (entry == null || entry.CurrencyType != currencyType || entry.Amount <= 0)
            {
                continue;
            }

            total += entry.Amount;
        }

        return total;
    }

    public bool CanAfford(int gold, int diamonds)
    {
        return gold >= GetAmount(MarketCurrencyType.Gold) &&
               diamonds >= GetAmount(MarketCurrencyType.Diamonds);
    }

    public string BuildDisplayText()
    {
        string goldText = FormatCurrencyAmount(GetAmount(MarketCurrencyType.Gold), MarketCurrencyType.Gold);
        string diamondText = FormatCurrencyAmount(GetAmount(MarketCurrencyType.Diamonds), MarketCurrencyType.Diamonds);

        if (string.IsNullOrEmpty(goldText))
        {
            return string.IsNullOrEmpty(diamondText) ? "Free" : diamondText;
        }

        if (string.IsNullOrEmpty(diamondText))
        {
            return goldText;
        }

        return $"{goldText} and {diamondText}";
    }

    public string BuildShortageText(int gold, int diamonds)
    {
        int missingGold = Mathf.Max(0, GetAmount(MarketCurrencyType.Gold) - gold);
        int missingDiamonds = Mathf.Max(0, GetAmount(MarketCurrencyType.Diamonds) - diamonds);

        string goldText = FormatCurrencyAmount(missingGold, MarketCurrencyType.Gold);
        string diamondText = FormatCurrencyAmount(missingDiamonds, MarketCurrencyType.Diamonds);

        if (string.IsNullOrEmpty(goldText))
        {
            return string.IsNullOrEmpty(diamondText) ? "Need more currency" : $"Need {diamondText}";
        }

        if (string.IsNullOrEmpty(diamondText))
        {
            return $"Need {goldText}";
        }

        return $"Need {goldText} and {diamondText}";
    }

    public void SetEntries(IEnumerable<MarketCostValue> sourceEntries)
    {
        entries.Clear();
        if (sourceEntries == null)
        {
            return;
        }

        foreach (MarketCostValue sourceEntry in sourceEntries)
        {
            if (sourceEntry.Amount <= 0)
            {
                continue;
            }

            entries.Add(new MarketCostEntry(sourceEntry.CurrencyType, sourceEntry.Amount));
        }
    }

    public static string GetCurrencyLabel(MarketCurrencyType currencyType)
    {
        switch (currencyType)
        {
            case MarketCurrencyType.Diamonds:
                return "diamonds";
            case MarketCurrencyType.Gold:
            default:
                return "gold";
        }
    }

    private static string FormatCurrencyAmount(int amount, MarketCurrencyType currencyType)
    {
        if (amount <= 0)
        {
            return string.Empty;
        }

        return $"{amount:N0} {GetCurrencyLabel(currencyType)}";
    }
}

[Serializable]
public sealed class MarketCostEntry
{
    [SerializeField] private MarketCurrencyType currencyType = MarketCurrencyType.Gold;
    [SerializeField, Min(0)] private int amount;

    public MarketCostEntry()
    {
    }

    public MarketCostEntry(MarketCurrencyType currencyType, int amount)
    {
        this.currencyType = currencyType;
        this.amount = Mathf.Max(0, amount);
    }

    public MarketCurrencyType CurrencyType => currencyType;
    public int Amount => amount;
}

public readonly struct MarketCostValue
{
    public MarketCostValue(MarketCurrencyType currencyType, int amount)
    {
        CurrencyType = currencyType;
        Amount = Mathf.Max(0, amount);
    }

    public MarketCurrencyType CurrencyType { get; }
    public int Amount { get; }
}
