using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(fileName = "NpcDefinition", menuName = "Sea Wars/NPC Definition")]
public class NpcDefinition : ScriptableObject
{
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
    public GameObject VisualPrefab => visualPrefab;
    public int Health => Mathf.Max(1, health);
    public int Damage => Mathf.Max(0, damage);
    public float AttackIntervalSeconds => Mathf.Max(0.05f, attackIntervalSeconds);
    public float RespawnDelaySeconds => Mathf.Max(0.5f, respawnDelaySeconds);
    public float CorpseLifetimeSeconds => Mathf.Max(0f, corpseLifetimeSeconds);
    public NpcReward Reward => reward;

    private void OnValidate()
    {
        if (visualPrefab != null && visualPrefab.TryGetComponent(out NetworkObject _))
        {
            Debug.LogWarning($"NpcDefinition '{name}': visualPrefab '{visualPrefab.name}' has a NetworkObject. Assign a visual-only prefab/model.", this);
        }
    }
}
