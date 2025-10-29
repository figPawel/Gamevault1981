using UnityEngine;
using System;

public class BeamerGame : GameManager
{
    const int sw = 160, sh = 192;

    // World layout
    const int groundY = 20;   // ground baseline
    float scroll = 28f;

    // Player (UFO)
    float px, py;
    float vy;
    bool alive;

    // Beam/energy
    bool  beaming;
    float energy;                  // 0..1
    const float drainRate    = 0.55f;
    const float rechargeRate = 0.45f;
    const float liftTarget   = 42f; // vertical speed target when beaming
    const float gravity      = 90f;

    // ----- scoring -----
    float scoreAcc;                // accumulate fractional points here
    const float scoreRate = 0.6f;  // pts per pixel of ground passed

    // Ground pads (good) + hazards (bad if beam touches)
    struct Pad     { public float x; public int w; }
    struct Hazard  { public float x; public int w; }
    Pad[]    pads    = new Pad[5];
    Hazard[] hazards = new Hazard[4];

    // Air obstacles (jets)
    struct Jet { public float x, y; public float speed; }
    Jet[] jets = new Jet[4];

    System.Random rng;

    public override void Begin()
    {
        rng = new System.Random(1337);
        ScoreP1 = 0;
        scoreAcc = 0f;
        alive   = true;

        // Start left-ish and low
        px = 26; py = 42; vy = 0;
        energy = 1f; beaming = false;
        scroll = 28f;

        // Layout pads & hazards on the ground ahead
        float x = 60f;
        for (int i = 0; i < pads.Length; i++)
        {
            int w = rng.Next(20, 36);
            pads[i] = new Pad { x = x, w = w };
            x += rng.Next(54, 88);
        }

        float hx = 120f;
        for (int i = 0; i < hazards.Length; i++)
        {
            int w = rng.Next(10, 18);
            hazards[i] = new Hazard { x = hx, w = w };
            hx += rng.Next(70, 120);
        }

        for (int i = 0; i < jets.Length; i++)
            jets[i] = NewJet(rng.Next(160, 300));

        if (meta && meta.audioBus) meta.audioBus.BeepOnce(210, 0.05f, 0.09f);
    }

    public override void OnStartMode() { Begin(); }

    Jet NewJet(float startX)
    {
        int band = rng.Next(0, 3); // 0 low, 1 mid, 2 high
        float y = (band == 0) ? 52f : (band == 1 ? 74f : 102f);
        return new Jet { x = startX, y = y, speed = rng.Next(34, 52) };
    }

    void Update()
    {
        if (!Running) return;
        if (HandleCommonPause()) return;

        float dt = Time.deltaTime;

        if (!alive)
        {
            if (BtnADown()) Begin();
            return;
        }

        // ----- Input & energy -----
        beaming = energy > 0.02f && BtnA();
        if (beaming) energy = Mathf.Max(0f, energy - drainRate * dt);
        else         energy = Mathf.Min(1f, energy + 0.12f * dt); // slow passive regen

        // Anti-gravity vs gravity
        if (beaming) vy = Mathf.Lerp(vy, liftTarget, 8f * dt);
        else         vy -= gravity * dt;

        py += vy * dt;
        py = Mathf.Clamp(py, groundY + 6, 120f);

        // ----- Scroll world -----
        float s = scroll * dt;

        for (int i = 0; i < pads.Length; i++)     pads[i].x    -= s;
        for (int i = 0; i < hazards.Length; i++) hazards[i].x -= s;
        for (int i = 0; i < jets.Length; i++)    jets[i].x    -= (jets[i].speed + scroll) * dt;

        // recycle pads
        if (pads[0].x + pads[0].w * 0.5f < -12f)
        {
            for (int i = 0; i < pads.Length - 1; i++) pads[i] = pads[i + 1];
            var last = pads[pads.Length - 2];
            int w = rng.Next(22, 40);
            float next = last.x + last.w * 0.5f + rng.Next(56, 96) + w * 0.5f;
            pads[^1] = new Pad { x = next, w = w };
        }

        // recycle hazards
        if (hazards[0].x + hazards[0].w * 0.5f < -12f)
        {
            for (int i = 0; i < hazards.Length - 1; i++) hazards[i] = hazards[i + 1];
            int w = rng.Next(10, 18);
            float lastX = hazards[hazards.Length - 2].x;
            hazards[^1] = new Hazard { x = lastX + rng.Next(80, 140), w = w };
        }

        // recycle jets
        if (jets[0].x < -20f)
        {
            for (int i = 0; i < jets.Length - 1; i++) jets[i] = jets[i + 1];
            jets[^1] = NewJet(rng.Next(200, 360));
        }

        // ----- Recharge when hovering over pad -----
        bool overPad = false;
        for (int i = 0; i < pads.Length; i++)
        {
            if (Mathf.Abs(px - pads[i].x) < pads[i].w * 0.5f)
            {
                overPad = true;
                energy = Mathf.Min(1f, energy + rechargeRate * dt);
            }
        }

        // ----- Scoring (accumulate fractional points, then add whole numbers) -----
        scoreAcc += scroll * dt * scoreRate;
        int add = (int)scoreAcc;
        if (add > 0)
        {
            ScoreP1 += add;
            scoreAcc -= add;
        }

        if (overPad && energy > 0.95f) scroll = Mathf.Min(54f, scroll + 4f * dt); // gentle ramp

        // ----- Failure conditions -----
        bool grounded = py <= groundY + 6.01f;
        if (grounded && !beaming) alive = false;

        // Beam sweeps a ground hazard
        if (beaming)
        {
            for (int i = 0; i < hazards.Length; i++)
            {
                if (Mathf.Abs(px - hazards[i].x) < hazards[i].w * 0.5f)
                { alive = false; break; }
            }
        }

        // Jet collision (UFO body)
        for (int i = 0; i < jets.Length; i++)
        {
            if (Mathf.Abs(px - jets[i].x) < 7f && Mathf.Abs(py - jets[i].y) < 7f)
            { alive = false; break; }
        }

        if (!alive && meta && meta.audioBus) meta.audioBus.BeepOnce(72, 0.12f, 0.10f);
    }

    void OnGUI()
    {
        if (!Running) return;

        RetroDraw.Begin(sw, sh);
        int vw = RetroDraw.ViewW;
        int vh = RetroDraw.ViewH;

        // ---- Sky & ground (full dynamic width) ----
        RetroDraw.PixelRect(0, 0, vw, vh, sw, sh, new Color(0.18f,0.42f,0.95f,1));        // sky
        RetroDraw.PixelRect(0, 0, vw, groundY, sw, sh, new Color(0.02f,0.14f,0.04f,1));   // ground fill
        RetroDraw.PixelRect(0, groundY, vw, 4, sw, sh, new Color(0.05f,0.28f,0.08f,1));   // ground line

        // clouds (wrap across full dynamic width)
        float t = Time.time * 10f;
        Cloud(( 10 + t*0.8f) % (vw + 50) - 25, 150, 28, 7, 0.5f);
        Cloud((100 + t*0.6f) % (vw + 70) - 35, 168, 34, 8, 0.4f);
        Cloud((170 + t*1.0f) % (vw + 60) - 30, 132, 24, 6, 0.6f);

        // ---- Pads (good)
        for (int i = 0; i < pads.Length; i++)
        {
            int x = Mathf.RoundToInt(pads[i].x);
            if (x + pads[i].w < -20 || x - pads[i].w > vw + 20) continue;
            RetroDraw.PixelRect(x - pads[i].w/2, groundY - 2, pads[i].w, 2, sw, sh, new Color(0.10f,0.95f,0.55f,1));
            RetroDraw.PixelRect(x - pads[i].w/2, groundY - 4, pads[i].w, 2, sw, sh, new Color(0.06f,0.65f,0.40f,1));
        }

        // ---- Hazards (bad if beam touches)
        for (int i = 0; i < hazards.Length; i++)
        {
            int x = Mathf.RoundToInt(hazards[i].x);
            if (x + hazards[i].w < -20 || x - hazards[i].w > vw + 20) continue;
            RetroDraw.PixelRect(x - hazards[i].w/2, groundY - 3, hazards[i].w, 3, sw, sh, new Color(0.92f,0.25f,0.20f,1));
            RetroDraw.PixelRect(x - hazards[i].w/2, groundY - 6, hazards[i].w, 3, sw, sh, new Color(0.45f,0.06f,0.04f,1));
        }

        // ---- Jets
        for (int i = 0; i < jets.Length; i++)
        {
            int x = Mathf.RoundToInt(jets[i].x);
            int y = Mathf.RoundToInt(jets[i].y);
            if (x < -20 || x > vw + 20) continue;
            RetroDraw.PixelRect(x - 5, y - 2, 10, 2, sw, sh, new Color(0.85f,0.85f,0.85f,1));
            RetroDraw.PixelRect(x - 2, y,     4, 2,  sw, sh, new Color(0.25f,0.25f,0.30f,1));
        }

        // ---- Beam then UFO
        if (beaming)
        {
            int top = Mathf.RoundToInt(py - 3);
            int h   = top - groundY;
            if (h > 0)
            {
                RetroDraw.PixelRect(Mathf.RoundToInt(px) - 3, groundY, 6, h, sw, sh, new Color(0.55f,1f,1f,0.25f));
                RetroDraw.PixelRect(Mathf.RoundToInt(px) - 1, groundY, 2, h, sw, sh, new Color(0.8f,1f,1f,0.8f));
            }
        }

        int ux = Mathf.RoundToInt(px);
        int uy = Mathf.RoundToInt(py);
        RetroDraw.PixelRect(ux - 8, uy - 3, 16, 3, sw, sh, new Color(0.72f,0.86f,1f,1));
        RetroDraw.PixelRect(ux - 6, uy,     12, 2, sw, sh, new Color(0.50f,0.70f,1f,1));
        RetroDraw.PixelRect(ux - 3, uy + 2,  6, 3, sw, sh, new Color(1f,0.95f,0.7f,1));
        RetroDraw.PixelRect(ux - 1, uy + 5,  2, 1, sw, sh, new Color(1f,1f,0.85f,1));

        // Energy bar uses dynamic height/width
        RetroDraw.PixelRect(24, vh - 14, vw - 48, 6, sw, sh, new Color(0,0,0,0.55f));
        int ew = Mathf.RoundToInt((vw - 52) * Mathf.Clamp01(energy));
        RetroDraw.PixelRect(26, vh - 13, ew, 4, sw, sh, new Color(0.4f,1f,1f,1));

        // Game over (centered)
        if (!alive)
        {
            RetroDraw.PixelRect(vw/2 - 52, sh/2 - 20, 104, 40, sw, sh, new Color(0,0,0,0.75f));
            RetroDraw.PrintBig  (vw/2 - 36, sh/2 - 4, "GAME OVER", sw, sh, Color.white);
            RetroDraw.PrintSmall(vw/2 - 44, sh/2 - 14, "A: RETRY   Back: MENU", sw, sh, new Color(0.9f,0.9f,1f,1));
 
        }

        DrawCommonHUD(sw, sh);
    }

    void Cloud(float wx, int wy, int w, int h, float parallax)
    {
        int sx = Mathf.RoundToInt(wx);
        RetroDraw.PixelRect(sx, wy, w, h, sw, sh, new Color(1,1,1,0.85f));
        RetroDraw.PixelRect(sx + 3, wy + h, Mathf.Max(2, w - 6), 2, sw, sh, new Color(1,1,1,0.65f));
    }
}
