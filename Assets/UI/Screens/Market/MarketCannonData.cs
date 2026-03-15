using UnityEngine;

[CreateAssetMenu(fileName = "MarketCannonData", menuName = "Sea Wars/Market/Cannon Data")]
public sealed class MarketCannonData : ScriptableObject
{
    [SerializeField] private string id = string.Empty;
    [SerializeField] private string displayName = "New Cannon";
    [SerializeField, TextArea(2, 4)] private string description = string.Empty;
    [SerializeField] private Texture2D icon;
    [SerializeField, Min(0)] private int hitProbability;
    [SerializeField, Min(0f)] private float cannonRange;
    [SerializeField, Min(0.1f)] private float reloadTimeSeconds = 1f;
    [SerializeField] private MarketCost cost = new MarketCost();
    [SerializeField, Min(0)] private int sortOrder;

    public string Id => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim().ToLowerInvariant();
    public string DisplayName => displayName ?? string.Empty;
    public string Description => description ?? string.Empty;
    public Texture2D Icon => icon;
    public int HitProbability => Mathf.Max(0, hitProbability);
    public float CannonRange => Mathf.Max(0f, cannonRange);
    public float ReloadTimeSeconds => Mathf.Max(0.1f, reloadTimeSeconds);
    public MarketCost Cost => cost ?? (cost = new MarketCost());
    public int SortOrder => Mathf.Max(0, sortOrder);

    public void SetEditorValues(
        string newId,
        string newDisplayName,
        string newDescription,
        Texture2D newIcon,
        int newHitProbability,
        float newCannonRange,
        float newReloadTimeSeconds,
        int newSortOrder,
        params MarketCostValue[] newCosts)
    {
        id = string.IsNullOrWhiteSpace(newId) ? string.Empty : newId.Trim().ToLowerInvariant();
        displayName = newDisplayName ?? string.Empty;
        description = newDescription ?? string.Empty;
        icon = newIcon;
        hitProbability = Mathf.Max(0, newHitProbability);
        cannonRange = Mathf.Max(0f, newCannonRange);
        reloadTimeSeconds = Mathf.Max(0.1f, newReloadTimeSeconds);
        sortOrder = Mathf.Max(0, newSortOrder);
        Cost.SetEntries(newCosts);
    }
}
