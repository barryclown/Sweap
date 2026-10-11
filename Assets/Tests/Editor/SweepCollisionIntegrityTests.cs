using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class SweepCollisionIntegrityTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void TrashPrefabs_HaveDeterministicAlphaFittedCollisionVolumes()
    {
        string[] paths = SweepColliderAutoFit.FindTrashPrefabPaths();
        Assert.That(paths.Length, Is.EqualTo(16));

        foreach (string path in paths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            BaseTrash trash = prefab.GetComponent<BaseTrash>();
            PolygonCollider2D polygon = prefab.GetComponent<PolygonCollider2D>();
            Assert.That(trash, Is.Not.Null, path);
            Assert.That(polygon, Is.Not.Null, $"{path} must keep its hit collider on the root.");
            Assert.That(polygon.isTrigger, Is.False, path);
            Assert.That(polygon.pathCount, Is.EqualTo(1), path);
            Assert.That(polygon.GetTotalPointCount(), Is.InRange(3, SweepColliderAutoFit.MaxPolygonPoints), path);
            Assert.That(trash.collisionNodes, Has.Length.InRange(1, SweepColliderAutoFit.MaxCollisionNodes), path);
            Assert.That(trash.collisionCheckRadius, Is.GreaterThan(0f), path);

            Assert.That(
                SweepColliderAutoFit.TryBuildFitGeometry(trash, out var first, out string firstError),
                Is.True,
                $"{path}: {firstError}");
            Assert.That(
                SweepColliderAutoFit.TryBuildFitGeometry(trash, out var second, out string secondError),
                Is.True,
                $"{path}: {secondError}");

            Assert.That(first.radius, Is.EqualTo(second.radius).Within(0.00001f), path);
            Assert.That(first.nodes.Length, Is.EqualTo(second.nodes.Length), path);
            for (int i = 0; i < first.nodes.Length; i++)
            {
                Assert.That(Vector2.Distance(first.nodes[i], second.nodes[i]), Is.LessThan(0.00001f), path);
                Assert.That(Vector2.Distance(trash.collisionNodes[i], first.nodes[i]), Is.LessThan(0.001f), path);
            }

            Assert.That(first.metrics.falseNegative, Is.LessThanOrEqualTo(0.22f), path);
            Assert.That(first.metrics.falsePositive, Is.LessThanOrEqualTo(0.30f), path);
        }

        string report = SweepColliderAutoFit.BuildValidationReport(out bool valid);
        Assert.That(valid, Is.True, report);
    }

    [Test]
    public void SweepScene_HasGroundFootprintsAndStanding2_5DRoom()
    {
        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        string report = SweepSceneCollisionSetup.BuildValidationReport(out bool valid);
        Assert.That(valid, Is.True, report);

        WorldBounds2D[] bounds = UnityEngine.Object.FindObjectsByType<WorldBounds2D>(
            FindObjectsInactive.Include);
        WorldBounds2D[] obstacles = bounds.Where(boundary =>
            boundary != null && !boundary.isMainWorldBoundary &&
            boundary.boundsType == WorldBounds2D.BoundsType.Obstacle).ToArray();

        Assert.That(obstacles, Has.Length.EqualTo(SweepSceneCollisionSetup.ExpectedObstacleCount));
        Assert.That(obstacles.All(obstacle => obstacle.size.y <= 1.5f), Is.True);

        GameObject frame = GameObject.Find(SweepArenaPresentationFix.RoomFrameName);
        Assert.That(frame, Is.Not.Null);
        Assert.That(frame.GetComponent<SweepRoomFrame>(), Is.Not.Null, "Camera limits live on the room frame.");
        Assert.That(frame.transform.childCount, Is.EqualTo(SweepArenaPresentationFix.FramePieceNames.Length));

        // 後牆與左右牆立在地板上、畫在玩法物件下層；前牆是矮牆、畫在玩家上層。
        foreach (string wallName in new[]
                 {
                     SweepSceneCollisionSetup.TopWallName,
                     SweepSceneCollisionSetup.LeftWallName,
                     SweepSceneCollisionSetup.RightWallName,
                     SweepSceneCollisionSetup.BottomWallName
                 })
        {
            Transform wall = frame.transform.Find(wallName);
            Assert.That(wall, Is.Not.Null, wallName);
            SpriteRenderer renderer = wall.GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null, wallName);
            Assert.That(renderer.drawMode, Is.EqualTo(SpriteDrawMode.Tiled), wallName);
            Assert.That(Vector3.Dot(wall.up, Vector3.back), Is.GreaterThan(0.999f), wallName + " must stand up (2.5D).");
            Assert.That(renderer.bounds.size.z, Is.GreaterThan(1f), wallName + " must have visible height.");
            Assert.That(wall.GetComponent<Collider2D>(), Is.Null,
                "WorldBounds2D supplies the physical edge; frame pieces are visual-only.");
        }

        int playerOrder = UnityEngine.Object.FindAnyObjectByType<PlayerController>()
            .GetComponentsInChildren<SpriteRenderer>(true).Max(renderer => renderer.sortingOrder);
        Assert.That(frame.transform.Find(SweepSceneCollisionSetup.TopWallName).GetComponent<SpriteRenderer>().sortingOrder,
            Is.LessThan(0), "Back wall stays behind every gameplay object.");
        Assert.That(frame.transform.Find(SweepSceneCollisionSetup.BottomWallName).GetComponent<SpriteRenderer>().sortingOrder,
            Is.GreaterThan(playerOrder), "Front wall covers the player, so the broom can never be drawn on top of it.");

        // 前牆牆腳在玩法邊界外：牆高在 45 度鏡頭下遮住的地板深度不會碰到玩家能站的位置。
        Rect play = WorldBounds2D.Instance != null ? WorldBounds2D.Instance.GetWorldRect()
            : bounds.First(boundary => boundary.isMainWorldBoundary).GetWorldRect();
        float frontLine = frame.GetComponent<SweepRoomFrame>().FrontWallLineY;
        Assert.That(play.yMin - frontLine, Is.GreaterThanOrEqualTo(SweepArenaPresentationFix.FrontWallHeight),
            "Front wall occlusion must stay outside the playable rectangle.");
    }

    [Test]
    public void ActiveSweepAssets_UseOrganizedGuidPreservingPaths()
    {
        string[] requiredFolders =
        {
            "Assets/barry/Sweep/Animations",
            "Assets/barry/Sweep/Data",
            "Assets/barry/Sweep/Materials",
            "Assets/barry/Sweep/Prefabs",
            "Assets/barry/Sweep/Scripts",
            "Assets/barry/Sweep/Sprites"
        };

        foreach (string folder in requiredFolders)
            Assert.That(AssetDatabase.IsValidFolder(folder), Is.True, folder);

        Assert.That(AssetDatabase.IsValidFolder("Assets/barry/Sweep/Perfab"), Is.False);
        Assert.That(AssetDatabase.IsValidFolder("Assets/barry/Sweep/Spirte"), Is.False);
        Assert.That(AssetDatabase.IsValidFolder("Assets/barry/Sweep/Script"), Is.False);
        Assert.That(AssetDatabase.IsValidFolder("Assets/Resources"), Is.False);

        string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/barry/Sweep/Prefabs" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();
        Assert.That(prefabPaths, Has.Length.EqualTo(17));
        Assert.That(prefabPaths.All(path =>
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null && prefab.name == Path.GetFileNameWithoutExtension(path);
        }), Is.True, "Every active prefab root should match its cleaned filename.");

        string[] enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        CollectionAssert.AreEquivalent(new[]
        {
            "Assets/barry/Scenes/StartMenu.unity",
            "Assets/barry/Scenes/Sweep.unity"
        }, enabledScenes);
        Assert.That(EditorBuildSettings.scenes.All(scene =>
            AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) != null), Is.True);
    }

    [Test]
    public void TrashCollision_WakesSleepingTargetBeforeApplyingChainImpulse()
    {
        GameObject sourceObject = new GameObject("CollisionSource");
        GameObject targetObject = new GameObject("SleepingTarget");
        try
        {
            BaseTrash source = sourceObject.AddComponent<BaseTrash>();
            BaseTrash target = targetObject.AddComponent<BaseTrash>();
            SetPrivateField(source, "currentVelocity", new Vector2(5f, 0f));
            SetPrivateField(target, "currentVelocity", Vector2.zero);
            SetPrivateField(target, "_isSleeping", true);
            SetPrivateField(source, "minCollisionSpeed", 0.1f);
            SetPrivateField(source, "collisionDamping", 0.8f);

            MethodInfo resolve = typeof(BaseTrash).GetMethod(
                "ResolveTrashCollision",
                PrivateInstance);
            Assert.That(resolve, Is.Not.Null);
            resolve.Invoke(source, new object[] { target, Vector2.right });

            Assert.That(GetPrivateField<bool>(target, "_isSleeping"), Is.False);
            Assert.That(target.CurrentVelocity.x, Is.GreaterThan(0.1f));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    [Test]
    public void CompoundBoundarySolver_UsesOneExitDirectionForWholeChain()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject obstacleObject = new GameObject("Obstacle");
        obstacleObject.SetActive(false);
        WorldBounds2D obstacle = obstacleObject.AddComponent<WorldBounds2D>();
        obstacle.isMainWorldBoundary = false;
        obstacle.boundsType = WorldBounds2D.BoundsType.Obstacle;
        obstacle.size = new Vector2(2f, 2f);
        obstacleObject.SetActive(true);
        if (!WorldBounds2D.ActiveBounds.Contains(obstacle))
        {
            // EditMode does not consistently dispatch OnEnable for components
            // created on an inactive GameObject. Register explicitly so this
            // unit test exercises the compound solver, not editor lifecycle.
            WorldBounds2D.ActiveBounds.Add(obstacle);
        }

        GameObject trashObject = new GameObject("CompoundTrash");
        try
        {
            BaseTrash trash = trashObject.AddComponent<BaseTrash>();
            trash.collisionNodes = new[]
            {
                new Vector2(-2f, 0f),
                Vector2.zero,
                new Vector2(2f, 0f)
            };
            trash.collisionCheckRadius = 0.4f;

            MethodInfo handleBoundary = typeof(BaseTrash).GetMethod(
                "HandleBoundaryCheck",
                PrivateInstance);
            Assert.That(handleBoundary, Is.Not.Null);
            object[] arguments = { Vector2.zero };
            handleBoundary.Invoke(trash, arguments);
            Vector2 resolvedPosition = (Vector2)arguments[0];

            Assert.That(resolvedPosition, Is.Not.EqualTo(Vector2.zero));
            foreach (Vector2 node in trash.collisionNodes)
            {
                Assert.That(
                    obstacle.IsOutside(resolvedPosition + node, trash.collisionCheckRadius),
                    Is.False,
                    $"Node {node} remained inside the obstacle at root {resolvedPosition}.");
            }
        }
        finally
        {
            WorldBounds2D.ActiveBounds.Remove(obstacle);
            UnityEngine.Object.DestroyImmediate(trashObject);
            UnityEngine.Object.DestroyImmediate(obstacleObject);
        }
    }

    private static void SetPrivateField<T>(BaseTrash target, string name, T value)
    {
        FieldInfo field = typeof(BaseTrash).GetField(name, PrivateInstance);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }

    private static T GetPrivateField<T>(BaseTrash target, string name)
    {
        FieldInfo field = typeof(BaseTrash).GetField(name, PrivateInstance);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(target);
    }
}
