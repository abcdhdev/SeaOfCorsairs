#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class WorldMapEditorWindow : EditorWindow
{
    private const string PrefPrefix = "SeaWars.WorldMapEditor.";
    private const string SelectedMapIndexPref = PrefPrefix + "SelectedMapIndex";
    private const string IncludeMainScenePref = PrefPrefix + "IncludeMainScene";
    private const string IncludeNeighborsPref = PrefPrefix + "IncludeNeighbors";
    private const string ReplaceSceneSetupPref = PrefPrefix + "ReplaceSceneSetup";
    private const string ShowScenePathsPref = PrefPrefix + "ShowScenePaths";

    private WorldMapCatalog catalog;
    private int selectedMapIndex;
    private bool includeMainScene = true;
    private bool includeNeighbors;
    private bool replaceCurrentSceneSetup = true;
    private bool showScenePaths;
    private Vector2 mapScrollPosition;

    [MenuItem("Tools/World Map/Map Editor")]
    public static void OpenWindow()
    {
        WorldMapEditorWindow window = GetWindow<WorldMapEditorWindow>("World Map");
        window.minSize = new Vector2(520f, 420f);
        window.Show();
    }

    private void OnEnable()
    {
        catalog = WorldMapEditorSceneUtility.LoadDefaultCatalog();
        selectedMapIndex = EditorPrefs.GetInt(SelectedMapIndexPref, 0);
        includeMainScene = EditorPrefs.GetBool(IncludeMainScenePref, true);
        includeNeighbors = EditorPrefs.GetBool(IncludeNeighborsPref, false);
        replaceCurrentSceneSetup = EditorPrefs.GetBool(ReplaceSceneSetupPref, true);
        showScenePaths = EditorPrefs.GetBool(ShowScenePathsPref, false);
    }

    private void OnDisable()
    {
        SavePrefs();
    }

    private void OnGUI()
    {
        catalog = (WorldMapCatalog)EditorGUILayout.ObjectField("Catalog", catalog, typeof(WorldMapCatalog), false);
        if (catalog == null)
        {
            EditorGUILayout.HelpBox($"Assign a WorldMapCatalog or create one at {WorldMapEditorSceneUtility.DefaultCatalogPath}.", MessageType.Info);
            if (GUILayout.Button("Load Default Catalog"))
            {
                catalog = WorldMapEditorSceneUtility.LoadDefaultCatalog();
            }

            return;
        }

        string[] mapOptions = WorldMapEditorSceneUtility.BuildMapOptions(catalog);
        if (mapOptions.Length == 0)
        {
            EditorGUILayout.HelpBox("The selected catalog has no map entries.", MessageType.Warning);
            return;
        }

        selectedMapIndex = Mathf.Clamp(selectedMapIndex, 0, mapOptions.Length - 1);
        int previousSelectedMapIndex = selectedMapIndex;
        selectedMapIndex = EditorGUILayout.Popup("Selected Map", selectedMapIndex, mapOptions);
        if (selectedMapIndex != previousSelectedMapIndex)
        {
            SavePrefs();
        }

        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        includeMainScene = EditorGUILayout.Toggle("Open MainScene", includeMainScene);
        includeNeighbors = EditorGUILayout.Toggle("Open Neighbors", includeNeighbors);
        replaceCurrentSceneSetup = EditorGUILayout.Toggle("Replace Scene Setup", replaceCurrentSceneSetup);
        showScenePaths = EditorGUILayout.Toggle("Show Scene Paths", showScenePaths);
        if (EditorGUI.EndChangeCheck())
        {
            SavePrefs();
        }

        EditorGUILayout.Space();
        WorldMapDefinition selectedDefinition = WorldMapEditorSceneUtility.GetDefinitionAt(catalog, selectedMapIndex);
        using (new EditorGUI.DisabledScope(selectedDefinition == null))
        {
            if (GUILayout.Button("Open Selected Map For Editing"))
            {
                WorldMapEditorSceneUtility.OpenMapForEditing(
                    catalog,
                    selectedDefinition,
                    includeMainScene,
                    includeNeighbors,
                    replaceCurrentSceneSetup);
            }

            if (GUILayout.Button("Open All Map Scenes For Editing"))
            {
                WorldMapEditorSceneUtility.OpenAllMapsForEditing(
                    catalog,
                    selectedDefinition,
                    includeMainScene,
                    replaceCurrentSceneSetup);
            }

            if (GUILayout.Button("Select Loaded Map Root"))
            {
                WorldMapEditorSceneUtility.SelectLoadedMapRoot(selectedDefinition.MapId);
            }
        }

        if (GUILayout.Button("Populate Loaded Map Scenes From MainScene Template"))
        {
            int updatedSceneCount = WorldMapStarterContentEditorUtility.PopulateLoadedMapScenes();
            Debug.Log($"WorldMapEditor: Applied MainScene template content to {updatedSceneCount} loaded map scene(s).", catalog);
        }

        EditorGUILayout.Space();
        DrawPersistentMapBoard(catalog);
    }

    private void DrawPersistentMapBoard(WorldMapCatalog worldMapCatalog)
    {
        EditorGUILayout.LabelField("All Maps", EditorStyles.boldLabel);

        int columnCount = Mathf.Max(1, WorldMapCatalog.DefaultColumnCount);
        float cardWidth = Mathf.Max(112f, (position.width - 42f) / columnCount);
        mapScrollPosition = EditorGUILayout.BeginScrollView(mapScrollPosition);

        for (int index = 0; index < worldMapCatalog.Maps.Count; index++)
        {
            if (index % columnCount == 0)
            {
                EditorGUILayout.BeginHorizontal();
            }

            DrawMapCard(worldMapCatalog, index, cardWidth);

            if (index % columnCount == columnCount - 1 || index == worldMapCatalog.Maps.Count - 1)
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawMapCard(WorldMapCatalog worldMapCatalog, int mapIndex, float width)
    {
        WorldMapDefinition definition = WorldMapEditorSceneUtility.GetDefinitionAt(worldMapCatalog, mapIndex);
        bool isSelected = mapIndex == selectedMapIndex;
        bool isLoaded = WorldMapEditorSceneUtility.IsMapSceneLoaded(definition);
        bool isActive = WorldMapEditorSceneUtility.IsMapSceneActive(definition);
        bool hasScene = definition?.Scene != null && definition.Scene.HasScenePath;

        Color previousColor = GUI.backgroundColor;
        if (isActive)
        {
            GUI.backgroundColor = new Color(0.55f, 0.8f, 1f);
        }
        else if (isLoaded)
        {
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
        }
        else if (isSelected)
        {
            GUI.backgroundColor = new Color(1f, 0.92f, 0.6f);
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(width));
        GUI.backgroundColor = previousColor;

        string mapId = definition != null ? definition.MapId : "missing";
        string status = isActive ? "Active" : isLoaded ? "Loaded" : hasScene ? "Closed" : "No Scene";
        EditorGUILayout.LabelField(mapId, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(status, EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(definition == null))
        {
            if (GUILayout.Button("Select", GUILayout.Height(20f)))
            {
                selectedMapIndex = mapIndex;
                SavePrefs();
                if (isLoaded)
                {
                    WorldMapEditorSceneUtility.SelectLoadedMapRoot(definition.MapId);
                }
            }

            using (new EditorGUI.DisabledScope(!hasScene))
            {
                if (GUILayout.Button("Open", GUILayout.Height(20f)))
                {
                    selectedMapIndex = mapIndex;
                    SavePrefs();
                    WorldMapEditorSceneUtility.OpenMapForEditing(
                        worldMapCatalog,
                        definition,
                        includeMainScene,
                        includeNeighbors,
                        replaceCurrentSceneSetup);
                }

                if (isLoaded && GUILayout.Button("Root", GUILayout.Height(20f)))
                {
                    selectedMapIndex = mapIndex;
                    SavePrefs();
                    WorldMapEditorSceneUtility.SelectLoadedMapRoot(definition.MapId);
                }
            }
        }

        if (showScenePaths && hasScene)
        {
            EditorGUILayout.LabelField(definition.Scene.ScenePath, EditorStyles.wordWrappedMiniLabel);
        }

        EditorGUILayout.EndVertical();
    }

    private void SavePrefs()
    {
        EditorPrefs.SetInt(SelectedMapIndexPref, selectedMapIndex);
        EditorPrefs.SetBool(IncludeMainScenePref, includeMainScene);
        EditorPrefs.SetBool(IncludeNeighborsPref, includeNeighbors);
        EditorPrefs.SetBool(ReplaceSceneSetupPref, replaceCurrentSceneSetup);
        EditorPrefs.SetBool(ShowScenePathsPref, showScenePaths);
    }
}
#endif
