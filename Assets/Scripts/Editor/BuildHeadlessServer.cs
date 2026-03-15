using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildHeadlessServer
{
    // Build output relative to project root.
    private const string DefaultOutputDir = "Builds/ServerWindows";
    private const string DefaultExeName = "SeaWarsServer.exe";

    // Unity CLI:
    //   Unity.exe -batchmode -quit -nographics -projectPath <path> -executeMethod BuildHeadlessServer.BuildWindowsHeadlessServer
    public static void BuildWindowsHeadlessServer()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputDir = Path.Combine(projectRoot, DefaultOutputDir);
        Directory.CreateDirectory(outputDir);

        string locationPathName = Path.Combine(outputDir, DefaultExeName);

        string[] scenes = GetEnabledScenes();
        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("No enabled scenes in Build Settings (ProjectSettings/EditorBuildSettings.asset).");
        }

        Debug.Log($"[BuildHeadlessServer] Building Windows headless server to: {locationPathName}");

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPathName,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            // Build as a normal Windows player. We'll run it headless via runtime args:
            //   -batchmode -nographics -mode server
            // This avoids requiring the "Dedicated Server Build Support" module.
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report == null)
        {
            throw new InvalidOperationException("BuildPipeline.BuildPlayer returned null report.");
        }

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException($"Build failed: {report.summary.result} (errors: {report.summary.totalErrors}, warnings: {report.summary.totalWarnings}).");
        }

        Debug.Log($"[BuildHeadlessServer] Build succeeded. Size: {report.summary.totalSize} bytes. Time: {report.summary.totalTime}.");
    }

    private static string[] GetEnabledScenes()
    {
        EditorBuildSettingsScene[] editorScenes = EditorBuildSettings.scenes;
        if (editorScenes == null || editorScenes.Length == 0)
        {
            return Array.Empty<string>();
        }

        int count = 0;
        for (int i = 0; i < editorScenes.Length; i++)
        {
            if (editorScenes[i] != null && editorScenes[i].enabled)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return Array.Empty<string>();
        }

        string[] scenes = new string[count];
        int idx = 0;
        for (int i = 0; i < editorScenes.Length; i++)
        {
            if (editorScenes[i] != null && editorScenes[i].enabled)
            {
                scenes[idx++] = editorScenes[i].path;
            }
        }

        return scenes;
    }
}
