// One-click WebGL build to Builds/WebGL (outside Assets, gitignored). The page is
// generated from the persistent template at Assets/WebGLTemplates/NightRider, so
// editing the page = edit that template, not the build output.
//
// Menu: Night Rider > Build WebGL  (Ctrl+Shift+W). Then serve it with serve-web.ps1
// (or use Unity's File > Build And Run for a one-shot serve+open).

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebBuild
{
    const string OutDir = "Builds/WebGL";

    [MenuItem("Night Rider/Build WebGL %#w")]
    public static void BuildWebGL()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            if (!EditorUtility.DisplayDialog(
                    "Switch to WebGL?",
                    "The active platform isn't WebGL. Switch now and build?\n" +
                    "(The first switch can take a while, and needs the WebGL Build Support module installed via Unity Hub.)",
                    "Switch & Build", "Cancel"))
                return;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
        }

        var opts = new BuildPlayerOptions
        {
            scenes = ScenesToBuild(),
            locationPathName = OutDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        BuildSummary s = report.summary;

        if (s.result == BuildResult.Succeeded)
        {
            Debug.Log($"WebGL build succeeded → {OutDir}  ({s.totalSize / (1024 * 1024)} MB). " +
                      $"Serve it: pwsh ./serve-web.ps1");
            EditorUtility.RevealInFinder(OutDir + "/index.html");
        }
        else
        {
            Debug.LogError($"WebGL build {s.result}: {s.totalErrors} error(s).");
        }
    }

    // Scenes ticked in Build Settings; fall back to the one sample scene.
    static string[] ScenesToBuild()
    {
        var scenes = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) scenes.Add(s.path);
        if (scenes.Count == 0) scenes.Add("Assets/Scenes/SampleScene.unity");
        return scenes.ToArray();
    }
}
