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

        CleanPreviousBuildArtifacts(projectRoot, outputPath, outputDirectory);
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

        ValidateNoEditorCompilerAssemblies(outputPath);
        Debug.Log($"[SweepDemoBuild] BUILD_SUCCEEDED path={outputPath} size={summary.totalSize} duration={summary.totalTime}");
    }

    [MenuItem("Tools/Sweep Demo/Run Preflight")]
    public static void RunPreflight()
    {
        string[] scenes = GetEnabledBuildScenes();
        ValidateRequiredScenes(scenes);

        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/InputSystem_Actions.inputactions") == null)
            throw new BuildFailedException("找不到 InputSystem_Actions.inputactions");
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/barry/Sweep/Sprites/limit/video.mp4") == null)
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

    private static void CleanPreviousBuildArtifacts(
        string projectRoot,
        string outputPath,
        string outputDirectory)
    {
        string defaultDirectory = Path.GetFullPath(
            Path.Combine(projectRoot, Path.GetDirectoryName(DefaultRelativeOutput) ?? string.Empty));

        // The default output is a dedicated generated directory, so replacing it
        // is safe and prevents stale managed DLLs from surviving an incremental build.
        if (string.Equals(
                outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                defaultDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
            return;
        }

        // A caller may point -sweepBuildOutput at a shared directory. In that case
        // remove only artifacts whose names belong to this build.
        if (File.Exists(outputPath)) File.Delete(outputPath);

        string dataDirectory = Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(outputPath) + "_Data");
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);

        string burstDirectory = Path.Combine(
            outputDirectory,
            PlayerSettings.productName + "_BurstDebugInformation_DoNotShip");
        if (Directory.Exists(burstDirectory)) Directory.Delete(burstDirectory, true);
    }

    private static void ValidateNoEditorCompilerAssemblies(string outputPath)
    {
        string outputDirectory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        string managedDirectory = Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(outputPath) + "_Data",
            "Managed");
        string[] editorCompilerAssemblies =
        {
            "Microsoft.CodeAnalysis.dll",
            "Microsoft.CodeAnalysis.CSharp.dll",
            "System.Collections.Immutable.dll",
            "System.Reflection.Metadata.dll"
        };

        string[] leaked = editorCompilerAssemblies
            .Where(name => File.Exists(Path.Combine(managedDirectory, name)))
            .ToArray();
        if (leaked.Length > 0)
        {
            throw new BuildFailedException(
                $"Editor-only Roslyn assemblies leaked into the Player build: {string.Join(", ", leaked)}");
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
