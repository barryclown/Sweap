using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Command-line-only showcase recorder. It drives the real Sweep scene, keeps audio muted,
/// and writes deterministic off-screen JPG frames for the review video.
/// </summary>
public sealed class SweepShowcaseCapture : MonoBehaviour
{
    private const string CaptureArgument = "-sweepShowcaseCapture";
    private const string FramesArgument = "-sweepShowcaseFrames";
    private const string FpsArgument = "-sweepShowcaseFps";
    private const string SecondsArgument = "-sweepShowcaseSeconds";
    private const int CaptureWidth = 1280;
    private const int CaptureHeight = 720;

    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private static bool _started;

    private PlayerController _player;
    private BlackHoleObstacle _blackHole;
    private UIManager _ui;
    private DemoFeedbackOverlay _overlay;
    private WorldBounds2D _mainBounds;
    private FieldInfo _moveDirField;
    private FieldInfo _currentSpeedField;
    private MethodInfo _growMethod;
    private MethodInfo _toggleModeMethod;
    private float _blackHoleRadius;
    private bool _trashPrepared;
    private bool _chargeReleased;
    private bool _feverStarted;
    private bool _bottomReached;
    private const float RoomTourStartSeconds = 12.2f;
    private const float RoomTourLegSeconds = 4.45f;
    private const float RoomTourSpeed = 10f;
    private readonly Vector2[] _tourStarts = new Vector2[4];
    private readonly Vector2[] _tourDirections = new Vector2[4];
    private int _tourLeg = -1;
    private MethodInfo _resetCameraMethod;
    private SkillManager _skills;
    private Rigidbody2D _blackHoleBody;
    private bool _stickyCaptureLogged;
    private FieldInfo _cameraOffsetField;
    private static bool _canvasSortingRaised;
    private readonly List<BaseTrash> _activeTrash = new List<BaseTrash>(32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_started || !HasArgument(CaptureArgument)) return;
        _started = true;
        PlayerPrefs.SetInt("Dragame.Sweep.DemoHintsSeen.v1", 1);
        PlayerPrefs.Save();

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
        GameObject runner = new GameObject("[Validation] Sweep Showcase Capture");
        runner.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(runner);
        runner.AddComponent<SweepShowcaseCapture>();
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        QualitySettings.vSyncCount = 0;

        int fps = Mathf.Clamp(ParseIntArgument(FpsArgument, 30), 12, 60);
        float seconds = Mathf.Clamp(ParseFloatArgument(SecondsArgument, 16f), 6f, 30f);
        int frameCount = Mathf.CeilToInt(fps * seconds);
        Time.captureDeltaTime = 1f / fps;

        string framesPath = GetArgumentValue(FramesArgument);
        if (string.IsNullOrWhiteSpace(framesPath))
            framesPath = Path.Combine(Application.persistentDataPath, "Sweep-Showcase-Frames");
        framesPath = Path.GetFullPath(framesPath);
        Directory.CreateDirectory(framesPath);
        foreach (string staleFrame in Directory.GetFiles(framesPath, "frame-*.jpg"))
            File.Delete(staleFrame);

        yield return null;
        yield return new WaitForSecondsRealtime(0.35f);

        if (!BindScene())
        {
            Debug.LogError("[SweepShowcaseCapture] Required Sweep objects are missing.");
            Application.Quit(2);
            yield break;
        }

        PrepareOpening();

        for (int frame = 0; frame < frameCount; frame++)
        {
            yield return new WaitForEndOfFrame();
            DriveShowcase(frame, frameCount, fps);

            string output = Path.Combine(framesPath, $"frame-{frame:D5}.jpg");
            try
            {
                RenderCameraAndUi(output, CaptureWidth, CaptureHeight);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError($"[SweepShowcaseCapture] Frame failed: {output}");
                Application.Quit(3);
                yield break;
            }
        }

        string manifest = Path.Combine(framesPath, "capture.txt");
        File.WriteAllText(
            manifest,
            $"SWEEP_SHOWCASE_CAPTURE_OK{Environment.NewLine}" +
            $"frames={frameCount}{Environment.NewLine}" +
            $"fps={fps}{Environment.NewLine}" +
            $"seconds={seconds.ToString("0.###", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
            $"resolution={CaptureWidth}x{CaptureHeight}{Environment.NewLine}");

        Debug.Log($"[SweepShowcaseCapture] SWEEP_SHOWCASE_CAPTURE_OK " +
                  $"frames={frameCount} fps={fps} seconds={seconds:0.###} path={framesPath}");
        Time.captureDeltaTime = 0f;
        Application.Quit(0);
    }

    private bool BindScene()
    {
        _player = PlayerController.instance;
        _blackHole = FindAnyObjectByType<BlackHoleObstacle>();
        _ui = FindAnyObjectByType<UIManager>();
        _overlay = FindAnyObjectByType<DemoFeedbackOverlay>();

        WorldBounds2D[] bounds = FindObjectsByType<WorldBounds2D>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < bounds.Length; i++)
        {
            if (bounds[i] != null && bounds[i].isMainWorldBoundary)
            {
                _mainBounds = bounds[i];
                break;
            }
        }

        if (_player == null || _blackHole == null || _ui == null || _mainBounds == null)
            return false;

        _moveDirField = typeof(PlayerController).GetField("moveDir", PrivateInstance);
        _currentSpeedField = typeof(PlayerController).GetField("currentSpeed", PrivateInstance);
        _growMethod = typeof(PlayerController).GetMethod("HandlePetGrowth", PrivateInstance);
        _toggleModeMethod = typeof(PlayerController).GetMethod("ToggleSkillMode", PrivateInstance);
        _resetCameraMethod = typeof(PlayerController).GetMethod("ResetCameraMotion", PrivateInstance);
        _skills = _player.GetComponent<SkillManager>();
        _blackHoleBody = _blackHole.GetComponentInParent<Rigidbody2D>();
        _cameraOffsetField = typeof(PlayerController).GetField("cameraOffset", PrivateInstance);

        FieldInfo radiusField = typeof(BlackHoleObstacle).GetField("absorbRadius", PrivateInstance);
        _blackHoleRadius = radiusField != null
            ? Mathf.Max(1f, (float)radiusField.GetValue(_blackHole))
            : 7.8f;

        BaseTrash[] allTrash = FindObjectsByType<BaseTrash>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < allTrash.Length; i++)
        {
            BaseTrash trash = allTrash[i];
            if (trash != null && trash.gameObject.activeInHierarchy && !trash.IsAbsorbing)
                _activeTrash.Add(trash);
        }

        return _moveDirField != null && _currentSpeedField != null && _growMethod != null;
    }

    private void PrepareOpening()
    {
        Rect world = _mainBounds.GetWorldRect();
        Vector2 start = new Vector2(world.center.x - 16f, world.center.y - 1.5f);
        SetPlayerPosition(start);
        SetPlayerMotion(Vector2.right, 10.5f);
        _player.currentMode = BroomMode.Impact;
        _player.SetDemoIntensity(1, false);

        int count = Mathf.Min(10, _activeTrash.Count);
        for (int i = 0; i < count; i++)
        {
            float row = (i % 2 == 0) ? -1.15f : 1.15f;
            Vector2 position = start + new Vector2(4.5f + (i * 1.55f), row);
            SetTrashPosition(_activeTrash[i], position);
        }

    }

    private void DriveShowcase(int frame, int totalFrames, int fps)
    {
        float t = frame / (float)fps;

        if (t < 3.1f)
        {
            Vector2 direction = new Vector2(1f, Mathf.Sin(t * 2.2f) * 0.16f).normalized;
            SetPlayerMotion(direction, 10.8f);
            if (frame > 0 && frame % Mathf.Max(1, fps / 2) == 0)
                DemoWorldBurst.Play(_player.GetDynamicSweepCenter(), true, Mathf.Min(15, 2 + frame / 12), false);
        }
        else if (t < 6.6f)
        {
            // 吸附模式：掃過一排垃圾，垃圾被拉過來、繞著刷頭排成一團。
            if (!_trashPrepared)
            {
                _trashPrepared = true;
                PrepareStickySequence();
            }
            SetPlayerMotion(Vector2.right, 6.2f);
        }
        else if (t < 9.1f)
        {
            // 蓄力後把整團丟進黑洞。
            Vector2 origin = _player.GetSweepCenter();
            Vector2 toHole = (Vector2)_blackHole.CenterPos - origin;
            Vector2 direction = toHole.sqrMagnitude > 0.01f ? toHole.normalized : Vector2.right;
            if (!_chargeReleased) PlaceBlackHoleAhead(origin);
            SetPlayerMotion(Vector2.zero, 0f);
            float charge01 = Mathf.Clamp01((t - 6.6f) / 1.3f);
            if (!_chargeReleased) _player.effectManager?.ShowChargeSweep(origin, direction, charge01);

            if (charge01 >= 0.98f && !_chargeReleased)
            {
                _chargeReleased = true;
                int thrown = _skills != null ? _skills.ThrowCapturedTrash(direction, 1f) : 0;
                _player.effectManager?.ShowChargeSweep(origin, direction, 0f);
                _overlay?.ShowFullCharge();
                Debug.Log($"[SweepShowcaseCapture] STICKY_THROW thrown={thrown}");
            }
        }
        else if (t < 12.2f)
        {
            if (!_feverStarted)
            {
                _feverStarted = true;
                if (_player.currentMode == BroomMode.Sticky) _toggleModeMethod?.Invoke(_player, null);
                _growMethod.Invoke(_player, new object[] { 2.25f });
                _ui.ShowPresentationCaptureClimax();
            }

            Vector2 orbit = new Vector2(Mathf.Cos(t * 1.6f), Mathf.Sin(t * 1.6f) * 0.65f).normalized;
            SetPlayerMotion(orbit, 8.5f);
            if (frame % Mathf.Max(1, fps / 4) == 0)
                DemoWorldBurst.Play(_player.GetDynamicSweepCenter(), true, 15, true);
        }
        else
        {
            if (!_bottomReached)
            {
                _bottomReached = true;
                _growMethod.Invoke(_player, new object[] { 2.7f });
                // 繞房間時關掉 FINAL FEVER 全螢幕染色，牆面顏色才看得準。
                _overlay?.SetPacing(0, false);
                _player.SetDemoIntensity(0, false);
                BuildRoomTour();
            }

            DriveRoomTour(t - RoomTourStartSeconds);
        }
    }

    private void PrepareStickySequence()
    {
        if (_player.currentMode != BroomMode.Sticky)
            _toggleModeMethod?.Invoke(_player, null);

        Rect world = _mainBounds.GetWorldRect();
        Vector2 start = new Vector2(world.center.x - 14f, world.center.y - 4f);
        SetPlayerPosition(start);
        int first = Mathf.Min(10, _activeTrash.Count);
        int count = Mathf.Min(10, _activeTrash.Count - first);
        Vector2 head = start + (_player.GetSweepCenter() - (Vector2)_player.transform.position);
        for (int i = 0; i < count; i++)
        {
            float wave = (i % 2 == 0 ? 0.35f : -0.35f);
            SetTrashPosition(_activeTrash[first + i], head + new Vector2(2.4f + i * 1.7f, wave));
        }
        _stickyCaptureLogged = false;
    }

    private void PlaceBlackHoleAhead(Vector2 origin)
    {
        if (_blackHoleBody == null) return;
        Vector2 target = origin + new Vector2(9.5f, 3f);
        _blackHoleBody.position = target;
        _blackHoleBody.linearVelocity = Vector2.zero;
        _blackHoleBody.transform.position = new Vector3(target.x, target.y, _blackHoleBody.transform.position.z);
        if (!_stickyCaptureLogged && _skills != null)
        {
            _stickyCaptureLogged = true;
            Debug.Log($"[SweepShowcaseCapture] STICKY_CAPTURED count={_skills.CapturedCount}");
        }
    }

    private void MovePlayerToward(Vector2 target, float speed)
    {
        Vector2 delta = target - (Vector2)_player.transform.position;
        if (delta.sqrMagnitude <= 0.2f)
        {
            SetPlayerMotion(Vector2.zero, 0f);
            return;
        }

        SetPlayerMotion(delta.normalized, speed);
    }

    /// <summary>最大成長後沿四面牆各走一段：前牆→左牆→後牆（書櫃、櫃檯）→右牆，讓簽收片看得到整個房間外框。</summary>
    private void BuildRoomTour()
    {
        Rect world = _mainBounds.GetWorldRect();
        const float inset = 3f;
        _tourStarts[0] = new Vector2(world.xMin + 46f, world.yMin + inset);
        _tourDirections[0] = Vector2.left;
        _tourStarts[1] = new Vector2(world.xMin + inset, world.yMin + 10f);
        _tourDirections[1] = Vector2.up;
        _tourStarts[2] = new Vector2(world.xMin + 12f, world.yMax - inset);
        _tourDirections[2] = Vector2.right;
        _tourStarts[3] = new Vector2(world.xMax - inset, world.yMax - 46f);
        _tourDirections[3] = Vector2.up;
        _tourLeg = -1;
    }

    private void DriveRoomTour(float elapsed)
    {
        int leg = Mathf.Clamp(Mathf.FloorToInt(elapsed / RoomTourLegSeconds), 0, _tourStarts.Length - 1);
        if (leg != _tourLeg)
        {
            _tourLeg = leg;
            SetPlayerPosition(_tourStarts[leg]);
            SnapCameraToPlayer();
        }
        SetPlayerMotion(_tourDirections[leg], RoomTourSpeed);
    }

    /// <summary>瞬移後直接把鏡頭放到跟隨位置，避免平滑跟隨橫掃整個房間。</summary>
    private void SnapCameraToPlayer()
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        _resetCameraMethod?.Invoke(_player, null);
        Vector3 offset = _cameraOffsetField != null ? (Vector3)_cameraOffsetField.GetValue(_player) : new Vector3(0f, -7f, -7.5f);
        Vector3 desired = _player.transform.position + offset;
        camera.transform.position = PlayerController.ClampCameraToRoom(camera, desired, _player.transform.position);
    }

    private void SetPlayerMotion(Vector2 direction, float speed)
    {
        if (_player == null) return;
        Vector2 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
        _moveDirField.SetValue(_player, safeDirection);
        _currentSpeedField.SetValue(_player, Mathf.Max(0f, speed));
        if (_player.rb != null) _player.rb.linearVelocity = safeDirection * Mathf.Max(0f, speed);
    }

    private void SetPlayerPosition(Vector2 position)
    {
        if (_player.rb != null)
        {
            _player.rb.position = position;
            _player.rb.linearVelocity = Vector2.zero;
        }
        _player.transform.position = new Vector3(position.x, position.y, _player.transform.position.z);
    }

    private static void SetTrashPosition(BaseTrash trash, Vector2 position)
    {
        if (trash == null) return;
        trash.ReleaseMagnetHold();
        trash.transform.position = new Vector3(position.x, position.y, trash.transform.position.z);
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

    private static string GetArgumentValue(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase)) return arguments[i + 1];
        }
        return null;
    }

    private static int ParseIntArgument(string name, int fallback)
    {
        string value = GetArgumentValue(name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    private static float ParseFloatArgument(string name, float fallback)
    {
        string value = GetArgumentValue(name);
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : fallback;
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
        // 實機 UI 是 Overlay（永遠在最上層）；離屏改成 Camera 模式後墊高排序，免得被前牆蓋住。
        if (!_canvasSortingRaised)
        {
            _canvasSortingRaised = true;
            for (int i = 0; i < canvases.Length; i++)
                if (canvases[i].isRootCanvas) canvases[i].sortingOrder += 1000;
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
            byte[] jpg = image.EncodeToJPG(90);
            if (jpg == null || jpg.Length < 1024)
                throw new InvalidOperationException("Offscreen renderer returned an empty frame.");
            File.WriteAllBytes(outputPath, jpg);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (image != null) Destroy(image);
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
