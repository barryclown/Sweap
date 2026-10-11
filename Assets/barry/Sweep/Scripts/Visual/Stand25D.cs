using UnityEngine;

/// <summary>
/// 把平貼在地上的 2D 圖改成「站在地上、朝鏡頭立起」的立牌，並在腳下補一個貼地軟陰影。
/// 只換圖像：原本的 SpriteRenderer 繼續給動畫、程式與碰撞擬合使用，只是不再算繪；
/// 物理、碰撞、WorldBounds2D 完全不動。編輯器（非 Play）裡維持原本平放的樣子，方便擺場景。
/// 立牌與陰影建在原圖的父物件底下（與原圖同層），跟著物件一起移動、縮放、被物件池開關。
/// 欄位由 Sweep25DVisualSetup 依圖片實際不透明範圍算好寫入。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class Stand25D : MonoBehaviour
{
    public enum Anchor
    {
        VisibleBase,   // 家具：圖上看得到的底邊中點踩在地上
        VisibleCenter  // 垃圾、黑洞：物件中心就是著地點，立牌往上長半個身高
    }

    [SerializeField] private Anchor anchor = Anchor.VisibleBase;
    [SerializeField] private float tiltDegrees = -50f;
    [SerializeField] private Vector2 visibleBaseLocal;
    [SerializeField] private Vector2 visibleCenterLocal;
    [SerializeField] private Vector2 visibleSizeLocal = Vector2.one;
    [Tooltip("跟著物理本體的 Z 旋轉，在牌面內轉（垃圾被撞飛時的翻滾）。")]
    [SerializeField] private bool followSpin;
    [Tooltip("不會移動的家具：第一次擺好後就不再每幀更新。")]
    [SerializeField] private bool isStatic;
    [Tooltip("靠牆擺設：著地點固定在指定 y（貼著後牆）。")]
    [SerializeField] private bool overrideGroundY;
    [SerializeField] private float groundY;
    [SerializeField, Min(0f)] private float hoverHeight;
    [SerializeField, Min(0f)] private float bobAmplitude;
    [SerializeField, Min(0f)] private float bobFrequency = 1.2f;
    [SerializeField] private Sprite shadowSprite;
    [SerializeField, Min(0f)] private float shadowWidth = 0.85f;
    [SerializeField, Min(0f)] private float shadowDepth = 0.32f;
    [SerializeField, Range(0f, 1f)] private float shadowAlpha = 0.35f;
    [SerializeField] private int depthBias;

    private SpriteRenderer _source;
    private SpriteRenderer _proxy;
    private SpriteRenderer _shadow;
    private Transform _sourceTransform;
    private Transform _proxyTransform;
    private Transform _shadowTransform;
    private float _bobPhase;
    private bool _placedOnce;

    public Anchor AnchorMode => anchor;
    public float TiltDegrees => tiltDegrees;
    public Vector2 VisibleBaseLocal => visibleBaseLocal;
    public Vector2 VisibleCenterLocal => visibleCenterLocal;
    public Vector2 VisibleSizeLocal => visibleSizeLocal;
    public bool FollowSpin => followSpin;
    public bool IsStatic => isStatic;
    public bool HasShadow => shadowSprite != null;
    public SpriteRenderer Proxy => _proxy;
    public SpriteRenderer Shadow => _shadow;

    private void Awake()
    {
        _source = GetComponent<SpriteRenderer>();
        _sourceTransform = transform;
        Transform parent = _sourceTransform.parent;

        _proxy = CreateRenderer("[2.5D] " + name, parent);
        _proxy.sharedMaterial = _source.sharedMaterial;
        _proxy.sortingLayerID = _source.sortingLayerID;
        _proxy.maskInteraction = _source.maskInteraction;
        _proxyTransform = _proxy.transform;
        _proxyTransform.localScale = _sourceTransform.localScale;

        if (shadowSprite != null)
        {
            _shadow = CreateRenderer("[2.5D shadow] " + name, parent);
            _shadow.sprite = shadowSprite;
            _shadow.sharedMaterial = _source.sharedMaterial;
            _shadow.sortingLayerID = _source.sortingLayerID;
            _shadow.sortingOrder = Depth25D.FloorDecalOrder;
            _shadow.color = new Color(0f, 0f, 0f, shadowAlpha);
            _shadowTransform = _shadow.transform;
        }

        _source.forceRenderingOff = true;
        // 每個物件漂浮相位錯開，但同一物件每次啟動都一樣（錄影可重現）。
        Vector3 seed = _sourceTransform.position;
        _bobPhase = Mathf.Repeat(seed.x * 12.9898f + seed.y * 78.233f, 1f) * Mathf.PI * 2f;
    }

    private void OnEnable()
    {
        SetProxiesActive(true);
    }

    private void OnDisable()
    {
        SetProxiesActive(false);
    }

    private void OnDestroy()
    {
        if (_proxy != null) Destroy(_proxy.gameObject);
        if (_shadow != null) Destroy(_shadow.gameObject);
    }

    private void LateUpdate()
    {
        if (_proxy == null) return;
        if (isStatic && _placedOnce) return;

        _proxy.enabled = _source.enabled;
        _proxy.sprite = _source.sprite;
        _proxy.color = _source.color;
        _proxy.flipX = _source.flipX;
        _proxy.flipY = _source.flipY;
        Place();
        _placedOnce = true;
    }

    private void Place()
    {
        Vector3 scale = _sourceTransform.lossyScale;
        float spin = followSpin ? _sourceTransform.eulerAngles.z : 0f;
        Quaternion tilt = Quaternion.Euler(tiltDegrees, 0f, 0f);
        Quaternion rotation = tilt * Quaternion.Euler(0f, 0f, spin);
        Vector2 anchorLocal = anchor == Anchor.VisibleBase ? visibleBaseLocal : visibleCenterLocal;

        Vector3 ground = _sourceTransform.TransformPoint(anchorLocal);
        if (overrideGroundY) ground.y = groundY;
        ground.z = 0f;

        float lift = hoverHeight;
        if (bobAmplitude > 0f)
            lift += (Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f + _bobPhase) * 0.5f + 0.5f) * bobAmplitude;
        lift *= Mathf.Abs(scale.y);

        Vector3 anchorWorld = ground + Vector3.back * lift;
        if (anchor == Anchor.VisibleCenter)
            anchorWorld += tilt * new Vector3(0f, visibleSizeLocal.y * 0.5f * Mathf.Abs(scale.y), 0f);

        Vector3 offset = rotation * Vector3.Scale(anchorLocal, scale);
        _proxyTransform.SetPositionAndRotation(anchorWorld - offset, rotation);
        _proxyTransform.localScale = _sourceTransform.localScale;
        _proxy.sortingOrder = Depth25D.OrderFor(ground.y) + depthBias;

        if (_shadow != null) PlaceShadow(ground, scale, lift);
    }

    private void PlaceShadow(Vector3 ground, Vector3 scale, float lift)
    {
        Transform parent = _shadowTransform.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        Vector2 spriteSize = shadowSprite.bounds.size;
        bool visible = _proxy.enabled && Mathf.Abs(parentScale.x) > 0.0001f && Mathf.Abs(parentScale.y) > 0.0001f &&
                       spriteSize.x > 0f && spriteSize.y > 0f;
        _shadow.enabled = visible;
        if (!visible) return;

        // 越高影子越小越淡，給漂浮物件高度感。
        float heightFade = 1f / (1f + lift * 0.35f);
        float width = visibleSizeLocal.x * Mathf.Abs(scale.x) * shadowWidth * Mathf.Lerp(0.75f, 1f, heightFade);
        float depth = width * shadowDepth;
        _shadowTransform.SetPositionAndRotation(ground + Vector3.back * 0.01f, Quaternion.identity);
        _shadowTransform.localScale = new Vector3(
            width / (spriteSize.x * Mathf.Abs(parentScale.x)),
            depth / (spriteSize.y * Mathf.Abs(parentScale.y)),
            1f);
        _shadow.color = new Color(0f, 0f, 0f, shadowAlpha * heightFade);
    }

    private void SetProxiesActive(bool active)
    {
        if (_proxy != null) _proxy.gameObject.SetActive(active);
        if (_shadow != null) _shadow.gameObject.SetActive(active);
    }

    private SpriteRenderer CreateRenderer(string objectName, Transform parent)
    {
        GameObject go = new GameObject(objectName);
        go.hideFlags = HideFlags.DontSave;
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return renderer;
    }
}
