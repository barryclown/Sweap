using UnityEngine;

/// <summary>
/// 2.5D 房間外框的鏡頭限制。地板是 z=0 平面，牆往 -Z 立起；後牆、左右牆是全高牆，
/// 前牆是剖面矮牆（畫在玩法物件上層）。鏡頭規則依序是：
/// 1. 玩家一定留在安全視窗內（硬限制）。
/// 2. 靠邊時牆要看得到，但不能壓滿畫面；牆頂外的黑邊只露一小段。
/// 由 SweepArenaPresentationFix 在編輯器內設定欄位，執行時只讀。
/// </summary>
public sealed class SweepRoomFrame : MonoBehaviour
{
    public static SweepRoomFrame Instance { get; private set; }

    [SerializeField, Min(0.1f)] private float wallHeight = 6f;
    [SerializeField, Min(0.1f)] private float frontWallHeight = 1.4f;
    [SerializeField] private float frontWallLineY;
    [SerializeField, Min(0f)] private float capThickness = 0.45f;
    [Tooltip("靠左右牆時，畫面中線左右各留多少比例給牆面。")]
    [SerializeField, Range(0f, 0.45f)] private float sideWallViewportShare = 0.15f;
    [Tooltip("靠後牆時，後牆牆腳最高只到畫面這個高度（0=底、1=頂）。")]
    [SerializeField, Range(0.5f, 1f)] private float backWallViewportLine = 0.7f;
    [Tooltip("牆頂外允許露出的黑邊寬度（世界單位）。")]
    [SerializeField, Min(0f)] private float outsideMargin = 1.5f;
    [SerializeField] private Vector2 playerSafeViewportMin = new Vector2(0.1f, 0.12f);
    [SerializeField] private Vector2 playerSafeViewportMax = new Vector2(0.9f, 0.85f);

    public float WallHeight => wallHeight;
    public float FrontWallHeight => frontWallHeight;
    public float FrontWallLineY => frontWallLineY;
    public float CapThickness => capThickness;

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    public CameraLimits GetCameraLimits(Rect room)
    {
        return new CameraLimits
        {
            Room = room,
            WallHeight = wallHeight,
            FrontWallHeight = frontWallHeight,
            FrontWallLineY = frontWallLineY,
            CapThickness = capThickness,
            SideWallViewportShare = sideWallViewportShare,
            BackWallViewportLine = backWallViewportLine,
            OutsideMargin = outsideMargin,
            PlayerSafeMin = playerSafeViewportMin,
            PlayerSafeMax = playerSafeViewportMax
        };
    }

    public struct CameraLimits
    {
        public Rect Room;
        public float WallHeight;
        public float FrontWallHeight;
        public float FrontWallLineY;
        public float CapThickness;
        public float SideWallViewportShare;
        public float BackWallViewportLine;
        public float OutsideMargin;
        public Vector2 PlayerSafeMin;
        public Vector2 PlayerSafeMax;
    }

    /// <summary>
    /// 只平移鏡頭的 XY。水平平移不改變射線方向，所以每條限制都能直接換算成允許的平移區間，
    /// 不需要反覆試算。牆的限制互相衝突時取中間；最後再夾回「玩家在安全視窗內」的區間。
    /// </summary>
    public static Vector3 ClampCamera(Camera camera, Vector3 desiredPosition, Vector3 playerPosition, in CameraLimits limits)
    {
        if (camera == null) return desiredPosition;

        Transform cameraTransform = camera.transform;
        Vector3 originalPosition = cameraTransform.position;
        cameraTransform.position = desiredPosition;
        try
        {
            Rect room = limits.Room;
            float outerPad = limits.CapThickness + limits.OutsideMargin;
            float lowX = float.NegativeInfinity, highX = float.PositiveInfinity;
            float lowY = float.NegativeInfinity, highY = float.PositiveInfinity;

            // 中線兩側的地板點要留在左右牆內：靠牆時牆只佔畫面邊緣一小塊。
            float share = limits.SideWallViewportShare;
            if (TryHitPlane(camera, share, 0.5f, 0f, out Vector3 hit)) lowX = Mathf.Max(lowX, room.xMin - hit.x);
            if (TryHitPlane(camera, 1f - share, 0.5f, 0f, out hit)) highX = Mathf.Min(highX, room.xMax - hit.x);

            // 後牆牆腳不能高過指定的畫面高度。
            if (TryHitPlane(camera, 0.5f, limits.BackWallViewportLine, 0f, out hit))
                highY = Mathf.Min(highY, room.yMax - hit.y);

            // 牆頂平面：四角射線穿過牆頂高度時，最多只能落在牆頂外 outerPad 內。
            float wallTopZ = -limits.WallHeight;
            ApplyWallTopCorner(camera, 0f, 0f, wallTopZ, room, outerPad, ref lowX, ref highX, ref highY, false);
            ApplyWallTopCorner(camera, 1f, 0f, wallTopZ, room, outerPad, ref lowX, ref highX, ref highY, false);
            ApplyWallTopCorner(camera, 0f, 1f, wallTopZ, room, outerPad, ref lowX, ref highX, ref highY, true);
            ApplyWallTopCorner(camera, 1f, 1f, wallTopZ, room, outerPad, ref lowX, ref highX, ref highY, true);

            // 前牆：畫面底部射線穿過矮牆頂時，最多落在矮牆外側 OutsideMargin 內——
            // 矮牆整段看得到，牆外只露一小段黑邊（與另外三面一致）。
            float frontOuterY = limits.FrontWallLineY - limits.CapThickness - limits.OutsideMargin;
            float frontTopZ = -limits.FrontWallHeight;
            if (TryHitPlane(camera, 0f, 0f, frontTopZ, out hit)) lowY = Mathf.Max(lowY, frontOuterY - hit.y);
            if (TryHitPlane(camera, 1f, 0f, frontTopZ, out hit)) lowY = Mathf.Max(lowY, frontOuterY - hit.y);

            // 硬限制：玩家留在安全視窗內。先定 Y（Y 平移會改變深度，連帶改變左右的換算），
            // 再在新的 Y 位置上算 X 的允許區間。牆的限制本身與另一軸無關。
            GetPlayerShiftRange(camera, playerPosition, limits.PlayerSafeMin, limits.PlayerSafeMax,
                out Vector2 playerLow, out Vector2 playerHigh);
            float shiftY = Mathf.Clamp(Solve(lowY, highY), playerLow.y, playerHigh.y);

            cameraTransform.position = desiredPosition + new Vector3(0f, shiftY, 0f);
            GetPlayerShiftRange(camera, playerPosition, limits.PlayerSafeMin, limits.PlayerSafeMax,
                out playerLow, out playerHigh);
            float shiftX = Mathf.Clamp(Solve(lowX, highX), playerLow.x, playerHigh.x);

            return desiredPosition + new Vector3(shiftX, shiftY, 0f);
        }
        finally
        {
            cameraTransform.position = originalPosition;
        }
    }

    /// <summary>射線打到 z=planeZ 的點；平面在鏡頭後面或射線平行時回傳 false。</summary>
    public static bool TryHitPlane(Camera camera, float viewportX, float viewportY, float planeZ, out Vector3 point)
    {
        Ray ray = camera.ViewportPointToRay(new Vector3(viewportX, viewportY, 0f));
        point = Vector3.zero;
        if (Mathf.Abs(ray.direction.z) < 0.0001f) return false;
        float enter = (planeZ - ray.origin.z) / ray.direction.z;
        if (enter <= 0f) return false;
        point = ray.origin + ray.direction * enter;
        return true;
    }

    private static void ApplyWallTopCorner(
        Camera camera, float viewportX, float viewportY, float wallTopZ, Rect room, float outerPad,
        ref float lowX, ref float highX, ref float highY, bool limitTop)
    {
        if (!TryHitPlane(camera, viewportX, viewportY, wallTopZ, out Vector3 hit)) return;
        lowX = Mathf.Max(lowX, room.xMin - outerPad - hit.x);
        highX = Mathf.Min(highX, room.xMax + outerPad - hit.x);
        if (limitTop) highY = Mathf.Min(highY, room.yMax + outerPad - hit.y);
    }

    private static float Solve(float low, float high)
    {
        if (low > high) return (low + high) * 0.5f;
        if (low > 0f) return low;
        if (high < 0f) return high;
        return 0f;
    }

    /// <summary>
    /// 鏡頭沿世界 X/Y 平移多少時，玩家仍落在安全視窗內。X 平移不改變深度，是線性的；
    /// Y 平移同時改變深度與高度，用投影式反解。
    /// </summary>
    private static void GetPlayerShiftRange(
        Camera camera, Vector3 playerPosition, Vector2 safeMin, Vector2 safeMax,
        out Vector2 low, out Vector2 high)
    {
        Transform cameraTransform = camera.transform;
        Vector3 relative = playerPosition - cameraTransform.position;
        Vector3 forward = cameraTransform.forward;
        Vector3 up = cameraTransform.up;
        Vector3 right = cameraTransform.right;
        float depth = Vector3.Dot(relative, forward);
        if (depth <= 0.01f)
        {
            low = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            high = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            return;
        }

        float tanV = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanH = tanV * camera.aspect;

        // X：鏡頭往 +X 移 s，玩家的 right 分量少 s*right.x，深度不變。
        float rightComponent = Vector3.Dot(relative, right);
        float viewportX = 0.5f + rightComponent / (2f * depth * tanH);
        float unitsPerViewportX = 2f * depth * tanH / Mathf.Max(0.0001f, Mathf.Abs(right.x));
        low.x = (viewportX - safeMax.x) * unitsPerViewportX;
        high.x = (viewportX - safeMin.x) * unitsPerViewportX;

        // Y：鏡頭往 +Y 移 s，up 分量與深度都會變；解 (u - s*b) = k (d - s*a)。
        float upComponent = Vector3.Dot(relative, up);
        float a = forward.y;
        float b = up.y;
        float sAtTop = SolveShiftForViewportY(upComponent, depth, a, b, tanV, safeMax.y);
        float sAtBottom = SolveShiftForViewportY(upComponent, depth, a, b, tanV, safeMin.y);
        low.y = Mathf.Min(sAtTop, sAtBottom);
        high.y = Mathf.Max(sAtTop, sAtBottom);
    }

    private static float SolveShiftForViewportY(float up, float depth, float a, float b, float tanV, float targetViewportY)
    {
        float k = 2f * tanV * (targetViewportY - 0.5f);
        float denominator = k * a - b;
        if (Mathf.Abs(denominator) < 0.0001f) return 0f;
        return (k * depth - up) / denominator;
    }
}
