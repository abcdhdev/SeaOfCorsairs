using System;
using System.Collections.Generic;
using Gaia;
using Gaia.Internal;
using PWCommon5;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gaia
{
    public class GaiaTerrainFixingManager : EditorWindow, IPWEditor
    {
        private HeightmapTerraceRemover _heightmapTerraceRemover;
        private TerrainTextureAligner _terrainTextureAligner;
        private GaiaTerrainStitcher _gaiaTerrainStitcher;

        private Vector2 _scrollPosition;
        private EditorUtils m_editorUtils;
        private GaiaSettings m_settings;
        private Terrain[] _terrainsToProcess;
        private Terrain _terrainToAdd;

        private bool _autoManagerProcessing = true;
        private bool _processAllTerrainsOnScene = true;
        
        private struct TerrainInfoStruct
        {
            private Terrain _terrain;

            private bool _stitched;
            private bool _textureAligned;
            private bool _heightmapFixed;
        }

        private TerrainInfoStruct[] _terrainsInfo;

        //[MenuItem("Window/Procedural Worlds/Gaia/Terrain Fixing Manager")]
        //public static void ShowWindow()
        //{
        //    GetWindow<GaiaTerrainFixingManager>("Terrain Fixing Manager");
        //}

        public bool PositionChecked
        {
            get => true;
            set => PositionChecked = value;
        }

        private void OnEnable()
        {
            if (m_editorUtils == null)
            {
                // Get editor utils for this
                m_editorUtils = PWApp.GetEditorUtils(this);
            }

            titleContent = m_editorUtils.GetContent("WindowTitle");

            _heightmapTerraceRemover = ScriptableObject.CreateInstance<HeightmapTerraceRemover>();
            _terrainTextureAligner = ScriptableObject.CreateInstance<TerrainTextureAligner>();
            _gaiaTerrainStitcher = ScriptableObject.CreateInstance<GaiaTerrainStitcher>();
        }


        #region OnGUI Region
        private void OnGUI()
        {
            m_editorUtils.Initialize();

            m_editorUtils.Panel("TerrainFixingManager", DrawTerrainFixingManager, true);
        }
        private void DrawTerrainFixingManager(bool helpEnabled)
        {
            _heightmapTerraceRemover.autoManagerProcessing = _autoManagerProcessing;
            _terrainTextureAligner.autoManagerProcessing = _autoManagerProcessing;
            _gaiaTerrainStitcher.autoManagerProcessing = _autoManagerProcessing;

            if (m_settings == null)
            {
                m_settings = GaiaUtils.GetGaiaSettings();
            }

            GUILayout.BeginVertical("box");
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            // Force auto manager processing always on and hide from UI
            _autoManagerProcessing = true;
            _heightmapTerraceRemover.autoManagerProcessing = _autoManagerProcessing;
            _terrainTextureAligner.autoManagerProcessing = _autoManagerProcessing;
            _gaiaTerrainStitcher.autoManagerProcessing = _autoManagerProcessing;
            m_editorUtils.Panel("HowToUse", DrawHowToUse, false);
            EditorGUILayout.Space(2);
            m_editorUtils.Panel("AutomaticFixingProperties", DrawAutomaticFixingProperties, true);

            // Show tool panels; tools expose their own enable checkboxes in manager context
            GUILayout.BeginVertical("box");
            _gaiaTerrainStitcher.OnGUI();
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            _heightmapTerraceRemover.OnGUI();
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            _terrainTextureAligner.OnGUI();
            GUILayout.EndVertical();

            // Start button shown when at least one tool is enabled in its panel
            bool anyToolEnabled = (_gaiaTerrainStitcher.enabledInManager || _heightmapTerraceRemover.enabledInManager || _terrainTextureAligner.enabledInManager);
            if (anyToolEnabled)
            {
                bool hasTerrainsToProcess = _terrainsToProcess != null && _terrainsToProcess.Length > 0 && Array.Exists(_terrainsToProcess, t => t != null);

                EditorGUI.BeginDisabledGroup(!hasTerrainsToProcess);
                GUI.backgroundColor = m_settings.GetActionButtonColor();
                if (m_editorUtils.Button("StartProcessing"))
                {
                    StartProcessingTerrains();
                }
                GUI.backgroundColor = Color.white;
                EditorGUI.EndDisabledGroup();

                if (!hasTerrainsToProcess)
                {
                    EditorGUILayout.HelpBox("No terrains available to process. Please add terrains to the list or switch to 'Process All Terrains in Scene' if terrains are present in the scene.", MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No tool selected yet - please enable at least one of the tools above, then a button will appear here to start the process.", MessageType.Info);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawHowToUse(bool obj)
        {
            m_editorUtils.InlineHelp("HowToUseText", true);
                                
                                    
        }

        private void DrawAutomaticFixingProperties(bool helpEnabled)
        {
            // Top section: Target terrain selection only. Auto-processing is always on and hidden.
            //EditorGUILayout.LabelField("Target Terrains", EditorStyles.boldLabel);

            _processAllTerrainsOnScene = m_editorUtils.Toggle("ProcessAllTerrainsInScene", _processAllTerrainsOnScene, helpEnabled);

            if (_processAllTerrainsOnScene)
            {
                _terrainsToProcess = Terrain.activeTerrains;
                if (_terrainsToProcess == null || _terrainsToProcess.Length == 0)
                {
                    EditorGUILayout.HelpBox("No active terrains found in the scene.", MessageType.Warning);
                }
            }
            else
            {
                // Terrain loading limitation info
                if (GaiaUtils.HasDynamicLoadedTerrains())
                {
                    EditorGUILayout.HelpBox("You are using a terrain loading setup - if you want to use this manual list, you need to load in all terrains that you want to process in order to be able to select them here. Alternatively you can activate 'Process all Terrains' this will process all terrains from your loading setup, even if they are currently not loaded in yet.", MessageType.Info);
                }
                GUILayout.Space(4);
                GUILayout.Label("Terrains to Process", GUILayout.Width(150));

                // Use a list to store selected terrains
                List<Terrain> tempTerrainsList = new List<Terrain>(_terrainsToProcess ?? Array.Empty<Terrain>());

                // Drag & Drop Zone for adding terrains
                Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
                GUI.Box(dropArea, "Drop Terrains Here");
                Event evt = Event.current;
                if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
                        {
                            Terrain t = null;
                            if (dragged is Terrain terrainObj)
                            {
                                t = terrainObj;
                            }
                            else if (dragged is GameObject go)
                            {
                                t = go.GetComponent<Terrain>();
                            }
                            if (t != null && !tempTerrainsList.Contains(t))
                            {
                                tempTerrainsList.Add(t);
                            }
                        }
                        Event.current.Use();
                    }
                    else
                    {
                        Event.current.Use();
                    }
                }

                // Display each terrain with an ObjectField
                for (int i = 0; i < tempTerrainsList.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Terrain {i + 1}", GUILayout.Width(150));
                    Terrain selectedTerrain = (Terrain)EditorGUILayout.ObjectField(tempTerrainsList[i], typeof(Terrain), true);

                    if (selectedTerrain != tempTerrainsList[i])
                    {
                        // Check for duplicates
                        if (selectedTerrain != null && tempTerrainsList.Contains(selectedTerrain))
                        {
                            Debug.LogWarning("Duplicate terrain detected. Field cleared.");
                            selectedTerrain = null;
                        }
                        tempTerrainsList[i] = selectedTerrain;
                    }

                    // Neutral remove button color (do not use action color)
                    if (m_editorUtils.Button("Remove", GUILayout.Width(70)))
                    {
                        tempTerrainsList.RemoveAt(i);
                        GUILayout.EndHorizontal();
                        break;
                    }

                    GUILayout.EndHorizontal();
                }

                // Manually add a new empty entry the user can fill
                if (m_editorUtils.Button("AddEntry"))
                {
                    tempTerrainsList.Add(null);
                }

                _terrainsToProcess = tempTerrainsList.ToArray();
            }

            GUILayout.Space(5);
        }
        #endregion


        private Terrain GetNextAvailableTerrain(List<Terrain> currentTerrains)
        {
            Terrain[] allTerrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.InstanceID);
            foreach (var terrain in allTerrains)
            {
                if (!currentTerrains.Contains(terrain))
                {
                    return terrain;
                }
            }

            return null;
        }

        private void StartProcessingTerrains()
        {
            if (_autoManagerProcessing)
            {
                if (GaiaUtils.HasDynamicLoadedTerrains())
                {
                    if (_gaiaTerrainStitcher.enabledInManager)
                    {
                        GaiaUtils.CallFunctionOnDynamicLoadedTerrains(StitchTerrain, false);
                    }

                    if (_heightmapTerraceRemover.enabledInManager)
                    {
                        GaiaUtils.CallFunctionOnDynamicLoadedTerrains(RemoveTerraceFromTerrain, false);
                    }

                    if (_terrainTextureAligner.enabledInManager)
                    {
                        GaiaUtils.CallFunctionOnDynamicLoadedTerrains(AlignTerrainEdgeTextures, false);
                    }
                    
                    Debug.Log("Done Processing and Fixing the Terrains");
                }
                else
                {
                    if (_gaiaTerrainStitcher.enabledInManager)
                    {
                        foreach (Terrain t in _terrainsToProcess)
                        {
                            StitchTerrain(t);
                        }
                    }

                    if (_heightmapTerraceRemover.enabledInManager)
                    {
                        foreach (Terrain t in _terrainsToProcess)
                        {
                            RemoveTerraceFromTerrain(t);
                        }
                    }

                    if (_terrainTextureAligner.enabledInManager)
                    {
                        foreach (Terrain t in _terrainsToProcess)
                        {
                            AlignTerrainEdgeTextures(t);
                        }
                    }
                    
                    Debug.Log("Done Processing and Fixing the Terrains");
                }
            }
        }

        #region StitchTerrainRegion
        private void StitchTerrain(Terrain terrain)
        {
            Terrain neighborTerrain = null;

            //Before we begin, we need to load in the potential neighbor scenes for stitching
            TerrainScene neighborSceneNorth = null;
            TerrainScene neighborSceneSouth = null;
            TerrainScene neighborSceneEast = null;
            TerrainScene neighborSceneWest = null;

            if (GaiaUtils.HasDynamicLoadedTerrains())
            {
                TerrainScene ts =
                    TerrainLoaderManager.Instance.GetTerrainSceneAtPosition(terrain.transform.position +
                                                                            terrain.terrainData.size * 0.5f);
                neighborSceneNorth = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.North);
                neighborSceneSouth = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.South);
                neighborSceneEast = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.East);
                neighborSceneWest = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.West);
            }

            if (neighborSceneNorth != null)
            {
                if (neighborSceneNorth.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneNorth.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.North);

            if (neighborTerrain != null)
            {
                _gaiaTerrainStitcher.StartStitchTerrain(terrain, neighborTerrain);
            }

            if (neighborSceneNorth != null)
            {
                neighborSceneNorth.RemoveAllReferences();
            }

            if (neighborSceneSouth != null)
            {
                if (neighborSceneSouth.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneSouth.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.South);

            if (neighborTerrain != null)
            {
                _gaiaTerrainStitcher.StartStitchTerrain(terrain, neighborTerrain);
            }

            if (neighborSceneSouth != null)
            {
                neighborSceneSouth.RemoveAllReferences();
            }
            
            if (neighborSceneWest != null)
            {
                if (neighborSceneWest.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneWest.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.West);
            if (neighborTerrain != null)
            {
                _gaiaTerrainStitcher.StartStitchTerrain(terrain, neighborTerrain);
            }

            if (neighborSceneWest != null)
            {
                neighborSceneWest.RemoveAllReferences();
            }

            if (neighborSceneEast != null)
            {
                if (neighborSceneEast.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneEast.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.East);
            if (neighborTerrain != null)
            {
                _gaiaTerrainStitcher.StartStitchTerrain(terrain, neighborTerrain);
            }

            if (neighborSceneEast != null)
            {
                neighborSceneEast.RemoveAllReferences();
            }
        }
        #endregion

        #region TerraceRemoverRegion

        private void RemoveTerraceFromTerrain(Terrain terrain)
        {
            _heightmapTerraceRemover.StartProcessingTerrain(terrain);
        }
        #endregion

        #region TextureAlignerRegion
        private void AlignTerrainEdgeTextures(Terrain terrain)
        {
            Terrain neighborTerrain = null;

            //Before we begin, we need to load in the potential neighbor scenes for processing
            TerrainScene neighborSceneNorth = null;
            TerrainScene neighborSceneSouth = null;
            TerrainScene neighborSceneEast = null;
            TerrainScene neighborSceneWest = null;

            if (GaiaUtils.HasDynamicLoadedTerrains())
            {
                TerrainScene ts =
                    TerrainLoaderManager.Instance.GetTerrainSceneAtPosition(terrain.transform.position +
                                                                            terrain.terrainData.size * 0.5f);
                neighborSceneNorth = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.North);
                neighborSceneSouth = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.South);
                neighborSceneEast = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.East);
                neighborSceneWest = TerrainLoaderManager.Instance.TryGetNeighbor(ts, StitchDirection.West);
            }

            if (neighborSceneNorth != null)
            {
                if (neighborSceneNorth.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneNorth.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.North);

            if (neighborTerrain != null)
            {
                _terrainTextureAligner.StartAligningProcess(terrain, neighborTerrain);
            }

            if (neighborSceneNorth != null)
            {
                neighborSceneNorth.RemoveAllReferences();
            }

            if (neighborSceneSouth != null)
            {
                if (neighborSceneSouth.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneSouth.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.South);

            if (neighborTerrain != null)
            {
                _terrainTextureAligner.StartAligningProcess(terrain, neighborTerrain);
            }

            if (neighborSceneSouth != null)
            {
                neighborSceneSouth.RemoveAllReferences();
            }

            if (neighborSceneWest != null)
            {
                if (neighborSceneWest.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneWest.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.West);
            if (neighborTerrain != null)
            {
                _terrainTextureAligner.StartAligningProcess(terrain, neighborTerrain);
            }

            if (neighborSceneWest != null)
            {
                neighborSceneWest.RemoveAllReferences();
            }

            if (neighborSceneEast != null)
            {
                if (neighborSceneEast.m_regularLoadState != LoadState.Loaded)
                {
                    neighborSceneEast.AddRegularReference(TerrainLoaderManager.Instance.gameObject);
                }
            }

            neighborTerrain = TerrainHelper.GetTerrainNeighbor(terrain, StitchDirection.East);
            if (neighborTerrain != null)
            {
                _terrainTextureAligner.StartAligningProcess(terrain, neighborTerrain);
            }

            if (neighborSceneEast != null)
            {
                neighborSceneEast.RemoveAllReferences();
            }
        }
        #endregion
    }
}
