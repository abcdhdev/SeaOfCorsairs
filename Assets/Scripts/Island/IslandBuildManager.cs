using System;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-200)]
public sealed class IslandBuildManager : MonoBehaviour
{
    private const string TurretResourcePath = "Island/Turret";
    private const float PlacementY = 15f;
    private const int TurretGoldCost = 100;
    private const int MaxOwnedTurrets = 6;
    private const float MinimumTurretSpacing = 26f;
    private const float FoundationInnerRadius = 14f;
    private const float FoundationOuterRadius = 22f;
    private const float TerrainRebuildDebounceSeconds = 0.08f;

    private enum PlacementMode
    {
        None,
        Build,
        Move
    }

    public static IslandBuildManager Instance { get; private set; }

    private PlacementMode placementMode;
    private GameObject turretPrefab;
    private GameObject previewInstance;
    private Renderer[] previewRenderers = Array.Empty<Renderer>();
    private MaterialPropertyBlock previewPropertyBlock;
    private bool previewValid;
    private Vector3 previewPosition;
    private ulong movingTurretId;
    private bool networkPrefabRegistered;
    private string statusMessage = "Shield button opens guild management.";
    private Player observedLocalPlayer;
    private Terrain cachedTerrain;
    private NavMeshSurface cachedNavMeshSurface;
    private float[,] baseHeights;
    private bool terrainDirty;
    private float terrainRebuildReadyAt;

    public bool IsPlacementActive => placementMode != PlacementMode.None;
    public string StatusMessage => statusMessage;
    public int TurretCost => TurretGoldCost;
    public int MaxTurrets => MaxOwnedTurrets;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeInstance()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject managerObject = new GameObject(nameof(IslandBuildManager));
        managerObject.AddComponent<IslandBuildManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        previewPropertyBlock = new MaterialPropertyBlock();
        LoadTurretPrefab();
    }

    private void OnEnable()
    {
        IslandTurret.RegistryChanged += HandleTurretRegistryChanged;
        Player.LocalPlayerSpawned += HandleLocalPlayerSpawned;
    }

    private void OnDisable()
    {
        IslandTurret.RegistryChanged -= HandleTurretRegistryChanged;
        Player.LocalPlayerSpawned -= HandleLocalPlayerSpawned;
        UntrackObservedLocalPlayer();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        LoadTurretPrefab();
        EnsureNetworkPrefabRegistered();
        TrackObservedLocalPlayer();
        UpdatePlacementPreview();
        RebuildTerrainIfNeeded();
    }

    public int GetLocalOwnedTurretCount()
    {
        ulong localClientId = GetLocalClientId();
        return localClientId == ulong.MaxValue ? 0 : IslandTurret.CountOwnedBy(localClientId);
    }

    public IslandTurret GetSelectedTurret()
    {
        if (SelectObject.Instance == null || SelectObject.Instance.SelectedTarget == null)
        {
            return null;
        }

        return SelectObject.Instance.SelectedTarget.GetComponent<IslandTurret>();
    }

    public IslandTurret GetSelectedOwnedTurret()
    {
        IslandTurret selectedTurret = GetSelectedTurret();
        if (selectedTurret == null)
        {
            return null;
        }

        ulong localClientId = GetLocalClientId();
        return selectedTurret.IsOwnedBy(localClientId) ? selectedTurret : null;
    }

    public bool BeginBuildPlacement()
    {
        if (!CanLoadTurretPrefab())
        {
            SetStatus("Turret prefab could not be loaded.");
            return false;
        }

        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            SetStatus("Your player ship is not ready yet.");
            return false;
        }

        if (localPlayer.Gold < TurretGoldCost)
        {
            SetStatus($"You need {TurretGoldCost} gold to build a turret.");
            return false;
        }

        if (GetLocalOwnedTurretCount() >= MaxOwnedTurrets)
        {
            SetStatus($"You can only build {MaxOwnedTurrets} turrets.");
            return false;
        }

        StartPlacement(PlacementMode.Build, 0);
        SetStatus("Move the cursor over the water and click to place your island turret.");
        return true;
    }

    public bool BeginMovePlacement(IslandTurret turret)
    {
        if (turret == null)
        {
            SetStatus("Select one of your turrets before moving it.");
            return false;
        }

        ulong localClientId = GetLocalClientId();
        if (!turret.IsOwnedBy(localClientId))
        {
            SetStatus("You can only move your own turrets.");
            return false;
        }

        StartPlacement(PlacementMode.Move, turret.NetworkObjectId);
        previewPosition = turret.transform.position;
        SetStatus("Click a new location to move the selected turret.");
        return true;
    }

    public bool DeleteSelectedTurret()
    {
        IslandTurret selectedTurret = GetSelectedOwnedTurret();
        if (selectedTurret == null)
        {
            SetStatus("Select one of your turrets before deleting it.");
            return false;
        }

        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            SetStatus("Your player ship is not ready yet.");
            return false;
        }

        if (!localPlayer.RequestDeleteTurret(selectedTurret.NetworkObjectId))
        {
            SetStatus("Unable to send the delete request.");
            return false;
        }

        SetStatus("Delete request sent.");
        return true;
    }

    public void CancelPlacement(string reason = null)
    {
        placementMode = PlacementMode.None;
        movingTurretId = 0;
        previewValid = false;
        SetPreviewVisible(false);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            SetStatus(reason);
        }
    }

    public bool TryHandleGameplayClick(Ray ray)
    {
        _ = ray;
        if (!IsPlacementActive)
        {
            return false;
        }

        if (!previewValid)
        {
            SetStatus("Choose a clear build spot above the water.");
            return true;
        }

        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            SetStatus("Your player ship is not ready yet.");
            CancelPlacement();
            return true;
        }

        bool requestSent = placementMode switch
        {
            PlacementMode.Build => localPlayer.RequestBuildTurret(previewPosition),
            PlacementMode.Move => localPlayer.RequestMoveTurret(movingTurretId, previewPosition),
            _ => false
        };

        if (requestSent)
        {
            SetStatus(placementMode == PlacementMode.Build
                ? "Build request sent."
                : "Move request sent.");
        }
        else
        {
            SetStatus("Unable to send the build request.");
        }

        CancelPlacement();
        return true;
    }

    public void MarkTerrainDirty()
    {
        terrainDirty = true;
        terrainRebuildReadyAt = Time.unscaledTime + TerrainRebuildDebounceSeconds;
    }

    public bool TryServerBuildTurret(Player owner, Vector3 requestedPosition, out string resultMessage)
    {
        resultMessage = string.Empty;

        if (!ValidateServerBuildRequest(owner, requestedPosition, out Vector3 resolvedPosition, out resultMessage))
        {
            return false;
        }

        if (!owner.TrySpendGold(TurretGoldCost))
        {
            resultMessage = $"You need {TurretGoldCost} gold to build a turret.";
            return false;
        }

        GameObject instance = Instantiate(turretPrefab, resolvedPosition, Quaternion.identity);
        IslandTurret turret = instance.GetComponent<IslandTurret>();
        if (turret == null)
        {
            Destroy(instance);
            resultMessage = "Turret prefab is missing the IslandTurret component.";
            return false;
        }

        turret.InitializeOwner(owner.OwnerClientId);
        turret.SetPlacementPosition(resolvedPosition);

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Destroy(instance);
            resultMessage = "Turret prefab is missing a NetworkObject.";
            return false;
        }

        networkObject.Spawn(true);
        MarkTerrainDirty();
        resultMessage = "Turret built.";
        return true;
    }

    public bool TryServerMoveTurret(Player owner, ulong turretNetworkObjectId, Vector3 requestedPosition, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (owner == null || !owner.IsServer)
        {
            resultMessage = "Only the server can move turrets.";
            return false;
        }

        if (!TryResolveOwnedTurret(owner, turretNetworkObjectId, out IslandTurret turret, out resultMessage))
        {
            return false;
        }

        if (!TrySanitizePlacementPosition(requestedPosition, turretNetworkObjectId, out Vector3 resolvedPosition, out resultMessage))
        {
            return false;
        }

        turret.SetPlacementPosition(resolvedPosition);
        MarkTerrainDirty();
        resultMessage = "Turret moved.";
        return true;
    }

    public bool TryServerDeleteTurret(Player owner, ulong turretNetworkObjectId, out string resultMessage)
    {
        resultMessage = string.Empty;
        if (owner == null || !owner.IsServer)
        {
            resultMessage = "Only the server can delete turrets.";
            return false;
        }

        if (!TryResolveOwnedTurret(owner, turretNetworkObjectId, out IslandTurret turret, out resultMessage))
        {
            return false;
        }

        NetworkObject networkObject = turret.NetworkObject;
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn(true);
        }
        else
        {
            Destroy(turret.gameObject);
        }

        MarkTerrainDirty();
        resultMessage = "Turret deleted.";
        return true;
    }

    private void HandleTurretRegistryChanged()
    {
        MarkTerrainDirty();
    }

    private void HandleLocalPlayerSpawned(Transform playerTransform)
    {
        _ = playerTransform;
        TrackObservedLocalPlayer();
    }

    private void TrackObservedLocalPlayer()
    {
        Player localPlayer = Player.LocalPlayer;
        if (observedLocalPlayer == localPlayer)
        {
            return;
        }

        UntrackObservedLocalPlayer();
        observedLocalPlayer = localPlayer;
        if (observedLocalPlayer != null)
        {
            observedLocalPlayer.OnIslandActionFeedback += HandleIslandActionFeedback;
        }
    }

    private void UntrackObservedLocalPlayer()
    {
        if (observedLocalPlayer == null)
        {
            return;
        }

        observedLocalPlayer.OnIslandActionFeedback -= HandleIslandActionFeedback;
        observedLocalPlayer = null;
    }

    private void HandleIslandActionFeedback(string message, bool success)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatus(message);
        }

        if (!success && IsPlacementActive)
        {
            SetPreviewVisible(true);
        }
    }

    private void LoadTurretPrefab()
    {
        if (turretPrefab == null)
        {
            turretPrefab = Resources.Load<GameObject>(TurretResourcePath);
        }
    }

    private bool CanLoadTurretPrefab()
    {
        LoadTurretPrefab();
        return turretPrefab != null;
    }

    private void EnsureNetworkPrefabRegistered()
    {
        if (networkPrefabRegistered || turretPrefab == null || NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.NetworkConfig.Prefabs.Contains(turretPrefab))
        {
            networkPrefabRegistered = true;
            return;
        }

        bool added = NetworkManager.Singleton.NetworkConfig.Prefabs.Add(new NetworkPrefab
        {
            Prefab = turretPrefab
        });

        if (!added)
        {
            Debug.LogWarning($"IslandBuildManager: Failed to register turret prefab '{turretPrefab.name}'.");
            return;
        }

        networkPrefabRegistered = true;
    }

    private void StartPlacement(PlacementMode newMode, ulong turretId)
    {
        placementMode = newMode;
        movingTurretId = turretId;
        previewValid = false;
        EnsurePreviewInstance();
    }

    private void EnsurePreviewInstance()
    {
        if (previewInstance != null || turretPrefab == null)
        {
            return;
        }

        previewInstance = Instantiate(turretPrefab);
        previewInstance.name = "IslandTurretPreview";

        DisablePreviewGameplayComponents(previewInstance);
        previewRenderers = previewInstance.GetComponentsInChildren<Renderer>(true);
        SetPreviewVisible(false);
        ApplyPreviewTint(isValid: true);
    }

    private static void DisablePreviewGameplayComponents(GameObject preview)
    {
        if (preview == null)
        {
            return;
        }

        foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (MonoBehaviour behaviour in preview.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is IslandTurret or Cannon or PlayerAttack or NetworkTransform)
            {
                behaviour.enabled = false;
            }
        }

        foreach (NetworkObject networkObject in preview.GetComponentsInChildren<NetworkObject>(true))
        {
            networkObject.enabled = false;
        }
    }

    private void UpdatePlacementPreview()
    {
        if (!IsPlacementActive)
        {
            return;
        }

        if (Pointer.current == null || !TryResolvePlacementPosition(out Vector3 resolvedPosition, out bool isValid))
        {
            previewValid = false;
            SetPreviewVisible(false);
            return;
        }

        previewPosition = resolvedPosition;
        previewValid = isValid;

        EnsurePreviewInstance();
        if (previewInstance == null)
        {
            return;
        }

        previewInstance.transform.position = previewPosition;
        previewInstance.transform.rotation = Quaternion.identity;
        SetPreviewVisible(true);
        ApplyPreviewTint(previewValid);
    }

    private bool TryResolvePlacementPosition(out Vector3 resolvedPosition, out bool isValid)
    {
        resolvedPosition = default;
        isValid = false;

        if (!TryGetPointerPosition(out Vector2 pointerPosition))
        {
            return false;
        }

        if (UIToolkitRaycastChecker.TryGetBlockingElementAtPointer(pointerPosition, out _))
        {
            return false;
        }

        Camera gameplayCamera = ResolveGameplayCamera();
        if (gameplayCamera == null)
        {
            return false;
        }

        Ray ray = gameplayCamera.ScreenPointToRay(pointerPosition);
        Plane placementPlane = new Plane(Vector3.up, new Vector3(0f, PlacementY, 0f));
        if (!placementPlane.Raycast(ray, out float distance))
        {
            return false;
        }

        resolvedPosition = ray.GetPoint(distance);
        resolvedPosition.y = PlacementY;
        isValid = IsPlacementWithinTerrainBounds(resolvedPosition) &&
                  IsPlacementPositionClear(resolvedPosition, movingTurretId);
        return true;
    }

    private bool ValidateServerBuildRequest(Player owner, Vector3 requestedPosition, out Vector3 resolvedPosition, out string resultMessage)
    {
        resolvedPosition = default;
        resultMessage = string.Empty;

        if (owner == null || !owner.IsServer)
        {
            resultMessage = "Only the server can build turrets.";
            return false;
        }

        if (turretPrefab == null)
        {
            resultMessage = "Turret prefab is unavailable.";
            return false;
        }

        if (IslandTurret.CountOwnedBy(owner.OwnerClientId) >= MaxOwnedTurrets)
        {
            resultMessage = $"You can only build {MaxOwnedTurrets} turrets.";
            return false;
        }

        if (!TrySanitizePlacementPosition(requestedPosition, 0, out resolvedPosition, out resultMessage))
        {
            return false;
        }

        return true;
    }

    private bool TryResolveOwnedTurret(Player owner, ulong turretNetworkObjectId, out IslandTurret turret, out string resultMessage)
    {
        turret = null;
        resultMessage = string.Empty;

        if (!IslandTurret.TryResolveOwnedTurret(turretNetworkObjectId, owner.OwnerClientId, out turret) || turret == null)
        {
            resultMessage = "That turret is no longer available.";
            return false;
        }

        return true;
    }

    private bool TrySanitizePlacementPosition(Vector3 requestedPosition, ulong ignoredTurretId, out Vector3 resolvedPosition, out string resultMessage)
    {
        resolvedPosition = requestedPosition;
        resolvedPosition.y = PlacementY;
        resultMessage = string.Empty;

        if (!IsPlacementWithinTerrainBounds(resolvedPosition))
        {
            resultMessage = "Turrets must be placed above the water and inside the terrain bounds.";
            return false;
        }

        if (!IsPlacementPositionClear(resolvedPosition, ignoredTurretId))
        {
            resultMessage = "That spot is too close to another turret.";
            return false;
        }

        return true;
    }

    private bool IsPlacementWithinTerrainBounds(Vector3 position)
    {
        Terrain terrain = GetTerrain();
        if (terrain == null)
        {
            return true;
        }

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;

        float minX = terrainPosition.x + FoundationOuterRadius;
        float maxX = terrainPosition.x + terrainSize.x - FoundationOuterRadius;
        float minZ = terrainPosition.z + FoundationOuterRadius;
        float maxZ = terrainPosition.z + terrainSize.z - FoundationOuterRadius;

        return position.x >= minX && position.x <= maxX &&
               position.z >= minZ && position.z <= maxZ;
    }

    private static bool IsPlacementPositionClear(Vector3 position, ulong ignoredTurretId)
    {
        float minimumSpacingSquared = MinimumTurretSpacing * MinimumTurretSpacing;
        foreach (IslandTurret turret in IslandTurret.ActiveTurrets)
        {
            if (turret == null || !turret.IsSpawned || turret.CurrentHealth <= 0)
            {
                continue;
            }

            if (ignoredTurretId != 0 &&
                turret.NetworkObject != null &&
                turret.NetworkObject.NetworkObjectId == ignoredTurretId)
            {
                continue;
            }

            Vector3 delta = turret.transform.position - position;
            delta.y = 0f;
            if (delta.sqrMagnitude < minimumSpacingSquared)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetPointerPosition(out Vector2 pointerPosition)
    {
        if (Pointer.current == null)
        {
            pointerPosition = Vector2.zero;
            return false;
        }

        pointerPosition = Pointer.current.position.ReadValue();
        return true;
    }

    private static Camera ResolveGameplayCamera()
    {
        if (Camera.main != null && Camera.main.enabled)
        {
            return Camera.main;
        }

        foreach (Camera candidate in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (candidate != null && candidate.enabled && candidate.cameraType == CameraType.Game)
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryGetLocalPlayer(out Player localPlayer)
    {
        localPlayer = Player.LocalPlayer;
        return localPlayer != null && localPlayer.IsOwner;
    }

    private ulong GetLocalClientId()
    {
        return NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClientId
            : ulong.MaxValue;
    }

    private void SetStatus(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            statusMessage = message;
        }
    }

    private void SetPreviewVisible(bool isVisible)
    {
        if (previewInstance != null)
        {
            previewInstance.SetActive(isVisible);
        }
    }

    private void ApplyPreviewTint(bool isValid)
    {
        if (previewRenderers == null || previewRenderers.Length == 0)
        {
            return;
        }

        Color tintColor = isValid
            ? new Color(0.55f, 1f, 0.65f, 0.65f)
            : new Color(1f, 0.45f, 0.45f, 0.65f);

        for (int index = 0; index < previewRenderers.Length; index++)
        {
            Renderer renderer = previewRenderers[index];
            if (renderer == null)
            {
                continue;
            }

            previewPropertyBlock.Clear();
            renderer.GetPropertyBlock(previewPropertyBlock);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                previewPropertyBlock.SetColor("_BaseColor", tintColor);
            }
            else if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_Color"))
            {
                previewPropertyBlock.SetColor("_Color", tintColor);
            }
            else if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_EmissionColor"))
            {
                previewPropertyBlock.SetColor("_EmissionColor", tintColor * 0.8f);
            }

            renderer.SetPropertyBlock(previewPropertyBlock);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private Terrain GetTerrain()
    {
        if (cachedTerrain == null)
        {
            cachedTerrain = Terrain.activeTerrain != null
                ? Terrain.activeTerrain
                : FindFirstObjectByType<Terrain>();
        }

        return cachedTerrain;
    }

    private NavMeshSurface GetNavMeshSurface()
    {
        if (cachedNavMeshSurface == null)
        {
            cachedNavMeshSurface = FindFirstObjectByType<NavMeshSurface>();
        }

        return cachedNavMeshSurface;
    }

    private void RebuildTerrainIfNeeded()
    {
        if (!terrainDirty || Time.unscaledTime < terrainRebuildReadyAt)
        {
            return;
        }

        terrainDirty = false;
        RebuildTerrainNow();
    }

    private void RebuildTerrainNow()
    {
        Terrain terrain = GetTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            return;
        }

        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        if (resolution <= 1)
        {
            return;
        }

        if (baseHeights == null ||
            baseHeights.GetLength(0) != resolution ||
            baseHeights.GetLength(1) != resolution)
        {
            baseHeights = terrainData.GetHeights(0, 0, resolution, resolution);
        }

        float[,] updatedHeights = new float[resolution, resolution];
        Array.Copy(baseHeights, updatedHeights, baseHeights.Length);

        foreach (IslandTurret turret in IslandTurret.ActiveTurrets)
        {
            if (turret == null || !turret.IsSpawned || turret.CurrentHealth <= 0)
            {
                continue;
            }

            ApplyFoundation(updatedHeights, terrain, turret.transform.position);
        }

        terrainData.SetHeights(0, 0, updatedHeights);
        terrain.Flush();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            NavMeshSurface navMeshSurface = GetNavMeshSurface();
            if (navMeshSurface != null && navMeshSurface.isActiveAndEnabled)
            {
                navMeshSurface.BuildNavMesh();
            }
        }
    }

    private static void ApplyFoundation(float[,] heights, Terrain terrain, Vector3 centerPosition)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPosition = terrain.transform.position;
        int resolution = terrainData.heightmapResolution;
        float normalizedTargetHeight = Mathf.Clamp01((PlacementY - terrainPosition.y) / Mathf.Max(terrainData.size.y, 0.01f));

        float minX = centerPosition.x - FoundationOuterRadius;
        float maxX = centerPosition.x + FoundationOuterRadius;
        float minZ = centerPosition.z - FoundationOuterRadius;
        float maxZ = centerPosition.z + FoundationOuterRadius;

        int startX = Mathf.Clamp(Mathf.FloorToInt(((minX - terrainPosition.x) / terrainData.size.x) * (resolution - 1)), 0, resolution - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt(((maxX - terrainPosition.x) / terrainData.size.x) * (resolution - 1)), 0, resolution - 1);
        int startZ = Mathf.Clamp(Mathf.FloorToInt(((minZ - terrainPosition.z) / terrainData.size.z) * (resolution - 1)), 0, resolution - 1);
        int endZ = Mathf.Clamp(Mathf.CeilToInt(((maxZ - terrainPosition.z) / terrainData.size.z) * (resolution - 1)), 0, resolution - 1);

        for (int z = startZ; z <= endZ; z++)
        {
            float worldZ = terrainPosition.z + (z / (float)(resolution - 1)) * terrainData.size.z;
            for (int x = startX; x <= endX; x++)
            {
                float worldX = terrainPosition.x + (x / (float)(resolution - 1)) * terrainData.size.x;
                float distance = Vector2.Distance(
                    new Vector2(worldX, worldZ),
                    new Vector2(centerPosition.x, centerPosition.z));

                if (distance > FoundationOuterRadius)
                {
                    continue;
                }

                float blend = distance <= FoundationInnerRadius
                    ? 1f
                    : 1f - Mathf.InverseLerp(FoundationInnerRadius, FoundationOuterRadius, distance);

                float currentHeight = heights[z, x];
                float raisedHeight = Mathf.Lerp(currentHeight, normalizedTargetHeight, blend);
                heights[z, x] = Mathf.Max(currentHeight, raisedHeight);
            }
        }
    }
}
