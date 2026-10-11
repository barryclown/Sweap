using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class SweepPresentationOptimizationTests
{
    [Test]
    public void CompletePresentationOptimization_IsAppliedToAssetsAndScene()
    {
        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        string report = SweepPresentationOptimizer.BuildValidationReport(out bool valid);
        Assert.That(valid, Is.True, report);
    }

    [Test]
    public void GrowthScaling_KeepsPlayerBodyAndBlackHoleAbilitiesPredictable()
    {
        Assert.That(PlayerController.CalculateGrowthMultiplier(2.7f, 1f), Is.EqualTo(2.7f).Within(0.0001f));
        Assert.That(PlayerController.CalculateGrowthMultiplier(2.7f, 0.5f), Is.EqualTo(1.85f).Within(0.0001f));

        float maximumSuckRadius = PetAI.CalculateScaledAbility(
            SweepPresentationOptimizer.BlackHoleSuckRadius,
            2.7f,
            SweepPresentationOptimizer.BlackHoleRadiusGrowthWeight);
        float maximumSuckForce = PetAI.CalculateScaledAbility(
            SweepPresentationOptimizer.BlackHoleSuckForce,
            2.7f,
            SweepPresentationOptimizer.BlackHoleForceGrowthWeight);

        Assert.That(maximumSuckRadius, Is.EqualTo(7.8052f).Within(0.001f));
        Assert.That(maximumSuckForce, Is.EqualTo(21.84f).Within(0.001f));
        Assert.That(maximumSuckRadius, Is.LessThan(
            SweepPresentationOptimizer.BlackHoleSuckRadius * 2.7f),
            "Range should grow with the character without exploding linearly at maximum level.");
    }

    [Test]
    public void RoomCamera_ShowsWallsAndKeepsPlayerVisibleAcrossGrowthAndWideScreens()
    {
        GameObject cameraObject = new GameObject("Sweep room camera test", typeof(Camera));
        GameObject frameObject = new GameObject("Sweep room frame test");
        try
        {
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            camera.transform.rotation = Quaternion.Euler(-45f, 0f, 0f);

            Rect room = new Rect(
                -SweepArenaPresentationFix.ArenaSize.x * 0.5f,
                -SweepArenaPresentationFix.ArenaSize.y * 0.5f,
                SweepArenaPresentationFix.ArenaSize.x,
                SweepArenaPresentationFix.ArenaSize.y);
            SweepRoomFrame frame = frameObject.AddComponent<SweepRoomFrame>();
            SerializedObject data = new SerializedObject(frame);
            data.FindProperty("wallHeight").floatValue = SweepArenaPresentationFix.WallHeight;
            data.FindProperty("frontWallHeight").floatValue = SweepArenaPresentationFix.FrontWallHeight;
            data.FindProperty("frontWallLineY").floatValue = SweepArenaPresentationFix.GetFrontWallLineY(room);
            data.FindProperty("capThickness").floatValue = SweepArenaPresentationFix.CapThickness;
            data.ApplyModifiedPropertiesWithoutUndo();
            SweepRoomFrame.CameraLimits limits = frame.GetCameraLimits(room);
            float frontOuter = limits.FrontWallLineY - limits.CapThickness;
            float outerPad = limits.CapThickness + limits.OutsideMargin + 0.01f;

            const float inset = 0.7f;
            float[] growthLevels = { 1f, 1.4f, 1.8f, 2.2f, 2.7f };
            float[] aspects = { 16f / 9f, SweepSceneCollisionSetup.ValidationAspect };
            foreach (float aspect in aspects)
            {
                camera.aspect = aspect;
                foreach (float growth in growthLevels)
                {
                    for (int ix = -1; ix <= 1; ix++)
                    for (int iy = -1; iy <= 1; iy++)
                    {
                        Vector3 player = new Vector3(
                            ix < 0 ? room.xMin + inset : ix > 0 ? room.xMax - inset : room.center.x,
                            iy < 0 ? room.yMin + inset : iy > 0 ? room.yMax - inset : room.center.y,
                            0f);
                        Vector3 desired = new Vector3(player.x, player.y - 7f * growth, -7.5f * growth);
                        camera.transform.position = SweepRoomFrame.ClampCamera(camera, desired, player, limits);
                        string label = $"aspect={aspect:F2} growth={growth} anchor=({ix},{iy})";

                        // 玩家永遠留在安全視窗內（上一版在最大成長靠牆時會被推出畫面）。
                        Vector3 viewport = camera.WorldToViewportPoint(player);
                        Assert.That(viewport.x, Is.InRange(limits.PlayerSafeMin.x - 0.01f, limits.PlayerSafeMax.x + 0.01f), label);
                        Assert.That(viewport.y, Is.InRange(limits.PlayerSafeMin.y - 0.01f, limits.PlayerSafeMax.y + 0.01f), label);

                        // 靠牆時那面牆一定看得到（上一版鏡頭被夾在場內，四面牆都拍不到）。
                        if (ix < 0) Assert.That(RayHitsSideWall(camera, 0f, room.xMin, frontOuter, room.yMax, limits.WallHeight), Is.True, "left wall " + label);
                        if (ix > 0) Assert.That(RayHitsSideWall(camera, 1f, room.xMax, frontOuter, room.yMax, limits.WallHeight), Is.True, "right wall " + label);
                        if (iy > 0) Assert.That(RayCrossesWallBelowTop(camera, 0.55f, 1f, room.yMax, limits.WallHeight), Is.True, "back wall " + label);
                        if (iy < 0) Assert.That(RayCrossesWallBelowTop(camera, 0f, 0.45f, limits.FrontWallLineY, limits.FrontWallHeight), Is.True, "front wall " + label);

                        // 一般成長範圍內幾乎不露出房間外：初始大小嚴格檢查；1.4 倍在 21:9 角落時
                        // 「玩家留在畫面內」優先，允許多露 0.5 單位黑邊。
                        if (growth > 1.4f) continue;
                        float tolerance = growth <= 1f ? 0f : 0.5f;
                        for (int corner = 0; corner < 4; corner++)
                        {
                            float vx = corner % 2, vy = corner / 2;
                            if (SweepRoomFrame.TryHitPlane(camera, vx, vy, -limits.WallHeight, out Vector3 top))
                            {
                                Assert.That(top.x, Is.InRange(room.xMin - outerPad - tolerance, room.xMax + outerPad + tolerance), "outside x " + label);
                                Assert.That(top.y, Is.LessThanOrEqualTo(room.yMax + outerPad + tolerance), "outside top " + label);
                            }
                            if (vy < 0.5f && SweepRoomFrame.TryHitPlane(camera, vx, vy, -limits.FrontWallHeight, out Vector3 front))
                                Assert.That(front.y, Is.GreaterThanOrEqualTo(frontOuter - limits.OutsideMargin - 0.01f - tolerance), "outside front " + label);
                        }
                    }
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(frameObject);
            Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void Standing25D_FurnitureTrashBlackHoleAndBroomAreConfigured()
    {
        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        string report = Sweep25DVisualSetup.BuildValidationReport(out bool valid);
        Assert.That(valid, Is.True, report);
    }

    [Test]
    public void Depth25D_NorthIsBehindSouthAndStaysBetweenWalls()
    {
        int south = Depth25D.OrderFor(-30f);
        int middle = Depth25D.OrderFor(0f);
        int north = Depth25D.OrderFor(28f);
        Assert.That(south, Is.GreaterThan(middle));
        Assert.That(middle, Is.GreaterThan(north));
        Assert.That(north, Is.GreaterThan(SweepArenaPresentationFix.CapSortingOrder), "Standing objects draw over the back wall.");
        Assert.That(south, Is.LessThan(SweepArenaPresentationFix.FrontWallSortingOrder), "The front wall still covers everything.");
        Assert.That(Depth25D.FloorDecalOrder, Is.InRange(SweepArenaPresentationFix.FloorSortingOrder + 1, SweepArenaPresentationFix.WallSortingOrder - 1));
        Assert.That(Depth25D.EffectOrder, Is.InRange(Depth25D.FrontMostStandingOrder + 1, SweepArenaPresentationFix.FrontWallSortingOrder - 1));
    }

    [Test]
    public void StickyCluster_SpreadsCapturedTrashAroundTheBroomHead()
    {
        const float sweepRadius = 1f;
        const int count = 12;
        Vector2[] slots = new Vector2[count];
        for (int i = 0; i < count; i++)
            slots[i] = SkillManager.GetClusterOffset(i, sweepRadius, 0.32f, 0.24f, 40f);

        float minDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        for (int j = i + 1; j < count; j++)
            minDistance = Mathf.Min(minDistance, Vector2.Distance(slots[i], slots[j]));

        Assert.That(minDistance, Is.GreaterThan(0.2f * sweepRadius), "Captured trash must not collapse onto one point.");
        Assert.That(slots.Max(slot => slot.magnitude), Is.LessThan(1.3f * sweepRadius), "The clump stays within the broom's reach.");
    }

    [Test]
    public void ResultScreen_HasFourGradesWithLinesAndRetryIcon()
    {
        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        string report = SweepResultScreenSetup.BuildValidationReport(out bool valid);
        Assert.That(valid, Is.True, report);
    }

    [Test]
    public void ResultScreen_PicksTheHighestReachedGrade()
    {
        UIManager.GradeThreshold[] grades = SweepResultScreenSetup.Tiers
            .Select(tier => new UIManager.GradeThreshold { minScore = tier.MinScore, line = tier.Line })
            .ToArray();
        Assert.That(UIManager.SelectGradeIndex(-5, grades), Is.EqualTo(-1));
        Assert.That(UIManager.SelectGradeIndex(0, grades), Is.EqualTo(0), "Idle rounds still get C.");
        Assert.That(UIManager.SelectGradeIndex(22, grades), Is.EqualTo(0), "Idle smoke score ~22 stays at C.");
        Assert.That(UIManager.SelectGradeIndex(64, grades), Is.EqualTo(1), "barry's casual round (64) is B.");
        Assert.That(UIManager.SelectGradeIndex(109, grades), Is.EqualTo(1));
        Assert.That(UIManager.SelectGradeIndex(110, grades), Is.EqualTo(2));
        Assert.That(UIManager.SelectGradeIndex(179, grades), Is.EqualTo(2));
        Assert.That(UIManager.SelectGradeIndex(180, grades), Is.EqualTo(3));
        Assert.That(UIManager.SelectGradeIndex(999, grades), Is.EqualTo(3));
    }

    [Test]
    public void RuntimeMix_HasHeadroomAndClampsExternalVolume()
    {
        Assert.That(AudioManager.CalculateBusGain(0.92f, 0.82f, 1f), Is.EqualTo(0.7544f).Within(0.0001f));
        Assert.That(AudioManager.CalculateBusGain(2f, 2f, 2f), Is.EqualTo(1f));
        Assert.That(AudioManager.CalculateBusGain(-1f, 0.8f, 1f), Is.EqualTo(0f));
    }

    [Test]
    public void BurstPool_CoversDenseClimaxWithoutPerHitCreation()
    {
        Assert.That(DemoWorldBurst.PoolCapacity, Is.GreaterThanOrEqualTo(12));
    }

    [Test]
    public void FeedbackOverlay_FindsCanvasElsewhereInTheSameScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject owner = new GameObject("UI manager root");
        GameObject canvasObject = new GameObject("Sibling canvas", typeof(Canvas), typeof(CanvasScaler));
        try
        {
            DemoFeedbackOverlay overlay = DemoFeedbackOverlay.Attach(owner.transform, null);
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.gameObject, Is.EqualTo(canvasObject));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(canvasObject);
        }
    }

    private static bool RayHitsSideWall(Camera camera, float viewportX, float wallX, float yMin, float yMax, float height)
    {
        for (float viewportY = 0.1f; viewportY <= 0.91f; viewportY += 0.1f)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
            if (Mathf.Abs(ray.direction.x) < 0.0001f) continue;
            float enter = (wallX - ray.origin.x) / ray.direction.x;
            if (enter <= 0f) continue;
            Vector3 point = ray.GetPoint(enter);
            if (point.z <= 0f && point.z >= -height && point.y >= yMin && point.y <= yMax) return true;
        }
        return false;
    }

    private static bool RayCrossesWallBelowTop(Camera camera, float viewportYFrom, float viewportYTo, float wallY, float height)
    {
        for (float viewportY = viewportYFrom; viewportY <= viewportYTo + 0.001f; viewportY += 0.05f)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, viewportY, 0f));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) continue;
            float enter = (wallY - ray.origin.y) / ray.direction.y;
            if (enter <= 0f) continue;
            Vector3 point = ray.GetPoint(enter);
            if (point.z <= 0.01f && point.z >= -height - 0.01f) return true;
        }
        return false;
    }
}
