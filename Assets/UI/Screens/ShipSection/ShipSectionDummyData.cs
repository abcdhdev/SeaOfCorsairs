using System.Collections.Generic;

public static class ShipSectionDummyData
{
    public static ShipSectionData Create()
    {
        var tabs = new List<ShipSectionTabData>
        {
            new ShipSectionTabData("ships", "Ships")
        };

        var categories = new List<ShipSectionCategoryData>
        {
            new ShipSectionCategoryData("designs", "ships", "Ship Designs"),
            new ShipSectionCategoryData("ship-depot", "ships", "Ship Depot")
        };

        var items = new List<ShipSectionItemData>
        {
            new ShipSectionItemData("elite27", "ships", "designs", "Elite 27", "The battle-tested flagship hull already assigned to every new captain.", "E27", "#C9A86B"),
            new ShipSectionItemData("elite1", "ships", "designs", "Elite 1", "A lighter elite hull with a sharper silhouette for quick swaps.", "E1", "#69D5AA")
        };

        return new ShipSectionData(
            "Ship Section",
            "Swap between the ship designs currently mounted on your hull.",
            tabs,
            categories,
            items);
    }
}
