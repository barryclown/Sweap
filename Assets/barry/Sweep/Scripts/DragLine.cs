using UnityEngine;

public class DragLine : MonoBehaviour
{
    public LineRenderer line;

    [Header("寬度設定")]
    [SerializeField] private float minWidth = 0.05f;
    [SerializeField] private float maxWidth = 0.4f;
    [SerializeField] private float maxLength = 5f;

    [Header("濕滑感（越小越滑/越慢跟手）")]
    [SerializeField] private float positionFollow = 10f; // 3~8 很滑；10~20 比較跟手
    [SerializeField] private float widthFollow = 12f;

    private Vector2 targetStart, targetEnd;
    private Vector2 currentStart, currentEnd;

    private float targetWidth, currentWidth;

    private void Awake()
    {
        if (line == null) line = GetComponent<LineRenderer>();

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;

        // 可選：讓線頭尾更圓潤，看起來更「水」
        line.numCapVertices = 8;
        line.numCornerVertices = 8;
    }

    // 指數平滑：k 越大跟越快；k 越小越「滑」
    private static float ExpSmoothingFactor(float k)
    {
        return 1f - Mathf.Exp(-k * Time.deltaTime);
    }

    public void ShowLine(Vector2 start, Vector2 end)
    {
        if (!line.enabled)
        {
            line.enabled = true;

            // 第一次顯示先對齊，避免從(0,0)滑過來
            targetStart = currentStart = start;
            targetEnd = currentEnd = end;

            targetWidth = currentWidth = CalcWidth(start, end);
            ApplyToRenderer();
            return;
        }

        // 只更新「目標」，真正顯示由 Update() 平滑追上
        targetStart = start;
        targetEnd = end;
        targetWidth = CalcWidth(start, end);
    }

    private void Update()
    {
        if (!line.enabled) return;

        float kp = ExpSmoothingFactor(positionFollow);
        float kw = ExpSmoothingFactor(widthFollow);

        currentStart = Vector2.Lerp(currentStart, targetStart, kp);
        currentEnd = Vector2.Lerp(currentEnd, targetEnd, kp);
        currentWidth = Mathf.Lerp(currentWidth, targetWidth, kw);

        ApplyToRenderer();
    }

    private float CalcWidth(Vector2 start, Vector2 end)
    {
        float length = Vector2.Distance(start, end);
        float t = Mathf.Clamp01(length / maxLength);
        return Mathf.Lerp(minWidth, maxWidth, t);
    }

    private void ApplyToRenderer()
    {
        line.SetPosition(0, currentStart);
        line.SetPosition(1, currentEnd);

        line.startWidth = currentWidth;
        line.endWidth = currentWidth; // 想更像水滴尾巴可改小：currentWidth * 0.4f
    }

    public void HideLine()
    {
        line.enabled = false;
    }
}
