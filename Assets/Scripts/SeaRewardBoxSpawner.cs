using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SeaRewardBoxSpawner : MonoBehaviour
{
    private sealed class SpawnSlotRuntime
    {
        public int SlotId;
        public SeaRewardBox ActiveBox;
        public Coroutine RespawnCoroutine;
    }

    private const int MaxSpawnSampleAttempts = 24;

    [Header("Spawn Settings")]
    [SerializeField] private GameObject boxNetworkPrefab;
    [SerializeField, Min(0)] private int spawnCount = 8;
    [SerializeField, Min(0f)] private float spawnRadius = 350f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 3f;
    [SerializeField] private Transform spawnCenter;
    [SerializeField, Min(0f)] private float additionalWaterlineOffset = 0.15f;
    [SerializeField] private bool requireWorldMapScope = true;

    [Header("Respawn Settings")]
    [SerializeField, Min(0.5f)] private float respawnDelaySeconds = 15f;
    [SerializeField, Min(0.25f)] private float respawnRetryIntervalSeconds = 2f;
    [SerializeField, Min(0f)] private float spawnClearanceRadius = 12f;
    [SerializeField] private bool pauseSpawningWhenMapEmpty = true;

    private readonly Dictionary<int, SpawnSlotRuntime> spawnSlots = new();
    private bool serverInitialized;
    private bool missingWorldMapScopeWarningLogged;
    private int walkableAreaMask;

    private void OnEnable()
    {
        TryInitializeServerState();
    }

    private void Update()
    {
        if (serverInitialized)
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
            {
                ClearSpawnSlots();
                serverInitialized = false;
            }
            else if (pauseSpawningWhenMapEmpty && !WorldMapSceneActivityUtility.HasActivePlayersOnMap(this))
            {
                SuspendActiveSpawns();
            }

            return;
        }

        TryInitializeServerState();
    }

    private void OnDisable()
    {
        ClearSpawnSlots();
        serverInitialized = false;
    }

    public void NotifyBoxCollected(SeaRewardBox rewardBox)
    {
        if (!serverInitialized || rewardBox == null || !spawnSlots.TryGetValue(rewardBox.SpawnSlotId, out SpawnSlotRuntime slot))
        {
            return;
        }

        if (slot.ActiveBox != rewardBox)
        {
            return;
        }

        slot.ActiveBox = null;
        if (slot.RespawnCoroutine != null)
        {
            StopCoroutine(slot.RespawnCoroutine);
        }

        slot.RespawnCoroutine = StartCoroutine(RespawnSlotAfterDelay(slot, Mathf.Max(0.5f, respawnDelaySeconds), rewardBox));
    }

    private void TryInitializeServerState()
    {
        if (serverInitialized)
        {
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        if (!EnsureNetworkPrefabRegistered(boxNetworkPrefab))
        {
            Debug.LogError("SeaRewardBoxSpawner: Assign a box prefab with NetworkObject and SeaRewardBox components.", this);
            return;
        }

        if (requireWorldMapScope && !TryResolveWorldMapId(out _))
        {
            LogMissingWorldMapScopeWarning();
            return;
        }

        walkableAreaMask = SeaSpawnSurfaceUtility.ResolveWalkableAreaMask();
        InitializeSpawnSlots();
        serverInitialized = true;
    }

    private void InitializeSpawnSlots()
    {
        ClearSpawnSlots();

        int desiredSpawnCount = Mathf.Max(0, spawnCount);
        for (int index = 0; index < desiredSpawnCount; index++)
        {
            var slot = new SpawnSlotRuntime
            {
                SlotId = index
            };

            spawnSlots[index] = slot;
            if (!TrySpawnSlot(slot))
            {
                slot.RespawnCoroutine = StartCoroutine(RespawnSlotAfterDelay(slot, Mathf.Max(0.25f, respawnRetryIntervalSeconds)));
            }
        }
    }

    private bool TrySpawnSlot(SpawnSlotRuntime slot)
    {
        if (slot == null || boxNetworkPrefab == null)
        {
            return false;
        }

        if (!TryResolveWorldMapId(out string mapId))
        {
            if (requireWorldMapScope)
            {
                LogMissingWorldMapScopeWarning();
                return false;
            }

            mapId = string.Empty;
        }

        if (pauseSpawningWhenMapEmpty && !WorldMapSceneActivityUtility.HasActivePlayersOnMap(this))
        {
            return false;
        }

        Vector3 center = spawnCenter != null ? spawnCenter.position : transform.position;
        if (!SeaSpawnSurfaceUtility.TryGetRandomWaterNavMeshPosition(
                center,
                spawnRadius,
                navMeshSampleDistance,
                walkableAreaMask,
                MaxSpawnSampleAttempts,
                out Vector3 spawnPosition))
        {
            return false;
        }

        if (!IsSpawnLocationClear(spawnPosition))
        {
            return false;
        }

        GameObject instance = Instantiate(boxNetworkPrefab, spawnPosition, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
        SeaSpawnSurfaceUtility.ApplyWaterlineOffset(instance, spawnPosition, additionalWaterlineOffset);

        if (!instance.TryGetComponent(out SeaRewardBox rewardBox) || !instance.TryGetComponent(out NetworkObject networkObject))
        {
            Destroy(instance);
            Debug.LogError("SeaRewardBoxSpawner: Box prefab must contain SeaRewardBox and NetworkObject components.", this);
            return false;
        }

        rewardBox.BindSpawnSlot(this, slot.SlotId);
        if (!string.IsNullOrWhiteSpace(mapId))
        {
            rewardBox.SetWorldMapIdFromServer(mapId);
        }

        networkObject.Spawn();

        slot.ActiveBox = rewardBox;
        slot.RespawnCoroutine = null;
        return true;
    }

    private IEnumerator RespawnSlotAfterDelay(SpawnSlotRuntime slot, float delaySeconds, SeaRewardBox previousBox = null)
    {
        if (previousBox != null && previousBox.NetworkObject != null && previousBox.NetworkObject.IsSpawned)
        {
            previousBox.NetworkObject.Despawn(true);
        }

        if (delaySeconds > 0f)
        {
            yield return new WaitForSeconds(delaySeconds);
        }

        while (serverInitialized)
        {
            if (TrySpawnSlot(slot))
            {
                yield break;
            }

            yield return new WaitForSeconds(Mathf.Max(0.25f, respawnRetryIntervalSeconds));
        }
    }

    private bool IsSpawnLocationClear(Vector3 spawnPosition)
    {
        if (spawnClearanceRadius <= 0f)
        {
            return true;
        }

        float clearanceSqrDistance = spawnClearanceRadius * spawnClearanceRadius;
        if (PlayerManager.Instance != null)
        {
            List<Player> players = PlayerManager.Instance.GetAllPlayers();
            for (int index = 0; index < players.Count; index++)
            {
                Player player = players[index];
                if (player == null || !player.IsSpawned || player.IsDead)
                {
                    continue;
                }

                if (!WorldMapSceneActivityUtility.IsRelevantPlayerForScopedMap(this, player))
                {
                    continue;
                }

                if ((player.transform.position - spawnPosition).sqrMagnitude <= clearanceSqrDistance)
                {
                    return false;
                }
            }
        }

        foreach (SpawnSlotRuntime slot in spawnSlots.Values)
        {
            if (slot.ActiveBox == null || !slot.ActiveBox.IsSpawned)
            {
                continue;
            }

            if ((slot.ActiveBox.transform.position - spawnPosition).sqrMagnitude <= clearanceSqrDistance)
            {
                return false;
            }
        }

        return true;
    }

    private void SuspendActiveSpawns()
    {
        foreach (SpawnSlotRuntime slot in spawnSlots.Values)
        {
            if (slot == null || slot.ActiveBox == null || slot.RespawnCoroutine != null)
            {
                continue;
            }

            SeaRewardBox activeBox = slot.ActiveBox;
            slot.ActiveBox = null;
            slot.RespawnCoroutine = StartCoroutine(RespawnSlotAfterDelay(slot, Mathf.Max(0.25f, respawnRetryIntervalSeconds), activeBox));
        }
    }

    private void ClearSpawnSlots()
    {
        foreach (SpawnSlotRuntime slot in spawnSlots.Values)
        {
            if (slot.RespawnCoroutine != null)
            {
                StopCoroutine(slot.RespawnCoroutine);
                slot.RespawnCoroutine = null;
            }

            if (slot.ActiveBox != null && slot.ActiveBox.NetworkObject != null && slot.ActiveBox.NetworkObject.IsSpawned)
            {
                slot.ActiveBox.NetworkObject.Despawn(true);
            }

            slot.ActiveBox = null;
        }

        spawnSlots.Clear();
    }

    private bool EnsureNetworkPrefabRegistered(GameObject prefab)
    {
        if (prefab == null)
        {
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            return false;
        }

        if (!prefab.TryGetComponent(out NetworkObject _) || !prefab.TryGetComponent(out SeaRewardBox _))
        {
            return false;
        }

        if (networkManager.NetworkConfig.Prefabs.Contains(prefab))
        {
            return true;
        }

        return networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab
        {
            Prefab = prefab
        });
    }

    private bool TryResolveWorldMapId(out string mapId)
    {
        return WorldMapMembershipUtility.TryGetMapId(this, out mapId) &&
               !string.IsNullOrWhiteSpace(mapId);
    }

    private void LogMissingWorldMapScopeWarning()
    {
        if (missingWorldMapScopeWarningLogged)
        {
            return;
        }

        missingWorldMapScopeWarningLogged = true;
        Debug.LogWarning("SeaRewardBoxSpawner: Spawner is not inside a WorldMapSceneAuthoring scene. Move it under a map scene root or disable requireWorldMapScope.", this);
    }
}
