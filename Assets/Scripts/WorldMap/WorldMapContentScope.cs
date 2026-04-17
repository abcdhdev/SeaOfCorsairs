using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldMapContentScope : MonoBehaviour
{
    [SerializeField] private string mapId = "1-1";

    private readonly Dictionary<Renderer, bool> localRendererEnabledStates = new();
    private readonly Dictionary<Behaviour, bool> localBehaviourEnabledStates = new();
    private bool localVisibilityInitialized;
    private bool localContentVisible = true;

    public string MapId => WorldMapCatalog.NormalizeMapId(mapId);

    public void SetLocalContentVisible(bool visible)
    {
        if (!localVisibilityInitialized || localContentVisible != visible)
        {
            localVisibilityInitialized = true;
            localContentVisible = visible;
            ApplyLocalContentVisibility(visible);
        }
    }

    private void ApplyLocalContentVisibility(bool visible)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null)
            {
                continue;
            }

            if (!localRendererEnabledStates.TryGetValue(renderer, out bool authoredEnabled))
            {
                authoredEnabled = renderer.enabled;
                localRendererEnabledStates[renderer] = authoredEnabled;
            }

            renderer.enabled = visible && authoredEnabled;
        }

        SetLocalBehaviourVisibility(GetComponentsInChildren<Terrain>(true), visible);
        SetLocalBehaviourVisibility(GetComponentsInChildren<Light>(true), visible);
        SetLocalBehaviourVisibility(GetComponentsInChildren<AudioSource>(true), visible);
    }

    private void SetLocalBehaviourVisibility<T>(T[] behaviours, bool visible) where T : Behaviour
    {
        for (int index = 0; index < behaviours.Length; index++)
        {
            T behaviour = behaviours[index];
            if (behaviour == null)
            {
                continue;
            }

            if (!localBehaviourEnabledStates.TryGetValue(behaviour, out bool authoredEnabled))
            {
                authoredEnabled = behaviour.enabled;
                localBehaviourEnabledStates[behaviour] = authoredEnabled;
            }

            behaviour.enabled = visible && authoredEnabled;
        }
    }

    private void OnValidate()
    {
        mapId = WorldMapCatalog.NormalizeMapId(mapId);
        if (string.IsNullOrWhiteSpace(mapId))
        {
            mapId = "1-1";
        }
    }
}
