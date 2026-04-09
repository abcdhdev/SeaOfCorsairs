using System;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public sealed class WorldMapSceneReference
{
    [SerializeField] private string scenePath = string.Empty;

#if UNITY_EDITOR
    [SerializeField] private SceneAsset sceneAsset;
#endif

    public string ScenePath => scenePath ?? string.Empty;
    public string SceneName => string.IsNullOrWhiteSpace(scenePath)
        ? string.Empty
        : Path.GetFileNameWithoutExtension(scenePath);

    public bool HasScenePath => !string.IsNullOrWhiteSpace(scenePath);

#if UNITY_EDITOR
    public SceneAsset SceneAsset => sceneAsset;
#endif

    public void SetScenePath(string newScenePath)
    {
        scenePath = string.IsNullOrWhiteSpace(newScenePath)
            ? string.Empty
            : newScenePath.Trim();
    }

#if UNITY_EDITOR
    public void SyncEditorState()
    {
        if (sceneAsset != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(sceneAsset);
            if (!string.Equals(scenePath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                scenePath = assetPath;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        }
    }

    public void SetSceneAsset(SceneAsset newSceneAsset)
    {
        sceneAsset = newSceneAsset;
        scenePath = sceneAsset != null
            ? AssetDatabase.GetAssetPath(sceneAsset)
            : string.Empty;
    }
#endif
}
