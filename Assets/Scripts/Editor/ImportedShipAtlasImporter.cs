using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

public static class ImportedShipAtlasImporter
{
    private const string SourceRoot = @"C:\Users\Abcd\Downloads\Ships_Export\Assets";
    private const string TargetRoot = "Assets/Sprites/ImportedShips";
    private const string NpcVisualRoot = "Assets/NPC/ImportedShips";
    private const string NpcDefinitionRoot = "Assets/GameData/ImportedShips";
    private const string NpcPrefabPath = "Assets/Prefabs/NPC.prefab";
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string NpcVisualRootName = "SpriteVisual";
    private const float SpriteHeightOffset = 0.15f;

    private static readonly Vector3 SpriteVisualScale = new(4f, 4f, 4f);
    private enum VisualDirectionMode
    {
        FourWayDiagonal = 0,
        EightWay = 1
    }

    private readonly struct VisualSpriteSet
    {
        public VisualSpriteSet(
            VisualDirectionMode directionMode,
            Sprite up,
            Sprite upRight,
            Sprite right,
            Sprite upLeft,
            Sprite down,
            Sprite downLeft,
            Sprite downRight,
            Sprite left)
            : this(
                directionMode,
                up,
                upRight,
                right,
                upLeft,
                down,
                downLeft,
                downRight,
                left,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null)
        {
        }

        public VisualSpriteSet(
            VisualDirectionMode directionMode,
            Sprite up,
            Sprite upRight,
            Sprite right,
            Sprite upLeft,
            Sprite down,
            Sprite downLeft,
            Sprite downRight,
            Sprite left,
            Sprite burningUp,
            Sprite burningUpRight,
            Sprite burningRight,
            Sprite burningUpLeft,
            Sprite burningDown,
            Sprite burningDownLeft,
            Sprite burningDownRight,
            Sprite burningLeft)
        {
            DirectionMode = directionMode;
            Up = up;
            UpRight = upRight;
            Right = right;
            UpLeft = upLeft;
            Down = down;
            DownLeft = downLeft;
            DownRight = downRight;
            Left = left;
            BurningUp = burningUp;
            BurningUpRight = burningUpRight;
            BurningRight = burningRight;
            BurningUpLeft = burningUpLeft;
            BurningDown = burningDown;
            BurningDownLeft = burningDownLeft;
            BurningDownRight = burningDownRight;
            BurningLeft = burningLeft;
        }

        public VisualDirectionMode DirectionMode { get; }
        public Sprite Up { get; }
        public Sprite UpRight { get; }
        public Sprite Right { get; }
        public Sprite UpLeft { get; }
        public Sprite Down { get; }
        public Sprite DownLeft { get; }
        public Sprite DownRight { get; }
        public Sprite Left { get; }
        public Sprite BurningUp { get; }
        public Sprite BurningUpRight { get; }
        public Sprite BurningRight { get; }
        public Sprite BurningUpLeft { get; }
        public Sprite BurningDown { get; }
        public Sprite BurningDownLeft { get; }
        public Sprite BurningDownRight { get; }
        public Sprite BurningLeft { get; }
    }

    [MenuItem("Sea Wars/Sprites/Import Exported Ship Atlases")]
    public static void ImportExportedShipAtlases()
    {
        if (!Directory.Exists(SourceRoot))
        {
            Debug.LogError($"Ship import source folder does not exist: {SourceRoot}");
            return;
        }

        EnsureFolderPath(TargetRoot);
        EnsureFolderPath(NpcVisualRoot);
        EnsureFolderPath(NpcDefinitionRoot);

        string atlasJsonDirectory = Path.Combine(SourceRoot, "SpriteAtlas");
        string spriteJsonDirectory = Path.Combine(SourceRoot, "Sprite");
        string textureDirectory = Path.Combine(SourceRoot, "Texture2D");

        string[] atlasFiles = Directory.GetFiles(atlasJsonDirectory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(atlasFiles, StringComparer.OrdinalIgnoreCase);

        var importedNpcDefinitions = new List<NpcDefinition>();

        for (int atlasIndex = 0; atlasIndex < atlasFiles.Length; atlasIndex++)
        {
            string atlasFile = atlasFiles[atlasIndex];
            AtlasDefinition atlas = ParseAtlasDefinition(atlasFile);
            string sourceTexture = ResolveTextureSourcePath(textureDirectory, atlas.Name);
            if (string.IsNullOrEmpty(sourceTexture))
            {
                Debug.LogWarning($"Skipping atlas '{atlas.Name}' because its texture could not be resolved.");
                continue;
            }

            string atlasFolder = $"{TargetRoot}/{atlas.Name}";
            string metadataFolder = $"{atlasFolder}/Metadata";
            string spriteMetadataFolder = $"{metadataFolder}/Sprites";
            EnsureFolderPath(atlasFolder);
            EnsureFolderPath(metadataFolder);
            EnsureFolderPath(spriteMetadataFolder);

            string textureAssetPath = $"{atlasFolder}/{atlas.Name}.png";
            string atlasMetadataAssetPath = $"{metadataFolder}/{atlas.Name}.json";

            CopyFileIntoProject(sourceTexture, textureAssetPath);
            CopyFileIntoProject(atlasFile, atlasMetadataAssetPath);

            var spriteDefinitions = new List<SpriteDefinition>();
            for (int spriteIndex = 0; spriteIndex < atlas.SpriteNames.Count; spriteIndex++)
            {
                string spriteName = atlas.SpriteNames[spriteIndex];
                string spriteJsonPath = Path.Combine(spriteJsonDirectory, $"{spriteName}.json");

                SpriteDefinition spriteDefinition = new SpriteDefinition(
                    spriteName,
                    atlas.SpriteRects[spriteIndex],
                    new Vector2(0.5f, 0.5f),
                    Vector4.zero,
                    100f);

                if (File.Exists(spriteJsonPath))
                {
                    string spriteMetadataAssetPath = $"{spriteMetadataFolder}/{spriteName}.json";
                    CopyFileIntoProject(spriteJsonPath, spriteMetadataAssetPath);
                    ParsedSpriteDefinition parsedSprite = ParseSpriteDefinition(spriteJsonPath);
                    spriteDefinition = new SpriteDefinition(
                        spriteDefinition.Name,
                        spriteDefinition.Rect,
                        parsedSprite.Definition.Pivot,
                        parsedSprite.Definition.Border,
                        parsedSprite.Definition.PixelsPerUnit);
                }

                spriteDefinitions.Add(spriteDefinition);
            }

            ConfigureTextureImporter(textureAssetPath, spriteDefinitions);
            CreateOrUpdateSpriteAtlasAsset(atlasFolder, atlas.Name, textureAssetPath);

            Dictionary<string, Sprite> spritesByName = LoadSpritesByName(textureAssetPath);
            if (!TryGetNpcVisualSpriteSet(spritesByName, out VisualSpriteSet spriteSet))
            {
                continue;
            }

            string visualFolder = $"{NpcVisualRoot}/{atlas.Name}";
            EnsureFolderPath(visualFolder);
            string visualPrefabPath = $"{visualFolder}/{atlas.Name}_Visual.prefab";
            GameObject visualPrefab = CreateOrUpdateVisualPrefab(visualPrefabPath, atlas.Name, spriteSet);

            string definitionAssetPath = $"{NpcDefinitionRoot}/{atlas.Name}.asset";
            NpcDefinition definition = CreateOrUpdateNpcDefinition(definitionAssetPath, atlas.Name, visualPrefab);
            importedNpcDefinitions.Add(definition);
        }

        ConfigureNpcPrefabForImportedVisuals();
        UpdateSpawnerDefinitions(importedNpcDefinitions);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        Debug.Log($"Imported {atlasFiles.Length} ship atlases. Added {importedNpcDefinitions.Count} sprite NPC definitions to MainScene.");
    }

    private static AtlasDefinition ParseAtlasDefinition(string atlasFile)
    {
        AtlasJson atlasJson = JsonUtility.FromJson<AtlasJson>(File.ReadAllText(atlasFile));
        string name = string.IsNullOrWhiteSpace(atlasJson.m_Name)
            ? Path.GetFileNameWithoutExtension(atlasFile)
            : atlasJson.m_Name;
        List<string> spriteNames = atlasJson.m_PackedSpriteNamesToIndex != null
            ? atlasJson.m_PackedSpriteNamesToIndex.Where(spriteName => !string.IsNullOrWhiteSpace(spriteName)).ToList()
            : new List<string>();
        List<Rect> spriteRects = new();

        if (atlasJson.m_RenderDataMap != null)
        {
            for (int index = 0; index < atlasJson.m_RenderDataMap.Length; index++)
            {
                spriteRects.Add(ReadRect(atlasJson.m_RenderDataMap[index].Value.m_TextureRect));
            }
        }

        return new AtlasDefinition(name, spriteNames, spriteRects);
    }

    private static ParsedSpriteDefinition ParseSpriteDefinition(string spriteFile)
    {
        SpriteJson spriteJson = JsonUtility.FromJson<SpriteJson>(File.ReadAllText(spriteFile));
        string name = string.IsNullOrWhiteSpace(spriteJson.m_Name)
            ? Path.GetFileNameWithoutExtension(spriteFile)
            : spriteJson.m_Name;
        Rect rect = ReadRect(spriteJson.m_RD.m_TextureRect);
        Vector2 pivot = ReadVector2(spriteJson.m_Pivot);
        Vector4 border = ReadVector4(spriteJson.m_Border);
        float pixelsPerUnit = Mathf.Approximately(spriteJson.m_PixelsToUnits, 0f) ? 100f : spriteJson.m_PixelsToUnits;

        return new ParsedSpriteDefinition(new SpriteDefinition(name, rect, pivot, border, pixelsPerUnit));
    }

    private static Rect ReadRect(JsonRect element)
    {
        return new Rect(element.m_X, element.m_Y, element.m_Width, element.m_Height);
    }

    private static Vector2 ReadVector2(JsonVector2 element)
    {
        return new Vector2(element.m_X, element.m_Y);
    }

    private static Vector4 ReadVector4(JsonVector4 element)
    {
        return new Vector4(element.m_X, element.m_Y, element.m_Z, element.m_W);
    }

    private static string ResolveTextureSourcePath(string textureDirectory, string atlasName)
    {
        string[] candidates = Directory.GetFiles(textureDirectory, "*.png", SearchOption.TopDirectoryOnly);
        return candidates.FirstOrDefault(path =>
            path.IndexOf($"-{atlasName}-", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static void CopyFileIntoProject(string sourcePath, string targetAssetPath)
    {
        string targetAbsolutePath = Path.Combine(Directory.GetCurrentDirectory(), targetAssetPath.Replace('/', Path.DirectorySeparatorChar));
        string targetDirectory = Path.GetDirectoryName(targetAbsolutePath);
        if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.Copy(sourcePath, targetAbsolutePath, true);
        AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void ConfigureTextureImporter(string textureAssetPath, IReadOnlyList<SpriteDefinition> spriteDefinitions)
    {
        if (!(AssetImporter.GetAtPath(textureAssetPath) is TextureImporter importer))
        {
            Debug.LogError($"Unable to configure sprite importer for '{textureAssetPath}'.");
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = spriteDefinitions.Count > 0 ? spriteDefinitions[0].PixelsPerUnit : 100f;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.compressionQuality = 0;
        importer.filterMode = FilterMode.Bilinear;
        importer.SetPlatformTextureSettings(CreateUncompressedPlatformSettings("DefaultTexturePlatform"));
        importer.SetPlatformTextureSettings(CreateUncompressedPlatformSettings("Standalone"));
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();

        if (!(factory.GetSpriteEditorDataProviderFromObject(importer) is ISpriteEditorDataProvider dataProvider))
        {
            Debug.LogError($"Unable to create sprite data provider for '{textureAssetPath}'.");
            return;
        }

        dataProvider.InitSpriteEditorDataProvider();

        var spriteRects = new List<SpriteRect>(spriteDefinitions.Count);
        var nameFileIdPairs = new List<SpriteNameFileIdPair>(spriteDefinitions.Count);

        foreach (SpriteDefinition spriteDefinition in spriteDefinitions)
        {
            var spriteRect = new SpriteRect
            {
                name = spriteDefinition.Name,
                rect = spriteDefinition.Rect,
                pivot = spriteDefinition.Pivot,
                border = spriteDefinition.Border,
                alignment = SpriteAlignment.Custom,
                spriteID = GUID.Generate()
            };

            spriteRects.Add(spriteRect);
            nameFileIdPairs.Add(new SpriteNameFileIdPair(spriteRect.name, spriteRect.spriteID));
        }

        dataProvider.SetSpriteRects(spriteRects.ToArray());

        if (dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>() is ISpriteNameFileIdDataProvider nameFileIdProvider)
        {
            nameFileIdProvider.SetNameFileIdPairs(nameFileIdPairs);
        }

        dataProvider.Apply();
        importer.SaveAndReimport();
    }

    private static TextureImporterPlatformSettings CreateUncompressedPlatformSettings(string buildTarget)
    {
        return new TextureImporterPlatformSettings
        {
            name = buildTarget,
            overridden = false,
            maxTextureSize = 2048,
            resizeAlgorithm = TextureResizeAlgorithm.Mitchell,
            format = TextureImporterFormat.Automatic,
            textureCompression = TextureImporterCompression.Uncompressed,
            compressionQuality = 0,
            crunchedCompression = false
        };
    }

    private static void CreateOrUpdateSpriteAtlasAsset(string atlasFolder, string atlasName, string textureAssetPath)
    {
        string legacyAtlasAssetPath = $"{atlasFolder}/{atlasName}.spriteatlasv2";
        if (AssetDatabase.LoadAssetAtPath<Object>(legacyAtlasAssetPath) != null)
        {
            AssetDatabase.DeleteAsset(legacyAtlasAssetPath);
        }

        string atlasAssetPath = $"{atlasFolder}/{atlasName}.spriteatlas";
        if (AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasAssetPath) == null)
        {
            var spriteAtlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(spriteAtlas, atlasAssetPath);
        }

        SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasAssetPath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureAssetPath);
        if (atlas == null || texture == null)
        {
            Debug.LogWarning($"Skipping Unity SpriteAtlas creation for '{atlasName}'.");
            return;
        }

        var packingSettings = atlas.GetPackingSettings();
        packingSettings.enableRotation = false;
        packingSettings.enableTightPacking = true;
        packingSettings.padding = 2;
        atlas.SetPackingSettings(packingSettings);

        var textureSettings = atlas.GetTextureSettings();
        textureSettings.readable = false;
        textureSettings.generateMipMaps = false;
        textureSettings.sRGB = true;
        textureSettings.filterMode = FilterMode.Bilinear;
        atlas.SetTextureSettings(textureSettings);

        SpriteAtlasExtensions.Add(atlas, new Object[] { texture });
        EditorUtility.SetDirty(atlas);
    }

    private static Dictionary<string, Sprite> LoadSpritesByName(string textureAssetPath)
    {
        return AssetDatabase.LoadAllAssetsAtPath(textureAssetPath)
            .OfType<Sprite>()
            .ToDictionary(sprite => sprite.name, sprite => sprite, StringComparer.Ordinal);
    }

    private static bool TryGetNpcVisualSpriteSet(
        IReadOnlyDictionary<string, Sprite> spritesByName,
        out VisualSpriteSet spriteSet)
    {
        spriteSet = default;
        return TryCreateEightWaySpriteSet(spritesByName, out spriteSet) ||
               TryCreateFourWayDiagonalSpriteSet(spritesByName, out spriteSet);
    }

    private static GameObject CreateOrUpdateVisualPrefab(
        string prefabPath,
        string atlasName,
        VisualSpriteSet spriteSet)
    {
        var root = new GameObject($"{atlasName}_Visual");
        try
        {
            var spriteRenderer = root.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = spriteSet.DirectionMode switch
            {
                VisualDirectionMode.EightWay when spriteSet.Down != null => spriteSet.Down,
                _ => spriteSet.DownLeft
            };
            spriteRenderer.shadowCastingMode = ShadowCastingMode.Off;
            spriteRenderer.receiveShadows = false;
            spriteRenderer.sortingOrder = 0;

            var controller = root.AddComponent<PlayerDirectionSpriteController>();
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("directionMode").enumValueIndex = (int)spriteSet.DirectionMode;
            serializedController.FindProperty("upSprite").objectReferenceValue = spriteSet.Up;
            serializedController.FindProperty("upRightSprite").objectReferenceValue = spriteSet.UpRight;
            serializedController.FindProperty("rightSprite").objectReferenceValue = spriteSet.Right;
            serializedController.FindProperty("upLeftSprite").objectReferenceValue = spriteSet.UpLeft;
            serializedController.FindProperty("downSprite").objectReferenceValue = spriteSet.Down;
            serializedController.FindProperty("downRightSprite").objectReferenceValue = spriteSet.DownRight;
            serializedController.FindProperty("leftSprite").objectReferenceValue = spriteSet.Left;
            serializedController.FindProperty("downLeftSprite").objectReferenceValue = spriteSet.DownLeft;
            serializedController.FindProperty("burningUpSprite").objectReferenceValue = spriteSet.BurningUp;
            serializedController.FindProperty("burningUpRightSprite").objectReferenceValue = spriteSet.BurningUpRight;
            serializedController.FindProperty("burningRightSprite").objectReferenceValue = spriteSet.BurningRight;
            serializedController.FindProperty("burningUpLeftSprite").objectReferenceValue = spriteSet.BurningUpLeft;
            serializedController.FindProperty("burningDownSprite").objectReferenceValue = spriteSet.BurningDown;
            serializedController.FindProperty("burningDownRightSprite").objectReferenceValue = spriteSet.BurningDownRight;
            serializedController.FindProperty("burningLeftSprite").objectReferenceValue = spriteSet.BurningLeft;
            serializedController.FindProperty("burningDownLeftSprite").objectReferenceValue = spriteSet.BurningDownLeft;
            serializedController.FindProperty("useBurningSprites").boolValue = false;
            serializedController.FindProperty("useXZPlane").boolValue = true;
            serializedController.FindProperty("lockWorldRotation").boolValue = true;
            serializedController.FindProperty("worldEulerRotation").vector3Value = new Vector3(90f, 0f, 0f);
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return prefab != null ? prefab : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static bool TryCreateFourWayDiagonalSpriteSet(
        IReadOnlyDictionary<string, Sprite> spritesByName,
        out VisualSpriteSet spriteSet)
    {
        spriteSet = default;
        if (!TryGetSprite(spritesByName, "ship_05_sprite", out Sprite downLeft) ||
            !TryGetSprite(spritesByName, "ship_06_sprite", out Sprite upRight) ||
            !TryGetSprite(spritesByName, "ship_07_sprite", out Sprite upLeft) ||
            !TryGetSprite(spritesByName, "ship_08_sprite", out Sprite downRight) ||
            !TryGetSprite(spritesByName, "ship_01_sprite", out Sprite burningDownLeft) ||
            !TryGetSprite(spritesByName, "ship_02_sprite", out Sprite burningUpRight) ||
            !TryGetSprite(spritesByName, "ship_03_sprite", out Sprite burningUpLeft) ||
            !TryGetSprite(spritesByName, "ship_04_sprite", out Sprite burningDownRight))
        {
            return false;
        }

        spriteSet = new VisualSpriteSet(
            VisualDirectionMode.FourWayDiagonal,
            null,
            upRight,
            null,
            upLeft,
            null,
            downLeft,
            downRight,
            null,
            null,
            burningUpRight,
            null,
            burningUpLeft,
            null,
            burningDownLeft,
            burningDownRight,
            null);
        return true;
    }

    private static bool TryCreateEightWaySpriteSet(
        IReadOnlyDictionary<string, Sprite> spritesByName,
        out VisualSpriteSet spriteSet)
    {
        spriteSet = default;
        if (!TryGetSprite(spritesByName, "ship_01_sprite", out Sprite down) ||
            !TryGetSprite(spritesByName, "ship_02_sprite", out Sprite right) ||
            !TryGetSprite(spritesByName, "ship_03_sprite", out Sprite up) ||
            !TryGetSprite(spritesByName, "ship_04_sprite", out Sprite left) ||
            !TryGetSprite(spritesByName, "ship_05_sprite", out Sprite downLeft) ||
            !TryGetSprite(spritesByName, "ship_06_sprite", out Sprite upRight) ||
            !TryGetSprite(spritesByName, "ship_07_sprite", out Sprite upLeft) ||
            !TryGetSprite(spritesByName, "ship_08_sprite", out Sprite downRight) ||
            !TryGetSprite(spritesByName, "ship_09_sprite", out Sprite burningDown) ||
            !TryGetSprite(spritesByName, "ship_10_sprite", out Sprite burningRight) ||
            !TryGetSprite(spritesByName, "ship_11_sprite", out Sprite burningUp) ||
            !TryGetSprite(spritesByName, "ship_12_sprite", out Sprite burningLeft) ||
            !TryGetSprite(spritesByName, "ship_13_sprite", out Sprite burningDownLeft) ||
            !TryGetSprite(spritesByName, "ship_14_sprite", out Sprite burningUpRight) ||
            !TryGetSprite(spritesByName, "ship_15_sprite", out Sprite burningUpLeft) ||
            !TryGetSprite(spritesByName, "ship_16_sprite", out Sprite burningDownRight))
        {
            return false;
        }

        spriteSet = new VisualSpriteSet(
            VisualDirectionMode.EightWay,
            up,
            upRight,
            right,
            upLeft,
            down,
            downLeft,
            downRight,
            left,
            burningUp,
            burningUpRight,
            burningRight,
            burningUpLeft,
            burningDown,
            burningDownLeft,
            burningDownRight,
            burningLeft);
        return true;
    }

    private static bool TryGetSprite(
        IReadOnlyDictionary<string, Sprite> spritesByName,
        string spriteName,
        out Sprite sprite)
    {
        return spritesByName.TryGetValue(spriteName, out sprite) && sprite != null;
    }

    private static NpcDefinition CreateOrUpdateNpcDefinition(string assetPath, string atlasName, GameObject visualPrefab)
    {
        NpcDefinition asset = AssetDatabase.LoadAssetAtPath<NpcDefinition>(assetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<NpcDefinition>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        var serializedObject = new SerializedObject(asset);
        serializedObject.FindProperty("npcName").stringValue = BuildNpcDisplayName(atlasName);
        serializedObject.FindProperty("visualPrefab").objectReferenceValue = visualPrefab;
        serializedObject.FindProperty("health").intValue = 100;
        serializedObject.FindProperty("damage").intValue = 2;
        serializedObject.FindProperty("attackIntervalSeconds").floatValue = 2f;
        serializedObject.FindProperty("respawnDelaySeconds").floatValue = 20f;
        serializedObject.FindProperty("corpseLifetimeSeconds").floatValue = 0f;

        SerializedProperty rewardProperty = serializedObject.FindProperty("reward");
        rewardProperty.FindPropertyRelative("pearls").intValue = 100;
        rewardProperty.FindPropertyRelative("gold").intValue = 50;
        rewardProperty.FindPropertyRelative("experience").intValue = 2;

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static string BuildNpcDisplayName(string atlasName)
    {
        string numericPart = new string(atlasName.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(numericPart) ? atlasName : $"Sprite Ship {numericPart}";
    }

    private static void ConfigureNpcPrefabForImportedVisuals()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(NpcPrefabPath);

        try
        {
            Transform visualRoot = root.transform.Find(NpcVisualRootName);
            if (visualRoot == null)
            {
                var visualRootObject = new GameObject(NpcVisualRootName);
                visualRoot = visualRootObject.transform;
                visualRoot.SetParent(root.transform, false);
            }

            visualRoot.localPosition = new Vector3(0f, SpriteHeightOffset, 0f);
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localScale = SpriteVisualScale;

            PlayerDirectionSpriteController spriteController = visualRoot.GetComponent<PlayerDirectionSpriteController>();
            if (spriteController != null)
            {
                Object.DestroyImmediate(spriteController);
            }

            SpriteRenderer spriteRenderer = visualRoot.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                Object.DestroyImmediate(spriteRenderer);
            }

            if (root.TryGetComponent(out NPC npc))
            {
                var serializedNpc = new SerializedObject(npc);
                serializedNpc.FindProperty("useDefinitionVisualPrefab").boolValue = true;
                serializedNpc.FindProperty("visualRoot").objectReferenceValue = visualRoot;
                serializedNpc.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(npc);
            }

            PrefabUtility.SaveAsPrefabAsset(root, NpcPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void UpdateSpawnerDefinitions(IReadOnlyList<NpcDefinition> importedDefinitions)
    {
        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        NPCSpawner spawner = Object.FindFirstObjectByType<NPCSpawner>(FindObjectsInactive.Include);
        if (spawner == null)
        {
            Debug.LogWarning($"Could not find NPCSpawner in scene '{scene.path}'.");
            return;
        }

        var serializedSpawner = new SerializedObject(spawner);
        SerializedProperty definitionsProperty = serializedSpawner.FindProperty("npcDefinitions");
        var existing = new List<Object>(definitionsProperty.arraySize);

        for (int index = 0; index < definitionsProperty.arraySize; index++)
        {
            existing.Add(definitionsProperty.GetArrayElementAtIndex(index).objectReferenceValue);
        }

        for (int index = 0; index < importedDefinitions.Count; index++)
        {
            NpcDefinition importedDefinition = importedDefinitions[index];
            if (importedDefinition != null && !existing.Contains(importedDefinition))
            {
                existing.Add(importedDefinition);
            }
        }

        definitionsProperty.arraySize = existing.Count;
        for (int index = 0; index < existing.Count; index++)
        {
            definitionsProperty.GetArrayElementAtIndex(index).objectReferenceValue = existing[index];
        }

        serializedSpawner.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spawner);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void EnsureFolderPath(string assetPath)
    {
        string[] parts = assetPath.Split('/');
        string currentPath = parts[0];

        for (int index = 1; index < parts.Length; index++)
        {
            string nextPath = $"{currentPath}/{parts[index]}";
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, parts[index]);
            }

            currentPath = nextPath;
        }
    }

    private readonly struct AtlasDefinition
    {
        public AtlasDefinition(string name, List<string> spriteNames, List<Rect> spriteRects)
        {
            Name = name;
            SpriteNames = spriteNames;
            SpriteRects = spriteRects;
        }

        public string Name { get; }
        public List<string> SpriteNames { get; }
        public List<Rect> SpriteRects { get; }
    }

    private readonly struct SpriteDefinition
    {
        public SpriteDefinition(string name, Rect rect, Vector2 pivot, Vector4 border, float pixelsPerUnit)
        {
            Name = name;
            Rect = rect;
            Pivot = pivot;
            Border = border;
            PixelsPerUnit = pixelsPerUnit;
        }

        public string Name { get; }
        public Rect Rect { get; }
        public Vector2 Pivot { get; }
        public Vector4 Border { get; }
        public float PixelsPerUnit { get; }
    }

    private readonly struct ParsedSpriteDefinition
    {
        public ParsedSpriteDefinition(SpriteDefinition definition)
        {
            Definition = definition;
        }

        public SpriteDefinition Definition { get; }
    }

    [Serializable]
    private sealed class AtlasJson
    {
        public string m_Name;
        public string[] m_PackedSpriteNamesToIndex;
        public RenderDataMapEntry[] m_RenderDataMap;
    }

    [Serializable]
    private sealed class SpriteJson
    {
        public string m_Name;
        public JsonVector2 m_Pivot;
        public JsonVector4 m_Border;
        public float m_PixelsToUnits;
        public JsonRenderData m_RD;
    }

    [Serializable]
    private struct JsonRect
    {
        public float m_X;
        public float m_Y;
        public float m_Width;
        public float m_Height;
    }

    [Serializable]
    private struct JsonVector2
    {
        public float m_X;
        public float m_Y;
    }

    [Serializable]
    private struct JsonVector4
    {
        public float m_X;
        public float m_Y;
        public float m_Z;
        public float m_W;
    }

    [Serializable]
    private struct JsonRenderData
    {
        public JsonRect m_TextureRect;
    }

    [Serializable]
    private sealed class RenderDataMapEntry
    {
        public RenderDataValue Value;
    }

    [Serializable]
    private sealed class RenderDataValue
    {
        public JsonRect m_TextureRect;
    }
}
