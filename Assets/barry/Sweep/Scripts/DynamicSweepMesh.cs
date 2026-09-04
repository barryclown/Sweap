using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DynamicSweepMesh : MonoBehaviour
{
    private Mesh mesh;
    private MeshRenderer meshRenderer;

    [Header("貼圖/材質")]
    [SerializeField] private Texture2D sweepTexture;
    [SerializeField] private Material overrideMaterial;
    [SerializeField] private bool createMaterialIfNull = true;

    [Header("渲染排序(2D 常用)")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 0;

    [Header("扇形半徑設定")]
    public float minRadius = 1f;
    public float maxRadius = 3f;

    [Header("弧線 Segments")]
    [Range(2, 64)]
    public int arcSegments = 12;

    [Header("扇形角度設定")]
    public float minArcAngle = 30f;
    public float maxArcAngle = 90f;

    [Header("形狀旋轉 (度)")]
    [Tooltip("0=朝上(+Y)。你若想讓扇形預設朝右，就設 -90。")]
    public float shapeRotationDeg = 0f;

    public float CurrentRadius { get; private set; }
    public Vector2[] CurrentPath2D { get; private set; }

    private Vector3[] _cachedVertices;
    private Vector2[] _cachedPath2D;
    private Vector2[] _cachedUVs;
    private int[] _cachedTriangles;
    private int _cachedSegmentCount = -1; // 用來檢查 segments 是否有變動

    public void UpdateShape(float t)
    {
        if (mesh == null) return;

        t = Mathf.Clamp01(t);
        float radius = Mathf.Lerp(minRadius, maxRadius, t);

        float arcDeg = Mathf.Lerp(minArcAngle, maxArcAngle, t);
        float halfRad = arcDeg * Mathf.Deg2Rad * 0.5f;

        CurrentRadius = radius;

        int seg = Mathf.Max(2, arcSegments);
        int vertexCount = 1 + (seg + 1);

        // ===只在陣列不存在或長度改變時，才分配新記憶體 ===
        if (_cachedVertices == null || _cachedVertices.Length != vertexCount || _cachedSegmentCount != seg)
        {
            _cachedVertices = new Vector3[vertexCount];
            _cachedPath2D = new Vector2[vertexCount];
            _cachedUVs = new Vector2[vertexCount];
            _cachedTriangles = new int[seg * 3];

            // 讓外部也能拿到最新的陣列物件
            CurrentPath2D = _cachedPath2D;
            _cachedSegmentCount = seg;
        }

        // Apex (頂點)
        _cachedVertices[0] = Vector3.zero;
        _cachedPath2D[0] = Vector2.zero;
        _cachedUVs[0] = new Vector2(0.5f, 1f);

        float maxX = Mathf.Sin(halfRad) * radius;
        if (maxX < 0.0001f) maxX = 0.0001f;

        float rot = shapeRotationDeg * Mathf.Deg2Rad;
        float cosR = Mathf.Cos(rot);
        float sinR = Mathf.Sin(rot);

        // 計算頂點資料 (直接填入快取的陣列，不產生垃圾)
        for (int i = 0; i <= seg; i++)
        {
            float tt = i / (float)seg;
            float angle = Mathf.Lerp(halfRad, -halfRad, tt);

            float x = Mathf.Sin(angle) * radius;
            float y = Mathf.Cos(angle) * radius;

            float rx = x * cosR - y * sinR;
            float ry = x * sinR + y * cosR;

            int idx = 1 + i;
           
            _cachedVertices[idx].x = rx;
            _cachedVertices[idx].y = ry;
            _cachedVertices[idx].z = 0f;

            _cachedPath2D[idx].x = rx;
            _cachedPath2D[idx].y = ry;

            float u = 0.5f + (rx / (2f * maxX));
            float v = 1f - (ry / radius);

            _cachedUVs[idx].x = Mathf.Clamp01(u);
            _cachedUVs[idx].y = Mathf.Clamp01(v);
        }

        // 三角形索引 
        int ti = 0;
        for (int i = 0; i < seg; i++)
        {
            _cachedTriangles[ti++] = 0;
            _cachedTriangles[ti++] = i + 1;
            _cachedTriangles[ti++] = i + 2;
        }

        // 更新 Mesh
        mesh.Clear(); // 為了安全先 Clear，若確定頂點數不變可移除這行以極致優化
        mesh.vertices = _cachedVertices;
        mesh.triangles = _cachedTriangles;
        mesh.uv = _cachedUVs;

        
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        mesh = new Mesh { name = "SweepMesh" };
        mesh.MarkDynamic(); // 告訴 Unity 這個 Mesh 會頻繁變動
        mf.mesh = mesh;

        SetupRendererMaterial();
        UpdateShape(0f);
    }

    private void SetupRendererMaterial()
    {
        if (meshRenderer == null) return;

        meshRenderer.sortingLayerName = sortingLayerName;
        meshRenderer.sortingOrder = sortingOrder;

        if (sweepTexture != null)
        {
            sweepTexture.wrapMode = TextureWrapMode.Clamp;
            sweepTexture.filterMode = FilterMode.Bilinear;
        }

        if (overrideMaterial != null)
        {
            meshRenderer.material = overrideMaterial;
            if (sweepTexture != null) meshRenderer.material.mainTexture = sweepTexture;
            return;
        }

        if (!createMaterialIfNull) return;

        Shader shader =
            Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Transparent");

        var mat = new Material(shader) { name = "SweepMesh_Mat(Auto)" };
        if (sweepTexture != null) mat.mainTexture = sweepTexture;

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

        meshRenderer.material = mat;
    }
}