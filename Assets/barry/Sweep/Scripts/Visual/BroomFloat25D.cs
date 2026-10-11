using UnityEngine;

/// <summary>
/// 掃把是朝鏡頭立著的立牌（原本就斜 50°）。這裡負責：輕微上下漂浮、貼地影子跟著呼吸、
/// 依著地點 y 參與 2.5D 前後遮擋。靜止位置與影子著地點由 Sweep25DVisualSetup 算好寫入。
/// </summary>
[DisallowMultipleComponent]
public sealed class BroomFloat25D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer broom;
    [SerializeField] private SpriteRenderer shadow;
    [SerializeField] private Vector3 restLocalPosition;
    [SerializeField] private Vector3 shadowRestLocalScale = Vector3.one;
    [SerializeField, Min(0f)] private float bobAmplitude = 0.12f;
    [SerializeField, Min(0f)] private float bobFrequency = 1.4f;
    [SerializeField, Range(0f, 1f)] private float shadowAlpha = 0.6f;
    [SerializeField] private int depthBias = 1;

    private Transform _broomTransform;
    private Transform _shadowTransform;
    private Transform _root;

    public SpriteRenderer Broom => broom;
    public SpriteRenderer FloorShadow => shadow;
    public Vector3 RestLocalPosition => restLocalPosition;

    private void Awake()
    {
        if (broom != null)
        {
            _broomTransform = broom.transform;
            _root = _broomTransform.parent;
        }
        if (shadow != null)
        {
            _shadowTransform = shadow.transform;
            shadow.sortingOrder = Depth25D.FloorDecalOrder;
        }
    }

    private void LateUpdate()
    {
        if (_broomTransform == null || _root == null) return;

        float rootScale = Mathf.Abs(_root.lossyScale.x);
        float bob01 = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * 0.5f + 0.5f;
        _broomTransform.position = _root.TransformPoint(restLocalPosition) + Vector3.back * (bob01 * bobAmplitude * rootScale);

        float groundY = _shadowTransform != null ? _shadowTransform.position.y : _root.position.y;
        broom.sortingOrder = Depth25D.OrderFor(groundY) + depthBias;

        if (_shadowTransform == null) return;
        _shadowTransform.localScale = shadowRestLocalScale * Mathf.Lerp(1f, 0.86f, bob01);
        Color color = shadow.color;
        color.a = shadowAlpha * Mathf.Lerp(1f, 0.75f, bob01);
        shadow.color = color;
    }
}
