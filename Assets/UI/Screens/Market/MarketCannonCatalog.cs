using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MarketCannonCatalog", menuName = "Sea Wars/Market/Cannon Catalog")]
public sealed class MarketCannonCatalog : ScriptableObject
{
    [SerializeField] private List<MarketCannonData> cannons = new List<MarketCannonData>();

    public IReadOnlyList<MarketCannonData> Cannons => cannons;

    public void SetEditorCannons(IEnumerable<MarketCannonData> sourceCannons)
    {
        cannons.Clear();
        if (sourceCannons == null)
        {
            return;
        }

        foreach (MarketCannonData sourceCannon in sourceCannons)
        {
            if (sourceCannon == null)
            {
                continue;
            }

            cannons.Add(sourceCannon);
        }

        cannons.Sort((left, right) =>
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
