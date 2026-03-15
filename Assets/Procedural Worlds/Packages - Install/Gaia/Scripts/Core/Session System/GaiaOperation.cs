using UnityEngine;
using System.Collections;
using System;
using UnityEditor;

namespace Gaia
{

    public enum SessionPlaybackState {Queued, Started }

    /// <summary>
    /// A gaia operation - serialises and deserialises and executes a gaia operation
    /// </summary>
    [System.Serializable]
    public class GaiaOperation
    {

        /// <summary>
        /// Settings for a world creation operation
        /// </summary>
        private WorldCreationSettings m_worldCreationSettings = null;
        public WorldCreationSettings WorldCreationSettings
        { 
            get {
                if (m_worldCreationSettings == null)
                {
#if UNITY_EDITOR
                    m_worldCreationSettings = (WorldCreationSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(WorldCreationSettings));
#endif
                }
                return m_worldCreationSettings;
            } 
        }
        /// <summary>
        /// Sets a world creation settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="worldCreationSettings"></param>
        public void SetTemporary(WorldCreationSettings worldCreationSettings)
        { 
            m_worldCreationSettings = worldCreationSettings;
        }


        private StamperSettings m_stamperSettings = null;
        public StamperSettings StamperSettings
        {
            get
            {
                if (m_stamperSettings == null)
                {
#if UNITY_EDITOR
                    m_stamperSettings = (StamperSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(StamperSettings));
#endif
                }
                return m_stamperSettings;
            }
        }

        /// <summary>
        /// Sets a stamper settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="stamperSettings"></param>
        public void SetTemporary(StamperSettings stamperSettings)
        {
            m_stamperSettings = stamperSettings;
        }

        private SpawnOperationSettings m_spawnOperationSettings = null;
        public SpawnOperationSettings SpawnOperationSettings
        {
            get
            {
                if (m_spawnOperationSettings == null)
                {
#if UNITY_EDITOR
                    m_spawnOperationSettings = (SpawnOperationSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(SpawnOperationSettings));
#endif
                }
                return m_spawnOperationSettings;
            }
        }

        /// <summary>
        /// Sets a spawner settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="spawnerSettings"></param>
        public void SetTemporary(SpawnOperationSettings spawnerSettings)
        {
            m_spawnOperationSettings = spawnerSettings;
        }

        private FlattenOperationSettings m_flattenOperationSettings = null;
        public FlattenOperationSettings FlattenOperationSettings
        {
            get
            {
                if (m_flattenOperationSettings == null)
                {
#if UNITY_EDITOR
                    m_flattenOperationSettings = (FlattenOperationSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(FlattenOperationSettings));
#endif
                }
                return m_flattenOperationSettings;
            }
        }

        /// <summary>
        /// Sets a flatten settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="flattenSettings"></param>
        public void SetTemporary(FlattenOperationSettings flattenSettings)
        {
            m_flattenOperationSettings = flattenSettings;
        }

        private UndoRedoOperationSettings m_undoRedoOperationSettings = null;
        public UndoRedoOperationSettings UndoRedoOperationSettings      {
            get
            {
                if (m_undoRedoOperationSettings == null)
                {
#if UNITY_EDITOR
                    m_undoRedoOperationSettings = (UndoRedoOperationSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(UndoRedoOperationSettings));
#endif
                }
                return m_undoRedoOperationSettings;
            }
        }
        /// <summary>
        /// Sets a undo settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="undoSettings"></param>
        public void SetTemporary(UndoRedoOperationSettings undoSettings)
        {
            m_undoRedoOperationSettings = undoSettings;
        }

        private ClearOperationSettings m_clearOperationSettings = null;
        public ClearOperationSettings ClearOperationSettings
        {
            get
            {
                if (m_clearOperationSettings == null)
                {
#if UNITY_EDITOR
                    m_clearOperationSettings = (ClearOperationSettings) AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(ClearOperationSettings));
#endif
                }
                return m_clearOperationSettings;
            }
        }
        /// <summary>
        /// Sets a clear op settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="clearSettings"></param>
        public void SetTemporary(ClearOperationSettings clearSettings)
        {
            m_clearOperationSettings = clearSettings;
        }

        private RemoveNonBiomeResourcesSettings m_removeNonBiomeResourcesSettings = null;
        public RemoveNonBiomeResourcesSettings RemoveNonBiomeResourcesSettings
        {
            get
            {
                if (m_removeNonBiomeResourcesSettings == null)
                {
#if UNITY_EDITOR
                    m_removeNonBiomeResourcesSettings = (RemoveNonBiomeResourcesSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(RemoveNonBiomeResourcesSettings));
#endif
                }
                return m_removeNonBiomeResourcesSettings;
            }
        }
        /// <summary>
        /// Sets a remove non biome assets settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="removeNonBiomeSettings"></param>
        public void SetTemporary(RemoveNonBiomeResourcesSettings removeNonBiomeSettings)
        {
            m_removeNonBiomeResourcesSettings = removeNonBiomeSettings;
        }

        private ExportMaskMapOperationSettings m_exportMaskMapOperationSettings = null;
        public ExportMaskMapOperationSettings ExportMaskMapOperationSettings
        {
            get
            {
                if (m_exportMaskMapOperationSettings == null)
                {
#if UNITY_EDITOR
                    m_exportMaskMapOperationSettings = (ExportMaskMapOperationSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(ExportMaskMapOperationSettings));
#endif
                }
                return m_exportMaskMapOperationSettings;
            }
        }
        /// <summary>
        /// Sets a Export map op assets settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="exportMapSettings"></param>
        public void SetTemporary(ExportMaskMapOperationSettings exportMapSettings)
        {
            m_exportMaskMapOperationSettings = exportMapSettings;
        }

        private ScriptableObject m_externalScriptableObject = null;
        public ScriptableObject ExternalOperationScriptableObject
        {
            get
            {
                if (m_externalScriptableObject == null)
                {
#if UNITY_EDITOR
                    m_externalScriptableObject = (ScriptableObject)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(ScriptableObject));
#endif
                }
                return m_externalScriptableObject;
            }
        }
        /// <summary>
        /// Sets an external scriptable object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="externalScriptableObject"></param>
        public void SetTemporary(ScriptableObject externalScriptableObject)
        {
            m_externalScriptableObject = externalScriptableObject;
        }

        private WorldMapStampSettings m_worldMapStampSettings = null;
        public WorldMapStampSettings WorldMapStampSettings
        {
            get
            {
                if (m_worldMapStampSettings == null)
                {
#if UNITY_EDITOR
                    m_worldMapStampSettings = (WorldMapStampSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(WorldMapStampSettings));
#endif
                }
                return m_worldMapStampSettings;
            }
        }
        /// <summary>
        /// Sets an world map stamp settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="worldMapStampSettings"></param>
        public void SetTemporary(WorldMapStampSettings worldMapStampSettings)
        {
            m_worldMapStampSettings = worldMapStampSettings;
        }

        private SingleTileCreationSettings m_singleTileCreationSettings = null;
        public SingleTileCreationSettings SingleTileCreationSettings
        {
            get
            {
                if (m_singleTileCreationSettings == null)
                {
#if UNITY_EDITOR
                    m_singleTileCreationSettings = (SingleTileCreationSettings)AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(scriptableObjectAssetGUID), typeof(SingleTileCreationSettings));
#endif
                }
                return m_singleTileCreationSettings;
            }
        }
        /// <summary>
        /// Sets a single Tile Creation settings object for temporary use only, can be used to bypass the creation of session assets
        /// </summary>
        /// <param name="singleTileCreationSettings"></param>
        public void SetTemporary(SingleTileCreationSettings singleTileCreationSettings)
        {
            m_singleTileCreationSettings = singleTileCreationSettings;
        }

        /// <summary>
        /// An optional description
        /// </summary>
        public string m_description;

        /// <summary>
        /// The types of operations we can record
        /// </summary>
        public enum OperationType { CreateWorld, FlattenTerrain, SmoothTerrain, ClearSpawns, Stamp, StampUndo, StampRedo, Spawn, RemoveNonBiomeResources,
            MaskMapExport,
            ClearWorld,
            ExportWorldMapToLocalMap,
            External,
            WorldMapStamp,
            UpdateWorld,
            CreateSingleTerrainTile
        }

        /// <summary>
        /// The operation type
        /// </summary>
        public OperationType m_operationType;

        /// <summary>
        /// Whether or not the operation is active
        /// </summary>
        public bool m_isActive = true;

        ///// <summary>
        ///// The name of the object that generated this operation
        ///// </summary>
        //public string m_generatedByName;

        ///// <summary>
        ///// The ID of the onject that generated this operation
        ///// </summary>
        //public string m_generatedByID;

        ///// <summary>
        ///// The type of object that generated this operation
        ///// </summary>
        //public string m_generatedByType;

        /// <summary>
        /// The list of terrains affected by this operation.
        /// </summary>
        public string[] m_affectedTerrainNames = new string[0];

        /// <summary>
        /// When the operation was recorded
        /// </summary>
        public string m_operationDateTime = DateTime.Now.ToString();


        /// <summary>
        /// GUID for the scriptable object that holds the actual settings data for the operation
        /// </summary>
        public string scriptableObjectAssetGUID;

        /// <summary>
        /// Whether or not we are folded out in the editor
        /// </summary>
        public bool m_isFoldedOut = false;


        public SessionPlaybackState sessionPlaybackState = SessionPlaybackState.Started;

        /// <summary>
        /// Whether the affected terrains section on the GUI is folded out or not
        /// </summary>
        public bool m_terrainsFoldedOut;

        /// <summary>
        /// Holds data from a serialized external action that was saved in the session.
        /// </summary>
        public byte[] m_serializedExternalAction;
    }
}