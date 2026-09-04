
using UnityEngine;
using System.Collections.Generic;


[System.Serializable]
public class TrashPreloadConfig
{
    public TrashType type;
    [Min(0)]
    public int amount ;
}

[System.Serializable]
public class ObstaclePreloadConfig
{
    public ObstacleType type;
    [Min(0)]
    public int amount;
}



[CreateAssetMenu(fileName = "NewPreloadConfig", menuName = "Settings/Preload Config")]
public class PreloadConfigSO : ScriptableObject
{
    [Header("垃圾預載設定")]
    [Tooltip("在此設定要預載的每種垃圾及其數量")]
    public List<TrashPreloadConfig> trashToPreload;

    [Header("障礙物預載設定")]
    [Tooltip("在此設定要預載的每種障礙物及其數量")]
    public List<ObstaclePreloadConfig> obstaclesToPreload;
}