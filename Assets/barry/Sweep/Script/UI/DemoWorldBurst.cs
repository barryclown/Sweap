using UnityEngine;

/// <summary>黑洞吞入瞬間的輕量世界座標爆圈；不依賴額外美術素材。</summary>
public sealed class DemoWorldBurst : MonoBehaviour
{
    private const int SegmentCount = 40;

    private LineRenderer _line;
    private Color _color;
    private float _elapsed;
    private float _duration;
    private float _startRadius;
    private float _endRadius;

    public static void Play(Vector2 position, bool playerAssisted)
    {
        GameObject go = new GameObject("[Demo] Absorption Burst");
        go.transform.position = new Vector3(position.x, position.y, -0.25f);

        DemoWorldBurst burst = go.AddComponent<DemoWorldBurst>();
        burst.Initialize(
            playerAssisted ? new Color(1f, 0.78f, 0.08f, 1f) : new Color(0.45f, 0.55f, 0.65f, 0.7f),
            playerAssisted ? 1.45f : 0.75f,
            playerAssisted ? 0.34f : 0.2f);
    }

    private void Initialize(Color color, float endRadius, float duration)
    {
        _color = color;
        _duration = duration;
        _startRadius = 0.08f;
        _endRadius = endRadius;

        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = false;
        _line.loop = true;
        _line.positionCount = SegmentCount;
        _line.numCapVertices = 2;
        _line.widthMultiplier = 0.09f;
        _line.sortingOrder = 250;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null) _line.material = new Material(shader);

        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / SegmentCount;
            _line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }

        transform.localScale = Vector3.one * _startRadius;
        ApplyColor(_color);
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration));
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float radius = Mathf.Lerp(_startRadius, _endRadius, eased);
        transform.localScale = Vector3.one * radius;

        Color frameColor = _color;
        frameColor.a *= 1f - t;
        ApplyColor(frameColor);
        _line.widthMultiplier = Mathf.Lerp(0.12f, 0.02f, t);

        if (t >= 1f)
        {
            if (_line.material != null) Destroy(_line.material);
            Destroy(gameObject);
        }
    }

    private void ApplyColor(Color color)
    {
        if (_line == null) return;
        _line.startColor = color;
        _line.endColor = color;
    }
}
