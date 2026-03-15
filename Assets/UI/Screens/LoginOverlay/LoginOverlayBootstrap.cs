using UnityEngine;

public static class LoginOverlayBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureLoginOverlayExists()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        // We keep meta UI + HUD gating alive across scene loads to avoid HUD showing before the client is ready.
        var overlay = Object.FindFirstObjectByType<LoginOverlayController>();
        var gate = Object.FindFirstObjectByType<HudVisibilityGate>();

        if (overlay != null && gate != null)
        {
            return;
        }

        var go = new GameObject("AppUI")
        {
            hideFlags = HideFlags.DontSave
        };

        Object.DontDestroyOnLoad(go);

        if (overlay == null)
        {
            go.AddComponent<LoginOverlayController>();
        }

        if (gate == null)
        {
            go.AddComponent<HudVisibilityGate>();
        }
    }
}
