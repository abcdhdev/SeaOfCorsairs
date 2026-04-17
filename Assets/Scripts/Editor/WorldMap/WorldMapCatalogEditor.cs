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

        if (GUILayout.Button("Open World Map Editor Window"))
        {
            WorldMapEditorWindow.OpenWindow();
        }

        if (GUILayout.Button("Open MainScene + Selected Map Scene"))
        {
            OpenSelectedMapScene(catalog, includeNeighbors: false);
        }

        if (GUILayout.Button("Open MainScene + Selected Map + Neighbors"))
        {
            OpenSelectedMapScene(catalog, includeNeighbors: true);
        }

        if (GUILayout.Button("Populate Loaded Map Scenes From MainScene Template"))
        {
            int updatedSceneCount = WorldMapStarterContentEditorUtility.PopulateLoadedMapScenes();
            Debug.Log($"WorldMapCatalog: Applied MainScene template content to {updatedSceneCount} loaded map scene(s).", catalog);
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

        WorldMapEditorSceneUtility.OpenMapForEditing(
            catalog,
            selectedDefinition,
            includeMainScene: true,
            includeNeighbors,
            replaceCurrentSceneSetup: true);
    }

    private void DrawSelectedMapPicker(WorldMapCatalog catalog)
    {
        if (catalog == null || catalog.Maps == null || catalog.Maps.Count == 0)
        {
            return;
        }

        string[] mapOptions = WorldMapEditorSceneUtility.BuildMapOptions(catalog);
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

        if (WorldMapEditorSceneUtility.SelectLoadedMapRoot(selectedDefinition.MapId))
        {
            return;
        }

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(selectedDefinition.Scene.ScenePath);
        if (sceneAsset != null)
        {
            EditorGUIUtility.PingObject(sceneAsset);
            Selection.activeObject = sceneAsset;
        }
    }

    private static WorldMapDefinition GetSelectedDefinition(WorldMapCatalog catalog, int selectedIndex)
    {
        return WorldMapEditorSceneUtility.GetDefinitionAt(catalog, selectedIndex);
    }
}
#endif
