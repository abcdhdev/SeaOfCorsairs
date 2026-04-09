using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-250)]
public sealed class WorldMapManager : MonoBehaviour
{
    private const string RuntimeObjectName = "[WorldMapManager]";

    private static WorldMapManager s_instance;

    [Header("Catalog")]
    [SerializeField] private WorldMapCatalog catalog;
    [SerializeField] private string startingMapId = "1-1";
    [SerializeField] private bool loadCatalogScenesAtRuntime = true;
    [SerializeField] private bool preferNetcodeSceneManagement = true;
    [SerializeField] private bool useAdditiveClientSynchronization = true;
    [SerializeField] private bool keepNonNetworkScenesLoadedOnClients = true;

    private readonly Dictionary<string, WorldMapSceneAuthoring> loadedScenesByMapId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> playerCountsByMapId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> requestedCatalogScenePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> completedNetcodeSceneLoads = new(StringComparer.OrdinalIgnoreCase);
    private WorldMapCatalog runtimeFallbackCatalog;
    private Coroutine loadCatalogScenesCoroutine;
    private NetworkManager subscribedNetworkManager;
    private NetworkSceneManager.VerifySceneBeforeLoadingDelegateHandler previousVerifySceneBeforeLoading;
    private NetworkSceneManager.VerifySceneBeforeLoadingDelegateHandler verifySceneBeforeLoadingHandler;
    private GameUIController registeredHudController;
    private WorldMapController registeredOverlayController;

    public static WorldMapManager Instance => EnsureInstance();

    public WorldMapCatalog Catalog => ResolveCatalog();
    public string StartingMapId => ResolveStartingMapId();
    public GameUIController RegisteredHudController => registeredHudController;
    public WorldMapController RegisteredOverlayController => registeredOverlayController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static WorldMapManager EnsureInstance()
    {
        if (s_instance != null)
        {
            return s_instance;
        }

        WorldMapManager existingInstance = FindFirstObjectByType<WorldMapManager>();
        if (existingInstance != null)
        {
            s_instance = existingInstance;
            return s_instance;
        }

        var runtimeObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(runtimeObject);
        s_instance = runtimeObject.AddComponent<WorldMapManager>();
        return s_instance;
    }

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_instance = this;
        DontDestroyOnLoad(gameObject);
        verifySceneBeforeLoadingHandler = VerifySceneBeforeLoading;
        TryConfigureNetworkSceneManagement();
    }

    private void Start()
    {
        if (!Application.isPlaying || !loadCatalogScenesAtRuntime)
        {
            return;
        }

        TryConfigureNetworkSceneManagement();
        TryBeginSceneLoading();
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkManager();

        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    public void RegisterScene(WorldMapSceneAuthoring authoring)
    {
        if (authoring == null)
        {
            return;
        }

        string mapId = authoring.MapId;
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return;
        }

        if (loadedScenesByMapId.TryGetValue(mapId, out WorldMapSceneAuthoring existingAuthoring) &&
            existingAuthoring != null &&
            !ReferenceEquals(existingAuthoring, authoring))
        {
            Debug.LogWarning($"WorldMapManager: Multiple loaded scene roots were registered for map '{mapId}'. Keeping the most recently registered root.", authoring);
        }

        loadedScenesByMapId[mapId] = authoring;
    }

    public void UnregisterScene(WorldMapSceneAuthoring authoring)
    {
        if (authoring == null)
        {
            return;
        }

        string mapId = authoring.MapId;
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return;
        }

        if (loadedScenesByMapId.TryGetValue(mapId, out WorldMapSceneAuthoring currentAuthoring) &&
            ReferenceEquals(currentAuthoring, authoring))
        {
            loadedScenesByMapId.Remove(mapId);
        }
    }

    public bool TryGetDefinition(string mapId, out WorldMapDefinition definition)
    {
        definition = null;
        return Catalog != null && Catalog.TryGetDefinition(mapId, out definition);
    }

    public bool TryGetAdjacentDefinition(string mapId, MapTransitionDirection direction, out WorldMapDefinition definition)
    {
        definition = null;
        return Catalog != null && Catalog.TryGetAdjacent(mapId, direction, out definition);
    }

    public bool TryGetLoadedScene(string mapId, out WorldMapSceneAuthoring authoring)
    {
        return loadedScenesByMapId.TryGetValue(WorldMapCatalog.NormalizeMapId(mapId), out authoring) &&
               authoring != null;
    }

    public bool TryGetCurrentScene(Player player, out WorldMapSceneAuthoring authoring)
    {
        authoring = null;
        return player != null &&
               TryGetLoadedScene(player.CurrentWorldMapId, out authoring);
    }

    public bool TryGetMapId(Component target, out string mapId)
    {
        mapId = string.Empty;
        if (target == null)
        {
            return false;
        }

        if (target is Player player)
        {
            mapId = player.CurrentWorldMapId;
            return !string.IsNullOrWhiteSpace(mapId);
        }

        Scene targetScene = target.gameObject.scene;
        if (targetScene.IsValid() && targetScene.isLoaded)
        {
            foreach (KeyValuePair<string, WorldMapSceneAuthoring> pair in loadedScenesByMapId)
            {
                WorldMapSceneAuthoring authoring = pair.Value;
                if (authoring == null)
                {
                    continue;
                }

                if (authoring.gameObject.scene == targetScene)
                {
                    mapId = pair.Key;
                    return true;
                }
            }
        }

        WorldMapSceneAuthoring parentAuthoring = target.GetComponentInParent<WorldMapSceneAuthoring>();
        if (parentAuthoring != null)
        {
            mapId = parentAuthoring.MapId;
            return !string.IsNullOrWhiteSpace(mapId);
        }

        return false;
    }

    public bool TryGetTravelPrompt(Player player, out MapTransitionDirection direction, out string destinationMapId)
    {
        direction = default;
        destinationMapId = string.Empty;

        if (player == null || player.IsDead)
        {
            return false;
        }

        if (!TryGetCurrentScene(player, out WorldMapSceneAuthoring currentScene) ||
            !currentScene.TryGetPromptDirection(player.transform.position, out direction) ||
            !TryGetAdjacentDefinition(player.CurrentWorldMapId, direction, out WorldMapDefinition destination) ||
            !TryGetLoadedScene(destination.MapId, out _))
        {
            return false;
        }

        destinationMapId = destination.MapId;
        return !string.IsNullOrWhiteSpace(destinationMapId);
    }

    public bool TryResolveTravel(Player player, MapTransitionDirection direction, out string destinationMapId, out Vector3 destinationPosition, out Quaternion destinationRotation)
    {
        destinationMapId = string.Empty;
        destinationPosition = default;
        destinationRotation = Quaternion.identity;

        if (player == null || player.IsDead)
        {
            return false;
        }

        if (!TryGetCurrentScene(player, out WorldMapSceneAuthoring currentScene) ||
            !currentScene.IsWithinTravelZone(direction, player.transform.position) ||
            !TryGetAdjacentDefinition(player.CurrentWorldMapId, direction, out WorldMapDefinition destinationDefinition) ||
            !TryGetLoadedScene(destinationDefinition.MapId, out WorldMapSceneAuthoring destinationScene))
        {
            return false;
        }

        float normalizedOrthogonal = currentScene.GetNormalizedOrthogonalPosition(direction, player.transform.position);
        if (!destinationScene.TryResolveTravelDestination(direction.GetOpposite(), normalizedOrthogonal, out destinationPosition, out destinationRotation))
        {
            return false;
        }

        destinationMapId = destinationDefinition.MapId;
        return !string.IsNullOrWhiteSpace(destinationMapId);
    }

    public bool TryResolveRespawn(Player player, out Vector3 spawnPosition, out Quaternion spawnRotation)
    {
        spawnPosition = default;
        spawnRotation = Quaternion.identity;

        return player != null &&
               TryGetLoadedScene(player.CurrentWorldMapId, out WorldMapSceneAuthoring sceneAuthoring) &&
               sceneAuthoring.TryResolveRespawnTransform(out spawnPosition, out spawnRotation);
    }

    public bool TryGetMinimapTexture(string mapId, out Texture2D minimapTexture)
    {
        minimapTexture = null;
        if (TryGetLoadedScene(mapId, out WorldMapSceneAuthoring sceneAuthoring) &&
            sceneAuthoring.MinimapTextureOverride != null)
        {
            minimapTexture = sceneAuthoring.MinimapTextureOverride;
            return true;
        }

        if (TryGetDefinition(mapId, out WorldMapDefinition definition) && definition.MinimapTextureOverride != null)
        {
            minimapTexture = definition.MinimapTextureOverride;
            return true;
        }

        return false;
    }

    public void NotifyPlayerMapChanged(Player player, string previousMapId, string currentMapId)
    {
        if (player == null)
        {
            return;
        }

        previousMapId = WorldMapCatalog.NormalizeMapId(previousMapId);
        currentMapId = WorldMapCatalog.NormalizeMapId(currentMapId);

        if (!string.IsNullOrWhiteSpace(previousMapId))
        {
            UpdatePlayerCount(previousMapId, -1);
        }

        if (!string.IsNullOrWhiteSpace(currentMapId))
        {
            UpdatePlayerCount(currentMapId, 1);
        }
    }

    public int GetPlayerCount(string mapId)
    {
        mapId = WorldMapCatalog.NormalizeMapId(mapId);
        return playerCountsByMapId.TryGetValue(mapId, out int count)
            ? Mathf.Max(0, count)
            : 0;
    }

    public void RegisterHudController(GameUIController controller)
    {
        if (controller != null)
        {
            registeredHudController = controller;
        }
    }

    public void UnregisterHudController(GameUIController controller)
    {
        if (controller != null && ReferenceEquals(registeredHudController, controller))
        {
            registeredHudController = null;
        }
    }

    public void RegisterWorldMapOverlayController(WorldMapController controller)
    {
        if (controller != null)
        {
            registeredOverlayController = controller;
        }
    }

    public void UnregisterWorldMapOverlayController(WorldMapController controller)
    {
        if (controller != null && ReferenceEquals(registeredOverlayController, controller))
        {
            registeredOverlayController = null;
        }
    }

    private void BeginCatalogSceneLoading()
    {
        if (loadCatalogScenesCoroutine != null)
        {
            return;
        }

        loadCatalogScenesCoroutine = StartCoroutine(LoadCatalogScenesRoutine());
    }

    private void TryBeginSceneLoading()
    {
        if (!loadCatalogScenesAtRuntime || loadCatalogScenesCoroutine != null)
        {
            return;
        }

        if (ShouldUseNetcodeSceneManagement(out NetworkManager networkManager))
        {
            if (networkManager.IsServer && networkManager.IsListening)
            {
                BeginCatalogSceneLoading();
            }

            return;
        }

        BeginCatalogSceneLoading();
    }

    private WorldMapCatalog ResolveCatalog()
    {
        if (catalog != null)
        {
            return catalog;
        }

        if (runtimeFallbackCatalog == null)
        {
            runtimeFallbackCatalog = ScriptableObject.CreateInstance<WorldMapCatalog>();
            runtimeFallbackCatalog.GenerateDefaultGrid();
        }

        return runtimeFallbackCatalog;
    }

    private string ResolveStartingMapId()
    {
        string normalizedStartingMapId = WorldMapCatalog.NormalizeMapId(startingMapId);
        if (!string.IsNullOrWhiteSpace(normalizedStartingMapId))
        {
            return normalizedStartingMapId;
        }

        return Catalog != null ? Catalog.StartingMapId : "1-1";
    }

    private void UpdatePlayerCount(string mapId, int delta)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return;
        }

        int currentValue = GetPlayerCount(mapId);
        int nextValue = Mathf.Max(0, currentValue + delta);
        if (nextValue <= 0)
        {
            playerCountsByMapId.Remove(mapId);
            return;
        }

        playerCountsByMapId[mapId] = nextValue;
    }

    private IEnumerator LoadCatalogScenesRoutine()
    {
        WorldMapCatalog resolvedCatalog = Catalog;
        if (resolvedCatalog == null || resolvedCatalog.Maps == null || resolvedCatalog.Maps.Count == 0)
        {
            loadCatalogScenesCoroutine = null;
            yield break;
        }

        bool shouldUseNetcodeSceneLoading = ShouldUseNetcodeSceneManagement(out NetworkManager sceneLoadingNetworkManager) &&
                                            sceneLoadingNetworkManager.IsServer &&
                                            sceneLoadingNetworkManager.IsListening;

        for (int index = 0; index < resolvedCatalog.Maps.Count; index++)
        {
            WorldMapDefinition definition = resolvedCatalog.Maps[index];
            string scenePath = definition?.Scene?.ScenePath;
            if (definition == null || string.IsNullOrWhiteSpace(scenePath))
            {
                continue;
            }

            Scene loadedScene = SceneManager.GetSceneByPath(scenePath);
            if (loadedScene.IsValid() && loadedScene.isLoaded)
            {
                requestedCatalogScenePaths.Add(scenePath);
                continue;
            }

            if (!requestedCatalogScenePaths.Add(scenePath))
            {
                continue;
            }

            if (shouldUseNetcodeSceneLoading)
            {
                if (!TryLoadCatalogSceneThroughNetcode(scenePath))
                {
                    continue;
                }

                string sceneName = Path.GetFileNameWithoutExtension(scenePath);
                while (!completedNetcodeSceneLoads.Contains(sceneName))
                {
                    yield return null;
                }

                completedNetcodeSceneLoads.Remove(sceneName);
                continue;
            }

            AsyncOperation loadOperation = null;
            try
            {
                loadOperation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"WorldMapManager: Failed to start additive load for '{scenePath}'. {ex.Message}", this);
            }

            if (loadOperation == null)
            {
                continue;
            }

            while (!loadOperation.isDone)
            {
                yield return null;
            }
        }

        loadCatalogScenesCoroutine = null;
    }

    private bool TryLoadCatalogSceneThroughNetcode(string scenePath)
    {
        if (!ShouldUseNetcodeSceneManagement(out NetworkManager networkManager) ||
            networkManager.SceneManager == null ||
            !networkManager.IsServer ||
            !networkManager.IsListening)
        {
            return false;
        }

        string sceneName = Path.GetFileNameWithoutExtension(scenePath);
        completedNetcodeSceneLoads.Remove(sceneName);

        SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(scenePath, LoadSceneMode.Additive);
        if (status == SceneEventProgressStatus.Started)
        {
            return true;
        }

        requestedCatalogScenePaths.Remove(scenePath);
        Debug.LogWarning($"WorldMapManager: Netcode scene load for '{scenePath}' returned '{status}'. The scene will remain unloaded until retried.", this);
        return false;
    }

    private bool ShouldUseNetcodeSceneManagement(out NetworkManager networkManager)
    {
        networkManager = NetworkManager.Singleton;
        if (!preferNetcodeSceneManagement ||
            networkManager == null ||
            networkManager.NetworkConfig == null ||
            !networkManager.NetworkConfig.EnableSceneManagement ||
            networkManager.SceneManager == null)
        {
            return false;
        }

        return true;
    }

    private void TryConfigureNetworkSceneManagement()
    {
        if (!ShouldUseNetcodeSceneManagement(out NetworkManager networkManager))
        {
            return;
        }

        if (!ReferenceEquals(subscribedNetworkManager, networkManager))
        {
            UnsubscribeFromNetworkManager();
            subscribedNetworkManager = networkManager;
            subscribedNetworkManager.OnServerStarted += OnNetworkServerStarted;
            subscribedNetworkManager.SceneManager.OnLoadEventCompleted += OnNetcodeLoadEventCompleted;
        }

        if (useAdditiveClientSynchronization)
        {
            networkManager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
        }

        if (keepNonNetworkScenesLoadedOnClients)
        {
            networkManager.SceneManager.PostSynchronizationSceneUnloading = false;
        }

        if (networkManager.SceneManager.VerifySceneBeforeLoading != verifySceneBeforeLoadingHandler)
        {
            previousVerifySceneBeforeLoading = networkManager.SceneManager.VerifySceneBeforeLoading;
            networkManager.SceneManager.VerifySceneBeforeLoading = verifySceneBeforeLoadingHandler;
        }
    }

    private void UnsubscribeFromNetworkManager()
    {
        if (subscribedNetworkManager == null)
        {
            return;
        }

        if (subscribedNetworkManager.SceneManager != null)
        {
            subscribedNetworkManager.SceneManager.OnLoadEventCompleted -= OnNetcodeLoadEventCompleted;
            if (subscribedNetworkManager.SceneManager.VerifySceneBeforeLoading == verifySceneBeforeLoadingHandler)
            {
                subscribedNetworkManager.SceneManager.VerifySceneBeforeLoading = previousVerifySceneBeforeLoading;
            }
        }

        subscribedNetworkManager.OnServerStarted -= OnNetworkServerStarted;
        subscribedNetworkManager = null;
        previousVerifySceneBeforeLoading = null;
    }

    private void OnNetworkServerStarted()
    {
        TryConfigureNetworkSceneManagement();
        TryBeginSceneLoading();
    }

    private void OnNetcodeLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (loadSceneMode != LoadSceneMode.Additive || string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        completedNetcodeSceneLoads.Add(sceneName);
    }

    private bool VerifySceneBeforeLoading(int sceneIndex, string sceneName, LoadSceneMode loadSceneMode)
    {
        if (previousVerifySceneBeforeLoading != null &&
            !previousVerifySceneBeforeLoading(sceneIndex, sceneName, loadSceneMode))
        {
            return false;
        }

        string scenePath = sceneIndex >= 0
            ? SceneUtility.GetScenePathByBuildIndex(sceneIndex)
            : string.Empty;

        if (string.IsNullOrWhiteSpace(scenePath) || !IsCatalogScene(scenePath))
        {
            return true;
        }

        Scene alreadyLoadedScene = SceneManager.GetSceneByPath(scenePath);
        return !alreadyLoadedScene.IsValid() || !alreadyLoadedScene.isLoaded;
    }

    private bool IsCatalogScene(string scenePath)
    {
        WorldMapCatalog resolvedCatalog = Catalog;
        if (resolvedCatalog == null || resolvedCatalog.Maps == null)
        {
            return false;
        }

        for (int index = 0; index < resolvedCatalog.Maps.Count; index++)
        {
            string catalogScenePath = resolvedCatalog.Maps[index]?.Scene?.ScenePath;
            if (string.Equals(catalogScenePath, scenePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
