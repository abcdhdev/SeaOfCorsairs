using Pathfinding;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class AstarNavigationMigrationEditor
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
    private const string NpcPrefabPath = "Assets/Prefabs/NPC.prefab";
    private const string SpriteVisualName = "SpriteVisual";
    private const float SpriteHeightOffset = 0.15f;
    private static readonly Vector3 SpriteVisualScale = new Vector3(4f, 4f, 4f);

    private readonly struct DirectionalSpriteSet
    {
        public DirectionalSpriteSet(Sprite downLeft, Sprite upRight, Sprite upLeft, Sprite downRight)
        {
            DownLeft = downLeft;
            UpRight = upRight;
            UpLeft = upLeft;
            DownRight = downRight;
        }

        public Sprite DownLeft { get; }
        public Sprite UpRight { get; }
        public Sprite UpLeft { get; }
        public Sprite DownRight { get; }
    }

    [MenuItem("Sea Wars/Navigation/Migrate Prefabs To A* Grid")]
    public static void MigratePrefabsToAstarGrid()
    {
        DirectionalSpriteSet playerSprites = LoadSpriteSet(
            "Assets/Sprites/ShipDesigns 1/01/ship_01_sprite.png",
            "Assets/Sprites/ShipDesigns 1/01/ship_02_sprite.png",
            "Assets/Sprites/ShipDesigns 1/01/ship_03_sprite.png",
            "Assets/Sprites/ShipDesigns 1/01/ship_04_sprite.png");
        DirectionalSpriteSet npcSprites = LoadSpriteSet(
            "Assets/Sprites/ShipDesigns 1/01/ship_05_sprite.png",
            "Assets/Sprites/ShipDesigns 1/01/ship_06_sprite.png",
            "Assets/Sprites/ShipDesigns 1/01/ship_07_sprite.png",
            "Assets/Sprites/ShipDesigns 1/01/ship_08_sprite.png");

        bool playerChanged = MigratePrefab(PlayerPrefabPath, playerSprites, disableDefinitionVisualPrefab: false);
        bool npcChanged = MigratePrefab(NpcPrefabPath, npcSprites, disableDefinitionVisualPrefab: true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"A* navigation prefab migration complete. Player changed: {playerChanged}. NPC changed: {npcChanged}.");
    }

    private static bool MigratePrefab(string prefabPath, DirectionalSpriteSet spriteSet, bool disableDefinitionVisualPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        bool changed = false;

        try
        {
            changed |= RemoveComponent<NavMeshAgent>(root);

            Seeker seeker = GetOrAddComponent<Seeker>(root, ref changed);
            ConfigureSeeker(seeker, ref changed);

            AILerp aiLerp = GetOrAddComponent<AILerp>(root, ref changed);
            ConfigureAiLerp(aiLerp, ref changed);

            GameObject spriteVisual = EnsureSpriteVisual(root, spriteSet, ref changed);
            changed |= DisableLegacyShipRenderers(root, spriteVisual);

            if (disableDefinitionVisualPrefab && root.TryGetComponent(out NPC npc))
            {
                changed |= SetSerializedBool(npc, "useDefinitionVisualPrefab", false);
                changed |= SetSerializedObjectReference(npc, "visualRoot", spriteVisual.transform);
            }

            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return changed;
    }

    private static void ConfigureSeeker(Seeker seeker, ref bool changed)
    {
        if (!seeker.drawGizmos)
        {
            seeker.drawGizmos = true;
            changed = true;
        }

        if (seeker.detailedGizmos)
        {
            seeker.detailedGizmos = false;
            changed = true;
        }

        if (seeker.startEndModifier.addPoints)
        {
            seeker.startEndModifier.addPoints = false;
            changed = true;
        }

        if (seeker.startEndModifier.exactStartPoint != StartEndModifier.Exactness.NodeConnection)
        {
            seeker.startEndModifier.exactStartPoint = StartEndModifier.Exactness.NodeConnection;
            changed = true;
        }

        if (seeker.startEndModifier.exactEndPoint != StartEndModifier.Exactness.SnapToNode)
        {
            seeker.startEndModifier.exactEndPoint = StartEndModifier.Exactness.SnapToNode;
            changed = true;
        }

        if (seeker.startEndModifier.useRaycasting)
        {
            seeker.startEndModifier.useRaycasting = false;
            changed = true;
        }

        if (seeker.startEndModifier.useGraphRaycasting)
        {
            seeker.startEndModifier.useGraphRaycasting = false;
            changed = true;
        }

        if (seeker.graphMask != GraphMask.everything)
        {
            seeker.graphMask = GraphMask.everything;
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(seeker);
        }
    }

    private static void ConfigureAiLerp(AILerp aiLerp, ref bool changed)
    {
        if (!aiLerp.enabled)
        {
            aiLerp.enabled = true;
            changed = true;
        }

        if (!aiLerp.simulateMovement)
        {
            aiLerp.simulateMovement = true;
            changed = true;
        }

        if (aiLerp.enableRotation)
        {
            aiLerp.enableRotation = false;
            changed = true;
        }

        if (aiLerp.interpolatePathSwitches)
        {
            aiLerp.interpolatePathSwitches = false;
            changed = true;
        }

        if (aiLerp.orientation != OrientationMode.ZAxisForward)
        {
            aiLerp.orientation = OrientationMode.ZAxisForward;
            changed = true;
        }

        if (aiLerp.autoRepath.mode != AutoRepathPolicy.Mode.EveryNSeconds)
        {
            aiLerp.autoRepath.mode = AutoRepathPolicy.Mode.EveryNSeconds;
            changed = true;
        }

        if (!Mathf.Approximately(aiLerp.autoRepath.period, 0.5f))
        {
            aiLerp.autoRepath.period = 0.5f;
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(aiLerp);
        }
    }

    private static GameObject EnsureSpriteVisual(GameObject root, DirectionalSpriteSet spriteSet, ref bool changed)
    {
        Transform spriteVisualTransform = root.transform.Find(SpriteVisualName);
        if (spriteVisualTransform == null)
        {
            var spriteVisual = new GameObject(SpriteVisualName);
            spriteVisualTransform = spriteVisual.transform;
            spriteVisualTransform.SetParent(root.transform, false);
            changed = true;
        }

        if (spriteVisualTransform.localPosition != new Vector3(0f, SpriteHeightOffset, 0f))
        {
            spriteVisualTransform.localPosition = new Vector3(0f, SpriteHeightOffset, 0f);
            changed = true;
        }

        if (spriteVisualTransform.localRotation != Quaternion.identity)
        {
            spriteVisualTransform.localRotation = Quaternion.identity;
            changed = true;
        }

        if (spriteVisualTransform.localScale != SpriteVisualScale)
        {
            spriteVisualTransform.localScale = SpriteVisualScale;
            changed = true;
        }

        GameObject spriteVisualObject = spriteVisualTransform.gameObject;
        SpriteRenderer spriteRenderer = GetOrAddComponent<SpriteRenderer>(spriteVisualObject, ref changed);
        if (spriteRenderer.sprite != spriteSet.DownLeft)
        {
            spriteRenderer.sprite = spriteSet.DownLeft;
            changed = true;
        }

        if (spriteRenderer.shadowCastingMode != ShadowCastingMode.Off)
        {
            spriteRenderer.shadowCastingMode = ShadowCastingMode.Off;
            changed = true;
        }

        if (spriteRenderer.receiveShadows)
        {
            spriteRenderer.receiveShadows = false;
            changed = true;
        }

        if (spriteRenderer.sortingOrder != 0)
        {
            spriteRenderer.sortingOrder = 0;
            changed = true;
        }

        PlayerDirectionSpriteController spriteController =
            GetOrAddComponent<PlayerDirectionSpriteController>(spriteVisualObject, ref changed);
        changed |= SetDirectionalSpriteControllerSprites(spriteController, spriteSet);

        return spriteVisualObject;
    }

    private static bool DisableLegacyShipRenderers(GameObject root, GameObject spriteVisual)
    {
        bool changed = false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null ||
                renderer.gameObject == spriteVisual ||
                renderer is SpriteRenderer ||
                renderer is ParticleSystemRenderer ||
                renderer is LineRenderer ||
                renderer is TrailRenderer)
            {
                continue;
            }

            if (!renderer.enabled)
            {
                continue;
            }

            renderer.enabled = false;
            EditorUtility.SetDirty(renderer);
            changed = true;
        }

        return changed;
    }

    private static bool SetDirectionalSpriteControllerSprites(
        PlayerDirectionSpriteController controller,
        DirectionalSpriteSet spriteSet)
    {
        var serializedObject = new SerializedObject(controller);
        bool changed = false;

        changed |= SetProperty(serializedObject, "downLeftSprite", spriteSet.DownLeft);
        changed |= SetProperty(serializedObject, "upRightSprite", spriteSet.UpRight);
        changed |= SetProperty(serializedObject, "upLeftSprite", spriteSet.UpLeft);
        changed |= SetProperty(serializedObject, "downRightSprite", spriteSet.DownRight);
        changed |= SetProperty(serializedObject, "useXZPlane", true);
        changed |= SetProperty(serializedObject, "lockWorldRotation", true);
        changed |= SetProperty(serializedObject, "worldEulerRotation", new Vector3(90f, 0f, 0f));

        if (changed)
        {
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        return changed;
    }

    private static DirectionalSpriteSet LoadSpriteSet(
        string downLeftPath,
        string upRightPath,
        string upLeftPath,
        string downRightPath)
    {
        Sprite downLeft = AssetDatabase.LoadAssetAtPath<Sprite>(downLeftPath);
        Sprite upRight = AssetDatabase.LoadAssetAtPath<Sprite>(upRightPath);
        Sprite upLeft = AssetDatabase.LoadAssetAtPath<Sprite>(upLeftPath);
        Sprite downRight = AssetDatabase.LoadAssetAtPath<Sprite>(downRightPath);

        if (downLeft == null || upRight == null || upLeft == null || downRight == null)
        {
            throw new System.InvalidOperationException(
                $"Failed to load one or more ship sprites from '{downLeftPath}', '{upRightPath}', '{upLeftPath}', '{downRightPath}'.");
        }

        return new DirectionalSpriteSet(downLeft, upRight, upLeft, downRight);
    }

    private static T GetOrAddComponent<T>(GameObject gameObject, ref bool changed) where T : Component
    {
        if (!gameObject.TryGetComponent(out T component))
        {
            component = gameObject.AddComponent<T>();
            changed = true;
        }

        return component;
    }

    private static bool RemoveComponent<T>(GameObject gameObject) where T : Component
    {
        if (!gameObject.TryGetComponent(out T component))
        {
            return false;
        }

        Object.DestroyImmediate(component, true);
        return true;
    }

    private static bool SetSerializedBool(Object target, string propertyName, bool value)
    {
        var serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.boolValue == value)
        {
            return false;
        }

        property.boolValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return true;
    }

    private static bool SetSerializedObjectReference(Object target, string propertyName, Object value)
    {
        var serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == value)
        {
            return false;
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return true;
    }

    private static bool SetProperty(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.boolValue == value)
        {
            return false;
        }

        property.boolValue = value;
        return true;
    }

    private static bool SetProperty(SerializedObject serializedObject, string propertyName, Vector3 value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.vector3Value == value)
        {
            return false;
        }

        property.vector3Value = value;
        return true;
    }

    private static bool SetProperty(SerializedObject serializedObject, string propertyName, Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == value)
        {
            return false;
        }

        property.objectReferenceValue = value;
        return true;
    }
}
