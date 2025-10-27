using UnityEngine;

public class PillarPrinceGame : GameManager
{
    // Logical “Atari-ish” resolution
    const int sw = 160, sh = 192;

    // Pillars
    struct Pillar { public float x; public int w; }
    Pillar[] pillars = new Pillar[6];

    // Player state
    float px, py;             // player center (y is fixed)
    bool  grounded, alive;

    // Dash mechanics
    float charge;             // 0..1 while holding A
    float dashTarget;         // pixels we intend to travel for this dash
    float dashLeft;           // pixels remaining in current dash
    float dashSpeed = 140f;   // pixels/sec while dashing
    bool  dashing;

    // UX
    int   onIndex;            // which pillar we're standing on
    bool  eatAUntilReleased;  // prevents “held A” from auto-firing after retry
    float legAnim;            // wiggle legs while dashing

    System.Random rng;

    public override void Begin()
    {
        rng = new System.Random(42);

        // Lay pillars out to the right; nothing scrolls, you just dash between them.
        float x = 28;
        for (int i = 0; i < pillars.Length; i++)
        {
            int w = rng.Next(16, 28);
            pillars[i] = new Pillar { x = x, w = w };
            x += w + rng.Next(22, 52); // variable gap
        }

        onIndex  = 0;
        px       = pillars[0].x;
        py       = 92;
        grounded = true;
        alive    = true;
        dashing  = false;
        charge   = 0;
        dashLeft = 0;
        ScoreP1  = 0;
        legAnim  = 0f;

        // If A is held when we spawn, wait until it’s released before accepting a press.
        eatAUntilReleased = BtnA();
    }

    public override void OnStartMode() { Begin(); }

    void Update()
    {
        if (!Running) return;
        if (HandleCommonPause()) return; // Esc/P pauses. Backspace (or Select) quits from pause.

        float dt = Time.deltaTime;

        // Dead → Retry (A), or Back to menu (Backspace/Select)
        if (!alive)
        {
            if (BtnADown()) Begin();
            if (BackPressed()) QuitToMenu();
            return;
        }

        // Clear the post-retry input eat once A is released
        if (eatAUntilReleased && !BtnA()) eatAUntilReleased = false;

        // Charge only while grounded and not eating input
        if (grounded && !dashing && !eatAUntilReleased)
        {
            if (BtnA())
            {
                // snappy fill with a soft cap near 1
                charge = Mathf.Clamp01(charge + 1.25f * dt);
            }
            else if (charge > 0f) // released → start dash
            {
                dashTarget = Mathf.Lerp(18f, 100f, charge); // distance is the entire game
                dashLeft   = dashTarget;
                dashing    = true;
                grounded   = false;
                charge     = 0f;
                meta.audioBus.BeepOnce(420, 0.06f);
            }
        }

        // Dash movement: simple constant-speed horizontal travel
        if (dashing)
        {
            float step = Mathf.Min(dashSpeed * dt, dashLeft);
            px       += step;
            dashLeft -= step;
            legAnim  += step * 0.25f; // wiggle legs

            if (dashLeft <= 0.0001f)
            {
                dashing = false;

                // Check if we stopped on top of any pillar
                int landed = -1;
                for (int i = 0; i < pillars.Length; i++)
                {
                    float half = pillars[i].w * 0.5f;
                    if (px >= pillars[i].x - half && px <= pillars[i].x + half)
                    { landed = i; break; }
                }

                if (landed >= 0)
                {
                    if (landed != onIndex)
                    {
                        onIndex = landed;
                        ScoreP1++;
                        meta.audioBus.BeepOnce(620, 0.05f);
                    }

                    grounded = true;

                    // Keep extending course so there’s always a next pillar
                    if (onIndex >= pillars.Length - 2)
                    {
                        for (int i = 0; i < pillars.Length - 1; i++)
                            pillars[i] = pillars[i + 1];

                        var last = pillars[pillars.Length - 2];
                        int w = rng.Next(16, 28);
                        float nextX = last.x + last.w + rng.Next(22, 52) + w;
                        pillars[^1] = new Pillar { x = nextX, w = w };
                    }
                }
                else
                {
                    alive = false;
                    meta.audioBus.BeepOnce(90, 0.12f);
                }
            }
        }
    }

    void OnGUI()
    {
        if (!Running) return;

        // Background stripes (simple vibe)
        RetroDraw.PixelRect(0,   0, sw, sh,  sw, sh, new Color(0.06f,0.06f,0.08f,1));
        for (int y = 0; y < sh; y += 8) RetroDraw.PixelRect(0, y, sw, 1, sw, sh, new Color(0,0,0,0.25f));
        RetroDraw.PixelRect(0,  88, sw,  2, sw, sh, new Color(0.34f,0.53f,0.25f,1));
        RetroDraw.PixelRect(0,  92, sw,  2, sw, sh, new Color(0.15f,0.35f,0.18f,1));

        // Pillars (top + shadow)
        for (int i = 0; i < pillars.Length; i++)
        {
            int x = Mathf.RoundToInt(pillars[i].x);
            int w = pillars[i].w;
            RetroDraw.PixelRect(x - w/2, 80, w, 12, sw, sh, new Color(0.16f,0.72f,0.55f,1));
            RetroDraw.PixelRect(x - w/2, 76, w,  4, sw, sh, new Color(0.05f,0.36f,0.28f,1));
        }

        // Player: tiny pitfall-y human (head, torso, swinging legs, arm)
        int ix = Mathf.RoundToInt(px), iy = Mathf.RoundToInt(py);
        // torso
        RetroDraw.PixelRect(ix - 2, iy - 8, 4, 6, sw, sh, new Color(0.99f,0.84f,0.28f,1));
        // head
        RetroDraw.PixelRect(ix - 2, iy - 12, 4, 4, sw, sh, new Color(0.98f,0.93f,0.83f,1));
        // arm (forward when dashing)
        int arm = dashing ? 1 : 0;
        RetroDraw.PixelRect(ix + 2 + arm, iy - 7, 2, 2, sw, sh, new Color(0.98f,0.93f,0.83f,1));
        // legs swing
        int swing = dashing ? (int)(Mathf.Sin(legAnim) * 2f) : 0;
        RetroDraw.PixelRect(ix - 3 - swing, iy - 14, 2, 4, sw, sh, new Color(0.18f,0.75f,0.40f,1));
        RetroDraw.PixelRect(ix + 1 + swing, iy - 14, 2, 4, sw, sh, new Color(0.18f,0.75f,0.40f,1));

        // Charge meter with tick marks
        RetroDraw.PixelRect(20, 10, sw - 40, 6, sw, sh, new Color(0,0,0,0.55f));
        int fill = Mathf.RoundToInt((sw - 44) * (grounded && !dashing ? charge : 0));
        RetroDraw.PixelRect(22, 11, fill, 4, sw, sh, new Color(1f,0.56f,0.22f,1));
        // ticks at 25/50/75%
        for (int i = 1; i <= 3; i++)
        {
            int tx = 22 + (sw - 44) * i / 4;
            RetroDraw.PixelRect(tx,  10, 1, 6, sw, sh, new Color(0,0,0,0.35f));
        }

        // Game over card
        if (!alive)
        {
            RetroDraw.PixelRect(sw/2 - 50, sh/2 - 18, 100, 36, sw, sh, new Color(0,0,0,0.80f));
            RetroDraw.PrintBig  (sw/2 - 30, sh/2 - 4, "YOU FELL", sw, sh, Color.white);
            RetroDraw.PrintSmall(sw/2 - 46, sh/2 - 14, "A: RETRY   Back: MENU", sw, sh, new Color(0.85f,0.9f,1f,1));
        }

        // Shared HUD (Score + Pause overlay)
        DrawCommonHUD(sw, sh);
    }
}
