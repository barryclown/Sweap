using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class SkillManager : MonoBehaviour
{
    [Header("=== Impact Mode (小掃) ===")]
    [SerializeField] private LayerMask trashLayer;
    [SerializeField] private float minSweepForce;
    [SerializeField] private float maxSweepForce;

    [Header("=== Sticky Mode (磁鐵) ===")]
    [SerializeField, Tooltip("磁鐵模式下的判定範圍倍率")] private float magnetRadiusMultiplier = 1.2f;
    [SerializeField, Tooltip("一次最多吸住幾個垃圾（避免整團疊成一顆、也避免重量把掃把拖到不能動）")]
    private int maxCapturedTrash = 12;
    [SerializeField, Tooltip("吸住的垃圾繞刷頭排開的內圈半徑（× 有效掃擊半徑）")]
    private float clusterInnerRadius = 0.32f;
    [SerializeField, Tooltip("每多一個垃圾往外擴的距離（× 有效掃擊半徑，依 √n 擴散）")]
    private float clusterSpacing = 0.24f;
    [SerializeField, Tooltip("整團繞刷頭旋轉的速度（度／秒）")]
    private float clusterSpinDegrees = 55f;
    [SerializeField, Tooltip("切回撞擊模式時，垃圾保留掃把速度的比例")]
    private float releaseInheritVelocity = 0.85f;
    [SerializeField, Tooltip("切回撞擊模式時，垃圾往外散開的速度")]
    private float releaseScatterSpeed = 2.2f;
    [SerializeField, Tooltip("蓄力丟出時每個垃圾的角度散佈（度）")]
    private float throwSpreadDegrees = 16f;

    [Header("=== Charged Mode (右鍵蓄力) ===")]
    [SerializeField] private Transform chargedSweepRoot;
    [SerializeField] private DynamicSweepMesh sweepMesh;
    [SerializeField] private PolygonCollider2D sweepCollider;
    [SerializeField] private float minForceMultiplier;
    [SerializeField] private float maxForceMultiplier;
    [SerializeField] private float chargedPowerExponent = 1f;

    private readonly Collider2D[] smallSweepResults = new Collider2D[32];
    private readonly Collider2D[] chargedSweepResults = new Collider2D[64];
    private readonly HashSet<BaseTrash> sweepHitTrash = new HashSet<BaseTrash>();

    private readonly HashSet<BaseTrash> capturedTrash = new HashSet<BaseTrash>();
    // 依吸到的先後排序，每個垃圾有固定的位置（slot），不會全部擠到同一點。
    private readonly List<BaseTrash> capturedOrder = new List<BaseTrash>(16);
    private readonly List<BaseTrash> trashToRemove = new List<BaseTrash>();
    private float _clusterAngle;
    private readonly List<BaseTrash> nearbyTrashBuffer = new List<BaseTrash>();

    private PlayerController player;
    private ContactFilter2D trashFilter;

    // 紀錄基準力度，避免重複乘法導致數值崩潰
    private float _baseMinSweepForce;
    private float _baseMaxSweepForce;
    private float _baseMinForceMultiplier;
    private float _baseMaxForceMultiplier;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        trashFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = trashLayer,
            useTriggers = true
        };
        if (chargedSweepRoot != null)
        {
            if (!sweepMesh) sweepMesh = chargedSweepRoot.GetComponentInChildren<DynamicSweepMesh>(true);
            if (!sweepCollider) sweepCollider = chargedSweepRoot.GetComponentInChildren<PolygonCollider2D>(true);
            if (sweepCollider) sweepCollider.isTrigger = true;
            chargedSweepRoot.gameObject.SetActive(false);
        }

        _baseMinSweepForce = minSweepForce;
        _baseMaxSweepForce = maxSweepForce;
        _baseMinForceMultiplier = minForceMultiplier;
        _baseMaxForceMultiplier = maxForceMultiplier;
    }

    private void OnEnable()
    {
        if (player == null) return;
        player.OnSweepMove += HandleSweepMove;
        player.OnChargedSweepUpdate += HandleChargedSweepUpdate;
        player.OnChargedSweepReleased += HandleChargedSweepReleased;
        player.OnModeChanged += HandleModeChanged;
        player.OnAbsorbedByBlackHole += HandleAbsorbedByBlackHole;

        player.OnScaleChanged += HandleScaleChanged;
    }

    private void OnDisable()
    {
        if (player == null) return;
        player.OnSweepMove -= HandleSweepMove;
        player.OnChargedSweepUpdate -= HandleChargedSweepUpdate;
        player.OnChargedSweepReleased -= HandleChargedSweepReleased;
        player.OnModeChanged -= HandleModeChanged;
        player.OnAbsorbedByBlackHole -= HandleAbsorbedByBlackHole;

        player.OnScaleChanged -= HandleScaleChanged;
        ReleaseAllCapturedTrash();
    }

    private void HandleScaleChanged(float multiplier)
    {
        minSweepForce = _baseMinSweepForce * multiplier;
        maxSweepForce = _baseMaxSweepForce * multiplier;
        minForceMultiplier = _baseMinForceMultiplier * multiplier;
        maxForceMultiplier = _baseMaxForceMultiplier * multiplier;

        if (chargedSweepRoot != null)
        {
            chargedSweepRoot.localScale = Vector3.one * multiplier;
        }
    }

    private void FixedUpdate()
    {
        if (player == null || player.isBeingAbsorbed) return;

        if (player.currentMode == BroomMode.Sticky)
        {
            Vector2 center = player.GetSweepCenter();
            ScanForNewStickyTrash(center);
            Vector2 velocity = player.rb.linearVelocity;
            UpdateCapturedTrashPosition(center, velocity);
        }
    }

    private void HandleAbsorbedByBlackHole(BlackHoleObstacle blackHole)
    {
        if (capturedTrash.Count == 0) return;

        foreach (var trash in capturedTrash)
        {
            if (trash != null && trash.gameObject.activeInHierarchy)
            {
                trash.ReleaseMagnetHold();
                blackHole.RegisterTrash(trash);
            }
        }

        capturedTrash.Clear();
        capturedOrder.Clear();
        player.SetStickyLoad(0f);
    }

    public int CapturedCount => capturedOrder.Count;
    public int MaxCapturedTrash => maxCapturedTrash;

    /// <summary>第 index 個吸住的垃圾相對刷頭的位置：黃金角螺旋排開，整團慢慢旋轉。</summary>
    public static Vector2 GetClusterOffset(int index, float sweepRadius, float innerRadius, float spacing, float spinDegrees)
    {
        const float GoldenAngle = 137.50776f;
        float radius = sweepRadius * (innerRadius + spacing * Mathf.Sqrt(index));
        float angle = (spinDegrees + index * GoldenAngle) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    /// <summary>把吸住的整團往 direction 丟出去，力道依蓄力 power01。回傳丟出幾個。</summary>
    public int ThrowCapturedTrash(Vector2 direction, float power01)
    {
        if (capturedOrder.Count == 0) return 0;
        Vector2 baseDir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float curve = Mathf.Pow(Mathf.Clamp01(power01), chargedPowerExponent);
        float forceMul = Mathf.Lerp(minForceMultiplier, maxForceMultiplier, curve);
        int thrown = 0;
        int count = capturedOrder.Count;
        for (int i = 0; i < count; i++)
        {
            BaseTrash trash = capturedOrder[i];
            if (trash == null || !trash.gameObject.activeInHierarchy || trash.IsAbsorbing) continue;
            float spread = count > 1 ? Mathf.Lerp(-throwSpreadDegrees, throwSpreadDegrees, i / (count - 1f)) : 0f;
            trash.ThrowFromMagnet(Rotate(baseDir, spread), forceMul);
            thrown++;
        }
        capturedTrash.Clear();
        capturedOrder.Clear();
        player.SetStickyLoad(0f);
        return thrown;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    private Vector2 GetSlotTarget(Vector2 center, int index)
    {
        return center + GetClusterOffset(index, player.GetEffectiveSweepRadius(), clusterInnerRadius, clusterSpacing, _clusterAngle);
    }

    private bool TryCapture(BaseTrash trash, Vector2 center)
    {
        if (capturedOrder.Count >= maxCapturedTrash || !capturedTrash.Add(trash)) return false;
        capturedOrder.Add(trash);
        trash.ApplyMagnetHold(GetSlotTarget(center, capturedOrder.Count - 1), Vector2.zero);
        return true;
    }

    private void HandleSweepMove(Vector2 center, float radius, Vector2 moveDir, float power01)
    {
        if (moveDir.sqrMagnitude < 0.0001f || player.currentMode == BroomMode.Sticky) return;

        float curve = power01 * power01;
        float power = Mathf.Lerp(minSweepForce, maxSweepForce, curve);
        sweepHitTrash.Clear();

        float totalWeight = 0f;
        int count = Physics2D.OverlapCircle(center, radius, trashFilter, smallSweepResults);

        for (int i = 0; i < count; i++)
        {
            var col = smallSweepResults[i];
            if (!col) continue;
            if (col.TryGetComponent(out BaseTrash trash))
            {
                if (sweepHitTrash.Add(trash))
                {
                    trash.ApplyBroomHit(moveDir, power);
                    totalWeight += trash.Weight;
                }
            }
        }
    }

    private void ScanForNewStickyTrash(Vector2 center)
    {
        nearbyTrashBuffer.Clear();
        if (capturedOrder.Count >= maxCapturedTrash) return;

        // [重點註釋] 讓吸附半徑也繼承 PlayerController 的判定溢出容錯，解決靠牆吸不到的問題
        float catchRadius = player.GetEffectiveSweepRadius() * magnetRadiusMultiplier;
        float sqrCatchRadius = catchRadius * catchRadius;

        SpatialGridManager.Instance.GetTrashAroundPosition(center, nearbyTrashBuffer);

        for (int i = 0; i < nearbyTrashBuffer.Count; i++)
        {
            var trash = nearbyTrashBuffer[i];
            if (trash == null || !trash.CanBeMagnetized) continue;
            if (capturedTrash.Contains(trash)) continue;
            if (((Vector2)trash.transform.position - center).sqrMagnitude <= sqrCatchRadius)
            {
                if (!TryCapture(trash, center)) break;
            }
        }
    }

    private void UpdateCapturedTrashPosition(Vector2 center, Vector2 velocity)
    {
        float currentTotalWeight = 0f;

        if (capturedTrash.Count == 0)
        {
            player.SetStickyLoad(0f);
            return;
        }

        trashToRemove.Clear();
        for (int i = 0; i < capturedOrder.Count; i++)
        {
            BaseTrash trash = capturedOrder[i];
            if (trash == null || !trash.gameObject.activeInHierarchy || trash.IsAbsorbing)
                trashToRemove.Add(trash);
        }

        if (trashToRemove.Count > 0)
        {
            foreach (var t in trashToRemove)
            {
                capturedTrash.Remove(t);
                capturedOrder.Remove(t);
            }
        }

        _clusterAngle = Mathf.Repeat(_clusterAngle + clusterSpinDegrees * Time.fixedDeltaTime, 360f);
        for (int i = 0; i < capturedOrder.Count; i++)
        {
            BaseTrash trash = capturedOrder[i];
            trash.ApplyMagnetHold(GetSlotTarget(center, i), velocity);
            currentTotalWeight += trash.Weight;
        }

        player.SetStickyLoad(currentTotalWeight);
    }

    private void ReleaseAllCapturedTrash()
    {
        // 鬆開時順著掃把當下的速度帶出去，再往外輕輕散開；不沿用平滑運算的殘留速度。
        Vector2 hostVelocity = player != null && player.rb != null ? player.rb.linearVelocity : Vector2.zero;
        Vector2 center = player != null ? player.GetSweepCenter() : Vector2.zero;
        for (int i = 0; i < capturedOrder.Count; i++)
        {
            BaseTrash trash = capturedOrder[i];
            if (trash == null) continue;
            Vector2 outward = (Vector2)trash.transform.position - center;
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector2.zero;
            trash.ReleaseMagnetHold(hostVelocity * releaseInheritVelocity + outward * releaseScatterSpeed);
        }
        foreach (var trash in capturedTrash)
        {
            if (trash != null) trash.ReleaseMagnetHold();
        }
        capturedTrash.Clear();
        capturedOrder.Clear();
        if (player != null) player.SetStickyLoad(0f);
    }

    private void HandleModeChanged(BroomMode newMode)
    {
        if (newMode != BroomMode.Sticky) ReleaseAllCapturedTrash();
    }

    private void HandleChargedSweepUpdate(float holdTime, float t, Vector2 origin, Vector2 dir)
    {
        if (chargedSweepRoot == null) return;
        chargedSweepRoot.gameObject.SetActive(true);
        chargedSweepRoot.position = origin;
        chargedSweepRoot.right = dir;
        if (sweepMesh) sweepMesh.UpdateShape(t);
        if (sweepMesh && sweepCollider)
        {
            var path = sweepMesh.CurrentPath2D;
            if (path != null && path.Length >= 3)
            {
                sweepCollider.pathCount = 1;
                sweepCollider.SetPath(0, path);
            }
        }
    }

    private void HandleChargedSweepReleased(float holdTime, float t, Vector2 origin, Vector2 dir)
    {
        if (sweepCollider == null) return;

        int count = sweepCollider.Overlap(trashFilter, chargedSweepResults);
        if (chargedSweepRoot != null) chargedSweepRoot.gameObject.SetActive(false);

        if (player.currentMode == BroomMode.Impact)
        {
            float curve = Mathf.Pow(t, chargedPowerExponent);
            float forceMul = Mathf.Lerp(minForceMultiplier, maxForceMultiplier, curve);

            for (int i = 0; i < count; i++)
            {
                var col = chargedSweepResults[i];
                if (!col) continue;

                if (col.TryGetComponent(out BaseTrash trash))
                {
                    Vector2 radialDir = ((Vector2)trash.transform.position - origin);
                    if (radialDir.sqrMagnitude < 0.0001f) radialDir = dir;
                    else radialDir.Normalize();

                    trash.ApplyBroomHit(radialDir, forceMul);
                }
            }
        }
        else if (player.currentMode == BroomMode.Sticky)
        {
            // 手上有垃圾：蓄力放開把整團往瞄準方向丟出去（可以丟進黑洞）。
            if (ThrowCapturedTrash(dir, t) > 0) return;

            // 手上沒東西：維持原本的蓄力範圍吸取。
            Vector2 sweepCenter = player.GetSweepCenter();
            for (int i = 0; i < count; i++)
            {
                var col = chargedSweepResults[i];
                if (!col) continue;

                if (col.TryGetComponent(out BaseTrash trash))
                {
                    if (trash == null || !trash.CanBeMagnetized || capturedTrash.Contains(trash)) continue;
                    if (!TryCapture(trash, sweepCenter)) break;
                }
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (player == null) player = GetComponent<PlayerController>();
        if (player != null && player.currentMode == BroomMode.Sticky)
        {
            Gizmos.color = Color.green;
            // 編輯器可視化同步支援新的容錯半徑
            float radius = player.GetEffectiveSweepRadius() * magnetRadiusMultiplier;
            Gizmos.DrawWireSphere(player.GetSweepCenter(), radius);
        }
    }
#endif
}