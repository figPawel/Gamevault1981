using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class MenuGridController : MonoBehaviour
{
    [Header("Camera / Horizon")]
    public Camera targetCamera;
    public float cameraHeight = 6f;
    [Range(5f, 60f)] public float cameraTilt = 22f;

    [Tooltip("Distance to the vanishing point along +Z. This is a free slider unless Auto Align is ON.")]
    public float horizonDistance = 120f;

    [Tooltip("If ON, horizonDistance is forced to the far edge of the grid + gap.")]
    public bool autoAlignHorizon = false;
    public float horizonGap = 2f;

    [Header("Grid")]
    public int cellsX = 24;
    public int cellsZVisible = 64;
    public float cellSize = 1f;
    public float scrollSpeed = 3.5f;

    [Tooltip("Line thickness approximation (drawn with multi-pass).")]
    public float lineWidth = 0.02f;

    [Header("Grid Squeeze")]
    [Range(0f, 1f)] public float squeezeAmount = 0.45f;
    [Range(0.5f, 5f)] public float squeezeExponent = 2f;

    [Header("Color")]
    public float hueScroll = 0.08f;
    [Range(0f, 1f)] public float saturation = 0.8f;
    [Range(0f, 1.5f)] public float value = 1.0f;
    [Range(0.2f, 4f)] public float glow = 1.35f;

    [Header("Vault (Vanishing Point)")]
    public bool showVault = true;
    public float vaultRadius = 0.9f;
    public float vaultPulse = 1.5f;
    public float vaultLightRange = 18f;
    public float vaultLightIntensity = 3f;

    Mesh _gridMesh;
    Material _lineMat;
    Vector3 _gridOrigin;
    float _scroll;
    Transform _vault;

    const string INTERNAL_MAT = "Hidden/Internal-Colored";
    const string VAULT_NAME = "Vault_VanishingPoint";

    void OnEnable()
    {
        if (!targetCamera) targetCamera = Camera.main;
        PoseCamera();
        AlignHorizonIfWanted();
        BuildGrid(force: true);
        EnsureLineMat();
        EnsureVault();
    }

    void OnValidate()
    {
        cellsX = Mathf.Max(2, cellsX);
        cellsZVisible = Mathf.Max(8, cellsZVisible);
        cellSize = Mathf.Max(0.05f, cellSize);
        lineWidth = Mathf.Clamp(lineWidth, 0.001f, 0.2f);

        PoseCamera();
        AlignHorizonIfWanted();
        BuildGrid();
        EnsureVault();
    }

    void Update()
    {
        if (!Application.isPlaying) PoseCamera();

        _scroll += scrollSpeed * Time.deltaTime;
        _gridOrigin.z = -Mathf.Repeat(_scroll, cellSize);

        if (showVault && _vault)
        {
            _vault.position = new Vector3(0f, 0f, horizonDistance);
            float s = vaultRadius * (1f + 0.15f * Mathf.Sin(Time.time * vaultPulse * 2f));
            _vault.localScale = Vector3.one * s;
        }
    }

    void OnDisable() { Cleanup(); }
    void OnDestroy() { Cleanup(); }

    void Cleanup()
    {
        if (_gridMesh) { if (Application.isPlaying) Destroy(_gridMesh); else DestroyImmediate(_gridMesh); _gridMesh = null; }
        if (_lineMat)  { if (Application.isPlaying) Destroy(_lineMat);  else DestroyImmediate(_lineMat);  _lineMat  = null; }

        if (_vault)
        {
            if (Application.isPlaying) Destroy(_vault.gameObject);
            else DestroyImmediate(_vault.gameObject);
            _vault = null;
        }

        var orphan = transform.Find(VAULT_NAME);
        if (orphan)
        {
            if (Application.isPlaying) Destroy(orphan.gameObject);
            else DestroyImmediate(orphan.gameObject);
        }
    }

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

    float GridLength => cellsZVisible * cellSize;
    float BaseHalfWidth => (cellsX * cellSize) * 0.5f;

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    float HalfWidthAt(float z)
    {
        float t = Mathf.Clamp01(z / Mathf.Max(0.0001f, GridLength));
        float u = Smooth01(t);
        float shrink = squeezeAmount * Mathf.Pow(u, squeezeExponent);
        return BaseHalfWidth * (1f - shrink);
    }

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

    int _cx, _cz; float _cs, _sqAmt, _sqPow;
    void BuildGrid(bool force = false)
    {
        if (!force && _gridMesh && _cx == cellsX && _cz == cellsZVisible &&
            Mathf.Approximately(_cs, cellSize) &&
            Mathf.Approximately(_sqAmt, squeezeAmount) &&
            Mathf.Approximately(_sqPow, squeezeExponent)) return;

        _cx = cellsX; _cz = cellsZVisible; _cs = cellSize; _sqAmt = squeezeAmount; _sqPow = squeezeExponent;

        if (_gridMesh == null) _gridMesh = new Mesh { name = "MenuGrid_Lines", hideFlags = HideFlags.HideAndDontSave };
        else _gridMesh.Clear();

        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var ids = new List<int>();

        for (int r = 0; r <= cellsZVisible; r++)
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
            ids.Add(vi); ids.Add(vi + 1);
        }

        for (int cx = 0; cx <= cellsX; cx++)
        {
            float nx = ((float)cx / Mathf.Max(1, cellsX)) * 2f - 1f;
            for (int r = 0; r < cellsZVisible; r++)
            {
                float z0 = r * cellSize;
                float z1 = (r + 1) * cellSize;
                int vi = verts.Count;
                verts.Add(new Vector3(nx * HalfWidthAt(z0), 0f, z0));
                verts.Add(new Vector3(nx * HalfWidthAt(z1), 0f, z1));

                float hue = Mathf.Repeat(((float)cx / Mathf.Max(1, cellsX)) + Time.time * hueScroll, 1f);
                Color c = Color.HSVToRGB(hue, saturation, value) * glow; c.a = 1f;
                cols.Add(c); cols.Add(c);
                ids.Add(vi); ids.Add(vi + 1);
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

        for (int i = 0; i < passes; i++)
        {
            float o = -lineWidth * 0.5f + step * i;
            Graphics.DrawMeshNow(_gridMesh, trs * Matrix4x4.Translate(new Vector3(o, 0f, o)));
        }
    }

    void EnsureVault()
    {
        if (!showVault)
        {
            if (_vault)
            {
                if (Application.isPlaying) Destroy(_vault.gameObject);
                else DestroyImmediate(_vault.gameObject);
                _vault = null;
            }
            var extra = transform.Find(VAULT_NAME);
            if (extra)
            {
                if (Application.isPlaying) Destroy(extra.gameObject);
                else DestroyImmediate(extra.gameObject);
            }
            return;
        }

        if (_vault == null)
        {
            var found = transform.Find(VAULT_NAME);
            if (found) _vault = found;
        }

        if (_vault == null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = VAULT_NAME;
            sphere.transform.SetParent(transform, false);
            var col = sphere.GetComponent<Collider>(); if (col) col.enabled = false;
            var mr = sphere.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.white };
            _vault = sphere.transform;
        }

        _vault.position = new Vector3(0f, 0f, horizonDistance);
        _vault.localScale = Vector3.one * vaultRadius;

        Light l = null;
        var lights = _vault.GetComponentsInChildren<Light>(true);
        foreach (var li in lights)
        {
            if (li.transform.parent == _vault && l == null) l = li;
            else
            {
                if (Application.isPlaying) Destroy(li.gameObject);
                else DestroyImmediate(li.gameObject);
            }
        }
        if (l == null)
        {
            var lightObj = new GameObject("Vault_Light");
            lightObj.transform.SetParent(_vault, false);
            l = lightObj.AddComponent<Light>();
        }
        l.type = LightType.Point;
        l.range = vaultLightRange;
        l.intensity = vaultLightIntensity;
    }

#if UNITY_EDITOR
    [ContextMenu("Align Horizon To Grid End")]
    void EditorAlignHorizon()
    {
        horizonDistance = cellsZVisible * cellSize + Mathf.Max(0f, horizonGap);
        PoseCamera();
        if (_vault) _vault.position = new Vector3(0f, 0f, horizonDistance);
    }
#endif
}
