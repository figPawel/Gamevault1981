using UnityEngine;

public static class RetroDraw
{
    static Material m;
    static void Ensure() { if (m) return; m = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave }; m.SetInt("_ZWrite",0); m.SetInt("_Cull",0); m.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
    public static void Rect(Rect r, Color c)
    {
        Ensure(); m.SetPass(0);
        GL.PushMatrix(); GL.LoadOrtho(); GL.Begin(GL.QUADS); GL.Color(c);
        GL.Vertex3(r.xMin, r.yMin, 0); GL.Vertex3(r.xMax, r.yMin, 0); GL.Vertex3(r.xMax, r.yMax, 0); GL.Vertex3(r.xMin, r.yMax, 0);
        GL.End(); GL.PopMatrix();
    }
    public static void Line(Vector2 a, Vector2 b, Color c, float w)
    {
        Ensure(); m.SetPass(0);
        Vector2 n=(b-a).normalized; Vector2 t=new Vector2(-n.y,n.x)*w*0.5f;
        GL.PushMatrix(); GL.LoadOrtho(); GL.Begin(GL.QUADS); GL.Color(c);
        GL.Vertex(a-t); GL.Vertex(a+t); GL.Vertex(b+t); GL.Vertex(b-t);
        GL.End(); GL.PopMatrix();
    }
    public static void Frame(Rect r, Color c, float w)
    {
        Rect(new Rect(r.xMin,r.yMin,r.width,w),c);
        Rect(new Rect(r.xMin,r.yMax-w,r.width,w),c);
        Rect(new Rect(r.xMin,r.yMin,w,r.height),c);
        Rect(new Rect(r.xMax-w,r.yMin,w,r.height),c);
    }
    public static void PixelRect(int x,int y,int w,int h, int sw,int sh, Color c)
    {
        float rx=(float)x/sw, ry=(float)y/sh, rw=(float)w/sw, rh=(float)h/sh;
        Rect(new Rect(rx,ry,rw,rh),c);
    }
}
