using UnityEngine;
using UnityEngine.UI;

// 強制依賴 Button 組件，避免掛錯物件導致報錯
[RequireComponent(typeof(Button))]
public class UIButtonSound : MonoBehaviour
{
    private Button targetButton;

    private void Awake()
    {
        targetButton = GetComponent<Button>();

        // 動態綁定點擊事件，免除手動設定面板的繁瑣，並完美支援程式動態生成的按鈕
        targetButton.onClick.AddListener(OnButtonClicked);
    }

    private void OnButtonClicked()
    {
        // 確保全域管理員存在時才觸發，避免切換場景或關閉遊戲時的空參考異常
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayUIClick();
        }
    }

    private void OnDestroy()
    {
        // 防禦性設計：物件銷毀時主動解除綁定，阻絕潛在的記憶體流失
        if (targetButton != null)
        {
            targetButton.onClick.RemoveListener(OnButtonClicked);
        }
    }
}