using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MarketShipCatalog", menuName = "Sea Wars/Market/Ship Catalog")]
public sealed class MarketShipCatalog : ScriptableObject
{
    [SerializeField] private List<MarketShipData> ships = new List<MarketShipData>();

    public IReadOnlyList<MarketShipData> Ships => ships;

    public void SetEditorShips(IEnumerable<MarketShipData> sourceShips)
    {
        ships.Clear();
        if (sourceShips == null)
        {
            return;
        }

        foreach (MarketShipData sourceShip in sourceShips)
        {
            if (sourceShip == null)
            {
                continue;
            }

            ships.Add(sourceShip);
        }

        ships.Sort((left, right) =>
        {
            if (ReferenceEquals(left, right))
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

            return string.Compare(left.DisplayName, right.DisplayName, System.StringComparison.OrdinalIgnoreCase);
        });
    }
}
