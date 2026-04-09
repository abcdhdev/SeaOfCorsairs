using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "NpcDefinition", menuName = "Sea Wars/NPC Definition")]
public class NpcDefinition : ScriptableObject
{
    [SerializeField, HideInInspector] private string stableId = string.Empty;

    [Header("Identity")]
    [SerializeField] private string npcName = "Raider";
    [SerializeField] private GameObject visualPrefab;

    [Header("Combat")]
    [SerializeField, Min(1)] private int health = 100;
    [SerializeField, Min(0)] private int damage = 20;
    [SerializeField, Min(0.05f)] private float attackIntervalSeconds = 2f;
    [SerializeField, Min(0.5f)] private float respawnDelaySeconds = 20f;
    [SerializeField, Min(0f)] private float corpseLifetimeSeconds = 2f;

    [Header("Reward")]
    [SerializeField] private NpcReward reward;

    public string NpcName => npcName;
    public string StableId => NormalizeStableId(stableId);
    public GameObject VisualPrefab => visualPrefab;
    public int Health => Mathf.Max(1, health);
    public int Damage => Mathf.Max(0, damage);
    public float AttackIntervalSeconds => Mathf.Max(0.05f, attackIntervalSeconds);
    public float RespawnDelaySeconds => Mathf.Max(0.5f, respawnDelaySeconds);
    public float CorpseLifetimeSeconds => Mathf.Max(0f, corpseLifetimeSeconds);
    public NpcReward Reward => reward;

    private void OnValidate()
    {
        SyncStableId();

        if (visualPrefab != null && visualPrefab.TryGetComponent(out NetworkObject _))
        {
            Debug.LogWarning($"NpcDefinition '{name}': visualPrefab '{visualPrefab.name}' has a NetworkObject. Assign a visual-only prefab/model.", this);
        }
    }

    public static string NormalizeStableId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private void SyncStableId()
    {
        stableId = NormalizeStableId(stableId);

#if UNITY_EDITOR
        string assetPath = AssetDatabase.GetAssetPath(this);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return;
        }

        string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
        if (!string.IsNullOrWhiteSpace(assetGuid))
        {
            stableId = NormalizeStableId(assetGuid);
        }
#endif
    }
}
