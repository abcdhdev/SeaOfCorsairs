using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures dedicated server builds start in a gameplay scene that contains Netcode bootstrap objects.
/// </summary>
public static class DedicatedServerBootstrap
{
#if UNITY_SERVER
    private const string ServerModeArg = "-mode";
    private const string ServerModeValue = "server";
    private const string ServerSceneEnvVar = "SEAWARS_SERVER_SCENE";
    private const string DefaultServerScene = "Server Bootstrap";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureServerScene()
    {
        if (!IsServerMode())
        {
            return;
        }

        string sceneToLoad = ResolveServerScene();
        if (string.IsNullOrWhiteSpace(sceneToLoad))
        {
            return;
        }

        Debug.Log($"[ServerBootstrap] Loading dedicated server scene '{sceneToLoad}'.");
        SceneManager.LoadScene(sceneToLoad, LoadSceneMode.Single);
    }

    private static bool IsServerMode()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], ServerModeArg, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(args[i + 1], ServerModeValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveServerScene()
    {
        string configured = Environment.GetEnvironmentVariable(ServerSceneEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return DefaultServerScene;
    }
#endif
}
