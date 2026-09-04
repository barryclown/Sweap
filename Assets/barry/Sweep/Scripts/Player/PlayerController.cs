using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum BroomMode
{
    Impact,
    Sticky
}

public class PlayerController : MonoBehaviour, IAbsorbable
{
    public static PlayerController instance { get; private set; }

    [Header("=== 外部依賴 ===")]
    [SerializeField] public PlayerEffectManager effectManager;

    [Header("=== 體驗優化 (防黏滯與疊音) ===")]
    [SerializeField, Tooltip("被黑洞吐出後的無敵冷卻時間 (秒)")]
    private float absorbInvincibleTime = 1.5f;
    [SerializeField, Tooltip("全局特效與音效防疊加冷卻 (秒)")]
    private float vfxHitCooldown = 0.1f;
    private float _vfxCooldownTimer = 0f;

    [Header("=== 展演爽感 ===")]
    [SerializeField, Range(0.01f, 0.2f)] private float hitStopDuration = 0.035f;
    [SerializeField, Range(0.01f, 0.5f)] private float hitStopTimeScale = 0.08f;
    [SerializeField, Min(0f)] private float hitCameraShake = 0.11f;
    [SerializeField, Min(0f)] private float wallCameraShake = 0.18f;
    [SerializeField, Min(0f)] private float fullChargeCameraShake = 0.42f;
    [SerializeField, Range(0.01f, 0.2f)] private float fullChargeHitStopDuration = 0.06f;

    private float _demoSpeedMultiplier = 1f;
    private float _demoCooldownRecoveryMultiplier = 1f;
    private Coroutine _hitStopRoutine;
    private float _hitStopUntil;
    private float _timeScaleBeforeHitStop = 1f;
    private float _activeHitStopScale = 1f;
    private float _cameraShakeUntil;
    private float _cameraShakeAmplitude;
    private Vector3 _lastCameraShakeOffset;

    [Header("=== 防穿模與重物阻擋 (連續碰撞) ===")]
    [SerializeField, Tooltip("實體阻擋半徑 (低於 sweepRadius，代表掃把本體的物理邊界)")]
    public float bodyCollisionRadius = 0.5f;
    [SerializeField, Tooltip("重量比率閾值：垃圾重量 / 掃把重量 >= 此值，即視為推不動的重物")]
    public float heavyTrashWeightRatio = 1.5f;
    private readonly List<BaseTrash> _nearbyObstacles = new List<BaseTrash>(32);

    [Header("=== 基礎組件 ===")]
    public Vector2 fixForWall;
    public Rigidbody2D rb;
    private Camera cam;
    private PlayerInput input;
    private InputActionMap playerActionMap;

    public event Action<Vector2, float, Vector2, float> OnSweepMove;
    public event Action<float, float, Vector2, Vector2> OnChargedSweepUpdate;
    public event Action<float, float, Vector2, Vector2> OnChargedSweepReleased;
    public event Action<BroomMode> OnModeChanged;
    public event Action<BlackHoleObstacle> OnAbsorbedByBlackHole;
    public event Action OnLeftPressAction;
    public event Action OnLeftReleaseAction;
    public event Action OnRightPressStart;
    public event Action OnWallHitEvent;
    public event Action<TrashType> OnTrashHitEvent;
    public event Action<float> OnScaleChanged;
    public event Action<float, float> OnRightSkillCooldownUpdate;

    private InputAction pointerPress;
    private InputAction rightPointerPress;
    private InputAction pointerPosition;
    private InputAction switchModeAction;
    private InputAction scrollYAction;
    private InputAction middleClickAction;

    private Vector2 dragStart;
    private Vector2 moveDir;
    private float currentSpeed;
    private Vector2 chargedDir;
    private float rightPressStartTime;
    private bool isLeftDown = false;
    private bool isRightDown = false;
    private bool blockBoth = false;
    public bool isBeingAbsorbed;
    public bool isBlocking = false;
    private float _absorbCooldown = 0f;
    private Collider2D _currentBlackHoleCollider;

    [Header("=== 模式與冷卻設定 ===")]
    public BroomMode currentMode = BroomMode.Impact;
    [SerializeField] private float switchCooldown = 0.2f;
    private float _lastSwitchTime = 0f;
    [SerializeField] private float rightSkillCooldown = 5f;
    private float _currentRightSkillCooldown = 0f;

    [Header("=== 移動參數 ===")]
    public float maxSpeed = 12f;
    public float deceleration = 8f;

    [Header("=== Sticky Weight Settings (重量系統) ===")]
    [SerializeField] private float weightPenaltyFactor = 0.5f;
    [SerializeField] private float minStickySpeed = 2f;
    private float _currentStickyLoad = 0f;

    [Header("=== 基礎判定參數 ===")]
    public float sweepRadius = 1f;
    public Vector2 sweepOffset;
    public float maxChargeTime = 1.5f;
    [SerializeField] private float chargeCenterOffset = 0f;

    [Header("=== 判定容錯設定 (解決靠牆死角) ===")]
    [SerializeField] public float sweepForgivenessMultiplier = 1.25f;
    [SerializeField] private float forwardReachOffset = 0.5f;

    [Header("=== 碰撞反應參數 (物理反饋) ===")]
    [SerializeField, Min(0.01f)] private float wieght = 1f;
    [SerializeField, Range(0f, 1f)] private float sweepRestitution = 0f;
    [SerializeField] private bool allowBounceBack = false;
    [SerializeField] private bool scaleHitBySweepPower = true;
    [SerializeField, Range(0f, 1f)] private float minHitPower = 0.25f;
    [SerializeField, Min(0f)] private float hitWeightScale = 1f;

    [Header("=== 成長與相機連動 ===")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 15f, -15f);
    [SerializeField] private float growthSyncRatio = 1f;
    [SerializeField] private float growthLerpSpeed = 3f;
    [SerializeField] private float cameraFollowSmoothTime = 0.18f;
    [SerializeField] private float cameraLookAheadSmoothTime = 0.12f;
    [SerializeField] private float cameraLookAheadDistance = 1.5f;
    [SerializeField] private float cameraLookAheadSpeedThreshold = 0.15f;

    private Vector3 _baseCameraOffset;
    private Vector3 _baseScale;
    private float _baseMaxSpeed;
    private float _baseSweepRadius;
    private Vector2 _baseSweepOffset;
    private float _baseWeight;
    private float _baseChargeCenterOffset;
    private float _baseForwardReachOffset;
    private Vector3 _targetScale;
    private Vector3 _targetCameraOffset;
    private Vector3 _cameraFollowVelocity;
    private Vector3 _cameraLookAheadOffset;
    private Vector3 _cameraLookAheadVelocity;

    public float CurrentSpeed => currentSpeed;
    public float Wieght => wieght;
    private Collider2D[] colliders;
    private bool _areCollidersEnabled = true;

    public Vector2 GetSweepCenter() => (Vector2)transform.position + sweepOffset;
    public bool CanBeAbsorbed => !isBeingAbsorbed && _absorbCooldown <= 0f;
    public float GetEffectiveSweepRadius() => sweepRadius * sweepForgivenessMultiplier;

    public Vector2 GetDynamicSweepCenter()
    {
        Vector2 baseCenter = GetSweepCenter();
        if (currentSpeed > 0.01f && currentMode != BroomMode.Sticky)
        {
            baseCenter += moveDir * forwardReachOffset;
        }
        return baseCenter;
    }

    public void OnAbsorbStart(BlackHoleObstacle blackHole)
    {
        if (isBeingAbsorbed || _absorbCooldown > 0f) return;
        Collider2D incomingCollider = blackHole.GetComponent<Collider2D>();
        if (incomingCollider == _currentBlackHoleCollider) return;

        _currentBlackHoleCollider = incomingCollider;
        isBeingAbsorbed = true;
        isLeftDown = false;
        isRightDown = false;
        _currentStickyLoad = 0f;
        effectManager.HideDragLine();
        effectManager.HideChargeSweep();

        if (PlayerAnimatorController.instance != null)
        {
            PlayerAnimatorController.instance.stateMachine.ChangeState(PlayerAnimatorController.instance.idleState);
        }

        BlockBoth();
        ResetCameraMotion();
        SetCollidersEnabled(false);
        blackHole.RegisterPlayer(this);
        OnAbsorbedByBlackHole?.Invoke(blackHole);
    }

    public void SetStickyLoad(float totalWeight)
    {
        _currentStickyLoad = totalWeight;
    }

    private float GetEffectiveMaxSpeed()
    {
        float effectiveSpeed = maxSpeed;

        if (currentMode == BroomMode.Sticky && _currentStickyLoad > 0.001f)
        {
            float penaltyDivisor = 1f + (_currentStickyLoad * weightPenaltyFactor);
            effectiveSpeed = Mathf.Max(maxSpeed / penaltyDivisor, minStickySpeed);
        }

        return effectiveSpeed * _demoSpeedMultiplier;
    }

    public void SetDemoIntensity(int phase, bool frenzy)
    {
        int safePhase = Mathf.Clamp(phase, 0, 3);
        _demoSpeedMultiplier = 1f + (safePhase * 0.08f) + (frenzy ? 0.15f : 0f);
        _demoCooldownRecoveryMultiplier = 1f + (safePhase * 0.25f) + (frenzy ? 1.25f : 0f);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (_currentBlackHoleCollider != null && other == _currentBlackHoleCollider)
        {
            _currentBlackHoleCollider = null;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        cam = Camera.main;
        rb = GetComponent<Rigidbody2D>();
        if (effectManager == null) effectManager = GetComponent<PlayerEffectManager>();
        input = GetComponent<PlayerInput>();
        if (input == null) { Debug.LogError("Missing PlayerInput!"); return; }

        playerActionMap = input.actions?.FindActionMap("Player", false);
        pointerPress = playerActionMap?.FindAction("PointerPress", false);
        rightPointerPress = playerActionMap?.FindAction("RightPointerPress", false);
        pointerPosition = playerActionMap?.FindAction("PointerPosition", false);
        switchModeAction = playerActionMap?.FindAction("SwitchMode", false);

        if (playerActionMap == null || pointerPress == null || rightPointerPress == null || pointerPosition == null)
        {
            Debug.LogError("[PlayerController] Player Input Actions 缺少 Player map 或必要滑鼠 Action。", this);
        }

        scrollYAction = new InputAction("ScrollY", binding: "<Mouse>/scroll/y");
        middleClickAction = new InputAction("MiddleClick", binding: "<Mouse>/middleButton");

        colliders = GetComponentsInChildren<Collider2D>(true);

        _baseCameraOffset = cameraOffset;
        _baseScale = transform.localScale;
        _baseMaxSpeed = maxSpeed;
        _baseSweepRadius = sweepRadius;
        _baseSweepOffset = sweepOffset;
        _baseWeight = wieght;
        _baseChargeCenterOffset = chargeCenterOffset;
        _baseForwardReachOffset = forwardReachOffset;
        _targetScale = _baseScale;
        _targetCameraOffset = _baseCameraOffset;
        _cameraFollowVelocity = Vector3.zero;
        _cameraLookAheadOffset = Vector3.zero;
        _cameraLookAheadVelocity = Vector3.zero;
    }

    private void OnEnable()
    {
        if (input == null) return;
        playerActionMap?.Enable();
        if (pointerPress != null)
        {
            pointerPress.started += OnPress;
            pointerPress.canceled += OnRelease;
        }
        if (rightPointerPress != null)
        {
            rightPointerPress.started += OnRightPress;
            rightPointerPress.canceled += OnRightRelease;
        }
        if (switchModeAction != null) switchModeAction.performed += OnSwitchModeInput;
        scrollYAction.Enable();
        middleClickAction.Enable();
        middleClickAction.performed += OnSwitchModeInput;
        PetAI.OnPetScaleChanged += HandlePetGrowth;
    }

    private void OnDisable()
    {
        if (pointerPress != null)
        {
            pointerPress.started -= OnPress;
            pointerPress.canceled -= OnRelease;
        }
        if (rightPointerPress != null)
        {
            rightPointerPress.started -= OnRightPress;
            rightPointerPress.canceled -= OnRightRelease;
        }
        if (switchModeAction != null) switchModeAction.performed -= OnSwitchModeInput;
        playerActionMap?.Disable();
        scrollYAction?.Disable();
        middleClickAction?.Disable();
        if (middleClickAction != null) middleClickAction.performed -= OnSwitchModeInput;
        PetAI.OnPetScaleChanged -= HandlePetGrowth;

        isLeftDown = false;
        isRightDown = false;
        blockBoth = false;
        effectManager?.StopAllEffects();
    }

    private void HandlePetGrowth(float petScaleMultiplier)
    {
        float actualMultiplier = 1f + ((petScaleMultiplier - 1f) * growthSyncRatio);
        _targetScale = _baseScale * actualMultiplier;
        _targetCameraOffset = _baseCameraOffset * actualMultiplier;
        maxSpeed = _baseMaxSpeed * actualMultiplier;
        sweepRadius = _baseSweepRadius * actualMultiplier;
        sweepOffset = _baseSweepOffset * actualMultiplier;
        wieght = _baseWeight * actualMultiplier;
        chargeCenterOffset = _baseChargeCenterOffset * actualMultiplier;
        forwardReachOffset = _baseForwardReachOffset * actualMultiplier;
        OnScaleChanged?.Invoke(actualMultiplier);
        effectManager?.ScaleEffects(actualMultiplier);
    }

    private void Update()
    {
        if (isBeingAbsorbed || pointerPosition == null) return;

        if (_currentRightSkillCooldown > 0f)
        {
            _currentRightSkillCooldown -= Time.deltaTime * _demoCooldownRecoveryMultiplier;
            if (_currentRightSkillCooldown <= 0f) _currentRightSkillCooldown = 0f;
            OnRightSkillCooldownUpdate?.Invoke(_currentRightSkillCooldown, rightSkillCooldown);
        }

        float scrollValue = scrollYAction.ReadValue<float>();
        if (Mathf.Abs(scrollValue) > 0.1f) ToggleSkillMode();

        if (_vfxCooldownTimer > 0f) _vfxCooldownTimer -= Time.deltaTime;
        if (_absorbCooldown > 0f) _absorbCooldown -= Time.deltaTime;

        if (transform.localScale != _targetScale)
            transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.deltaTime * growthLerpSpeed);

        if (cameraOffset != _targetCameraOffset)
            cameraOffset = Vector3.Lerp(cameraOffset, _targetCameraOffset, Time.deltaTime * growthLerpSpeed);

        Vector2 pointerWorld = ScreenToWorld(pointerPosition.ReadValue<Vector2>());
        Vector2 center = GetSweepCenter();

        if (pointerPress.IsPressed() && !blockBoth)
        {
            Vector2 drag = pointerWorld - dragStart;
            float len = drag.magnitude;
            float effectiveMax = GetEffectiveMaxSpeed();
            float maxVisualLen = effectiveMax / 3f;
            Vector2 visualDrag = (len > 0.001f) ? (drag / len) * Mathf.Min(len, maxVisualLen) : Vector2.zero;
            effectManager.UpdateDragLine(center, center + visualDrag);
        }

        if (isRightDown && !blockBoth)
        {
            float hold = Mathf.Clamp(Time.time - rightPressStartTime, 0f, maxChargeTime);
            float t = Mathf.Clamp01(hold / maxChargeTime);
            Vector2 toPointer = pointerWorld - center;
            if (toPointer.sqrMagnitude > 0.0001f) chargedDir = toPointer.normalized;
            Vector2 origin = center + chargedDir * chargeCenterOffset;
            OnChargedSweepUpdate?.Invoke(hold, t, origin, chargedDir);
            effectManager.ShowChargeSweep(origin, chargedDir, t);
        }
        else
        {
            effectManager.HideChargeSweep();
        }
    }

    private void FixedUpdate()
    {
        if (isBlocking || isBeingAbsorbed)
        {
            rb.linearVelocity = Vector2.zero;
            effectManager.StopTrail();
            return;
        }

        Vector2 center = GetSweepCenter();
        Vector2 velocityVector = Vector2.zero;
        float sweepPower = 0f;

        if (currentSpeed > 0.01f)
        {
            velocityVector = moveDir * currentSpeed;

            // [重點註釋] 邏輯校正：使用 2.5D 的真實判定中心 (sweepCenter) 與真實半徑 (bodyCollisionRadius) 撞擊牆壁
            Vector2 nextCenter = center + velocityVector * Time.fixedDeltaTime;
            Vector2 safeCenter = nextCenter;
            Vector2 tempVel = velocityVector;

            if (WorldBounds2D.TryHandlePlayerCollision(nextCenter, moveDir, ref safeCenter, ref tempVel, out var hitPoint, out var hitNormal, bodyCollisionRadius))
            {
                // 反向推算出正確的腳底位置 (rb.position) 確保視覺不脫節
                rb.position = safeCenter - sweepOffset;
                rb.linearVelocity = Vector2.zero;
                effectManager.PlayWallHit(hitPoint, hitNormal);
                OnWallHitEvent?.Invoke();
                RequestCameraShake(wallCameraShake, 0.14f);
                TriggerHitStop(hitStopDuration, hitStopTimeScale);
                currentSpeed = 0f;
                effectManager.StopTrail();
                return;
            }

            Vector2 nextPos = safeCenter - sweepOffset;
            bool blockedByHeavy = false;
            nextPos = ResolveObstaclePenetrations(nextPos, ref blockedByHeavy);
            rb.position = nextPos;

            if (blockedByHeavy)
            {
                currentSpeed = 0f;
                rb.linearVelocity = Vector2.zero;
                effectManager.StopTrail();
            }
            else
            {
                rb.linearVelocity = velocityVector;
                float damping = 1f - (deceleration * Time.fixedDeltaTime);
                currentSpeed *= Mathf.Clamp01(damping);
                if (currentSpeed < 0.05f) currentSpeed = 0f;

                float effectiveMax = GetEffectiveMaxSpeed();
                sweepPower = Mathf.Clamp01(currentSpeed / effectiveMax);
                effectManager.UpdateTrail(center, moveDir, sweepPower, true);
                OnSweepMove?.Invoke(GetDynamicSweepCenter(), GetEffectiveSweepRadius(), moveDir, sweepPower);
            }
        }
        else
        {
            rb.linearVelocity = Vector2.zero;
            effectManager.StopTrail();
        }
    }

    private Vector2 ResolveObstaclePenetrations(Vector2 projectedPos, ref bool blocked)
    {
        if (currentMode == BroomMode.Sticky || SpatialGridManager.Instance == null)
            return projectedPos;

        Vector2 sweepCenter = projectedPos + sweepOffset;
        SpatialGridManager.Instance.GetTrashAroundPosition(sweepCenter, _nearbyObstacles);

        for (int i = 0; i < _nearbyObstacles.Count; i++)
        {
            BaseTrash trash = _nearbyObstacles[i];
            if (trash == null || trash.IsAbsorbing || trash._isStuck) continue;

            float massRatio = trash.Weight / Mathf.Max(0.01f, wieght);
            bool isHeavy = massRatio >= heavyTrashWeightRatio;

            for (int n = 0; n < trash.collisionNodes.Length; n++)
            {
                Vector2 trashNodePos = (Vector2)trash.transform.position + trash.collisionNodes[n];
                Vector2 toTrash = trashNodePos - sweepCenter;
                float distSqr = toTrash.sqrMagnitude;
                float minDist = bodyCollisionRadius + trash.collisionCheckRadius;

                if (distSqr < minDist * minDist)
                {
                    float dist = Mathf.Sqrt(distSqr);
                    float penetration = minDist - dist;
                    Vector2 pushDir = (dist > 0.001f) ? -toTrash / dist : -moveDir;
                    if (pushDir == Vector2.zero) pushDir = Vector2.up;

                    if (isHeavy)
                    {
                        projectedPos += pushDir * penetration;
                        sweepCenter = projectedPos + sweepOffset;
                        blocked = true;
                    }
                    else
                    {
                        Vector2 correction = -pushDir * penetration;
                        trash.transform.position = (Vector2)trash.transform.position + correction;
                        float power = Mathf.Clamp01(currentSpeed / Mathf.Max(GetEffectiveMaxSpeed(), 0.01f));
                        trash.ApplyBroomHit(moveDir, power);
                    }
                }
            }
        }
        return projectedPos;
    }

    private void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        UpdateCameraFollow();
    }

    private void UpdateCameraFollow()
    {
        Vector3 lookAheadTarget = GetCameraLookAheadTarget();

        if (cameraLookAheadSmoothTime <= 0f)
        {
            _cameraLookAheadOffset = lookAheadTarget;
            _cameraLookAheadVelocity = Vector3.zero;
        }
        else
        {
            _cameraLookAheadOffset = Vector3.SmoothDamp(
                _cameraLookAheadOffset, lookAheadTarget, ref _cameraLookAheadVelocity, cameraLookAheadSmoothTime);
        }

        Vector3 desiredPosition = transform.position + cameraOffset + _cameraLookAheadOffset;
        Vector3 currentCameraBase = cam.transform.position - _lastCameraShakeOffset;
        Vector3 cameraBasePosition;

        if (cameraFollowSmoothTime <= 0f)
        {
            cameraBasePosition = desiredPosition;
            _cameraFollowVelocity = Vector3.zero;
        }
        else
        {
            cameraBasePosition = Vector3.SmoothDamp(
                currentCameraBase, desiredPosition, ref _cameraFollowVelocity, cameraFollowSmoothTime);
        }

        _lastCameraShakeOffset = GetCameraShakeOffset();
        cam.transform.position = cameraBasePosition + _lastCameraShakeOffset;
    }

    private Vector3 GetCameraLookAheadTarget()
    {
        if (isBeingAbsorbed || isBlocking || currentSpeed <= cameraLookAheadSpeedThreshold || moveDir.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        float effectiveMax = Mathf.Max(GetEffectiveMaxSpeed(), 0.01f);
        float speedRatio = Mathf.Clamp01(currentSpeed / effectiveMax);
        Vector2 offset = moveDir * (cameraLookAheadDistance * speedRatio);
        return new Vector3(offset.x, offset.y, 0f);
    }

    private void ResetCameraMotion()
    {
        if (cam != null) cam.transform.position -= _lastCameraShakeOffset;
        _cameraFollowVelocity = Vector3.zero;
        _cameraLookAheadOffset = Vector3.zero;
        _cameraLookAheadVelocity = Vector3.zero;
        _lastCameraShakeOffset = Vector3.zero;
        _cameraShakeUntil = 0f;
        _cameraShakeAmplitude = 0f;
    }

    private void OnSwitchModeInput(InputAction.CallbackContext ctx) => ToggleSkillMode();

    private void ToggleSkillMode()
    {
        if (Time.time - _lastSwitchTime < switchCooldown) return;
        _lastSwitchTime = Time.time;
        currentMode = (currentMode == BroomMode.Impact) ? BroomMode.Sticky : BroomMode.Impact;
        _currentStickyLoad = 0f;
        OnModeChanged?.Invoke(currentMode);
    }

    private void OnPress(InputAction.CallbackContext ctx)
    {
        if (isBeingAbsorbed) return;
        isLeftDown = true;
        OnLeftPressAction?.Invoke();
        if (isRightDown) { BlockBoth(); return; }
        effectManager.StopTrail();
        dragStart = ScreenToWorld(pointerPosition.ReadValue<Vector2>());
        currentSpeed = 0f;
        rb.linearVelocity = Vector2.zero;
        Vector2 c = GetSweepCenter();
        effectManager.UpdateDragLine(c, c);
    }

    private void OnRelease(InputAction.CallbackContext ctx)
    {
        isLeftDown = false;
        OnLeftReleaseAction?.Invoke();
        if (isBeingAbsorbed) return;
        if (blockBoth) { if (!isRightDown) UnblockBoth(); return; }
        effectManager.HideDragLine();
        Vector2 drag = ScreenToWorld(pointerPosition.ReadValue<Vector2>()) - dragStart;
        float len = drag.magnitude;
        if (len >= 0.05f)
        {
            moveDir = drag / len;
            float effectiveMax = GetEffectiveMaxSpeed();
            currentSpeed = Mathf.Min(len * 3f, effectiveMax);
            effectManager.StartFollowParticle(transform);
        }
    }

    private void OnRightPress(InputAction.CallbackContext ctx)
    {
        if (isBeingAbsorbed || _currentRightSkillCooldown > 0f) return;
        PlayerAnimatorController.instance?.stateMachine.ChangeState(PlayerAnimatorController.instance.powerState);
        rb.linearVelocity = Vector2.zero;
        isRightDown = true;
        OnRightPressStart?.Invoke();
        rightPressStartTime = Time.time;
        effectManager.StopTrail();
        if (isLeftDown) { BlockBoth(); return; }
    }

    private void OnRightRelease(InputAction.CallbackContext ctx)
    {
        if (!isRightDown) return;
        isRightDown = false;
        if (isBeingAbsorbed) return;
        if (blockBoth) { if (!isLeftDown) UnblockBoth(); return; }

        float hold = Mathf.Clamp(Time.time - rightPressStartTime, 0f, maxChargeTime);
        float t = Mathf.Clamp01(hold / maxChargeTime);
        Vector2 p = ScreenToWorld(pointerPosition.ReadValue<Vector2>());
        Vector2 c = GetSweepCenter();
        Vector2 dir = p - c;

        if (PlayerAnimatorController.instance != null)
        {
            PlayerAnimatorController.instance.anim.SetFloat("ClickX", dir.x);
            PlayerAnimatorController.instance.anim.SetFloat("ClickY", dir.y);
            PlayerAnimatorController.instance.stateMachine.ChangeState(PlayerAnimatorController.instance.releaseState);
        }

        if (dir.sqrMagnitude > 0.0001f) chargedDir = dir.normalized;
        Vector2 origin = c + chargedDir * chargeCenterOffset;

        OnChargedSweepReleased?.Invoke(hold, t, origin, chargedDir);
        if (t >= 0.98f)
        {
            RequestCameraShake(fullChargeCameraShake, 0.22f);
            TriggerHitStop(fullChargeHitStopDuration, hitStopTimeScale);
        }
        effectManager.HideChargeSweep();
        _currentRightSkillCooldown = rightSkillCooldown;
        OnRightSkillCooldownUpdate?.Invoke(_currentRightSkillCooldown, rightSkillCooldown);
    }

    private void BlockBoth()
    {
        blockBoth = true;
        rb.linearVelocity = Vector2.zero;
        currentSpeed = 0f;
        effectManager.StopAllEffects();
        ResetCameraMotion();
    }

    private void UnblockBoth() => blockBoth = false;

    public void EmitTrashHit(Vector2 hitPoint, Vector2 hitNormal, TrashType type)
    {
        if (_vfxCooldownTimer > 0f) return;
        effectManager.PlayTrashHit(hitPoint, hitNormal);
        OnTrashHitEvent?.Invoke(type);
        RequestCameraShake(hitCameraShake, 0.09f);
        TriggerHitStop(hitStopDuration, hitStopTimeScale);
        _vfxCooldownTimer = vfxHitCooldown;
    }

    private void RequestCameraShake(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f) return;
        _cameraShakeAmplitude = Mathf.Max(_cameraShakeAmplitude, amplitude);
        _cameraShakeUntil = Mathf.Max(_cameraShakeUntil, Time.unscaledTime + duration);
    }

    private Vector3 GetCameraShakeOffset()
    {
        float remaining = _cameraShakeUntil - Time.unscaledTime;
        if (remaining <= 0f)
        {
            _cameraShakeAmplitude = 0f;
            return Vector3.zero;
        }

        float fade = Mathf.Clamp01(remaining / 0.22f);
        Vector2 offset = UnityEngine.Random.insideUnitCircle * (_cameraShakeAmplitude * fade);
        return new Vector3(offset.x, offset.y, 0f);
    }

    private void TriggerHitStop(float duration, float targetTimeScale)
    {
        if (duration <= 0f || Time.timeScale <= 0f) return;

        _hitStopUntil = Mathf.Max(_hitStopUntil, Time.unscaledTime + duration);
        targetTimeScale = Mathf.Clamp(targetTimeScale, 0.01f, 1f);

        if (_hitStopRoutine != null)
        {
            _activeHitStopScale = Mathf.Min(_activeHitStopScale, targetTimeScale);
            Time.timeScale = _activeHitStopScale;
            return;
        }

        _timeScaleBeforeHitStop = Time.timeScale;
        _activeHitStopScale = Mathf.Min(Time.timeScale, targetTimeScale);
        Time.timeScale = _activeHitStopScale;
        _hitStopRoutine = StartCoroutine(HitStopRoutine());
    }

    private IEnumerator HitStopRoutine()
    {
        while (Time.unscaledTime < _hitStopUntil) yield return null;

        // 若暫停選單已把 timeScale 改為 0，就交給 UIManager 恢復，不越權覆蓋。
        if (Mathf.Approximately(Time.timeScale, _activeHitStopScale))
        {
            Time.timeScale = _timeScaleBeforeHitStop;
        }

        _hitStopRoutine = null;
        _activeHitStopScale = 1f;
    }

    public void ExitBlackHole(Vector2 ejectDir, float ejectSpeed)
    {
        _absorbCooldown = absorbInvincibleTime;
        isBeingAbsorbed = false;
        blockBoth = false;
        isBlocking = false;
        isLeftDown = false;
        isRightDown = false;
        _currentStickyLoad = 0f;
        moveDir = ejectDir;
        currentSpeed = ejectSpeed;
        ResetCameraMotion();
        transform.localScale = Vector3.zero;
        SetCollidersEnabled(true);
        rb.linearVelocity = moveDir * currentSpeed;
        effectManager.HideDragLine();
        effectManager.HideChargeSweep();
    }

    public void ApplyHitSlowdown(float totalTrashWieght, float sweepPower01)
    {
        if (currentSpeed <= 0f || totalTrashWieght <= 0f || currentMode == BroomMode.Sticky) return;
        float M = Mathf.Max(0.01f, wieght);
        float p = scaleHitBySweepPower ? Mathf.Lerp(minHitPower, 1f, Mathf.Clamp01(sweepPower01)) : 1f;
        float m = Mathf.Max(0f, totalTrashWieght) * hitWeightScale * p;
        if (m <= 0f) return;
        float e = Mathf.Clamp01(sweepRestitution);
        float ratio = (M - e * m) / (M + m);
        if (!allowBounceBack) currentSpeed *= Mathf.Clamp01(ratio);
        else
        {
            if (ratio < 0f) { moveDir = -moveDir; currentSpeed *= Mathf.Clamp01(-ratio); }
            else currentSpeed *= Mathf.Clamp01(ratio);
        }

        if (currentSpeed <= 0.01f)
        {
            currentSpeed = 0f;
            rb.linearVelocity = Vector2.zero;
            effectManager.StopTrail();
        }
        else rb.linearVelocity = moveDir * currentSpeed;
    }

    private Vector2 ScreenToWorld(Vector2 p)
    {
        if (cam == null) return Vector2.zero;
        Ray ray = cam.ScreenPointToRay(p);
        Plane groundPlane = new Plane(Vector3.back, Vector3.zero);
        if (groundPlane.Raycast(ray, out float enter)) return ray.GetPoint(enter);
        return cam.ScreenToWorldPoint(new Vector3(p.x, p.y, Mathf.Abs(cam.transform.position.z)));
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (_areCollidersEnabled == enabled) return;
        _areCollidersEnabled = enabled;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null && colliders[i].enabled != enabled)
                colliders[i].enabled = enabled;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GetSweepCenter(), sweepRadius);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(GetSweepCenter(), bodyCollisionRadius);
        if (Application.isPlaying)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            Gizmos.DrawWireSphere(GetDynamicSweepCenter(), GetEffectiveSweepRadius());
        }
    }
#endif
}
