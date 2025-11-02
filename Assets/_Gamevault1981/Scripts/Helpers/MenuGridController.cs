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
    [Range(0.1f, 5f)] public float squeezeExponent = 2f;

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

    // ─────────────────────────────────────────────────────────────────────────────
    // MUSIC VIS + DIAGNOSTICS (INTEGRATED)
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Music Viz")]
    public bool reactToMusic = true;

    [Tooltip("Overall multiplier for audio reactivity. Boost this if waves are too small.")]
    public float audioGain = 8f; // ★ NEW: Added for sensitivity control

    [Range(256, 8192)] public int fftSize = 1024;
    public FFTWindow fftWindow = FFTWindow.BlackmanHarris;

    [Range(0.0f, 0.99f)] public float levelSmoothing = 0.80f;
    [Range(0.0f, 0.99f)] public float bandSmoothing = 0.85f;
    [Range(1.0f, 3.0f)] public float beatSensitivity = 1.35f;
    [Range(0.05f, 0.50f)] public float minBeatInterval = 0.12f;
    [Range(0.5f, 5f)] public float pulseDecayHz = 2.2f;

    [Tooltip("Max vertical displacement of the lines (world units).")]
    public float waveAmplitude = 20f;
    [Range(0f, 2f)] public float waveFalloffZ = 0.75f;
    public float waveFreqX = 0.90f;
    public float waveFreqZ = 0.25f;
    public float waveScroll = 1.30f;
    [Range(0f, 1f)] public float speedPulse = 0.30f;
    [Range(0f, 1f)] public float bandBalance = 0.35f; // 0=bass, 1=mid

    [Header("Debug / Preview / Logs")]
    public bool debugWave = false;              // force deformation
    [Range(0f, 1f)] public float debugAmount = 0.8f;
    public bool deformInEditMode = false;
    public bool diagnostics = true;             // print logs
    [Range(0.1f, 2f)] public float logEverySeconds = 0.5f;

    // Inspector readouts
    [SerializeField] float _level, _levelSmooth, _bass, _mid, _pulse;
    [SerializeField] bool _heardAudioLastFrame;

    float[] _fft;
    float _beatEnv, _lastBeatTime, _nextLogTime;

    // ─────────────────────────────────────────────────────────────────────────────
    // INTERNALS
    // ─────────────────────────────────────────────────────────────────────────────
    Mesh _gridMesh;
    Material _lineMat;
    Vector3 _gridOrigin;
    float _scroll;
    Transform _vault;

    Vector3[] _baseVerts, _deformVerts;

    // NEW: dense rows so waves across X are visible
    [Header("Tessellation")]
    [Tooltip("Extra segments per row across X to allow visible curvature.")]
    [Range(4, 128)] public int rowSubdivX = 32;

    const string INTERNAL_MAT = "Hidden/Internal-Colored";
    const string VAULT_NAME = "Vault_VanishingPoint";

    // ─────────────────────────────────────────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────────
    void OnEnable()
    {
        if (!targetCamera) targetCamera = Camera.main;
        PoseCamera();
        AlignHorizonIfWanted();
        BuildGrid(force: true);
        EnsureLineMat();
        EnsureVault();
        EnsureFftBuffer();
        _nextLogTime = Time.unscaledTime + 1f;
        if (diagnostics) Debug.Log("[MenuGrid] enabled.");
    }

    void OnValidate()
    {
        cellsX = Mathf.Max(2, cellsX);
        cellsZVisible = Mathf.Max(8, cellsZVisible);
        cellSize = Mathf.Max(0.05f, cellSize);
        lineWidth = Mathf.Clamp(lineWidth, 0.001f, 0.2f);
        rowSubdivX = Mathf.Clamp(rowSubdivX, 4, 128);

        fftSize = Mathf.Clamp(fftSize, 64, 8192);
        int coerced = Mathf.NextPowerOfTwo(fftSize);
        if (coerced != fftSize) fftSize = (coerced > 8192) ? 8192 : coerced;
        EnsureFftBuffer();

        PoseCamera();
        AlignHorizonIfWanted();
        BuildGrid();
        EnsureVault();
    }

    void Update()
    {
        if (!Application.isPlaying) PoseCamera();

        // 1) AUDIO INGEST
        float beat = 0f, amp = 0f;
        if (reactToMusic)
        {
            SampleSpectrum(out float level, out float bass, out float mid, out float pulse);
            _level = level; _bass = bass; _mid = mid; _pulse = pulse;

            // ★ MODIFIED: Use audioGain to boost the signal
            amp = Mathf.Clamp01(Mathf.Lerp(bass, mid, bandBalance) * audioGain + _levelSmooth * (audioGain * 0.25f));
            beat = pulse;
        }
        if (debugWave) amp = Mathf.Max(amp, debugAmount);

        // 2) SCROLL
        float speedMul = 1f + speedPulse * (beat * 1.0f + amp * 0.35f);
        _scroll += (scrollSpeed * speedMul) * Time.deltaTime;
        _gridOrigin.z = -Mathf.Repeat(_scroll, cellSize);

        // 3) DEFORM
        bool shouldDeform =
            _gridMesh &&
            (Application.isPlaying || deformInEditMode) &&
            (reactToMusic || debugWave);

        float maxDisp = 0f;
        if (shouldDeform) maxDisp = ApplyWaveDeform(amp, beat);

        // 4) VAULT
        if (showVault && _vault)
        {
            _vault.position = new Vector3(0f, 0f, horizonDistance);
            float s = vaultRadius * (1f + 0.15f * Mathf.Sin(Time.time * vaultPulse * 2f + beat * 2f));
            _vault.localScale = Vector3.one * s;
        }

        // 5) LOGGING (movement + audio)
        if (diagnostics && Time.unscaledTime >= _nextLogTime)
        {
            _nextLogTime = Time.unscaledTime + Mathf.Max(0.1f, logEverySeconds);
            Debug.Log($"[MenuGrid] audio={(reactToMusic ? (_heardAudioLastFrame ? "YES" : "no") : "off")} " +
                        $"lvl={_levelSmooth:F3} bass={_bass:F3} mid={_mid:F3} pulse={_pulse:F3} " +
                        $"amp={amp:F3} beat={beat:F3} verts={(_deformVerts != null ? _deformVerts.Length : 0)} disp={maxDisp:F3}");
        }
    }

    void OnDisable() { Cleanup(); }
    void OnDestroy() { Cleanup(); }

    // ─────────────────────────────────────────────────────────────────────────────
    // AUDIO / SPECTRUM
    // ─────────────────────────────────────────────────────────────────────────────
    void EnsureFftBuffer()
    {
        if (_fft == null || _fft.Length != fftSize) _fft = new float[fftSize];
    }

    void SampleSpectrum(out float level, out float bassOut, out float midOut, out float pulseOut)
    {
        level = 0f; bassOut = 0f; midOut = 0f; pulseOut = 0f;
        EnsureFftBuffer();

        var listeners = FindObjectsOfType<AudioListener>(true);
        if (listeners == null || listeners.Length == 0)
        {
            if (diagnostics) Debug.LogWarning("[MenuGrid] No AudioListener found.");
            _heardAudioLastFrame = false;
            return;
        }
        if (listeners.Length > 1 && diagnostics)
            Debug.LogWarning($"[MenuGrid] Multiple AudioListeners found ({listeners.Length}).");

        try { AudioListener.GetSpectrumData(_fft, 0, fftWindow); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MenuGrid] GetSpectrumData failed ({e.Message}).");
            _heardAudioLastFrame = false; return;
        }

        int n = _fft.Length; if (n <= 8) { _heardAudioLastFrame = false; return; }

        int bBassEnd = Mathf.Clamp(Mathf.RoundToInt(n * 110f / 22050f), 1, n - 1);
        int bLowMidEnd = Mathf.Clamp(Mathf.RoundToInt(n * 400f / 22050f), bBassEnd + 1, n - 1);
        int bMidEnd = Mathf.Clamp(Mathf.RoundToInt(n * 1600f / 22050f), bLowMidEnd + 1, n - 1);

        float eBass = 0, eLowMid = 0, eMid = 0, eAll = 0;
        Sum(0, bBassEnd, ref eBass);
        Sum(bBassEnd, bLowMidEnd, ref eLowMid);
        Sum(bLowMidEnd, bMidEnd, ref eMid);
        eAll = eBass + eLowMid + eMid;

        float overall = Mathf.Sqrt(Mathf.Max(1e-8f, eAll));
        _levelSmooth = Mathf.Lerp(_levelSmooth, overall, 1f - levelSmoothing);
        level = overall;

        float a = 1f - bandSmoothing;
        _bass = Mathf.Lerp(_bass, Mathf.Sqrt(eBass), a);
        _mid = Mathf.Lerp(_mid, Mathf.Sqrt(eMid), a);
        bassOut = _bass; midOut = _mid;

        _beatEnv = Mathf.Lerp(_beatEnv, _bass, 0.08f);
        float threshold = _beatEnv * beatSensitivity;
        bool canBeat = (Time.unscaledTime - _lastBeatTime) >= minBeatInterval;

        if (canBeat && _bass > threshold) { _pulse = 1f; _lastBeatTime = Time.unscaledTime; }
        else { _pulse *= Mathf.Exp(-pulseDecayHz * Time.unscaledDeltaTime); }
        pulseOut = _pulse;

        _heardAudioLastFrame = overall > 1e-5f;
    }

    void Sum(int from, int to, ref float acc)
    {
        float s = 0f;
        for (int i = from; i < to; i++) { float v = _fft[i]; s += v * v; }
        acc = s / Mathf.Max(1, to - from);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // GRID
    // ─────────────────────────────────────────────────────────────────────────────
    void Cleanup()
    {
        if (_gridMesh) { if (Application.isPlaying) Destroy(_gridMesh); else DestroyImmediate(_gridMesh); _gridMesh = null; }
        if (_lineMat) { if (Application.isPlaying) Destroy(_lineMat); else DestroyImmediate(_lineMat); _lineMat = null; }
        _baseVerts = null; _deformVerts = null;

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
            Mathf.Approximately(_sqPow, squeezeExponent) &&
            _baseVerts != null) return;

        _cx = cellsX; _cz = cellsZVisible; _cs = cellSize; _sqAmt = squeezeAmount; _sqPow = squeezeExponent;

        if (_gridMesh == null)
        {
            _gridMesh = new Mesh { name = "MenuGrid_Lines", hideFlags = HideFlags.HideAndDontSave };
            _gridMesh.MarkDynamic();
        }
        else _gridMesh.Clear();

        var verts = new List<Vector3>();
        var cols = new List<Color>();
        var idx = new List<int>();

        // ROWS (polylines across X with subdivisions)
        for (int r = 0; r <= cellsZVisible; r++)
        {
            float z = r * cellSize;
            float hw = HalfWidthAt(z);

            int start = verts.Count;
            int segments = Mathf.Max(1, rowSubdivX);
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                float x = Mathf.Lerp(-hw, hw, t);
                verts.Add(new Vector3(x, 0f, z));

                float v = 1f - (float)r / Mathf.Max(1, cellsZVisible);
                float hue = Mathf.Repeat(0.6f + Time.time * hueScroll, 1f);
                Color c = Color.HSVToRGB(hue, saturation * 0.6f, Mathf.Lerp(value * 0.4f, value, v)) * glow; c.a = 1f;
                cols.Add(c);

                if (s > 0) { idx.Add(start + s - 1); idx.Add(start + s); }
            }
        }

        // COLUMNS (as before; plenty of segments along Z)
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
                idx.Add(vi); idx.Add(vi + 1);
            }
        }

        _gridMesh.SetVertices(verts);
        _gridMesh.SetColors(cols);
        _gridMesh.SetIndices(idx, MeshTopology.Lines, 0, true);
        _gridMesh.RecalculateBounds();

        _baseVerts = verts.ToArray();
        _deformVerts = new Vector3[_baseVerts.Length];
        System.Array.Copy(_baseVerts, _deformVerts, _baseVerts.Length);
    }

    float ApplyWaveDeform(float amp, float beat)
    {
        if (_baseVerts == null || _deformVerts == null || _deformVerts.Length != _baseVerts.Length) return 0f;

        float music = Mathf.Clamp01(amp * 1.1f + beat * 0.4f);

        float t =
#if UNITY_EDITOR
            (Application.isPlaying ? Time.timeSinceLevelLoad : (float)UnityEditor.EditorApplication.timeSinceStartup);
#else
            Time.timeSinceLevelLoad;
#endif
        float kx = waveFreqX * 2f * Mathf.PI / Mathf.Max(0.0001f, BaseHalfWidth * 2f);
        float kz = waveFreqZ * 2f * Mathf.PI / Mathf.Max(0.0001f, GridLength);
        float baseAmp = waveAmplitude * Mathf.Max(music, debugWave ? debugAmount : 0f);

        float maxDisp = 0f;
        for (int i = 0; i < _baseVerts.Length; i++)
        {
            var v = _baseVerts[i];
            float z01 = Mathf.Clamp01(v.z / Mathf.Max(0.0001f, GridLength));
            float fall = Mathf.Pow(1f - z01, waveFalloffZ);
            float phase = v.x * kx + v.z * kz + (t * waveScroll);
            v.y = Mathf.Sin(phase) * baseAmp * fall;
            _deformVerts[i] = v;
            float ad = Mathf.Abs(v.y); if (ad > maxDisp) maxDisp = ad;
        }

        _gridMesh.SetVertices(_deformVerts);
        return maxDisp;
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