using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds deterministic collision volumes for every Sweep trash prefab from the
/// sprite's alpha-derived physics outline. The visual assets are presented on a
/// tilted 2.5D plane, while gameplay happens in logical XY space, so this tool
/// intentionally keeps Z rotation / XY scale and ignores presentation X/Y tilt.
/// </summary>
public static class SweepColliderAutoFit
{
    public const int MaxCollisionNodes = 6;
    public const int MaxPolygonPoints = 16;

    private const string SweepAssetRoot = "Assets/barry/Sweep";
    private const int CoverageResolution = 72;
    private const float MinimumRadius = 0.12f;

    public readonly struct FitMetrics
    {
        public readonly float falseNegative;
        public readonly float falsePositive;

        public FitMetrics(float falseNegative, float falsePositive)
        {
            this.falseNegative = falseNegative;
            this.falsePositive = falsePositive;
        }
    }

    public readonly struct FitGeometry
    {
        public readonly Vector2[] outline;
        public readonly Vector2[] hull;
        public readonly Vector2[] nodes;
        public readonly float radius;
        public readonly FitMetrics metrics;

        public FitGeometry(
            Vector2[] outline,
            Vector2[] hull,
            Vector2[] nodes,
            float radius,
            FitMetrics metrics)
        {
            this.outline = outline;
            this.hull = hull;
            this.nodes = nodes;
            this.radius = radius;
            this.metrics = metrics;
        }
    }

    [MenuItem("Tools/Sweep Demo/Collision/Auto-Fit All Trash Prefabs")]
    public static void AutoFitAllTrashPrefabsMenu()
    {
        AutoFitAllTrashPrefabs(true);
    }

    [MenuItem("Tools/Sweep Demo/Collision/Validate All Trash Prefabs")]
    public static void ValidateAllTrashPrefabsMenu()
    {
        string report = BuildValidationReport(out bool valid);
        if (valid) Debug.Log(report);
        else Debug.LogError(report);

    }

    public static string[] FindTrashPrefabPaths()
    {
        List<string> results = new List<string>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SweepAssetRoot });

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponent<BaseTrash>() != null)
            {
                results.Add(path);
            }
        }

        results.Sort(StringComparer.Ordinal);
        return results.ToArray();
    }

    public static int AutoFitAllTrashPrefabs(bool logResults)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Collision auto-fit only runs in Edit Mode.");

        string[] paths = FindTrashPrefabPaths();
        int updated = 0;
        List<string> lines = new List<string>();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    BaseTrash trash = root.GetComponent<BaseTrash>();
                    if (!TryBuildFitGeometry(trash, out FitGeometry geometry, out string error))
                    {
                        throw new InvalidOperationException($"{path}: {error}");
                    }

                    ApplyCircleChain(trash, geometry.nodes, geometry.radius);
                    ApplyPolygonCollider(root, trash, geometry.hull);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    updated++;

                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: nodes={1}, radius={2:F3}, missed={3:P1}, excess={4:P1}",
                        System.IO.Path.GetFileNameWithoutExtension(path),
                        geometry.nodes.Length,
                        geometry.radius,
                        geometry.metrics.falseNegative,
                        geometry.metrics.falsePositive));
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (logResults)
        {
            Debug.Log($"[SweepColliderAutoFit] Updated {updated}/{paths.Length} prefabs.\n" +
                      string.Join("\n", lines));
        }

        return updated;
    }

    public static string BuildValidationReport(out bool valid)
    {
        string[] paths = FindTrashPrefabPaths();
        List<string> errors = new List<string>();
        List<string> details = new List<string>();

        if (paths.Length == 0)
        {
            valid = false;
            return "[SweepColliderAutoFit] No BaseTrash prefabs were found.";
        }

        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                BaseTrash trash = root.GetComponent<BaseTrash>();
                PolygonCollider2D polygon = root.GetComponent<PolygonCollider2D>();

                if (!TryBuildFitGeometry(trash, out FitGeometry expected, out string error))
                {
                    errors.Add($"{path}: {error}");
                    continue;
                }

                if (trash.collisionNodes == null || trash.collisionNodes.Length == 0 ||
                    trash.collisionNodes.Length > MaxCollisionNodes)
                {
                    errors.Add($"{path}: invalid collision node count.");
                }

                if (trash.collisionCheckRadius <= 0f)
                    errors.Add($"{path}: collision radius must be positive.");

                if (polygon == null || polygon.pathCount != 1 ||
                    polygon.GetTotalPointCount() < 3 || polygon.GetTotalPointCount() > MaxPolygonPoints)
                {
                    errors.Add($"{path}: expected one root PolygonCollider2D with 3-{MaxPolygonPoints} points.");
                }
                else
                {
                    Vector2[] expectedColliderPath = new Vector2[expected.hull.Length];
                    for (int pointIndex = 0; pointIndex < expected.hull.Length; pointIndex++)
                    {
                        expectedColliderPath[pointIndex] =
                            LogicalWorldOffsetToColliderLocal(trash.transform, expected.hull[pointIndex]);
                    }

                    float polygonDelta = MaxPointDelta(polygon.GetPath(0), expectedColliderPath);
                    if (polygonDelta > 0.001f)
                        errors.Add($"{path}: serialized polygon is stale (max delta {polygonDelta:F4}).");
                }

                float nodeDelta = MaxPointDelta(trash.collisionNodes, expected.nodes);
                if (nodeDelta > 0.001f || Mathf.Abs(trash.collisionCheckRadius - expected.radius) > 0.001f)
                {
                    errors.Add($"{path}: serialized circle chain is stale (max delta {nodeDelta:F4}).");
                }

                // A chain of equal circles is deliberately a forgiving approximation,
                // but it should neither miss nor add a large part of the alpha hull.
                if (expected.metrics.falseNegative > 0.22f)
                    errors.Add($"{path}: circle chain misses {expected.metrics.falseNegative:P1} of hull area.");
                if (expected.metrics.falsePositive > 0.30f)
                    errors.Add($"{path}: circle chain adds {expected.metrics.falsePositive:P1} outside hull.");

                details.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1} nodes @ {2:F3}, missed {3:P1}, excess {4:P1}, polygon {5} pts",
                    System.IO.Path.GetFileNameWithoutExtension(path),
                    expected.nodes.Length,
                    expected.radius,
                    expected.metrics.falseNegative,
                    expected.metrics.falsePositive,
                    polygon != null ? polygon.GetTotalPointCount() : 0));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        valid = errors.Count == 0;
        string header = $"[SweepColliderAutoFit] Validated {paths.Length} prefabs: " +
                        (valid ? "PASS" : $"FAIL ({errors.Count} issues)");
        string report = header + "\n" + string.Join("\n", details);
        if (errors.Count > 0) report += "\n---\n" + string.Join("\n", errors);
        return report;
    }

    public static bool TryBuildFitGeometry(
        BaseTrash trash,
        out FitGeometry geometry,
        out string error)
    {
        geometry = default;
        error = string.Empty;

        if (trash == null)
        {
            error = "BaseTrash is null.";
            return false;
        }

        SpriteRenderer renderer = trash.GetComponentsInChildren<SpriteRenderer>(true)
            .FirstOrDefault(candidate => candidate != null && candidate.sprite != null);
        if (renderer == null)
        {
            error = "No SpriteRenderer with a sprite was found.";
            return false;
        }

        if (renderer.drawMode != SpriteDrawMode.Simple)
        {
            error = $"SpriteRenderer draw mode {renderer.drawMode} is not supported.";
            return false;
        }

        List<Vector2> outline = ReadLogicalOutline(trash.transform, renderer);
        if (outline.Count < 3)
        {
            error = "Sprite physics outline has fewer than three points.";
            return false;
        }

        List<Vector2> hull = BuildConvexHull(outline);
        if (hull.Count < 3)
        {
            error = "Could not construct a convex alpha hull.";
            return false;
        }

        Vector2[] reducedHull = ReduceConvexHull(hull, MaxPolygonPoints).ToArray();
        Vector2[] nodes = FitCircleChain(hull, out float radius, out FitMetrics metrics);

        geometry = new FitGeometry(
            outline.ToArray(),
            reducedHull,
            nodes,
            radius,
            metrics);
        return true;
    }

    private static List<Vector2> ReadLogicalOutline(Transform root, SpriteRenderer renderer)
    {
        List<Vector2> result = new List<Vector2>(256);
        List<Vector2> shape = new List<Vector2>(256);
        Sprite sprite = renderer.sprite;
        int shapeCount = sprite.GetPhysicsShapeCount();

        for (int shapeIndex = 0; shapeIndex < shapeCount; shapeIndex++)
        {
            shape.Clear();
            sprite.GetPhysicsShape(shapeIndex, shape);
            for (int i = 0; i < shape.Count; i++)
            {
                Vector2 spritePoint = ApplyRendererFlip(renderer, shape[i]);
                Vector2 rootLocal = TransformPointLogical2D(renderer.transform, root, spritePoint);
                result.Add(ApplyRootLogicalTransform(root, rootLocal));
            }
        }

        // Every current Sweep sprite supplies a fallback physics shape. Keep a
        // deterministic fallback so a newly imported Tight sprite is still usable.
        if (result.Count < 3 && sprite.vertices != null)
        {
            Vector2[] vertices = sprite.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector2 spritePoint = ApplyRendererFlip(renderer, vertices[i]);
                Vector2 rootLocal = TransformPointLogical2D(renderer.transform, root, spritePoint);
                result.Add(ApplyRootLogicalTransform(root, rootLocal));
            }
        }

        return result;
    }

    private static Vector2 ApplyRendererFlip(SpriteRenderer renderer, Vector2 point)
    {
        if (renderer.flipX) point.x = -point.x;
        if (renderer.flipY) point.y = -point.y;
        return point;
    }

    private static Vector2 TransformPointLogical2D(Transform source, Transform root, Vector2 point)
    {
        Transform current = source;
        Vector2 value = point;

        while (current != root)
        {
            if (current == null)
                throw new InvalidOperationException("SpriteRenderer is not a child of the BaseTrash root.");

            Vector3 scale3 = current.localScale;
            value = new Vector2(value.x * scale3.x, value.y * scale3.y);
            value = Rotate(value, current.localEulerAngles.z);
            value += (Vector2)current.localPosition;
            current = current.parent;
        }

        return value;
    }

    private static Vector2 ApplyRootLogicalTransform(Transform root, Vector2 point)
    {
        Vector3 scale3 = root.localScale;
        Vector2 scaled = new Vector2(point.x * scale3.x, point.y * scale3.y);
        return Rotate(scaled, root.localEulerAngles.z);
    }

    private static Vector2 Rotate(Vector2 point, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
    }

    public static List<Vector2> BuildConvexHull(IReadOnlyList<Vector2> points)
    {
        List<Vector2> sorted = points
            .Distinct(new ApproximateVectorComparer())
            .OrderBy(point => point.x)
            .ThenBy(point => point.y)
            .ToList();

        if (sorted.Count <= 2) return sorted;

        List<Vector2> lower = new List<Vector2>();
        for (int i = 0; i < sorted.Count; i++)
        {
            while (lower.Count >= 2 && Cross(
                       lower[lower.Count - 1] - lower[lower.Count - 2],
                       sorted[i] - lower[lower.Count - 1]) <= 0f)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(sorted[i]);
        }

        List<Vector2> upper = new List<Vector2>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            while (upper.Count >= 2 && Cross(
                       upper[upper.Count - 1] - upper[upper.Count - 2],
                       sorted[i] - upper[upper.Count - 1]) <= 0f)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(sorted[i]);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    public static List<Vector2> ReduceConvexHull(IReadOnlyList<Vector2> source, int maxPoints)
    {
        List<Vector2> hull = new List<Vector2>(source);
        while (hull.Count > Mathf.Max(3, maxPoints))
        {
            int removeIndex = -1;
            float smallestArea = float.PositiveInfinity;

            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 previous = hull[(i - 1 + hull.Count) % hull.Count];
                Vector2 current = hull[i];
                Vector2 next = hull[(i + 1) % hull.Count];
                float area = Mathf.Abs(Cross(current - previous, next - current));
                if (area < smallestArea)
                {
                    smallestArea = area;
                    removeIndex = i;
                }
            }

            hull.RemoveAt(removeIndex);
        }

        return hull;
    }

    public static Vector2[] FitCircleChain(
        IReadOnlyList<Vector2> hull,
        out float bestRadius,
        out FitMetrics bestMetrics)
    {
        CalculatePrincipalAxes(hull, out Vector2 major, out Vector2 minor);
        ProjectRange(hull, major, out float majorMin, out float majorMax);
        ProjectRange(hull, minor, out float minorMin, out float minorMax);

        float majorSpan = Mathf.Max(majorMax - majorMin, 0.001f);
        float minorSpan = Mathf.Max(minorMax - minorMin, 0.001f);
        float bestScore = float.PositiveInfinity;
        Vector2[] bestNodes = { Average(hull) };
        bestRadius = Mathf.Max(MinimumRadius, minorSpan * 0.45f);
        bestMetrics = EvaluateCoverage(hull, bestNodes, bestRadius);

        for (int radiusStep = 0; radiusStep <= 12; radiusStep++)
        {
            float radiusFactor = Mathf.Lerp(0.32f, 0.60f, radiusStep / 12f);
            float radius = Mathf.Max(MinimumRadius, minorSpan * radiusFactor);

            for (int nodeCount = 1; nodeCount <= MaxCollisionNodes; nodeCount++)
            {
                Vector2[] nodes = BuildChainCandidate(
                    hull, major, minor,
                    majorMin, majorMax,
                    minorMin, minorMax,
                    radius, nodeCount);

                FitMetrics metrics = EvaluateCoverage(hull, nodes, radius);
                float score = metrics.falseNegative * 1.35f + metrics.falsePositive;
                score += Mathf.Max(0f, metrics.falseNegative - 0.18f) * 4f;
                score += Mathf.Max(0f, metrics.falsePositive - 0.25f) * 3f;
                score += nodes.Length * 0.004f;

                // Discourage needlessly large circles on long, narrow objects.
                float aspect = majorSpan / minorSpan;
                if (aspect > 2f && radiusFactor > 0.52f)
                    score += (radiusFactor - 0.52f) * 0.4f;

                if (score < bestScore - 0.00001f)
                {
                    bestScore = score;
                    bestNodes = nodes;
                    bestRadius = radius;
                    bestMetrics = metrics;
                }
            }
        }

        bestRadius = Round(bestRadius);
        for (int i = 0; i < bestNodes.Length; i++)
            bestNodes[i] = new Vector2(Round(bestNodes[i].x), Round(bestNodes[i].y));

        bestMetrics = EvaluateCoverage(hull, bestNodes, bestRadius);
        return bestNodes;
    }

    private static Vector2[] BuildChainCandidate(
        IReadOnlyList<Vector2> hull,
        Vector2 major,
        Vector2 minor,
        float majorMin,
        float majorMax,
        float minorMin,
        float minorMax,
        float radius,
        int requestedNodeCount)
    {
        float centerMin = majorMin + Mathf.Min(radius * 0.82f, (majorMax - majorMin) * 0.25f);
        float centerMax = majorMax - Mathf.Min(radius * 0.82f, (majorMax - majorMin) * 0.25f);
        if (centerMax <= centerMin + 0.001f) requestedNodeCount = 1;

        Vector2[] nodes = new Vector2[requestedNodeCount];
        float fallbackMinor = (minorMin + minorMax) * 0.5f;

        for (int i = 0; i < requestedNodeCount; i++)
        {
            float t = requestedNodeCount == 1
                ? (majorMin + majorMax) * 0.5f
                : Mathf.Lerp(centerMin, centerMax, i / (requestedNodeCount - 1f));
            float crossSectionCenter = FindCrossSectionCenter(hull, major, minor, t, fallbackMinor);
            nodes[i] = major * t + minor * crossSectionCenter;
        }

        return nodes;
    }

    private static float FindCrossSectionCenter(
        IReadOnlyList<Vector2> hull,
        Vector2 major,
        Vector2 minor,
        float targetMajor,
        float fallback)
    {
        float low = float.PositiveInfinity;
        float high = float.NegativeInfinity;

        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 a = hull[i];
            Vector2 b = hull[(i + 1) % hull.Count];
            float ta = Vector2.Dot(a, major);
            float tb = Vector2.Dot(b, major);

            if (targetMajor < Mathf.Min(ta, tb) - 0.0001f ||
                targetMajor > Mathf.Max(ta, tb) + 0.0001f ||
                Mathf.Abs(tb - ta) < 0.00001f)
            {
                continue;
            }

            float interpolation = (targetMajor - ta) / (tb - ta);
            float value = Vector2.Dot(Vector2.LerpUnclamped(a, b, interpolation), minor);
            low = Mathf.Min(low, value);
            high = Mathf.Max(high, value);
        }

        return float.IsInfinity(low) ? fallback : (low + high) * 0.5f;
    }

    public static FitMetrics EvaluateCoverage(
        IReadOnlyList<Vector2> hull,
        IReadOnlyList<Vector2> nodes,
        float radius)
    {
        Bounds2D bounds = GetBounds(hull);
        for (int i = 0; i < nodes.Count; i++)
        {
            bounds.Encapsulate(nodes[i] - Vector2.one * radius);
            bounds.Encapsulate(nodes[i] + Vector2.one * radius);
        }

        int insideHull = 0;
        int missedHull = 0;
        int insideCircles = 0;
        int excessCircles = 0;
        float radiusSquared = radius * radius;

        for (int y = 0; y < CoverageResolution; y++)
        {
            float py = Mathf.Lerp(bounds.min.y, bounds.max.y, (y + 0.5f) / CoverageResolution);
            for (int x = 0; x < CoverageResolution; x++)
            {
                float px = Mathf.Lerp(bounds.min.x, bounds.max.x, (x + 0.5f) / CoverageResolution);
                Vector2 point = new Vector2(px, py);
                bool inHull = IsInsideConvexPolygon(hull, point);
                bool inCircles = false;

                for (int i = 0; i < nodes.Count; i++)
                {
                    if ((point - nodes[i]).sqrMagnitude <= radiusSquared)
                    {
                        inCircles = true;
                        break;
                    }
                }

                if (inHull)
                {
                    insideHull++;
                    if (!inCircles) missedHull++;
                }
                if (inCircles)
                {
                    insideCircles++;
                    if (!inHull) excessCircles++;
                }
            }
        }

        return new FitMetrics(
            insideHull > 0 ? (float)missedHull / insideHull : 1f,
            insideCircles > 0 ? (float)excessCircles / insideCircles : 1f);
    }

    private static bool IsInsideConvexPolygon(IReadOnlyList<Vector2> polygon, Vector2 point)
    {
        bool hasPositive = false;
        bool hasNegative = false;

        for (int i = 0; i < polygon.Count; i++)
        {
            float cross = Cross(polygon[(i + 1) % polygon.Count] - polygon[i], point - polygon[i]);
            if (cross > 0.00001f) hasPositive = true;
            else if (cross < -0.00001f) hasNegative = true;
            if (hasPositive && hasNegative) return false;
        }

        return true;
    }

    private static void CalculatePrincipalAxes(
        IReadOnlyList<Vector2> points,
        out Vector2 major,
        out Vector2 minor)
    {
        Vector2 center = Average(points);
        float xx = 0f;
        float xy = 0f;
        float yy = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 delta = points[i] - center;
            xx += delta.x * delta.x;
            xy += delta.x * delta.y;
            yy += delta.y * delta.y;
        }

        float angle = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
        major = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        minor = new Vector2(-major.y, major.x);

        ProjectRange(points, major, out float majorMin, out float majorMax);
        ProjectRange(points, minor, out float minorMin, out float minorMax);
        if (minorMax - minorMin > majorMax - majorMin)
        {
            Vector2 swap = major;
            major = minor;
            minor = -swap;
        }

        if (major.x < -0.0001f || (Mathf.Abs(major.x) <= 0.0001f && major.y < 0f))
        {
            major = -major;
            minor = -minor;
        }
    }

    private static void ProjectRange(
        IReadOnlyList<Vector2> points,
        Vector2 axis,
        out float min,
        out float max)
    {
        min = float.PositiveInfinity;
        max = float.NegativeInfinity;
        for (int i = 0; i < points.Count; i++)
        {
            float projection = Vector2.Dot(points[i], axis);
            min = Mathf.Min(min, projection);
            max = Mathf.Max(max, projection);
        }
    }

    private static Vector2 Average(IReadOnlyList<Vector2> points)
    {
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Count; i++) sum += points[i];
        return sum / Mathf.Max(1, points.Count);
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float Round(float value)
    {
        return Mathf.Round(value * 10000f) / 10000f;
    }

    private static float MaxPointDelta(IReadOnlyList<Vector2> actual, IReadOnlyList<Vector2> expected)
    {
        if (actual == null || expected == null || actual.Count != expected.Count)
            return float.PositiveInfinity;

        float max = 0f;
        for (int i = 0; i < actual.Count; i++)
            max = Mathf.Max(max, Vector2.Distance(actual[i], expected[i]));
        return max;
    }

    private static Bounds2D GetBounds(IReadOnlyList<Vector2> points)
    {
        Bounds2D bounds = new Bounds2D(points[0]);
        for (int i = 1; i < points.Count; i++) bounds.Encapsulate(points[i]);
        return bounds;
    }

    private static void ApplyCircleChain(BaseTrash trash, Vector2[] nodes, float radius)
    {
        SerializedObject serialized = new SerializedObject(trash);
        SerializedProperty nodesProperty = serialized.FindProperty("collisionNodes");
        SerializedProperty radiusProperty = serialized.FindProperty("collisionCheckRadius");

        nodesProperty.arraySize = nodes.Length;
        for (int i = 0; i < nodes.Length; i++)
            nodesProperty.GetArrayElementAtIndex(i).vector2Value = nodes[i];
        radiusProperty.floatValue = radius;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(trash);
    }

    private static void ApplyPolygonCollider(GameObject root, BaseTrash trash, IReadOnlyList<Vector2> logicalHull)
    {
        Collider2D[] existing = root.GetComponentsInChildren<Collider2D>(true);
        PhysicsMaterial2D material = existing.FirstOrDefault(collider =>
            collider != null && !collider.isTrigger)?.sharedMaterial;
        PolygonCollider2D polygon = root.GetComponent<PolygonCollider2D>();
        for (int i = existing.Length - 1; i >= 0; i--)
        {
            // Functional triggers are gameplay rules, not physical silhouette data.
            // Keep them when future prefabs add pickup or proximity volumes.
            if (existing[i] != polygon && !existing[i].isTrigger)
                UnityEngine.Object.DestroyImmediate(existing[i], true);
        }

        if (polygon == null) polygon = root.AddComponent<PolygonCollider2D>();
        polygon.isTrigger = false;
        polygon.sharedMaterial = material;
        polygon.pathCount = 1;

        Vector2[] colliderPath = new Vector2[logicalHull.Count];
        for (int i = 0; i < logicalHull.Count; i++)
            colliderPath[i] = LogicalWorldOffsetToColliderLocal(trash.transform, logicalHull[i]);
        polygon.SetPath(0, colliderPath);
        EditorUtility.SetDirty(polygon);
    }

    private static Vector2 LogicalWorldOffsetToColliderLocal(Transform root, Vector2 logicalOffset)
    {
        Vector3 origin = root.TransformPoint(Vector3.zero);
        Vector2 xAxis = root.TransformPoint(Vector3.right) - origin;
        Vector2 yAxis = root.TransformPoint(Vector3.up) - origin;
        float determinant = xAxis.x * yAxis.y - yAxis.x * xAxis.y;

        if (Mathf.Abs(determinant) < 0.00001f)
            throw new InvalidOperationException($"{root.name} has a degenerate XY transform.");

        return new Vector2(
            (logicalOffset.x * yAxis.y - yAxis.x * logicalOffset.y) / determinant,
            (xAxis.x * logicalOffset.y - logicalOffset.x * xAxis.y) / determinant);
    }

    private sealed class ApproximateVectorComparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b)
        {
            return (a - b).sqrMagnitude < 0.00000001f;
        }

        public int GetHashCode(Vector2 value)
        {
            int x = Mathf.RoundToInt(value.x * 10000f);
            int y = Mathf.RoundToInt(value.y * 10000f);
            unchecked { return (x * 397) ^ y; }
        }
    }

    private struct Bounds2D
    {
        public Vector2 min;
        public Vector2 max;

        public Bounds2D(Vector2 point)
        {
            min = point;
            max = point;
        }

        public void Encapsulate(Vector2 point)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
    }
}
