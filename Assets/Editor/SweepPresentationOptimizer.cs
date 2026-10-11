using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 將 Sweep 展演版的 importer、渲染、牆面與手感參數收斂成可重跑的 Editor API 流程。
/// 所有 Scene、Prefab 與 Render Pipeline 資產都由 Unity 寫入，避免直接改 YAML。
/// </summary>
public static class SweepPresentationOptimizer
{
    public const string BlackHolePrefabPath = "Assets/barry/Sweep/Prefabs/BlackHole.prefab";
    public const string RenderPipelinePath = "Assets/Settings/PC_RPAsset.asset";
    public const float PlayerBodyRadius = 0.68f;
    public const float PlayerSweepRadius = 0.84f;
    public const float PlayerSweepForgiveness = 1.2f;
    public const float PlayerForwardReach = 0.55f;
    public const float MagnetRadiusMultiplier = 1.34f;
    public const float BlackHoleAbsorbRadius = 1.12f;
    public const float BlackHoleSuckRadius = 3.8f;
    public const float BlackHoleSuckForce = 13f;
    public const float BlackHoleRadiusGrowthWeight = 0.62f;
    public const float BlackHoleForceGrowthWeight = 0.4f;

    private static readonly string[] TextureRoots = { "Assets/barry/Sweep" };
    private static readonly string[] AudioRoots = { "Assets/barry/Sweep" };

    [MenuItem("Tools/Sweep Demo/Presentation/Apply Complete Optimization")]
    public static void ApplyAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Presentation optimization only runs in Edit Mode.");

        int textureCount = OptimizeTextureImporters();
        int audioCount = OptimizeAudioImporters();
        // Sprite 的 alpha physics outline 會隨 texture importer 重建；必須先完成
        // 同步重匯入，再重算場景腳印，否則會留下看似很小但真實存在的誤差。
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        int fittedTrashPrefabs = SweepColliderAutoFit.AutoFitAllTrashPrefabs(false);
        OptimizeRenderPipeline();
        TuneBlackHolePrefab();
        Sweep25DVisualSetup.ApplyPrefabs();

        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        SweepSceneCollisionSetup.SetupSceneMenu();
        TuneSweepScene();
        Sweep25DVisualSetup.ApplyToActiveScene();
        SweepResultScreenSetup.ApplyToActiveScene();

        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        string report = BuildValidationReport(out bool valid);
        if (!valid) throw new InvalidOperationException(report);

        Debug.Log($"[SweepPresentationOptimizer] PRESENTATION_OPTIMIZATION_OK " +
                  $"textures={textureCount} audio={audioCount} trashColliders={fittedTrashPrefabs}\n{report}");
    }

    [MenuItem("Tools/Sweep Demo/Presentation/Validate Complete Optimization")]
    public static void ValidateMenu()
    {
        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        string report = BuildValidationReport(out bool valid);
        if (valid) Debug.Log(report);
        else Debug.LogError(report);
    }

    public static string BuildValidationReport(out bool valid)
    {
        List<string> errors = new List<string>();
        List<string> details = new List<string>();

        if (SceneManager.GetActiveScene().path != SweepSceneCollisionSetup.SweepScenePath)
            EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);

        string wallReport = SweepSceneCollisionSetup.BuildValidationReport(out bool wallValid);
        if (!wallValid) errors.Add(wallReport);

        string trashColliderReport = SweepColliderAutoFit.BuildValidationReport(out bool trashCollidersValid);
        if (!trashCollidersValid) errors.Add(trashColliderReport);

        ValidateSceneTuning(errors, details);
        string standingReport = Sweep25DVisualSetup.BuildValidationReport(out bool standingValid);
        if (!standingValid) errors.Add(standingReport);
        else details.Add(standingReport);
        string resultReport = SweepResultScreenSetup.BuildValidationReport(out bool resultValid);
        if (!resultValid) errors.Add(resultReport);
        else details.Add(resultReport);
        ValidateBlackHolePrefab(errors, details);
        ValidateRenderPipeline(errors, details);
        ValidateImporters(errors, details);

        valid = errors.Count == 0;
        string header = $"[SweepPresentationOptimizer] {(valid ? "PASS" : "FAIL")}";
        if (details.Count > 0) header += "\n" + string.Join("\n", details);
        if (errors.Count > 0) header += "\n---\n" + string.Join("\n", errors);
        return header;
    }

    public static int GetTextureMaxSizeForPath(string path)
    {
        string normalized = path.Replace('\\', '/').ToLowerInvariant();
        bool isSceneryOrTrash = normalized.Contains("/sprites/trash/") ||
                                normalized.Contains("/sprites/object/");
        string[] keepSharpTokens =
        {
            "background", "floor", "ground", "wall", "牆", "地板", "broom", "掃把",
            "blackhole", "black_hole", "黑洞", "player", "human"
        };
        bool isHeroOrLargeScenery = keepSharpTokens.Any(token => normalized.Contains(token));

        // 只有大量同屏的垃圾與小型場景物件降到 1024；玩家動畫、黑洞、牆、
        // 全螢幕 UI 與其他不明用途圖片保留 2048，避免以效能之名傷到展演主視覺。
        return isSceneryOrTrash && !isHeroOrLargeScenery ? 1024 : 2048;
    }

    private static int OptimizeTextureImporters()
    {
        string[] paths = AssetDatabase.FindAssets("t:Texture2D", TextureRoots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => IsRasterTexture(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

                int maxSize = GetTextureMaxSizeForPath(path);
                importer.maxTextureSize = maxSize;
                importer.mipmapEnabled = false;
                importer.streamingMipmaps = false;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.crunchedCompression = true;
                importer.compressionQuality = 72;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                if (importer.DoesSourceTextureHaveAlpha()) importer.alphaIsTransparency = true;

                TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
                standalone.name = "Standalone";
                standalone.overridden = true;
                standalone.maxTextureSize = maxSize;
                standalone.format = TextureImporterFormat.Automatic;
                standalone.compressionQuality = 72;
                standalone.crunchedCompression = true;
                importer.SetPlatformTextureSettings(standalone);
                AssetDatabase.WriteImportSettingsIfDirty(path);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        return paths.Length;
    }

    private static int OptimizeAudioImporters()
    {
        string[] paths = AssetDatabase.FindAssets("t:AudioClip", AudioRoots)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                float length = clip != null ? clip.length : 0f;
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;

                if (length >= 20f)
                {
                    settings.loadType = AudioClipLoadType.Streaming;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0.68f;
                    settings.preloadAudioData = false;
                    importer.loadInBackground = true;
                }
                else if (length >= 5f)
                {
                    settings.loadType = AudioClipLoadType.CompressedInMemory;
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0.72f;
                    settings.preloadAudioData = false;
                    importer.loadInBackground = true;
                }
                else
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.compressionFormat = AudioCompressionFormat.ADPCM;
                    settings.quality = 0.8f;
                    settings.preloadAudioData = true;
                    importer.loadInBackground = false;
                }

                importer.defaultSampleSettings = settings;
                AssetDatabase.WriteImportSettingsIfDirty(path);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        return paths.Length;
    }

    private static void OptimizeRenderPipeline()
    {
        UnityEngine.Object pipeline = AssetDatabase.LoadMainAssetAtPath(RenderPipelinePath);
        if (pipeline == null) throw new InvalidOperationException($"Missing {RenderPipelinePath}");

        Undo.RecordObject(pipeline, "Optimize Sweep render pipeline");
        SerializedObject data = new SerializedObject(pipeline);
        SetBool(data, "m_RequireDepthTexture", false);
        SetBool(data, "m_RequireOpaqueTexture", false);
        SetBool(data, "m_SupportsHDR", false);
        SetInt(data, "m_MSAA", 1);
        SetBool(data, "m_MainLightShadowsSupported", false);
        SetInt(data, "m_AdditionalLightsRenderingMode", 0);
        SetBool(data, "m_AdditionalLightShadowsSupported", false);
        SetFloat(data, "m_ShadowDistance", 0f);
        SetInt(data, "m_ShadowCascadeCount", 1);
        SetBool(data, "m_SupportsLightCookies", false);
        SetBool(data, "m_SupportsLightLayers", false);
        data.ApplyModifiedProperties();
        EditorUtility.SetDirty(pipeline);
    }

    private static void TuneBlackHolePrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BlackHolePrefabPath);
        try
        {
            PetAI pet = root.GetComponentInChildren<PetAI>(true);
            BlackHoleObstacle obstacle = root.GetComponentInChildren<BlackHoleObstacle>(true);
            if (pet == null || obstacle == null)
                throw new InvalidOperationException("BlackHole prefab is missing PetAI or BlackHoleObstacle.");

            SerializedObject petData = new SerializedObject(pet);
            SetFloat(petData, "suckRadius", BlackHoleSuckRadius);
            SetFloat(petData, "suckForce", BlackHoleSuckForce);
            SetFloat(petData, "suckRadiusGrowthWeight", BlackHoleRadiusGrowthWeight);
            SetFloat(petData, "suckForceGrowthWeight", BlackHoleForceGrowthWeight);
            petData.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject obstacleData = new SerializedObject(obstacle);
            SetFloat(obstacleData, "absorbRadius", BlackHoleAbsorbRadius);
            obstacleData.ApplyModifiedPropertiesWithoutUndo();

            CircleCollider2D trigger = obstacle.GetComponent<CircleCollider2D>();
            if (trigger == null) trigger = obstacle.gameObject.AddComponent<CircleCollider2D>();
            trigger.isTrigger = true;
            trigger.radius = BlackHoleAbsorbRadius;
            EditorUtility.SetDirty(trigger);

            PrefabUtility.SaveAsPrefabAsset(root, BlackHolePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void TuneSweepScene()
    {
        PlayerController player = UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == SceneManager.GetActiveScene());
        if (player == null) throw new InvalidOperationException("Sweep scene player is missing.");

        SerializedObject playerData = new SerializedObject(player);
        SetFloat(playerData, "bodyCollisionRadius", PlayerBodyRadius);
        SetFloat(playerData, "sweepRadius", PlayerSweepRadius);
        SetFloat(playerData, "sweepForgivenessMultiplier", PlayerSweepForgiveness);
        SetFloat(playerData, "forwardReachOffset", PlayerForwardReach);
        playerData.ApplyModifiedProperties();

        CircleCollider2D body = player.GetComponents<CircleCollider2D>()
            .FirstOrDefault(collider => !collider.isTrigger);
        if (body == null) throw new InvalidOperationException("Player physical CircleCollider2D is missing.");
        Undo.RecordObject(body, "Tune Sweep player body collider");
        body.radius = PlayerBodyRadius;
        EditorUtility.SetDirty(body);

        SkillManager skills = player.GetComponent<SkillManager>();
        if (skills == null) throw new InvalidOperationException("Player SkillManager is missing.");
        SerializedObject skillData = new SerializedObject(skills);
        SetFloat(skillData, "magnetRadiusMultiplier", MagnetRadiusMultiplier);
        skillData.ApplyModifiedProperties();

        AudioManager audio = UnityEngine.Object.FindObjectsByType<AudioManager>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == SceneManager.GetActiveScene());
        if (audio != null)
        {
            SerializedObject audioData = new SerializedObject(audio);
            SetFloat(audioData, "masterVolume", 0.92f);
            SetFloat(audioData, "bgmBusVolume", 0.5f);
            SetFloat(audioData, "sfxBusVolume", 0.82f);
            SetFloat(audioData, "uiBusVolume", 0.72f);
            SetFloat(audioData, "maximumBgmDuck", 0.48f);
            SetFloat(audioData, "duckReleaseSeconds", 0.42f);
            audioData.ApplyModifiedProperties();
        }

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
        foreach (Camera camera in cameras.Where(item => item.gameObject.scene == SceneManager.GetActiveScene()))
        {
            Undo.RecordObject(camera, "Optimize Sweep camera");
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false;
            EditorUtility.SetDirty(camera);
        }

        Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
        foreach (Light light in lights.Where(item => item.gameObject.scene == SceneManager.GetActiveScene()))
        {
            Undo.RecordObject(light, "Disable unused Sweep shadows");
            light.shadows = LightShadows.None;
            EditorUtility.SetDirty(light);
        }

        SpriteRenderer[] sprites = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include);
        foreach (SpriteRenderer sprite in sprites.Where(item => item.gameObject.scene == SceneManager.GetActiveScene()))
        {
            Undo.RecordObject(sprite, "Optimize Sweep sprite renderer");
            sprite.shadowCastingMode = ShadowCastingMode.Off;
            sprite.receiveShadows = false;
            sprite.lightProbeUsage = LightProbeUsage.Off;
            sprite.reflectionProbeUsage = ReflectionProbeUsage.Off;
            sprite.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            EditorUtility.SetDirty(sprite);
        }
    }

    private static void ValidateSceneTuning(ICollection<string> errors, ICollection<string> details)
    {
        PlayerController player = UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == SceneManager.GetActiveScene());
        if (player == null)
        {
            errors.Add("Sweep scene player is missing.");
            return;
        }

        SerializedObject playerData = new SerializedObject(player);
        CheckFloat(playerData, "bodyCollisionRadius", PlayerBodyRadius, errors);
        CheckFloat(playerData, "sweepRadius", PlayerSweepRadius, errors);
        CheckFloat(playerData, "sweepForgivenessMultiplier", PlayerSweepForgiveness, errors);
        CheckFloat(playerData, "forwardReachOffset", PlayerForwardReach, errors);

        CircleCollider2D body = player.GetComponents<CircleCollider2D>().FirstOrDefault(collider => !collider.isTrigger);
        if (body == null || Mathf.Abs(body.radius - PlayerBodyRadius) > 0.001f)
            errors.Add($"Player body collider radius is not {PlayerBodyRadius:F2}.");

        SkillManager skills = player.GetComponent<SkillManager>();
        if (skills == null) errors.Add("Player SkillManager is missing.");
        else CheckFloat(new SerializedObject(skills), "magnetRadiusMultiplier", MagnetRadiusMultiplier, errors);

        Camera camera = Camera.main;
        if (camera == null || camera.allowHDR || camera.allowMSAA || camera.useOcclusionCulling)
            errors.Add("Sweep camera still has HDR, MSAA, or occlusion culling enabled.");

        details.Add($"handFeel: body={PlayerBodyRadius:F2} sweep={PlayerSweepRadius:F2} " +
                    $"effectiveSweep={(PlayerSweepRadius * PlayerSweepForgiveness):F3} " +
                    $"magnet={(PlayerSweepRadius * PlayerSweepForgiveness * MagnetRadiusMultiplier):F3}");
    }

    private static void ValidateBlackHolePrefab(ICollection<string> errors, ICollection<string> details)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlackHolePrefabPath);
        if (prefab == null)
        {
            errors.Add($"Missing {BlackHolePrefabPath}");
            return;
        }

        PetAI pet = prefab.GetComponentInChildren<PetAI>(true);
        BlackHoleObstacle obstacle = prefab.GetComponentInChildren<BlackHoleObstacle>(true);
        if (pet == null || obstacle == null)
        {
            errors.Add("BlackHole prefab is missing PetAI or BlackHoleObstacle.");
            return;
        }

        SerializedObject petData = new SerializedObject(pet);
        CheckFloat(petData, "suckRadius", BlackHoleSuckRadius, errors);
        CheckFloat(petData, "suckForce", BlackHoleSuckForce, errors);
        CheckFloat(petData, "suckRadiusGrowthWeight", BlackHoleRadiusGrowthWeight, errors);
        CheckFloat(petData, "suckForceGrowthWeight", BlackHoleForceGrowthWeight, errors);
        CheckFloat(new SerializedObject(obstacle), "absorbRadius", BlackHoleAbsorbRadius, errors);

        CircleCollider2D trigger = obstacle.GetComponent<CircleCollider2D>();
        if (trigger == null || !trigger.isTrigger || Mathf.Abs(trigger.radius - BlackHoleAbsorbRadius) > 0.001f)
            errors.Add("BlackHole trigger does not match the tuned absorb radius.");

        float maxRadius = PetAI.CalculateScaledAbility(
            BlackHoleSuckRadius, 2.7f, BlackHoleRadiusGrowthWeight);
        float maxForce = PetAI.CalculateScaledAbility(
            BlackHoleSuckForce, 2.7f, BlackHoleForceGrowthWeight);
        details.Add($"blackHole: absorb={BlackHoleAbsorbRadius:F2} suck={BlackHoleSuckRadius:F2}->{maxRadius:F2} " +
                    $"force={BlackHoleSuckForce:F1}->{maxForce:F1}");
    }

    private static void ValidateRenderPipeline(ICollection<string> errors, ICollection<string> details)
    {
        UnityEngine.Object pipeline = AssetDatabase.LoadMainAssetAtPath(RenderPipelinePath);
        if (pipeline == null)
        {
            errors.Add($"Missing {RenderPipelinePath}");
            return;
        }

        SerializedObject data = new SerializedObject(pipeline);
        string[] disabledBooleans =
        {
            "m_RequireDepthTexture", "m_RequireOpaqueTexture", "m_SupportsHDR",
            "m_MainLightShadowsSupported", "m_AdditionalLightShadowsSupported",
            "m_SupportsLightCookies", "m_SupportsLightLayers"
        };
        foreach (string propertyName in disabledBooleans)
        {
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null || property.boolValue)
                errors.Add($"Render pipeline {propertyName} is not disabled.");
        }
        SerializedProperty additionalLights = data.FindProperty("m_AdditionalLightsRenderingMode");
        if (additionalLights == null || additionalLights.intValue != 0)
            errors.Add("Render pipeline additional lights are not disabled.");
        details.Add("render: HDR/depth/opaque/shadows/additional-lights/cookies/layers disabled");
    }

    private static void ValidateImporters(ICollection<string> errors, ICollection<string> details)
    {
        string[] texturePaths = AssetDatabase.FindAssets("t:Texture2D", TextureRoots)
            .Select(AssetDatabase.GUIDToAssetPath).Where(IsRasterTexture).Distinct().ToArray();
        foreach (string path in texturePaths)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
            int expected = GetTextureMaxSizeForPath(path);
            TextureImporterPlatformSettings standalone = importer.GetPlatformTextureSettings("Standalone");
            if (importer.mipmapEnabled || importer.maxTextureSize > expected ||
                !standalone.overridden || standalone.maxTextureSize > expected)
            {
                errors.Add($"Texture importer is not optimized: {path}");
            }
        }

        string[] audioPaths = AssetDatabase.FindAssets("t:AudioClip", AudioRoots)
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().ToArray();
        foreach (string path in audioPaths)
        {
            if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            AudioClipLoadType expected = clip != null && clip.length >= 20f
                ? AudioClipLoadType.Streaming
                : clip != null && clip.length >= 5f
                    ? AudioClipLoadType.CompressedInMemory
                    : AudioClipLoadType.DecompressOnLoad;
            if (importer.defaultSampleSettings.loadType != expected)
                errors.Add($"Audio importer has wrong load type: {path}");
        }
        details.Add($"importers: textures={texturePaths.Length} audio={audioPaths.Length}");
    }

    private static bool IsRasterTexture(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".png" || extension == ".jpg" || extension == ".jpeg" ||
               extension == ".tga" || extension == ".psd";
    }

    private static void SetBool(SerializedObject data, string propertyName, bool value)
    {
        SerializedProperty property = RequireProperty(data, propertyName);
        property.boolValue = value;
    }

    private static void SetInt(SerializedObject data, string propertyName, int value)
    {
        SerializedProperty property = RequireProperty(data, propertyName);
        property.intValue = value;
    }

    private static void SetFloat(SerializedObject data, string propertyName, float value)
    {
        SerializedProperty property = RequireProperty(data, propertyName);
        property.floatValue = value;
    }

    private static SerializedProperty RequireProperty(SerializedObject data, string propertyName)
    {
        SerializedProperty property = data.FindProperty(propertyName);
        if (property == null)
            throw new InvalidOperationException($"{data.targetObject.name} is missing serialized property {propertyName}.");
        return property;
    }

    private static void CheckFloat(
        SerializedObject data,
        string propertyName,
        float expected,
        ICollection<string> errors)
    {
        SerializedProperty property = data.FindProperty(propertyName);
        if (property == null || Mathf.Abs(property.floatValue - expected) > 0.001f)
            errors.Add($"{data.targetObject.name}.{propertyName} is not {expected:F3}.");
    }
}
