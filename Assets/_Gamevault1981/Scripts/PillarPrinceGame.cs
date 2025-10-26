using UnityEngine;

public class PillarPrinceGame : GameManager
{
    int sw=160, sh=192;
    float px, py;
    float charge;
    float speed=24f;
    System.Random rng;
    float[] gaps = new float[6];
    int current=0;
    bool jumping;
    float vx;
    bool alive=true;

    public override void Begin()
    {
        rng = new System.Random(42);
        px=20; py=70; charge=0; speed=24f; for(int i=0;i<gaps.Length;i++) gaps[i]=20+ i*25;
        current=0; jumping=false; vx=0; ScoreP1=0; alive=true;
    }

    public override void OnStartMode() { Begin(); }

    void Update()
    {
        if (!Running) return;
        float dt=Time.deltaTime;

        if (!alive && BtnA()) { Begin(); return; }

        for(int i=0;i<gaps.Length;i++) gaps[i]-=speed*dt;
        if (gaps[0]<-20f)
        {
            for(int i=0;i<gaps.Length-1;i++) gaps[i]=gaps[i+1];
            gaps[^1] = gaps[^2] + 25 + (float)rng.NextDouble()*20f;
            speed = Mathf.Min(60f, speed + 0.25f);
            ScoreP1++;
        }

        if (!jumping)
        {
            if (BtnA()) charge = Mathf.Min(1f, charge + 0.8f*dt);
            if (!BtnA() && charge>0f)
            {
                vx = Mathf.Lerp(40f, 140f, charge);
                jumping=true; charge=0f; meta.audioBus.BeepOnce(220,0.08f);
            }
        }
        else
        {
            px += vx*dt; vx = Mathf.Lerp(vx,0f,4f*dt);
            if (vx<5f) jumping=false;
        }

        float gapFront = gaps[0]+10f;
        if (px>gapFront && px<gapFront+20f && !jumping) { alive=false; meta.audioBus.BeepOnce(80,0.15f); }
        px = Mathf.Repeat(px, sw);
    }

    void OnGUI()
    {
        if (!Running) return;
        RetroDraw.Rect(new Rect(0,0,1,1), Color.black);

        for(int i=0;i<gaps.Length;i++)
        {
            float x=gaps[i];
            RetroDraw.PixelRect((int)x-50,60,40,12,sw,sh,new Color(0.2f,0.9f,0.7f,1));
            RetroDraw.PixelRect((int)x+10,60,40,12,sw,sh,new Color(0.2f,0.9f,0.7f,1));
        }

        RetroDraw.PixelRect((int)px-3,(int)py-6,6,12,sw,sh,new Color(1,1,0.3f,1));

        RetroDraw.Rect(new Rect(0.2f,0.9f,0.6f,0.03f), new Color(0,0,0,0.5f));
        if (!jumping) RetroDraw.Rect(new Rect(0.2f,0.9f,0.6f*charge,0.03f), new Color(1f,0.5f,0.2f,1));

        if (!alive) RetroDraw.Rect(new Rect(0.2f,0.45f,0.6f,0.1f), new Color(0,0,0,0.7f));
    }
}
