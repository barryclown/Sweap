using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class SweepCursorLayerTests
{
    [TestCase("pausePanel")]
    [TestCase("teachPanel")]
    [TestCase("endPanel")]
    [TestCase("runtimeOverlay")]
    public void CursorStaysAbovePanelsWithoutBlockingClicks(string layer)
    {
        EditorSceneManager.OpenScene(SweepSceneCollisionSetup.SweepScenePath, OpenSceneMode.Single);
        UIManager ui = UnityEngine.Object.FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        SerializedObject data = new SerializedObject(ui);
        RectTransform cursor = (RectTransform)data.FindProperty("customCursorUI").objectReferenceValue;
        GameObject panel;
        if (layer == "runtimeOverlay")
        {
            panel = new GameObject("Cursor test runtime overlay", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(cursor.parent, false);
        }
        else
        {
            panel = (GameObject)data.FindProperty(layer).objectReferenceValue;
            panel.SetActive(true);
        }
        panel.transform.SetAsLastSibling();
        if (layer == "endPanel")
        {
            Assert.That(ui.PreviewResultForCapture(286), Is.EqualTo("S"));
            Button retry = (Button)data.FindProperty("endRestartButton").objectReferenceValue;
            Canvas.ForceUpdateCanvases();
            cursor.position = retry.transform.position;
        }

        // Reproduce panels moving above the cursor, then run the actual frame hook.
        // Absence of the hook reproduces the original defect instead of aborting.
        typeof(UIManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(ui, null);
        Canvas.ForceUpdateCanvases();

        string capture = Environment.GetEnvironmentVariable("SWEEP_CURSOR_CAPTURE_PATH");
        if (layer == "endPanel" && !string.IsNullOrEmpty(capture))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(capture));
            CaptureEditorResult(capture);
        }

        Debug.Log($"CURSOR_LAYER_PROBE layer={layer} cursor={cursor.GetSiblingIndex()} panel={panel.transform.GetSiblingIndex()} raycast={cursor.GetComponent<Graphic>().raycastTarget}");
        Assert.That(cursor.parent, Is.EqualTo(panel.transform.parent));
        Assert.That(cursor.GetSiblingIndex(), Is.GreaterThan(panel.transform.GetSiblingIndex()), layer);
        Assert.That(cursor.GetComponent<Graphic>().raycastTarget, Is.False,
            "The topmost cursor must not intercept retry/home/menu clicks.");
    }

    // Optional offscreen evidence in the isolated Editor fixture, never a player
    // feature. Use immediate cleanup because these tests run outside Play mode.
    private static void CaptureEditorResult(string path)
    {
        Camera camera = Camera.main;
        foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            if (canvas.isRootCanvas) canvas.sortingOrder += 1000;
        }
        Canvas.ForceUpdateCanvases();
        RenderTexture target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
        RenderTexture oldActive = RenderTexture.active;
        RenderTexture oldTarget = camera.targetTexture;
        float oldAspect = camera.aspect;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            camera.aspect = 1600f / 900f;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            camera.Render();
            image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0, false);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget;
            camera.aspect = oldAspect;
            RenderTexture.active = oldActive;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
