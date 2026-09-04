using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fits 2.5D ground-contact footprints in the Sweep scene and creates the missing
/// visual bottom wall. The wall is visual-only because WorldBounds2D and the
/// existing EdgeCollider2D already provide a physical bottom boundary.
/// </summary>
public static class SweepSceneCollisionSetup
{
    public const string SweepScenePath = "Assets/barry/Scenes/Sweep.unity";
    public const string BottomWallName = "BottomWallVisual";

    private enum FootprintProfile
    {
        Humanoid,
        WideFurniture,
        Pedestal
    }

    private readonly struct Interval
    {
        public readonly float min;
        public readonly float max;
        public float Width => max - min;
        public float Center => (min + max) * 0.5f;

        public Interval(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }

    [MenuItem("Tools/Sweep Demo/Collision/Set Up Scene Footprints and Bottom Wall")]
    public static void SetupSceneMenu()
    {
        EnsureSweepSceneIsActive();
        int fitted = AutoFitObstacleFootprints();
        GameObject bottomWall = EnsureBottomWallVisual();
        RenameExistingWallVisuals();

        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[SweepSceneCollisionSetup] Updated {fitted} obstacle footprints and " +
                  $"verified {bottomWall.name}.\n{BuildValidationReport(out _)}");
    }

    [MenuItem("Tools/Sweep Demo/Collision/Validate Scene Collision Setup")]
    public static void ValidateSceneMenu()
    {
        EnsureSweepSceneIsActive();
        string report = BuildValidationReport(out bool valid);
        if (valid) Debug.Log(report);
        else Debug.LogError(report);
    }

    public static int AutoFitObstacleFootprints()
    {
        WorldBounds2D[] allBounds = UnityEngine.Object.FindObjectsByType<WorldBounds2D>(
            FindObjectsInactive.Include);
        int fitted = 0;

        foreach (WorldBounds2D worldBounds in allBounds)
        {
            if (worldBounds == null || worldBounds.gameObject.scene != SceneManager.GetActiveScene() ||
                worldBounds.isMainWorldBoundary || worldBounds.boundsType != WorldBounds2D.BoundsType.Obstacle)
            {
                continue;
            }

            SpriteRenderer renderer = worldBounds.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null)
                throw new InvalidOperationException($"{GetPath(worldBounds.transform)} has no SpriteRenderer.");

            FootprintProfile profile = Classify(renderer);
            if (!TryCalculateFootprint(renderer, profile, out Rect rect, out string error))
                throw new InvalidOperationException($"{GetPath(worldBounds.transform)}: {error}");

            Undo.RecordObject(worldBounds, "Auto-fit Sweep obstacle footprint");
            Vector2 position = worldBounds.transform.position;
            worldBounds.centerOffset = Round(rect.center - position);
            worldBounds.size = Round(rect.size);
            EditorUtility.SetDirty(worldBounds);
            fitted++;
        }

        if (fitted != 10)
            throw new InvalidOperationException($"Expected 10 obstacle footprints, found {fitted}.");
        return fitted;
    }

    public static GameObject EnsureBottomWallVisual()
    {
        WorldBounds2D mainBoundary = UnityEngine.Object.FindObjectsByType<WorldBounds2D>(
                FindObjectsInactive.Include)
            .FirstOrDefault(boundary => boundary != null && boundary.isMainWorldBoundary);
        if (mainBoundary == null)
            throw new InvalidOperationException("The main WorldBounds2D was not found.");

        SpriteRenderer[] renderers = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(
            FindObjectsInactive.Include);
        SpriteRenderer top = renderers
            .Where(renderer => renderer != null && renderer.sprite != null &&
                               renderer.gameObject.scene == SceneManager.GetActiveScene() &&
                               AssetDatabase.GetAssetPath(renderer.sprite).EndsWith("/牆壁.png", StringComparison.Ordinal))
            .OrderByDescending(renderer => renderer.transform.position.y)
            .FirstOrDefault();
        if (top == null || top.transform.parent == null)
            throw new InvalidOperationException("The existing top wall SpriteRenderer was not found.");

        Transform parent = top.transform.parent;
        Transform existing = parent.Find(BottomWallName);
        GameObject wall;
        SpriteRenderer target;

        if (existing == null)
        {
            wall = new GameObject(BottomWallName);
            Undo.RegisterCreatedObjectUndo(wall, "Create Sweep bottom wall visual");
            wall.transform.SetParent(parent, false);
            target = wall.AddComponent<SpriteRenderer>();
        }
        else
        {
            wall = existing.gameObject;
            target = wall.GetComponent<SpriteRenderer>();
            if (target == null) target = wall.AddComponent<SpriteRenderer>();
            Undo.RecordObject(wall.transform, "Update Sweep bottom wall visual");
            Undo.RecordObject(target, "Update Sweep bottom wall visual");
        }

        wall.layer = top.gameObject.layer;
        wall.tag = top.gameObject.tag;
        GameObjectUtility.SetStaticEditorFlags(wall, GameObjectUtility.GetStaticEditorFlags(top.gameObject));

        Vector3 local = top.transform.localPosition;
        Vector3 bottomWorld = new Vector3(top.transform.position.x, mainBoundary.Min.y, top.transform.position.z);
        local.y = parent.InverseTransformPoint(bottomWorld).y;
        wall.transform.localPosition = local;
        wall.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        wall.transform.localScale = top.transform.localScale;

        CopyRendererSettings(top, target);
        RemoveUnexpectedColliders(wall);
        EditorUtility.SetDirty(target);
        return wall;
    }

    public static string BuildValidationReport(out bool valid)
    {
        List<string> errors = new List<string>();
        List<string> details = new List<string>();
        Scene active = SceneManager.GetActiveScene();

        WorldBounds2D[] allBounds = UnityEngine.Object.FindObjectsByType<WorldBounds2D>(
            FindObjectsInactive.Include);
        WorldBounds2D main = allBounds.FirstOrDefault(boundary =>
            boundary != null && boundary.gameObject.scene == active && boundary.isMainWorldBoundary);
        WorldBounds2D[] obstacles = allBounds.Where(boundary =>
            boundary != null && boundary.gameObject.scene == active && !boundary.isMainWorldBoundary &&
            boundary.boundsType == WorldBounds2D.BoundsType.Obstacle).ToArray();

        if (obstacles.Length != 10) errors.Add($"Expected 10 obstacle bounds, found {obstacles.Length}.");
        foreach (WorldBounds2D obstacle in obstacles)
        {
            if (obstacle.size.x <= 0f || obstacle.size.y <= 0f || obstacle.size.y > 1.5f)
                errors.Add($"{GetPath(obstacle.transform)} has implausible size {obstacle.size}.");

            SpriteRenderer renderer = obstacle.GetComponent<SpriteRenderer>();
            try
            {
                if (renderer == null || renderer.sprite == null)
                {
                    errors.Add($"{GetPath(obstacle.transform)} has no SpriteRenderer.");
                }
                else if (!TryCalculateFootprint(renderer, Classify(renderer), out Rect expected, out string error))
                {
                    errors.Add($"{GetPath(obstacle.transform)}: {error}");
                }
                else
                {
                    Vector2 expectedCenter = Round(expected.center - (Vector2)obstacle.transform.position);
                    Vector2 expectedSize = Round(expected.size);
                    if (Vector2.Distance(obstacle.centerOffset, expectedCenter) > 0.001f ||
                        Vector2.Distance(obstacle.size, expectedSize) > 0.001f)
                    {
                        errors.Add($"{GetPath(obstacle.transform)} footprint is stale: " +
                                   $"actual center={obstacle.centerOffset} size={obstacle.size}, " +
                                   $"expected center={expectedCenter} size={expectedSize}.");
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add($"{GetPath(obstacle.transform)}: {exception.Message}");
            }

            details.Add($"{GetPath(obstacle.transform)}: rect={FormatRect(obstacle.GetWorldRect())}");
        }

        GameObject wall = GameObject.Find(BottomWallName);
        if (wall == null)
        {
            errors.Add($"{BottomWallName} is missing.");
        }
        else
        {
            SpriteRenderer renderer = wall.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) errors.Add("Bottom wall has no sprite.");
            if (wall.GetComponent<Collider2D>() != null) errors.Add("Bottom wall must remain visual-only.");
            if (main != null && Mathf.Abs(wall.transform.position.y - main.Min.y) > 0.02f)
                errors.Add($"Bottom wall y={wall.transform.position.y:F3} does not match boundary {main.Min.y:F3}.");
            if (Quaternion.Angle(wall.transform.localRotation, Quaternion.Euler(90f, 0f, 0f)) > 0.1f)
                errors.Add("Bottom wall is not mirrored to +90 degrees on X.");

            details.Add($"{BottomWallName}: local={wall.transform.localPosition}, " +
                        $"euler={wall.transform.localEulerAngles}, scale={wall.transform.localScale}");
        }

        valid = errors.Count == 0;
        string report = $"[SweepSceneCollisionSetup] {(valid ? "PASS" : "FAIL")}";
        if (details.Count > 0) report += "\n" + string.Join("\n", details);
        if (errors.Count > 0) report += "\n---\n" + string.Join("\n", errors);
        return report;
    }

    private static bool TryCalculateFootprint(
        SpriteRenderer renderer,
        FootprintProfile profile,
        out Rect rect,
        out string error)
    {
        rect = default;
        error = string.Empty;
        List<List<Vector2>> paths = ReadWorldPaths(renderer);
        if (paths.Count == 0)
        {
            error = "Sprite physics outline is empty.";
            return false;
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        foreach (List<Vector2> path in paths)
        {
            foreach (Vector2 point in path)
            {
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
            }
        }

        float width = maxX - minX;
        float height = maxY - minY;
        if (width <= 0.01f || height <= 0.01f)
        {
            error = "Sprite physics outline is degenerate.";
            return false;
        }

        GetBand(profile, out float bandStart, out float bandEnd);
        float selectedMinX = float.PositiveInfinity;
        float selectedMaxX = float.NegativeInfinity;
        const int samples = 18;

        for (int sample = 0; sample < samples; sample++)
        {
            float fraction = Mathf.Lerp(bandStart, bandEnd, (sample + 0.5f) / samples);
            float y = minY + height * fraction;
            List<Interval> intervals = GetIntervals(paths, y);
            if (intervals.Count == 0) continue;

            if (profile == FootprintProfile.WideFurniture)
            {
                selectedMinX = Mathf.Min(selectedMinX, intervals.Min(interval => interval.min));
                selectedMaxX = Mathf.Max(selectedMaxX, intervals.Max(interval => interval.max));
            }
            else
            {
                Interval chosen = profile == FootprintProfile.Humanoid
                    ? intervals.OrderBy(interval => Mathf.Abs(interval.Center - renderer.transform.position.x))
                        .ThenByDescending(interval => interval.Width).First()
                    : intervals.OrderByDescending(interval => interval.Width)
                        .ThenBy(interval => interval.min).First();
                selectedMinX = Mathf.Min(selectedMinX, chosen.min);
                selectedMaxX = Mathf.Max(selectedMaxX, chosen.max);
            }
        }

        if (float.IsInfinity(selectedMinX) || selectedMaxX - selectedMinX <= 0.02f)
        {
            error = "Could not find a stable ground-contact interval.";
            return false;
        }

        float depth;
        if (profile == FootprintProfile.Humanoid)
        {
            depth = Mathf.Clamp(height * 0.11f, 0.75f, 1.45f);
            float maxHumanoidWidth = Mathf.Min(4.30f, height * 0.45f);
            float selectedWidth = selectedMaxX - selectedMinX;
            if (selectedWidth > maxHumanoidWidth)
            {
                float center = (selectedMinX + selectedMaxX) * 0.5f;
                selectedMinX = center - maxHumanoidWidth * 0.5f;
                selectedMaxX = center + maxHumanoidWidth * 0.5f;
            }
        }
        else if (profile == FootprintProfile.WideFurniture)
        {
            depth = Mathf.Clamp(height * 0.35f, 0.75f, 1.25f);
        }
        else
        {
            depth = Mathf.Clamp(height * 0.13f, 0.65f, 1f);
        }

        rect = Rect.MinMaxRect(
            Round(selectedMinX),
            Round(minY),
            Round(selectedMaxX),
            Round(minY + depth));
        return true;
    }

    private static FootprintProfile Classify(SpriteRenderer renderer)
    {
        for (Transform current = renderer.transform; current != null; current = current.parent)
        {
            if (string.Equals(current.name, "human", StringComparison.OrdinalIgnoreCase))
                return FootprintProfile.Humanoid;
        }

        string path = AssetDatabase.GetAssetPath(renderer.sprite);
        if (path.IndexOf("長桌", StringComparison.Ordinal) >= 0)
            return FootprintProfile.WideFurniture;
        if (path.IndexOf("燈具", StringComparison.Ordinal) >= 0)
            return FootprintProfile.Pedestal;

        throw new InvalidOperationException($"No safe footprint profile for {path}.");
    }

    private static void GetBand(FootprintProfile profile, out float start, out float end)
    {
        switch (profile)
        {
            case FootprintProfile.Humanoid:
                start = 0.02f;
                end = 0.06f;
                break;
            case FootprintProfile.WideFurniture:
                start = 0.03f;
                end = 0.135f;
                break;
            default:
                start = 0.03f;
                end = 0.102f;
                break;
        }
    }

    private static List<List<Vector2>> ReadWorldPaths(SpriteRenderer renderer)
    {
        List<List<Vector2>> paths = new List<List<Vector2>>();
        List<Vector2> shape = new List<Vector2>(256);
        int count = renderer.sprite.GetPhysicsShapeCount();

        for (int shapeIndex = 0; shapeIndex < count; shapeIndex++)
        {
            shape.Clear();
            renderer.sprite.GetPhysicsShape(shapeIndex, shape);
            if (shape.Count < 3) continue;

            List<Vector2> worldPath = new List<Vector2>(shape.Count);
            for (int i = 0; i < shape.Count; i++)
            {
                Vector2 point = shape[i];
                if (renderer.flipX) point.x = -point.x;
                if (renderer.flipY) point.y = -point.y;
                Vector3 world = renderer.transform.TransformPoint(point);
                worldPath.Add(world);
            }
            paths.Add(worldPath);
        }

        return paths;
    }

    private static List<Interval> GetIntervals(IReadOnlyList<List<Vector2>> paths, float y)
    {
        List<float> intersections = new List<float>();
        for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            List<Vector2> path = paths[pathIndex];
            for (int i = 0; i < path.Count; i++)
            {
                Vector2 a = path[i];
                Vector2 b = path[(i + 1) % path.Count];
                if ((a.y > y) == (b.y > y)) continue;
                float t = (y - a.y) / (b.y - a.y);
                intersections.Add(Mathf.LerpUnclamped(a.x, b.x, t));
            }
        }

        intersections.Sort();
        List<Interval> intervals = new List<Interval>();
        for (int i = 0; i + 1 < intersections.Count; i += 2)
        {
            if (intersections[i + 1] - intersections[i] > 0.01f)
                intervals.Add(new Interval(intersections[i], intersections[i + 1]));
        }
        return intervals;
    }

    private static void CopyRendererSettings(SpriteRenderer source, SpriteRenderer target)
    {
        target.sprite = source.sprite;
        target.color = source.color;
        target.sharedMaterial = source.sharedMaterial;
        target.flipX = source.flipX;
        target.flipY = source.flipY;
        target.drawMode = source.drawMode;
        target.size = source.size;
        target.tileMode = source.tileMode;
        target.maskInteraction = source.maskInteraction;
        target.spriteSortPoint = source.spriteSortPoint;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder;
        target.enabled = source.enabled;
    }

    private static void RemoveUnexpectedColliders(GameObject wall)
    {
        Collider2D[] colliders = wall.GetComponents<Collider2D>();
        for (int i = colliders.Length - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(colliders[i]);
    }

    private static void RenameExistingWallVisuals()
    {
        SpriteRenderer[] walls = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(
                FindObjectsInactive.Include)
            .Where(renderer => renderer != null && renderer.sprite != null &&
                               renderer.gameObject.scene == SceneManager.GetActiveScene() &&
                               AssetDatabase.GetAssetPath(renderer.sprite).EndsWith("/牆壁.png", StringComparison.Ordinal) &&
                               renderer.gameObject.name != BottomWallName)
            .ToArray();

        SpriteRenderer top = walls.OrderByDescending(renderer => renderer.transform.position.y).FirstOrDefault();
        if (top != null) Rename(top.gameObject, "TopWallVisual");

        SpriteRenderer[] sides = walls.Where(renderer => renderer != top)
            .OrderBy(renderer => renderer.transform.position.x).ToArray();
        if (sides.Length > 0) Rename(sides[0].gameObject, "LeftWallVisual");
        if (sides.Length > 1) Rename(sides[sides.Length - 1].gameObject, "RightWallVisual");
    }

    private static void Rename(GameObject gameObject, string name)
    {
        if (gameObject.name == name) return;
        Undo.RecordObject(gameObject, "Rename Sweep wall visual");
        gameObject.name = name;
        EditorUtility.SetDirty(gameObject);
    }

    private static void EnsureSweepSceneIsActive()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path != SweepScenePath)
            throw new InvalidOperationException($"Open {SweepScenePath} before running scene collision setup.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Scene collision setup only runs in Edit Mode.");
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }

    private static Vector2 Round(Vector2 value)
    {
        return new Vector2(Round(value.x), Round(value.y));
    }

    private static float Round(float value)
    {
        return Mathf.Round(value * 100f) / 100f;
    }

    private static string FormatRect(Rect rect)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "({0:F2},{1:F2})-({2:F2},{3:F2})",
            rect.xMin, rect.yMin, rect.xMax, rect.yMax);
    }
}
