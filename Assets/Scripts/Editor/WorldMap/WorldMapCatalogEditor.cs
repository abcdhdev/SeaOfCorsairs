#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(WorldMapCatalog))]
public sealed class WorldMapCatalogEditor : Editor
{
    private int selectedMapIndex;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        WorldMapCatalog catalog = (WorldMapCatalog)target;
        DrawSelectedMapPicker(catalog);

        if (GUILayout.Button("Populate Default 28-Map Grid"))
        {
            Undo.RecordObject(catalog, "Populate Default World Map Grid");
            catalog.GenerateDefaultGrid();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        if (GUILayout.Button("Validate Catalog"))
        {
            List<string> issues = catalog.ValidateCatalog();
            if (issues.Count == 0)
            {
                Debug.Log("WorldMapCatalog: No validation issues found.", catalog);
            }
            else
            {
                for (int index = 0; index < issues.Count; index++)
                {
                    Debug.LogWarning($"WorldMapCatalog: {issues[index]}", catalog);
                }
            }
        }

        if (GUILayout.Button("Validate Catalog + Loaded Map Scenes"))
        {
            ValidateCatalogAndLoadedScenes(catalog);
        }

        if (GUILayout.Button("Open MainScene + Selected Map Scene"))
        {
            OpenSelectedMapScene(catalog, includeNeighbors: false);
        }

        if (GUILayout.Button("Open MainScene + Selected Map + Neighbors"))
        {
            OpenSelectedMapScene(catalog, includeNeighbors: true);
        }

        if (GUILayout.Button("Populate Loaded Map Scenes With Starter Content"))
        {
            int updatedSceneCount = WorldMapStarterContentEditorUtility.PopulateLoadedMapScenes();
            Debug.Log($"WorldMapCatalog: Applied starter content to {updatedSceneCount} loaded map scene(s).", catalog);
        }

        if (GUILayout.Button("Ping Selected Map Entry Or Loaded Root"))
        {
            PingSelectedMap(catalog);
        }
    }

    private void OpenSelectedMapScene(WorldMapCatalog catalog, bool includeNeighbors)
    {
        WorldMapDefinition selectedDefinition = GetSelectedDefinition(catalog, selectedMapIndex);
        if (selectedDefinition == null)
        {
            return;
        }

        if (selectedDefinition.Scene == null || !selectedDefinition.Scene.HasScenePath)
        {
            Debug.LogWarning("WorldMapCatalog: The selected map entry does not have a scene path assigned.", catalog);
            return;
        }

        EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity", OpenSceneMode.Single);
        EditorSceneManager.OpenScene(selectedDefinition.Scene.ScenePath, OpenSceneMode.Additive);

        if (!includeNeighbors)
        {
            return;
        }

        MapTransitionDirection[] directions =
        {
            MapTransitionDirection.North,
            MapTransitionDirection.East,
            MapTransitionDirection.South,
            MapTransitionDirection.West
        };

        for (int index = 0; index < directions.Length; index++)
        {
            if (!catalog.TryGetAdjacent(selectedDefinition.MapId, directions[index], out WorldMapDefinition adjacent) ||
                adjacent?.Scene == null ||
                !adjacent.Scene.HasScenePath)
            {
                continue;
            }

            EditorSceneManager.OpenScene(adjacent.Scene.ScenePath, OpenSceneMode.Additive);
        }
    }

    private void DrawSelectedMapPicker(WorldMapCatalog catalog)
    {
        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            return;
        }

        string[] mapOptions = BuildMapOptions(catalog);
        selectedMapIndex = Mathf.Clamp(selectedMapIndex, 0, mapOptions.Length - 1);
        selectedMapIndex = EditorGUILayout.Popup("Selected Map", selectedMapIndex, mapOptions);
    }

    private static void ValidateCatalogAndLoadedScenes(WorldMapCatalog catalog)
    {
        var issues = new List<string>();
        if (catalog != null)
        {
            issues.AddRange(catalog.ValidateCatalog());
        }

        WorldMapSceneAuthoring[] authoringRoots = FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        var rootCountsByScenePath = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < authoringRoots.Length; index++)
        {
            WorldMapSceneAuthoring authoring = authoringRoots[index];
            if (authoring == null)
            {
                continue;
            }

            string scenePath = authoring.gameObject.scene.path ?? string.Empty;
            if (!rootCountsByScenePath.TryAdd(scenePath, 1))
            {
                rootCountsByScenePath[scenePath] += 1;
            }

            issues.AddRange(authoring.ValidateAuthoring(catalog));
        }

        foreach (KeyValuePair<string, int> pair in rootCountsByScenePath)
        {
            if (pair.Value > 1)
            {
                issues.Add($"Scene '{pair.Key}' has {pair.Value} WorldMapSceneAuthoring roots. Expected exactly one.");
            }
        }

        for (int sceneIndex = 0; sceneIndex < EditorSceneManager.sceneCount; sceneIndex++)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(scene.path))
            {
                continue;
            }

            if (!scene.name.StartsWith("Map_", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!rootCountsByScenePath.ContainsKey(scene.path))
            {
                issues.Add($"Loaded map scene '{scene.name}' is missing a WorldMapSceneAuthoring root.");
            }
        }

        if (issues.Count == 0)
        {
            Debug.Log("WorldMapCatalog: No validation issues found for the catalog or loaded map scenes.", catalog);
            return;
        }

        for (int index = 0; index < issues.Count; index++)
        {
            Debug.LogWarning($"WorldMapCatalog: {issues[index]}", catalog);
        }
    }

    private void PingSelectedMap(WorldMapCatalog catalog)
    {
        WorldMapDefinition selectedDefinition = GetSelectedDefinition(catalog, selectedMapIndex);
        if (selectedDefinition == null)
        {
            return;
        }

        WorldMapSceneAuthoring[] authoringRoots = FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        for (int index = 0; index < authoringRoots.Length; index++)
        {
            WorldMapSceneAuthoring authoring = authoringRoots[index];
            if (authoring == null)
            {
                continue;
            }

            if (string.Equals(authoring.MapId, selectedDefinition.MapId, System.StringComparison.OrdinalIgnoreCase))
            {
                EditorGUIUtility.PingObject(authoring.gameObject);
                Selection.activeGameObject = authoring.gameObject;
                return;
            }
        }

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(selectedDefinition.Scene.ScenePath);
        if (sceneAsset != null)
        {
            EditorGUIUtility.PingObject(sceneAsset);
            Selection.activeObject = sceneAsset;
        }
    }

    private static string[] BuildMapOptions(WorldMapCatalog catalog)
    {
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

    private static WorldMapDefinition GetSelectedDefinition(WorldMapCatalog catalog, int selectedIndex)
    {
        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            return null;
        }

        int clampedIndex = Mathf.Clamp(selectedIndex, 0, catalog.Maps.Count - 1);
        return catalog.Maps[clampedIndex];
    }
}
#endif
