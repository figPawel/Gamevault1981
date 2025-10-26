using UnityEngine;

public class BeamerGame : GameManager
{
    int sw=160, sh=192;
    float x, y, vy;
    float energy=1f;
    float recharge=0f;
    System.Random rng;
    float[] bombsZ;
    float speed=20f;
    bool beam;
    float padTimer;
    bool alive=true;

    public override void Begin()
    {
        rng = new System.Random(1234);
        bombsZ = new float[8];
        for(int i=0;i<bombsZ.Length;i++) bombsZ[i] = 80f + i*20f;
        x=sw*0.5f; y=30f; vy=0f; energy=1f; recharge=0f; padTimer=0f; ScoreP1=0; alive=true;
    }

    public override void OnStartMode()
    {
        Begin();
    }

    void Update()
    {
        if (!Running) return;
        float dt = Time.deltaTime;
        if (!alive && BtnA()) { Begin(); return; }

        beam = BtnA();
        if (beam && energy>0f) { vy = 60f*dt; energy = Mathf.Max(0f, energy - 0.4f*dt); }
        else { vy -= 90f*dt; }

        y += vy;
        if (y<10f) { alive=false; meta.audioBus.BeepOnce(120,0.12f); }
        if (y>sh-10f) y=sh-10f;

        float ax = AxisH();
        x = Mathf.Clamp(x + ax*80f*dt, 10f, sw-10f);

        for (int i=0;i<bombsZ.Length;i++)
        {
            bombsZ[i] -= speed*dt;
            if (bombsZ[i]<0f)
            {
                bombsZ[i] = 160f + (float)rng.NextDouble()*80f;
                ScoreP1++;
                speed = Mathf.Min(60f, speed + 0.2f);
            }
        }

        bool onPad = Mathf.Abs((y%40f)-20f)<3f;
        if (onPad) energy = Mathf.Min(1f, energy + 0.7f*dt);

        foreach (var bz in bombsZ)
        {
            if (Mathf.Abs(bz- (x))<6f && Mathf.Abs(y-100f)<24f) { alive=false; meta.audioBus.BeepOnce(90,0.15f); break; }
        }
    }

    void OnGUI()
    {
        if (!Running) return;
        Color bg = new Color(0,0,0,1);
        RetroDraw.Rect(new Rect(0,0,1,1), bg);

        RetroDraw.PixelRect(0,0,sw,1,sw,sh,Color.black);

        RetroDraw.PixelRect((int)(x-2),(int)(y-2),4,4,sw,sh, new Color(0.6f,0.9f,1f,1f));
        if (beam && energy>0f) RetroDraw.PixelRect((int)(x-1),0,2,(int)y,sw,sh,new Color(0.2f,0.8f,1f,0.8f));

        for (int i=0;i<bombsZ.Length;i++)
            RetroDraw.PixelRect((int)bombsZ[i]-3,94,6,12,sw,sh,new Color(1f,0.3f,0.3f,1));

        for (int r=20;r<sh;r+=40)
            RetroDraw.PixelRect(0,r-1,sw,2,sw,sh, new Color(0.2f,1f,0.6f,0.6f));

        float w = Mathf.Clamp01(energy);
        RetroDraw.Rect(new Rect(0.05f,0.92f,0.9f,0.03f), new Color(0,0,0,0.6f));
        RetroDraw.Rect(new Rect(0.05f,0.92f,0.9f*w,0.03f), new Color(0.2f,0.9f,0.2f,0.9f));

        if (!alive)
            RetroDraw.Rect(new Rect(0.2f,0.45f,0.6f,0.1f), new Color(0,0,0,0.7f));
    }
}
