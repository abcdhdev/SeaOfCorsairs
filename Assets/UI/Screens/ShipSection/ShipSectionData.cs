using System;
using System.Collections.Generic;

public sealed class ShipSectionData
{
    public ShipSectionData(
        string title,
        string subtitle,
        IReadOnlyList<ShipSectionTabData> tabs,
        IReadOnlyList<ShipSectionCategoryData> categories,
        IReadOnlyList<ShipSectionItemData> items)
    {
        Title = title ?? string.Empty;
        Subtitle = subtitle ?? string.Empty;
        Tabs = tabs ?? Array.Empty<ShipSectionTabData>();
        Categories = categories ?? Array.Empty<ShipSectionCategoryData>();
        Items = items ?? Array.Empty<ShipSectionItemData>();
    }

    public string Title { get; }

    public string Subtitle { get; }

    public IReadOnlyList<ShipSectionTabData> Tabs { get; }

    public IReadOnlyList<ShipSectionCategoryData> Categories { get; }

    public IReadOnlyList<ShipSectionItemData> Items { get; }
}

public sealed class ShipSectionTabData
{
    public ShipSectionTabData(string id, string title)
    {
        Id = id ?? string.Empty;
        Title = title ?? string.Empty;
    }

    public string Id { get; }

    public string Title { get; }
}

public sealed class ShipSectionCategoryData
{
    public ShipSectionCategoryData(string id, string tabId, string title)
    {
        Id = id ?? string.Empty;
        TabId = tabId ?? string.Empty;
        Title = title ?? string.Empty;
    }

    public string Id { get; }

    public string TabId { get; }

    public string Title { get; }
}

public sealed class ShipSectionItemData
{
    public ShipSectionItemData(
        string id,
        string tabId,
        string categoryId,
        string name,
        string description,
        int cost,
        string accentColor,
        int quantity = 1)
    {
        Id = id ?? string.Empty;
        TabId = tabId ?? string.Empty;
        CategoryId = categoryId ?? string.Empty;
        Name = name ?? string.Empty;
        Description = description ?? string.Empty;
        Cost = Math.Max(0, cost);
        AccentColor = accentColor ?? "#C9A86B";
        Quantity = Math.Max(1, quantity);
    }

    public string Id { get; }

    public string TabId { get; }

    public string CategoryId { get; }

    public string Name { get; }

    public string Description { get; }

    public int Cost { get; }

    public string AccentColor { get; }

    public int Quantity { get; private set; }

    public void ChangeQuantity(int delta)
    {
        Quantity = Math.Max(1, Quantity + delta);
    }
}
