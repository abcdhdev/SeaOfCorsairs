using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_POST_PROCESSING_STACK_V2
using UnityEngine.Rendering.PostProcessing;
#endif

namespace Gaia
{
    /// <summary>
    /// Holds all relevant settings and prefab references for setting up lighting in the built-in render pipeline
    /// </summary>
    /// 
    [CreateAssetMenu(menuName = "Procedural Worlds/Gaia/Lighting Preset BuiltIn")]
    public class LightingPresetBuiltIn : ScriptableObject
    {
        public string m_displayName;
        public GameObject m_directionalLightPrefab;
        public GameObject m_globalPostProcessingPrefab;
     
#if UNITY_POST_PROCESSING_STACK_V2
        public PostProcessProfile m_globalPostProcessingProfile;
        
        // These can remain instance members as their state is only relevant for the duration of a single Apply() call.
        //private bool m_overwriteProfilePermission;
        //private bool m_keepProfilePermission;
#endif
        public EnvironmentBuiltInURP m_environmentBuiltIn; // Assuming this is correct, though named URP

        [HideInInspector]
        public bool m_enableDynamicDepthOfField = true;
        public Camera m_mainCamera;

        // private string m_lastCreatedProfile; // Removed: Will use EditorPrefs instead

#if UNITY_EDITOR
        // Helper method to get last created profile path from EditorPrefs
        private string GetLastCreatedProfilePathForPreset(string originalPresetProfileAssetPath)
        {
            if (string.IsNullOrEmpty(originalPresetProfileAssetPath)) return null;
            // Using a specific key prefix for BuiltIn
            return EditorPrefs.GetString($"Gaia.LightingPresetBuiltIn.LastCreated.{originalPresetProfileAssetPath}", null);
        }

        // Helper method to set last created profile path in EditorPrefs
        private void SetLastCreatedProfilePathForPreset(string originalPresetProfileAssetPath, string lastCreatedPathInSessionFolder)
        {
            if (string.IsNullOrEmpty(originalPresetProfileAssetPath)) return;

            if (string.IsNullOrEmpty(lastCreatedPathInSessionFolder))
            {
                EditorPrefs.DeleteKey($"Gaia.LightingPresetBuiltIn.LastCreated.{originalPresetProfileAssetPath}");
            }
            else
            {
                EditorPrefs.SetString($"Gaia.LightingPresetBuiltIn.LastCreated.{originalPresetProfileAssetPath}", lastCreatedPathInSessionFolder);
            }
        }
#endif

        public void Apply(bool addPPLayerToCam = true)
        {
#if UNITY_EDITOR
            GameObject lightingObject = GaiaUtils.GetLightingObject(false);

            RemoveFromScene();
            lightingObject = GaiaUtils.GetLightingObject(true);

            var allLights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < allLights.Length; i++)
            {
                Light light = allLights[i];
                if (light.type == LightType.Directional)
                {
                    light.gameObject.SetActive(false);
                }
            }
#if UNITY_POST_PROCESSING_STACK_V2
            PostProcessProfile copiedPostProcessingProfile = null;
            string originalGppProfilePath = null;

            if (m_globalPostProcessingProfile != null)
            {
                originalGppProfilePath = AssetDatabase.GetAssetPath(m_globalPostProcessingProfile);
                string targetFolder = GaiaDirectories.GetSRPLightingProfilePathForSession(); // Assuming this gets the Built-in path
                
                if (!AssetDatabase.IsValidFolder(targetFolder))
                {
                    // More robust folder creation
                    string[] folderParts = targetFolder.Split('/');
                    string currentPath = folderParts[0];
                    for (int j = 1; j < folderParts.Length; j++)
                    {
                        if (!AssetDatabase.IsValidFolder(currentPath + "/" + folderParts[j]))
                        {
                            AssetDatabase.CreateFolder(currentPath, folderParts[j]);
                        }
                        currentPath += "/" + folderParts[j];
                    }
                    AssetDatabase.Refresh();
                }
                copiedPostProcessingProfile = CopyProfileAsset(originalGppProfilePath, targetFolder); // Renamed for clarity
                if (copiedPostProcessingProfile != null)
                {
                    EditorUtility.SetDirty(copiedPostProcessingProfile);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    copiedPostProcessingProfile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(AssetDatabase.GetAssetPath(copiedPostProcessingProfile));
                    if (copiedPostProcessingProfile == null)
                    {
                        Debug.LogError($"BuiltIn: Failed to re-load copiedPostProcessingProfile ({originalGppProfilePath}) after save/refresh.");
                    }
                }
            }
#endif

            if (m_directionalLightPrefab != null)
            {
                GameObject newGO = GameObject.Instantiate(m_directionalLightPrefab, lightingObject.transform);
                newGO.name = newGO.name.Replace("(Clone)", "");
            }
            if (m_globalPostProcessingPrefab != null)
            {
#if UNITY_POST_PROCESSING_STACK_V2
                GameObject newGO = GameObject.Instantiate(m_globalPostProcessingPrefab, lightingObject.transform);
                newGO.name = newGO.name.Replace("(Clone)", "");
                PostProcessVolume vol = newGO.GetComponent<PostProcessVolume>();
                if (vol != null && copiedPostProcessingProfile != null)
                {
                    Undo.RecordObject(vol, "Assign Copied PostProcessProfile");
                    vol.profile = copiedPostProcessingProfile; // For PostProcessVolume, 'profile' is correct
                    EditorUtility.SetDirty(vol);
                    if (PrefabUtility.IsPartOfPrefabInstance(newGO))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(vol);
                    }
                     if (vol.profile != copiedPostProcessingProfile)
                         Debug.LogError($"BuiltIn: FAILED to assign profile. Is {vol.profile?.name}, expected {copiedPostProcessingProfile.name}");
                }
                 if (m_enableDynamicDepthOfField == true)
                 {
                    if (m_mainCamera == null)
                    {
                        m_mainCamera = Camera.main;
                        if (m_mainCamera == null)
                        {
                            Debug.LogError("No camera assigned or found!, Please assign the Camera in the PostProcessing object in the Dynamic Depth of Field script");
                        }
                    }
                   if (m_mainCamera != null && !m_mainCamera.gameObject.GetComponent<DynamicDepthOfField>())
                    {
                        DynamicDepthOfField DDOF = m_mainCamera.gameObject.AddComponent<DynamicDepthOfField>();
                        DDOF.mainCamera = m_mainCamera;
                    }
                 }
#endif
            }
            if (m_environmentBuiltIn != null)
            {
                m_environmentBuiltIn.Apply();
            }
            if (addPPLayerToCam)
            {
#if UNITY_POST_PROCESSING_STACK_V2
                Camera cam = Camera.main;
                GameObject playerObj = GaiaUtils.GetPlayerObject(false);
                if (playerObj != null)
                {
                    Camera playerCam = playerObj.GetComponentInChildren<Camera>();
                    if (playerCam != null)
                    {
                        cam = playerCam;
                    }
                }

                if (cam != null)
                {
                    PostProcessLayer layer = cam.GetComponent<PostProcessLayer>();
                    if (layer == null)
                    {
                        layer = cam.gameObject.AddComponent<PostProcessLayer>();
                    }
                    // Configure layer as needed
                    layer.volumeLayer = LayerMask.GetMask("PostProcessing"); // Example, ensure this layer exists or use a suitable one
                    layer.volumeTrigger = cam.transform; // Or specific trigger
                    // Ensure other settings like antialiasingMode are appropriate
                }
                else
                {
                    Debug.LogWarning("Could not find a camera: Post-Processing was NOT added to the camera.");
                }
#endif
            }
#endif
        }

#if UNITY_EDITOR && UNITY_POST_PROCESSING_STACK_V2
        // Renamed for clarity, originalPresetProfileAssetPath is the path of m_globalPostProcessingProfile from this SO.
        private PostProcessProfile CopyProfileAsset(string originalPresetProfileAssetPath, string targetFolder)
        {
            if (string.IsNullOrEmpty(originalPresetProfileAssetPath) || !AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(originalPresetProfileAssetPath))
            {
                Debug.LogError($"BuiltIn Error: Original asset at path '{originalPresetProfileAssetPath}' not found or path is invalid. Unable to copy.");
                return null;
            }

            string lastKnownCopiedProfilePath = GetLastCreatedProfilePathForPreset(originalPresetProfileAssetPath);
            string originalFileName = System.IO.Path.GetFileName(originalPresetProfileAssetPath);
            string baseDestinationPath = System.IO.Path.Combine(targetFolder, originalFileName);
            string pathToUseForCopy;

            bool baseFileExists = System.IO.File.Exists(baseDestinationPath);
            bool lastKnownCopiedFileExistsAndRelevant = !string.IsNullOrEmpty(lastKnownCopiedProfilePath) &&
                                                        System.IO.Path.GetDirectoryName(lastKnownCopiedProfilePath).Replace("\\", "/") == targetFolder.Replace("\\", "/") &&
                                                        System.IO.File.Exists(lastKnownCopiedProfilePath);

            if (baseFileExists || lastKnownCopiedFileExistsAndRelevant)
            {
                string suggestedOverwritePath = baseDestinationPath;
                if (lastKnownCopiedFileExistsAndRelevant)
                {
                    suggestedOverwritePath = lastKnownCopiedProfilePath;
                }
                
                string overwriteTargetDisplay = System.IO.Path.GetFileName(suggestedOverwritePath);
                string dialogMessage = $"A lighting profile related to '{originalFileName}' (likely '{overwriteTargetDisplay}') already exists in the session folder. Overwrite '{overwriteTargetDisplay}' or create a new copy?";
                 if (overwriteTargetDisplay == originalFileName && suggestedOverwritePath == baseDestinationPath)
                {
                    dialogMessage = $"The lighting profile '{originalFileName}' already exists in the session folder. Overwrite it or create a new copy?";
                }

                if (EditorUtility.DisplayDialog("Lighting Profile Exists (Built-In)", dialogMessage, "Overwrite Existing", "Create New Copy"))
                {
                    pathToUseForCopy = suggestedOverwritePath;
                    AssetDatabase.CopyAsset(originalPresetProfileAssetPath, pathToUseForCopy);
                }
                else
                {
                    pathToUseForCopy = AssetDatabase.GenerateUniqueAssetPath(baseDestinationPath);
                    AssetDatabase.CopyAsset(originalPresetProfileAssetPath, pathToUseForCopy);
                }
            }
            else
            {
                pathToUseForCopy = baseDestinationPath;
                AssetDatabase.CopyAsset(originalPresetProfileAssetPath, pathToUseForCopy);
            }

            SetLastCreatedProfilePathForPreset(originalPresetProfileAssetPath, pathToUseForCopy);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            PostProcessProfile loadedProfile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(pathToUseForCopy);
            if (loadedProfile == null)
            {
                Debug.LogError($"BuiltIn: Failed to load the copied PostProcessProfile from path: {pathToUseForCopy}. Original: {originalPresetProfileAssetPath}");
            }
            else
            {
                //Debug.Log($"BuiltIn: Successfully copied and loaded PostProcessProfile: {loadedProfile.name} from {pathToUseForCopy}");
            }
            return loadedProfile;
        }
#endif
        public void RemoveFromScene()
        {
            GameObject lightingObject = GaiaUtils.GetLightingObject(false);
            if (lightingObject != null)
            {
                if (Application.isPlaying)
                {
                    GameObject.Destroy(lightingObject);
                }
                else
                {
                    GameObject.DestroyImmediate(lightingObject);
                }
            }
        }

        public void IngestFromScene()
        {
#if UNITY_EDITOR
            string currentPath = AssetDatabase.GetAssetPath(this);
            currentPath = currentPath.Substring(0, currentPath.LastIndexOf("/"));
            var allLights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < allLights.Length; i++)
            {
                Light light = allLights[i];
                if (light.type == LightType.Directional)
                {
                    string savePath = currentPath + "/" + this.name + " DirectionalLight.prefab";
                    PrefabUtility.SaveAsPrefabAsset(light.gameObject, savePath);
                    m_directionalLightPrefab = (GameObject)AssetDatabase.LoadAssetAtPath(savePath, typeof(GameObject));
                    break;
                }
            }

#if UNITY_POST_PROCESSING_STACK_V2
            var allPPVolumes = GameObject.FindObjectsByType<PostProcessVolume>(FindObjectsSortMode.None);
            for (int i = 0; i < allPPVolumes.Length; i++)
            {
                PostProcessVolume ppv = allPPVolumes[i];
                if (ppv.isGlobal)
                {
                    string savePath = currentPath + "/" + this.name + " GlobalPostProcessing.prefab";
                    PrefabUtility.SaveAsPrefabAsset(ppv.gameObject, savePath);
                    m_globalPostProcessingPrefab = (GameObject)AssetDatabase.LoadAssetAtPath(savePath, typeof(GameObject));
                    break; 
                }
            }
#endif

            if (m_environmentBuiltIn == null)
            {
                string savePath = currentPath + "/" + this.name + " Environment.asset";
                // Assuming EnvironmentBuiltInURP is the correct type for BuiltIn pipeline environment settings
                EnvironmentBuiltInURP eb = ScriptableObject.CreateInstance<EnvironmentBuiltInURP>(); 
                AssetDatabase.CreateAsset(eb, savePath);
                m_environmentBuiltIn = (EnvironmentBuiltInURP)AssetDatabase.LoadAssetAtPath(savePath, typeof(EnvironmentBuiltInURP));
            }

            if (m_environmentBuiltIn != null) // Check if it was created or already existed
            {
                m_environmentBuiltIn.IngestFromScene();
            }
            EditorUtility.SetDirty(this);
#endif
        }
    }
}