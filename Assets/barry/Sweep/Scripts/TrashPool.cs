using System.Collections.Generic;
using UnityEngine;

public enum TrashType
{
    None = 0,
    Banana = 1,
    Paper = 2,
    Can = 3,
    Sock = 4,
    Spoon = 5,
    Glass = 6,
    Pot = 7
}

[System.Serializable]
public class TrashPoolEntry : BasePoolEntry<TrashType, BaseTrash> { }

public class TrashPool : BasePool<TrashType, BaseTrash>
{
    public static TrashPool Instance { get; private set; }
    public List<BaseTrash> ActiveTrashList = new List<BaseTrash>();
    [SerializeField]
    private List<TrashPoolEntry> trashEntries;
    public Dictionary<TrashType, int> TrashTierMap { get; private set; } = new Dictionary<TrashType, int>();
    public Dictionary<TrashType, HashSet<int>> AvailableTiersMap { get; private set; } = new Dictionary<TrashType, HashSet<int>>();

    // [重點註釋] 快取結構升級為 Stack，符合 LIFO (後進先出) 對象池標準特性
    private Dictionary<TrashType, Dictionary<int, Stack<BaseTrash>>> _tieredInactiveCache = new Dictionary<TrashType, Dictionary<int, Stack<BaseTrash>>>();

    // [重點註釋] 預計算快取，讓 GetRandomTrashTypeUpToTier 達成 O(1) 零 GC 分配
    private Dictionary<int, TrashType[]> _validTypesPerTierCache = new Dictionary<int, TrashType[]>();

    // [重點優化] 新增精準 Tier 快取，支援 GetRandomTrashTypeByTier 的 O(1) 查詢
    private Dictionary<int, TrashType[]> _exactTypesPerTierCache = new Dictionary<int, TrashType[]>();

    private int _maxDiscoveredTier = 1;

    protected virtual void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        InitializePool(trashEntries);
        BuildTierMap();
    }

    private void BuildTierMap()
    {
        TrashTierMap.Clear();
        AvailableTiersMap.Clear();
        _tieredInactiveCache.Clear();
        _validTypesPerTierCache.Clear();
        _exactTypesPerTierCache.Clear();

        foreach (TrashType type in System.Enum.GetValues(typeof(TrashType)))
        {
            if (type == TrashType.None) continue;
            AvailableTiersMap[type] = new HashSet<int>();
            List<BaseTrash> tempPopped = new List<BaseTrash>();
            for (int i = 0; i < 15; i++)
            {
                BaseTrash t = base.Get(type);
                if (t == null) break;
                int currentTier = t.trashTier;
                AvailableTiersMap[type].Add(currentTier);
                TrashTierMap[type] = currentTier;
                _maxDiscoveredTier = Mathf.Max(_maxDiscoveredTier, currentTier);
                tempPopped.Add(t);
            }
            foreach (var t in tempPopped) ReturnTrash(t);
        }

        // 預先計算並封裝每階級合法的 TrashType 陣列
        for (int i = 1; i <= _maxDiscoveredTier; i++)
        {
            List<TrashType> valids = new List<TrashType>();
            foreach (var kvp in AvailableTiersMap)
            {
                foreach (int tier in kvp.Value)
                {
                    if (tier <= i) { valids.Add(kvp.Key); break; }
                }
            }
            _validTypesPerTierCache[i] = valids.ToArray();
        }

        // [重點優化] 同步預計算精準 Tier 的 TrashType 陣列，讓 GetRandomTrashTypeByTier 達成 O(1) 零 GC
        for (int i = 1; i <= _maxDiscoveredTier; i++)
        {
            List<TrashType> valids = new List<TrashType>();
            foreach (var kvp in AvailableTiersMap)
            {
                if (kvp.Value.Contains(i))
                {
                    valids.Add(kvp.Key);
                }
            }
            _exactTypesPerTierCache[i] = valids.ToArray();
        }

        Debug.Log($"[TrashPool] 已成功建立多階級垃圾快取緩存。最高階級: {_maxDiscoveredTier}");
    }

    public TrashType GetRandomTrashTypeUpToTier(int maxTier)
    {
        int clampedTier = Mathf.Clamp(maxTier, 1, _maxDiscoveredTier);
        if (_validTypesPerTierCache.TryGetValue(clampedTier, out var validTypes) && validTypes.Length > 0)
        {
            return validTypes[Random.Range(0, validTypes.Length)];
        }
        return TrashType.None;
    }

    // [重點實作] 依照指定 Tier 選取「精準」有該階級的 TrashType，避免後續 GetTrash fallback
    public TrashType GetRandomTrashTypeByTier(int tier)
    {
        int clampedTier = Mathf.Clamp(tier, 1, _maxDiscoveredTier);
        if (_exactTypesPerTierCache.TryGetValue(clampedTier, out var validTypes) && validTypes.Length > 0)
        {
            return validTypes[Random.Range(0, validTypes.Length)];
        }
        return TrashType.None;
    }

    private void FixedUpdate()
    {
        if (SpatialGridManager.Instance != null)
        {
            SpatialGridManager.Instance.UpdateGrid(ActiveTrashList);
        }
    }

    public BaseTrash GetTrash(TrashType type, Vector3 position)
    {
        return GetTrash(type, 0, position);
    }

    public BaseTrash GetTrash(TrashType type, int targetTier, Vector3 position)
    {
        // 1. O(1) 從精準緩存中提取
        if (targetTier > 0 && _tieredInactiveCache.TryGetValue(type, out var tierMap) &&
            tierMap.TryGetValue(targetTier, out var stack) && stack.Count > 0)
        {
            return ActivateTrash(stack.Pop(), position);
        }
        if (targetTier <= 0 && _tieredInactiveCache.TryGetValue(type, out tierMap))
        {
            foreach (var s in tierMap.Values)
            {
                if (s.Count > 0) return ActivateTrash(s.Pop(), position);
            }
        }
        // 2. 緩存無貨，向 BasePool 請求實例化
        // [重點註釋] 利用暫存清單隔離不匹配的實例，避免反覆進出快取池引發效能損耗
        List<BaseTrash> mismatched = new List<BaseTrash>();
        BaseTrash matchedTrash = null;
        for (int i = 0; i < 20; i++)
        {
            BaseTrash trash = base.Get(type);
            if (trash == null) break;
            if (targetTier <= 0 || trash.trashTier == targetTier)
            {
                matchedTrash = trash;
                break;
            }
            mismatched.Add(trash);
        }
        // 3. 處理未命中時的保底機制
        BaseTrash result = matchedTrash;
        if (result == null && mismatched.Count > 0)
        {
            result = mismatched[0];
            mismatched.RemoveAt(0); // 從暫存中抽離，確保它不會被倒回快取池
            Debug.LogWarning($"[TrashPool] 無法獲取 {type} 的 Tier {targetTier}，提供保底 Tier {result.trashTier}。");
        }
        // 將剩餘的 mismatched 乾淨地存入快取
        foreach (var t in mismatched)
        {
            StoreInTieredCache(type, t.trashTier, t);
        }
        return result != null ? ActivateTrash(result, position) : null;
    }

    public void ReturnTrash(BaseTrash trash)
    {
        if (trash == null) return;
        ActiveTrashList.Remove(trash);
        StoreInTieredCache(trash.trashType, trash.trashTier, trash);
    }

    private BaseTrash ActivateTrash(BaseTrash trash, Vector3 pos)
    {
        trash.transform.position = pos;
        trash.gameObject.SetActive(true);
        ActiveTrashList.Add(trash);
        return trash;
    }

    private void StoreInTieredCache(TrashType type, int tier, BaseTrash trash)
    {
        if (!_tieredInactiveCache.TryGetValue(type, out var tierMap))
        {
            tierMap = new Dictionary<int, Stack<BaseTrash>>();
            _tieredInactiveCache[type] = tierMap;
        }
        if (!tierMap.TryGetValue(tier, out var stack))
        {
            stack = new Stack<BaseTrash>();
            tierMap[tier] = stack;
        }
        if (!stack.Contains(trash))
        {
            trash.gameObject.SetActive(false);
            stack.Push(trash);
        }
    }
}