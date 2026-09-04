using UnityEngine;

/// <summary>
/// 所有能被黑洞吸入的物件必須實作此介面
/// </summary>
public interface IAbsorbable
{
    /// <summary>
    /// 當前是否允許被吸入 (例如玩家無敵時回傳 false)
    /// </summary>
    bool CanBeAbsorbed { get; }

    /// <summary>
    /// 當觸發吸入時呼叫
    /// </summary>
    /// <param name="blackHole">吸入它的黑洞實例，用於註冊動畫回調</param>
    void OnAbsorbStart(BlackHoleObstacle blackHole);
}