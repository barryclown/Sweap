using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SweepDemoBuild
{
    private const string DefaultRelativeOutput = "Builds/Windows/DragameDemo.exe";
    private const string DemoHintsSeenKey = "Dragame.Sweep.DemoHintsSeen.v1";

    [MenuItem("Tools/Sweep Demo/Build Windows")]
    public static void BuildWindows()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = GetCommandLineValue("-sweepBuildOutput");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.Combine(projectRoot, DefaultRelativeOutput);
        }
        outputPath = Path.GetFullPath(outputPath);

        string[] scenes = GetEnabledBuildScenes();
        ValidateRequiredScenes(scenes);

        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
            throw new BuildFailedException($"無法解析建置輸出目錄：{outputPath}");

        Directory.CreateDirectory(outputDirectory);
        AssetDatabase.SaveAssets();

        BuildOptions options = BuildOptions.CompressWithLz4HC;
        if (HasCommandLineFlag("-sweepDevelopmentBuild")) options |= BuildOptions.Development;

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = options
        });

        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException(
                $"Sweep Demo Windows 建置失敗：{summary.result}，errors={summary.totalErrors}，warnings={summary.totalWarnings}");
        }

        Debug.Log($"[SweepDemoBuild] BUILD_SUCCEEDED path={outputPath} size={summary.totalSize} duration={summary.totalTime}");
    }

    [MenuItem("Tools/Sweep Demo/Run Preflight")]
    public static void RunPreflight()
    {
        string[] scenes = GetEnabledBuildScenes();
        ValidateRequiredScenes(scenes);

        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/InputSystem_Actions.inputactions") == null)
            throw new BuildFailedException("找不到 InputSystem_Actions.inputactions");
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/barry/Sweep/Spirte/limit/video.mp4") == null)
            throw new BuildFailedException("找不到開場 video.mp4");

        Debug.Log($"[SweepDemoBuild] PREFLIGHT_SUCCEEDED scenes={string.Join(",", scenes)}");
    }

    [MenuItem("Tools/Sweep Demo/Reset Demo Now")]
    public static void ResetDemoNow()
    {
        PlayerPrefs.DeleteKey(DemoHintsSeenKey);
        PlayerPrefs.Save();
        Time.timeScale = 1f;

        if (EditorApplication.isPlaying && SceneManager.GetActiveScene().IsValid())
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        Debug.Log("[SweepDemoBuild] 展演狀態已重置；首次短提示會再次出現。");
    }

    private static string[] GetEnabledBuildScenes()
    {
        return EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }

    private static void ValidateRequiredScenes(string[] scenes)
    {
        if (!scenes.Contains("Assets/barry/Scenes/StartMenu.unity"))
            throw new BuildFailedException("Build Settings 未啟用 StartMenu 場景。");
        if (!scenes.Contains("Assets/barry/Scenes/Sweep.unity"))
            throw new BuildFailedException("Build Settings 未啟用 Sweep 場景。");

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        foreach (string scene in scenes)
        {
            string absoluteScenePath = Path.GetFullPath(Path.Combine(projectRoot, scene));
            if (!File.Exists(absoluteScenePath))
                throw new BuildFailedException($"Build Settings 場景不存在：{scene}");
        }
    }

    private static string GetCommandLineValue(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    private static bool HasCommandLineFlag(string key)
    {
        return Environment.GetCommandLineArgs().Any(
            arg => string.Equals(arg, key, StringComparison.OrdinalIgnoreCase));
    }
}
