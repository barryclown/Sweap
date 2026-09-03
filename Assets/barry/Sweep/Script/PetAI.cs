using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
public class PetAI : MonoBehaviour
{
    public static event System.Action<int> OnVomitPenalty;
    public static event System.Action<float> OnPetScaleChanged;
    public static event System.Action<int> OnPetLevelChanged;

    [System.Serializable]
    public struct PetLevelConfig
    {
        [Tooltip("升級到下一等所需累積吃掉的垃圾總量 (若為最高等則無視)")]
        public int requiredTotalTrash;
        [Tooltip("該等級的體積縮放比例")]
        public float scaleMultiplier;
        [Tooltip("該等級最高能吃的垃圾階級 (例如填 2，就能吃 tier 1 和 tier 2 的垃圾)")]
        public int maxEatTier;
        [Tooltip("該等級對應的動畫控制器 (若為 null 則保持當前)")]
        public RuntimeAnimatorController animatorController;
    }

    [Header("=== 1. 持續移動 (Moving) ===")]
    [SerializeField] private float minWalkDistance = 3f;
    [SerializeField] private float maxWalkDistance = 8f;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float wallPadding = 1.5f;
    [SerializeField] private float suckRadius = 6f;
    [SerializeField] private float suckForce = 20f;
    [SerializeField] private LayerMask trashLayer;

    [Header("=== 2. 飽腹嘔吐 (Vomit) ===")]
    [SerializeField] private List<TrashType> vomitTriggers;
    [SerializeField] private int vomitScorePenalty = 50;
    [SerializeField] private float vomitForce = 5f;

    [Header("=== 3. 成長系統 (Growth) ===")]
    [SerializeField] private float eatingPauseDuration = 1f;
    [SerializeField] private PetLevelConfig[] levelConfigs;

    [Header("=== 4. 動畫控制 (Animation) ===")]
    [SerializeField] private Animator petAnimator;

    private static readonly int AnimHashIsEating = Animator.StringToHash("IsEating");

    [Header("Debug 資訊")]
    [SerializeField] private PetState currentState;
    [SerializeField] private int currentLevel;
    [SerializeField] private int totalEaten;

    public enum PetState { Moving, Eating, Vomiting }

    public int CurrentLevel => currentLevel;
    public int CurrentMaxEatTier { get; private set; } = 1;

    private Rigidbody2D _rb;
    private float _stateTimer;
    private Vector2 _moveTarget;
    private ContactFilter2D _trashFilter;
    private readonly Collider2D[] _suckResults = new Collider2D[16];

    private readonly HashSet<TrashType> _vomitTriggersSet = new HashSet<TrashType>();
    private readonly List<TrashType> _stomachContents = new List<TrashType>();
    private LevelSpawner _cachedSpawner;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();

        if (petAnimator == null)
            petAnimator = GetComponentInChildren<Animator>();

        _trashFilter = new ContactFilter2D
        {
            layerMask = trashLayer,
            useLayerMask = true,
            useTriggers = true
        };

        if (vomitTriggers != null)
        {
            _vomitTriggersSet.UnionWith(vomitTriggers);
        }

#if UNITY_2021_3_18_OR_NEWER || UNITY_2022_2_OR_NEWER
        _cachedSpawner = UnityEngine.Object.FindAnyObjectByType<LevelSpawner>();
#else
        _cachedSpawner = UnityEngine.Object.FindObjectOfType<LevelSpawner>();
#endif
    }

    private void Start()
    {
        currentLevel = 0;
        totalEaten = 0;
        ApplyLevelData();

        OnPetLevelChanged?.Invoke(currentLevel);
        ChangeState(PetState.Moving);
    }

    private void Update()
    {
        UpdateStateMachine();
    }

    private void FixedUpdate()
    {
        if (currentState == PetState.Moving)
        {
            PerformSuckingPhysics();
        }
        EnsureWithinBounds();
    }

    #region --- 狀態機大腦 ---
    private void UpdateStateMachine()
    {
        switch (currentState)
        {
            case PetState.Moving:
                if (((Vector2)transform.position - _moveTarget).sqrMagnitude > 0.04f)
                {
                    Vector2 dir = (_moveTarget - (Vector2)transform.position).normalized;
                    _rb.linearVelocity = dir * moveSpeed;
                }
                else
                {
                    _moveTarget = PickValidTarget();
                }
                break;
            case PetState.Eating:
                _stateTimer += Time.deltaTime;
                if (_stateTimer >= eatingPauseDuration) ChangeState(PetState.Moving);
                break;
            case PetState.Vomiting:
                break;
        }
    }

    private void ChangeState(PetState newState)
    {
        currentState = newState;
        _stateTimer = 0f;

        UpdateAnimationState(newState);

        switch (newState)
        {
            case PetState.Moving:
                _moveTarget = PickValidTarget();
                break;
            case PetState.Eating:
                _rb.linearVelocity = Vector2.zero;
                break;
            case PetState.Vomiting:
                _rb.linearVelocity = Vector2.zero;
                PerformVomit();
                break;
        }
    }

    private void UpdateAnimationState(PetState state)
    {
        if (petAnimator == null) return;

        switch (state)
        {
            case PetState.Eating:
                petAnimator.SetBool(AnimHashIsEating, true);
                break;
            case PetState.Moving:
            case PetState.Vomiting:
                petAnimator.SetBool(AnimHashIsEating, false);
                break;
        }
    }
    #endregion

    #region --- 移動與導航 ---
    private Vector2 PickValidTarget()
    {
        Vector2 origin = transform.position;
        if (WorldBounds2D.Instance != null)
        {
            Rect bounds = WorldBounds2D.Instance.GetWorldRect();
            float minX = bounds.xMin + wallPadding;
            float maxX = bounds.xMax - wallPadding;
            float minY = bounds.yMin + wallPadding;
            float maxY = bounds.yMax - wallPadding;

            if (minX >= maxX || minY >= maxY) return bounds.center;

            for (int i = 0; i < 10; i++)
            {
                float dist = Random.Range(minWalkDistance, maxWalkDistance);
                Vector2 testPos = origin + Random.insideUnitCircle.normalized * dist;

                if (testPos.x >= minX && testPos.x <= maxX && testPos.y >= minY && testPos.y <= maxY)
                    return testPos;
            }
            return new Vector2(Random.Range(minX, maxX), Random.Range(minY, maxY));
        }
        return origin + Random.insideUnitCircle.normalized * Random.Range(minWalkDistance, maxWalkDistance);
    }

    private void EnsureWithinBounds()
    {
        if (WorldBounds2D.Instance != null)
        {
            Vector2 pos = transform.position;
            Vector2 vel = _rb.linearVelocity;

            if (WorldBounds2D.Instance.ConstrainToBounds(ref pos, ref vel, wallPadding * 0.5f))
            {
                transform.position = pos;
                _rb.linearVelocity = vel;
                if (currentState == PetState.Moving) _moveTarget = PickValidTarget();
            }
        }
    }
    #endregion

    #region --- 腸胃、互動與成長 ---
    private void ApplyLevelData()
    {
        if (levelConfigs == null || levelConfigs.Length == 0 || currentLevel >= levelConfigs.Length) return;

        var config = levelConfigs[currentLevel];
        transform.localScale = Vector3.one * config.scaleMultiplier;
        CurrentMaxEatTier = config.maxEatTier;

        if (petAnimator != null && config.animatorController != null)
        {
            petAnimator.runtimeAnimatorController = config.animatorController;
            petAnimator.SetBool(AnimHashIsEating, currentState == PetState.Eating);
        }

        OnPetScaleChanged?.Invoke(config.scaleMultiplier);
    }

    private void CheckLevelUp()
    {
        if (levelConfigs == null || currentLevel >= levelConfigs.Length - 1) return;

        if (totalEaten >= levelConfigs[currentLevel].requiredTotalTrash)
        {
            currentLevel++;
            ApplyLevelData();
            OnPetLevelChanged?.Invoke(currentLevel);
        }
    }

    private void PerformSuckingPhysics()
    {
        int count = Physics2D.OverlapCircle(transform.position, suckRadius, _trashFilter, _suckResults);

        for (int i = 0; i < count; i++)
        {
            if (_suckResults[i] != null && _suckResults[i].TryGetComponent<BaseTrash>(out var trash))
            {
                if (trash.IsAbsorbing || !CanEat(trash)) continue;

                Vector2 dir = ((Vector2)transform.position - (Vector2)trash.transform.position).normalized;
                trash.ApplyExternalForce(dir * suckForce);

                // [重點註釋] 已移除此處直接呼叫 EatTrash 的邏輯，改由 BlackHoleObstacle 的 Trigger 統一接管吸入瞬間
            }
        }
    }

    // [重點註釋] 開放權限：讓外部（BlackHoleObstacle）詢問大腦是否能吃
    public bool CanEat(BaseTrash trash)
    {
        if (currentState == PetState.Vomiting) return false;

        bool isVomitTrigger = _vomitTriggersSet.Contains(trash.trashType);
        // 如果不是催吐物且階級大於嘴巴，則拒絕
        if (!isVomitTrigger && trash.trashTier > CurrentMaxEatTier) return false;

        return true;
    }

    // [重點註釋] 開放介面：當 BlackHoleObstacle 確定咬下垃圾時，透過此函數寫入大腦資料
    public void NotifyTrashEaten(BaseTrash trash)
    {
        bool isVomitTrigger = _vomitTriggersSet.Contains(trash.trashType);

       

        _stomachContents.Add(trash.trashType);
        trash.OnEnterBlackHole();

        if (isVomitTrigger)
        {
            ChangeState(PetState.Vomiting);
            return;
        }

        totalEaten++;
        CheckLevelUp();

        if (currentState == PetState.Eating)
        {
            _stateTimer = 0f;
        }
        else
        {
            ChangeState(PetState.Eating);
        }
    }

    private void PerformVomit()
    {
        OnVomitPenalty?.Invoke(-Mathf.Abs(vomitScorePenalty));

        if (TrashPool.Instance != null && _stomachContents.Count > 0)
        {
            int vomitAmount = Random.Range(3, 6);
            vomitAmount = Mathf.Min(vomitAmount, _stomachContents.Count);
            bool hasSpawnedVomit = false;

            for (int i = 0; i < vomitAmount; i++)
            {
                int randomIndex = Random.Range(0, _stomachContents.Count);
                TrashType typeToVomit = _stomachContents[randomIndex];

                _stomachContents[randomIndex] = _stomachContents[_stomachContents.Count - 1];
                _stomachContents.RemoveAt(_stomachContents.Count - 1);

                Vector2 spawnPos = (Vector2)transform.position + Random.insideUnitCircle * 0.5f;
                BaseTrash vomitObj = TrashPool.Instance.GetTrash(typeToVomit, spawnPos);

                if (vomitObj != null)
                {
                    hasSpawnedVomit = true;
                    vomitObj.ResetState();
                    vomitObj.ApplyExternalImpulse(Random.insideUnitCircle.normalized * vomitForce);
                }
            }

            if (hasSpawnedVomit && _cachedSpawner != null)
            {
                _cachedSpawner.RecalculateTotalTrash();
            }
        }

        _stomachContents.Clear();
        Invoke(nameof(ReturnToMovingAfterVomit), 1f);
    }

    private void ReturnToMovingAfterVomit() => ChangeState(PetState.Moving);
    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.yellow;
        style.fontStyle = FontStyle.Bold;
        style.fontSize = 14;

        Vector3 labelPos = transform.position + Vector3.up * 1.5f;
        UnityEditor.Handles.Label(labelPos, $"Level: {currentLevel} | Eaten: {totalEaten}", style);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1, 0, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, suckRadius);

        if (currentState == PetState.Moving)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, _moveTarget);
            Gizmos.DrawWireSphere(_moveTarget, 0.2f);
        }
    }
#endif
}
