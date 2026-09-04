using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click, GUID-preserving cleanup for the actively used Sweep assets.
/// High-risk legacy animation assets are intentionally left untouched.
/// </summary>
public static class SweepProjectOrganizer
{
    private readonly struct Move
    {
        public readonly string source;
        public readonly string destination;

        public Move(string source, string destination)
        {
            this.source = source;
            this.destination = destination;
        }
    }

    private static readonly Move[] FolderMoves =
    {
        new Move("Assets/barry/Sweep/Perfab", "Assets/barry/Sweep/Prefabs"),
        new Move("Assets/barry/Sweep/Spirte", "Assets/barry/Sweep/Sprites"),
        new Move("Assets/barry/Sweep/Script", "Assets/barry/Sweep/Scripts"),
        new Move("Assets/barry/Sweep/ScriptableObject", "Assets/barry/Sweep/Data"),
        new Move("Assets/barry/Sweep/Material", "Assets/barry/Sweep/Materials"),
        new Move("Assets/barry/Sweep/Animation", "Assets/barry/Sweep/Animations"),
        new Move("Assets/barry/Sweep/Animations/broom animation", "Assets/barry/Sweep/Animations/Broom")
    };

    private static readonly Move[] AssetMoves =
    {
        new Move("Assets/barry/Sweep/Prefabs/balckHole.prefab", "Assets/barry/Sweep/Prefabs/BlackHole.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/bananaTrash.prefab", "Assets/barry/Sweep/Prefabs/BananaTrash.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/canTrash.prefab", "Assets/barry/Sweep/Prefabs/CanTrash.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/canTrash2.prefab", "Assets/barry/Sweep/Prefabs/CanTrash2.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/canTrash 3.prefab", "Assets/barry/Sweep/Prefabs/CanTrash3.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/glassTrash.prefab", "Assets/barry/Sweep/Prefabs/GlassTrash.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/glassTrash2.prefab", "Assets/barry/Sweep/Prefabs/GlassTrash2.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/paperTrash.prefab", "Assets/barry/Sweep/Prefabs/PaperTrash.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/paperTrash2.prefab", "Assets/barry/Sweep/Prefabs/PaperTrash2.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/paperTrash3.prefab", "Assets/barry/Sweep/Prefabs/PaperTrash3.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/paperTrash4.prefab", "Assets/barry/Sweep/Prefabs/PaperTrash4.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/paperTrash5.prefab", "Assets/barry/Sweep/Prefabs/PaperTrash5.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/paperTrash6.prefab", "Assets/barry/Sweep/Prefabs/PaperTrash6.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/paperTrash7.prefab", "Assets/barry/Sweep/Prefabs/PaperTrash7.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/potTrash .prefab", "Assets/barry/Sweep/Prefabs/PotTrash.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/sockTrash.prefab", "Assets/barry/Sweep/Prefabs/SockTrash.prefab"),
        new Move("Assets/barry/Sweep/Prefabs/sponTrash .prefab", "Assets/barry/Sweep/Prefabs/SpoonTrash.prefab"),
        new Move("Assets/barry/Sweep/Scripts/itemData.cs", "Assets/barry/Sweep/Scripts/ItemData.cs"),
        new Move("Assets/barry/Sweep/Data/levelSetting.asset", "Assets/barry/Sweep/Data/LevelSetting.asset"),
        new Move("Assets/barry/Sweep/Animations/Broom/sweap.controller", "Assets/barry/Sweep/Animations/Broom/Sweep.controller")
    };

    [MenuItem("Tools/Sweep Demo/Project/Organize Safe Active Assets")]
    public static void OrganizeMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Project organization only runs in Edit Mode.");

        List<string> changes = new List<string>();
        // Keep the AssetDatabase live between moves. This is slower than batching,
        // but paths renamed earlier in the same pass are immediately resolvable and
        // script-folder moves cannot leave the organizer in a half-known state.
        ApplyMoves(FolderMoves, changes);
        ApplyMoves(AssetMoves, changes);
        RenamePrefabRoots(changes);
        RemoveEmptyResourcesFolder(changes);
        RemoveMissingDisabledBuildScenes(changes);
        AssetDatabase.SaveAssets();

        Debug.Log($"[SweepProjectOrganizer] PASS ({changes.Count} changes)\n" +
                  string.Join("\n", changes));
    }

    private static void ApplyMoves(IEnumerable<Move> moves, List<string> changes)
    {
        foreach (Move move in moves)
        {
            string sourceGuid = AssetDatabase.AssetPathToGUID(move.source);
            string destinationGuid = AssetDatabase.AssetPathToGUID(move.destination);
            string sourceCanonical = string.IsNullOrEmpty(sourceGuid)
                ? string.Empty
                : AssetDatabase.GUIDToAssetPath(sourceGuid);
            string destinationCanonical = string.IsNullOrEmpty(destinationGuid)
                ? string.Empty
                : AssetDatabase.GUIDToAssetPath(destinationGuid);

            if (string.Equals(destinationCanonical, move.destination, StringComparison.Ordinal))
                continue;
            if (string.IsNullOrEmpty(sourceCanonical))
                throw new FileNotFoundException($"Source asset is missing: {move.source}");
            if (!string.IsNullOrEmpty(destinationCanonical) && destinationGuid != sourceGuid)
                throw new IOException($"Destination already exists: {move.destination}");

            string error;
            if (string.Equals(move.source, move.destination, StringComparison.OrdinalIgnoreCase))
            {
                string extension = Path.GetExtension(move.destination);
                string withoutExtension = move.destination.Substring(0, move.destination.Length - extension.Length);
                string temporaryPath = withoutExtension + "__casefix__" + extension;
                error = AssetDatabase.MoveAsset(sourceCanonical, temporaryPath);
                if (string.IsNullOrEmpty(error))
                    error = AssetDatabase.MoveAsset(temporaryPath, move.destination);
            }
            else
            {
                error = AssetDatabase.MoveAsset(sourceCanonical, move.destination);
            }

            if (!string.IsNullOrEmpty(error))
                throw new IOException($"Could not move {move.source} -> {move.destination}: {error}");
            changes.Add($"MOVE {move.source} -> {move.destination}");
        }
    }

    private static void RenamePrefabRoots(List<string> changes)
    {
        string folder = "Assets/barry/Sweep/Prefabs";
        string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (string path in paths)
        {
            string expectedName = Path.GetFileNameWithoutExtension(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.name == expectedName) continue;
                root.name = expectedName;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                changes.Add($"RENAME prefab root {path} -> {expectedName}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void RemoveEmptyResourcesFolder(List<string> changes)
    {
        const string path = "Assets/Resources";
        if (!AssetDatabase.IsValidFolder(path)) return;
        bool hasAssets = AssetDatabase.FindAssets(string.Empty, new[] { path }).Length > 0;
        bool hasFolders = AssetDatabase.GetSubFolders(path).Length > 0;
        if (hasAssets || hasFolders) return;

        if (!AssetDatabase.DeleteAsset(path))
            throw new IOException($"Could not delete empty folder {path}.");
        changes.Add($"DELETE empty folder {path}");
    }

    private static void RemoveMissingDisabledBuildScenes(List<string> changes)
    {
        EditorBuildSettingsScene[] before = EditorBuildSettings.scenes;
        EditorBuildSettingsScene[] after = before.Where(scene =>
            scene.enabled || AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) != null).ToArray();
        if (after.Length == before.Length) return;

        foreach (EditorBuildSettingsScene scene in before.Except(after))
            changes.Add($"REMOVE missing disabled Build Settings scene {scene.path}");
        EditorBuildSettings.scenes = after;
    }
}
