using UnityEngine;
using System;

public class BeamerGame : GameManager
{
    const int sw = 160, sh = 192;

    // Player
    float px = 24, py = 24;
    float vy;
    bool alive = true;

    // Beam
    float energy;           // 0..1
    bool beaming;
    float rechargeRate = 0.45f;
    float drainRate    = 0.55f;

    // World lanes
    struct Pad { public float x; public int w; }
    struct Bomb { public float x; }
    Pad[] pads = new Pad[4];
    Bomb[] bombs = new Bomb[6];
    System.Random rng;
    float scroll = 26f;

    public override void Begin()
    {
        rng = new System.Random(7);
        ScoreP1 = 0; alive = true;
        px = 28; py = 26; vy = 0;
        energy = 1f; beaming = false;
        scroll = 26f;

        float x = 40;
        for (int i = 0; i < pads.Length; i++)
        {
            int w = rng.Next(20, 36);
            pads[i] = new Pad { x = x, w = w };
            x += rng.Next(44, 80);
        }
        for (int i = 0; i < bombs.Length; i++)
            bombs[i] = new Bomb { x = rng.Next(60, 260) };

        // clean start sound
        meta.audioBus.BeepOnce(180, 0.05f);
    }

    public override void OnStartMode(){ Begin(); }

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

        // Input
        beaming = energy > 0.02f && BtnA();
        if (beaming) energy = Mathf.Max(0f, energy - drainRate * dt);
        else         energy = Mathf.Min(1f, energy + 0.12f * dt);

        // Apply “anti-grav” while beaming, else we drop
        if (beaming) vy = Mathf.Lerp(vy, 38f, 8f * dt);
        else         vy -= 90f * dt;

        py += vy * dt;
        py = Mathf.Clamp(py, 18f, 120f);

        // World scroll + spawn
        for (int i = 0; i < pads.Length; i++) pads[i].x -= scroll * dt;
        for (int i = 0; i < bombs.Length; i++) bombs[i].x -= scroll * dt;

        if (pads[0].x + pads[0].w * 0.5f < -10f)
        {
            for (int i = 0; i < pads.Length - 1; i++) pads[i] = pads[i + 1];
            var last = pads[pads.Length - 2];
            int w = rng.Next(20, 40);
            float next = last.x + last.w * 0.5f + rng.Next(48, 92) + w * 0.5f;
            pads[^1] = new Pad { x = next, w = w };
        }
        if (bombs[0].x < -12f)
        {
            for (int i = 0; i < bombs.Length - 1; i++) bombs[i] = bombs[i + 1];
            bombs[^1] = new Bomb { x = rng.Next(200, 320) };
        }

        // Recharge when over pad
        bool overPad = false;
        for (int i = 0; i < pads.Length; i++)
        {
            if (Mathf.Abs(px - pads[i].x) < pads[i].w * 0.5f)
            {
                overPad = true;
                energy = Mathf.Min(1f, energy + rechargeRate * dt);
            }
        }

        // Distance score
        ScoreP1 += Mathf.FloorToInt(scroll * dt * 0.5f);

        // Death: touching ground without beam OR hit bomb
        bool grounded = py <= 18f + 0.1f;
        if (grounded && !beaming) alive = false;

        for (int i = 0; i < bombs.Length; i++)
        {
            if (Mathf.Abs(px - bombs[i].x) < 5f && Mathf.Abs(py - 26f) < 12f)
            { alive = false; break; }
        }

        if (!alive) meta.audioBus.BeepOnce(70, 0.12f);

        // Small difficulty ramp
        if (overPad && energy > 0.95f) scroll = Mathf.Min(48f, scroll + 2f * dt);
    }

    void OnGUI()
    {
        if (!Running) return;

        // Background scanlines
        for (int y = 0; y < sh; y += 16)
            RetroDraw.PixelRect(0, y, sw, 1, sw, sh, new Color(0, 0.25f, 0.1f, 0.25f));

        // Ground line + pads
        RetroDraw.PixelRect(0, 18, sw, 2, sw, sh, new Color(0.12f, 0.6f, 0.32f, 1f));
        for (int i = 0; i < pads.Length; i++)
        {
            int x = Mathf.RoundToInt(pads[i].x);
            RetroDraw.PixelRect(x - pads[i].w / 2, 12, pads[i].w, 6, sw, sh, new Color(0.21f, 0.88f, 0.42f, 1f));
        }

        // Bombs
        for (int i = 0; i < bombs.Length; i++)
        {
            int x = Mathf.RoundToInt(bombs[i].x);
            RetroDraw.PixelRect(x - 4, 20, 8, 6, sw, sh, new Color(1f, 0.28f, 0.18f, 1f));
        }

        // Player + beam
        if (beaming)
            RetroDraw.PixelRect(Mathf.RoundToInt(px) - 1, 20, 2, Mathf.RoundToInt(py - 20), sw, sh, new Color(0.5f, 1f, 1f, 1f));

        RetroDraw.PixelRect(Mathf.RoundToInt(px) - 3, Mathf.RoundToInt(py) - 3, 6, 6, sw, sh, new Color(0.7f, 0.9f, 1f, 1f));

        // Energy bar
        RetroDraw.PixelRect(24, sh - 14, sw - 48, 6, sw, sh, new Color(0, 0, 0, 0.5f));
        int ew = Mathf.RoundToInt((sw - 52) * Mathf.Clamp01(energy));
        RetroDraw.PixelRect(26, sh - 13, ew, 4, sw, sh, new Color(0.4f, 1f, 1f, 1f));

        if (!alive)
        {
            RetroDraw.PixelRect(sw/2 - 52, sh/2 - 20, 104, 40, sw, sh, new Color(0, 0, 0, 0.75f));
            RetroDraw.PrintBig(sw/2 - 36, sh/2 - 4, "GAME OVER", sw, sh, Color.white);
            RetroDraw.PrintSmall(sw/2 - 42, sh/2 - 14, "A = RETRY   B = MENU", sw, sh, new Color(0.9f,0.9f,1f,1));
            if (BackPressed()) QuitToMenu();
        }

        DrawCommonHUD(sw, sh);
    }
}
