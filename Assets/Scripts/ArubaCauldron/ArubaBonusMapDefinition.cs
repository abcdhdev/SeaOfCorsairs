using UnityEngine;

[CreateAssetMenu(fileName = "ArubaBonusMap", menuName = "Sea Wars/Aruba Cauldron/Bonus Map")]
public sealed class ArubaBonusMapDefinition : ScriptableObject
{
    [SerializeField] private string id = string.Empty;
    [SerializeField] private string displayName = "Bonus Map";
    [SerializeField] private string badgeText = "BM";
    [SerializeField, Min(0)] private int collectedPieces;
    [SerializeField, Min(1)] private int requiredPieces = 1;
    [SerializeField, Min(0)] private int completedMaps;
    [SerializeField, Min(0)] private int sortOrder;

    public string Id => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim().ToLowerInvariant();
    public string DisplayName => displayName ?? string.Empty;
    public string BadgeText => string.IsNullOrWhiteSpace(badgeText) ? "BM" : badgeText.Trim().ToUpperInvariant();
    public int CollectedPieces => Mathf.Max(0, collectedPieces);
    public int RequiredPieces => Mathf.Max(1, requiredPieces);
    public int CompletedMaps => Mathf.Max(0, completedMaps);
    public int SortOrder => Mathf.Max(0, sortOrder);

    public void SetEditorValues(
        string newId,
        string newDisplayName,
        string newBadgeText,
        int newCollectedPieces,
        int newRequiredPieces,
        int newCompletedMaps,
        int newSortOrder)
    {
        id = string.IsNullOrWhiteSpace(newId) ? string.Empty : newId.Trim().ToLowerInvariant();
        displayName = newDisplayName ?? string.Empty;
        badgeText = string.IsNullOrWhiteSpace(newBadgeText) ? "BM" : newBadgeText.Trim().ToUpperInvariant();
        collectedPieces = Mathf.Max(0, newCollectedPieces);
        requiredPieces = Mathf.Max(1, newRequiredPieces);
        completedMaps = Mathf.Max(0, newCompletedMaps);
        sortOrder = Mathf.Max(0, newSortOrder);
    }
}
