#if UNITY_EDITOR
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WorldMapStarterContentEditorUtility
{
    private const string NpcSpawnerPrefabPath = "Assets/Prefabs/Bak/NPCSpawner.prefab";
    private const string MonsterPrefabPath = "Assets/Prefabs/Monster.prefab";
    private const string RewardBoxPrefabPath = "Assets/Prefabs/Box.prefab";
    private const string GroundMaterialPath = "Assets/Materials/Terrain.mat";
    private const string PropMaterialPath = "Assets/Procedural Worlds/Packages - Install/Asset Samples/Procedural Worlds/Content Resources/PW_Stone_01/PW_Stone_01.mat";

    public static int PopulateLoadedMapScenes()
    {
        int updatedSceneCount = 0;
        WorldMapSceneAuthoring[] authoringRoots = Object.FindObjectsByType<WorldMapSceneAuthoring>(FindObjectsSortMode.None);
        for (int index = 0; index < authoringRoots.Length; index++)
        {
            WorldMapSceneAuthoring authoring = authoringRoots[index];
            if (authoring == null ||
                !authoring.gameObject.scene.IsValid() ||
                !authoring.gameObject.scene.isLoaded)
            {
                continue;
            }

            PopulateScene(authoring);
            updatedSceneCount += 1;
        }

        return updatedSceneCount;
    }

    public static void PopulateScene(WorldMapSceneAuthoring authoring)
    {
        if (authoring == null)
        {
            return;
        }

        Transform root = authoring.transform;
        Material groundMaterial = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
        Material propMaterial = AssetDatabase.LoadAssetAtPath<Material>(PropMaterialPath);

        EnsureAnchor(root, "NorthArrivalAnchor", new Vector3(0f, 0f, 216f), Quaternion.Euler(0f, 180f, 0f));
        EnsureAnchor(root, "EastArrivalAnchor", new Vector3(216f, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
        EnsureAnchor(root, "SouthArrivalAnchor", new Vector3(0f, 0f, -216f), Quaternion.identity);
        EnsureAnchor(root, "WestArrivalAnchor", new Vector3(-216f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
        EnsureAnchor(root, "RespawnAnchor", Vector3.zero, Quaternion.identity);

        GameObject environmentRoot = EnsureEmptyChild(root, "EnvironmentRoot", Vector3.zero);
        GameObject propsRoot = EnsureEmptyChild(root, "PropsRoot", Vector3.zero);
        GameObject spawnRoot = EnsureEmptyChild(root, "SpawnRoot", Vector3.zero);

        EnsureVisualPrimitive(environmentRoot.transform, "StarterIslandBase", PrimitiveType.Cube, new Vector3(0f, -7f, 0f), Quaternion.identity, new Vector3(96f, 10f, 96f), groundMaterial);
        EnsureVisualPrimitive(environmentRoot.transform, "StarterIslandShelf", PrimitiveType.Cube, new Vector3(0f, -3f, 22f), Quaternion.identity, new Vector3(132f, 2f, 168f), groundMaterial);

        EnsureVisualPrimitive(propsRoot.transform, "StarterRockNorth", PrimitiveType.Cube, new Vector3(54f, 8f, 78f), Quaternion.Euler(0f, 24f, 0f), new Vector3(14f, 18f, 12f), propMaterial);
        EnsureVisualPrimitive(propsRoot.transform, "StarterRockWest", PrimitiveType.Cube, new Vector3(-72f, 5f, -38f), Quaternion.Euler(0f, -18f, 0f), new Vector3(18f, 12f, 18f), propMaterial);
        EnsureVisualPrimitive(propsRoot.transform, "StarterJettyEast", PrimitiveType.Cube, new Vector3(86f, 1f, 6f), Quaternion.Euler(0f, 12f, 0f), new Vector3(26f, 2f, 8f), propMaterial);
        EnsureVisualPrimitive(propsRoot.transform, "StarterJettyWest", PrimitiveType.Cube, new Vector3(-90f, 1f, -12f), Quaternion.Euler(0f, -16f, 0f), new Vector3(22f, 2f, 8f), propMaterial);

        GameObject monsterSpawnCenter = EnsureEmptyChild(spawnRoot.transform, "MonsterSpawnCenter", new Vector3(72f, 0f, 74f));
        GameObject rewardSpawnCenter = EnsureEmptyChild(spawnRoot.transform, "RewardSpawnCenter", new Vector3(-84f, 0f, 46f));
        GameObject playerSpawnPoint = EnsureEmptyChild(spawnRoot.transform, "PlayerSpawnPoint", Vector3.zero);
        EnsureComponent<PlayerSpawnPoint>(playerSpawnPoint);

        NPCSpawner npcSpawner = EnsureNpcSpawner(spawnRoot.transform);
        MonsterSpawner monsterSpawner = EnsureComponent<MonsterSpawner>(EnsureEmptyChild(spawnRoot.transform, "MonsterSpawner", new Vector3(64f, 0f, 68f)));
        SeaRewardBoxSpawner rewardBoxSpawner = EnsureComponent<SeaRewardBoxSpawner>(EnsureEmptyChild(spawnRoot.transform, "RewardBoxSpawner", new Vector3(-76f, 0f, 42f)));

        EnsureNpcSpawnPoint(npcSpawner.transform, "NpcSpawnPoint_A", new Vector3(-32f, 0f, 18f));
        EnsureNpcSpawnPoint(npcSpawner.transform, "NpcSpawnPoint_B", new Vector3(-10f, 0f, 42f));
        EnsureNpcSpawnPoint(npcSpawner.transform, "NpcSpawnPoint_C", new Vector3(18f, 0f, 12f));

        ConfigureMonsterSpawner(monsterSpawner, monsterSpawnCenter.transform);
        ConfigureRewardBoxSpawner(rewardBoxSpawner, rewardSpawnCenter.transform);
        ConfigureNpcSpawner(npcSpawner);

        authoring.RefreshEditorState();
        EditorUtility.SetDirty(authoring);
        EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
    }

    private static void ConfigureNpcSpawner(NPCSpawner npcSpawner)
    {
        if (npcSpawner == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(npcSpawner);
        SetFloat(serializedObject, "spawnRadius", 140f);
        SetInt(serializedObject, "spawnCount", 3);
        SetFloat(serializedObject, "navMeshSampleDistance", 3f);
        SetBool(serializedObject, "preferAuthoredSpawnPoints", true);
        SetBool(serializedObject, "includeChildSpawnPoints", true);
        SetFloat(serializedObject, "additionalWaterlineOffset", 0.1f);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(npcSpawner);
    }

    private static void ConfigureMonsterSpawner(MonsterSpawner monsterSpawner, Transform spawnCenter)
    {
        if (monsterSpawner == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(monsterSpawner);
        SetObjectReference(serializedObject, "monsterNetworkPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabPath));
        SetInt(serializedObject, "spawnCount", 2);
        SetFloat(serializedObject, "spawnRadius", 150f);
        SetFloat(serializedObject, "navMeshSampleDistance", 3f);
        SetObjectReference(serializedObject, "spawnCenter", spawnCenter);
        SetFloat(serializedObject, "additionalWaterlineOffset", 0.1f);
        SetFloat(serializedObject, "respawnDelaySeconds", 30f);
        SetFloat(serializedObject, "respawnRetryIntervalSeconds", 2f);
        SetFloat(serializedObject, "spawnClearanceRadius", 16f);
        SetBool(serializedObject, "pauseSpawningWhenMapEmpty", true);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(monsterSpawner);
    }

    private static void ConfigureRewardBoxSpawner(SeaRewardBoxSpawner rewardBoxSpawner, Transform spawnCenter)
    {
        if (rewardBoxSpawner == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(rewardBoxSpawner);
        SetObjectReference(serializedObject, "boxNetworkPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(RewardBoxPrefabPath));
        SetInt(serializedObject, "spawnCount", 4);
        SetFloat(serializedObject, "spawnRadius", 150f);
        SetFloat(serializedObject, "navMeshSampleDistance", 3f);
        SetObjectReference(serializedObject, "spawnCenter", spawnCenter);
        SetFloat(serializedObject, "additionalWaterlineOffset", 0.15f);
        SetFloat(serializedObject, "respawnDelaySeconds", 15f);
        SetFloat(serializedObject, "respawnRetryIntervalSeconds", 2f);
        SetFloat(serializedObject, "spawnClearanceRadius", 12f);
        SetBool(serializedObject, "pauseSpawningWhenMapEmpty", true);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rewardBoxSpawner);
    }

    private static NPCSpawner EnsureNpcSpawner(Transform parent)
    {
        NPCSpawner existingSpawner = parent.GetComponentInChildren<NPCSpawner>(true);
        if (existingSpawner != null)
        {
            return existingSpawner;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcSpawnerPrefabPath);
        GameObject instance = null;
        if (prefab != null)
        {
            instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            instance.transform.SetParent(parent, false);
            instance.name = "NPCSpawner";
        }
        else
        {
            instance = EnsureEmptyChild(parent, "NPCSpawner", Vector3.zero);
            EnsureComponent<NetworkObject>(instance);
            EnsureComponent<NPCSpawner>(instance);
        }

        instance.transform.localPosition = new Vector3(-14f, 0f, 24f);
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        return instance.GetComponent<NPCSpawner>();
    }

    private static NpcSpawnPoint EnsureNpcSpawnPoint(Transform parent, string name, Vector3 localPosition)
    {
        GameObject point = EnsureEmptyChild(parent, name, localPosition);
        NpcSpawnPoint spawnPoint = EnsureComponent<NpcSpawnPoint>(point);
        EditorUtility.SetDirty(spawnPoint);
        return spawnPoint;
    }

    private static Transform EnsureAnchor(Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
    {
        GameObject anchor = EnsureEmptyChild(parent, name, localPosition);
        anchor.transform.localRotation = localRotation;
        anchor.transform.localScale = Vector3.one;
        return anchor.transform;
    }

    private static GameObject EnsureEmptyChild(Transform parent, string name, Vector3 localPosition)
    {
        Transform existing = parent.Find(name);
        GameObject result = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null)
        {
            result.transform.SetParent(parent, false);
        }

        result.transform.localPosition = localPosition;
        result.transform.localRotation = Quaternion.identity;
        result.transform.localScale = Vector3.one;
        result.SetActive(true);
        return result;
    }

    private static GameObject EnsureVisualPrimitive(Transform parent, string name, PrimitiveType primitiveType, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material sharedMaterial)
    {
        Transform existing = parent.Find(name);
        GameObject visual = existing != null ? existing.gameObject : GameObject.CreatePrimitive(primitiveType);
        if (existing == null)
        {
            visual.name = name;
            visual.transform.SetParent(parent, false);
        }

        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
        {
            Object.DestroyImmediate(collider);
        }

        visual.transform.localPosition = localPosition;
        visual.transform.localRotation = localRotation;
        visual.transform.localScale = localScale;

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null && sharedMaterial != null)
        {
            renderer.sharedMaterial = sharedMaterial;
        }

        return visual;
    }

    private static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }

    private static void SetObjectReference(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
        }
    }

    private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.floatValue = value;
        }
    }

    private static void SetInt(SerializedObject serializedObject, string propertyName, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.intValue = value;
        }
    }

    private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }
}
#endif
