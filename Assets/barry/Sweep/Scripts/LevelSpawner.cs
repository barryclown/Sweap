using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct TrashSpawnData
{
    public TrashType type;
    [Tooltip("指定要生成的 Tier。若為 0 則不限制，將隨機抽取該類型。")]
    public int targetTier;
    public int amount;
}

[System.Serializable]
public struct ObstacleSpawnData
{
    public ObstacleType type;
    public Vector3 position;
}

public class LevelSpawner : MonoBehaviour
{
    [Header("關卡設定")]
    [SerializeField] private PreloadConfigSO levelConfig;
    [SerializeField] private List<ObstacleSpawnData> obstacleLayout = new List<ObstacleSpawnData>();

    [Header("開局生成佈局 (畫面內)")]
    [Tooltip("開場直接出現在玩家視野內的垃圾")]
    [SerializeField] private List<TrashSpawnData> startInViewTrash = new List<TrashSpawnData>();

    [Header("開局生成佈局 (畫面外)")]
    [SerializeField] private List<TrashSpawnData> startOutViewTrash = new List<TrashSpawnData>();

    [Header("生成安全參數")]
    [SerializeField, Tooltip("物體與物體之間的最短安全距離")]
    public float minSafeDistance = 0.5f;
    [SerializeField, Tooltip("物體與空氣牆(世界邊界)的最短安全距離")]
    public float wallSafeDistance = 1.5f;
    [SerializeField] private int maxSpawnAttempts = 15;
    [SerializeField] private Transform trashParent;
    [SerializeField] private Transform obstacleParent;

    private List<Vector3> _permanentOccupiedPos = new List<Vector3>();

    public bool IsReady { get; private set; } = false;

    private void Start()
    {
        TrashCounter.Reset();
        if (TrashPool.Instance == null)
        {
            Debug.LogError("[LevelSpawner] 嚴重錯誤：找不到 TrashPool.Instance，生成終止！");
            return;
        }
        if (ObstaclePool.Instance == null)
        {
            Debug.LogWarning("[LevelSpawner] 找不到 ObstaclePool.Instance，本次將跳過障礙物生成，但不影響垃圾生成。");
        }
        if (trashParent == null) trashParent = new GameObject("TrashContainer").transform;
        if (obstacleParent == null) obstacleParent = new GameObject("ObstacleContainer").transform;

        PreloadTrashPool();
        PreloadObstaclePool();
        SpawnInitialLayout();
    }

    private void SpawnInitialLayout()
    {
        _permanentOccupiedPos.Clear();

        if (ObstaclePool.Instance != null)
        {
            foreach (var ob in obstacleLayout)
            {
                if (IsPositionValid(ob.position))
                {
                    SpawnObstacleAtPosition(ob.type, ob.position);
                    _permanentOccupiedPos.Add(ob.position);
                }
            }
        }

        foreach (var data in startInViewTrash)
        {
            for (int i = 0; i < data.amount; i++)
            {
                TrySpawnRandomTrash(data.type, data.targetTier, true);
            }
        }

        foreach (var data in startOutViewTrash)
        {
            for (int i = 0; i < data.amount; i++)
            {
                TrySpawnRandomTrash(data.type, data.targetTier, false);
            }
        }

        RecalculateTotalTrash();
        Debug.Log($"[LevelSpawner] 開局生成完畢，場上總垃圾數: {TrashCounter.Total}");
        IsReady = true;
    }

    public BaseTrash TrySpawnRandomTrash(TrashType type, int targetTier, bool forceInView)
    {
        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            Vector3 pos = forceInView ? GetRandomPosInView() : GetRandomPosOutView();
            if (IsPositionValid(pos))
            {
                BaseTrash spawnedTrash = SpawnTrashAtPosition(type, targetTier, pos);
                return spawnedTrash;
            }
        }
        return null;
    }

    private Vector3 GetRandomPosInView()
    {
        Rect viewRect = GetCameraWorldRect();
        Vector3 p = new Vector3(Random.Range(viewRect.xMin, viewRect.xMax), Random.Range(viewRect.yMin, viewRect.yMax), 0f);
        return ClampToWorld(p);
    }

    private Vector3 GetRandomPosOutView()
    {
        if (WorldBounds2D.Instance == null) return GetRandomPosInView();

        Rect world = WorldBounds2D.Instance.GetWorldRect();
        Rect view = GetCameraWorldRect();
        Rect exclude = new Rect(view.x - 1f, view.y - 1f, view.width + 2f, view.height + 2f);

        float safeXMin = world.xMin + wallSafeDistance;
        float safeXMax = world.xMax - wallSafeDistance;
        float safeYMin = world.yMin + wallSafeDistance;
        float safeYMax = world.yMax - wallSafeDistance;

        for (int i = 0; i < 10; i++)
        {
            Vector3 p = new Vector3(Random.Range(safeXMin, safeXMax), Random.Range(safeYMin, safeYMax), 0f);
            if (!exclude.Contains(p)) return p;
        }

        return new Vector3(Random.Range(safeXMin, safeXMax), Random.Range(safeYMin, safeYMax), 0f);
    }

    private Rect GetCameraWorldRect()
    {
        Camera cam = Camera.main;
        if (cam == null) return new Rect(-5, -5, 10, 10);
        Plane p = new Plane(Vector3.back, Vector3.zero);
        Ray r1 = cam.ViewportPointToRay(new Vector3(0, 0, 0));
        Ray r2 = cam.ViewportPointToRay(new Vector3(1, 1, 0));
        p.Raycast(r1, out float d1);
        p.Raycast(r2, out float d2);
        return Rect.MinMaxRect(r1.GetPoint(d1).x, r1.GetPoint(d1).y, r2.GetPoint(d2).x, r2.GetPoint(d2).y);
    }

    private Vector3 ClampToWorld(Vector3 p)
    {
        if (WorldBounds2D.Instance == null) return p;
        Rect r = WorldBounds2D.Instance.GetWorldRect();

        p.x = Mathf.Clamp(p.x, r.xMin + wallSafeDistance, r.xMax - wallSafeDistance);
        p.y = Mathf.Clamp(p.y, r.yMin + wallSafeDistance, r.yMax - wallSafeDistance);
        return p;
    }

    private bool IsPositionValid(Vector3 p)
    {
        // [重點註釋] 核心阻斷：遍歷所有激活的空氣牆與邊界，只要目標座標在任何一個阻擋區域內，直接判定無效
        for (int i = 0; i < WorldBounds2D.ActiveBounds.Count; i++)
        {
            if (WorldBounds2D.ActiveBounds[i].IsOutside(p, wallSafeDistance)) return false;
        }

        float d2 = minSafeDistance * minSafeDistance;

        for (int i = 0; i < _permanentOccupiedPos.Count; i++)
        {
            if (Vector3.SqrMagnitude(p - _permanentOccupiedPos[i]) < d2) return false;
        }

        if (TrashPool.Instance != null && TrashPool.Instance.ActiveTrashList != null)
        {
            var list = TrashPool.Instance.ActiveTrashList;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].gameObject.activeInHierarchy && !list[i].IsAbsorbing)
                {
                    if (Vector3.SqrMagnitude(p - list[i].transform.position) < d2) return false;
                }
            }
        }
        return true;
    }

    private void PreloadTrashPool()
    {
        if (levelConfig == null) return;
        List<BaseTrash> tempTrashList = new List<BaseTrash>();
        foreach (TrashPreloadConfig config in levelConfig.trashToPreload)
        {
            for (int i = 0; i < config.amount; i++)
            {
                BaseTrash trash = TrashPool.Instance.GetTrash(config.type, 0, Vector3.zero);
                if (trash != null) tempTrashList.Add(trash);
            }
        }
        foreach (BaseTrash trash in tempTrashList) TrashPool.Instance.ReturnTrash(trash);
    }

    private void PreloadObstaclePool()
    {
        if (levelConfig == null || ObstaclePool.Instance == null) return;
        List<BaseObstacle> tempObstacleList = new List<BaseObstacle>();
        foreach (ObstaclePreloadConfig config in levelConfig.obstaclesToPreload)
        {
            for (int i = 0; i < config.amount; i++)
            {
                BaseObstacle obstacle = ObstaclePool.Instance.GetObstacle(config.type, Vector3.zero);
                if (obstacle != null) tempObstacleList.Add(obstacle);
            }
        }
        foreach (BaseObstacle obstacle in tempObstacleList) ObstaclePool.Instance.ReturnObstacle(obstacle);
    }

    private void SpawnObstacleAtPosition(ObstacleType type, Vector3 position)
    {
        BaseObstacle obstacle = ObstaclePool.Instance.GetObstacle(type, position);
        if (obstacle != null) obstacle.transform.SetParent(obstacleParent);
    }

    private BaseTrash SpawnTrashAtPosition(TrashType type, int targetTier, Vector3 position)
    {
        BaseTrash trash = TrashPool.Instance.GetTrash(type, targetTier, position);
        if (trash != null) trash.transform.SetParent(trashParent);
        return trash;
    }

    public void RecalculateTotalTrash()
    {
        if (TrashPool.Instance == null || TrashPool.Instance.ActiveTrashList == null) return;
        int count = 0;
        var list = TrashPool.Instance.ActiveTrashList;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].gameObject.activeInHierarchy && !list[i].IsAbsorbing) count++;
        }
        TrashCounter.SetTotal(count);
    }
}