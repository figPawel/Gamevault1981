using System.Collections.Generic;
using UnityEngine;

/// Gamevault 1981 — MenuGridController (simple & sane)
/// - Endless neon grid with optional squeeze toward a Vault
/// - Cartridges sit on tile centers (post-warp), with tiny bounded jitter inside each tile
/// - Single Vault instance; single cartridge pool; proper cleanup
[ExecuteAlways]
public class MenuGridController : MonoBehaviour
{
    // ---------- Camera / Horizon ----------
    [Header("Camera / Horizon")]
    public Camera targetCamera;
    public float cameraHeight = 6f;
    [Range(5f, 60f)] public float cameraTilt = 22f;

    [Tooltip("Distance to the vanishing point along +Z. This is a free slider unless Auto Align is ON.")]
    public float horizonDistance = 80f;

    [Tooltip("If ON, horizonDistance is forced to the far edge of the grid + gap.")]
    public bool autoAlignHorizon = false;
    public float horizonGap = 2f;

    // ---------- Grid ----------
    [Header("Grid")]
    public int cellsX = 24;
    public int cellsZVisible = 64;
    public float cellSize = 1f;
    public float scrollSpeed = 3.5f;

    [Tooltip("Line thickness approximation (drawn with multi-pass).")]
    public float lineWidth = 0.02f;

    [Header("Grid Squeeze")]
    [Range(0f, 1f)] public float squeezeAmount = 0.45f;   // 0=flat, 1=collapse at end
    [Range(0.5f, 5f)] public float squeezeExponent = 2f;  // >1 = stronger near the end

    [Header("Color")]
    public float hueScroll = 0.08f;
    [Range(0f, 1f)] public float saturation = 0.8f;
    [Range(0f, 1.5f)] public float value = 1.0f;
    [Range(0.2f, 4f)] public float glow = 1.35f;

    // ---------- Vault ----------
    [Header("Vault (Vanishing Point)")]
    public bool showVault = true;
    public float vaultRadius = 0.9f;
    public float vaultPulse = 1.5f;
    public float vaultLightRange = 18f;
    public float vaultLightIntensity = 3f;

    // ---------- Cartridges ----------
    [Header("Cartridges")]
    public GameObject cartridgePrefab;
    public int cartridgesToShow = 12;

    [Tooltip("Rows (inclusive) for cartridge placement, measured in grid rows from camera.")]
    public int cartRowMin = 6;
    public int cartRowMax = 36;

    [Tooltip("Fraction of half-tile you allow as jitter INSIDE the tile. 0 = perfectly centered.")]
    [Range(0f, 0.49f)] public float cartJitterFrac = 0.12f;

    public float cartHeight = 0.02f;
    public bool cartYawOnly = true;
    [Range(-30f, 30f)] public float cartPitch = -10f;
    [Range(-15f, 15f)] public float cartRoll = 0f;

    // ---------- Internals ----------
    Mesh _gridMesh;
    Material _lineMat;
    Vector3 _gridOrigin;   // we slide along Z for the endless effect
    float _scroll;
    Transform _vault;

    readonly List<Transform> _cartPool = new();
    readonly List<(int col, int row, Vector2 jitter)> _cartSlots = new();

    const string INTERNAL_MAT = "Hidden/Internal-Colored";

    void OnEnable()
    {
        if (!targetCamera) targetCamera = Camera.main;
        PoseCamera();
        AlignHorizonIfWanted();
        BuildGrid(force:true);
        EnsureLineMat();
        EnsureVault();
        RebuildCartridgePool();
        AssignCartSlots();
        UpdateCartPositions();
    }

    void OnValidate()
    {
        cellsX = Mathf.Max(2, cellsX);
        cellsZVisible = Mathf.Max(8, cellsZVisible);
        cellSize = Mathf.Max(0.05f, cellSize);
        lineWidth = Mathf.Clamp(lineWidth, 0.001f, 0.2f);

        cartRowMin = Mathf.Clamp(cartRowMin, 0, Mathf.Max(0, cellsZVisible - 1));
        cartRowMax = Mathf.Clamp(cartRowMax, cartRowMin, Mathf.Max(cartRowMin, cellsZVisible - 1));

        PoseCamera();
        AlignHorizonIfWanted();
        BuildGrid();
        EnsureVault();
        RebuildCartridgePool();
        AssignCartSlots();
        UpdateCartPositions();
    }

    void Update()
    {
        if (!Application.isPlaying) PoseCamera();

        _scroll += scrollSpeed * Time.deltaTime;
        _gridOrigin.z = -Mathf.Repeat(_scroll, cellSize); // endless slide in Z

        if (showVault && _vault)
        {
            _vault.position = new Vector3(0f, 0f, horizonDistance);
            float s = vaultRadius * (1f + 0.15f * Mathf.Sin(Time.time * vaultPulse * 2f));
            _vault.localScale = Vector3.one * s;
        }

        UpdateCartPositions();
    }

    void OnDisable() { Cleanup(); }
    void OnDestroy() { Cleanup(); }

    void Cleanup()
    {
        if (_gridMesh) { if (Application.isPlaying) Destroy(_gridMesh); else DestroyImmediate(_gridMesh); _gridMesh = null; }
        if (_lineMat)  { if (Application.isPlaying) Destroy(_lineMat);  else DestroyImmediate(_lineMat);  _lineMat  = null; }
        if (_vault)    { if (Application.isPlaying) Destroy(_vault.gameObject); else DestroyImmediate(_vault.gameObject); _vault = null; }

        for (int i = _cartPool.Count - 1; i >= 0; i--)
            if (_cartPool[i])
                if (Application.isPlaying) Destroy(_cartPool[i].gameObject);
                else DestroyImmediate(_cartPool[i].gameObject);
        _cartPool.Clear();
        _cartSlots.Clear();
    }

    // ---------- Camera / Horizon ----------
    void PoseCamera()
    {
        if (!targetCamera) return;
        targetCamera.transform.position = new Vector3(0f, cameraHeight, 0f);
        targetCamera.transform.rotation = Quaternion.Euler(cameraTilt, 0f, 0f);
        targetCamera.nearClipPlane = 0.05f;
        targetCamera.farClipPlane = Mathf.Max(targetCamera.farClipPlane, horizonDistance * 2f);
    }

    void AlignHorizonIfWanted()
    {
        if (!autoAlignHorizon) return;
        horizonDistance = cellsZVisible * cellSize + Mathf.Max(0f, horizonGap);
    }

    // ---------- Grid math ----------
    float GridLength => cellsZVisible * cellSize;
    float BaseHalfWidth => (cellsX * cellSize) * 0.5f;

    float HalfWidthAt(float z)
    {
        float t = Mathf.Clamp01(z / Mathf.Max(0.0001f, GridLength));
        float shrink = squeezeAmount * Mathf.Pow(t, squeezeExponent);
        return BaseHalfWidth * (1f - shrink);
    }

    // ---------- Rendering ----------
    void EnsureLineMat()
    {
        if (_lineMat) return;
        var sh = Shader.Find(INTERNAL_MAT);
        _lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _lineMat.SetInt("_ZWrite", 0);
        _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
    }

    int _cx,_cz; float _cs,_sqAmt,_sqPow;
    void BuildGrid(bool force = false)
    {
        if (!force && _gridMesh && _cx==cellsX && _cz==cellsZVisible &&
            Mathf.Approximately(_cs,cellSize) &&
            Mathf.Approximately(_sqAmt,squeezeAmount) &&
            Mathf.Approximately(_sqPow,squeezeExponent)) return;

        _cx=cellsX; _cz=cellsZVisible; _cs=cellSize; _sqAmt=squeezeAmount; _sqPow=squeezeExponent;

        if (_gridMesh == null) _gridMesh = new Mesh { name = "MenuGrid_Lines", hideFlags = HideFlags.HideAndDontSave };
        else _gridMesh.Clear();

        var verts = new List<Vector3>();
        var cols  = new List<Color>();
        var ids   = new List<int>();

        // Horizontal lines (constant z)
        for (int r=0; r<=cellsZVisible; r++)
        {
            float z = r * cellSize;
            float hw = HalfWidthAt(z);
            int vi = verts.Count;
            verts.Add(new Vector3(-hw, 0f, z));
            verts.Add(new Vector3(+hw, 0f, z));

            float t = 1f - (float)r / Mathf.Max(1, cellsZVisible);
            float hue = Mathf.Repeat(0.6f + Time.time * hueScroll, 1f);
            Color c = Color.HSVToRGB(hue, saturation * 0.6f, Mathf.Lerp(value * 0.4f, value, t)) * glow; c.a = 1f;
            cols.Add(c); cols.Add(c);
            ids.Add(vi); ids.Add(vi+1);
        }

        // Vertical lines (constant column) — draw as segments to follow squeeze curve
        for (int cx=0; cx<=cellsX; cx++)
        {
            float nx = ((float)cx / Mathf.Max(1, cellsX)) * 2f - 1f;
            for (int r=0; r<cellsZVisible; r++)
            {
                float z0 = r * cellSize;
                float z1 = (r+1) * cellSize;
                int vi = verts.Count;
                verts.Add(new Vector3(nx * HalfWidthAt(z0), 0f, z0));
                verts.Add(new Vector3(nx * HalfWidthAt(z1), 0f, z1));

                float hue = Mathf.Repeat(((float)cx/Mathf.Max(1,cellsX)) + Time.time * hueScroll, 1f);
                Color c = Color.HSVToRGB(hue, saturation, value) * glow; c.a = 1f;
                cols.Add(c); cols.Add(c);
                ids.Add(vi); ids.Add(vi+1);
            }
        }

        _gridMesh.SetVertices(verts);
        _gridMesh.SetColors(cols);
        _gridMesh.SetIndices(ids, MeshTopology.Lines, 0, true);
        _gridMesh.RecalculateBounds();
    }

    void OnRenderObject()
    {
        if (!_gridMesh || !_lineMat) return;

        Matrix4x4 trs = Matrix4x4.TRS(_gridOrigin, Quaternion.identity, Vector3.one);
        _lineMat.SetPass(0);

        int passes = Mathf.Clamp(Mathf.CeilToInt(lineWidth / 0.01f), 1, 5);
        float step = lineWidth / Mathf.Max(1, passes);

        for (int i=0; i<passes; i++)
        {
            float o = -lineWidth * 0.5f + step * i;
            Graphics.DrawMeshNow(_gridMesh, trs * Matrix4x4.Translate(new Vector3(o,0f,o)));
        }
    }

    // ---------- Vault ----------
    void EnsureVault()
    {
        if (!showVault)
        {
            if (_vault) { if (Application.isPlaying) Destroy(_vault.gameObject); else DestroyImmediate(_vault.gameObject); _vault = null; }
            return;
        }
        if (_vault) return; // don’t double-spawn

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Vault_VanishingPoint";
        var col = sphere.GetComponent<Collider>(); if (col) col.enabled = false;
        var mr = sphere.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
        _vault = sphere.transform;
        _vault.position = new Vector3(0f, 0f, horizonDistance);
        _vault.localScale = Vector3.one * vaultRadius;

        var lightObj = new GameObject("Vault_Light");
        lightObj.transform.SetParent(_vault, false);
        var l = lightObj.AddComponent<Light>();
        l.type = LightType.Point; l.range = vaultLightRange; l.intensity = vaultLightIntensity;
    }

    // ---------- Cartridges ----------
    void RebuildCartridgePool()
    {
        // shrink
        while (_cartPool.Count > cartridgesToShow)
        {
            var t = _cartPool[^1];
            if (t) { if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject); }
            _cartPool.RemoveAt(_cartPool.Count-1);
        }
        // grow
        while (_cartPool.Count < cartridgesToShow && cartridgePrefab)
        {
            var go = Instantiate(cartridgePrefab, transform);
            go.name = $"Cartridge_{_cartPool.Count:00}";
            _cartPool.Add(go.transform);
        }
        // keep slots list same size
        while (_cartSlots.Count > _cartPool.Count) _cartSlots.RemoveAt(_cartSlots.Count-1);
        while (_cartSlots.Count < _cartPool.Count) _cartSlots.Add((0,0,Vector2.zero));
    }

    void AssignCartSlots()
    {
        if (_cartPool.Count == 0) return;

        var used = new HashSet<(int,int)>();
        for (int i=0; i<_cartPool.Count; i++)
        {
            int col, row; int guard = 200;
            do {
                col = Random.Range(0, Mathf.Max(1, cellsX));
                row = Random.Range(cartRowMin, Mathf.Min(cartRowMax, cellsZVisible-1));
            } while (used.Contains((col,row)) && --guard > 0);
            used.Add((col,row));

            // jitter is a fraction of half-tile; bounded so it never leaves the tile
            float jx = Random.Range(-cartJitterFrac, cartJitterFrac);
            float jz = Random.Range(-cartJitterFrac, cartJitterFrac);
            _cartSlots[i] = (col, row, new Vector2(jx, jz));
        }
    }

    void UpdateCartPositions()
    {
        float len = GridLength;

        for (int i=0; i<_cartPool.Count; i++)
        {
            var t = _cartPool[i];
            if (!t) continue;

            var (col, row, jitter) = _cartSlots[i];

            // center of the target tile in grid space
            float zLocal = row * cellSize + 0.5f * cellSize;
            zLocal = Mathf.Clamp(zLocal, 0f, len);

            float halfW = HalfWidthAt(zLocal);
            float tileWidth = (halfW * 2f) / Mathf.Max(1, cellsX);
            float xCenter = -halfW + (col + 0.5f) * tileWidth;

            // jitter *inside* the tile, never exceeding half-tile
            float x = xCenter + (tileWidth * 0.5f) * jitter.x;
            float z = zLocal + (cellSize * 0.5f) * jitter.y;

            Vector3 p = new Vector3(x, cartHeight, z) + _gridOrigin;
            t.position = p;

            // orient
            if (targetCamera)
            {
                Vector3 toCam = targetCamera.transform.position - p;
                if (cartYawOnly) toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-4f)
                {
                    var look = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                    var e = look.eulerAngles;
                    e.x += cartPitch;
                    e.z += cartRoll;
                    t.rotation = Quaternion.Euler(e);
                }
            }
        }
    }

    // ---------- Editor helpers ----------
#if UNITY_EDITOR
    [ContextMenu("Align Horizon To Grid End")]
    void EditorAlignHorizon()
    {
        horizonDistance = cellsZVisible * cellSize + Mathf.Max(0f, horizonGap);
        PoseCamera();
        if (_vault) _vault.position = new Vector3(0f,0f,horizonDistance);
    }

    [ContextMenu("Respawn Cartridge Slots")]
    void EditorRespawnSlots() { AssignCartSlots(); UpdateCartPositions(); }
#endif
}
