using UnityEngine;

[CreateAssetMenu(fileName = "MarketShipData", menuName = "Sea Wars/Market/Ship Data")]
public sealed class MarketShipData : ScriptableObject
{
    [SerializeField] private string id = string.Empty;
    [SerializeField] private string displayName = "New Ship";
    [SerializeField, TextArea(2, 4)] private string description = string.Empty;
    [SerializeField] private Texture2D icon;
    [SerializeField] private string primaryStatLabel = string.Empty;
    [SerializeField] private string secondaryStatLabel = string.Empty;
    [SerializeField] private string tertiaryStatLabel = string.Empty;
    [SerializeField] private MarketCost cost = new MarketCost();
    [SerializeField, Min(0)] private int sortOrder;

    public string Id => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim().ToLowerInvariant();
    public string DisplayName => displayName ?? string.Empty;
    public string Description => description ?? string.Empty;
    public Texture2D Icon => icon;
    public string PrimaryStatLabel => primaryStatLabel ?? string.Empty;
    public string SecondaryStatLabel => secondaryStatLabel ?? string.Empty;
    public string TertiaryStatLabel => tertiaryStatLabel ?? string.Empty;
    public MarketCost Cost => cost ?? (cost = new MarketCost());
    public int SortOrder => Mathf.Max(0, sortOrder);

    public void SetEditorValues(
        string newId,
        string newDisplayName,
        string newDescription,
        Texture2D newIcon,
        string newPrimaryStatLabel,
        string newSecondaryStatLabel,
        string newTertiaryStatLabel,
        int newSortOrder,
        params MarketCostValue[] newCosts)
    {
        id = string.IsNullOrWhiteSpace(newId) ? string.Empty : newId.Trim().ToLowerInvariant();
        displayName = newDisplayName ?? string.Empty;
        description = newDescription ?? string.Empty;
        icon = newIcon;
        primaryStatLabel = newPrimaryStatLabel ?? string.Empty;
        secondaryStatLabel = newSecondaryStatLabel ?? string.Empty;
        tertiaryStatLabel = newTertiaryStatLabel ?? string.Empty;
        sortOrder = Mathf.Max(0, newSortOrder);
        Cost.SetEntries(newCosts);
    }
}
