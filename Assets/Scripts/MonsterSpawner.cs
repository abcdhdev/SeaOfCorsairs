using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public sealed class MonsterSpawner : MonoBehaviour
{
    private sealed class SpawnSlotRuntime
    {
        public int SlotId;
        public Monster ActiveMonster;
        public Coroutine RespawnCoroutine;
    }

    private const int MaxSpawnSampleAttempts = 24;

    [Header("Spawn Settings")]
    [SerializeField] private GameObject monsterNetworkPrefab;
    [SerializeField, Min(0)] private int spawnCount = 4;
    [SerializeField, Min(0f)] private float spawnRadius = 350f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 3f;
    [SerializeField] private Transform spawnCenter;
    [SerializeField, Min(0f)] private float additionalWaterlineOffset = 0.1f;

    [Header("Respawn Settings")]
    [SerializeField, Min(0.5f)] private float respawnDelaySeconds = 30f;
    [SerializeField, Min(0.25f)] private float respawnRetryIntervalSeconds = 2f;
    [SerializeField, Min(0f)] private float spawnClearanceRadius = 16f;

    private readonly Dictionary<int, SpawnSlotRuntime> spawnSlots = new();
    private bool serverInitialized;
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

            return;
        }

        TryInitializeServerState();
    }

    private void OnDisable()
    {
        ClearSpawnSlots();
        serverInitialized = false;
    }

    public void NotifyMonsterDeath(Monster monster)
    {
        if (!serverInitialized || monster == null || !spawnSlots.TryGetValue(monster.SpawnSlotId, out SpawnSlotRuntime slot))
        {
            return;
        }

        if (slot.ActiveMonster != monster)
        {
            return;
        }

        slot.ActiveMonster = null;
        if (slot.RespawnCoroutine != null)
        {
            StopCoroutine(slot.RespawnCoroutine);
        }

        slot.RespawnCoroutine = StartCoroutine(RespawnSlotAfterDelay(slot, Mathf.Max(0.5f, respawnDelaySeconds), monster));
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

        if (!EnsureNetworkPrefabRegistered(monsterNetworkPrefab))
        {
            Debug.LogError("MonsterSpawner: Assign a monster prefab with NetworkObject and Monster components.", this);
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
                slot.RespawnCoroutine = StartCoroutine(RespawnSlotAfterDelay(slot, Mathf.Max(0.25f, respawnRetryIntervalSeconds), null));
            }
        }
    }

    private bool TrySpawnSlot(SpawnSlotRuntime slot)
    {
        if (slot == null || monsterNetworkPrefab == null)
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

        GameObject instance = Instantiate(monsterNetworkPrefab, spawnPosition, Quaternion.identity);
        SeaSpawnSurfaceUtility.ApplyWaterlineOffset(instance, spawnPosition, additionalWaterlineOffset);

        if (!instance.TryGetComponent(out Monster monster) || !instance.TryGetComponent(out NetworkObject networkObject))
        {
            Destroy(instance);
            Debug.LogError("MonsterSpawner: Monster prefab must contain Monster and NetworkObject components.", this);
            return false;
        }

        monster.BindSpawnSlot(this, slot.SlotId);
        networkObject.Spawn();

        slot.ActiveMonster = monster;
        slot.RespawnCoroutine = null;
        return true;
    }

    private IEnumerator RespawnSlotAfterDelay(SpawnSlotRuntime slot, float delaySeconds, Monster previousMonster)
    {
        // Monsters should disappear on death; the respawn timer is separate.
        if (previousMonster != null && previousMonster.NetworkObject != null && previousMonster.NetworkObject.IsSpawned)
        {
            previousMonster.NetworkObject.Despawn(true);
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

                if ((player.transform.position - spawnPosition).sqrMagnitude <= clearanceSqrDistance)
                {
                    return false;
                }
            }
        }

        foreach (SpawnSlotRuntime slot in spawnSlots.Values)
        {
            if (slot.ActiveMonster == null || !slot.ActiveMonster.IsSpawned)
            {
                continue;
            }

            if ((slot.ActiveMonster.transform.position - spawnPosition).sqrMagnitude <= clearanceSqrDistance)
            {
                return false;
            }
        }

        return true;
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

            if (slot.ActiveMonster != null && slot.ActiveMonster.NetworkObject != null && slot.ActiveMonster.NetworkObject.IsSpawned)
            {
                slot.ActiveMonster.NetworkObject.Despawn(true);
            }

            slot.ActiveMonster = null;
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

        if (!prefab.TryGetComponent(out NetworkObject _) || !prefab.TryGetComponent(out Monster _))
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
}
