using System.Collections.Generic;
using UnityEngine;

public class SceneTrashGridUpdater : MonoBehaviour
{
    // 這個 list 會自動由子物件裡的 BaseTrash 填滿
    private readonly List<BaseTrash> _trashList = new List<BaseTrash>();

    private void Awake()
    {
        RefreshTrashList();
    }

    // 如果你之後在執行中有新增 / 刪除子物件，可以自動重新抓一次
    private void OnTransformChildrenChanged()
    {
        RefreshTrashList();
    }

    private void FixedUpdate()
    {
        if (SpatialGridManager.Instance == null) return;

        // 把「List」底下的垃圾更新進網格
        SpatialGridManager.Instance.UpdateGrid(_trashList);
    }

    private void RefreshTrashList()
    {
        _trashList.Clear();

        // 把所有子物件上的 BaseTrash 抓出來
        // 兩種寫法，看你 Unity 版本支援哪一個

        // 寫法 1（較新版本 Unity 支援）：
        // GetComponentsInChildren(true, _trashList);

        // 寫法 2（相容所有版本）：
        BaseTrash[] trashArray = GetComponentsInChildren<BaseTrash>(true);
        _trashList.AddRange(trashArray);
    }
}
