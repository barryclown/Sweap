using System.Collections.Generic;
using UnityEngine;

public class SpatialGridManager : MonoBehaviour
{
    public static SpatialGridManager Instance { get; private set; }

    [Header("Grid Settings")]
    public float cellSize = 2.0f;

    // 將二維座標壓縮為 long 作為 Key，避免 Vector2Int 的 Hash 運算效能損耗
    private readonly Dictionary<long, List<BaseTrash>> grid = new Dictionary<long, List<BaseTrash>>();

    // 緩存 cellSize 的倒數，將高耗能的「除法」轉為「乘法」
    private float invCellSize;

    public float MaxRegisteredTrashExtent { get; private set; } = 2f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            invCellSize = 1f / cellSize;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void UpdateGrid(List<BaseTrash> activeTrashList)
    {
        // 清除舊參照，避免物件移出網格後殘留
        foreach (var kv in grid)
        {
            kv.Value.Clear();
        }

        MaxRegisteredTrashExtent = 0f;
        int count = activeTrashList.Count;
        for (int i = 0; i < count; i++)
        {
            var trash = activeTrashList[i];
            if (trash == null || trash.IsAbsorbing) continue;

            MaxRegisteredTrashExtent = Mathf.Max(
                MaxRegisteredTrashExtent,
                trash.GetCollisionExtent());

            long cellKey = GetCellKey(trash.transform.position);

            if (!grid.TryGetValue(cellKey, out var cellList))
            {
                cellList = new List<BaseTrash>(16); // 預分配初始容量，減少擴容開銷
                grid[cellKey] = cellList;
            }
            cellList.Add(trash);
        }

        MaxRegisteredTrashExtent = Mathf.Max(MaxRegisteredTrashExtent, cellSize * 0.5f);
    }

    public void GetNearbyTrash(BaseTrash trash, List<BaseTrash> buffer)
    {
        if (trash == null) return;
        GetTrashAroundPosition(trash.transform.position, buffer);
    }

    public void GetTrashAroundPosition(Vector3 position, List<BaseTrash> buffer)
    {
        GetTrashAroundPosition(position, buffer, cellSize);
    }

    public void GetTrashAroundPosition(Vector3 position, List<BaseTrash> buffer, float searchRadius)
    {
        buffer.Clear();

        // 使用乘法替換除法
        int centerX = Mathf.FloorToInt(position.x * invCellSize);
        int centerY = Mathf.FloorToInt(position.y * invCellSize);

        int cellRange = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, searchRadius) * invCellSize));

        for (int x = -cellRange; x <= cellRange; x++)
        {
            for (int y = -cellRange; y <= cellRange; y++)
            {
                long checkKey = GetKeyFromCoordinates(centerX + x, centerY + y);

                if (grid.TryGetValue(checkKey, out var cellContent))
                {
                    int contentCount = cellContent.Count;
                    // 捨棄 AddRange，改用 for 迴圈確保無裝箱與 Enumerator 分配開銷
                    for (int i = 0; i < contentCount; i++)
                    {
                        buffer.Add(cellContent[i]);
                    }
                }
            }
        }
    }

    // 將世界座標轉換為 64-bit 整數 Key (前 32-bit 為 x，後 32-bit 為 y)
    private long GetCellKey(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x * invCellSize);
        int y = Mathf.FloorToInt(position.y * invCellSize);
        return GetKeyFromCoordinates(x, y);
    }

    // 位元運算壓縮
    private long GetKeyFromCoordinates(int x, int y)
    {
        return ((long)x << 32) | (uint)y;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = Color.blue;
        Vector3 cubeSize = new Vector3(cellSize, cellSize, 0.1f);

        foreach (var kv in grid)
        {
            if (kv.Value.Count == 0) continue;

            // 解壓縮 long 取回 x 與 y
            int x = (int)(kv.Key >> 32);
            int y = (int)kv.Key;

            Vector3 center = new Vector3(
                (x + 0.5f) * cellSize,
                (y + 0.5f) * cellSize,
                0f
            );
            Gizmos.DrawWireCube(center, cubeSize);
        }
    }
#endif
}
