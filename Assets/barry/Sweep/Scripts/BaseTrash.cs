using System.Collections.Generic;
using UnityEngine;

public class BaseTrash : BasePoolItem, IAbsorbable
{
    [Header("基礎數值")]
    [SerializeField] protected float absorbEffectDuration;
    [SerializeField] protected float rotationSpeed;

    [SerializeField] public TrashType trashType;

    [Tooltip("垃圾的階級/大小。寵物需達到對應的 maxEatTier 才能吃下它")]
    [SerializeField] public int trashTier = 1;

    [SerializeField] protected int scoreValue = 10;

    [Header("動畫曲線")]
    [SerializeField] protected AnimationCurve scaleCurve;
    [SerializeField] protected AnimationCurve moveCurve;

    [Header("物理屬性")]
    [SerializeField] private Vector2 currentVelocity;
    [SerializeField] private float deceleration;
    [SerializeField] private float viewportPadding;

    [Header("受力設定")]
    [SerializeField] private float broomForce;
    [SerializeField] private float trashForce;
    [SerializeField, Min(0.01f)] private float weight = 1f;

    [Header("碰撞檢測 (串珠節點法)")]
    [Tooltip("新增多個節點來拼湊出長條形。如果只有一個點，就是普通的圓形。")]
    [SerializeField] public Vector2[] collisionNodes = { Vector2.zero };
    [SerializeField] public float collisionCheckRadius = 0.45f;
    [SerializeField] private float hitCooldown = 0.2f;
    [SerializeField] private float collisionDamping = 0.8f;
    [SerializeField] private float minCollisionSpeed = 0.1f;

    [Header("睡眠優化")]
    [SerializeField] private float sleepSpeedThreshold;
    [SerializeField] private float sleepTime;

    [Header("Sticky 模式專用")]
    [SerializeField] private float stickSmoothTime = 0.05f;

    [HideInInspector] public bool isDynamicSpawned = false;

    public float AbsorbEffectDuration => absorbEffectDuration;
    public float RotationSpeed => rotationSpeed;
    public bool IsAbsorbing { get; private set; }
    public Vector2 CurrentVelocity => currentVelocity;
    public float Weight => weight;
    public float InvWeight => 1f / weight;
    public int ScoreValue => scoreValue;

    private bool _isRecentlyHit;
    private float _hitCooldownTimer;
    private bool _isSleeping;
    private float _sleepTimer;

    public bool _isStuck;
    private Vector2 _stickVelocitySmooth;

    private readonly List<BaseTrash> _nearbyTrash = new List<BaseTrash>(32);
    private readonly List<BaseTrash> _overlapCandidates = new List<BaseTrash>(32);
    private readonly HashSet<BaseTrash> _overlapCandidateSet = new HashSet<BaseTrash>();
    private Collider2D[] _colliders;
    private Transform _transform;
    private bool _areCollidersEnabled = true;
    private float _lastPlayerInfluenceTime = float.NegativeInfinity;
    private const int MAX_SUB_STEPS = 8;
    private const int MAX_BOUNDARY_SOLVER_PASSES = 8;
    private const float BOUNDARY_SEPARATION_EPSILON = 0.0001f;

    public bool CanBeAbsorbed => !IsAbsorbing && !_isStuck;

    public bool WasPlayerInfluencedRecently(float maxAgeSeconds)
    {
        return Time.time - _lastPlayerInfluenceTime <= Mathf.Max(0f, maxAgeSeconds);
    }

    protected virtual void Awake()
    {
        _transform = transform;
        _colliders = GetComponentsInChildren<Collider2D>(true);
    }

    public void OnAbsorbStart(BlackHoleObstacle blackHole)
    {
        BeginAbsorb(true, blackHole);
    }

    public void OnEnterBlackHole()
    {
        BeginAbsorb(false, null);
    }

    private void BeginAbsorb(bool registerWithBlackHole, BlackHoleObstacle blackHole)
    {
        if (IsAbsorbing) return;

        IsAbsorbing = true;
        NotifyDynamicConsumption();

        WakeUp();
        currentVelocity = Vector2.zero;
        SetCollidersEnabled(false);
        TrashCounter.MarkCollected(this);

        if (registerWithBlackHole && blackHole != null)
        {
            blackHole.RegisterTrash(this);
        }
    }

    private void NotifyDynamicConsumption()
    {
        if (isDynamicSpawned && DynamicSpawnManager.Instance != null)
        {
            DynamicSpawnManager.Instance.OnDynamicTrashConsumed();
            isDynamicSpawned = false;
        }
    }

    protected virtual void FixedUpdate()
    {
        if (IsAbsorbing) return;
        if (_isStuck)
        {
            currentVelocity = Vector2.zero;
            return;
        }

        if (_isRecentlyHit)
        {
            _hitCooldownTimer -= Time.fixedDeltaTime;
            if (_hitCooldownTimer <= 0f)
            {
                _hitCooldownTimer = 0f;
                _isRecentlyHit = false;
            }
        }

        if (_isSleeping) return;

        Vector2 localPos = _transform.position;
        HandleMovement(ref localPos);
        _transform.position = localPos;

        HandleSleepCheck();
    }

    public void ApplyMagnetHold(Vector2 targetPosition, Vector2 hostVelocity)
    {
        if (IsAbsorbing) return;

        MarkPlayerInfluence();

        if (!_isStuck)
        {
            _isStuck = true;
            _isSleeping = false;
            _stickVelocitySmooth = Vector2.zero;
            currentVelocity = Vector2.zero;
            SetCollidersEnabled(false);
        }

        if (_colliders != null && _colliders.Length > 0 && _colliders[0] != null && _colliders[0].enabled)
        {
            SetCollidersEnabled(false);
        }

        Vector2 currentPos = _transform.position;
        float actualSmoothTime = (currentPos - targetPosition).sqrMagnitude > 1f ? stickSmoothTime * 0.5f : stickSmoothTime;
        Vector2 newPos = Vector2.SmoothDamp(currentPos, targetPosition, ref _stickVelocitySmooth, actualSmoothTime, Mathf.Infinity, Time.fixedDeltaTime);

        HandleBoundaryCheck(ref newPos);
        _transform.position = newPos;
    }

    public void ReleaseMagnetHold()
    {
        if (!_isStuck) return;

        _isStuck = false;
        SetCollidersEnabled(true);
        currentVelocity = _stickVelocitySmooth;
    }

    private void HandleMovement(ref Vector2 currentPos)
    {
        if (currentVelocity.sqrMagnitude < 0.0001f)
        {
            currentVelocity = Vector2.zero;
            ResolveOverlap(ref currentPos);
            HandleBoundaryCheck(ref currentPos);
            return;
        }

        float remainingTime = Time.fixedDeltaTime;
        float radius = collisionCheckRadius;
        float maxStepDist = radius * 0.5f;
        int safetyLoopCount = 0;

        while (remainingTime > 0.00001f && safetyLoopCount < MAX_SUB_STEPS)
        {
            safetyLoopCount++;

            float speed = currentVelocity.magnitude;
            if (speed <= 0.0001f)
            {
                currentVelocity = Vector2.zero;
                break;
            }

            float stepTime = remainingTime;
            float stepDist = speed * stepTime;

            if (stepDist > maxStepDist)
            {
                stepTime = maxStepDist / speed;
                stepDist = maxStepDist;
            }

            Vector2 stepDelta = currentVelocity * stepTime;
            Vector2 targetPos = currentPos + stepDelta;
            float moveDist = stepDist;

            bool hasCollision = CheckPathCollision(
                currentPos,
                targetPos,
                ref moveDist,
                out BaseTrash hitTrash,
                out Vector2 hitNormal);

            if (moveDist > 0f)
            {
                currentPos += (stepDelta / stepDist) * moveDist;
            }

            if (hasCollision && hitTrash != null && !hitTrash._isRecentlyHit)
            {
                if (!hitTrash._isStuck)
                {
                    Vector2 hitDir = hitNormal;
                    if (hitDir.sqrMagnitude < 0.0001f) hitDir = Random.insideUnitCircle;
                    hitDir.Normalize();

                    Vector2 relVel = currentVelocity - hitTrash.CurrentVelocity;
                    float relSpeed = Vector2.Dot(relVel, hitDir);

                    if (relSpeed > minCollisionSpeed)
                    {
                        ResolveTrashCollision(hitTrash, hitDir);
                        StartHitCooldownTimer();
                        hitTrash.StartHitCooldownTimer();
                    }
                }
            }

            HandleBoundaryCheck(ref currentPos);

            currentVelocity = Vector2.Lerp(currentVelocity, Vector2.zero, GetVelocityDamping(stepTime));
            remainingTime -= stepTime;
        }

        ResolveOverlap(ref currentPos);
        HandleBoundaryCheck(ref currentPos);
    }

    private float GetVelocityDamping(float deltaTime)
    {
        float effectiveWeight = Mathf.Max(weight, 0.01f);
        float drag = Mathf.Max(0f, deceleration) * Mathf.Sqrt(effectiveWeight);
        if (drag <= 0f) return 0f;

        return 1f - Mathf.Exp(-drag * deltaTime);
    }

    private bool CheckPathCollision(
        Vector2 start,
        Vector2 end,
        ref float moveDist,
        out BaseTrash hitTrash,
        out Vector2 hitNormal)
    {
        hitTrash = null;
        hitNormal = Vector2.zero;
        if (collisionNodes == null || collisionNodes.Length == 0) return false;

        SpatialGridManager grid = SpatialGridManager.Instance;
        if (grid == null) return false;

        float r1 = collisionCheckRadius;
        Vector2 dir = end - start;
        float len = dir.magnitude;
        if (len <= 0.0001f) return false;
        dir /= len;

        float maxTravel = Mathf.Min(len, moveDist);
        float bestDist = maxTravel;
        bool foundHit = false;

        for (int myNodeIdx = 0; myNodeIdx < collisionNodes.Length; myNodeIdx++)
        {
            Vector2 startCenter = start + collisionNodes[myNodeIdx];
            grid.GetTrashAroundPosition(
                startCenter,
                _nearbyTrash,
                r1 + grid.MaxRegisteredTrashExtent);

            for (int i = 0; i < _nearbyTrash.Count; i++)
            {
                var trash = _nearbyTrash[i];
                if (trash == null || trash == this || trash.IsAbsorbing || trash._isRecentlyHit || trash._isStuck) continue;
                if (trash.collisionNodes == null || trash.collisionNodes.Length == 0) continue;

                float r2 = trash.collisionCheckRadius;
                float combined = r1 + r2;

                for (int theirNodeIdx = 0; theirNodeIdx < trash.collisionNodes.Length; theirNodeIdx++)
                {
                    Vector2 theirCenter = (Vector2)trash._transform.position + trash.collisionNodes[theirNodeIdx];
                    Vector2 toTrash = theirCenter - startCenter;
                    float proj = Vector2.Dot(toTrash, dir);

                    if (proj < 0f || proj > maxTravel + combined) continue;

                    float sqLine = toTrash.sqrMagnitude - proj * proj;
                    if (sqLine > combined * combined) continue;

                    float offset = Mathf.Sqrt(Mathf.Max(combined * combined - sqLine, 0f));
                    float hitAlong = proj - offset;

                    if (hitAlong < 0f) hitAlong = proj;
                    if (hitAlong < 0f || hitAlong > bestDist) continue;

                    bestDist = hitAlong;
                    hitTrash = trash;
                    Vector2 myHitCenter = startCenter + dir * hitAlong;
                    hitNormal = theirCenter - myHitCenter;
                    foundHit = true;
                }
            }
        }

        if (foundHit)
        {
            moveDist = Mathf.Max(0f, bestDist);
            return true;
        }

        return false;
    }

    private void ResolveTrashCollision(BaseTrash other, Vector2 normal)
    {
        Vector2 v1 = currentVelocity;
        Vector2 v2 = other.currentVelocity;
        Vector2 relVel = v1 - v2;
        float relSpeedN = Vector2.Dot(relVel, normal);

        if (relSpeedN <= minCollisionSpeed) return;

        // 玩家打出的垃圾形成連鎖碰撞時，後續垃圾也保留同一份玩家功勞。
        float sharedInfluenceTime = Mathf.Max(_lastPlayerInfluenceTime, other._lastPlayerInfluenceTime);
        _lastPlayerInfluenceTime = sharedInfluenceTime;
        other._lastPlayerInfluenceTime = sharedInfluenceTime;

        float e = Mathf.Clamp01(collisionDamping);
        float invSum = InvWeight + other.InvWeight;
        if (invSum <= 0f) return;

        float j = (1f + e) * relSpeedN / invSum;
        Vector2 impulse = j * normal;

        currentVelocity -= impulse * InvWeight;
        other.currentVelocity += impulse * other.InvWeight;
        other.WakeUp();
    }

    private void ResolveOverlap(ref Vector2 pos)
    {
        if (_isStuck || collisionNodes == null || collisionNodes.Length == 0) return;

        PlayerController player = PlayerController.instance;
        if (player != null && player.currentMode == BroomMode.Impact && !player.isBeingAbsorbed)
        {
            Vector2 playerCenter = player.GetSweepCenter();
            float playerRadius = player.bodyCollisionRadius;
            Vector2 deepestPlayerPush = Vector2.zero;
            float deepestPlayerPenetration = 0f;

            for (int n = 0; n < collisionNodes.Length; n++)
            {
                Vector2 myCenter = pos + collisionNodes[n];
                Vector2 toPlayer = playerCenter - myCenter;
                float distSqr = toPlayer.sqrMagnitude;
                float minDist = playerRadius + collisionCheckRadius;

                if (distSqr < minDist * minDist)
                {
                    float dist = Mathf.Sqrt(distSqr);
                    float penetration = minDist - dist;
                    Vector2 pushDir = (dist > 0.001f) ? -toPlayer / dist : Vector2.down;
                    if (penetration > deepestPlayerPenetration)
                    {
                        deepestPlayerPenetration = penetration;
                        deepestPlayerPush = pushDir * penetration;
                    }
                }
            }

            pos += deepestPlayerPush;
        }

        SpatialGridManager grid = SpatialGridManager.Instance;
        if (grid == null) return;

        float r1 = collisionCheckRadius;
        _overlapCandidates.Clear();
        _overlapCandidateSet.Clear();
        for (int myNodeIdx = 0; myNodeIdx < collisionNodes.Length; myNodeIdx++)
        {
            Vector2 myCenter = pos + collisionNodes[myNodeIdx];
            grid.GetTrashAroundPosition(
                myCenter,
                _nearbyTrash,
                r1 + grid.MaxRegisteredTrashExtent);
            for (int i = 0; i < _nearbyTrash.Count; i++)
            {
                var trash = _nearbyTrash[i];
                if (trash == null || trash == this || trash.IsAbsorbing || trash._isStuck) continue;
                if (trash.collisionNodes == null || trash.collisionNodes.Length == 0) continue;
                if (_overlapCandidateSet.Add(trash)) _overlapCandidates.Add(trash);
            }
        }

        // A compound collider pair receives one minimum-translation correction per
        // solver pass. Applying a full correction for every bead pair causes a
        // 4-6 bead object to explode away from a contact.
        for (int solverPass = 0; solverPass < 2; solverPass++)
        {
            bool correctedAny = false;
            for (int candidateIndex = 0; candidateIndex < _overlapCandidates.Count; candidateIndex++)
            {
                BaseTrash trash = _overlapCandidates[candidateIndex];
                if (trash == null || trash.IsAbsorbing || trash._isStuck) continue;

                float minDist = r1 + trash.collisionCheckRadius;
                float minDistSqr = minDist * minDist;
                float deepestPenetration = 0f;
                Vector2 deepestPushDirection = Vector2.zero;

                for (int myNodeIdx = 0; myNodeIdx < collisionNodes.Length; myNodeIdx++)
                {
                    Vector2 myCenter = pos + collisionNodes[myNodeIdx];
                    for (int theirNodeIdx = 0; theirNodeIdx < trash.collisionNodes.Length; theirNodeIdx++)
                    {
                        Vector2 theirCenter = (Vector2)trash._transform.position + trash.collisionNodes[theirNodeIdx];
                        Vector2 delta = myCenter - theirCenter;
                        float sqr = delta.sqrMagnitude;

                        if (sqr >= minDistSqr) continue;
                        if (sqr <= 0.000001f)
                        {
                            int stableSign = GetHashCode() < trash.GetHashCode() ? -1 : 1;
                            delta = Vector2.right * (0.001f * stableSign);
                            sqr = delta.sqrMagnitude;
                        }

                        float dist = Mathf.Sqrt(sqr);
                        float penetration = minDist - dist;
                        if (penetration <= deepestPenetration) continue;

                        deepestPenetration = penetration;
                        deepestPushDirection = delta / dist;
                    }
                }

                if (deepestPenetration <= 0f) continue;
                pos += deepestPushDirection * deepestPenetration;
                correctedAny = true;
            }

            if (!correctedAny) break;
        }
    }

    public float GetCollisionExtent()
    {
        float extent = collisionCheckRadius;
        if (collisionNodes == null) return extent;

        for (int i = 0; i < collisionNodes.Length; i++)
            extent = Mathf.Max(extent, collisionNodes[i].magnitude + collisionCheckRadius);
        return extent;
    }

    private void ApplyImpulse(Vector2 impulse)
    {
        currentVelocity += impulse * InvWeight;
    }

    public void ApplyExternalForce(Vector2 force)
    {
        ApplyExternalImpulse(force * Time.fixedDeltaTime);
    }

    public void ApplyExternalImpulse(Vector2 impulse)
    {
        if (IsAbsorbing || _isStuck) return;

        WakeUp();
        ApplyImpulse(impulse);
    }

    private void MarkPlayerInfluence()
    {
        _lastPlayerInfluenceTime = Time.time;
    }

    public void ApplyBroomHit(Vector2 hitDirection, float power)
    {
        if (IsAbsorbing || _isStuck) return;
        if (PlayerController.instance != null && PlayerController.instance.currentMode == BroomMode.Sticky) return;

        MarkPlayerInfluence();
        WakeUp();
        if (_isRecentlyHit) return;

        StartHitCooldownTimer();

        Vector2 dir = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : Vector2.right;
        PlayerController controller = PlayerController.instance;

        if (controller != null)
        {
            Vector2 primaryNode = collisionNodes != null && collisionNodes.Length > 0 ? collisionNodes[0] : Vector2.zero;
            Vector2 hitPoint = ((Vector2)_transform.position + primaryNode) - dir * collisionCheckRadius;
            controller.EmitTrashHit(hitPoint, -dir, trashType);
        }

        Vector2 impulse = dir * (broomForce * power);
        ApplyImpulse(impulse);
    }

    public void ApplyTrashHit(Vector2 hitDirection, float strength01)
    {
        if (IsAbsorbing || _isStuck) return;

        WakeUp();
        StartHitCooldownTimer();

        Vector2 dir = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : Vector2.right;
        float impulseMag = trashForce * Mathf.Clamp01(strength01);

        ApplyImpulse(dir * impulseMag);
        currentVelocity *= collisionDamping;
    }

    private void HandleSleepCheck()
    {
        float speed = currentVelocity.magnitude;
        if (speed < sleepSpeedThreshold && speed > 0f)
        {
            _sleepTimer += Time.fixedDeltaTime;
            if (_sleepTimer >= sleepTime)
            {
                currentVelocity = Vector2.zero;
                _isSleeping = true;
            }
        }
        else
        {
            _sleepTimer = 0f;
        }
    }

    private void WakeUp()
    {
        _isSleeping = false;
        _sleepTimer = 0f;
    }

    private void HandleBoundaryCheck(ref Vector2 pos)
    {
        if (collisionNodes == null || collisionNodes.Length == 0) return;

        // Resolve one coherent translation for the entire bead chain against each
        // boundary. Correcting beads one by one can make opposite beads push left
        // and right in the same frame, leaving a long object inside an obstacle.
        int passLimit = Mathf.Min(
            MAX_BOUNDARY_SOLVER_PASSES,
            Mathf.Max(2, WorldBounds2D.ActiveBounds.Count + 1));
        for (int pass = 0; pass < passLimit; pass++)
        {
            bool corrected = false;
            for (int i = 0; i < WorldBounds2D.ActiveBounds.Count; i++)
            {
                WorldBounds2D boundary = WorldBounds2D.ActiveBounds[i];
                if (boundary == null || !boundary.isActiveAndEnabled) continue;

                corrected |= boundary.boundsType == WorldBounds2D.BoundsType.Containment
                    ? ResolveCompoundContainment(boundary, ref pos)
                    : ResolveCompoundObstacle(boundary, ref pos);
            }

            if (!corrected) break;
        }
    }

    private bool ResolveCompoundContainment(WorldBounds2D boundary, ref Vector2 pos)
    {
        GetCollisionNodeExtents(out Vector2 nodeMin, out Vector2 nodeMax);
        Vector2 allowedMin = boundary.Min + Vector2.one * collisionCheckRadius - nodeMin;
        Vector2 allowedMax = boundary.Max - Vector2.one * collisionCheckRadius - nodeMax;
        bool corrected = false;

        if (allowedMin.x > allowedMax.x)
        {
            pos.x = (allowedMin.x + allowedMax.x) * 0.5f;
            currentVelocity.x = 0f;
            corrected = true;
        }
        else if (pos.x < allowedMin.x)
        {
            pos.x = allowedMin.x;
            BounceVelocity(Vector2.right);
            corrected = true;
        }
        else if (pos.x > allowedMax.x)
        {
            pos.x = allowedMax.x;
            BounceVelocity(Vector2.left);
            corrected = true;
        }

        if (allowedMin.y > allowedMax.y)
        {
            pos.y = (allowedMin.y + allowedMax.y) * 0.5f;
            currentVelocity.y = 0f;
            corrected = true;
        }
        else if (pos.y < allowedMin.y)
        {
            pos.y = allowedMin.y;
            BounceVelocity(Vector2.up);
            corrected = true;
        }
        else if (pos.y > allowedMax.y)
        {
            pos.y = allowedMax.y;
            BounceVelocity(Vector2.down);
            corrected = true;
        }

        return corrected;
    }

    private bool ResolveCompoundObstacle(WorldBounds2D boundary, ref Vector2 pos)
    {
        Vector2 inflatedMin = boundary.Min - Vector2.one * collisionCheckRadius;
        Vector2 inflatedMax = boundary.Max + Vector2.one * collisionCheckRadius;
        bool overlaps = false;

        for (int i = 0; i < collisionNodes.Length; i++)
        {
            Vector2 center = pos + collisionNodes[i];
            if (center.x > inflatedMin.x && center.x < inflatedMax.x &&
                center.y > inflatedMin.y && center.y < inflatedMax.y)
            {
                overlaps = true;
                break;
            }
        }

        if (!overlaps) return false;

        GetCollisionNodeExtents(out Vector2 nodeMin, out Vector2 nodeMax);
        Vector2 bestTranslation = new Vector2(
            inflatedMin.x - nodeMax.x - pos.x - BOUNDARY_SEPARATION_EPSILON,
            0f);
        Vector2 bestNormal = Vector2.left;
        float bestSqrMagnitude = bestTranslation.sqrMagnitude;

        ConsiderBoundaryTranslation(
            new Vector2(
                inflatedMax.x - nodeMin.x - pos.x + BOUNDARY_SEPARATION_EPSILON,
                0f),
            Vector2.right,
            ref bestTranslation,
            ref bestNormal,
            ref bestSqrMagnitude);
        ConsiderBoundaryTranslation(
            new Vector2(
                0f,
                inflatedMin.y - nodeMax.y - pos.y - BOUNDARY_SEPARATION_EPSILON),
            Vector2.down,
            ref bestTranslation,
            ref bestNormal,
            ref bestSqrMagnitude);
        ConsiderBoundaryTranslation(
            new Vector2(
                0f,
                inflatedMax.y - nodeMin.y - pos.y + BOUNDARY_SEPARATION_EPSILON),
            Vector2.up,
            ref bestTranslation,
            ref bestNormal,
            ref bestSqrMagnitude);

        pos += bestTranslation;
        BounceVelocity(bestNormal);
        return true;
    }

    private void ConsiderBoundaryTranslation(
        Vector2 candidate,
        Vector2 candidateNormal,
        ref Vector2 best,
        ref Vector2 bestNormal,
        ref float bestSqrMagnitude)
    {
        float candidateSqrMagnitude = candidate.sqrMagnitude;
        bool shorter = candidateSqrMagnitude < bestSqrMagnitude - 0.000001f;
        bool betterVelocityTie = Mathf.Abs(candidateSqrMagnitude - bestSqrMagnitude) <= 0.000001f &&
                                 Vector2.Dot(currentVelocity, candidateNormal) <
                                 Vector2.Dot(currentVelocity, bestNormal);
        if (!shorter && !betterVelocityTie) return;

        best = candidate;
        bestNormal = candidateNormal;
        bestSqrMagnitude = candidateSqrMagnitude;
    }

    private void GetCollisionNodeExtents(out Vector2 min, out Vector2 max)
    {
        min = collisionNodes[0];
        max = collisionNodes[0];
        for (int i = 1; i < collisionNodes.Length; i++)
        {
            min = Vector2.Min(min, collisionNodes[i]);
            max = Vector2.Max(max, collisionNodes[i]);
        }
    }

    private void BounceVelocity(Vector2 outwardNormal)
    {
        float normalSpeed = Vector2.Dot(currentVelocity, outwardNormal);
        if (normalSpeed >= 0f) return;

        currentVelocity -= outwardNormal *
                           ((1f + Mathf.Max(0f, collisionDamping)) * normalSpeed);
    }

    private void StartHitCooldownTimer()
    {
        _isRecentlyHit = true;
        _hitCooldownTimer = hitCooldown;
    }

    public override void ResetState()
    {
        base.ResetState();
        currentVelocity = Vector2.zero;
        IsAbsorbing = false;

        isDynamicSpawned = false;

        _transform.localScale = initialScale;
        _isRecentlyHit = false;
        _hitCooldownTimer = 0f;
        _isStuck = false;
        _stickVelocitySmooth = Vector2.zero;
        _lastPlayerInfluenceTime = float.NegativeInfinity;
        WakeUp();
        SetCollidersEnabled(true);
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (_colliders == null) return;
        if (_areCollidersEnabled == enabled) return;

        _areCollidersEnabled = enabled;

        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null && _colliders[i].enabled != enabled)
            {
                _colliders[i].enabled = enabled;
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = _isStuck ? Color.green : Color.yellow;
        if (collisionNodes == null || collisionNodes.Length == 0) return;

        foreach (var node in collisionNodes)
        {
            Gizmos.DrawWireSphere((Vector2)transform.position + node, collisionCheckRadius);
        }
    }
#endif
}
