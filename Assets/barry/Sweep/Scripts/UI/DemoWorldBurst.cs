using UnityEngine;

/// <summary>
/// 黑洞吞入瞬間的世界座標雙爆圈。固定預熱池與共享材質讓狂熱階段不再反覆
/// Instantiate、配置 Material、Destroy。
/// </summary>
public sealed class DemoWorldBurst : MonoBehaviour
{
    public const int PoolCapacity = 14;
    private const int SegmentCount = 40;

    private static readonly DemoWorldBurst[] Pool = new DemoWorldBurst[PoolCapacity];
    private static Material _sharedMaterial;
    private static int _replacementCursor;

    private LineRenderer _outerLine;
    private LineRenderer _innerLine;
    private Color _color;
    private float _elapsed;
    private float _duration;
    private float _startRadius;
    private float _endRadius;

    public static void Prewarm()
    {
        EnsurePool();
    }

    public static void Play(
        Vector2 position,
        bool playerAssisted,
        int combo = 0,
        bool frenzy = false)
    {
        EnsurePool();
        DemoWorldBurst burst = Acquire();

        float combo01 = Mathf.Clamp01(combo / 15f);
        Color color = playerAssisted
            ? Color.Lerp(new Color(1f, 0.8f, 0.08f, 1f), new Color(1f, 0.22f, 0.04f, 1f), combo01)
            : new Color(0.45f, 0.55f, 0.65f, 0.62f);
        float radius = playerAssisted ? Mathf.Lerp(1.35f, 2.15f, combo01) : 0.72f;
        float duration = playerAssisted ? 0.34f : 0.2f;

        if (frenzy && playerAssisted)
        {
            color = new Color(1f, 0.16f, 0.035f, 1f);
            radius *= 1.18f;
            duration = 0.4f;
        }

        burst.Restart(position, color, radius, duration);
    }

    private static void EnsurePool()
    {
        EnsureSharedMaterial();

        for (int i = 0; i < Pool.Length; i++)
        {
            if (Pool[i] != null) continue;

            GameObject go = new GameObject($"[Demo] Absorption Burst {i + 1:00}");
            go.hideFlags = HideFlags.DontSave;
            DemoWorldBurst burst = go.AddComponent<DemoWorldBurst>();
            burst.BuildRenderers();
            go.SetActive(false);
            Pool[i] = burst;
        }
    }

    private static void EnsureSharedMaterial()
    {
        if (_sharedMaterial != null) return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) return;

        _sharedMaterial = new Material(shader)
        {
            name = "[Demo] Shared Burst Material",
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static DemoWorldBurst Acquire()
    {
        for (int i = 0; i < Pool.Length; i++)
        {
            if (Pool[i] != null && !Pool[i].gameObject.activeSelf) return Pool[i];
        }

        DemoWorldBurst replacement = Pool[_replacementCursor % Pool.Length];
        _replacementCursor = (_replacementCursor + 1) % Pool.Length;
        return replacement;
    }

    private void BuildRenderers()
    {
        _outerLine = CreateRing("Outer", 250, 0.11f);
        _innerLine = CreateRing("Inner", 251, 0.055f);
    }

    private LineRenderer CreateRing(string ringName, int sortingOrder, float width)
    {
        GameObject ring = new GameObject(ringName);
        ring.hideFlags = HideFlags.DontSave;
        ring.transform.SetParent(transform, false);

        LineRenderer line = ring.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = SegmentCount;
        line.numCapVertices = 2;
        line.widthMultiplier = width;
        line.sortingOrder = sortingOrder;
        line.sharedMaterial = _sharedMaterial;

        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = (Mathf.PI * 2f * i) / SegmentCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }
        return line;
    }

    private void Restart(Vector2 position, Color color, float endRadius, float duration)
    {
        transform.position = new Vector3(position.x, position.y, -0.25f);
        _color = color;
        _elapsed = 0f;
        _duration = Mathf.Max(0.05f, duration);
        _startRadius = 0.08f;
        _endRadius = Mathf.Max(_startRadius, endRadius);
        transform.localScale = Vector3.one * _startRadius;
        gameObject.SetActive(true);
        ApplyColor(_color);
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_elapsed / _duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float radius = Mathf.Lerp(_startRadius, _endRadius, eased);
        transform.localScale = Vector3.one * radius;

        Color outerColor = _color;
        outerColor.a *= 1f - t;
        Color innerColor = Color.Lerp(Color.white, _color, 0.65f);
        innerColor.a = outerColor.a * Mathf.Clamp01(1.25f - t);
        ApplyColor(_outerLine, outerColor);
        ApplyColor(_innerLine, innerColor);

        _outerLine.widthMultiplier = Mathf.Lerp(0.13f, 0.018f, t);
        _innerLine.widthMultiplier = Mathf.Lerp(0.07f, 0.01f, t);
        _innerLine.transform.localScale = Vector3.one * Mathf.Lerp(0.38f, 0.82f, eased);

        if (t >= 1f) gameObject.SetActive(false);
    }

    private void ApplyColor(Color color)
    {
        ApplyColor(_outerLine, color);
        Color inner = Color.Lerp(Color.white, color, 0.65f);
        ApplyColor(_innerLine, inner);
    }

    private static void ApplyColor(LineRenderer line, Color color)
    {
        if (line == null) return;
        line.startColor = color;
        line.endColor = color;
    }
}
