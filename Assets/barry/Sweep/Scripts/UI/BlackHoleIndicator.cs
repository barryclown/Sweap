using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform), typeof(Image))]
public class BlackHoleIndicator : MonoBehaviour
{
    [Header("=== 目標設定 ===")]
    [SerializeField, Tooltip("將在運行時自動尋找場景中的 BlackHoleObstacle，無需手動拖曳")]
    private Transform target;

    [Header("=== 顯示設定 ===")]
    [SerializeField, Tooltip("圖示距離螢幕邊緣的留白距離 (像素)")]
    private float edgePadding = 50f;
    [SerializeField, Tooltip("當目標在畫面內時，是否隱藏指標")]
    private bool hideWhenOnScreen = true;
    [SerializeField, Tooltip("圖示預設朝向調整。若你的圖案是朝上，請填 -90；朝右填 0")]
    private float rotationOffset = -90f;

    private RectTransform _rectTransform;
    private Image _image;
    private Camera _mainCamera;
    private Vector2 _screenCenter;
    private Vector2 _screenBounds;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<Image>();
        _mainCamera = Camera.main;

        // 確保剛開始找不到目標時，圖示是隱藏的
        _image.enabled = false;
    }

    private void LateUpdate()
    {
        // 1. 動態捕捉機制：如果目標為空，主動尋找場上的黑洞
        if (target == null)
        {
#if UNITY_2021_3_18_OR_NEWER || UNITY_2022_2_OR_NEWER
            var blackHole = Object.FindAnyObjectByType<BlackHoleObstacle>();
#else
            var blackHole = Object.FindObjectOfType<BlackHoleObstacle>();
#endif
            if (blackHole != null)
            {
                // 成功抓到黑洞，鎖定目標
                target = blackHole.transform;
            }
            else
            {
                // 場上目前沒有黑洞，隱藏指標並退出運算
                _image.enabled = false;
                return;
            }
        }

        if (_mainCamera == null) return;

        UpdateIndicator();
    }

    private void UpdateIndicator()
    {
        Vector3 screenPos = _mainCamera.WorldToScreenPoint(target.position);

        if (screenPos.z < 0)
        {
            screenPos *= -1;
        }

        _screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        _screenBounds = _screenCenter - new Vector2(edgePadding, edgePadding);

        bool isOffScreen = screenPos.x <= 0 || screenPos.x >= Screen.width ||
                           screenPos.y <= 0 || screenPos.y >= Screen.height ||
                           screenPos.z < 0;

        if (!isOffScreen && hideWhenOnScreen)
        {
            _image.enabled = false;
            return;
        }

        _image.enabled = true;

        Vector2 offset = (Vector2)screenPos - _screenCenter;
        Vector2 dir = offset.normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _rectTransform.rotation = Quaternion.Euler(0, 0, angle + rotationOffset);

        float ratioX = _screenBounds.x / Mathf.Max(Mathf.Abs(offset.x), 0.0001f);
        float ratioY = _screenBounds.y / Mathf.Max(Mathf.Abs(offset.y), 0.0001f);

        float minRatio = Mathf.Min(ratioX, ratioY);

        if (minRatio < 1f)
        {
            _rectTransform.position = _screenCenter + (offset * minRatio);
        }
        else
        {
            _rectTransform.position = screenPos;
        }
    }
}