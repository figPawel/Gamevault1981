using UnityEngine;

public static class RetroDraw
{
    static Material m;

    // GUI text cache
    static GUIStyle small, big;
    static bool stylesReady;

    static void Ensure()
    {
        if (!m)
        {
            m = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
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

    // Pixel-space rect using a logical resolution (sw x sh), origin bottom-left.
    public static void PixelRect(int x, int y, int w, int h, int sw, int sh, Color c)
    {
        float rx = (float)x / sw, ry = (float)y / sh, rw = (float)w / sw, rh = (float)h / sh;
        Rect(new Rect(rx, ry, rw, rh), c);
    }

    // --- Pixel-space text helpers ------------------------------------------

    // Draws text at logical pixel coordinates (x,y) with origin at bottom-left.
    // We scale GUI.matrix so 1 unit == 1 logical pixel in sw x sh space.
    static void Print(string text, int x, int y, int sw, int sh, Color color, GUIStyle style)
    {
        Ensure();

        // Save/replace GUI state
        var prevColor  = GUI.color;
        var prevMatrix = GUI.matrix;

        float sx = Screen.width  / (float)sw;
        float sy = Screen.height / (float)sh;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(sx, sy, 1f));
        GUI.color  = color;

        // GUI coordinates are top-left origin; convert from bottom-left.
        // Give a generous width so we don't wrap.
        int h = Mathf.CeilToInt(style.lineHeight);
        var r = new Rect(x, (sh - y) - h, sw - x, h + 2);
        GUI.Label(r, text, style);

        // Restore GUI state
        GUI.color  = prevColor;
        GUI.matrix = prevMatrix;
    }

    public static void PrintSmall(int x, int y, string text, int sw, int sh, Color color)
    {
        Print(text, x, y, sw, sh, color, small);
    }

    public static void PrintBig(int x, int y, string text, int sw, int sh, Color color)
    {
        Print(text, x, y, sw, sh, color, big);
    }
}
