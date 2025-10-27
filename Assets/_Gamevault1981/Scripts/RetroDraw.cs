using UnityEngine;

public static class RetroDraw
{
    static Material m;

    // GUI text cache
    static GUIStyle small, big;
    static bool stylesReady;

    // Dynamic logical view
    static int _baseW = 160, _baseH = 192;
    static int _viewW = 160, _viewH = 192;
    public  static int ViewW => _viewW;
    public  static int ViewH => _viewH;

    static void Ensure()
    {
        if (!m)
        {
            m = new Material(Shader.Find("Hidden/Internal-Colored"))
            { hideFlags = HideFlags.HideAndDontSave };
            m.SetInt("_ZWrite", 0);
            m.SetInt("_Cull", 0);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (!stylesReady)
        {
            small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                richText = false
            };
            big = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                clipping = TextClipping.Overflow,
                richText = false,
                fontStyle = FontStyle.Bold
            };
            stylesReady = true;
        }
    }

    /// Call at top of each OnGUI
    public static void Begin(int sw, int sh)
    {
        Ensure();
        _baseW = Mathf.Max(1, sw);
        _baseH = Mathf.Max(1, sh);

        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        _viewH = _baseH;
        _viewW = Mathf.Max(1, Mathf.RoundToInt(_viewH * aspect));
    }

    public static void Rect(Rect r, Color c)
    {
        Ensure(); m.SetPass(0);
        GL.PushMatrix(); GL.LoadOrtho();
        GL.Begin(GL.QUADS); GL.Color(c);
        GL.Vertex3(r.xMin, r.yMin, 0);
        GL.Vertex3(r.xMax, r.yMin, 0);
        GL.Vertex3(r.xMax, r.yMax, 0);
        GL.Vertex3(r.xMin, r.yMax, 0);
        GL.End();
        GL.PopMatrix();
    }

    public static void Line(Vector2 a, Vector2 b, Color c, float w)
    {
        Ensure(); m.SetPass(0);
        Vector2 n = (b - a).normalized;
        Vector2 t = new Vector2(-n.y, n.x) * w * 0.5f;

        GL.PushMatrix(); GL.LoadOrtho();
        GL.Begin(GL.QUADS); GL.Color(c);
        GL.Vertex(a - t); GL.Vertex(a + t); GL.Vertex(b + t); GL.Vertex(b - t);
        GL.End();
        GL.PopMatrix();
    }

    public static void Frame(Rect r, Color c, float w)
    {
        Rect(new Rect(r.xMin, r.yMin, r.width, w), c);
        Rect(new Rect(r.xMin, r.yMax - w, r.width, w), c);
        Rect(new Rect(r.xMin, r.yMin, w, r.height), c);
        Rect(new Rect(r.xMax - w, r.yMin, w, r.height), c);
    }

    // Pixel-space rect in dynamic logical view
    public static void PixelRect(int x, int y, int w, int h, int sw, int sh, Color c)
    {
        float rx = (float)x / _viewW;
        float ry = (float)y / _viewH;
        float rw = (float)w / _viewW;
        float rh = (float)h / _viewH;
        Rect(new Rect(rx, ry, rw, rh), c);
    }

    // --- Text helpers ---
    static void Print(string text, int x, int y, int sw, int sh, Color color, GUIStyle style)
    {
        Ensure();
        var prevColor  = GUI.color;
        var prevMatrix = GUI.matrix;

        float sx = Screen.width  / Mathf.Max(1f, _viewW);
        float sy = Screen.height / Mathf.Max(1f, _viewH);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(sx, sy, 1f));
        GUI.color  = color;

        int h = Mathf.CeilToInt(style.lineHeight);
        var r = new Rect(x, (_viewH - y) - h, _viewW - x, h + 2);
        GUI.Label(r, text, style);

        GUI.color  = prevColor;
        GUI.matrix = prevMatrix;
    }

    public static void PrintSmall(int x, int y, string text, int sw, int sh, Color color)
    { Print(text, x, y, sw, sh, color, small); }

    public static void PrintBig(int x, int y, string text, int sw, int sh, Color color)
    { Print(text, x, y, sw, sh, color, big); }

    // --- Small shape helper used by SoundBound ---
    public static void PixelStar(int x, int y, int sw, int sh, Color c)
    {
        // simple 5-point pixel star shape ~7x5 footprint
        PixelRect(x - 0, y + 2, 1, 1, sw, sh, c);
        PixelRect(x - 2, y + 1, 5, 1, sw, sh, c);
        PixelRect(x - 1, y + 0, 3, 1, sw, sh, c);
        PixelRect(x - 2, y - 1, 5, 1, sw, sh, c);
        PixelRect(x - 0, y - 2, 1, 1, sw, sh, c);
    }
}
