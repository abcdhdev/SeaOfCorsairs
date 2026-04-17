using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-225)]
public sealed class WorldMapRuntimeVisibilityController : MonoBehaviour
{
    private const string RuntimeObjectName = "[WorldMapRuntimeVisibility]";

    private static WorldMapRuntimeVisibilityController s_instance;

    private Player localPlayer;
    private Coroutine queuedRefresh;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static WorldMapRuntimeVisibilityController EnsureInstance()
    {
        if (s_instance != null)
        {
            return s_instance;
        }

        WorldMapRuntimeVisibilityController existingInstance = FindFirstObjectByType<WorldMapRuntimeVisibilityController>();
        if (existingInstance != null)
        {
            s_instance = existingInstance;
            return s_instance;
        }

        var runtimeObject = new GameObject(RuntimeObjectName);
        DontDestroyOnLoad(runtimeObject);
        s_instance = runtimeObject.AddComponent<WorldMapRuntimeVisibilityController>();
        return s_instance;
    }

    public static void SetAllMapContentVisibleNow()
    {
        EnsureInstance().SetAllMapScenesVisibleImmediate();
    }

    public static void RefreshVisibilityNowStatic()
    {
        EnsureInstance().RefreshVisibilityNow();
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
    }

    private void OnEnable()
    {
        Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        Player.LocalPlayerSpawned += OnLocalPlayerSpawned;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;

        if (Player.LocalPlayer != null)
        {
            BindLocalPlayer(Player.LocalPlayer);
        }

        QueueRefresh();
    }

    private void OnDisable()
    {
        Player.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        UnbindLocalPlayer();

        if (queuedRefresh != null)
        {
            StopCoroutine(queuedRefresh);
            queuedRefresh = null;
        }
    }

    private void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
        }
    }

    private void OnLocalPlayerSpawned(Player player)
    {
        BindLocalPlayer(player);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        QueueRefresh();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        QueueRefresh();
    }

    private void OnLocalPlayerWorldMapChanged(string previousMapId, string currentMapId)
    {
        QueueRefresh();
    }

    private void BindLocalPlayer(Player player)
    {
        if (ReferenceEquals(localPlayer, player))
        {
            QueueRefresh();
            return;
        }

        UnbindLocalPlayer();
        localPlayer = player;
        if (localPlayer != null)
        {
            localPlayer.OnWorldMapIdChanged += OnLocalPlayerWorldMapChanged;
        }

        QueueRefresh();
    }

    private void UnbindLocalPlayer()
    {
        if (localPlayer != null)
        {
            localPlayer.OnWorldMapIdChanged -= OnLocalPlayerWorldMapChanged;
            localPlayer = null;
        }
    }

    private void QueueRefresh()
    {
        if (!Application.isPlaying || queuedRefresh != null)
        {
            return;
        }

        queuedRefresh = StartCoroutine(RefreshAfterSceneSettle());
    }

    private IEnumerator RefreshAfterSceneSettle()
    {
        yield return null;
        queuedRefresh = null;
        RefreshVisibilityNow();
    }

    private void RefreshVisibilityNow()
    {
        Player resolvedLocalPlayer = ResolveLocalPlayer();
        if (resolvedLocalPlayer == null || !resolvedLocalPlayer.IsSpawned)
        {
            SetAllMapScenesVisible();
            return;
        }

        string currentMapId = WorldMapCatalog.NormalizeMapId(resolvedLocalPlayer.CurrentWorldMapId);
        if (string.IsNullOrWhiteSpace(currentMapId) && WorldMapManager.Instance != null)
        {
            currentMapId = WorldMapManager.Instance.StartingMapId;
        }

        WorldMapSceneAuthoring[] sceneRoots = FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        for (int index = 0; index < sceneRoots.Length; index++)
        {
            WorldMapSceneAuthoring sceneRoot = sceneRoots[index];
            if (sceneRoot == null)
            {
                continue;
            }

            bool isCurrentMap = string.Equals(sceneRoot.MapId, currentMapId, StringComparison.OrdinalIgnoreCase);
            sceneRoot.SetLocalSceneContentVisible(isCurrentMap);
        }

        WorldMapContentScope[] contentScopes = FindObjectsByType<WorldMapContentScope>(FindObjectsSortMode.None);
        for (int index = 0; index < contentScopes.Length; index++)
        {
            WorldMapContentScope contentScope = contentScopes[index];
            if (contentScope == null || contentScope.GetComponentInParent<WorldMapSceneAuthoring>() != null)
            {
                continue;
            }

            bool isCurrentMap = string.Equals(contentScope.MapId, currentMapId, StringComparison.OrdinalIgnoreCase);
            contentScope.SetLocalContentVisible(isCurrentMap);
        }
    }

    private void SetAllMapScenesVisibleImmediate()
    {
        if (queuedRefresh != null)
        {
            StopCoroutine(queuedRefresh);
            queuedRefresh = null;
        }

        SetAllMapScenesVisible();
    }

    private Player ResolveLocalPlayer()
    {
        if (localPlayer != null && localPlayer.IsSpawned)
        {
            return localPlayer;
        }

        if (Player.LocalPlayer != null)
        {
            BindLocalPlayer(Player.LocalPlayer);
            return Player.LocalPlayer;
        }

        return null;
    }

    private static void SetAllMapScenesVisible()
    {
        WorldMapSceneAuthoring[] sceneRoots = FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        for (int index = 0; index < sceneRoots.Length; index++)
        {
            WorldMapSceneAuthoring sceneRoot = sceneRoots[index];
            if (sceneRoot != null)
            {
                sceneRoot.SetLocalSceneContentVisible(true);
            }
        }

        WorldMapContentScope[] contentScopes = FindObjectsByType<WorldMapContentScope>(FindObjectsSortMode.None);
        for (int index = 0; index < contentScopes.Length; index++)
        {
            WorldMapContentScope contentScope = contentScopes[index];
            if (contentScope != null)
            {
                contentScope.SetLocalContentVisible(true);
            }
        }
    }
}
