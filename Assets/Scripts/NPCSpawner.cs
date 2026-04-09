using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// Server-authoritative NPC spawn service.
/// Scene-authored spawner settings create persistent spawn slots that own NPC respawn timers.
/// </summary>
public class NPCSpawner : NetworkBehaviour
{
    private enum SpawnSlotState
    {
        Uninitialized,
        Alive,
        Corpse,
        PendingRespawn,
    }

    private sealed class SpawnSlotRuntime
    {
        public int SlotId;
        public string StableId;
        public NpcSpawnPoint SpawnPoint;
        public int DefinitionIndex;
        public NpcDefinition Definition;
        public Vector3 HomePosition;
        public Quaternion HomeRotation;
        public NPC ActiveNpc;
        public SpawnSlotState State;
        public double NextRespawnAt;
        public Coroutine RespawnCoroutine;
    }

    private const string WalkableAreaName = "Walkable";
    private const string WaterLayerName = "Water";
    private const int MaxSpawnSampleAttempts = 24;
    private const float WaterSurfaceProbeHeight = 100f;
    private const float WaterSurfaceProbeDistance = 300f;
    private const float WaterSurfaceYTolerance = 1.5f;

    public static NPCSpawner Instance { get; private set; }

    [Header("Spawn Settings")]
    [FormerlySerializedAs("npcPrefab")]
    [SerializeField] private GameObject npcNetworkPrefab;
    [SerializeField] private List<NpcDefinition> npcDefinitions = new();
    [SerializeField] private int spawnCount = 3;
    [SerializeField] private float spawnRadius = 50f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 3f;
    [SerializeField] private Transform spawnCenter;
    [SerializeField] private bool preferAuthoredSpawnPoints = true;
    [SerializeField] private bool includeChildSpawnPoints = true;
    [SerializeField] private List<NpcSpawnPoint> authoredSpawnPoints = new();
    [SerializeField, Min(0f)] private float additionalWaterlineOffset = 0.1f;

    [Header("Respawn Settings")]
    [SerializeField, Min(0.5f)] private float defaultRespawnDelaySeconds = 20f;
    [SerializeField, Min(0f)] private float defaultCorpseLifetimeSeconds = 2f;
    [SerializeField, Min(0f)] private float respawnJitterSeconds = 2f;
    [SerializeField, Min(0.25f)] private float respawnRetryIntervalSeconds = 2f;
    [SerializeField, Min(0f)] private float respawnBlockedDistance = 50f;
    [SerializeField, Min(0f)] private float respawnPositionJitterRadius = 12f;
    [SerializeField, Min(0f)] private float respawnClearanceRadius = 10f;

    private int walkableAreaMask;
    private int waterLayer = -1;
    private readonly RaycastHit[] waterProbeHits = new RaycastHit[8];
    private readonly List<Renderer> waterSurfaceRenderers = new();
    private readonly Dictionary<int, SpawnSlotRuntime> spawnSlots = new();
    private GameObject resolvedNpcNetworkPrefab;
    private bool waterSurfaceDataCached;
    private float cachedWaterSurfaceY = float.NaN;
    private bool definitionsRegistered;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("NPCSpawner: Multiple instances detected. Legacy Instance access will point at the most recently initialized spawner. Use owner-spawner references or NpcDefinitionRegistry for multi-map-safe behavior.");
        }

        Instance = this;
        RegisterDefinitions();
    }

    private void OnEnable()
    {
        RegisterDefinitions();
    }

    public override void OnDestroy()
    {
        UnregisterDefinitions();
        ClearSpawnSlots();

        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    private void OnDisable()
    {
        UnregisterDefinitions();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer)
        {
            return;
        }

        int walkableArea = NavMesh.GetAreaFromName(WalkableAreaName);
        walkableAreaMask = walkableArea >= 0 ? 1 << walkableArea : NavMesh.AllAreas;
        if (walkableArea < 0)
        {
            Debug.LogWarning($"NPCSpawner: NavMesh area '{WalkableAreaName}' was not found. Falling back to all NavMesh areas.");
        }

        waterLayer = LayerMask.NameToLayer(WaterLayerName);
        if (waterLayer < 0)
        {
            Debug.LogWarning($"NPCSpawner: Layer '{WaterLayerName}' was not found. Water surface validation will be skipped.");
        }

        CacheWaterSurfaceData();

        if (!TryResolveNpcNetworkPrefab(out resolvedNpcNetworkPrefab))
        {
            Debug.LogError("NPCSpawner: Could not resolve a valid npcNetworkPrefab. Assign a prefab with NPC + NetworkObject components, or register one in NetworkManager NetworkPrefabs.");
            return;
        }

        InitializeSpawnSlots();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            ClearSpawnSlots();
        }

        base.OnNetworkDespawn();
    }

    public void NotifyNpcDeath(NPC npc)
    {
        if (!IsServer || npc == null)
        {
            return;
        }

        if (!spawnSlots.TryGetValue(npc.SpawnSlotId, out SpawnSlotRuntime slot))
        {
            StartCoroutine(DespawnNpcAfterDelay(npc, 0.1f));
            return;
        }

        if (slot.ActiveNpc != npc)
        {
            return;
        }

        slot.ActiveNpc = null;
        slot.State = SpawnSlotState.Corpse;
        slot.NextRespawnAt = GetServerTime() + ResolveRespawnDelay(slot);

        if (slot.RespawnCoroutine != null)
        {
            StopCoroutine(slot.RespawnCoroutine);
        }

        slot.RespawnCoroutine = StartCoroutine(RespawnSlotLoop(slot, npc));
    }

    public NpcDefinition ResolveDefinitionByIndex(int index)
    {
        if (npcDefinitions == null)
        {
            return null;
        }

        if (index < 0 || index >= npcDefinitions.Count)
        {
            return null;
        }

        return npcDefinitions[index];
    }

    public int GetDefinitionIndex(NpcDefinition definition)
    {
        if (definition == null || npcDefinitions == null)
        {
            return -1;
        }

        for (int i = 0; i < npcDefinitions.Count; i++)
        {
            if (npcDefinitions[i] == definition)
            {
                return i;
            }
        }

        return -1;
    }

    private void InitializeSpawnSlots()
    {
        ClearSpawnSlots();

        if (TryInitializeAuthoredSpawnSlots())
        {
            return;
        }

        Vector3 center = spawnCenter != null ? spawnCenter.position : transform.position;
        int desiredSpawnCount = Mathf.Max(0, spawnCount);

        for (int i = 0; i < desiredSpawnCount; i++)
        {
            if (!TryGetRandomNavMeshPosition(center, spawnRadius, MaxSpawnSampleAttempts, out Vector3 homePosition))
            {
                Debug.LogWarning($"NPCSpawner: Could not find valid spawn position for slot {i + 1}.");
                continue;
            }

            bool hasDefinition = TryResolveDefinitionForSpawn(out int definitionIndex, out NpcDefinition definition);
            Quaternion homeRotation = spawnCenter != null
                ? spawnCenter.rotation
                : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var slot = new SpawnSlotRuntime
            {
                SlotId = i,
                StableId = $"random-slot-{i}",
                DefinitionIndex = hasDefinition ? definitionIndex : -1,
                Definition = definition,
                HomePosition = homePosition,
                HomeRotation = homeRotation,
                State = SpawnSlotState.Uninitialized,
            };

            spawnSlots[slot.SlotId] = slot;

            if (!TrySpawnSlot(slot, ignorePlayerClearance: true))
            {
                slot.State = SpawnSlotState.PendingRespawn;
                slot.NextRespawnAt = GetServerTime() + Mathf.Max(0.25f, respawnRetryIntervalSeconds);
                slot.RespawnCoroutine = StartCoroutine(RespawnSlotLoop(slot, null));
            }
        }
    }

    private bool TryInitializeAuthoredSpawnSlots()
    {
        if (!preferAuthoredSpawnPoints)
        {
            return false;
        }

        List<NpcSpawnPoint> configuredSpawnPoints = CollectConfiguredSpawnPoints();
        if (configuredSpawnPoints.Count == 0)
        {
            return false;
        }

        var seenStableIds = new HashSet<string>();

        for (int i = 0; i < configuredSpawnPoints.Count; i++)
        {
            NpcSpawnPoint spawnPoint = configuredSpawnPoints[i];
            if (spawnPoint == null)
            {
                continue;
            }

            if (!TrySampleNavMeshPosition(spawnPoint.transform.position, out Vector3 homePosition))
            {
                Debug.LogWarning($"NPCSpawner: Authored spawn point '{spawnPoint.name}' is not on a valid water NavMesh location.");
                continue;
            }

            bool hasDefinition = TryResolveDefinitionForSpawnPoint(spawnPoint, out int definitionIndex, out NpcDefinition definition);
            string stableId = string.IsNullOrWhiteSpace(spawnPoint.StableId)
                ? $"spawn-point-{i}"
                : spawnPoint.StableId.Trim();

            if (!seenStableIds.Add(stableId))
            {
                Debug.LogWarning($"NPCSpawner: Duplicate NPC spawn stable ID '{stableId}' detected. Using authored order as the runtime tie-breaker.");
            }

            var slot = new SpawnSlotRuntime
            {
                SlotId = i,
                StableId = stableId,
                SpawnPoint = spawnPoint,
                DefinitionIndex = hasDefinition ? definitionIndex : -1,
                Definition = definition,
                HomePosition = homePosition,
                HomeRotation = spawnPoint.transform.rotation,
                State = SpawnSlotState.Uninitialized,
            };

            spawnSlots[slot.SlotId] = slot;

            if (!TrySpawnSlot(slot, ignorePlayerClearance: true))
            {
                slot.State = SpawnSlotState.PendingRespawn;
                slot.NextRespawnAt = GetServerTime() + Mathf.Max(0.25f, respawnRetryIntervalSeconds);
                slot.RespawnCoroutine = StartCoroutine(RespawnSlotLoop(slot, null));
            }
        }

        return spawnSlots.Count > 0;
    }

    private List<NpcSpawnPoint> CollectConfiguredSpawnPoints()
    {
        var configuredSpawnPoints = new List<NpcSpawnPoint>();
        var seen = new HashSet<NpcSpawnPoint>();

        if (authoredSpawnPoints != null)
        {
            for (int i = 0; i < authoredSpawnPoints.Count; i++)
            {
                NpcSpawnPoint spawnPoint = authoredSpawnPoints[i];
                if (spawnPoint != null && seen.Add(spawnPoint))
                {
                    configuredSpawnPoints.Add(spawnPoint);
                }
            }
        }

        if (includeChildSpawnPoints)
        {
            NpcSpawnPoint[] childSpawnPoints = GetComponentsInChildren<NpcSpawnPoint>(true);
            for (int i = 0; i < childSpawnPoints.Length; i++)
            {
                NpcSpawnPoint spawnPoint = childSpawnPoints[i];
                if (spawnPoint != null && seen.Add(spawnPoint))
                {
                    configuredSpawnPoints.Add(spawnPoint);
                }
            }
        }

        return configuredSpawnPoints;
    }

    private bool TrySpawnSlot(SpawnSlotRuntime slot, bool ignorePlayerClearance = false)
    {
        if (slot == null)
        {
            return false;
        }

        if (resolvedNpcNetworkPrefab == null && !TryResolveNpcNetworkPrefab(out resolvedNpcNetworkPrefab))
        {
            return false;
        }

        if (!TryResolveRespawnPosition(slot, out Vector3 spawnPosition, ignorePlayerClearance))
        {
            Debug.LogWarning($"NPCSpawner: Failed to resolve a valid respawn position for slot {slot.SlotId}.");
            return false;
        }

        GameObject npc = Instantiate(resolvedNpcNetworkPrefab, spawnPosition, slot.HomeRotation);
        SceneManager.MoveGameObjectToScene(npc, gameObject.scene);
        if (!npc.TryGetComponent(out NPC npcComponent) || !npc.TryGetComponent(out NetworkObject networkObject))
        {
            Debug.LogError("NPCSpawner: NPC prefab must contain NPC and NetworkObject components.");
            Destroy(npc);
            return false;
        }

        npcComponent.BindSpawnSlot(this, slot.SlotId, slot.HomePosition, slot.HomeRotation);
        if (WorldMapManager.Instance != null &&
            WorldMapManager.Instance.TryGetMapId(this, out string mapId))
        {
            npcComponent.SetWorldMapIdFromServer(mapId);
        }

        ApplyWaterlineOffset(npc, spawnPosition);

        networkObject.Spawn();
        npcComponent.SetDefinitionFromServer(slot.DefinitionIndex, slot.Definition);

        slot.ActiveNpc = npcComponent;
        slot.State = SpawnSlotState.Alive;
        slot.NextRespawnAt = 0d;
        slot.RespawnCoroutine = null;

        Debug.Log($"NPCSpawner: Spawned slot {slot.SlotId} ({slot.StableId}) at {spawnPosition}.");
        return true;
    }

    private IEnumerator RespawnSlotLoop(SpawnSlotRuntime slot, NPC deadNpc)
    {
        if (slot == null)
        {
            yield break;
        }

        float corpseLifetime = ResolveCorpseLifetime(slot);
        if (deadNpc != null && corpseLifetime > 0f)
        {
            yield return new WaitForSeconds(corpseLifetime);
        }

        if (deadNpc != null)
        {
            yield return DespawnNpcAfterDelay(deadNpc, 0f);
        }

        slot.State = SpawnSlotState.PendingRespawn;

        while (IsServer && IsSpawned)
        {
            double secondsUntilRespawn = slot.NextRespawnAt - GetServerTime();
            if (secondsUntilRespawn > 0d)
            {
                yield return new WaitForSeconds(Mathf.Min((float)secondsUntilRespawn, 0.5f));
                continue;
            }

            if (IsRespawnBlockedByPlayers(slot) || !TrySpawnSlot(slot))
            {
                slot.NextRespawnAt = GetServerTime() + Mathf.Max(0.25f, respawnRetryIntervalSeconds);
                yield return new WaitForSeconds(Mathf.Min(respawnRetryIntervalSeconds, 0.5f));
                continue;
            }

            yield break;
        }

        slot.RespawnCoroutine = null;
    }

    private IEnumerator DespawnNpcAfterDelay(NPC npc, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (npc == null)
        {
            yield break;
        }

        NetworkObject networkObject = npc.NetworkObject;
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn();
        }
    }

    private bool TryResolveNpcNetworkPrefab(out GameObject resolvedPrefab)
    {
        resolvedPrefab = npcNetworkPrefab;

        if (IsValidNpcNetworkPrefab(resolvedPrefab, false))
        {
            return EnsureNetworkPrefabRegistered(resolvedPrefab);
        }

        if (resolvedPrefab != null)
        {
            Debug.LogWarning($"NPCSpawner: Assigned npcNetworkPrefab '{resolvedPrefab.name}' is not a valid network NPC prefab. Falling back to registered NPC prefabs.");
        }

        resolvedPrefab = FindRegisteredNpcNetworkPrefab();
        if (resolvedPrefab == null)
        {
            return false;
        }

        npcNetworkPrefab = resolvedPrefab;
        Debug.LogWarning($"NPCSpawner: Falling back to registered NPC network prefab '{resolvedPrefab.name}'.");
        return true;
    }

    private bool EnsureNetworkPrefabRegistered(GameObject prefab)
    {
        if (prefab == null)
        {
            return false;
        }

        NetworkManager activeNetworkManager = NetworkManager != null ? NetworkManager : NetworkManager.Singleton;
        if (activeNetworkManager == null)
        {
            Debug.LogError("NPCSpawner: NetworkManager is not available.");
            return false;
        }

        if (activeNetworkManager.NetworkConfig.Prefabs.Contains(prefab))
        {
            return true;
        }

        bool added = activeNetworkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab
        {
            Prefab = prefab
        });

        if (!added)
        {
            Debug.LogError($"NPCSpawner: '{prefab.name}' is not registered in NetworkManager NetworkPrefabs and could not be auto-registered.");
            return false;
        }

        Debug.LogWarning($"NPCSpawner: Auto-registered '{prefab.name}' in NetworkManager NetworkPrefabs.");
        return true;
    }

    private GameObject FindRegisteredNpcNetworkPrefab()
    {
        NetworkManager activeNetworkManager = NetworkManager != null ? NetworkManager : NetworkManager.Singleton;
        if (activeNetworkManager == null)
        {
            return null;
        }

        IReadOnlyList<NetworkPrefab> registeredPrefabs = activeNetworkManager.NetworkConfig.Prefabs.Prefabs;
        for (int i = 0; i < registeredPrefabs.Count; i++)
        {
            GameObject candidate = registeredPrefabs[i]?.Prefab;
            if (IsValidNpcNetworkPrefab(candidate, false))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsValidNpcNetworkPrefab(GameObject prefab, bool logWarnings)
    {
        if (prefab == null)
        {
            return false;
        }

        bool hasNetworkObject = prefab.TryGetComponent(out NetworkObject _);
        bool hasNpc = prefab.TryGetComponent(out NPC _);

        if (logWarnings && !hasNetworkObject)
        {
            Debug.LogWarning($"NPCSpawner: '{prefab.name}' is missing NetworkObject.");
        }

        if (logWarnings && !hasNpc)
        {
            Debug.LogWarning($"NPCSpawner: '{prefab.name}' is missing NPC.");
        }

        return hasNetworkObject && hasNpc;
    }

    private bool TryResolveDefinitionForSpawn(out int definitionIndex, out NpcDefinition definition)
    {
        definitionIndex = -1;
        definition = null;

        if (npcDefinitions == null)
        {
            return false;
        }

        int availableCount = 0;
        for (int i = 0; i < npcDefinitions.Count; i++)
        {
            if (npcDefinitions[i] != null)
            {
                availableCount++;
            }
        }

        if (availableCount == 0)
        {
            return false;
        }

        int target = Random.Range(0, availableCount);
        for (int i = 0; i < npcDefinitions.Count; i++)
        {
            NpcDefinition candidate = npcDefinitions[i];
            if (candidate == null)
            {
                continue;
            }

            if (target == 0)
            {
                definitionIndex = i;
                definition = candidate;
                return true;
            }

            target--;
        }

        return false;
    }

    private bool TryResolveDefinitionForSpawnPoint(NpcSpawnPoint spawnPoint, out int definitionIndex, out NpcDefinition definition)
    {
        definitionIndex = -1;
        definition = null;

        if (spawnPoint != null && spawnPoint.DefinitionOverride != null)
        {
            int overrideIndex = GetDefinitionIndex(spawnPoint.DefinitionOverride);
            if (overrideIndex >= 0)
            {
                definitionIndex = overrideIndex;
                definition = spawnPoint.DefinitionOverride;
                return true;
            }

            Debug.LogWarning($"NPCSpawner: Spawn point '{spawnPoint.name}' references definition '{spawnPoint.DefinitionOverride.name}' which is not in npcDefinitions. Falling back to weighted definitions.");
        }

        return TryResolveDefinitionForSpawn(out definitionIndex, out definition);
    }

    private bool TryResolveRespawnPosition(SpawnSlotRuntime slot, out Vector3 spawnPosition, bool ignorePlayerClearance = false)
    {
        spawnPosition = default;
        if (slot == null)
        {
            return false;
        }

        float jitterRadius = ResolveRespawnJitterRadius(slot);
        if (jitterRadius > 0f &&
            TryGetRandomNavMeshPosition(slot.HomePosition, jitterRadius, MaxSpawnSampleAttempts / 2, out spawnPosition) &&
            IsRespawnLocationClear(spawnPosition, ignorePlayerClearance))
        {
            return true;
        }

        if (TrySampleNavMeshPosition(slot.HomePosition, out spawnPosition) && IsRespawnLocationClear(spawnPosition, ignorePlayerClearance))
        {
            return true;
        }

        return false;
    }

    private bool TryGetRandomNavMeshPosition(Vector3 center, float radius, int maxAttempts, out Vector3 spawnPosition)
    {
        float sampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
        int attempts = Mathf.Max(1, maxAttempts);
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Vector2 planarOffset = Random.insideUnitCircle * Mathf.Max(0f, radius);
            Vector3 randomDirection = center + new Vector3(planarOffset.x, 0f, planarOffset.y);
            if (TryGetWaterSurfaceY(randomDirection, out float waterSurfaceY))
            {
                randomDirection.y = waterSurfaceY;
            }
            else
            {
                randomDirection.y = center.y;
            }

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomDirection, out hit, sampleDistance, walkableAreaMask) &&
                IsPointOnWaterSurface(hit.position))
            {
                spawnPosition = hit.position;
                return true;
            }
        }

        spawnPosition = default;
        return false;
    }

    private bool TrySampleNavMeshPosition(Vector3 desiredPosition, out Vector3 sampledPosition)
    {
        NavMeshHit hit;
        float sampleDistance = Mathf.Max(0.1f, navMeshSampleDistance);
        Vector3 sampleOrigin = desiredPosition;
        if (TryGetWaterSurfaceY(desiredPosition, out float waterSurfaceY))
        {
            sampleOrigin.y = waterSurfaceY;
            sampleDistance = Mathf.Max(sampleDistance, Mathf.Abs(desiredPosition.y - waterSurfaceY) + 0.5f);
        }

        if (NavMesh.SamplePosition(sampleOrigin, out hit, sampleDistance, walkableAreaMask) &&
            IsPointOnWaterSurface(hit.position))
        {
            sampledPosition = hit.position;
            return true;
        }

        sampledPosition = default;
        return false;
    }

    private void CacheWaterSurfaceData()
    {
        waterSurfaceDataCached = true;
        waterSurfaceRenderers.Clear();
        cachedWaterSurfaceY = float.NaN;

        if (waterLayer < 0)
        {
            return;
        }

        Renderer[] sceneRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        float largestSurfaceArea = -1f;

        for (int i = 0; i < sceneRenderers.Length; i++)
        {
            Renderer renderer = sceneRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (renderer.gameObject.layer != waterLayer)
            {
                continue;
            }

            waterSurfaceRenderers.Add(renderer);

            Bounds bounds = renderer.bounds;
            float surfaceArea = bounds.size.x * bounds.size.z;
            if (surfaceArea > largestSurfaceArea)
            {
                largestSurfaceArea = surfaceArea;
                cachedWaterSurfaceY = bounds.center.y;
            }
        }
    }

    private bool TryGetWaterSurfaceY(Vector3 point, out float waterSurfaceY)
    {
        waterSurfaceY = default;

        if (waterLayer < 0)
        {
            return false;
        }

        if (!waterSurfaceDataCached)
        {
            CacheWaterSurfaceData();
        }

        float closestVerticalDelta = float.MaxValue;
        bool found = false;

        for (int i = 0; i < waterSurfaceRenderers.Count; i++)
        {
            Renderer renderer = waterSurfaceRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            if (!IsPointWithinWaterBounds(point, bounds))
            {
                continue;
            }

            float candidateY = bounds.center.y;
            float verticalDelta = Mathf.Abs(point.y - candidateY);
            if (!found || verticalDelta < closestVerticalDelta)
            {
                found = true;
                closestVerticalDelta = verticalDelta;
                waterSurfaceY = candidateY;
            }
        }

        if (found)
        {
            return true;
        }

        if (!float.IsNaN(cachedWaterSurfaceY))
        {
            waterSurfaceY = cachedWaterSurfaceY;
            return true;
        }

        return false;
    }

    private static bool IsPointWithinWaterBounds(Vector3 point, Bounds bounds)
    {
        const float BoundsPadding = 0.5f;
        return point.x >= bounds.min.x - BoundsPadding &&
               point.x <= bounds.max.x + BoundsPadding &&
               point.z >= bounds.min.z - BoundsPadding &&
               point.z <= bounds.max.z + BoundsPadding;
    }

    private bool IsPointOnWaterSurface(Vector3 point)
    {
        if (waterLayer < 0)
        {
            return true;
        }

        Vector3 origin = point + Vector3.up * WaterSurfaceProbeHeight;
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            waterProbeHits,
            WaterSurfaceProbeDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
        {
            return false;
        }

        int nearestHitIndex = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            if (waterProbeHits[i].collider == null)
            {
                continue;
            }

            float distance = waterProbeHits[i].distance;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestHitIndex = i;
            }
        }

        if (nearestHitIndex >= 0 &&
            waterProbeHits[nearestHitIndex].collider != null &&
            waterProbeHits[nearestHitIndex].collider.gameObject.layer == waterLayer)
        {
            return true;
        }

        if (TryGetWaterSurfaceY(point, out float waterSurfaceY))
        {
            return Mathf.Abs(point.y - waterSurfaceY) <= WaterSurfaceYTolerance;
        }

        return false;
    }

    private bool IsRespawnLocationClear(Vector3 spawnPosition, bool ignorePlayers = false)
    {
        if (respawnClearanceRadius <= 0f)
        {
            return true;
        }

        float clearanceSqrDistance = respawnClearanceRadius * respawnClearanceRadius;
        if (!ignorePlayers && PlayerManager.Instance != null)
        {
            List<Player> players = PlayerManager.Instance.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players[i];
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
            if (slot.ActiveNpc == null || !slot.ActiveNpc.IsSpawned)
            {
                continue;
            }

            if ((slot.ActiveNpc.transform.position - spawnPosition).sqrMagnitude <= clearanceSqrDistance)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsRespawnBlockedByPlayers(SpawnSlotRuntime slot)
    {
        float blockedDistance = ResolveRespawnBlockedDistance(slot);
        if (slot == null || blockedDistance <= 0f || PlayerManager.Instance == null)
        {
            return false;
        }

        float blockedSqrDistance = blockedDistance * blockedDistance;
        List<Player> players = PlayerManager.Instance.GetAllPlayers();
        for (int i = 0; i < players.Count; i++)
        {
            Player player = players[i];
            if (player == null || !player.IsSpawned || player.IsDead)
            {
                continue;
            }

            if (!WorldMapSceneActivityUtility.IsRelevantPlayerForScopedMap(this, player))
            {
                continue;
            }

            if ((player.transform.position - slot.HomePosition).sqrMagnitude <= blockedSqrDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyWaterlineOffset(GameObject npc, Vector3 navMeshSpawnPosition)
    {
        if (npc == null || !npc.TryGetComponent(out NavMeshAgent navMeshAgent))
        {
            return;
        }

        float waterSurfaceY = navMeshSpawnPosition.y;
        if (TryGetWaterSurfaceY(navMeshSpawnPosition, out float resolvedWaterSurfaceY))
        {
            waterSurfaceY = resolvedWaterSurfaceY;
        }

        float navMeshToWaterDelta = waterSurfaceY - navMeshSpawnPosition.y;
        float desiredBaseOffset = navMeshAgent.baseOffset + navMeshToWaterDelta + additionalWaterlineOffset;
        navMeshAgent.baseOffset = Mathf.Max(navMeshAgent.baseOffset, desiredBaseOffset);

        Vector3 correctedPosition = navMeshSpawnPosition;
        correctedPosition.y = navMeshSpawnPosition.y + navMeshAgent.baseOffset;
        npc.transform.position = correctedPosition;
    }

    private double GetServerTime()
    {
        return NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    }

    private float ResolveRespawnDelay(SpawnSlotRuntime slot)
    {
        float baseDelay = Mathf.Max(0.5f, defaultRespawnDelaySeconds);
        if (slot != null)
        {
            if (slot.SpawnPoint != null && slot.SpawnPoint.TryGetRespawnDelayOverride(out float overrideDelay))
            {
                baseDelay = overrideDelay;
            }
            else if (slot.Definition != null)
            {
                baseDelay = slot.Definition.RespawnDelaySeconds;
            }
        }

        if (respawnJitterSeconds > 0f)
        {
            baseDelay += Random.Range(0f, respawnJitterSeconds);
        }

        return Mathf.Max(0.5f, baseDelay);
    }

    private float ResolveCorpseLifetime(SpawnSlotRuntime slot)
    {
        float corpseLifetime = Mathf.Max(0f, defaultCorpseLifetimeSeconds);
        if (slot != null)
        {
            if (slot.SpawnPoint != null && slot.SpawnPoint.TryGetCorpseLifetimeOverride(out float overrideLifetime))
            {
                corpseLifetime = overrideLifetime;
            }
            else if (slot.Definition != null)
            {
                corpseLifetime = slot.Definition.CorpseLifetimeSeconds;
            }
        }

        return Mathf.Max(0f, corpseLifetime);
    }

    private float ResolveRespawnBlockedDistance(SpawnSlotRuntime slot)
    {
        if (slot != null &&
            slot.SpawnPoint != null &&
            slot.SpawnPoint.TryGetRespawnBlockedDistanceOverride(out float overrideBlockedDistance))
        {
            return overrideBlockedDistance;
        }

        return Mathf.Max(0f, respawnBlockedDistance);
    }

    private float ResolveRespawnJitterRadius(SpawnSlotRuntime slot)
    {
        if (slot != null &&
            slot.SpawnPoint != null &&
            slot.SpawnPoint.TryGetRespawnJitterRadiusOverride(out float overrideJitterRadius))
        {
            return overrideJitterRadius;
        }

        return Mathf.Max(0f, respawnPositionJitterRadius);
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

            slot.ActiveNpc = null;
        }

        spawnSlots.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = spawnCenter != null ? spawnCenter.position : transform.position;

        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(center, spawnRadius);

        Gizmos.color = new Color(0f, 1f, 1f, 0.1f);
        Gizmos.DrawSphere(center, 1f);
    }

    private void OnValidate()
    {
        RegisterDefinitions();

        if (npcNetworkPrefab == null)
        {
            return;
        }

        IsValidNpcNetworkPrefab(npcNetworkPrefab, true);
    }

    private void RegisterDefinitions()
    {
        if (definitionsRegistered)
        {
            return;
        }

        NpcDefinitionRegistry.Register(npcDefinitions);
        definitionsRegistered = true;
    }

    private void UnregisterDefinitions()
    {
        if (!definitionsRegistered)
        {
            return;
        }

        NpcDefinitionRegistry.Unregister(npcDefinitions);
        definitionsRegistered = false;
    }
}
