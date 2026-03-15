using System.Collections.Generic;

public static class ShipSectionDummyData
{
    public static ShipSectionData Create()
    {
        var tabs = new List<ShipSectionTabData>
        {
            new ShipSectionTabData("shipyard", "Shipyard"),
            new ShipSectionTabData("modules", "Modules"),
            new ShipSectionTabData("crew", "Crew"),
            new ShipSectionTabData("flags", "Flags")
        };

        var categories = new List<ShipSectionCategoryData>
        {
            new ShipSectionCategoryData("starter", "shipyard", "Starter Hulls"),
            new ShipSectionCategoryData("raiders", "shipyard", "Raiders"),
            new ShipSectionCategoryData("merchant", "shipyard", "Merchant Class"),
            new ShipSectionCategoryData("legendary", "shipyard", "Legendary Builds"),
            new ShipSectionCategoryData("cannons", "modules", "Cannons"),
            new ShipSectionCategoryData("sails", "modules", "Sails"),
            new ShipSectionCategoryData("armor", "modules", "Hull Plating"),
            new ShipSectionCategoryData("captains", "crew", "Captains"),
            new ShipSectionCategoryData("gunners", "crew", "Gunners"),
            new ShipSectionCategoryData("deckhands", "crew", "Deckhands"),
            new ShipSectionCategoryData("faction", "flags", "Faction Flags"),
            new ShipSectionCategoryData("signal", "flags", "Signal Banners")
        };

        var items = new List<ShipSectionItemData>
        {
            new ShipSectionItemData("swiftwake-sloop", "shipyard", "starter", "Swiftwake Sloop", "Fast scout hull with a sharp turning arc.", 320, "#4FC6FF"),
            new ShipSectionItemData("coastal-cutter", "shipyard", "starter", "Coastal Cutter", "Balanced frame for early trade routes and escort duty.", 460, "#69D5AA"),
            new ShipSectionItemData("ironfin-brig", "shipyard", "raiders", "Ironfin Brig", "Boarding-focused brig with reinforced prow armor.", 950, "#D87956"),
            new ShipSectionItemData("stormwrath-frigate", "shipyard", "raiders", "Stormwrath Frigate", "Aggressive broadside platform for open-water ambushes.", 1480, "#C9A86B"),
            new ShipSectionItemData("ledgerline-hauler", "shipyard", "merchant", "Ledgerline Hauler", "Expanded storage hold with reduced crew upkeep.", 880, "#8DD0F8"),
            new ShipSectionItemData("royal-azure-galleon", "shipyard", "legendary", "Royal Azure Galleon", "High-end flagship fitted for convoy command.", 2450, "#A7B7FF"),
            new ShipSectionItemData("hollowbore-cannon", "modules", "cannons", "Hollowbore Cannon", "Reliable deck cannon with lower recoil drift.", 210, "#E6B45C", 3),
            new ShipSectionItemData("ember-lance", "modules", "cannons", "Ember Lance", "Incendiary broadside tube for siege openings.", 360, "#F08A58", 2),
            new ShipSectionItemData("galeweave-sails", "modules", "sails", "Galeweave Sails", "Improves acceleration and wind recovery after turns.", 140, "#78D1E1", 2),
            new ShipSectionItemData("deep-keel-plating", "modules", "armor", "Deep Keel Plating", "Heavy plated ribs for collision resistance.", 270, "#8C97A5"),
            new ShipSectionItemData("captain-morrow", "crew", "captains", "Captain Morrow", "Boosts cannon reload timing for nearby gunners.", 780, "#C79AE0"),
            new ShipSectionItemData("quartermaster-vale", "crew", "deckhands", "Quartermaster Vale", "Reduces repair material consumption per encounter.", 420, "#7DDA8A"),
            new ShipSectionItemData("crowsnest-duo", "crew", "gunners", "Crow's Nest Duo", "Spotter pair that increases long-range crit chance.", 510, "#EFC56B"),
            new ShipSectionItemData("freeport-standard", "flags", "faction", "Freeport Standard", "Recognized faction colors for harbor bonuses.", 90, "#4EA7F4", 4),
            new ShipSectionItemData("storm-warning", "flags", "signal", "Storm Warning Banner", "Signal cloth set for convoy and fleet commands.", 65, "#E17878", 5)
        };

        return new ShipSectionData(
            "Ship Section",
            "Figma section imported as a modular in-game panel. Data is placeholder for now.",
            tabs,
            categories,
            items);
    }
}
