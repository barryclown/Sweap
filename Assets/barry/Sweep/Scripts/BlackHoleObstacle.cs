using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioEmitter))]
public class BlackHoleObstacle : BaseObstacle
{
    public static event System.Action<int, bool, Vector2> OnTrashAbsorbedScore;

    [Header("黑洞能力設定")]
    [SerializeField] private float absorbRadius = 1f;
    [SerializeField] private Vector2 centerOffset;
    [Tooltip("此黑洞能吞噬的最高垃圾階級 (若身上有 PetAI，將會被 PetAI 的等級覆蓋)")]
    [SerializeField] private int maxAbsorbTier = 99;

    [Header("垃圾吸入設定")]
    [SerializeField] private float trashAbsorbTime = 1f;
    [SerializeField] private float trashRotateSpeed = 360f;
    [SerializeField] private AnimationCurve trashScaleCurve;
    [SerializeField] private AnimationCurve trashMoveCurve;

    [Header("展演計分歸因")]
    [SerializeField, Range(0f, 1f), Tooltip("不是由玩家推入時，只保留多少比例的分數")]
    private float passiveScoreMultiplier = 0.1f;
    [SerializeField, Min(0f), Tooltip("垃圾最後一次受玩家影響後，幾秒內被吸入仍算玩家得分")]
    private float playerInfluenceWindow = 8f;

    [Header("玩家吸入設定")]
    [SerializeField] private float playerAbsorbTime = 0.5f;
    [SerializeField] private float playerVanishTime = 0.5f;
    // [已廢棄] playerEjectDuration 已移除，交由 PlayerController 處理
    [SerializeField] private float playerEjectSpeed = 15f;
    [SerializeField] private float playerRotateSpeed = 1f;

    [Header("內建音效設定")]
    [SerializeField] private AudioEmitter audioEmitter;
    [SerializeField] private string absorbSound = "Absorb";
    [SerializeField] private string ejectSound = "Eject";
    [SerializeField] private float absorbCooldown = 0.1f;

    [Header("調試設置")]
    [SerializeField] private bool showTriggerGizmos = true;
    [SerializeField] private Color triggerColor = new Color(1f, 0.5f, 0f, 0.3f);

    private readonly List<AbsorbData> _absorbing = new List<AbsorbData>(64);
    private PlayerAbsorbData _playerAbsorb;
    private bool _hasPlayerAbsorb;
    private CircleCollider2D _triggerCollider;
    private float _lastAbsorbTime = -1f;

    private PetAI _petBrain;

    public Vector3 CenterPos => transform.position + (Vector3)centerOffset;

    public static int CalculateAbsorptionScore(int baseScore, bool playerAssisted, float passiveMultiplier)
    {
        int safeBaseScore = Mathf.Max(0, baseScore);
        if (playerAssisted) return safeBaseScore;
        return Mathf.RoundToInt(safeBaseScore * Mathf.Clamp01(passiveMultiplier));
    }

    private struct AbsorbData
    {
        public BaseTrash trash;
        public Vector3 startPos;
        public Vector3 startScale;
        public float elapsed;
        public bool playerAssisted;
    }

    // [重點註釋] 拔除冗餘的 Ejecting 狀態，解決狀態脫節
    private enum PlayerState { Absorbing, Waiting }

    private struct PlayerAbsorbData
    {
        public PlayerController player;
        public Vector3 startPos;
        public Vector3 startScale;
        public Vector2 ejectDir;
        public float timer;
        public PlayerState state;
    }

    private void Awake()
    {
        if (!TryGetComponent(out _triggerCollider))
        {
            _triggerCollider = gameObject.AddComponent<CircleCollider2D>();
        }

        _triggerCollider.isTrigger = true;
        _triggerCollider.offset = centerOffset;
        _triggerCollider.radius = absorbRadius;

        if (audioEmitter == null) audioEmitter = GetComponent<AudioEmitter>();

        _petBrain = GetComponent<PetAI>();
        if (_petBrain == null)
        {
            _petBrain = GetComponentInParent<PetAI>();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent<IAbsorbable>(out var target) && target.CanBeAbsorbed)
        {
            if (target is BaseTrash trash)
            {
                if (_petBrain != null)
                {
                    if (!_petBrain.CanEat(trash)) return;
                }
                else
                {
                    if (trash.trashTier > maxAbsorbTier) return;
                }
            }

            target.OnAbsorbStart(this);
        }
    }

    private void PlayAbsorbSound()
    {
        if (audioEmitter != null && Time.time - _lastAbsorbTime > absorbCooldown)
        {
            audioEmitter.PlayOneShot(absorbSound, 0.98f, 0.88f);
            AudioManager.Instance?.PushMixMoment(DemoMixMoment.Absorb);
            _lastAbsorbTime = Time.time;
        }
    }

    public void RegisterTrash(BaseTrash trash)
    {
        PlayAbsorbSound();
        _petBrain?.NotifyTrashEaten(trash);

        _absorbing.Add(new AbsorbData
        {
            trash = trash,
            startPos = trash.transform.position,
            startScale = trash.transform.localScale,
            elapsed = 0f,
            // 在開始吸入時凍結歸因，避免吸入動畫期間跨過時間門檻。
            playerAssisted = trash.WasPlayerInfluencedRecently(playerInfluenceWindow)
        });
    }

    public void RegisterPlayer(PlayerController player)
    {
        if (_hasPlayerAbsorb) return;

        PlayAbsorbSound();

        Vector2 dir = Vector2.right;
        if (WorldBounds2D.Instance != null)
        {
            Vector2 safeCenter = WorldBounds2D.Instance.GetCenter();
            Vector2 dirToCenter = (safeCenter - (Vector2)CenterPos).normalized;
            if (dirToCenter == Vector2.zero) dirToCenter = Random.insideUnitCircle.normalized;

            Vector2 randomOffset = Random.insideUnitCircle * 0.15f;
            dir = (dirToCenter + randomOffset).normalized;
        }
        else
        {
            dir = Random.insideUnitCircle.normalized;
        }

        if (dir == Vector2.zero) dir = Vector2.right;

        _playerAbsorb = new PlayerAbsorbData
        {
            player = player,
            startPos = player.transform.position,
            startScale = player.transform.localScale,
            ejectDir = dir,
            timer = 0f,
            state = PlayerState.Absorbing
        };
        _hasPlayerAbsorb = true;
    }

    private void Update()
    {
        UpdateTrashAbsorbAnimation();
        UpdatePlayerAbsorbAnimation();
    }

    private void UpdateTrashAbsorbAnimation()
    {
        int count = _absorbing.Count;
        if (count == 0) return;

        Vector3 targetPos = CenterPos;
        float dt = Time.deltaTime;

        for (int i = count - 1; i >= 0; i--)
        {
            AbsorbData data = _absorbing[i];

            if (data.trash == null || !data.trash.gameObject.activeSelf)
            {
                RemoveAbsorbDataFast(i);
                continue;
            }

            data.elapsed += dt;
            float t = Mathf.Clamp01(data.elapsed / trashAbsorbTime);
            float scaleT = trashScaleCurve != null ? trashScaleCurve.Evaluate(t) : t;
            float moveT = trashMoveCurve != null ? trashMoveCurve.Evaluate(t) : t;

            Transform tr = data.trash.transform;
            tr.Rotate(0f, 0f, trashRotateSpeed * dt);
            tr.localScale = Vector3.LerpUnclamped(data.startScale, Vector3.zero, scaleT);
            tr.position = Vector2.LerpUnclamped(data.startPos, targetPos, moveT);

            if (data.elapsed >= trashAbsorbTime)
            {
                int score = CalculateAbsorptionScore(
                    data.trash.ScoreValue, data.playerAssisted, passiveScoreMultiplier);
                OnTrashAbsorbedScore?.Invoke(score, data.playerAssisted, CenterPos);
                data.trash.ResetState();

                if (TrashPool.Instance != null) TrashPool.Instance.ReturnTrash(data.trash);
                else Destroy(data.trash.gameObject);

                RemoveAbsorbDataFast(i);
            }
            else
            {
                _absorbing[i] = data;
            }
        }
    }

    private void RemoveAbsorbDataFast(int index)
    {
        int lastIndex = _absorbing.Count - 1;
        if (index != lastIndex)
        {
            _absorbing[index] = _absorbing[lastIndex];
        }
        _absorbing.RemoveAt(lastIndex);
    }

    private void UpdatePlayerAbsorbAnimation()
    {
        if (!_hasPlayerAbsorb) return;

        PlayerAbsorbData data = _playerAbsorb;
        if (data.player == null)
        {
            _hasPlayerAbsorb = false;
            return;
        }

        Transform tr = data.player.transform;
        float dt = Time.deltaTime;

        switch (data.state)
        {
            case PlayerState.Absorbing:
                data.timer += dt;
                float tAbsorb = Mathf.Clamp01(data.timer / playerAbsorbTime);
                tr.position = Vector2.LerpUnclamped(data.startPos, CenterPos, tAbsorb);
                tr.localScale = Vector3.LerpUnclamped(data.startScale, Vector3.zero, tAbsorb);
                tr.Rotate(0f, 0f, playerRotateSpeed * dt);

                if (data.timer >= playerAbsorbTime)
                {
                    data.timer = 0f;
                    data.state = PlayerState.Waiting;
                }
                break;

            case PlayerState.Waiting:
                data.timer += dt;
                if (data.timer >= playerVanishTime)
                {
                    if (audioEmitter != null) audioEmitter.PlayOneShot(ejectSound);

                    // [重點註釋] 吐出去的瞬間，直接結案並把剩下的演出全權交給 PlayerController，防止瞬移！
                    data.player.ExitBlackHole(data.ejectDir, playerEjectSpeed);
                    _hasPlayerAbsorb = false;
                    return;
                }
                break;
        }

        _playerAbsorb = data;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3 center = CenterPos;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, absorbRadius);

        if (showTriggerGizmos)
        {
            Gizmos.color = triggerColor;
            Gizmos.DrawSphere(center, absorbRadius);
        }
    }
#endif
}
