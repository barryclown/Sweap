using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 讓掃把、家具、垃圾、黑洞有 2.5D 的立體感（全部經 Editor API 寫入，不手改 YAML）：
/// - 家具／垃圾／黑洞掛 <see cref="Stand25D"/>：執行時把平放的圖立成朝鏡頭的立牌，腳下補軟陰影；
///   著地點用圖片「實際不透明範圍」算，不用圖檔畫布（很多圖四周留白很大）。
/// - 掃把原本就是斜 50° 的立牌，但一半沉在地板下、影子也跟著立起來：沿鏡頭視線等比縮放把它抬到地面上
///   （畫面上位置與大小完全不變），影子改成平貼地面，加上 <see cref="BroomFloat25D"/> 漂浮。
/// - 特效重新分層：地面陰影類放地面層，火花／分數放在所有站立物件之上。
/// </summary>
public static class Sweep25DVisualSetup
{
    public const string ShadowSpritePath = SweepArenaPresentationFix.ArenaFolder + "/Shadow25D.png";
    public const string BlackHolePrefabPath = "Assets/barry/Sweep/Prefabs/BlackHole.prefab";
    public const string BlackHoleBodyName = "balckHole";
    public const string BroomName = "spire";
    public const string BroomShadowName = "spire (1)";
    public const float StandTilt = -50f;
    public const float WallHuggerTilt = -90f;
    public const float WallHuggerGroundInset = 0.25f;
    public const float BroomHover = 0.35f;

    public static readonly string[] VfxPrefabPaths =
    {
        "Assets/barry/Sweep/VFX/Flash_magic_ellow_blue.prefab",
        "Assets/barry/Sweep/VFX/points.prefab",
        "Assets/barry/Sweep/VFX/Shockwave (1).prefab"
    };

    private static readonly string[] WallHuggerTokens = { "書櫃", "櫃檯" };

    // ───────────────────────── entry points ─────────────────────────

    public static void ApplyPrefabs()
    {
        Sprite shadow = EnsureShadowSprite();
        foreach (string path in SweepColliderAutoFit.FindTrashPrefabPaths())
            EditPrefab(path, root => ConfigureTrash(root, shadow));
        EditPrefab(BlackHolePrefabPath, ConfigureBlackHole);
        foreach (string path in VfxPrefabPaths)
            EditPrefab(path, ConfigureVfx);
    }

    public static void ApplyToActiveScene()
    {
        Sprite shadow = EnsureShadowSprite();
        WorldBounds2D main = FindInScene<WorldBounds2D>(bounds => bounds.isMainWorldBoundary);
        if (main == null) throw new InvalidOperationException("Main WorldBounds2D is missing.");
        Rect room = main.GetWorldRect();

        foreach (SpriteRenderer renderer in FindFurniture())
            ConfigureFurniture(renderer, room, shadow);

        PlayerController player = FindInScene<PlayerController>(_ => true);
        if (player == null) throw new InvalidOperationException("Sweep player is missing.");
        ConfigureBroom(player);

        DynamicSweepMesh cone = player.GetComponentInChildren<DynamicSweepMesh>(true);
        if (cone != null)
        {
            SerializedObject data = new SerializedObject(cone);
            data.FindProperty("sortingOrder").intValue = Depth25D.FloorDecalOrder + 1;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cone);
        }

        foreach (LineRenderer line in player.GetComponentsInChildren<LineRenderer>(true))
        {
            Undo.RecordObject(line, "Draw Sweep drag line above standing objects");
            line.sortingOrder = Depth25D.EffectOrder;
            EditorUtility.SetDirty(line);
        }
    }

    public static string BuildValidationReport(out bool valid)
    {
        List<string> errors = new List<string>();
        List<string> details = new List<string>();

        foreach (string path in SweepColliderAutoFit.FindTrashPrefabPaths())
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Stand25D stand = prefab != null ? prefab.GetComponentInChildren<Stand25D>(true) : null;
            if (stand == null) { errors.Add($"Trash has no Stand25D: {path}"); continue; }
            if (stand.AnchorMode != Stand25D.Anchor.VisibleCenter || !stand.FollowSpin || !stand.HasShadow ||
                !Mathf.Approximately(stand.TiltDegrees, StandTilt))
                errors.Add($"Trash Stand25D is misconfigured: {path}");
            if (stand.VisibleSizeLocal.x <= 0f || stand.VisibleSizeLocal.y <= 0f)
                errors.Add($"Trash Stand25D has no visible size: {path}");
        }

        GameObject blackHole = AssetDatabase.LoadAssetAtPath<GameObject>(BlackHolePrefabPath);
        Transform body = blackHole != null ? FindChild(blackHole.transform, BlackHoleBodyName) : null;
        Stand25D bodyStand = body != null ? body.GetComponent<Stand25D>() : null;
        if (bodyStand == null || bodyStand.AnchorMode != Stand25D.Anchor.VisibleCenter)
            errors.Add("Black hole body has no floating Stand25D.");
        if (blackHole != null)
        {
            foreach (SpriteRenderer renderer in blackHole.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (body != null && renderer.transform == body) continue;
                if (renderer.sortingOrder != Depth25D.FloorDecalOrder)
                    errors.Add($"Black hole shadow {renderer.name} is not on the floor layer.");
            }
        }

        foreach (string path in VfxPrefabPaths)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { errors.Add($"Missing VFX prefab {path}"); continue; }
            foreach (ParticleSystemRenderer renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                bool isShadow = IsGroundShadow(renderer.name);
                if (isShadow && renderer.sortingOrder != Depth25D.FloorDecalOrder + 1)
                    errors.Add($"{path}/{renderer.name} ground shadow should be on the floor layer.");
                if (!isShadow && renderer.sortingOrder < Depth25D.EffectOrder)
                    errors.Add($"{path}/{renderer.name} effect would be hidden behind standing objects.");
            }
        }

        if (SceneManager.GetActiveScene().path == SweepSceneCollisionSetup.SweepScenePath)
            ValidateScene(errors, details);

        valid = errors.Count == 0;
        string report = $"[Sweep25DVisualSetup] {(valid ? "PASS" : "FAIL")}";
        if (details.Count > 0) report += "\n" + string.Join("\n", details);
        if (errors.Count > 0) report += "\n---\n" + string.Join("\n", errors);
        return report;
    }

    // ───────────────────────── prefabs ─────────────────────────

    private static void ConfigureTrash(GameObject root, Sprite shadow)
    {
        SpriteRenderer renderer = root.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null || renderer.sprite == null)
            throw new InvalidOperationException($"Trash prefab {root.name} has no sprite.");
        Stand25D stand = GetOrAdd<Stand25D>(renderer.gameObject);
        VisibleMetrics metrics = MeasureVisible(renderer.sprite);
        SetStand(stand, Stand25D.Anchor.VisibleCenter, StandTilt, metrics, followSpin: true, isStatic: false,
            hover: 0f, bob: 0f, bobFrequency: 1.2f, shadow: shadow, shadowWidth: 0.9f, shadowDepth: 0.4f,
            shadowAlpha: 0.5f, overrideGroundY: false, groundY: 0f);
    }

    private static void ConfigureBlackHole(GameObject root)
    {
        Transform body = FindChild(root.transform, BlackHoleBodyName);
        SpriteRenderer renderer = body != null ? body.GetComponent<SpriteRenderer>() : null;
        if (renderer == null || renderer.sprite == null)
            throw new InvalidOperationException("Black hole body sprite is missing.");

        // 黑洞本身就有一張黑色剪影當影子：留在地面層，本體立起來飄在上面。
        foreach (SpriteRenderer other in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (other == renderer) continue;
            other.sortingOrder = Depth25D.FloorDecalOrder;
            EditorUtility.SetDirty(other);
        }

        VisibleMetrics metrics = MeasureVisible(renderer.sprite);
        Stand25D stand = GetOrAdd<Stand25D>(body.gameObject);
        SetStand(stand, Stand25D.Anchor.VisibleCenter, StandTilt, metrics, followSpin: false, isStatic: false,
            hover: metrics.Size.y * 0.22f, bob: metrics.Size.y * 0.1f, bobFrequency: 0.8f, shadow: null,
            shadowWidth: 0f, shadowDepth: 0f, shadowAlpha: 0f, overrideGroundY: false, groundY: 0f);
    }

    private static void ConfigureVfx(GameObject root)
    {
        foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
        {
            int original = renderer.sortingOrder;
            if (IsGroundShadow(renderer.name))
                renderer.sortingOrder = Depth25D.FloorDecalOrder + 1;
            else if (original < Depth25D.EffectOrder)
                renderer.sortingOrder = Depth25D.EffectOrder + Mathf.Clamp(original, 0, 9);
            EditorUtility.SetDirty(renderer);
        }
    }

    private static bool IsGroundShadow(string objectName) =>
        objectName.StartsWith("Shadow", StringComparison.OrdinalIgnoreCase);

    // ───────────────────────── scene ─────────────────────────

    private static IEnumerable<SpriteRenderer> FindFurniture()
    {
        Transform background = FindInScene<Transform>(item => item.name == "backGroundGroup");
        if (background == null) return Enumerable.Empty<SpriteRenderer>();
        Transform frame = background.Find(SweepArenaPresentationFix.RoomFrameName);
        return background.GetComponentsInChildren<SpriteRenderer>(false)
            .Where(renderer => renderer.sprite != null &&
                               renderer.name != SweepArenaPresentationFix.FloorName &&
                               (frame == null || !renderer.transform.IsChildOf(frame)))
            .ToArray();
    }

    private static bool IsWallHugger(SpriteRenderer renderer)
    {
        string path = AssetDatabase.GetAssetPath(renderer.sprite);
        return WallHuggerTokens.Any(token => path.IndexOf(token, StringComparison.Ordinal) >= 0);
    }

    private static void ConfigureFurniture(SpriteRenderer renderer, Rect room, Sprite shadow)
    {
        VisibleMetrics metrics = MeasureVisible(renderer.sprite);
        bool wallHugger = IsWallHugger(renderer);
        string path = AssetDatabase.GetAssetPath(renderer.sprite);
        float shadowWidth = wallHugger ? 0.9f : path.IndexOf("燈具", StringComparison.Ordinal) >= 0 ? 0.6f : 0.95f;
        Stand25D stand = GetOrAdd<Stand25D>(renderer.gameObject);
        Undo.RecordObject(stand, "Stand Sweep furniture up in 2.5D");
        // 靠牆的書櫃、櫃檯直立貼著後牆（跟牆一樣被俯角壓縮）；其他家具朝鏡頭立起，與掃把同角度。
        SetStand(stand, Stand25D.Anchor.VisibleBase, wallHugger ? WallHuggerTilt : StandTilt, metrics,
            followSpin: false, isStatic: true, hover: 0f, bob: 0f, bobFrequency: 1f, shadow: shadow,
            shadowWidth: shadowWidth, shadowDepth: wallHugger ? 0.16f : 0.34f, shadowAlpha: 0.55f,
            overrideGroundY: wallHugger, groundY: room.yMax - WallHuggerGroundInset);
    }

    /// <summary>
    /// 掃把立牌整個沿「鏡頭→掃把」等比縮放：投影在畫面上的位置與大小不變，但刷頭底部剛好浮在地面上
    /// BroomHover 高度（原本有一半沉在地板下，鏡頭移動時會跟地板錯位）。鏡頭偏移跟玩家一起隨成長等比放大，
    /// 所以任何成長階段都成立。重跑時刷頭已在 -BroomHover，縮放比例為 1，不會越跑越小。
    /// </summary>
    private static void ConfigureBroom(PlayerController player)
    {
        Transform root = player.transform;
        Transform broom = FindChild(root, BroomName);
        SpriteRenderer broomRenderer = broom != null ? broom.GetComponent<SpriteRenderer>() : null;
        if (broomRenderer == null || broomRenderer.sprite == null)
            throw new InvalidOperationException("Player broom sprite is missing.");
        Transform shadow = FindChild(root, BroomShadowName);
        SpriteRenderer shadowRenderer = shadow != null ? shadow.GetComponent<SpriteRenderer>() : null;
        if (shadowRenderer == null) throw new InvalidOperationException("Player broom shadow is missing.");

        BroomFloat25D floatComponent = broom.GetComponent<BroomFloat25D>();
        Vector3 restPosition = floatComponent != null ? floatComponent.RestLocalPosition : broom.localPosition;
        Vector3 eye = new SerializedObject(player).FindProperty("cameraOffset").vector3Value;

        VisibleMetrics metrics = MeasureVisible(broomRenderer.sprite);
        Vector3 bottom = restPosition + broom.localRotation * Vector3.Scale(metrics.Base, broom.localScale);
        float lambda = (-BroomHover - eye.z) / (bottom.z - eye.z);
        Undo.RecordObject(broom, "Lift Sweep broom out of the floor");
        restPosition = eye + (restPosition - eye) * lambda;
        broom.localPosition = restPosition;
        broom.localScale *= lambda;
        Vector3 liftedBottom = eye + (bottom - eye) * lambda;

        // 刷頭（影子）對準掃擊中心：影子落在哪裡，就是打得到垃圾的地方。只做水平平移，漂浮高度不變。
        Vector2 sweepOffset = new SerializedObject(player).FindProperty("sweepOffset").vector2Value;
        Vector3 feetShift = new Vector3(sweepOffset.x - liftedBottom.x, sweepOffset.y - liftedBottom.y, 0f);
        restPosition += feetShift;
        broom.localPosition = restPosition;
        Vector3 ground = new Vector3(sweepOffset.x, sweepOffset.y, 0f);

        // 影子從掃把底下拿出來，平貼地面，橢圓中心對準刷頭正下方。
        Undo.SetTransformParent(shadow, root, "Put Sweep broom shadow on the floor");
        Undo.RecordObject(shadow, "Put Sweep broom shadow on the floor");
        VisibleMetrics shadowMetrics = MeasureVisible(shadowRenderer.sprite);
        float shadowScale = 0.3f;
        shadow.localRotation = Quaternion.identity;
        shadow.localScale = new Vector3(shadowScale, shadowScale, 1f);
        shadow.localPosition = ground - (Vector3)(shadowMetrics.Center * shadowScale) + Vector3.back * 0.01f;
        Undo.RecordObject(shadowRenderer, "Put Sweep broom shadow on the floor");
        shadowRenderer.sortingOrder = Depth25D.FloorDecalOrder;
        EditorUtility.SetDirty(shadow);
        EditorUtility.SetDirty(shadowRenderer);
        EditorUtility.SetDirty(broom);

        if (floatComponent == null) floatComponent = Undo.AddComponent<BroomFloat25D>(broom.gameObject);
        SerializedObject data = new SerializedObject(floatComponent);
        data.FindProperty("broom").objectReferenceValue = broomRenderer;
        data.FindProperty("shadow").objectReferenceValue = shadowRenderer;
        data.FindProperty("restLocalPosition").vector3Value = restPosition;
        data.FindProperty("shadowRestLocalScale").vector3Value = shadow.localScale;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(floatComponent);
    }

    private static void ValidateScene(ICollection<string> errors, ICollection<string> details)
    {
        int furniture = 0;
        foreach (SpriteRenderer renderer in FindFurniture())
        {
            Stand25D stand = renderer.GetComponent<Stand25D>();
            furniture++;
            if (stand == null) { errors.Add($"Furniture {renderer.name} is still a flat sticker."); continue; }
            float expectedTilt = IsWallHugger(renderer) ? WallHuggerTilt : StandTilt;
            if (!stand.IsStatic || stand.AnchorMode != Stand25D.Anchor.VisibleBase || !stand.HasShadow ||
                !Mathf.Approximately(stand.TiltDegrees, expectedTilt))
                errors.Add($"Furniture {renderer.name} Stand25D is misconfigured.");
        }
        if (furniture == 0) errors.Add("No furniture found in the room.");

        PlayerController player = FindInScene<PlayerController>(_ => true);
        Transform broom = player != null ? FindChild(player.transform, BroomName) : null;
        BroomFloat25D floatComponent = broom != null ? broom.GetComponent<BroomFloat25D>() : null;
        if (floatComponent == null || floatComponent.FloorShadow == null)
        {
            errors.Add("Player broom has no BroomFloat25D with a floor shadow.");
        }
        else
        {
            Transform shadow = floatComponent.FloorShadow.transform;
            if (shadow.parent != player.transform) errors.Add("Broom shadow should sit on the floor, not on the tilted broom.");
            if (Quaternion.Angle(shadow.rotation, Quaternion.identity) > 0.5f) errors.Add("Broom shadow is not flat on the floor.");
            VisibleMetrics metrics = MeasureVisible(floatComponent.Broom.sprite);
            Vector3 bottom = broom.TransformPoint(metrics.Base);
            float bottomZ = bottom.z;
            if (Mathf.Abs(bottomZ + BroomHover * Mathf.Abs(player.transform.lossyScale.x)) > 0.05f)
                errors.Add($"Broom bottom is at z={bottomZ:F2}, expected it hovering at {-BroomHover:F2}.");
            Vector2 sweepCenter = player.GetSweepCenter();
            if (Vector2.Distance(new Vector2(bottom.x, bottom.y), sweepCenter) > 0.05f)
                errors.Add($"Broom bristles at {(Vector2)bottom} are not above the sweep center {sweepCenter}.");
            details.Add($"broom: rest={floatComponent.RestLocalPosition} scale={broom.localScale.x:F3} bottomZ={bottomZ:F2}");
        }
        details.Add($"standingFurniture={furniture}");
    }

    // ───────────────────────── helpers ─────────────────────────

    public struct VisibleMetrics
    {
        public Vector2 Base;
        public Vector2 Center;
        public Vector2 Size;
    }

    /// <summary>圖片實際不透明範圍（alpha &gt; 0.08）換算成 sprite 本地座標；讀原檔，不受匯入縮小影響。</summary>
    public static VisibleMetrics MeasureVisible(Sprite sprite)
    {
        string path = AssetDatabase.GetAssetPath(sprite);
        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(path)))
                throw new InvalidOperationException($"Could not read {path}");
            float ratio = source.width / (float)Mathf.Max(1, sprite.texture.width);
            Rect rect = sprite.rect;
            int x0 = Mathf.FloorToInt(rect.x * ratio), y0 = Mathf.FloorToInt(rect.y * ratio);
            int w = Mathf.Min(source.width - x0, Mathf.CeilToInt(rect.width * ratio));
            int h = Mathf.Min(source.height - y0, Mathf.CeilToInt(rect.height * ratio));
            Color32[] pixels = source.GetPixels32();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * source.width;
                for (int x = 0; x < w; x++)
                {
                    if (pixels[row + x0 + x].a <= 20) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < minX) throw new InvalidOperationException($"{path} has no visible pixels.");

            // LoadImage 的 y 從下往上，與 sprite.rect／pivot 同方向。
            float ppu = sprite.pixelsPerUnit;
            Vector2 pivot = sprite.pivot;
            Vector2 ToLocal(float px, float py) => new Vector2(px / ratio - pivot.x, py / ratio - pivot.y) / ppu;
            Vector2 min = ToLocal(minX, minY);
            Vector2 max = ToLocal(maxX + 1, maxY + 1);
            return new VisibleMetrics
            {
                Base = new Vector2((min.x + max.x) * 0.5f, min.y),
                Center = (min + max) * 0.5f,
                Size = max - min
            };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void SetStand(
        Stand25D stand, Stand25D.Anchor anchor, float tilt, VisibleMetrics metrics, bool followSpin, bool isStatic,
        float hover, float bob, float bobFrequency, Sprite shadow, float shadowWidth, float shadowDepth,
        float shadowAlpha, bool overrideGroundY, float groundY)
    {
        SerializedObject data = new SerializedObject(stand);
        data.FindProperty("anchor").enumValueIndex = (int)anchor;
        data.FindProperty("tiltDegrees").floatValue = tilt;
        data.FindProperty("visibleBaseLocal").vector2Value = metrics.Base;
        data.FindProperty("visibleCenterLocal").vector2Value = metrics.Center;
        data.FindProperty("visibleSizeLocal").vector2Value = metrics.Size;
        data.FindProperty("followSpin").boolValue = followSpin;
        data.FindProperty("isStatic").boolValue = isStatic;
        data.FindProperty("overrideGroundY").boolValue = overrideGroundY;
        data.FindProperty("groundY").floatValue = groundY;
        data.FindProperty("hoverHeight").floatValue = hover;
        data.FindProperty("bobAmplitude").floatValue = bob;
        data.FindProperty("bobFrequency").floatValue = bobFrequency;
        data.FindProperty("shadowSprite").objectReferenceValue = shadow;
        data.FindProperty("shadowWidth").floatValue = shadowWidth;
        data.FindProperty("shadowDepth").floatValue = shadowDepth;
        data.FindProperty("shadowAlpha").floatValue = shadowAlpha;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(stand);
    }

    private static Sprite EnsureShadowSprite()
    {
        if (!File.Exists(ShadowSpritePath))
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                Color32[] pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                string temp = ShadowSpritePath + ".tmp";
                File.WriteAllBytes(temp, texture.EncodeToPNG());
                if (File.Exists(ShadowSpritePath)) File.Delete(ShadowSpritePath);
                File.Move(temp, ShadowSpritePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(ShadowSpritePath, ImportAssetOptions.ForceSynchronousImport);
        }
        SweepArenaPresentationFix.ConfigureSpriteImporter(ShadowSpritePath, 128f, true);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ShadowSpritePath);
        if (sprite == null) throw new InvalidOperationException($"Shadow sprite failed to import: {ShadowSpritePath}");
        return sprite;
    }

    private static void EditPrefab(string path, Action<GameObject> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static Transform FindChild(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == name);
    }

    private static T FindInScene<T>(Func<T, bool> predicate) where T : Component
    {
        Scene active = SceneManager.GetActiveScene();
        return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include)
            .FirstOrDefault(item => item != null && item.gameObject.scene == active && predicate(item));
    }
}
