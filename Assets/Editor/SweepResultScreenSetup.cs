using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 結算畫面：評級收成 C／B／A／S 四種（各自不同的圖與巫婆台詞），右邊重試按鈕換成重試圖示。
/// 經 Editor API 寫入場景，不手改 YAML。分數門檻的理由：
/// 掛機約 16～28 分 → C；稍微有在掃（barry 試玩一局 64）→ B；穩定推進黑洞、有連段 → A；
/// 長時間連段＋滿蓄力＋FINAL FEVER 才到 S（腳本示範局約 220）。
/// </summary>
public static class SweepResultScreenSetup
{
    public const string RetrySpritePath = "Assets/barry/Sweep/Sprites/retry_round.png";
    public const string RetryStyleSourcePath = "Assets/barry/Sweep/Sprites/next.png";
    public const string DialoguePath = "witch/Text/text";

    public struct Tier
    {
        public string Name;
        public int MinScore;
        public string SpritePath;
        public string Line;
    }

    public static readonly Tier[] Tiers =
    {
        new Tier { Name = "C", MinScore = 0, SpritePath = "Assets/barry/Sweep/Sprites/limit/C.png", Line = "還有好多垃圾耶…" },
        new Tier { Name = "B", MinScore = 50, SpritePath = "Assets/barry/Sweep/Sprites/limit/B.png", Line = "總算乾淨啦～" },
        new Tier { Name = "A", MinScore = 110, SpritePath = "Assets/barry/Sweep/Sprites/limit/A.png", Line = "好乾淨！做得好！" },
        new Tier { Name = "S", MinScore = 180, SpritePath = "Assets/barry/Sweep/Sprites/limit/S.png", Line = "閃閃發亮！完美！" }
    };

    public static void ApplyToActiveScene()
    {
        Sprite retry = EnsureRetrySprite();
        UIManager ui = FindUiManager();
        if (ui == null) throw new InvalidOperationException("Sweep UIManager is missing.");

        SerializedObject data = new SerializedObject(ui);
        SerializedProperty grades = data.FindProperty("gradeSettings");
        grades.arraySize = Tiers.Length;
        for (int i = 0; i < Tiers.Length; i++)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Tiers[i].SpritePath);
            if (sprite == null) throw new InvalidOperationException($"Missing grade sprite {Tiers[i].SpritePath}");
            SerializedProperty element = grades.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("minScore").intValue = Tiers[i].MinScore;
            element.FindPropertyRelative("gradeSprite").objectReferenceValue = sprite;
            element.FindPropertyRelative("line").stringValue = Tiers[i].Line;
        }

        TMP_Text dialogue = FindDialogue(data);
        if (dialogue == null) throw new InvalidOperationException($"End panel dialogue text ({DialoguePath}) is missing.");
        data.FindProperty("endDialogueText").objectReferenceValue = dialogue;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(ui);

        // 台詞最長 8 個字：放大文字框、開自動縮放，兩行也收得進對話框。
        Undo.RecordObject(dialogue, "Fit Sweep result dialogue");
        Undo.RecordObject(dialogue.rectTransform, "Fit Sweep result dialogue");
        dialogue.rectTransform.sizeDelta = new Vector2(240f, 110f);
        // 舊場景的負邊距會把文字撐出對話框；保留台詞與尺寸，只清除負邊距。
        dialogue.margin = Vector4.zero;
        dialogue.enableAutoSizing = true;
        dialogue.fontSizeMin = 22f;
        dialogue.fontSizeMax = 36f;
        dialogue.text = Tiers[1].Line;
        EditorUtility.SetDirty(dialogue);
        EditorUtility.SetDirty(dialogue.rectTransform);

        Button restart = data.FindProperty("endRestartButton").objectReferenceValue as Button;
        Image restartImage = restart != null ? restart.GetComponent<Image>() : null;
        if (restartImage == null) throw new InvalidOperationException("End restart button image is missing.");
        Undo.RecordObject(restartImage, "Use retry icon on the result screen");
        restartImage.sprite = retry;
        EditorUtility.SetDirty(restartImage);
    }

    public static string BuildValidationReport(out bool valid)
    {
        List<string> errors = new List<string>();
        UIManager ui = FindUiManager();
        if (ui == null)
        {
            valid = false;
            return "[SweepResultScreenSetup] FAIL\nSweep UIManager is missing.";
        }

        SerializedObject data = new SerializedObject(ui);
        SerializedProperty grades = data.FindProperty("gradeSettings");
        if (grades.arraySize != Tiers.Length) errors.Add($"Result has {grades.arraySize} grades, expected {Tiers.Length} (C/B/A/S).");
        HashSet<UnityEngine.Object> sprites = new HashSet<UnityEngine.Object>();
        int previous = int.MinValue;
        for (int i = 0; i < grades.arraySize; i++)
        {
            SerializedProperty element = grades.GetArrayElementAtIndex(i);
            int minScore = element.FindPropertyRelative("minScore").intValue;
            UnityEngine.Object sprite = element.FindPropertyRelative("gradeSprite").objectReferenceValue;
            string line = element.FindPropertyRelative("line").stringValue;
            if (minScore <= previous) errors.Add("Grade thresholds must strictly increase.");
            previous = minScore;
            if (sprite == null || !sprites.Add(sprite)) errors.Add($"Grade {i} has a missing or repeated sprite.");
            if (string.IsNullOrWhiteSpace(line)) errors.Add($"Grade {i} has no dialogue line.");
            if (i < Tiers.Length && sprite != null && AssetDatabase.GetAssetPath(sprite) != Tiers[i].SpritePath)
                errors.Add($"Grade {i} should use {Tiers[i].SpritePath}.");
        }
        if (grades.arraySize > 0 && grades.GetArrayElementAtIndex(0).FindPropertyRelative("minScore").intValue > 0)
            errors.Add("Lowest grade must start at 0 so every finished round gets a grade.");

        TMP_Text resultDialogue = data.FindProperty("endDialogueText").objectReferenceValue as TMP_Text;
        if (resultDialogue != null && resultDialogue.margin != Vector4.zero)
            errors.Add("Result dialogue margins must be zero to stay inside the speech bubble.");

        if (data.FindProperty("endDialogueText").objectReferenceValue == null)
            errors.Add("End dialogue text is not bound, the witch would always say the same line.");

        Button restart = data.FindProperty("endRestartButton").objectReferenceValue as Button;
        Image restartImage = restart != null ? restart.GetComponent<Image>() : null;
        if (restartImage == null || AssetDatabase.GetAssetPath(restartImage.sprite) != RetrySpritePath)
            errors.Add("Result restart button does not use the retry icon.");

        valid = errors.Count == 0;
        string report = $"[SweepResultScreenSetup] {(valid ? "PASS" : "FAIL")} grades=" +
                        string.Join("/", Tiers.Select(tier => $"{tier.Name}≥{tier.MinScore}"));
        if (errors.Count > 0) report += "\n---\n" + string.Join("\n", errors);
        return report;
    }

    private static TMP_Text FindDialogue(SerializedObject uiData)
    {
        GameObject panel = uiData.FindProperty("endPanel").objectReferenceValue as GameObject;
        Transform text = panel != null ? panel.transform.Find(DialoguePath) : null;
        return text != null ? text.GetComponent<TMP_Text>() : null;
    }

    /// <summary>重試圖示沿用「下一步」按鈕的匯入設定，並符合 SweepPresentationOptimizer 的 importer 標準。</summary>
    private static Sprite EnsureRetrySprite()
    {
        AssetDatabase.ImportAsset(RetrySpritePath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter source = AssetImporter.GetAtPath(RetryStyleSourcePath) as TextureImporter;
        TextureImporter target = AssetImporter.GetAtPath(RetrySpritePath) as TextureImporter;
        if (source == null || target == null) throw new InvalidOperationException($"Cannot configure {RetrySpritePath}.");

        TextureImporterSettings settings = new TextureImporterSettings();
        source.ReadTextureSettings(settings);
        target.SetTextureSettings(settings);
        target.textureType = TextureImporterType.Sprite;
        target.spriteImportMode = SpriteImportMode.Single;
        target.spritePixelsPerUnit = source.spritePixelsPerUnit;
        target.mipmapEnabled = false;
        int maxSize = SweepPresentationOptimizer.GetTextureMaxSizeForPath(RetrySpritePath);
        target.maxTextureSize = maxSize;
        TextureImporterPlatformSettings standalone = target.GetPlatformTextureSettings("Standalone");
        standalone.name = "Standalone";
        standalone.overridden = true;
        standalone.maxTextureSize = maxSize;
        standalone.format = TextureImporterFormat.Automatic;
        standalone.compressionQuality = 72;
        target.SetPlatformTextureSettings(standalone);
        target.SaveAndReimport();

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(RetrySpritePath);
        if (sprite == null) throw new InvalidOperationException($"Retry sprite failed to import: {RetrySpritePath}");
        return sprite;
    }

    private static UIManager FindUiManager()
    {
        Scene active = SceneManager.GetActiveScene();
        return UnityEngine.Object.FindObjectsByType<UIManager>(FindObjectsInactive.Include)
            .FirstOrDefault(item => item != null && item.gameObject.scene == active);
    }
}
