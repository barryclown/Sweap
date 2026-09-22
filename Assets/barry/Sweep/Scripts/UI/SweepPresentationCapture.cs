using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>只在自動驗收參數存在時啟動的真實 Player 截圖探針。</summary>
public sealed class SweepPresentationCapture : MonoBehaviour
{
    private const string CaptureArgument = "-sweepPresentationCapture";
    private const string PathArgument = "-sweepCapturePath";
    private static bool _started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_started || !HasArgument(CaptureArgument)) return;
        _started = true;

        if (!string.Equals(SceneManager.GetActiveScene().name, "Sweep", StringComparison.Ordinal))
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene("Sweep");
            return;
        }
        CreateRunner();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!string.Equals(scene.name, "Sweep", StringComparison.Ordinal)) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        CreateRunner();
    }

    private static void CreateRunner()
    {
        GameObject runner = new GameObject("[Validation] Sweep Presentation Capture");
        runner.hideFlags = HideFlags.DontSave;
        runner.AddComponent<SweepPresentationCapture>();
    }

    private IEnumerator Start()
    {
        yield return null;
        yield return new WaitForSecondsRealtime(0.4f);

        PlayerController player = PlayerController.instance;
        if (player != null)
        {
            MethodInfo grow = typeof(PlayerController).GetMethod(
                "HandlePetGrowth",
                BindingFlags.Instance | BindingFlags.NonPublic);
            grow?.Invoke(player, new object[] { 2.7f });
        }

        yield return new WaitForSecondsRealtime(1.1f);

        UIManager ui = FindAnyObjectByType<UIManager>();
        ui?.ShowPresentationCaptureClimax();
        BlackHoleObstacle blackHole = FindAnyObjectByType<BlackHoleObstacle>();
        Vector2 burstPosition = blackHole != null ? blackHole.CenterPos : Vector2.zero;
        DemoWorldBurst.Play(burstPosition, true, 15, true);

        yield return new WaitForSecondsRealtime(0.18f);

        string outputPath = GetArgumentValue(PathArgument);
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = Path.Combine(Application.persistentDataPath, "Sweep-Presentation-MaxGrowth.png");
        outputPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string bottomWallPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(outputPath) + "-BottomWall" + Path.GetExtension(outputPath));

        if (!TryRender(outputPath))
        {
            Application.Quit(2);
            yield break;
        }

        MovePlayerToBottomBoundary(player);
        yield return new WaitForSecondsRealtime(1.1f);
        ui?.ShowPresentationCaptureClimax();
        yield return new WaitForSecondsRealtime(0.18f);

        if (!TryRender(bottomWallPath))
        {
            Application.Quit(2);
            yield break;
        }

        Debug.Log($"[SweepPresentationCapture] SWEEP_PRESENTATION_CAPTURE_OK " +
                  $"climax={outputPath} bottomWall={bottomWallPath}");
        Application.Quit(0);
    }

    private static bool HasArgument(string expected)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], expected, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryRender(string outputPath)
    {
        try
        {
            RenderCameraAndUi(outputPath, 1600, 900);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"[SweepPresentationCapture] Screenshot was not written: {outputPath}");
            return false;
        }
    }

    private static void MovePlayerToBottomBoundary(PlayerController player)
    {
        if (player == null) return;
        WorldBounds2D[] bounds = FindObjectsByType<WorldBounds2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        WorldBounds2D main = null;
        for (int i = 0; i < bounds.Length; i++)
        {
            if (bounds[i] != null && bounds[i].isMainWorldBoundary)
            {
                main = bounds[i];
                break;
            }
        }
        if (main == null) return;

        Rect rect = main.GetWorldRect();
        Vector2 target = new Vector2(rect.center.x, rect.yMin + 6f);
        Rigidbody2D body = player.rb != null ? player.rb : player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = target;
            body.linearVelocity = Vector2.zero;
        }
        player.transform.position = new Vector3(target.x, target.y, player.transform.position.z);
    }

    private static void RenderCameraAndUi(string outputPath, int width, int height)
    {
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Main Camera is missing.");

        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
            canvases[i].worldCamera = camera;
            canvases[i].planeDistance = 1f;
        }
        Canvas.ForceUpdateCanvases();

        RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            camera.Render();

            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            image.Apply(false, false);
            byte[] png = image.EncodeToPNG();
            if (png == null || png.Length < 1024)
                throw new InvalidOperationException("Offscreen renderer returned an empty frame.");
            File.WriteAllBytes(outputPath, png);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (image != null) Destroy(image);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static string GetArgumentValue(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase)) return arguments[i + 1];
        }
        return null;
    }
}
