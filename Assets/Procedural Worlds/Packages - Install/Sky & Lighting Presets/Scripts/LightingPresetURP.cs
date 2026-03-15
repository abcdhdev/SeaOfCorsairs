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
#if UPPipeline
using UnityEngine.Rendering.Universal;
#endif

namespace Gaia
{
    [CreateAssetMenu(menuName = "Procedural Worlds/Gaia/Lighting Preset URP")]
    public class LightingPresetURP : ScriptableObject
    {
        public string m_displayName;
        public GameObject m_directionalLightPrefab;
        public GameObject m_globalPostProcessingPrefab;
#if UPPipeline
        public VolumeProfile m_gppVolumeProfile;
#endif
        public EnvironmentBuiltInURP m_environment;

        [HideInInspector]
        public bool m_enableDynamicDepthOfField = true;
        public Camera m_mainCamera;


#if UNITY_EDITOR
        // Helper method to get last created profile path from EditorPrefs
        private string GetLastCreatedProfilePathForPreset(string originalPresetVolumeProfileAssetPath)
        {
            if (string.IsNullOrEmpty(originalPresetVolumeProfileAssetPath)) return null;
            return EditorPrefs.GetString($"Gaia.LightingPresetURP.LastCreated.{originalPresetVolumeProfileAssetPath}", null);
        }

        // Helper method to set last created profile path in EditorPrefs
        private void SetLastCreatedProfilePathForPreset(string originalPresetVolumeProfileAssetPath, string lastCreatedPathInSessionFolder)
        {
            if (string.IsNullOrEmpty(originalPresetVolumeProfileAssetPath)) return;

            if (string.IsNullOrEmpty(lastCreatedPathInSessionFolder))
            {
                EditorPrefs.DeleteKey($"Gaia.LightingPresetURP.LastCreated.{originalPresetVolumeProfileAssetPath}");
            }
            else
            {
                EditorPrefs.SetString($"Gaia.LightingPresetURP.LastCreated.{originalPresetVolumeProfileAssetPath}", lastCreatedPathInSessionFolder);
            }
        }
#endif

        public void Apply(bool addPPLayerToCam = true)
        {
#if UNITY_EDITOR && UPPipeline
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
            
            VolumeProfile copiedPostProcessingVolume = null;

            if (m_gppVolumeProfile != null)
            {
                // This is the path of the VolumeProfile asset assigned to this LightingPresetURP ScriptableObject.
                // It will serve as a stable key for EditorPrefs.
                string originalPresetVolumeProfileAssetPath = AssetDatabase.GetAssetPath(m_gppVolumeProfile);
                string targetFolder = GaiaDirectories.GetURPLightingProfilePathForSession();
                
                if (!AssetDatabase.IsValidFolder(targetFolder))
                {
                    string[] folderParts = targetFolder.Split('/');
                    string currentPath = folderParts[0];
                    for (int i = 1; i < folderParts.Length; i++)
                    {
                        if (!AssetDatabase.IsValidFolder(currentPath + "/" + folderParts[i]))
                        {
                            AssetDatabase.CreateFolder(currentPath, folderParts[i]);
                        }
                        currentPath += "/" + folderParts[i];
                    }
                    AssetDatabase.Refresh(); 
                }

                copiedPostProcessingVolume = CopyVolumeProfileAsset(originalPresetVolumeProfileAssetPath, targetFolder);
                
                if (copiedPostProcessingVolume != null)
                {
                    EditorUtility.SetDirty(copiedPostProcessingVolume);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    copiedPostProcessingVolume = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GetAssetPath(copiedPostProcessingVolume));
                    if (copiedPostProcessingVolume == null)
                    {
                        Debug.LogError("Failed to re-load copiedPostProcessingVolume after save/refresh. This is unexpected.");
                    }
                }
            }

            if (m_directionalLightPrefab != null)
            {
                GameObject.Instantiate(m_directionalLightPrefab, lightingObject.transform);
            }

            if (m_globalPostProcessingPrefab != null)
            {
                GameObject postProcessingGO = GameObject.Instantiate(m_globalPostProcessingPrefab, lightingObject.transform);
                Volume vol = postProcessingGO.GetComponent<Volume>();
                
                if (vol != null && copiedPostProcessingVolume != null)
                {
                    Undo.RecordObject(vol, "Assign Copied Shared Volume Profile");
                    vol.sharedProfile = copiedPostProcessingVolume; 
                    EditorUtility.SetDirty(vol); 
                    if (PrefabUtility.IsPartOfPrefabInstance(postProcessingGO))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(vol);
                    }

                    if (vol.sharedProfile == copiedPostProcessingVolume)
                    {
                        //Debug.Log($"Successfully assigned profile: {vol.sharedProfile?.name} to Volume on {postProcessingGO.name}");
                    }
                    else
                    {
                        Debug.LogError($"FAILED to assign profile. vol.sharedProfile is {vol.sharedProfile?.name}, expected {copiedPostProcessingVolume.name}");
                    }
                    
                    EditorUtility.SetDirty(vol); 
                    if (Selection.activeGameObject == postProcessingGO)
                    {
                        Selection.activeGameObject = null; 
                        EditorApplication.delayCall += () => Selection.activeGameObject = postProcessingGO; 
                    }
                }
                else if (vol == null)
                {
                    Debug.LogError($"Volume component not found on instantiated object: {postProcessingGO.name}");
                }
                else if (copiedPostProcessingVolume == null && m_gppVolumeProfile != null)
                {
                    Debug.LogError($"Failed to copy or load the VolumeProfile. 'copiedPostProcessingVolume' is null after copy attempt.");
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
            }

            if (m_environment != null)
            {
                m_environment.Apply();
            }
            if (addPPLayerToCam)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    UniversalAdditionalCameraData uacd = cam.GetComponent<UniversalAdditionalCameraData>();
                    if (uacd == null)
                    {
                        uacd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                    }
                    if (uacd != null)
                    {
                        uacd.renderPostProcessing = true;
                    }
                }
            }
#endif
        }

#if UNITY_EDITOR && UPPipeline
        private VolumeProfile CopyVolumeProfileAsset(string originalPresetVolumeProfileAssetPath, string targetFolder)
        {
            // originalPresetVolumeProfileAssetPath is the path of the m_gppVolumeProfile asset defined in this LightingPresetURP SO.
            // This will be our key for EditorPrefs.
            if (string.IsNullOrEmpty(originalPresetVolumeProfileAssetPath) || !AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(originalPresetVolumeProfileAssetPath))
            {
                Debug.LogError($"Error: Original asset at path '{originalPresetVolumeProfileAssetPath}' not found or path is invalid. Unable to copy.");
                return null;
            }

            // Get the path of the last profile created by this system for this specific originalPresetVolumeProfileAssetPath
            string lastKnownCopiedProfilePath = GetLastCreatedProfilePathForPreset(originalPresetVolumeProfileAssetPath);

            string originalFileName = System.IO.Path.GetFileName(originalPresetVolumeProfileAssetPath);
            // This is the path if we were to copy the original file name directly into the session folder
            string baseDestinationPath = System.IO.Path.Combine(targetFolder, originalFileName);
            string pathToUseForCopy; 

            bool baseFileExists = System.IO.File.Exists(baseDestinationPath);
            bool lastKnownCopiedFileExistsAndRelevant = !string.IsNullOrEmpty(lastKnownCopiedProfilePath) &&
                                                        System.IO.Path.GetDirectoryName(lastKnownCopiedProfilePath).Replace("\\", "/") == targetFolder.Replace("\\", "/") &&
                                                        System.IO.File.Exists(lastKnownCopiedProfilePath);

            if (baseFileExists || lastKnownCopiedFileExistsAndRelevant)
            {
                string suggestedOverwritePath = baseDestinationPath; // Default to overwriting the base name

                if (lastKnownCopiedFileExistsAndRelevant)
                {
                    // If a previously copied version by this system exists and is relevant (e.g. "Profile 1.asset"),
                    // suggest overwriting that one.
                    suggestedOverwritePath = lastKnownCopiedProfilePath;
                }
                // If only the base file exists (baseFileExists=true, lastKnownCopiedFileExistsAndRelevant=false), 
                // suggestedOverwritePath remains baseDestinationPath.

                string overwriteTargetDisplay = System.IO.Path.GetFileName(suggestedOverwritePath);
                string dialogMessage = $"A lighting profile related to '{originalFileName}' (likely '{overwriteTargetDisplay}') already exists in the session folder. Overwrite '{overwriteTargetDisplay}' or create a new copy?";
                
                // Simplify message if the suggested overwrite target is just the base name and no other "last copied" version was identified as more relevant.
                if (overwriteTargetDisplay == originalFileName && suggestedOverwritePath == baseDestinationPath) 
                {
                    dialogMessage = $"The lighting profile '{originalFileName}' already exists in the session folder. Overwrite it or create a new copy?";
                }

                if (EditorUtility.DisplayDialog("Lighting Profile Exists", dialogMessage, "Overwrite Existing", "Create New Copy"))
                {
                    // User chose to Overwrite the suggested path
                    pathToUseForCopy = suggestedOverwritePath;
                    AssetDatabase.CopyAsset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);
                }
                else
                {
                    // User chose to Create New Copy. Generate unique based on the original name.
                    pathToUseForCopy = AssetDatabase.GenerateUniqueAssetPath(baseDestinationPath);
                    AssetDatabase.CopyAsset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);
                }
            }
            else // Neither base file nor a relevant last-copied file exists, so just copy with the original name.
            {
                pathToUseForCopy = baseDestinationPath;
                AssetDatabase.CopyAsset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);
            }
            
            // Update EditorPrefs with the path that was actually used for this operation
            SetLastCreatedProfilePathForPreset(originalPresetVolumeProfileAssetPath, pathToUseForCopy);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            VolumeProfile loadedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(pathToUseForCopy);
            if (loadedProfile == null)
            {
                Debug.LogError($"Failed to load the copied VolumeProfile from path: {pathToUseForCopy}. Original path was: {originalPresetVolumeProfileAssetPath}");
            }
            else
            {
                //Debug.Log($"Successfully copied and loaded VolumeProfile: {loadedProfile.name} from {pathToUseForCopy}");
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

#if UPPipeline
            var allPPVolumes = GameObject.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            for (int i = 0; i < allPPVolumes.Length; i++)
            {
                Volume ppv = allPPVolumes[i];
                if (ppv.isGlobal)
                {
                    string savePath = currentPath + "/" + this.name + " GlobalPostProcessing.prefab";
                    PrefabUtility.SaveAsPrefabAsset(ppv.gameObject, savePath);
                    m_globalPostProcessingPrefab = (GameObject)AssetDatabase.LoadAssetAtPath(savePath, typeof(GameObject));
                    break;
                }
            }
#endif

            if (m_environment == null)
            {
                string savePath = currentPath + "/" + this.name + " Environment.asset";
                EnvironmentBuiltInURP eb = ScriptableObject.CreateInstance<EnvironmentBuiltInURP>();
                AssetDatabase.CreateAsset(eb, savePath); // Use savePath directly
                m_environment = (EnvironmentBuiltInURP)AssetDatabase.LoadAssetAtPath(savePath, typeof(EnvironmentBuiltInURP));
            }

            m_environment.IngestFromScene();
            EditorUtility.SetDirty(this);
#endif
        }
    }
}