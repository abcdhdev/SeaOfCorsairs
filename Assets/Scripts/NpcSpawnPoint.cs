using System;
using UnityEngine;

[DisallowMultipleComponent]
public class NpcSpawnPoint : MonoBehaviour
{
    [SerializeField] private string stableId;
    [SerializeField] private NpcDefinition definitionOverride;

    [Header("Respawn Overrides")]
    [SerializeField] private bool overrideRespawnDelay;
    [SerializeField, Min(0.5f)] private float respawnDelaySeconds = 20f;
    [SerializeField] private bool overrideCorpseLifetime;
    [SerializeField, Min(0f)] private float corpseLifetimeSeconds = 2f;
    [SerializeField] private bool overrideRespawnBlockedDistance;
    [SerializeField, Min(0f)] private float respawnBlockedDistance = 50f;
    [SerializeField] private bool overrideRespawnJitterRadius;
    [SerializeField, Min(0f)] private float respawnJitterRadius = 12f;

    public string StableId => stableId;
    public NpcDefinition DefinitionOverride => definitionOverride;

    public bool TryGetRespawnDelayOverride(out float value)
    {
        value = Mathf.Max(0.5f, respawnDelaySeconds);
        return overrideRespawnDelay;
    }

    public bool TryGetCorpseLifetimeOverride(out float value)
    {
        value = Mathf.Max(0f, corpseLifetimeSeconds);
        return overrideCorpseLifetime;
    }

    public bool TryGetRespawnBlockedDistanceOverride(out float value)
    {
        value = Mathf.Max(0f, respawnBlockedDistance);
        return overrideRespawnBlockedDistance;
    }

    public bool TryGetRespawnJitterRadiusOverride(out float value)
    {
        value = Mathf.Max(0f, respawnJitterRadius);
        return overrideRespawnJitterRadius;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            stableId = Guid.NewGuid().ToString("N");
        }

        if (string.Equals(name, "SpawnPoint", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("NpcSpawnPoint: Avoid naming NPC spawn anchors exactly 'SpawnPoint'. That name is reserved by legacy player spawn fallback lookup.", this);
        }
    }
}
