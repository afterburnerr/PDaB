using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FPLab.Gameplay.Maze;

// Run through the attached editor's CLI, after stopping Play and saving the scene.
public static class BuildPDaBLinux
{
    public static object Validate()
    {
        return new
        {
            unityVersion = Application.unityVersion,
            activeTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            linuxModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64),
            sceneExists = File.Exists("Assets/Scenes/SampleScene.unity"),
            playing = EditorApplication.isPlayingOrWillChangePlaymode,
            compiling = EditorApplication.isCompiling
        };
    }

    // Explicit opt-in: strip only generated editor preview geometry before saving.
    public static object ClearPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop Play and finish compilation before clearing the preview.");
        MazeBuilder[] builders = UnityEngine.Object.FindObjectsByType<MazeBuilder>(FindObjectsInactive.Include);
        foreach (MazeBuilder builder in builders)
        {
            builder.ClearGenerated();
            builder.gameObject.SendMessage("ReleaseMeshes", SendMessageOptions.DontRequireReceiver);
            builder.gameObject.SendMessage("ReleaseWallStyleMap", SendMessageOptions.DontRequireReceiver);
            EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
        }
        if (!EditorSceneManager.SaveOpenScenes())
            throw new IOException("Could not save the cleaned scene.");
        AssetDatabase.SaveAssets();
        return new { clearedBuilders = builders.Length, sceneBytes = new FileInfo("Assets/Scenes/SampleScene.unity").Length };
    }

    public static object RunClean()
    {
        ClearPreview();
        return Run();
    }

    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Stop Play and finish compilation before building PDaB.");
        const string scenePath = "Assets/Scenes/SampleScene.unity";
        if (!File.Exists(scenePath)) throw new FileNotFoundException("Missing game scene", scenePath);
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save all open scenes before the release build.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            throw new InvalidOperationException("Install Linux Build Support for this Unity editor.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
            throw new InvalidOperationException("Select Linux x86_64 as the active target before running this script; target switches can reload scripts.");

        string project = Directory.GetParent(Application.dataPath).FullName;
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string release = Path.Combine(Directory.GetParent(project).FullName, "FPLab-Releases", stamp);
        string output = Path.Combine(release, "PDaB-Linux");
        if (Directory.Exists(release)) throw new IOException("Release directory already exists: " + release);
        Directory.CreateDirectory(output);

        PlayerSettings.productName = "PDaB";
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = false;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        EditorUserBuildSettings.connectProfiler = false;
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = Path.Combine(output, "PDaB.x86_64"),
            target = BuildTarget.StandaloneLinux64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None
        });
        var receipt = new Receipt
        {
            product = "PDaB", unityVersion = Application.unityVersion, platform = "Linux x86_64",
            scriptingBackend = "Mono", development = false, scene = scenePath,
            result = report.summary.result.ToString(), totalBytes = report.summary.totalSize,
            errors = report.summary.totalErrors, warnings = report.summary.totalWarnings,
            durationSeconds = report.summary.totalTime.TotalSeconds,
            outputDirectory = output, releaseDirectory = release, createdUtc = DateTime.UtcNow.ToString("O")
        };
        File.WriteAllText(Path.Combine(release, "BuildReport.json"), JsonUtility.ToJson(receipt, true));
        if (report.summary.result != BuildResult.Succeeded)
        {
            string[] errors = report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Error || message.type == LogType.Exception)
                .Select(message => message.content).ToArray();
            throw new InvalidOperationException("PDaB Linux build failed: " + string.Join("\n", errors));
        }
        if (!File.Exists(Path.Combine(output, "PDaB.x86_64")) || !Directory.Exists(Path.Combine(output, "PDaB_Data")))
            throw new InvalidOperationException("Unity reported success but the Linux player output is incomplete.");
        return receipt;
    }

    [Serializable]
    public sealed class Receipt
    {
        public string product, unityVersion, platform, scriptingBackend, scene, result;
        public bool development;
        public ulong totalBytes;
        public int errors, warnings;
        public double durationSeconds;
        public string outputDirectory, releaseDirectory, createdUtc;
    }
}
