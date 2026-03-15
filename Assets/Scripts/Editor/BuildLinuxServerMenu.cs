using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildLinuxServerMenu
{
    private const string DefaultOutputDir = "Builds/LinuxServerBuild";
    private const string DefaultExeName = "linux-server-build.x86_64";

    [MenuItem("Tools/Build Linux Server Headless")]
    public static void BuildLinuxHeadlessServer()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputDir = Path.Combine(projectRoot, DefaultOutputDir);
        Directory.CreateDirectory(outputDir);

        string locationPathName = Path.Combine(outputDir, DefaultExeName);

        Debug.Log($"[BuildLinuxServer] Building Linux headless server to: {locationPathName}");

        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = locationPathName,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.Development,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Build failed: {report.summary.result} (errors: {report.summary.totalErrors}).");
        }
        else
        {
            Debug.Log($"[BuildLinuxServer] Build succeeded. Size: {report.summary.totalSize} bytes.");
        }
    }

    private static string[] GetEnabledScenes()
    {
        EditorBuildSettingsScene[] editorScenes = EditorBuildSettings.scenes;
        if (editorScenes == null || editorScenes.Length == 0) return Array.Empty<string>();
        int count = 0;
        foreach (var s in editorScenes) if (s != null && s.enabled) count++;
        string[] scenes = new string[count];
        int idx = 0;
        foreach (var s in editorScenes) if (s != null && s.enabled) scenes[idx++] = s.path;
        return scenes;
    }
}
