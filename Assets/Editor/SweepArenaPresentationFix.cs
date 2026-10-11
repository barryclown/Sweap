using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 把 Sweep 場地重建成真正的 2.5D 房間（全部尺寸都由 WorldBounds2D 推出）：
/// - 後牆、左右牆：沿用原本的牆壁素材立起來（往 -Z 長高），用 Tiled 平鋪，不再整張拉伸。
/// - 前牆：剖面矮牆，蓋在玩法物件上層；牆腳放在玩法邊界外，玩家走不到它後面。
/// - 牆頂加深色頂蓋，地板只鋪房間內，房間外是深色背景。
/// 牆面只負責視覺，碰撞仍由 WorldBounds2D 處理。
/// </summary>
public static class SweepArenaPresentationFix
{
    public const string WallSpritePath = "Assets/barry/Sweep/Sprites/object/牆壁.png";
    public const string ArenaFolder = "Assets/barry/Sweep/Visuals/Arena";
    public const string FrontWallSpritePath = ArenaFolder + "/ArenaFrontWall.png";
    public const string SolidSpritePath = ArenaFolder + "/ArenaSolid.png";
    // 上一版平貼在地上的細框素材；新版不用，重建時移除。
    public const string LegacyFrameSpritePath = ArenaFolder + "/ArenaWallFrame.png";

    public const string RoomFrameName = "RoomFrameVisual";
    public const string DecorativeHumansName = "human";
    public const string FloorName = "BackGround (32)";
    public const string TopCapName = "TopWallCap";
    public const string BottomCapName = "BottomWallCap";
    public const string LeftCapName = "LeftWallCap";
    public const string RightCapName = "RightWallCap";

    public static readonly Vector2 ArenaSize = new Vector2(132f, 60f);
    public const float WallHeight = 6f;
    public const float FrontWallHeight = 1.4f;
    public const int FrontWallCropPixels = 140;
    // 掃把圖像最低點約在座標下方 1.6×體型；2.4 讓初始大小留一小段地板，最大成長時刷毛尖剛好碰到矮牆。
    public const float FrontWallClearance = 2.4f;
    public const float CapThickness = 0.45f;
    public const float FloorTileScale = 3f;

    public const int FloorSortingOrder = -100;
    public const int WallSortingOrder = -60;
    public const int CapSortingOrder = -58;
    public const int FrontWallSortingOrder = 200;
    public const int FrontCapSortingOrder = 201;

    public static readonly Color CapColor = new Color(0.30f, 0.22f, 0.17f, 1f);
    public static readonly Color FrontCapColor = new Color(0.40f, 0.31f, 0.24f, 1f);
    public static readonly Color OutsideColor = new Color(0.075f, 0.055f, 0.07f, 1f);

    // 靠牆擺設在 main 分支（550ce5d）的世界座標 y，以及當時上牆的牆腳 y。
    // 圖片四周有透明留白，不能用 bounds 貼牆；改為保持原作者擺放時與牆腳的相對距離。
    public const float LegacyTopWallLineY = 24.78f;
    private static readonly (string Token, float OriginalWorldY)[] WallHuggingDecor =
    {
        ("書櫃", 25.68f),
        ("櫃檯", 23.75f)
    };
    private static readonly Vector3 WallUp = new Vector3(0f, 0f, -1f);

    public static string[] FramePieceNames => new[]
    {
        SweepSceneCollisionSetup.TopWallName,
        SweepSceneCollisionSetup.BottomWallName,
        SweepSceneCollisionSetup.LeftWallName,
        SweepSceneCollisionSetup.RightWallName,
        TopCapName, BottomCapName, LeftCapName, RightCapName
    };

    public static float GetFrontWallLineY(Rect bounds) => bounds.yMin - FrontWallClearance;

    public static void ApplyToActiveScene()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.path != SweepSceneCollisionSetup.SweepScenePath)
            throw new InvalidOperationException($"Open {SweepSceneCollisionSetup.SweepScenePath} first.");

        EnsureArenaSprites();
        Sprite wallSprite = LoadSprite(WallSpritePath);
        Sprite frontSprite = LoadSprite(FrontWallSpritePath);
        Sprite solidSprite = LoadSprite(SolidSpritePath);
        if (wallSprite == null || frontSprite == null || solidSprite == null)
            throw new InvalidOperationException("Arena sprites are missing after import.");

        WorldBounds2D main = FindMainBoundary();
        Undo.RecordObject(main, "Enlarge Sweep arena");
        main.size = ArenaSize;
        EditorUtility.SetDirty(main);
        Rect bounds = main.GetWorldRect();

        Transform backgroundGroup = FindSceneTransform("backGroundGroup");
        if (backgroundGroup == null)
            throw new InvalidOperationException("Sweep scene is missing backGroundGroup.");

        RemoveDecorativeHumans(backgroundGroup);
        SpriteRenderer floor = ConfigureFloor(backgroundGroup, bounds);
        RebuildRoomFrame(backgroundGroup, bounds, wallSprite, frontSprite, solidSprite, floor.sharedMaterial);
        MoveWallHuggingDecor(backgroundGroup, bounds);
        ConfigureCamera();
        ConfigurePlayerSorting();
        DeleteLegacyFrameSprite();
    }

    public static string BuildValidationReport(out bool valid)
    {
        List<string> errors = new List<string>();
        List<string> details = new List<string>();
        WorldBounds2D main = FindMainBoundary(false);
        if (main == null)
        {
            valid = false;
            return "[SweepArenaPresentationFix] FAIL\nMain WorldBounds2D is missing.";
        }

        if (Vector2.Distance(main.size, ArenaSize) > 0.001f)
            errors.Add($"Arena size is {main.size}, expected {ArenaSize}.");
        Rect bounds = main.GetWorldRect();

        Transform backgroundGroup = FindSceneTransform("backGroundGroup");
        if (backgroundGroup == null)
        {
            errors.Add("backGroundGroup is missing.");
        }
        else
        {
            if (FindDescendant(backgroundGroup, DecorativeHumansName) != null)
                errors.Add("Decorative gameplay humans were not removed.");

            Transform frame = backgroundGroup.Find(RoomFrameName);
            if (frame == null) errors.Add($"{RoomFrameName} is missing.");
            else ValidateFrame(frame, bounds, errors, details);

            ValidateFloor(backgroundGroup, bounds, errors);
            ValidateWallHuggingDecor(backgroundGroup, bounds, errors);
        }

        Camera camera = FindSceneCamera();
        if (camera == null) errors.Add("Main Camera is missing.");
        else if (camera.clearFlags != CameraClearFlags.SolidColor || ColorDistance(camera.backgroundColor, OutsideColor) > 0.01f)
            errors.Add("Camera background is not the dark outside color.");

        if (AssetDatabase.LoadMainAssetAtPath(LegacyFrameSpritePath) != null)
            errors.Add($"Legacy flat frame sprite still exists: {LegacyFrameSpritePath}");

        valid = errors.Count == 0;
        string report = $"[SweepArenaPresentationFix] {(valid ? "PASS" : "FAIL")}";
        if (details.Count > 0) report += "\n" + string.Join("\n", details);
        if (errors.Count > 0) report += "\n---\n" + string.Join("\n", errors);
        return report;
    }

    // ───────────────────────── assets ─────────────────────────

    private static void EnsureArenaSprites()
    {
        Directory.CreateDirectory(ArenaFolder);
        ConfigureSpriteImporter(WallSpritePath, 100f, false);

        if (!File.Exists(FrontWallSpritePath))
        {
            // 前牆只取原牆壁圖最底下的護牆板與踢腳線（原生解析度，不拉伸）。
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(WallSpritePath)))
                    throw new InvalidOperationException($"Could not read {WallSpritePath}");
                int height = Mathf.Min(FrontWallCropPixels, source.height);
                Color32[] pixels = GetPixelBlock(source, 0, 0, source.width, height);
                WritePng(FrontWallSpritePath, source.width, height, pixels);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        if (!File.Exists(SolidSpritePath))
        {
            Color32[] white = Enumerable.Repeat(new Color32(255, 255, 255, 255), 16).ToArray();
            WritePng(SolidSpritePath, 4, 4, white);
        }

        AssetDatabase.ImportAsset(FrontWallSpritePath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(SolidSpritePath, ImportAssetOptions.ForceSynchronousImport);
        ConfigureSpriteImporter(FrontWallSpritePath, 100f, true);
        ConfigureSpriteImporter(SolidSpritePath, 4f, true);
    }

    private static Color32[] GetPixelBlock(Texture2D texture, int x, int y, int width, int height)
    {
        Color32[] all = texture.GetPixels32();
        Color32[] block = new Color32[width * height];
        for (int row = 0; row < height; row++)
            Array.Copy(all, (y + row) * texture.width + x, block, row * width, width);
        return block;
    }

    private static void WritePng(string path, int width, int height, Color32[] pixels)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, texture.EncodeToPNG());
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    /// <summary>Tiled 繪製需要 FullRect；其餘欄位對齊 SweepPresentationOptimizer 的 importer 標準。</summary>
    internal static void ConfigureSpriteImporter(string path, float pixelsPerUnit, bool ownedAsset)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            throw new InvalidOperationException($"Could not load TextureImporter for {path}");

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        bool changed = settings.spriteMeshType != SpriteMeshType.FullRect;
        if (changed)
        {
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }

        if (ownedAsset)
        {
            int maxSize = SweepPresentationOptimizer.GetTextureMaxSizeForPath(path);
            changed |= importer.textureType != TextureImporterType.Sprite ||
                       importer.spriteImportMode != SpriteImportMode.Single ||
                       !Mathf.Approximately(importer.spritePixelsPerUnit, pixelsPerUnit) ||
                       importer.mipmapEnabled || importer.maxTextureSize != maxSize;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (!standalone.overridden || standalone.maxTextureSize != maxSize) changed = true;
            standalone.name = "Standalone";
            standalone.overridden = true;
            standalone.maxTextureSize = maxSize;
            standalone.format = TextureImporterFormat.Automatic;
            standalone.compressionQuality = 72;
            importer.SetPlatformTextureSettings(standalone);
        }

        if (changed) importer.SaveAndReimport();
    }

    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        return sprite != null ? sprite : AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
    }

    private static void DeleteLegacyFrameSprite()
    {
        if (AssetDatabase.LoadMainAssetAtPath(LegacyFrameSpritePath) == null) return;
        string guid = AssetDatabase.AssetPathToGUID(LegacyFrameSpritePath);
        bool referenced = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include)
            .Any(renderer => renderer.sprite != null &&
                             AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(renderer.sprite)) == guid);
        if (!referenced) AssetDatabase.DeleteAsset(LegacyFrameSpritePath);
    }

    // ───────────────────────── scene ─────────────────────────

    private static void RemoveDecorativeHumans(Transform backgroundGroup)
    {
        Transform humans = FindDescendant(backgroundGroup, DecorativeHumansName);
        if (humans != null) Undo.DestroyObjectImmediate(humans.gameObject);
    }

    private static Rect GetFloorRect(Rect bounds)
    {
        float frontOuter = GetFrontWallLineY(bounds) - CapThickness;
        return Rect.MinMaxRect(bounds.xMin, frontOuter, bounds.xMax, bounds.yMax);
    }

    private static SpriteRenderer ConfigureFloor(Transform backgroundGroup, Rect bounds)
    {
        Transform floor = FindDescendant(backgroundGroup, FloorName);
        SpriteRenderer renderer = floor != null ? floor.GetComponent<SpriteRenderer>() : null;
        if (renderer == null || renderer.sprite == null)
            throw new InvalidOperationException("Sweep arena floor sprite is missing.");
        ConfigureSpriteImporter(AssetDatabase.GetAssetPath(renderer.sprite), renderer.sprite.pixelsPerUnit, false);

        // 地板圖左右、上下接縫都幾乎無差，用等比平鋪取代各軸不同倍率的整張拉伸。
        Rect floorRect = GetFloorRect(bounds);
        Undo.RecordObject(floor, "Fit Sweep arena floor");
        Undo.RecordObject(renderer, "Tile Sweep arena floor");
        // 先切 Tiled 再設縮放：drawMode 的 setter 會為了「保持原本視覺大小」自動改 transform 縮放。
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        floor.position = new Vector3(floorRect.center.x, floorRect.center.y, 0f);
        floor.rotation = Quaternion.identity;
        floor.localScale = new Vector3(FloorTileScale, FloorTileScale, 1f);
        renderer.size = floorRect.size / FloorTileScale;
        renderer.sortingOrder = FloorSortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        EditorUtility.SetDirty(floor);
        EditorUtility.SetDirty(renderer);
        return renderer;
    }

    private static void RebuildRoomFrame(
        Transform backgroundGroup, Rect bounds, Sprite wallSprite, Sprite frontSprite, Sprite solidSprite, Material material)
    {
        Transform oldFrame = backgroundGroup.Find(RoomFrameName);
        if (oldFrame != null) Undo.DestroyObjectImmediate(oldFrame.gameObject);
        foreach (string pieceName in FramePieceNames)
        {
            Transform legacy = FindDescendant(backgroundGroup, pieceName);
            if (legacy != null) Undo.DestroyObjectImmediate(legacy.gameObject);
        }

        GameObject frameObject = new GameObject(RoomFrameName);
        Undo.RegisterCreatedObjectUndo(frameObject, "Create Sweep room frame");
        frameObject.transform.SetParent(backgroundGroup, false);
        frameObject.transform.localPosition = Vector3.zero;
        frameObject.transform.localRotation = Quaternion.identity;
        frameObject.transform.localScale = Vector3.one;
        Transform root = frameObject.transform;

        float frontLine = GetFrontWallLineY(bounds);
        float frontOuter = frontLine - CapThickness;
        float sideLength = bounds.yMax - frontOuter;
        float sideCenterY = (bounds.yMax + frontOuter) * 0.5f;

        CreateWallFace(root, SweepSceneCollisionSetup.TopWallName,
            new Vector3(bounds.center.x, bounds.yMax, 0f), Vector3.right,
            bounds.width, WallHeight, wallSprite, WallSortingOrder, material);
        CreateWallFace(root, SweepSceneCollisionSetup.LeftWallName,
            new Vector3(bounds.xMin, sideCenterY, 0f), Vector3.up,
            sideLength, WallHeight, wallSprite, WallSortingOrder, material);
        CreateWallFace(root, SweepSceneCollisionSetup.RightWallName,
            new Vector3(bounds.xMax, sideCenterY, 0f), Vector3.down,
            sideLength, WallHeight, wallSprite, WallSortingOrder, material);
        CreateWallFace(root, SweepSceneCollisionSetup.BottomWallName,
            new Vector3(bounds.center.x, frontOuter, 0f), Vector3.right,
            bounds.width + CapThickness * 2f, FrontWallHeight, frontSprite, FrontWallSortingOrder, material);

        float outerWidth = bounds.width + CapThickness * 2f;
        CreateCap(root, TopCapName,
            new Vector3(bounds.center.x, bounds.yMax + CapThickness * 0.5f, -WallHeight),
            new Vector2(outerWidth, CapThickness), CapColor, CapSortingOrder, solidSprite, material);
        CreateCap(root, LeftCapName,
            new Vector3(bounds.xMin - CapThickness * 0.5f, sideCenterY + CapThickness * 0.5f, -WallHeight),
            new Vector2(CapThickness, sideLength + CapThickness), CapColor, CapSortingOrder, solidSprite, material);
        CreateCap(root, RightCapName,
            new Vector3(bounds.xMax + CapThickness * 0.5f, sideCenterY + CapThickness * 0.5f, -WallHeight),
            new Vector2(CapThickness, sideLength + CapThickness), CapColor, CapSortingOrder, solidSprite, material);
        CreateCap(root, BottomCapName,
            new Vector3(bounds.center.x, frontOuter + CapThickness * 0.5f, -FrontWallHeight),
            new Vector2(outerWidth, CapThickness), FrontCapColor, FrontCapSortingOrder, solidSprite, material);

        SweepRoomFrame roomFrame = frameObject.AddComponent<SweepRoomFrame>();
        SerializedObject data = new SerializedObject(roomFrame);
        data.FindProperty("wallHeight").floatValue = WallHeight;
        data.FindProperty("frontWallHeight").floatValue = FrontWallHeight;
        data.FindProperty("frontWallLineY").floatValue = frontLine;
        data.FindProperty("capThickness").floatValue = CapThickness;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(roomFrame);
    }

    /// <summary>
    /// 立起一面牆：牆腳中心在 baseCenter（z=0），沿 right 方向延伸 length，往 -Z 長高 height。
    /// 原圖原生寬度平鋪，只壓縮高度，所以不會出現整張拉伸的破圖。
    /// </summary>
    private static void CreateWallFace(
        Transform parent, string name, Vector3 baseCenter, Vector3 right, float length, float height,
        Sprite sprite, int sortingOrder, Material material)
    {
        GameObject wall = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(wall, "Create Sweep wall face");
        wall.transform.SetParent(parent, false);

        Vector3 forward = Vector3.Cross(right, WallUp);
        Quaternion rotation = Quaternion.LookRotation(forward, WallUp);
        Vector2 spriteSize = sprite.bounds.size;
        float heightScale = height / spriteSize.y;
        Vector2 pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        Vector2 drawSize = new Vector2(length, spriteSize.y);
        Vector2 centerFromPivot = Vector2.Scale(new Vector2(0.5f, 0.5f) - pivot, drawSize);

        Vector3 worldCenter = baseCenter + WallUp * (height * 0.5f);
        Vector3 pivotWorld = worldCenter - rotation * new Vector3(centerFromPivot.x, centerFromPivot.y * heightScale, 0f);
        SpriteRenderer renderer = wall.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        if (material != null) renderer.sharedMaterial = material;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.tileMode = SpriteTileMode.Continuous;
        wall.transform.SetPositionAndRotation(pivotWorld, rotation);
        wall.transform.localScale = new Vector3(1f, heightScale, 1f);
        renderer.size = drawSize;
        ConfigureRenderer(renderer, sortingOrder);
    }

    private static void CreateCap(
        Transform parent, string name, Vector3 center, Vector2 size, Color color, int sortingOrder,
        Sprite sprite, Material material)
    {
        GameObject cap = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(cap, "Create Sweep wall cap");
        cap.transform.SetParent(parent, false);
        cap.transform.SetPositionAndRotation(center, Quaternion.identity);
        Vector2 spriteSize = sprite.bounds.size;
        cap.transform.localScale = new Vector3(size.x / spriteSize.x, size.y / spriteSize.y, 1f);

        SpriteRenderer renderer = cap.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        if (material != null) renderer.sharedMaterial = material;
        renderer.color = color;
        ConfigureRenderer(renderer, sortingOrder);
    }

    private static void ConfigureRenderer(SpriteRenderer renderer, int sortingOrder)
    {
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        EditorUtility.SetDirty(renderer);
    }

    /// <summary>原本靠在舊上牆的書櫃、櫃檯，跟著場地放大移到新後牆前（與牆腳距離不變）。</summary>
    private static void MoveWallHuggingDecor(Transform backgroundGroup, Rect bounds)
    {
        foreach ((SpriteRenderer renderer, float targetY) in FindWallHuggingDecor(backgroundGroup, bounds))
        {
            Vector3 position = renderer.transform.position;
            if (Mathf.Abs(position.y - targetY) < 0.001f) continue;
            Undo.RecordObject(renderer.transform, "Move Sweep decor to the back wall");
            renderer.transform.position = new Vector3(position.x, targetY, position.z);
            EditorUtility.SetDirty(renderer.transform);
        }
    }

    private static List<(SpriteRenderer Renderer, float TargetY)> FindWallHuggingDecor(Transform backgroundGroup, Rect bounds)
    {
        Transform frame = backgroundGroup.Find(RoomFrameName);
        List<(SpriteRenderer, float)> result = new List<(SpriteRenderer, float)>();
        foreach (SpriteRenderer renderer in backgroundGroup.GetComponentsInChildren<SpriteRenderer>(false))
        {
            if (renderer.sprite == null || (frame != null && renderer.transform.IsChildOf(frame))) continue;
            string path = AssetDatabase.GetAssetPath(renderer.sprite);
            foreach ((string token, float originalY) in WallHuggingDecor)
            {
                if (path.IndexOf(token, StringComparison.Ordinal) < 0) continue;
                result.Add((renderer, originalY + (bounds.yMax - LegacyTopWallLineY)));
                break;
            }
        }
        return result;
    }

    private static void ConfigureCamera()
    {
        Camera camera = FindSceneCamera();
        if (camera == null) throw new InvalidOperationException("Sweep Main Camera is missing.");
        Undo.RecordObject(camera, "Dark outside for Sweep room");
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = OutsideColor;
        EditorUtility.SetDirty(camera);
    }

    private static void ConfigurePlayerSorting()
    {
        PlayerController player = UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include)
            .FirstOrDefault(item => item != null && item.gameObject.scene == SceneManager.GetActiveScene());
        if (player == null) return;

        foreach (SpriteRenderer renderer in player.GetComponentsInChildren<SpriteRenderer>(true))
        {
            Undo.RecordObject(renderer, "Keep Sweep player between room walls and front wall");
            string path = AssetDatabase.GetAssetPath(renderer.sprite);
            // 影子平貼地面放在地面層；掃把本體的前後排序由 BroomFloat25D 在執行時依著地點決定。
            renderer.sortingOrder = path.IndexOf("陰影", StringComparison.Ordinal) >= 0 ? Depth25D.FloorDecalOrder : 5;
            EditorUtility.SetDirty(renderer);
        }
    }

    // ───────────────────────── validation ─────────────────────────

    private struct PieceSpec
    {
        public string Name;
        public Vector3 BaseCenter;
        public Vector3 Right;
        public float Length;
        public float Height;
        public string SpritePath;
        public int SortingOrder;
    }

    private static PieceSpec[] GetExpectedFaces(Rect bounds)
    {
        float frontOuter = GetFrontWallLineY(bounds) - CapThickness;
        float sideLength = bounds.yMax - frontOuter;
        float sideCenterY = (bounds.yMax + frontOuter) * 0.5f;
        return new[]
        {
            new PieceSpec { Name = SweepSceneCollisionSetup.TopWallName, BaseCenter = new Vector3(bounds.center.x, bounds.yMax, 0f),
                Right = Vector3.right, Length = bounds.width, Height = WallHeight, SpritePath = WallSpritePath, SortingOrder = WallSortingOrder },
            new PieceSpec { Name = SweepSceneCollisionSetup.LeftWallName, BaseCenter = new Vector3(bounds.xMin, sideCenterY, 0f),
                Right = Vector3.up, Length = sideLength, Height = WallHeight, SpritePath = WallSpritePath, SortingOrder = WallSortingOrder },
            new PieceSpec { Name = SweepSceneCollisionSetup.RightWallName, BaseCenter = new Vector3(bounds.xMax, sideCenterY, 0f),
                Right = Vector3.down, Length = sideLength, Height = WallHeight, SpritePath = WallSpritePath, SortingOrder = WallSortingOrder },
            new PieceSpec { Name = SweepSceneCollisionSetup.BottomWallName, BaseCenter = new Vector3(bounds.center.x, frontOuter, 0f),
                Right = Vector3.right, Length = bounds.width + CapThickness * 2f, Height = FrontWallHeight,
                SpritePath = FrontWallSpritePath, SortingOrder = FrontWallSortingOrder }
        };
    }

    private static void ValidateFrame(Transform frame, Rect bounds, ICollection<string> errors, ICollection<string> details)
    {
        string[] expectedNames = FramePieceNames;
        if (frame.childCount != expectedNames.Length)
            errors.Add($"Room frame has {frame.childCount} children, expected {expectedNames.Length}.");

        SweepRoomFrame roomFrame = frame.GetComponent<SweepRoomFrame>();
        if (roomFrame == null) errors.Add("Room frame has no SweepRoomFrame camera limits.");
        else
        {
            if (!Mathf.Approximately(roomFrame.WallHeight, WallHeight)) errors.Add("SweepRoomFrame wall height mismatch.");
            if (!Mathf.Approximately(roomFrame.FrontWallHeight, FrontWallHeight)) errors.Add("SweepRoomFrame front height mismatch.");
            if (Mathf.Abs(roomFrame.FrontWallLineY - GetFrontWallLineY(bounds)) > 0.01f) errors.Add("SweepRoomFrame front line mismatch.");
        }

        foreach (PieceSpec spec in GetExpectedFaces(bounds))
        {
            Transform wall = frame.Find(spec.Name);
            if (wall == null)
            {
                errors.Add($"Room frame is missing {spec.Name}.");
                continue;
            }

            SpriteRenderer renderer = wall.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null || AssetDatabase.GetAssetPath(renderer.sprite) != spec.SpritePath)
            {
                errors.Add($"{spec.Name} does not use {spec.SpritePath}.");
                continue;
            }
            if (renderer.drawMode != SpriteDrawMode.Tiled) errors.Add($"{spec.Name} is stretched instead of tiled.");
            if (renderer.sortingOrder != spec.SortingOrder)
                errors.Add($"{spec.Name} sorting order is {renderer.sortingOrder}, expected {spec.SortingOrder}.");
            if (Vector3.Dot(wall.up, WallUp) < 0.999f) errors.Add($"{spec.Name} is not standing up from the floor.");
            if (Vector3.Dot(wall.right, spec.Right) < 0.999f) errors.Add($"{spec.Name} runs along {wall.right}, expected {spec.Right}.");
            if (Mathf.Abs(renderer.size.x - spec.Length) > 0.01f)
                errors.Add($"{spec.Name} length is {renderer.size.x:F2}, expected {spec.Length:F2}.");

            Bounds drawn = renderer.bounds;
            float drawnHeight = drawn.max.z - drawn.min.z;
            if (Mathf.Abs(drawnHeight - spec.Height) > 0.02f)
                errors.Add($"{spec.Name} height is {drawnHeight:F2}, expected {spec.Height:F2}.");
            if (Mathf.Abs(drawn.max.z) > 0.02f) errors.Add($"{spec.Name} does not stand on the floor (z max {drawn.max.z:F3}).");
            if (Vector3.Distance(new Vector3(drawn.center.x, drawn.center.y, 0f), spec.BaseCenter) > 0.02f)
                errors.Add($"{spec.Name} base center is {drawn.center}, expected {spec.BaseCenter}.");
            if (wall.GetComponent<Collider2D>() != null) errors.Add($"{spec.Name} must remain visual-only.");
            if (PrefabUtility.IsPartOfPrefabInstance(wall.gameObject)) errors.Add($"{spec.Name} must not be a prefab instance.");
        }

        foreach (string capName in new[] { TopCapName, BottomCapName, LeftCapName, RightCapName })
        {
            Transform cap = frame.Find(capName);
            SpriteRenderer renderer = cap != null ? cap.GetComponent<SpriteRenderer>() : null;
            if (renderer == null) errors.Add($"Room frame is missing {capName}.");
            else if (Vector3.Dot(cap.forward, Vector3.forward) < 0.999f) errors.Add($"{capName} is not lying flat on the wall top.");
        }

        details.Add($"arenaRoom: bounds={bounds} wallHeight={WallHeight:F2} front={FrontWallHeight:F2}@{GetFrontWallLineY(bounds):F2} " +
                    $"orders floor={FloorSortingOrder} walls={WallSortingOrder} front={FrontWallSortingOrder}");
    }

    private static void ValidateFloor(Transform backgroundGroup, Rect bounds, ICollection<string> errors)
    {
        Transform floor = FindDescendant(backgroundGroup, FloorName);
        SpriteRenderer renderer = floor != null ? floor.GetComponent<SpriteRenderer>() : null;
        if (renderer == null || renderer.sprite == null)
        {
            errors.Add("Arena floor renderer is missing.");
            return;
        }

        Rect expected = GetFloorRect(bounds);
        Bounds drawn = renderer.bounds;
        if (Mathf.Abs(drawn.min.x - expected.xMin) > 0.05f || Mathf.Abs(drawn.max.x - expected.xMax) > 0.05f ||
            Mathf.Abs(drawn.min.y - expected.yMin) > 0.05f || Mathf.Abs(drawn.max.y - expected.yMax) > 0.05f)
            errors.Add($"Arena floor covers {drawn.min}-{drawn.max}, expected {expected}.");
        if (renderer.drawMode != SpriteDrawMode.Tiled || Mathf.Abs(floor.lossyScale.x - floor.lossyScale.y) > 0.001f)
            errors.Add("Arena floor must be uniformly tiled, not stretched per axis.");
        foreach (string path in new[] { AssetDatabase.GetAssetPath(renderer.sprite), WallSpritePath, FrontWallSpritePath })
        {
            TextureImporterSettings settings = new TextureImporterSettings();
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.ReadTextureSettings(settings);
                if (settings.spriteMeshType != SpriteMeshType.FullRect) errors.Add($"Tiled sprite is not Full Rect: {path}");
            }
        }
        if (renderer.sortingOrder != FloorSortingOrder) errors.Add("Arena floor is not the lowest layer.");
    }

    private static void ValidateWallHuggingDecor(Transform backgroundGroup, Rect bounds, ICollection<string> errors)
    {
        List<(SpriteRenderer Renderer, float TargetY)> decor = FindWallHuggingDecor(backgroundGroup, bounds);
        if (decor.Count == 0) errors.Add("No wall-hugging decor (書櫃/櫃檯) found in the room.");
        foreach ((SpriteRenderer renderer, float targetY) in decor)
        {
            if (Mathf.Abs(renderer.transform.position.y - targetY) > 0.01f)
                errors.Add($"{renderer.name} y={renderer.transform.position.y:F2} is not against the back wall (expected {targetY:F2}).");
        }
    }

    // ───────────────────────── lookup ─────────────────────────

    private static float ColorDistance(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

    private static Camera FindSceneCamera()
    {
        return UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
            .FirstOrDefault(camera => camera != null && camera.CompareTag("MainCamera") &&
                                      camera.gameObject.scene == SceneManager.GetActiveScene());
    }

    private static WorldBounds2D FindMainBoundary(bool required = true)
    {
        WorldBounds2D main = UnityEngine.Object.FindObjectsByType<WorldBounds2D>(FindObjectsInactive.Include)
            .FirstOrDefault(boundary => boundary != null && boundary.isMainWorldBoundary &&
                                        boundary.gameObject.scene == SceneManager.GetActiveScene());
        if (main == null && required) throw new InvalidOperationException("Main WorldBounds2D is missing.");
        return main;
    }

    private static Transform FindSceneTransform(string name)
    {
        return UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(item => item != null && item.name == name &&
                                    item.gameObject.scene == SceneManager.GetActiveScene());
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root == null) return null;
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item != null && item.name == name);
    }
}
