using System;
using System.Collections.Generic;
using UnityEngine;

public static class TrashCounter
{
    public static int Total { get; private set; }
    public static int Collected { get; private set; }

    // [Why] 改用 HashSet<BaseTrash> 直接記錄物件參考，徹底避開 EntityId 轉型的過時警告，且物件參考比對效能最佳
    private static readonly HashSet<BaseTrash> _collectedTrashes = new HashSet<BaseTrash>();

    public static event Action<int, int> Changed;

    public static void Reset()
    {
        Total = 0;
        Collected = 0;
        _collectedTrashes.Clear();
        Changed?.Invoke(Collected, Total);
    }

    public static void SetTotal(int total)
    {
        Total = Mathf.Max(0, total);
        Collected = Mathf.Clamp(Collected, 0, Total);
        Changed?.Invoke(Collected, Total);
    }

    public static void MarkCollected(BaseTrash trash)
    {
        if (trash == null) return;

        if (_collectedTrashes.Contains(trash)) return;

        _collectedTrashes.Add(trash);
        Collected = Mathf.Min(Collected + 1, Total);
        Changed?.Invoke(Collected, Total);
    }
}