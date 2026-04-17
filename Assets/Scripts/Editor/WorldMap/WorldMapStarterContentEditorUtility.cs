#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WorldMapStarterContentEditorUtility
{
    private const string LegacyMenuPath = "Tools/World Map/Populate All Catalog Map Scenes With Starter Content";
    private const string MenuPath = "Tools/World Map/Populate All Catalog Map Scenes From MainScene Template";
    private const string DefaultTemplateMapId = "1-1";
    private const string ScopedMapIdPropertyName = "mapId";

    private static readonly string[] LegacyGeneratedRootNames =
    {
        "EnvironmentRoot",
        "PropsRoot",
        "SpawnRoot"
    };

    private sealed class PopulationSourceContext
    {
        public Scene SourceScene;
        public string SourceMapId = DefaultTemplateMapId;
        public List<GameObject> SourceRoots = new();
        public bool OpenedSourceScene;
    }

    [MenuItem(LegacyMenuPath)]
    [MenuItem(MenuPath)]
    private static void PopulateAllCatalogMapScenesMenu()
    {
        WorldMapCatalog catalog = AssetDatabase.LoadAssetAtPath<WorldMapCatalog>(WorldMapEditorSceneUtility.DefaultCatalogPath);
        int updatedSceneCount = PopulateCatalogMapScenes(catalog);
        Debug.Log($"WorldMapStarterContent: Applied MainScene template content to {updatedSceneCount} catalog map scene(s).", catalog);
    }

    public static int PopulateLoadedMapScenes()
    {
        CaptureSceneState(out string activeScenePath, out HashSet<string> initiallyLoadedScenePaths);
        if (!TryCreatePopulationSourceContext(
                AssetDatabase.LoadAssetAtPath<WorldMapCatalog>(WorldMapEditorSceneUtility.DefaultCatalogPath),
                initiallyLoadedScenePaths,
                out PopulationSourceContext sourceContext))
        {
            RestoreEditorSceneState(activeScenePath, initiallyLoadedScenePaths, null);
            return 0;
        }

        int updatedSceneCount = 0;
        try
        {
            WorldMapSceneAuthoring[] authoringRoots = UnityEngine.Object.FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
            Array.Sort(authoringRoots, CompareAuthoringRoots);
            for (int index = 0; index < authoringRoots.Length; index++)
            {
                WorldMapSceneAuthoring authoring = authoringRoots[index];
                if (authoring == null ||
                    !authoring.gameObject.scene.IsValid() ||
                    !authoring.gameObject.scene.isLoaded)
                {
                    continue;
                }

                PopulateScene(authoring, sourceContext);
                updatedSceneCount += 1;
            }
        }
        finally
        {
            RestoreEditorSceneState(activeScenePath, initiallyLoadedScenePaths, sourceContext);
            AssetDatabase.SaveAssets();
        }

        return updatedSceneCount;
    }

    public static int PopulateCatalogMapScenes(WorldMapCatalog catalog)
    {
        if (catalog == null || catalog.Maps == null)
        {
            Debug.LogWarning("WorldMapStarterContent: No WorldMapCatalog was supplied.");
            return 0;
        }

        CaptureSceneState(out string activeScenePath, out HashSet<string> initiallyLoadedScenePaths);
        if (!TryCreatePopulationSourceContext(catalog, initiallyLoadedScenePaths, out PopulationSourceContext sourceContext))
        {
            RestoreEditorSceneState(activeScenePath, initiallyLoadedScenePaths, null);
            return 0;
        }

        int updatedSceneCount = 0;
        try
        {
            IReadOnlyList<WorldMapDefinition> maps = catalog.Maps;
            for (int index = 0; index < maps.Count; index++)
            {
                WorldMapDefinition definition = maps[index];
                string scenePath = definition?.Scene?.ScenePath;
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    continue;
                }

                Scene mapScene = SceneManager.GetSceneByPath(scenePath);
                bool openedForPopulation = false;
                if (!mapScene.IsValid() || !mapScene.isLoaded)
                {
                    mapScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    openedForPopulation = true;
                }

                if (!TryFindAuthoringRoot(mapScene, out WorldMapSceneAuthoring authoring))
                {
                    Debug.LogWarning($"WorldMapStarterContent: Scene '{scenePath}' has no WorldMapSceneAuthoring root.");
                    if (openedForPopulation)
                    {
                        EditorSceneManager.CloseScene(mapScene, true);
                    }

                    continue;
                }

                PopulateScene(authoring, sourceContext);
                EditorSceneManager.SaveScene(mapScene);
                updatedSceneCount += 1;

                if (openedForPopulation && !initiallyLoadedScenePaths.Contains(scenePath))
                {
                    EditorSceneManager.CloseScene(mapScene, true);
                }
            }
        }
        finally
        {
            RestoreEditorSceneState(activeScenePath, initiallyLoadedScenePaths, sourceContext);
            AssetDatabase.SaveAssets();
        }

        return updatedSceneCount;
    }

    public static void PopulateScene(WorldMapSceneAuthoring authoring)
    {
        CaptureSceneState(out string activeScenePath, out HashSet<string> initiallyLoadedScenePaths);
        if (!TryCreatePopulationSourceContext(
                AssetDatabase.LoadAssetAtPath<WorldMapCatalog>(WorldMapEditorSceneUtility.DefaultCatalogPath),
                initiallyLoadedScenePaths,
                out PopulationSourceContext sourceContext))
        {
            RestoreEditorSceneState(activeScenePath, initiallyLoadedScenePaths, null);
            return;
        }

        try
        {
            PopulateScene(authoring, sourceContext);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            RestoreEditorSceneState(activeScenePath, initiallyLoadedScenePaths, sourceContext);
        }
    }

    private static void PopulateScene(WorldMapSceneAuthoring authoring, PopulationSourceContext sourceContext)
    {
        if (authoring == null || sourceContext == null)
        {
            return;
        }

        EnsureAnchor(authoring.transform, "NorthArrivalAnchor", new Vector3(0f, 0f, 216f), Quaternion.Euler(0f, 180f, 0f));
        EnsureAnchor(authoring.transform, "EastArrivalAnchor", new Vector3(216f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
        EnsureAnchor(authoring.transform, "SouthArrivalAnchor", new Vector3(0f, 0f, -216f), Quaternion.identity);
        EnsureAnchor(authoring.transform, "WestArrivalAnchor", new Vector3(-216f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        EnsureAnchor(authoring.transform, "RespawnAnchor", Vector3.zero, Quaternion.identity);

        authoring.RefreshEditorState();
        string targetMapId = string.IsNullOrWhiteSpace(authoring.MapId)
            ? WorldMapCatalog.NormalizeMapId(authoring.gameObject.scene.name.Replace("Map_", string.Empty))
            : authoring.MapId;

        ClearExistingTemplateRoots(authoring.transform, sourceContext.SourceRoots);
        string terrainDataFolderPath = RecreateGeneratedTerrainDataFolder(authoring.gameObject.scene.path);

        for (int index = 0; index < sourceContext.SourceRoots.Count; index++)
        {
            GameObject sourceRoot = sourceContext.SourceRoots[index];
            if (sourceRoot == null)
            {
                continue;
            }

            GameObject clone = CloneTemplateRootIntoScene(sourceRoot, authoring.transform, authoring.gameObject.scene);
            if (clone == null)
            {
                continue;
            }

            RetargetScopedMapIds(clone, targetMapId);
            DuplicateTerrainDataAssets(clone, targetMapId, terrainDataFolderPath);
        }

        authoring.RefreshEditorState();
        EditorUtility.SetDirty(authoring);
        EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
    }

    private static bool TryCreatePopulationSourceContext(
        WorldMapCatalog catalog,
        HashSet<string> initiallyLoadedScenePaths,
        out PopulationSourceContext sourceContext)
    {
        sourceContext = null;
        string sourceScenePath = WorldMapEditorSceneUtility.MainScenePath;
        if (string.IsNullOrWhiteSpace(sourceScenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(sourceScenePath) == null)
        {
            Debug.LogWarning($"WorldMapStarterContent: Could not locate source scene '{sourceScenePath}'.");
            return false;
        }

        Scene sourceScene = SceneManager.GetSceneByPath(sourceScenePath);
        bool openedSourceScene = false;
        if (!sourceScene.IsValid() || !sourceScene.isLoaded)
        {
            sourceScene = EditorSceneManager.OpenScene(sourceScenePath, OpenSceneMode.Additive);
            openedSourceScene = true;
        }

        string sourceMapId = ResolveSourceMapId(catalog);
        List<GameObject> sourceRoots = CollectTemplateSourceRoots(sourceScene, sourceMapId);
        if (sourceRoots.Count == 0)
        {
            Debug.LogWarning(
                $"WorldMapStarterContent: MainScene has no scoped content roots for map '{sourceMapId}'. " +
                "Add WorldMapContentScope to the authored template roots in MainScene before populating map scenes.");

            if (openedSourceScene && !initiallyLoadedScenePaths.Contains(sourceScenePath))
            {
                EditorSceneManager.CloseScene(sourceScene, true);
            }

            return false;
        }

        sourceContext = new PopulationSourceContext
        {
            SourceScene = sourceScene,
            SourceMapId = sourceMapId,
            SourceRoots = sourceRoots,
            OpenedSourceScene = openedSourceScene
        };

        return true;
    }

    private static void CaptureSceneState(out string activeScenePath, out HashSet<string> initiallyLoadedScenePaths)
    {
        Scene activeScene = EditorSceneManager.GetActiveScene();
        activeScenePath = activeScene.IsValid() ? activeScene.path : string.Empty;
        initiallyLoadedScenePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int sceneIndex = 0; sceneIndex < EditorSceneManager.sceneCount; sceneIndex++)
        {
            Scene loadedScene = EditorSceneManager.GetSceneAt(sceneIndex);
            if (loadedScene.IsValid() && loadedScene.isLoaded && !string.IsNullOrWhiteSpace(loadedScene.path))
            {
                initiallyLoadedScenePaths.Add(loadedScene.path);
            }
        }
    }

    private static void RestoreEditorSceneState(
        string activeScenePath,
        HashSet<string> initiallyLoadedScenePaths,
        PopulationSourceContext sourceContext)
    {
        if (sourceContext != null &&
            sourceContext.OpenedSourceScene &&
            sourceContext.SourceScene.IsValid() &&
            sourceContext.SourceScene.isLoaded &&
            !initiallyLoadedScenePaths.Contains(sourceContext.SourceScene.path))
        {
            EditorSceneManager.CloseScene(sourceContext.SourceScene, true);
        }

        if (string.IsNullOrWhiteSpace(activeScenePath))
        {
            return;
        }

        Scene restoredActiveScene = SceneManager.GetSceneByPath(activeScenePath);
        if (restoredActiveScene.IsValid() && restoredActiveScene.isLoaded)
        {
            EditorSceneManager.SetActiveScene(restoredActiveScene);
        }
    }

    private static string ResolveSourceMapId(WorldMapCatalog catalog)
    {
        string sourceMapId = catalog != null ? catalog.StartingMapId : string.Empty;
        if (string.IsNullOrWhiteSpace(sourceMapId))
        {
            sourceMapId = DefaultTemplateMapId;
        }

        return WorldMapCatalog.NormalizeMapId(sourceMapId);
    }

    private static List<GameObject> CollectTemplateSourceRoots(Scene sourceScene, string sourceMapId)
    {
        var roots = new List<GameObject>();
        WorldMapContentScope[] contentScopes = UnityEngine.Object.FindObjectsByType<WorldMapContentScope>(FindObjectsSortMode.None);
        for (int index = 0; index < contentScopes.Length; index++)
        {
            WorldMapContentScope contentScope = contentScopes[index];
            if (contentScope == null ||
                contentScope.gameObject.scene != sourceScene ||
                contentScope.GetComponentInParent<WorldMapSceneAuthoring>() != null ||
                !string.Equals(contentScope.MapId, sourceMapId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            WorldMapContentScope parentScope = contentScope.transform.parent != null
                ? contentScope.transform.parent.GetComponentInParent<WorldMapContentScope>()
                : null;
            if (parentScope != null && !ReferenceEquals(parentScope, contentScope))
            {
                continue;
            }

            roots.Add(contentScope.gameObject);
        }

        roots.Sort((left, right) =>
        {
            int siblingComparison = left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex());
            return siblingComparison != 0
                ? siblingComparison
                : string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
        });

        return roots;
    }

    private static void ClearExistingTemplateRoots(Transform authoringRoot, IReadOnlyList<GameObject> sourceRoots)
    {
        var namesToReplace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < LegacyGeneratedRootNames.Length; index++)
        {
            namesToReplace.Add(LegacyGeneratedRootNames[index]);
        }

        for (int index = 0; index < sourceRoots.Count; index++)
        {
            GameObject sourceRoot = sourceRoots[index];
            if (sourceRoot != null)
            {
                namesToReplace.Add(sourceRoot.name);
            }
        }

        for (int childIndex = authoringRoot.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = authoringRoot.GetChild(childIndex);
            if (child == null || !namesToReplace.Contains(child.name))
            {
                continue;
            }

            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    private static string RecreateGeneratedTerrainDataFolder(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            return string.Empty;
        }

        string sceneDirectory = NormalizeAssetPath(Path.GetDirectoryName(scenePath));
        string folderName = $"{Path.GetFileNameWithoutExtension(scenePath)}_TerrainData";
        if (string.IsNullOrWhiteSpace(sceneDirectory))
        {
            return string.Empty;
        }

        string folderPath = $"{sceneDirectory}/{folderName}";
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            var assetPaths = new List<string>(guids.Length);
            for (int index = 0; index < guids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (!string.IsNullOrWhiteSpace(assetPath) &&
                    !string.Equals(assetPath, folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    assetPaths.Add(assetPath);
                }
            }

            assetPaths.Sort((left, right) => string.Compare(right, left, StringComparison.OrdinalIgnoreCase));
            for (int index = 0; index < assetPaths.Count; index++)
            {
                AssetDatabase.DeleteAsset(assetPaths[index]);
            }
        }
        else
        {
            EnsureFolder(sceneDirectory, folderName);
        }

        return folderPath;
    }

    private static GameObject CloneTemplateRootIntoScene(GameObject sourceRoot, Transform targetParent, Scene targetScene)
    {
        GameObject clone = UnityEngine.Object.Instantiate(sourceRoot);
        clone.name = sourceRoot.name;
        EditorSceneManager.MoveGameObjectToScene(clone, targetScene);
        clone.transform.SetParent(targetParent, false);
        Undo.RegisterCreatedObjectUndo(clone, $"Populate {clone.name}");
        return clone;
    }

    private static void RetargetScopedMapIds(GameObject root, string targetMapId)
    {
        WorldMapContentScope[] contentScopes = root.GetComponentsInChildren<WorldMapContentScope>(true);
        for (int index = 0; index < contentScopes.Length; index++)
        {
            WorldMapContentScope contentScope = contentScopes[index];
            if (contentScope == null)
            {
                continue;
            }

            SerializedObject serializedObject = new SerializedObject(contentScope);
            SerializedProperty mapIdProperty = serializedObject.FindProperty(ScopedMapIdPropertyName);
            if (mapIdProperty == null)
            {
                continue;
            }

            mapIdProperty.stringValue = targetMapId;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(contentScope);
        }
    }

    private static void DuplicateTerrainDataAssets(GameObject root, string targetMapId, string terrainDataFolderPath)
    {
        if (root == null || string.IsNullOrWhiteSpace(terrainDataFolderPath))
        {
            return;
        }

        var clonedTerrainData = new Dictionary<TerrainData, TerrainData>();

        Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true);
        for (int index = 0; index < terrains.Length; index++)
        {
            Terrain terrain = terrains[index];
            if (terrain == null || terrain.terrainData == null)
            {
                continue;
            }

            TerrainData duplicatedTerrainData = GetOrCreateTerrainDataClone(
                terrain.terrainData,
                terrain.name,
                targetMapId,
                terrainDataFolderPath,
                clonedTerrainData);

            terrain.terrainData = duplicatedTerrainData;
            EditorUtility.SetDirty(terrain);
        }

        TerrainCollider[] colliders = root.GetComponentsInChildren<TerrainCollider>(true);
        for (int index = 0; index < colliders.Length; index++)
        {
            TerrainCollider collider = colliders[index];
            if (collider == null || collider.terrainData == null)
            {
                continue;
            }

            TerrainData duplicatedTerrainData = GetOrCreateTerrainDataClone(
                collider.terrainData,
                collider.name,
                targetMapId,
                terrainDataFolderPath,
                clonedTerrainData);

            collider.terrainData = duplicatedTerrainData;
            EditorUtility.SetDirty(collider);
        }
    }

    private static TerrainData GetOrCreateTerrainDataClone(
        TerrainData sourceTerrainData,
        string terrainObjectName,
        string targetMapId,
        string terrainDataFolderPath,
        Dictionary<TerrainData, TerrainData> clonedTerrainData)
    {
        if (clonedTerrainData.TryGetValue(sourceTerrainData, out TerrainData existingClone) && existingClone != null)
        {
            return existingClone;
        }

        string sourceTerrainDataPath = AssetDatabase.GetAssetPath(sourceTerrainData);
        string baseFileName = $"{SanitizeFileName(targetMapId)}_{SanitizeFileName(terrainObjectName)}.asset";
        string targetTerrainDataPath = AssetDatabase.GenerateUniqueAssetPath($"{terrainDataFolderPath}/{baseFileName}");

        if (!string.IsNullOrWhiteSpace(sourceTerrainDataPath))
        {
            AssetDatabase.CopyAsset(sourceTerrainDataPath, targetTerrainDataPath);
        }
        else
        {
            TerrainData duplicatedTerrainData = UnityEngine.Object.Instantiate(sourceTerrainData);
            duplicatedTerrainData.name = $"{targetMapId}_{terrainObjectName}";
            AssetDatabase.CreateAsset(duplicatedTerrainData, targetTerrainDataPath);
        }

        TerrainData loadedTerrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(targetTerrainDataPath);
        clonedTerrainData[sourceTerrainData] = loadedTerrainData;
        return loadedTerrainData;
    }

    private static int CompareAuthoringRoots(WorldMapSceneAuthoring left, WorldMapSceneAuthoring right)
    {
        string leftPath = left != null ? left.gameObject.scene.path : string.Empty;
        string rightPath = right != null ? right.gameObject.scene.path : string.Empty;
        return string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindAuthoringRoot(Scene scene, out WorldMapSceneAuthoring authoring)
    {
        authoring = null;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        WorldMapSceneAuthoring[] authoringRoots = UnityEngine.Object.FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        for (int index = 0; index < authoringRoots.Length; index++)
        {
            WorldMapSceneAuthoring candidate = authoringRoots[index];
            if (candidate == null || candidate.gameObject.scene != scene)
            {
                continue;
            }

            authoring = candidate;
            return true;
        }

        return false;
    }

    private static Transform EnsureAnchor(Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
    {
        Transform existing = parent.Find(name);
        GameObject anchor = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null)
        {
            anchor.transform.SetParent(parent, false);
        }

        anchor.transform.localPosition = localPosition;
        anchor.transform.localRotation = localRotation;
        anchor.transform.localScale = Vector3.one;
        anchor.SetActive(true);
        return anchor.transform;
    }

    private static void EnsureFolder(string parentPath, string folderName)
    {
        string normalizedParentPath = NormalizeAssetPath(parentPath);
        if (string.IsNullOrWhiteSpace(normalizedParentPath))
        {
            return;
        }

        string folderPath = $"{normalizedParentPath}/{folderName}";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(normalizedParentPath, folderName);
        }
    }

    private static string NormalizeAssetPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/');
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Terrain";
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = value.Trim().ToCharArray();
        for (int index = 0; index < sanitized.Length; index++)
        {
            if (Array.IndexOf(invalidCharacters, sanitized[index]) >= 0 || sanitized[index] == '/')
            {
                sanitized[index] = '_';
            }
        }

        return new string(sanitized);
    }
}
#endif
