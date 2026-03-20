using System;
using System.Threading.Tasks;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PlayerUIMode
{
    Normal,
    IslandEdit
}

public enum IslandEditState
{
    None,
    Selecting,
    BuildChooseType,
    BuildPlacing,
    Moving,
    DestroyConfirm
}

[DefaultExecutionOrder(-200)]
public sealed class IslandBuildManager : MonoBehaviour
{
    private const string TurretResourcePath = "Island/Turret";
    private const string TurretWorldObjectType = "turret";
    private const float PlacementY = 11f;
    private const int TurretGoldCost = 100;
    private const int MaxOwnedTurrets = 6;
    private const float MinimumTurretSpacing = 26f;
    private const float FoundationInnerRadius = 5f;
    private const float FoundationOuterRadius = 8f;
    private const float TerrainRebuildDebounceSeconds = 0.08f;
    private const float EditModeTargetSelectionRadius = 2.5f;
    private const float EditModeTargetSelectionDistance = 1000f;

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
    private string statusMessage = "Manage Defenses enters defense edit mode.";
    private Player observedLocalPlayer;
    private Terrain cachedTerrain;
    private TerrainCollider cachedTerrainCollider;
    private NavMeshSurface cachedNavMeshSurface;
    private TerrainData sourceTerrainData;
    private TerrainData runtimeTerrainData;
    private float[,] baseHeights;
    private bool terrainDirty;
    private float terrainRebuildReadyAt;
    private bool restoreInProgress;
    private bool restoreCompleted;

    public PlayerUIMode UiMode { get; private set; } = PlayerUIMode.Normal;
    public IslandEditState EditState { get; private set; } = IslandEditState.None;
    public bool IsEditModeActive => UiMode == PlayerUIMode.IslandEdit;
    public bool IsPlacementActive => placementMode != PlacementMode.None;
    public bool IsBuildCatalogVisible => EditState == IslandEditState.BuildChooseType || EditState == IslandEditState.BuildPlacing;
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
        CleanupRuntimeState();
    }

    private void OnDestroy()
    {
        CleanupRuntimeState();
    }

    private void Update()
    {
        LoadTurretPrefab();
        EnsureNetworkPrefabRegistered();
        TrackObservedLocalPlayer();
        HandleEditModeCancelInput();
        SanitizeEditModeState();
        UpdatePlacementPreview();
        RebuildTerrainIfNeeded();
    }

    private void CleanupRuntimeState()
    {
        IslandTurret.RegistryChanged -= HandleTurretRegistryChanged;
        Player.LocalPlayerSpawned -= HandleLocalPlayerSpawned;
        UntrackObservedLocalPlayer();
        RestoreTerrainState();

        if (Instance == this)
        {
            Instance = null;
        }
    }

#if UNITY_EDITOR
    // Restore the terrain before Play Mode teardown can leave the heightmap dirty in the editor.
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorPlayModeCleanup()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= HandleEditorPlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += HandleEditorPlayModeStateChanged;
    }

    private static void HandleEditorPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            return;
        }

        Instance?.CleanupRuntimeState();
    }
#endif

    public int GetLocalOwnedTurretCount()
    {
        return TryGetLocalOwnerEntityId(out string localOwnerEntityId)
            ? IslandTurret.CountOwnedByOwnerEntity(localOwnerEntityId)
            : 0;
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

        return IsOwnedByLocalPlayer(selectedTurret) ? selectedTurret : null;
    }

    public void EnterEditMode()
    {
        UiMode = PlayerUIMode.IslandEdit;
        EditState = IslandEditState.Selecting;
        CancelPlacement();
        DeselectNonTurretTarget();
        SetStatus("Defense edit mode active. Select a turret or choose Build.");
    }

    public void ExitEditMode(string reason = null)
    {
        CancelPlacement();
        UiMode = PlayerUIMode.Normal;
        EditState = IslandEditState.None;
        DeselectSelectedTurret();
        SetStatus(string.IsNullOrWhiteSpace(reason) ? "Defense edit mode closed." : reason);
    }

    public void OpenBuildCatalog()
    {
        if (!IsEditModeActive)
        {
            EnterEditMode();
        }

        CancelPlacement();
        EditState = IslandEditState.BuildChooseType;
        SetStatus("Choose a turret to place, then click a clear position on the island.");
    }

    public void ReturnToSelectionMode(string reason = null)
    {
        if (!IsEditModeActive)
        {
            return;
        }

        CancelPlacement();
        EditState = IslandEditState.Selecting;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            SetStatus(reason);
        }
    }

    public void CancelCurrentAction(string reason = null)
    {
        if (IsPlacementActive)
        {
            CancelPlacement(reason);
            return;
        }

        if (!IsEditModeActive)
        {
            return;
        }

        if (EditState == IslandEditState.BuildChooseType || EditState == IslandEditState.DestroyConfirm)
        {
            EditState = IslandEditState.Selecting;
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            SetStatus(reason);
        }
    }

    public bool BeginDestroyConfirmation()
    {
        if (!IsEditModeActive)
        {
            EnterEditMode();
        }

        IslandTurret selectedTurret = GetSelectedOwnedTurret();
        if (selectedTurret == null)
        {
            SetStatus("Select one of your turrets before demolishing it.");
            return false;
        }

        CancelPlacement();
        EditState = IslandEditState.DestroyConfirm;
        SetStatus($"Confirm demolish for {selectedTurret.name}.");
        return true;
    }

    public bool BeginBuildPlacement()
    {
        if (!IsEditModeActive)
        {
            EnterEditMode();
        }

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
        EditState = IslandEditState.BuildPlacing;
        SetStatus("Move the cursor over the island and click to place your turret. Right-click or Esc cancels.");
        return true;
    }

    public bool BeginMovePlacement(IslandTurret turret)
    {
        if (!IsEditModeActive)
        {
            EnterEditMode();
        }

        if (turret == null)
        {
            SetStatus("Select one of your turrets before moving it.");
            return false;
        }

        if (!IsOwnedByLocalPlayer(turret))
        {
            SetStatus("You can only move your own turrets.");
            return false;
        }

        StartPlacement(PlacementMode.Move, turret.NetworkObjectId);
        EditState = IslandEditState.Moving;
        previewPosition = turret.transform.position;
        SetStatus("Click a new location for the selected turret. Right-click or Esc cancels.");
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

        EditState = IsEditModeActive ? IslandEditState.Selecting : EditState;
        SetStatus("Demolish request sent.");
        return true;
    }

    public void CancelPlacement(string reason = null)
    {
        placementMode = PlacementMode.None;
        movingTurretId = 0;
        previewValid = false;
        SetPreviewVisible(false);

        if (EditState == IslandEditState.BuildPlacing || EditState == IslandEditState.Moving)
        {
            EditState = IslandEditState.Selecting;
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            SetStatus(reason);
        }
    }

    public bool TryHandleGameplayClick(Ray ray)
    {
        if (!IsEditModeActive && !IsPlacementActive)
        {
            return false;
        }

        if (!IsPlacementActive)
        {
            if (EditState == IslandEditState.BuildChooseType)
            {
                SetStatus("Choose a turret from the build bar, or cancel to return to selection.");
                return true;
            }

            return TryHandleEditSelectionClick(ray);
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

    public async Task RestorePersistentTurretsAsync()
    {
        if (restoreCompleted || restoreInProgress)
        {
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        restoreInProgress = true;
        try
        {
            if (!TryCreateWorldObjectClient(out BackendWorldObjectClient worldObjectClient, out string failureMessage))
            {
                Debug.LogWarning($"[WorldObjects] {failureMessage}");
                return;
            }

            LoadTurretPrefab();
            EnsureNetworkPrefabRegistered();

            BackendWorldObjectResponse[] worldObjects = await worldObjectClient.GetWorldObjectsAsync(TurretWorldObjectType);
            for (int index = 0; index < worldObjects.Length; index++)
            {
                BackendWorldObjectResponse worldObject = worldObjects[index];
                if (worldObject == null || string.IsNullOrWhiteSpace(worldObject.Id))
                {
                    continue;
                }

                if (TryFindTurretByWorldObjectId(worldObject.Id, out _))
                {
                    continue;
                }

                if (!PersistentTurretState.TryParse(worldObject.State, out PersistentTurretState persistentState))
                {
                    Debug.LogWarning($"[WorldObjects] Skipping turret {worldObject.Id}: missing or invalid position state.");
                    continue;
                }

                if (!TrySanitizePlacementPosition(persistentState.Position, 0, out Vector3 resolvedPosition, out string restoreFailure))
                {
                    Debug.LogWarning($"[WorldObjects] Skipping turret {worldObject.Id}: {restoreFailure}");
                    continue;
                }

                if (!TrySpawnPersistentTurret(worldObject.OwnerEntityId, worldObject.Id, resolvedPosition, out _, out restoreFailure))
                {
                    Debug.LogWarning($"[WorldObjects] Failed to restore turret {worldObject.Id}: {restoreFailure}");
                }
            }

            restoreCompleted = true;
            MarkTerrainDirty();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WorldObjects] Failed to restore persistent turrets: {ex.Message}");
        }
        finally
        {
            restoreInProgress = false;
        }
    }

    public void NotifyTurretDestroyed(IslandTurret turret)
    {
        if (turret == null || !turret.HasPersistentWorldObjectId)
        {
            return;
        }

        _ = DeletePersistentWorldObjectAsync(turret.PersistentWorldObjectId);
    }

    public async Task<(bool success, string message)> TryServerBuildTurretAsync(Player owner, Vector3 requestedPosition)
    {
        if (!TryResolveOwnerEntityId(owner, out string ownerEntityId, out string resultMessage))
        {
            return (false, resultMessage);
        }

        if (!ValidateServerBuildRequest(owner, ownerEntityId, requestedPosition, out Vector3 resolvedPosition, out resultMessage))
        {
            return (false, resultMessage);
        }

        if (!TryCreateWorldObjectClient(out BackendWorldObjectClient worldObjectClient, out resultMessage))
        {
            return (false, resultMessage);
        }

        BackendWorldObjectResponse worldObject;
        try
        {
            worldObject = await worldObjectClient.CreateWorldObjectAsync(
                TurretWorldObjectType,
                ownerEntityId,
                PersistentTurretState.FromPosition(resolvedPosition).ToJson());
        }
        catch (Exception ex)
        {
            return (false, $"Could not persist turret: {ex.Message}");
        }

        if (!owner.TrySpendGold(TurretGoldCost))
        {
            await DeletePersistentWorldObjectAsync(worldObject.Id);
            return (false, $"You need {TurretGoldCost} gold to build a turret.");
        }

        if (!TrySpawnPersistentTurret(ownerEntityId, worldObject.Id, resolvedPosition, out _, out resultMessage))
        {
            await DeletePersistentWorldObjectAsync(worldObject.Id);
            owner.ApplyPersistedWallet(owner.Gold + TurretGoldCost, owner.Diamonds);
            return (false, resultMessage);
        }

        MarkTerrainDirty();
        return (true, "Turret built.");
    }

    public async Task<(bool success, string message)> TryServerMoveTurretAsync(Player owner, ulong turretNetworkObjectId, Vector3 requestedPosition)
    {
        if (owner == null || !owner.IsServer)
        {
            return (false, "Only the server can move turrets.");
        }

        if (!TryResolveOwnedTurret(owner, turretNetworkObjectId, out IslandTurret turret, out string resultMessage))
        {
            return (false, resultMessage);
        }

        if (!TrySanitizePlacementPosition(requestedPosition, turretNetworkObjectId, out Vector3 resolvedPosition, out resultMessage))
        {
            return (false, resultMessage);
        }

        if (!TryCreateWorldObjectClient(out BackendWorldObjectClient worldObjectClient, out resultMessage))
        {
            return (false, resultMessage);
        }

        try
        {
            await worldObjectClient.UpdateWorldObjectAsync(
                turret.PersistentWorldObjectId,
                PersistentTurretState.FromPosition(resolvedPosition).ToJson());
        }
        catch (Exception ex)
        {
            return (false, $"Could not save turret position: {ex.Message}");
        }

        turret.SetPlacementPosition(resolvedPosition);
        MarkTerrainDirty();
        return (true, "Turret moved.");
    }

    public async Task<(bool success, string message)> TryServerDeleteTurretAsync(Player owner, ulong turretNetworkObjectId)
    {
        if (owner == null || !owner.IsServer)
        {
            return (false, "Only the server can delete turrets.");
        }

        if (!TryResolveOwnedTurret(owner, turretNetworkObjectId, out IslandTurret turret, out string resultMessage))
        {
            return (false, resultMessage);
        }

        if (!TryCreateWorldObjectClient(out _, out resultMessage))
        {
            return (false, resultMessage);
        }

        if (!await DeletePersistentWorldObjectAsync(turret.PersistentWorldObjectId))
        {
            return (false, "Could not remove turret from persistent world storage.");
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
        return (true, "Turret deleted.");
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

    private void HandleEditModeCancelInput()
    {
        if (!IsEditModeActive)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsPlacementActive || EditState == IslandEditState.BuildChooseType || EditState == IslandEditState.DestroyConfirm)
            {
                CancelCurrentAction("Action canceled.");
            }
            else
            {
                ExitEditMode("Defense edit mode closed.");
            }

            return;
        }

        if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame)
        {
            return;
        }

        if (IsPlacementActive || EditState == IslandEditState.BuildChooseType || EditState == IslandEditState.DestroyConfirm)
        {
            CancelCurrentAction("Action canceled.");
        }
    }

    private void SanitizeEditModeState()
    {
        if (!IsEditModeActive)
        {
            return;
        }

        if (EditState == IslandEditState.None)
        {
            EditState = IslandEditState.Selecting;
        }

        if (EditState == IslandEditState.DestroyConfirm && GetSelectedOwnedTurret() == null)
        {
            EditState = IslandEditState.Selecting;
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

    private bool TryHandleEditSelectionClick(Ray ray)
    {
        if (TryResolveTurretFromRay(ray, out IslandTurret turret))
        {
            if (SelectObject.Instance != null)
            {
                SelectObject.Instance.Select(turret.gameObject);
            }

            EditState = IslandEditState.Selecting;
            SetStatus(IsOwnedByLocalPlayer(turret)
                ? $"Selected {turret.name}. Choose an action from the defense bar."
                : $"Selected {turret.name}. You can inspect it, but only your own turrets can be changed.");
            return true;
        }

        DeselectSelectedTurret();
        EditState = IslandEditState.Selecting;
        SetStatus("No turret selected. Choose one of your defenses or enter Build mode.");
        return true;
    }

    private bool TryResolveTurretFromRay(Ray ray, out IslandTurret turret)
    {
        turret = null;
        GameObject requester = Player.LocalPlayer != null ? Player.LocalPlayer.gameObject : null;
        if (!CombatTargetingUtility.TryFindTargetAlongRay(
                ray,
                requester,
                EditModeTargetSelectionDistance,
                EditModeTargetSelectionRadius,
                out GameObject target))
        {
            return false;
        }

        return target != null && target.TryGetComponent(out turret);
    }

    private void DeselectSelectedTurret()
    {
        if (SelectObject.Instance?.SelectedTarget != null &&
            SelectObject.Instance.SelectedTarget.TryGetComponent(out IslandTurret _))
        {
            SelectObject.Instance.Deselect();
        }
    }

    private void DeselectNonTurretTarget()
    {
        if (SelectObject.Instance?.SelectedTarget == null)
        {
            return;
        }

        if (!SelectObject.Instance.SelectedTarget.TryGetComponent(out IslandTurret _))
        {
            SelectObject.Instance.Deselect();
        }
    }

    private bool TryCreateWorldObjectClient(out BackendWorldObjectClient worldObjectClient, out string failureMessage)
    {
        worldObjectClient = null;
        failureMessage = string.Empty;

        string playerDataBaseUrl = MultiplayerController.ResolvePlayerDataBaseUrlForServer();
        if (string.IsNullOrWhiteSpace(playerDataBaseUrl))
        {
            failureMessage = "Player-data backend URL is not configured.";
            return false;
        }

        string serverApiKey = MultiplayerController.ResolveServerApiKeyForWorldObjects();
        if (string.IsNullOrWhiteSpace(serverApiKey))
        {
            failureMessage = "World-object server API key is not configured.";
            return false;
        }

        try
        {
            worldObjectClient = new BackendWorldObjectClient(playerDataBaseUrl, serverApiKey);
            return true;
        }
        catch (Exception ex)
        {
            failureMessage = $"Could not create world-object backend client: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> DeletePersistentWorldObjectAsync(string worldObjectId)
    {
        if (string.IsNullOrWhiteSpace(worldObjectId))
        {
            return true;
        }

        if (!TryCreateWorldObjectClient(out BackendWorldObjectClient worldObjectClient, out string failureMessage))
        {
            Debug.LogWarning($"[WorldObjects] {failureMessage}");
            return false;
        }

        try
        {
            await worldObjectClient.DeleteWorldObjectAsync(worldObjectId);
            return true;
        }
        catch (BackendApiException ex) when (ex.StatusCode == 404)
        {
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WorldObjects] Failed to delete world object {worldObjectId}: {ex.Message}");
            return false;
        }
    }

    private bool TryFindTurretByWorldObjectId(string worldObjectId, out IslandTurret foundTurret)
    {
        foundTurret = null;
        if (string.IsNullOrWhiteSpace(worldObjectId))
        {
            return false;
        }

        foreach (IslandTurret turret in IslandTurret.ActiveTurrets)
        {
            if (turret == null || !turret.IsSpawned)
            {
                continue;
            }

            if (!string.Equals(turret.PersistentWorldObjectId, worldObjectId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foundTurret = turret;
            return true;
        }

        return false;
    }

    private bool TrySpawnPersistentTurret(string ownerEntityId, string worldObjectId, Vector3 resolvedPosition, out IslandTurret turret, out string resultMessage)
    {
        turret = null;
        resultMessage = string.Empty;

        LoadTurretPrefab();
        EnsureNetworkPrefabRegistered();
        if (turretPrefab == null)
        {
            resultMessage = "Turret prefab is unavailable.";
            return false;
        }

        GameObject instance = Instantiate(turretPrefab, resolvedPosition, Quaternion.identity);
        turret = instance.GetComponent<IslandTurret>();
        if (turret == null)
        {
            Destroy(instance);
            resultMessage = "Turret prefab is missing the IslandTurret component.";
            return false;
        }

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Destroy(instance);
            resultMessage = "Turret prefab is missing a NetworkObject.";
            return false;
        }

        networkObject.Spawn(true);
        turret.InitializeOwnership(ownerEntityId, worldObjectId);
        turret.SetPlacementPosition(resolvedPosition);
        return true;
    }

    private bool TryResolveOwnerEntityId(Player owner, out string ownerEntityId, out string resultMessage)
    {
        ownerEntityId = string.Empty;
        resultMessage = string.Empty;

        if (owner == null)
        {
            resultMessage = "Unable to resolve your account for turret ownership.";
            return false;
        }

        if (owner != null && !string.IsNullOrWhiteSpace(owner.OwnerEntityId))
        {
            ownerEntityId = owner.OwnerEntityId.Trim();
            return true;
        }

        MultiplayerController controller = MultiplayerController.Instance;
        if (controller == null || !controller.TryGetAuthenticatedUserId(owner.OwnerClientId, out ownerEntityId))
        {
            resultMessage = "Unable to resolve your account for turret ownership.";
            return false;
        }

        return !string.IsNullOrWhiteSpace(ownerEntityId);
    }

    private bool ValidateServerBuildRequest(Player owner, string ownerEntityId, Vector3 requestedPosition, out Vector3 resolvedPosition, out string resultMessage)
    {
        resolvedPosition = default;
        resultMessage = string.Empty;

        if (owner == null || !owner.IsServer)
        {
            resultMessage = "Only the server can build turrets.";
            return false;
        }

        LoadTurretPrefab();
        if (turretPrefab == null)
        {
            resultMessage = "Turret prefab is unavailable.";
            return false;
        }

        if (IslandTurret.CountOwnedByOwnerEntity(ownerEntityId) >= MaxOwnedTurrets)
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

        if (!TryResolveOwnerEntityId(owner, out string ownerEntityId, out resultMessage))
        {
            return false;
        }

        if (!IslandTurret.TryResolveOwnedTurret(turretNetworkObjectId, ownerEntityId, out turret) || turret == null)
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

    private bool TryGetLocalOwnerEntityId(out string ownerEntityId)
    {
        ownerEntityId = string.Empty;
        if (!TryGetLocalPlayer(out Player localPlayer))
        {
            return false;
        }

        ownerEntityId = localPlayer.OwnerEntityId;
        return !string.IsNullOrWhiteSpace(ownerEntityId);
    }

    private bool IsOwnedByLocalPlayer(IslandTurret turret)
    {
        return turret != null &&
               TryGetLocalOwnerEntityId(out string localOwnerEntityId) &&
               turret.IsOwnedByOwnerEntity(localOwnerEntityId);
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

    private TerrainData EnsureRuntimeTerrainData(Terrain terrain)
    {
        if (terrain == null)
        {
            return null;
        }

        TerrainData terrainData = terrain.terrainData;
        if (terrainData == null)
        {
            return null;
        }

        bool runtimeCloneMatchesTerrain = runtimeTerrainData != null &&
                                          cachedTerrain == terrain &&
                                          terrain.terrainData == runtimeTerrainData;
        if (!runtimeCloneMatchesTerrain)
        {
            ReleaseRuntimeTerrainData();
        }
        else
        {
            return runtimeTerrainData;
        }

        cachedTerrain = terrain;
        cachedTerrainCollider = terrain.GetComponent<TerrainCollider>();
        sourceTerrainData = terrainData;
        runtimeTerrainData = Instantiate(sourceTerrainData);
        runtimeTerrainData.name = $"{sourceTerrainData.name} (Runtime Foundations)";
        runtimeTerrainData.hideFlags = HideFlags.HideAndDontSave;

        terrain.terrainData = runtimeTerrainData;
        if (cachedTerrainCollider != null)
        {
            cachedTerrainCollider.terrainData = runtimeTerrainData;
        }

        return runtimeTerrainData;
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
        if (terrain == null)
        {
            return;
        }

        TerrainData terrainData = EnsureRuntimeTerrainData(terrain);
        if (terrainData == null)
        {
            return;
        }

        int resolution = terrainData.heightmapResolution;
        if (resolution <= 1)
        {
            return;
        }

        TerrainData sourceData = sourceTerrainData != null ? sourceTerrainData : terrainData;
        if (baseHeights == null ||
            baseHeights.GetLength(0) != resolution ||
            baseHeights.GetLength(1) != resolution)
        {
            baseHeights = sourceData.GetHeights(0, 0, resolution, resolution);
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

    // Foundation shaping runs against a runtime terrain clone so the imported terrain asset stays untouched.
    private void RestoreTerrainState()
    {
        ReleaseRuntimeTerrainData();
        terrainDirty = false;
        terrainRebuildReadyAt = 0f;
    }

    private void ReleaseRuntimeTerrainData()
    {
        if (cachedTerrain != null && sourceTerrainData != null)
        {
            cachedTerrain.terrainData = sourceTerrainData;

            TerrainCollider terrainCollider = cachedTerrainCollider != null
                ? cachedTerrainCollider
                : cachedTerrain.GetComponent<TerrainCollider>();
            if (terrainCollider != null)
            {
                terrainCollider.terrainData = sourceTerrainData;
            }

            cachedTerrain.Flush();
        }

        DestroyTrackedObject(runtimeTerrainData);

        baseHeights = null;
        sourceTerrainData = null;
        runtimeTerrainData = null;
        cachedTerrain = null;
        cachedTerrainCollider = null;
        cachedNavMeshSurface = null;
    }

    private static void DestroyTrackedObject(UnityEngine.Object trackedObject)
    {
        if (trackedObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(trackedObject);
            return;
        }

        DestroyImmediate(trackedObject);
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

public readonly struct PersistentTurretState
{
    public PersistentTurretState(Vector3 position)
    {
        Position = position;
    }

    public Vector3 Position { get; }

    public Newtonsoft.Json.Linq.JObject ToJson()
    {
        return new Newtonsoft.Json.Linq.JObject
        {
            ["positionX"] = Position.x,
            ["positionY"] = Position.y,
            ["positionZ"] = Position.z,
        };
    }

    public static PersistentTurretState FromPosition(Vector3 position)
    {
        return new PersistentTurretState(position);
    }

    public static bool TryParse(Newtonsoft.Json.Linq.JObject state, out PersistentTurretState persistentState)
    {
        persistentState = default;
        if (state == null)
        {
            return false;
        }

        if (!TryReadFloat(state, "positionX", out float x) ||
            !TryReadFloat(state, "positionY", out float y) ||
            !TryReadFloat(state, "positionZ", out float z))
        {
            return false;
        }

        persistentState = new PersistentTurretState(new Vector3(x, y, z));
        return true;
    }

    private static bool TryReadFloat(Newtonsoft.Json.Linq.JObject state, string propertyName, out float value)
    {
        value = 0f;
        if (state == null || !state.TryGetValue(propertyName, out var token) || token == null)
        {
            return false;
        }

        switch (token.Type)
        {
            case Newtonsoft.Json.Linq.JTokenType.Float:
            case Newtonsoft.Json.Linq.JTokenType.Integer:
                value = token.ToObject<float>();
                return true;
            case Newtonsoft.Json.Linq.JTokenType.String:
                return float.TryParse(token.ToObject<string>(), out value);
            default:
                return false;
        }
    }
}
