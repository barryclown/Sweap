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
    private const int CaptureCanvasSortingOffset = 1000;
    private static bool _started;
    private static bool _canvasSortingRaised;

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

    /// <summary>一張幾何驗收圖：玩家放在房間內的哪個相對位置（-1=左/下牆邊、0=中、1=右/上牆邊）。</summary>
    private readonly struct Shot
    {
        public readonly string Suffix;
        public readonly int AnchorX;
        public readonly int AnchorY;
        public readonly int Width;
        public readonly int Height;

        public Shot(string suffix, int anchorX, int anchorY, int width = 1600, int height = 900)
        {
            Suffix = suffix;
            AnchorX = anchorX;
            AnchorY = anchorY;
            Width = width;
            Height = height;
        }
    }

    private static readonly Shot[] BaseShots =
    {
        new Shot("-Base-Center", 0, 0),
        new Shot("-Base-BackWall", 0, 1),
        new Shot("-Base-BottomWall", 0, -1),
        new Shot("-Base-LeftWall", -1, 0),
        new Shot("-Base-BottomRightCorner", 1, -1)
    };

    private static readonly Shot[] MaxGrowthShots =
    {
        new Shot("-BackWall", 0, 1),
        new Shot("-BottomWall", 0, -1),
        new Shot("-BottomWall-21x9", 0, -1, 2100, 900),
        new Shot("-BottomLeftCorner-21x9", -1, -1, 2100, 900),
        new Shot("-LeftWall-21x9", -1, 0, 2100, 900),
        new Shot("-TopRightCorner", 1, 1)
    };

    public static string[] ExpectedSuffixes
    {
        get
        {
            string[] suffixes = new string[1 + BaseShots.Length + MaxGrowthShots.Length];
            suffixes[0] = string.Empty;
            for (int i = 0; i < BaseShots.Length; i++) suffixes[1 + i] = BaseShots[i].Suffix;
            for (int i = 0; i < MaxGrowthShots.Length; i++) suffixes[1 + BaseShots.Length + i] = MaxGrowthShots[i].Suffix;
            System.Array.Resize(ref suffixes, suffixes.Length + 1 + ResultShots.Length);
            suffixes[suffixes.Length - 1 - ResultShots.Length] = BehindDeskSuffix;
            for (int i = 0; i < ResultShots.Length; i++)
                suffixes[suffixes.Length - ResultShots.Length + i] = ResultShots[i].Suffix;
            return suffixes;
        }
    }

    private const string BehindDeskSuffix = "-Base-BehindDesk";

    // 結算畫面四種評級：分數分別落在 C／B／A／S。
    private static readonly (string Suffix, int Score)[] ResultShots =
    {
        ("-Result-C", 22), ("-Result-B", 64), ("-Result-A", 140), ("-Result-S", 230)
    };

    private IEnumerator Start()
    {
        yield return null;
        yield return new WaitForSecondsRealtime(0.4f);

        string outputPath = GetArgumentValue(PathArgument);
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = Path.Combine(Application.persistentDataPath, "Sweep-Presentation-MaxGrowth.png");
        outputPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        PlayerController player = PlayerController.instance;
        DemoFeedbackOverlay overlay = FindAnyObjectByType<DemoFeedbackOverlay>();
        overlay?.SetPacing(0, false);
        player?.SetDemoIntensity(0, false);

        // 1) 初始大小：一般遊玩時的牆面比例。
        foreach (Shot shot in BaseShots)
        {
            MovePlayerToAnchor(player, shot.AnchorX, shot.AnchorY);
            yield return new WaitForSecondsRealtime(1.1f);
            if (!TryRender(WithSuffix(outputPath, shot.Suffix), shot.Width, shot.Height))
            {
                Application.Quit(2);
                yield break;
            }
        }

        // 1b) 前後遮擋：掃把站在書桌正後方，書桌要擋住刷頭。
        if (MovePlayerBehindFurniture(player))
        {
            yield return new WaitForSecondsRealtime(1.1f);
            if (!TryRender(WithSuffix(outputPath, BehindDeskSuffix)))
            {
                Application.Quit(2);
                yield break;
            }
        }
        else
        {
            Debug.LogError("[SweepPresentationCapture] No free-standing furniture found for the occlusion shot.");
            Application.Quit(2);
            yield break;
        }

        // 2) 最大成長的高潮畫面（保留原本的簽收圖）。
        MovePlayerToAnchor(player, 0, 0);
        if (player != null)
        {
            MethodInfo grow = typeof(PlayerController).GetMethod(
                "HandlePetGrowth",
                BindingFlags.Instance | BindingFlags.NonPublic);
            grow?.Invoke(player, new object[] { 2.7f });
        }
        yield return new WaitForSecondsRealtime(2.2f);

        UIManager ui = FindAnyObjectByType<UIManager>();
        ui?.ShowPresentationCaptureClimax();
        BlackHoleObstacle blackHole = FindAnyObjectByType<BlackHoleObstacle>();
        Vector2 burstPosition = blackHole != null ? blackHole.CenterPos : Vector2.zero;
        DemoWorldBurst.Play(burstPosition, true, 15, true);
        yield return new WaitForSecondsRealtime(0.18f);
        if (!TryRender(outputPath))
        {
            Application.Quit(2);
            yield break;
        }

        // 3) 最大成長的幾何驗收：清掉全螢幕染色，讓房間外框本身看得清楚。
        overlay?.SetPacing(0, false);
        player?.SetDemoIntensity(0, false);
        yield return new WaitForSecondsRealtime(1.4f);
        foreach (Shot shot in MaxGrowthShots)
        {
            MovePlayerToAnchor(player, shot.AnchorX, shot.AnchorY);
            yield return new WaitForSecondsRealtime(1.3f);
            if (!TryRender(WithSuffix(outputPath, shot.Suffix), shot.Width, shot.Height))
            {
                Application.Quit(2);
                yield break;
            }
        }

        // 4) 結算畫面：每種評級各拍一張，確認圖、台詞、字型與重試按鈕。
        UIManager resultUi = FindAnyObjectByType<UIManager>();
        for (int i = 0; i < ResultShots.Length && resultUi != null; i++)
        {
            string gradeName = resultUi.PreviewResultForCapture(ResultShots[i].Score);
            Debug.Log($"[SweepPresentationCapture] RESULT score={ResultShots[i].Score} grade={gradeName}");
            yield return new WaitForSecondsRealtime(0.35f);
            if (!TryRender(WithSuffix(outputPath, ResultShots[i].Suffix)))
            {
                Application.Quit(2);
                yield break;
            }
        }

        Debug.Log($"[SweepPresentationCapture] SWEEP_PRESENTATION_CAPTURE_OK climax={outputPath} " +
                  $"shots={BaseShots.Length + MaxGrowthShots.Length}");
        Application.Quit(0);
    }

    private static string WithSuffix(string path, string suffix)
    {
        return Path.Combine(
            Path.GetDirectoryName(path) ?? string.Empty,
            Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path));
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

    private static bool TryRender(string outputPath, int width = 1600, int height = 900)
    {
        try
        {
            RenderCameraAndUi(outputPath, width, height);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"[SweepPresentationCapture] Screenshot was not written: {outputPath}");
            return false;
        }
    }

    /// <summary>挑離房間中心最近、朝鏡頭立起的家具，把玩家放在它正後方（北邊）。</summary>
    private static bool MovePlayerBehindFurniture(PlayerController player)
    {
        if (player == null || WorldBounds2D.Instance == null) return false;
        Vector2 center = WorldBounds2D.Instance.GetWorldRect().center;
        Stand25D best = null;
        float bestDistance = float.MaxValue;
        Stand25D[] stands = FindObjectsByType<Stand25D>(FindObjectsInactive.Exclude);
        for (int i = 0; i < stands.Length; i++)
        {
            Stand25D stand = stands[i];
            if (!stand.IsStatic || stand.TiltDegrees < -80f) continue;
            float distance = ((Vector2)stand.transform.position - center).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = stand;
            }
        }
        if (best == null) return false;

        // 掃把的腳（刷頭）在掃擊中心，所以用掃擊中心而不是物理中心來決定「站在書桌後面」。
        Vector3 ground = best.transform.TransformPoint(best.VisibleBaseLocal);
        Vector2 feetOffset = player.GetSweepCenter() - (Vector2)player.transform.position;
        Vector2 target = new Vector2(ground.x, ground.y + 1.2f) - feetOffset;
        Rigidbody2D body = player.rb != null ? player.rb : player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = target;
            body.linearVelocity = Vector2.zero;
        }
        player.transform.position = new Vector3(target.x, target.y, player.transform.position.z);
        Debug.Log($"[SweepPresentationCapture] BEHIND_FURNITURE furniture={best.name} ground={ground} player={target}");
        return true;
    }

    /// <summary>把玩家貼到牆邊（留 2 單位，碰撞半徑在最大成長時約 1.8）。</summary>
    private static void MovePlayerToAnchor(PlayerController player, int anchorX, int anchorY)
    {
        if (player == null || WorldBounds2D.Instance == null) return;
        const float inset = 2f;
        Rect rect = WorldBounds2D.Instance.GetWorldRect();
        float x = anchorX < 0 ? rect.xMin + inset : anchorX > 0 ? rect.xMax - inset : rect.center.x;
        float y = anchorY < 0 ? rect.yMin + inset : anchorY > 0 ? rect.yMax - inset : rect.center.y;
        Vector2 target = new Vector2(x, y);
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
        // 實機 UI 是 Overlay、永遠在最上層；離屏截圖改成 Camera 模式後要把排序墊高，
        // 否則會被前牆（sorting 200）蓋住，截出實機不存在的畫面。
        if (!_canvasSortingRaised)
        {
            _canvasSortingRaised = true;
            for (int i = 0; i < canvases.Length; i++)
                if (canvases[i].isRootCanvas) canvases[i].sortingOrder += CaptureCanvasSortingOffset;
        }
        Canvas.ForceUpdateCanvases();

        RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        float previousAspect = camera.aspect;
        Vector3 previousCameraPosition = camera.transform.position;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            PlayerController player = PlayerController.instance;
            if (player != null)
            {
                camera.transform.position = PlayerController.ClampCameraToRoom(
                    camera, camera.transform.position, player.transform.position);
            }
            LogShot(outputPath, camera, player);
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
            camera.aspect = previousAspect;
            camera.transform.position = previousCameraPosition;
            RenderTexture.active = previousActive;
            if (image != null) Destroy(image);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>每張圖記下玩家、鏡頭與玩家圖像最低點，作為「前牆不會蓋到玩家」的數據證據。</summary>
    private static void LogShot(string outputPath, Camera camera, PlayerController player)
    {
        if (player == null) return;
        Vector3 position = player.transform.position;
        float spriteMinY = float.PositiveInfinity;
        SpriteRenderer[] renderers = player.GetComponentsInChildren<SpriteRenderer>(false);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].sprite == null || renderers[i].sortingOrder < 5) continue;
            spriteMinY = Mathf.Min(spriteMinY, renderers[i].bounds.min.y);
        }
        Vector3 viewport = camera.WorldToViewportPoint(position);
        SweepRoomFrame frame = SweepRoomFrame.Instance;
        Rect bounds = WorldBounds2D.Instance != null ? WorldBounds2D.Instance.GetWorldRect() : default;
        Debug.Log($"[SweepPresentationCapture] SHOT {Path.GetFileName(outputPath)} player={position} " +
                  $"spriteMinY={spriteMinY:F2} viewport=({viewport.x:F2},{viewport.y:F2}) camera={camera.transform.position} " +
                  $"scale={player.transform.localScale.x:F2} boundsYMin={bounds.yMin:F2} " +
                  $"frontLine={(frame != null ? frame.FrontWallLineY : float.NaN):F2}");
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
