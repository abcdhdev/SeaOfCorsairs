#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WorldMapEditorSceneUtility
{
    public const string DefaultCatalogPath = "Assets/Data/WorldMap/WorldMapCatalog.asset";
    public const string MainScenePath = "Assets/Scenes/MainScene.unity";

    public static WorldMapCatalog LoadDefaultCatalog()
    {
        return AssetDatabase.LoadAssetAtPath<WorldMapCatalog>(DefaultCatalogPath);
    }

    public static bool OpenMapForEditing(
        WorldMapCatalog catalog,
        WorldMapDefinition definition,
        bool includeMainScene,
        bool includeNeighbors,
        bool replaceCurrentSceneSetup)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("WorldMapEditor: Stop play mode before changing the editor scene setup.");
            return false;
        }

        if (definition == null)
        {
            Debug.LogWarning("WorldMapEditor: No map definition was selected.");
            return false;
        }

        if (definition.Scene == null || !definition.Scene.HasScenePath)
        {
            Debug.LogWarning($"WorldMapEditor: Map '{definition.MapId}' has no scene assigned.");
            return false;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return false;
        }

        string selectedScenePath = definition.Scene.ScenePath;
        var scenePathsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            selectedScenePath
        };

        if (includeMainScene)
        {
            scenePathsToKeep.Add(MainScenePath);
        }

        if (includeNeighbors && catalog != null)
        {
            AddNeighborScenePaths(catalog, definition, scenePathsToKeep);
        }

        if (includeMainScene && replaceCurrentSceneSetup)
        {
            OpenScene(MainScenePath, OpenSceneMode.Single);
        }
        else if (includeMainScene)
        {
            OpenSceneIfNeeded(MainScenePath, OpenSceneMode.Additive);
        }

        OpenSceneIfNeeded(selectedScenePath, OpenSceneMode.Additive);

        if (includeNeighbors && catalog != null)
        {
            OpenNeighborScenes(catalog, definition);
        }

        if (replaceCurrentSceneSetup && !(includeMainScene && scenePathsToKeep.Contains(MainScenePath)))
        {
            CloseLoadedMapScenesExcept(scenePathsToKeep);
        }

        Scene selectedScene = SceneManager.GetSceneByPath(selectedScenePath);
        if (selectedScene.IsValid() && selectedScene.isLoaded)
        {
            EditorSceneManager.SetActiveScene(selectedScene);
        }

        if (TryFindLoadedMapRoot(definition.MapId, out WorldMapSceneAuthoring authoring))
        {
            SelectAndFrame(authoring);
        }
        else
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(selectedScenePath);
            if (sceneAsset != null)
            {
                Selection.activeObject = sceneAsset;
                EditorGUIUtility.PingObject(sceneAsset);
            }
        }

        return true;
    }

    public static bool OpenAllMapsForEditing(
        WorldMapCatalog catalog,
        WorldMapDefinition activeDefinition,
        bool includeMainScene,
        bool replaceCurrentSceneSetup)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("WorldMapEditor: Stop play mode before changing the editor scene setup.");
            return false;
        }

        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            Debug.LogWarning("WorldMapEditor: No map catalog was supplied.");
            return false;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return false;
        }

        bool openedFirstScene = false;
        if (includeMainScene)
        {
            OpenScene(MainScenePath, replaceCurrentSceneSetup ? OpenSceneMode.Single : OpenSceneMode.Additive);
            openedFirstScene = true;
        }

        int openedMapCount = 0;
        for (int index = 0; index < catalog.Maps.Count; index++)
        {
            WorldMapDefinition definition = catalog.Maps[index];
            if (definition?.Scene == null || !definition.Scene.HasScenePath)
            {
                continue;
            }

            OpenSceneMode openMode = openedFirstScene || !replaceCurrentSceneSetup
                ? OpenSceneMode.Additive
                : OpenSceneMode.Single;
            if (openMode == OpenSceneMode.Single)
            {
                OpenScene(definition.Scene.ScenePath, openMode);
            }
            else
            {
                OpenSceneIfNeeded(definition.Scene.ScenePath, openMode);
            }

            openedFirstScene = true;
            openedMapCount += 1;
        }

        WorldMapDefinition selectedDefinition = activeDefinition ?? GetDefinitionAt(catalog, 0);
        if (selectedDefinition?.Scene != null && selectedDefinition.Scene.HasScenePath)
        {
            Scene selectedScene = SceneManager.GetSceneByPath(selectedDefinition.Scene.ScenePath);
            if (selectedScene.IsValid() && selectedScene.isLoaded)
            {
                EditorSceneManager.SetActiveScene(selectedScene);
            }

            if (TryFindLoadedMapRoot(selectedDefinition.MapId, out WorldMapSceneAuthoring authoring))
            {
                SelectAndFrame(authoring);
            }
        }

        Debug.Log($"WorldMapEditor: Opened {openedMapCount} map scene(s) for editing.", catalog);
        return openedMapCount > 0;
    }

    public static bool SelectLoadedMapRoot(string mapId)
    {
        if (!TryFindLoadedMapRoot(mapId, out WorldMapSceneAuthoring authoring))
        {
            Debug.LogWarning($"WorldMapEditor: Map '{mapId}' is not loaded in the current scene setup.");
            return false;
        }

        SelectAndFrame(authoring);
        EditorSceneManager.SetActiveScene(authoring.gameObject.scene);
        return true;
    }

    public static bool TryFindLoadedMapRoot(string mapId, out WorldMapSceneAuthoring authoring)
    {
        authoring = null;
        string normalizedMapId = WorldMapCatalog.NormalizeMapId(mapId);
        if (string.IsNullOrWhiteSpace(normalizedMapId))
        {
            return false;
        }

        WorldMapSceneAuthoring[] authoringRoots = UnityEngine.Object.FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        for (int index = 0; index < authoringRoots.Length; index++)
        {
            WorldMapSceneAuthoring candidate = authoringRoots[index];
            if (candidate == null)
            {
                continue;
            }

            if (string.Equals(candidate.MapId, normalizedMapId, StringComparison.OrdinalIgnoreCase))
            {
                authoring = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool IsMapSceneLoaded(WorldMapDefinition definition)
    {
        if (definition?.Scene == null || !definition.Scene.HasScenePath)
        {
            return false;
        }

        Scene scene = SceneManager.GetSceneByPath(definition.Scene.ScenePath);
        return scene.IsValid() && scene.isLoaded;
    }

    public static bool IsMapSceneActive(WorldMapDefinition definition)
    {
        if (definition?.Scene == null || !definition.Scene.HasScenePath)
        {
            return false;
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        return activeScene.IsValid() &&
               activeScene.isLoaded &&
               string.Equals(activeScene.path, definition.Scene.ScenePath, StringComparison.OrdinalIgnoreCase);
    }

    public static WorldMapDefinition GetDefinitionAt(WorldMapCatalog catalog, int index)
    {
        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            return null;
        }

        return catalog.Maps[Mathf.Clamp(index, 0, catalog.Maps.Count - 1)];
    }

    public static string[] BuildMapOptions(WorldMapCatalog catalog)
    {
        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            return Array.Empty<string>();
        }

        string[] options = new string[catalog.Maps.Count];
        for (int index = 0; index < catalog.Maps.Count; index++)
        {
            WorldMapDefinition definition = catalog.Maps[index];
            string mapId = definition != null ? definition.MapId : "missing";
            string sceneName = definition?.Scene != null && definition.Scene.HasScenePath
                ? definition.Scene.SceneName
                : "No Scene";
            options[index] = $"{mapId} ({sceneName})";
        }

        return options;
    }

    private static void AddNeighborScenePaths(WorldMapCatalog catalog, WorldMapDefinition definition, HashSet<string> scenePaths)
    {
        MapTransitionDirection[] directions =
        {
            MapTransitionDirection.North,
            MapTransitionDirection.East,
            MapTransitionDirection.South,
            MapTransitionDirection.West
        };

        for (int index = 0; index < directions.Length; index++)
        {
            if (!catalog.TryGetAdjacent(definition.MapId, directions[index], out WorldMapDefinition adjacent) ||
                adjacent?.Scene == null ||
                !adjacent.Scene.HasScenePath)
            {
                continue;
            }

            scenePaths.Add(adjacent.Scene.ScenePath);
        }
    }

    private static void OpenNeighborScenes(WorldMapCatalog catalog, WorldMapDefinition definition)
    {
        var scenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddNeighborScenePaths(catalog, definition, scenePaths);
        foreach (string scenePath in scenePaths)
        {
            OpenSceneIfNeeded(scenePath, OpenSceneMode.Additive);
        }
    }

    private static Scene OpenSceneIfNeeded(string scenePath, OpenSceneMode mode)
    {
        Scene loadedScene = SceneManager.GetSceneByPath(scenePath);
        return loadedScene.IsValid() && loadedScene.isLoaded
            ? loadedScene
            : OpenScene(scenePath, mode);
    }

    private static Scene OpenScene(string scenePath, OpenSceneMode mode)
    {
        if (string.IsNullOrWhiteSpace(scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
        {
            Debug.LogWarning($"WorldMapEditor: Scene '{scenePath}' could not be found.");
            return default;
        }

        return EditorSceneManager.OpenScene(scenePath, mode);
    }

    private static void CloseLoadedMapScenesExcept(HashSet<string> scenePathsToKeep)
    {
        for (int sceneIndex = EditorSceneManager.sceneCount - 1; sceneIndex >= 0; sceneIndex--)
        {
            Scene scene = EditorSceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(scene.path))
            {
                continue;
            }

            if (!IsWorldMapScene(scene.path) || scenePathsToKeep.Contains(scene.path))
            {
                continue;
            }

            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static bool IsWorldMapScene(string scenePath)
    {
        return scenePath.Replace('\\', '/').StartsWith("Assets/Scenes/WorldMaps/Map_", StringComparison.OrdinalIgnoreCase);
    }

    private static void SelectAndFrame(WorldMapSceneAuthoring authoring)
    {
        Selection.activeGameObject = authoring.gameObject;
        EditorGUIUtility.PingObject(authoring.gameObject);

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        Bounds bounds = authoring.GetPlayableBoundsWorld();
        if (bounds.size == Vector3.zero)
        {
            bounds = new Bounds(authoring.transform.position, Vector3.one * 64f);
        }

        sceneView.Frame(bounds, false);
    }
}
#endif
