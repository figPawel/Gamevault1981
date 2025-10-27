using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class LaserTangoGame : GameManager
{
    // “2600-ish” logical height; width expands with aspect
    const int sw = 160, sh = 192;

    // Helpers to keep logic in sync with our RetroDraw expand-view
    int ViewW => Mathf.Max(1, Mathf.RoundToInt(sh * ((float)Screen.width / Mathf.Max(1, Screen.height))));

    // Player aim (Y you place the scanline)
    float aimY = sh * 0.5f;
    float aimSpeed = 110f;

    // Laser sweep state
    bool  firing;
    float laserY;
    float laserX;
    int   laserDir = +1;     // toggles each shot: +1 left→right, -1 right→left
    float laserSpeed = 520f; // visual sweep speed (cosmetic)
    float fireCooldown;      // small lockout between shots
    const float minCooldown = 0.12f;
    float comboFlash;        // UI feedback timer
    int   lastCombo;

    // Targets (good) and Bombs (bad)
    struct Target { public float x, y; public bool alive; }
    struct Bomb   { public float x, y; public bool alive; }
    Target[] targets;
    Bomb[]   bombs;

    System.Random rng;

    // Spawn cadence
    float spawnT_Target;
    float spawnT_Bomb;

    public override void Begin()
    {

          Running = true;

        rng = new System.Random(Environment.TickCount);
        ScoreP1 = 0;

        int vw = ViewW;

        // Start aim mid-air
        aimY = Mathf.Clamp(sh * 0.55f, 24f, sh - 24f);

        // Pools
        targets = new Target[40];
        bombs   = new Bomb[8];
        for (int i = 0; i < targets.Length; i++) targets[i].alive = false;
        for (int i = 0; i < bombs.Length;   i++) bombs[i].alive   = false;

        // Seed a few things
        for (int i = 0; i < 12; i++) SpawnTarget(vw);
        for (int i = 0; i <  3; i++) SpawnBomb(vw);

        firing = false; fireCooldown = 0f; comboFlash = 0f; lastCombo = 0;
        laserDir = +1;  // first sweep goes left→right
    }

    public override void OnStartMode() { Begin(); }

    // ---------- Input helpers (axis) ----------
    float GetYMove()
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current;
        float v = 0f;
        if (k != null)
        {
            if (k.wKey.isPressed || k.upArrowKey.isPressed) v += 1f;
            if (k.sKey.isPressed || k.downArrowKey.isPressed) v -= 1f;
        }
        if (g != null) v += g.leftStick.ReadValue().y;
        return Mathf.Clamp(v, -1f, 1f);
    #else
        float v = Input.GetAxisRaw("Vertical");
        if (v == 0f)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
        }
        return Mathf.Clamp(v, -1f, 1f);
    #endif
    }

    void Update()
    {
        if (!Running) return;
        if (HandleCommonPause()) return;

        float dt = Time.deltaTime;
        int vw = ViewW;

        // Move the aim marker
        aimY += GetYMove() * aimSpeed * dt;
        aimY = Mathf.Clamp(aimY, 22f, sh - 22f);

        // Spawn trickle
        spawnT_Target -= dt;
        spawnT_Bomb   -= dt;
        if (spawnT_Target <= 0f) { SpawnTarget(vw); spawnT_Target = Mathf.Lerp(0.10f, 0.35f, (float)rng.NextDouble()); }
        if (spawnT_Bomb   <= 0f) { if (rng.NextDouble() < 0.55) SpawnBomb(vw);      spawnT_Bomb = 1.2f + (float)rng.NextDouble()*0.8f; }

        // Parallax / drift for targets (subtle)
        DriftStuff(dt, vw);

        // Laser firing logic
        fireCooldown = Mathf.Max(0f, fireCooldown - dt);
        comboFlash   = Mathf.Max(0f, comboFlash - dt);

        if (!firing && fireCooldown <= 0f && BtnADown())
        {
            FireLaser(vw);
        }

        if (firing)
        {
            laserX += laserDir * laserSpeed * dt;
            if ((laserDir > 0 && laserX > vw + 8) || (laserDir < 0 && laserX < -8))
            {
                firing = false;
                fireCooldown = minCooldown;
                laserDir = -laserDir; // bounce direction next time
            }
        }
    }

    void FireLaser(int vw)
    {
        firing = true;
        laserY = aimY;
        laserX = (laserDir > 0) ? -6f : (vw + 6f);

        // Hit window (vertical band)
        const float band = 3f;

        // Count hits + mark dead
        int hits = 0;
        for (int i = 0; i < targets.Length; i++)
        {
            if (!targets[i].alive) continue;
            if (Mathf.Abs(targets[i].y - laserY) <= band) { targets[i].alive = false; hits++; }
        }

        // Bomb check → instant death
        bool bombed = false;
        for (int i = 0; i < bombs.Length; i++)
        {
            if (!bombs[i].alive) continue;
            if (Mathf.Abs(bombs[i].y - laserY) <= band) { bombed = true; break; }
        }

        if (bombed)
        {
            // boom
            Running = false;
            if (meta && meta.audioBus) meta.audioBus.BeepOnce(90f, 0.14f, 0.12f);
        }
        else
        {
            // Score = combo^2 (reward multi-hits)
            lastCombo = hits;
            if (hits > 0)
            {
                ScoreP1 += hits * hits;
                comboFlash = 0.65f;
                // rising chirps
                if (meta && meta.audioBus)
                {
                    float f = 300f;
                    for (int k = 0; k < Mathf.Min(4, hits); k++)
                        meta.audioBus.BeepOnce(f + 70f * k, 0.04f, 0.06f);
                }
            }
            else
            {
                // dry shot click
                if (meta && meta.audioBus) meta.audioBus.BeepOnce(220f, 0.03f, 0.04f);
            }
        }
    }

    void DriftStuff(float dt, int vw)
    {
        // mild float for targets
        for (int i = 0; i < targets.Length; i++)
        {
            if (!targets[i].alive) continue;
            targets[i].x -= 12f * dt; // gentle left drift to cycle the field
            targets[i].y += Mathf.Sin((targets[i].x + i * 13f) * 0.06f) * 6f * dt;
            if (targets[i].x < -8f) { targets[i].x = vw + 6f; targets[i].y = RandY(); }
        }
        // bombs crawl too (slower, a little menacing)
        for (int i = 0; i < bombs.Length; i++)
        {
            if (!bombs[i].alive) continue;
            bombs[i].x -= 26f * dt;
            if (bombs[i].x < -10f) { bombs[i].x = vw + 10f; bombs[i].y = RandY(); }
        }
    }

    void SpawnTarget(int vw)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (!targets[i].alive)
            {
                targets[i].alive = true;
                targets[i].x = vw + (float)rng.NextDouble() * 30f;
                targets[i].y = RandY();
                return;
            }
        }
    }

    void SpawnBomb(int vw)
    {
        for (int i = 0; i < bombs.Length; i++)
        {
            if (!bombs[i].alive)
            {
                bombs[i].alive = true;
                bombs[i].x = vw + (float)rng.NextDouble() * 60f;
                bombs[i].y = RandY();
                return;
            }
        }
    }

    float RandY() => Mathf.Lerp(26f, sh - 26f, (float)rng.NextDouble());

    void OnGUI()
    {
        if (!Running && !Paused) { /* dead → show card, but still draw world */ }

        RetroDraw.Begin(sw, sh);
        int vw = ViewW;
        int vh = RetroDraw.ViewH;

        // Background
        RetroDraw.PixelRect(0, 0, vw, vh, sw, sh, new Color(0.06f, 0.06f, 0.08f, 1f));

        // Particles (good)
        for (int i = 0; i < targets.Length; i++)
        {
            if (!targets[i].alive) continue;
            int x = Mathf.RoundToInt(targets[i].x);
            int y = Mathf.RoundToInt(targets[i].y);
            RetroDraw.PixelRect(x - 1, y - 1, 2, 2, sw, sh, new Color(0.45f, 1f, 0.95f, 1f));
        }

        // Bombs (bad)
        for (int i = 0; i < bombs.Length; i++)
        {
            if (!bombs[i].alive) continue;
            int x = Mathf.RoundToInt(bombs[i].x);
            int y = Mathf.RoundToInt(bombs[i].y);
            RetroDraw.PixelRect(x - 2, y - 2, 4, 4, sw, sh, new Color(1f, 0.2f, 0.2f, 1f));
            RetroDraw.PixelRect(x - 3, y - 1, 6, 2, sw, sh, new Color(0.35f, 0.0f, 0.0f, 1f));
        }

        // Aim marker (follows aimY)
        RetroDraw.PixelRect(8, Mathf.RoundToInt(aimY) - 1, vw - 16, 1, sw, sh, new Color(0f, 0.3f, 0.6f, 0.35f));
        RetroDraw.PixelRect(8, Mathf.RoundToInt(aimY),     vw - 16, 1, sw, sh, new Color(0.2f, 0.8f, 1f, 0.5f));

        // Laser sweep visual
        if (firing)
        {
            int y = Mathf.RoundToInt(laserY);
            int x = Mathf.RoundToInt(laserX);
            // bright core + soft wing
            RetroDraw.PixelRect(x - 8, y - 1, 16, 3, sw, sh, new Color(0.9f, 1f, 1f, 0.85f));
            RetroDraw.PixelRect(x - 16, y - 2, 32, 5, sw, sh, new Color(0.6f, 1f, 1f, 0.25f));
        }

        // Combo toast
        if (comboFlash > 0f && lastCombo > 1)
        {
            float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(comboFlash / 0.65f));
            RetroDraw.PixelRect(vw/2 - 28, vh - 30, 56, 10, sw, sh, new Color(0,0,0,0.35f * a));
            RetroDraw.PrintSmall(vw/2 - 24, vh - 22, $"COMBO x{lastCombo}", sw, sh, new Color(0.8f,1f,1f, a));
        }

        // Game over card
        if (!Running && !Paused)
        {
            RetroDraw.PixelRect(vw/2 - 54, sh/2 - 20, 108, 40, sw, sh, new Color(0, 0, 0, 0.78f));
            RetroDraw.PrintBig  (vw/2 - 38, sh/2 - 4, "BOOM!", sw, sh, Color.white);
            RetroDraw.PrintSmall(vw/2 - 52, sh/2 - 14, "A: RETRY    Back: MENU", sw, sh, new Color(0.9f,0.9f,1f,1));
            if (BtnADown()) Begin();
            if (BackPressed()) QuitToMenu();
        }

        // Shared HUD (score + pause)
        DrawCommonHUD(sw, sh);
    }
}
