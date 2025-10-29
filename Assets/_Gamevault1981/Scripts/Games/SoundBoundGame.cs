using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SoundBoundGame : GameManager
{
    const int sw = 160, sh = 192;

    // Five discrete tones (0..4): low→high
    const int TONES = 5;
    int   toneIndex = 2;    // current tone
    float tonePos   = 2f;   // smooth position we steer
    float toneSpeed = 6.0f; // tones per second with full stick/keys

    // Singing state (hold A)
    bool  singing;
    float beepTimer;        // faux continuous tone via short beeps

    // Falling shapes
    struct Note {
        public int type; public float x, y; public float speed; public bool alive; public float shake;
    }
    Note[] notes = new Note[36];

    // Tiny pop effect
    struct Pop { public float x, y, t; public Color c; public bool alive; }
    Pop[] pops = new Pop[32];

    // Spawning & difficulty
    float spawnTimer;
    float baseFall = 28f;
    float diff; // 0..1 over time
    System.Random rng;

    // Rules
    const int groundY = 26;
    int lives = 3;
    int streak = 0;
    float lastPopAt = -999f;

    // Dynamic view
    int ViewW => Mathf.Max(1, Mathf.RoundToInt(sh * ((float)Screen.width / Mathf.Max(1, Screen.height))));
    int ViewH => Mathf.Max(1, RetroDraw.ViewH);

    public override void Begin()
    {
        Running  = true;
        ScoreP1  = 0;
        lives    = 3;
        streak   = 0;
        lastPopAt= -999f;
        diff     = 0f;
        beepTimer= 0f;
        singing  = false;

        tonePos   = toneIndex = 2;

        rng = new System.Random(Environment.TickCount);
        for (int i=0;i<notes.Length;i++) notes[i].alive = false;
        for (int i=0;i<pops.Length;i++)  pops[i].alive  = false;

        spawnTimer = 0.55f;

        if (meta && meta.audioBus) meta.audioBus.BeepOnce(220, 0.05f, 0.06f);
    }
    public override void OnStartMode() { Begin(); }

    // -------- Robust left/right (works with New, Legacy, gamepad) ----------
    float ReadLR()
    {
        float v = 0f;

        #if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current; var g = Gamepad.current;
        if (k != null)
        {
            if (k.leftArrowKey.isPressed || k.aKey.isPressed || k.jKey.isPressed)  v -= 1f;
            if (k.rightArrowKey.isPressed || k.dKey.isPressed || k.lKey.isPressed) v += 1f;
        }
        if (g != null)
        {
            v += g.leftStick.ReadValue().x;
            if (g.dpad.left.isPressed)  v -= 1f;
            if (g.dpad.right.isPressed) v += 1f;
        }
        #endif

        #if ENABLE_LEGACY_INPUT_MANAGER
        v += Input.GetAxisRaw("Horizontal");
        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.J)) v -= 1f;
        if (Input.GetKey(KeyCode.RightArrow)|| Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.L)) v += 1f;
        #endif

        return Mathf.Clamp(v, -1f, 1f);
    }

    void Update()
    {
        if (!Running) return;
        if (HandleCommonPause()) return;

        float dt = Time.deltaTime;
        int vw = ViewW;
        int vh = ViewH;

        // Slide tone; snap to nearest discrete tone each frame
        tonePos   = Mathf.Clamp(tonePos + ReadLR() * toneSpeed * dt, 0f, TONES-1);
        toneIndex = Mathf.RoundToInt(tonePos);

        // Singing (hold A)
        singing = BtnA();

        // Faux continuous tone via periodic beeps while singing
        if (singing)
        {
            beepTimer -= dt;
            if (beepTimer <= 0f && meta && meta.audioBus)
            {
                meta.audioBus.BeepOnce(ToneFreq(toneIndex), 0.05f, 0.07f);
                beepTimer = 0.11f;
            }
        }
        else beepTimer = 0f;

        // Streak soft decay
        if (Time.time - lastPopAt > 1.0f) streak = 0;

        // Spawn & ramp
        spawnTimer -= dt;
        if (spawnTimer <= 0f)
        {
            SpawnNote(vw, vh);
            spawnTimer = Mathf.Lerp(0.22f, 0.60f, 0.65f - Mathf.Clamp01(diff));
        }
        diff = Mathf.Min(1f, diff + 0.018f * dt);

        // Fall & match
        for (int i=0;i<notes.Length;i++)
        {
            if (!notes[i].alive) continue;

            notes[i].y -= (baseFall + notes[i].speed * (0.6f + diff)) * dt;

            if (singing && notes[i].type == toneIndex)
            {
                notes[i].shake += 3.5f * dt; // ~0.25s to pop
                if (notes[i].shake >= 1f)
                {
                    PopAt(notes[i].x, notes[i].y, ToneColor(notes[i].type));
                    notes[i].alive = false;

                    streak++;
                    lastPopAt = Time.time;
                    ScoreP1 += 10 + 2 * streak;

                    if (meta && meta.audioBus)
                        meta.audioBus.BeepOnce(420 + 14 * streak, 0.035f, 0.05f);

                    continue;
                }
            }
            else notes[i].shake = Mathf.Max(0f, notes[i].shake - 2.0f * dt);

            // Ground collision => lose life
            if (notes[i].y <= groundY)
            {
                notes[i].alive = false;
                streak = 0;
                if (--lives <= 0)
                {
                    Running = false;
                    if (meta && meta.audioBus) meta.audioBus.BeepOnce(110, 0.10f, 0.09f);
                    break;
                }
                else if (meta && meta.audioBus) meta.audioBus.BeepOnce(140, 0.06f, 0.07f);
            }
        }

        // Pops
        for (int i=0;i<pops.Length;i++)
        {
            if (!pops[i].alive) continue;
            pops[i].t -= dt;
            if (pops[i].t <= 0f) pops[i].alive = false;
        }
    }

    void SpawnNote(int vw, int vh)
    {
        int idx = -1;
        for (int i=0;i<notes.Length;i++) if (!notes[i].alive){ idx = i; break; }
        if (idx < 0) return;

        int type = rng.Next(0, TONES);
        float x = 12f + (float)rng.NextDouble() * (vw - 24f);
        float y = vh - 6f;

        notes[idx].alive = true;
        notes[idx].type  = type;
        notes[idx].x     = x;
        notes[idx].y     = y;
        notes[idx].speed = rng.Next(14, 36);
        notes[idx].shake = 0f;
    }

    // --- Drawing -------------------------------------------------------------
    void OnGUI()
    {
        RetroDraw.Begin(sw, sh);
        int vw = ViewW;
        int vh = ViewH;

        // Gradient sky
        for (int y=0; y<vh; y+=2)
        {
            float t = (float)y / Mathf.Max(1, vh);
            var c = Color.Lerp(new Color(0.07f,0.09f,0.20f,1), new Color(0.16f,0.25f,0.50f,1), t);
            RetroDraw.PixelRect(0, y, vw, 2, sw, sh, c);
        }

        // Ground line
        RetroDraw.PixelRect(0, groundY, vw, 2, sw, sh, new Color(0.18f,0.45f,0.24f,1));

        // Notes
        for (int i=0;i<notes.Length;i++)
        {
            if (!notes[i].alive) continue;
            int nx = Mathf.RoundToInt(notes[i].x);
            int ny = Mathf.RoundToInt(notes[i].y);
            Color c = ToneColor(notes[i].type);

            float j = notes[i].shake > 0f ? Mathf.Sin(Time.time * 50f) * 1.5f * notes[i].shake : 0f;
            int jx = Mathf.RoundToInt(j);
            int jy = Mathf.RoundToInt(-j * 0.5f);

            if (notes[i].shake > 0f)
                RetroDraw.PixelRect(nx - 5, ny - 5, 10, 10, sw, sh, new Color(c.r, c.g, c.b, 0.10f + 0.15f * notes[i].shake));

            DrawShape(notes[i].type, nx + jx, ny + jy, c);
        }

        // Pops
        for (int i=0;i<pops.Length;i++)
        {
            if (!pops[i].alive) continue;
            float a = Mathf.Clamp01(pops[i].t / 0.25f);
            int r = Mathf.RoundToInt(6 * (1f - a));
            Color c = new Color(pops[i].c.r, pops[i].c.g, pops[i].c.b, 0.25f + 0.55f * a);
            RetroDraw.PixelRect(Mathf.RoundToInt(pops[i].x) - r, Mathf.RoundToInt(pops[i].y) - r, r*2, 1, sw, sh, c);
            RetroDraw.PixelRect(Mathf.RoundToInt(pops[i].x) - 1, Mathf.RoundToInt(pops[i].y) - r, 2, r*2, sw, sh, c);
            RetroDraw.PixelStar(Mathf.RoundToInt(pops[i].x), Mathf.RoundToInt(pops[i].y), sw, sh, new Color(pops[i].c.r, pops[i].c.g, pops[i].c.b, 0.8f*a));
        }

        // Singer
        int sx = vw/2, sy = groundY + 6;
        bool openMouth = singing;
        RetroDraw.PixelRect(sx - 6, sy + 0, 12, 6, sw, sh, new Color(0.25f,0.70f,0.95f,1));
        RetroDraw.PixelRect(sx - 4, sy + 6,  8, 4, sw, sh, new Color(0.36f,0.86f,1f,1));
        RetroDraw.PixelRect(sx - 3, sy + 10, 6, 4, sw, sh, new Color(1f,0.92f,0.82f,1));
        RetroDraw.PixelRect(sx - 1, sy + 11 + (openMouth?0:1), 2, openMouth?2:1, sw, sh, new Color(0.6f,0.1f,0.1f,1));
        var tc = ToneColor(toneIndex);
        RetroDraw.PixelRect(sx - 3, sy + 15, 6, 1, sw, sh, tc);
        RetroDraw.PixelRect(sx - 2, sy + 16, 4, 1, sw, sh, tc);

        // Shared HUD (draws SCORE + pause card)
        DrawCommonHUD(sw, sh);
        // Add our extras to the right of the SCORE box (no overlap)
        RetroDraw.PrintSmall(90, vh - 10, $"LIVES {lives}   TONE {toneIndex+1}/5", sw, sh, Color.white);

        if (!Running && !Paused)
        {
            RetroDraw.PixelRect(vw/2 - 60, sh/2 - 20, 120, 40, sw, sh, new Color(0,0,0,0.78f));
            RetroDraw.PrintBig  (vw/2 - 40, sh/2 - 4, "GAME OVER", sw, sh, Color.white);
            RetroDraw.PrintSmall(vw/2 - 56, sh/2 - 14, "A: RETRY    Back: MENU", sw, sh, new Color(0.9f,0.9f,1f,1));
            if (BtnADown()) Begin();
       
        }
    }

    // --- helpers ---
    void DrawShape(int type, int x, int y, Color c)
    {
        switch (type)
        {
            case 0: // diamond
                RetroDraw.PixelRect(x - 1, y + 2, 2, 2, sw, sh, c);
                RetroDraw.PixelRect(x - 2, y + 1, 4, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 3, y + 0, 6, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 2, y - 1, 4, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 1, y - 2, 2, 1, sw, sh, c);
                break;
            case 1: // triangle
                RetroDraw.PixelRect(x - 0, y + 2, 1, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 1, y + 1, 3, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 2, y + 0, 5, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 3, y - 1, 7, 1, sw, sh, c);
                break;
            case 2: // circle-ish
                RetroDraw.PixelRect(x - 1, y + 2, 2, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 2, y + 1, 4, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 2, y + 0, 4, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 2, y - 1, 4, 1, sw, sh, c);
                RetroDraw.PixelRect(x - 1, y - 2, 2, 1, sw, sh, c);
                break;
            case 3: // square
                RetroDraw.PixelRect(x - 2, y - 2, 5, 5, sw, sh, c);
                break;
            default: // star
                RetroDraw.PixelStar(x, y, sw, sh, c);
                break;
        }
    }

    Color ToneColor(int i)
    {
        return i switch
        {
            0 => new Color(1.00f,0.55f,0.45f,1), // coral
            1 => new Color(1.00f,0.86f,0.40f,1), // amber
            2 => new Color(0.60f,0.95f,0.50f,1), // green
            3 => new Color(0.50f,0.80f,1.00f,1), // blue
            _ => new Color(0.90f,0.70f,1.00f,1), // violet
        };
    }

    float ToneFreq(int idx)
    {
        switch (Mathf.Clamp(idx,0,4))
        {
            case 0: return 220f; // A3
            case 1: return 262f; // C4
            case 2: return 294f; // D4
            case 3: return 330f; // E4
            default:return 392f; // G4
        }
    }

    void PopAt(float x, float y, Color c)
    {
        for (int i=0;i<pops.Length;i++)
        {
            if (!pops[i].alive)
            {
                pops[i].alive = true;
                pops[i].x = x; pops[i].y = y;
                pops[i].t = 0.25f;
                pops[i].c = c;
                return;
            }
        }
    }
}
