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
    private readonly List<BaseTrash> trashToRemove = new List<BaseTrash>();
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
        player.SetStickyLoad(0f);
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

        // [重點註釋] 讓吸附半徑也繼承 PlayerController 的判定溢出容錯，解決靠牆吸不到的問題
        float catchRadius = player.GetEffectiveSweepRadius() * magnetRadiusMultiplier;
        float sqrCatchRadius = catchRadius * catchRadius;

        SpatialGridManager.Instance.GetTrashAroundPosition(center, nearbyTrashBuffer);

        for (int i = 0; i < nearbyTrashBuffer.Count; i++)
        {
            var trash = nearbyTrashBuffer[i];
            if (trash == null || trash.IsAbsorbing) continue;
            if (capturedTrash.Contains(trash)) continue;
            if (((Vector2)trash.transform.position - center).sqrMagnitude <= sqrCatchRadius)
            {
                capturedTrash.Add(trash);
                trash.ApplyMagnetHold(center, Vector2.zero);
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
        foreach (var trash in capturedTrash)
        {
            if (trash == null || !trash.gameObject.activeInHierarchy || trash.IsAbsorbing)
            {
                trashToRemove.Add(trash);
                continue;
            }
            trash.ApplyMagnetHold(center, velocity);
            currentTotalWeight += trash.Weight;
        }

        if (trashToRemove.Count > 0)
        {
            foreach (var t in trashToRemove) capturedTrash.Remove(t);
        }

        player.SetStickyLoad(currentTotalWeight);
    }

    private void ReleaseAllCapturedTrash()
    {
        foreach (var trash in capturedTrash)
        {
            if (trash != null) trash.ReleaseMagnetHold();
        }
        capturedTrash.Clear();
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
            Vector2 sweepCenter = player.GetSweepCenter();

            for (int i = 0; i < count; i++)
            {
                var col = chargedSweepResults[i];
                if (!col) continue;

                if (col.TryGetComponent(out BaseTrash trash))
                {
                    if (trash == null || trash.IsAbsorbing || capturedTrash.Contains(trash)) continue;

                    capturedTrash.Add(trash);
                    trash.ApplyMagnetHold(sweepCenter, Vector2.zero);
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