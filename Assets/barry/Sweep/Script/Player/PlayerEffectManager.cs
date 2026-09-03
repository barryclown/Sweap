using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerEffectManager : MonoBehaviour
{
    [Header("=== 特效組件引用 ===")]
    [SerializeField] private DragLine dragLine;
    [SerializeField] private DynamicSweepMesh chargedSweepMesh;
    [SerializeField] private float chargedSweepRotationOffset = -90f;

    [Header("=== 粒子系統列表 ===")]
    [SerializeField] private ParticleSystem[] particleSystems;

    [Header("=== Trail (拖尾) 設定 ===")]
    [SerializeField] private float trailRateMax = 60f;
    [SerializeField] private float trailMinPowerToPlay = 0.01f;

    [Header("=== Object Pool (物件池) 設定 ===")]
    [SerializeField] private int poolSizeTrashHit = 12;
    [SerializeField] private int poolSizeWallHit = 12;

    private const int FX_TRASH_HIT = 0;
    private const int FX_TRAIL = 1;
    private const int FX_WALL_HIT = 2;

    private ParticleSystem _trailPS;
    private ParticleSystem.EmissionModule _trailEmission;
    private bool _trailReady;

    private ParticleSystem _dragParticleInstance;

    private ParticlePool _trashHitPool;
    private ParticlePool _wallHitPool;
    private Transform _fxRoot;

    private void Awake()
    {
        dragLine?.HideLine();
        if (chargedSweepMesh != null) chargedSweepMesh.gameObject.SetActive(false);

        SetupFX();
    }

    private void SetupFX()
    {
        _fxRoot = new GameObject("[FX Root]").transform;
        _fxRoot.SetParent(transform.parent);

        _trailPS = GetPS(FX_TRAIL);
        if (_trailPS != null)
        {
            _trailEmission = _trailPS.emission;
            _trailEmission.rateOverTime = 0f;
            _trailPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _trailReady = true;
        }

        var trashPrefab = GetPS(FX_TRASH_HIT);
        if (trashPrefab != null) _trashHitPool = new ParticlePool(trashPrefab, poolSizeTrashHit, _fxRoot, "TrashHitPool");

        var wallPrefab = GetPS(FX_WALL_HIT);
        if (wallPrefab != null) _wallHitPool = new ParticlePool(wallPrefab, poolSizeWallHit, _fxRoot, "WallHitPool");
    }

    // [重點註釋] 給 PlayerController 呼叫，處理剝離父物件導致的粒子未放大問題
    public void ScaleEffects(float multiplier)
    {
        if (_fxRoot != null)
        {
            // 將整個特效池根節點放大，保證拖尾與撞擊特效視覺大小同步
            _fxRoot.localScale = Vector3.one * multiplier;
        }

        if (chargedSweepMesh != null)
        {
            // 同步放大扇形網格的基礎視覺
            chargedSweepMesh.transform.localScale = Vector3.one * multiplier;
        }
    }

    private ParticleSystem GetPS(int index)
    {
        if (particleSystems == null) return null;
        if (index < 0 || index >= particleSystems.Length) return null;
        return particleSystems[index];
    }

    public void UpdateDragLine(Vector2 start, Vector2 end) => dragLine?.ShowLine(start, end);
    public void HideDragLine() => dragLine?.HideLine();

    public void ShowChargeSweep(Vector2 position, Vector2 dir, float t)
    {
        if (chargedSweepMesh == null) return;

        if (!chargedSweepMesh.gameObject.activeSelf)
            chargedSweepMesh.gameObject.SetActive(true);

        chargedSweepMesh.transform.position = position;
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + chargedSweepRotationOffset;
        chargedSweepMesh.transform.rotation = Quaternion.Euler(0f, 0f, ang);

        chargedSweepMesh.UpdateShape(t);
    }

    public void HideChargeSweep()
    {
        if (chargedSweepMesh != null && chargedSweepMesh.gameObject.activeSelf)
            chargedSweepMesh.gameObject.SetActive(false);
    }

    public void UpdateTrail(Vector2 center, Vector2 dir, float power01, bool armed)
    {
        if (!_trailReady || !armed)
        {
            StopTrail();
            return;
        }

        _trailPS.transform.position = center;
        if (dir.sqrMagnitude > 0.0001f)
        {
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            _trailPS.transform.rotation = Quaternion.Euler(0f, 0f, ang);
        }

        _trailEmission.rateOverTime = Mathf.Lerp(0f, trailRateMax, Mathf.Clamp01(power01));

        if (power01 > trailMinPowerToPlay)
        {
            if (!_trailPS.isPlaying) _trailPS.Play();
        }
        else
        {
            StopTrail();
        }
    }

    public void StopTrail()
    {
        if (!_trailReady) return;
        _trailEmission.rateOverTime = 0f;
        if (_trailPS.isPlaying)
            _trailPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    public void StartFollowParticle(Transform target)
    {
        if (target == null) return;

        if (_dragParticleInstance == null)
        {
            var prefab = GetPS(FX_TRAIL);
            if (prefab != null)
            {
                _dragParticleInstance = Instantiate(prefab, target);
                _dragParticleInstance.transform.localPosition = Vector3.zero;
                _dragParticleInstance.transform.localRotation = Quaternion.identity;
            }
        }

        if (_dragParticleInstance != null)
        {
            // 粒子已經是玩家子物件，不需要為每次放開滑鼠建立永久追蹤協程。
            _dragParticleInstance.transform.SetParent(target, false);
            _dragParticleInstance.transform.localPosition = Vector3.zero;
            _dragParticleInstance.transform.localRotation = Quaternion.identity;
            _dragParticleInstance.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _dragParticleInstance.Play(true);
        }
    }

    public void StopAllEffects()
    {
        StopTrail();
        HideDragLine();
        HideChargeSweep();
        if (_dragParticleInstance != null) _dragParticleInstance.Stop();
    }

    public void PlayTrashHit(Vector2 pos, Vector2 normal)
    {
        if (_trashHitPool != null) SpawnOneShot(_trashHitPool, pos, normal);
    }

    public void PlayWallHit(Vector2 pos, Vector2 normal)
    {
        if (_wallHitPool != null) SpawnOneShot(_wallHitPool, pos, normal);
    }

    private static void SpawnOneShot(ParticlePool pool, Vector2 pos, Vector2 normal)
    {
        ParticleSystem ps = pool.Get();
        if (ps == null) return;

        float ang = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
        ps.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 0f, ang));

        ps.gameObject.SetActive(true);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play();
    }

    private sealed class ParticlePool
    {
        private readonly Queue<ParticleSystem> _idle = new Queue<ParticleSystem>();
        private readonly ParticleSystem _prefab;
        private readonly Transform _root;

        public ParticlePool(ParticleSystem prefab, int prewarm, Transform parentRoot, string name)
        {
            _prefab = prefab;
            _root = new GameObject(name).transform;
            _root.SetParent(parentRoot, false);
            for (int i = 0; i < Mathf.Max(0, prewarm); i++) _idle.Enqueue(CreateInstance());
        }

        public ParticleSystem Get() => _idle.Count > 0 ? _idle.Dequeue() : CreateInstance();

        private ParticleSystem CreateInstance()
        {
            ParticleSystem ps = Instantiate(_prefab, _root);
            ps.gameObject.SetActive(false);
            var main = ps.main;
            main.stopAction = ParticleSystemStopAction.Callback;
            var returner = ps.gameObject.GetComponent<ReturnToPool>() ?? ps.gameObject.AddComponent<ReturnToPool>();
            returner.Bind(this, ps);
            return ps;
        }

        public void Return(ParticleSystem ps)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            _idle.Enqueue(ps);
        }

        private sealed class ReturnToPool : MonoBehaviour
        {
            private ParticlePool _pool;
            private ParticleSystem _ps;
            public void Bind(ParticlePool pool, ParticleSystem ps) { _pool = pool; _ps = ps; }
            private void OnParticleSystemStopped() { if (_pool != null && _ps != null) _pool.Return(_ps); }
        }
    }
}
