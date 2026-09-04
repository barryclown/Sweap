using System.Collections;
using UnityEngine;

public class DynamicSpawnManager : MonoBehaviour
{
    public static DynamicSpawnManager Instance { get; private set; }

    [Header("動態補給配置")]
    [SerializeField] private bool enableDynamicSpawn = true;
    [SerializeField] private int maxTrashOnField = 30;
    [SerializeField] private SpawnWeightConfig weightConfig;

    [Header("依賴引用")]
    [SerializeField] private LevelSpawner spawner;
    [SerializeField] private PetAI pet;

    [Header("除錯設定")]
    [SerializeField] private bool showDebugLog = true;

    private bool _isInitialized = false;
    private TrashPool _trashPool;
    private int _currentDynamicTrashCount = 0;
    private Coroutine _refillRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (spawner == null && !TryGetComponent(out spawner))
        {
            Debug.LogError("[DynamicSpawnManager] 缺失 LevelSpawner 依賴！", this);
            enabled = false;
        }
    }

    private IEnumerator Start()
    {
        if (spawner == null) yield break;

        if (showDebugLog) Debug.Log("[DynamicSpawnManager] 等待 LevelSpawner 佈局完成...");

        while (!spawner.IsReady) yield return null;

        if (pet == null)
        {
#if UNITY_2021_3_18_OR_NEWER || UNITY_2022_2_OR_NEWER
            pet = UnityEngine.Object.FindAnyObjectByType<PetAI>();
#else
            pet = UnityEngine.Object.FindObjectOfType<PetAI>();
#endif
        }

        if (pet == null)
        {
            Debug.LogError("[DynamicSpawnManager] LevelSpawner 已就緒，但場上依然找不到 PetAI！請確認 Obstacle 是否正確掛載 PetAI 腳本。", this);
            enabled = false;
            yield break;
        }

        _trashPool = TrashPool.Instance;
        _isInitialized = true;

        if (showDebugLog) Debug.Log($"[DynamicSpawnManager] 依賴全數就緒。準備首次觸發生成，當前數量: {_currentDynamicTrashCount}");
        TriggerRefill();
    }

    public void OnDynamicTrashConsumed()
    {
        if (!enableDynamicSpawn || !_isInitialized) return;

        _currentDynamicTrashCount = Mathf.Max(0, _currentDynamicTrashCount - 1);
        if (showDebugLog) Debug.Log($"[DynamicSpawnManager] 偵測到垃圾被消耗。當前數量: {_currentDynamicTrashCount}/{maxTrashOnField}，準備觸發補充。");

        TriggerRefill();
    }

    private void TriggerRefill()
    {
        if (!enableDynamicSpawn || !_isInitialized)
        {
            if (showDebugLog) Debug.LogWarning("[DynamicSpawnManager] 觸發補充失敗：未啟用動態生成或未初始化完成。");
            return;
        }

        if (_refillRoutine == null)
        {
            if (showDebugLog) Debug.Log("[DynamicSpawnManager] 啟動補充協程。");
            _refillRoutine = StartCoroutine(RefillCoroutine());
        }
        else
        {
            if (showDebugLog) Debug.Log("[DynamicSpawnManager] 補充協程已在運行中，忽略本次觸發。");
        }
    }

    private IEnumerator RefillCoroutine()
    {
        if (showDebugLog) Debug.Log($"[DynamicSpawnManager] 開始執行補充迴圈，目標數量: {maxTrashOnField}");

        int consecutiveFailures = 0;

        while (_currentDynamicTrashCount < maxTrashOnField)
        {
            if (weightConfig == null)
            {
                Debug.LogError("[DynamicSpawnManager] 缺失 SpawnWeightConfig 綁定！", this);
                break;
            }

            int targetTier = weightConfig.GetRandomTier(pet.CurrentMaxEatTier);
            TrashType type = _trashPool.GetRandomTrashTypeByTier(targetTier);

            if (type == TrashType.None)
            {
                if (showDebugLog) Debug.LogWarning($"[DynamicSpawnManager] 獲取不到對應階級的 TrashType (Tier: {targetTier})，中斷補充迴圈。");
                break;
            }

            BaseTrash spawnedTrash = spawner.TrySpawnRandomTrash(type, targetTier, false);
            if (spawnedTrash != null)
            {
                spawnedTrash.isDynamicSpawned = true;
                _currentDynamicTrashCount++;
                spawner.RecalculateTotalTrash();
                consecutiveFailures = 0;

                if (showDebugLog) Debug.Log($"[DynamicSpawnManager] 成功生成動態垃圾。類型: {type}, 階級: {targetTier}。當前總數: {_currentDynamicTrashCount}/{maxTrashOnField}");
            }
            else
            {
                consecutiveFailures++;
                if (showDebugLog) Debug.LogWarning($"[DynamicSpawnManager] Spawner 生成失敗 (已嘗試 {consecutiveFailures} 次)。可能是場地過於擁擠或被空氣牆佔滿。");

                // [重點註釋] 效能熔斷防護：當場地被空氣牆與垃圾佔滿，連續生成失敗時強制休眠，阻止死循環拖垮幀數
                if (consecutiveFailures >= 3)
                {
                    if (showDebugLog) Debug.LogWarning("[DynamicSpawnManager] 連續失敗次數過多，觸發效能熔斷機制，暫停生成 2 秒。");
                    yield return new WaitForSeconds(2f);
                    consecutiveFailures = 0;
                    continue;
                }
            }

            yield return null;
        }

        if (showDebugLog) Debug.Log($"[DynamicSpawnManager] 補充協程結束。最終數量: {_currentDynamicTrashCount}/{maxTrashOnField}");
        _refillRoutine = null;
    }

    [ContextMenu("Debug: 強制觸發補充 (Trigger Refill)")]
    private void DebugForceRefill()
    {
        if (showDebugLog) Debug.Log("[DynamicSpawnManager - Debug] 從 Inspector 強制觸發補充。");
        TriggerRefill();
    }

    [ContextMenu("Debug: 模擬消耗一個垃圾 (Simulate Consume)")]
    private void DebugSimulateConsume()
    {
        if (showDebugLog) Debug.Log("[DynamicSpawnManager - Debug] 從 Inspector 模擬消耗垃圾。");
        OnDynamicTrashConsumed();
    }
}