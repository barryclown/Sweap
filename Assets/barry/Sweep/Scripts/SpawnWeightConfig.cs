using UnityEngine;

[CreateAssetMenu(fileName = "SpawnWeightConfig", menuName = "Game/Config/Spawn Weight Config")]
public class SpawnWeightConfig : ScriptableObject
{
    [System.Serializable]
    public struct TierWeight
    {
        [Tooltip("依序填入 T1 到 T5 的權重。陣列長度必須為 5。")]
        public int[] weights;
    }

    [Tooltip("陣列索引 0 代表 LV1，依序對應到滿級配置。")]
    public TierWeight[] levelWeights;

    // [重點註釋] 輪盤賭算法（Roulette Wheel Selection）。利用預先配置的權重陣列進行 O(N) 檢索。因 N 恆為 5，無須實作複雜的二分搜尋以避免過度工程。
    public int GetRandomTier(int currentLevel)
    {
        if (levelWeights == null || levelWeights.Length == 0) return 1;

        int index = Mathf.Clamp(currentLevel - 1, 0, levelWeights.Length - 1);
        int[] weights = levelWeights[index].weights;

        if (weights == null || weights.Length != 5)
        {
            Debug.LogError("[SpawnWeightConfig] 權重陣列設定錯誤，長度必須為 5。");
            return 1;
        }

        int totalWeight = 0;
        for (int i = 0; i < 5; i++)
        {
            totalWeight += weights[i];
        }

        if (totalWeight <= 0) return 1;

        int randomValue = Random.Range(0, totalWeight);
        int currentSum = 0;

        for (int i = 0; i < 5; i++)
        {
            currentSum += weights[i];
            if (randomValue < currentSum)
            {
                return i + 1;
            }
        }

        return 1;
    }
}