using UnityEngine;

public class PillarPrinceGame : GameManager
{
    const int sw = 160, sh = 192;

    // World layout
    const float platformY = 92f; // y of pillar caps
    const int   capH      = 4;   // cap thickness
    float camX;

    struct Pillar { public float x; public int w; }
    Pillar[] pillars = new Pillar[7];
    System.Random rng;

    // Player FEET position sits ON the cap (so he stands on top)
    float px, py;
    bool grounded, alive, dashing;

    // Dash
    float charge, dashLeft;
    const float dashSpeed = 140f;
    int onIndex;
    bool eatAUntilReleased;
    float legAnim;

    public override void Begin()
    {
        rng = new System.Random(1981);

        // Lay pillars to the right
        float x = 28f;
        for (int i = 0; i < pillars.Length; i++)
        {
            int w = rng.Next(16, 30);
            pillars[i] = new Pillar { x = x, w = w };
            x += w + rng.Next(22, 56);
        }

        onIndex  = 0;
        px       = pillars[0].x;
        py       = platformY + capH; // FEET on cap
        grounded = true; alive = true; dashing = false;
        charge   = 0f; dashLeft = 0f; legAnim = 0f;
        ScoreP1  = 0;
        camX     = 0f;

        eatAUntilReleased = BtnA(); // avoid auto-dash if A was held when you died
    }

    public override void OnStartMode() { Begin(); }

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

        if (eatAUntilReleased && !BtnA()) eatAUntilReleased = false;

        // Charge → Dash
        if (grounded && !dashing && !eatAUntilReleased)
        {
            if (BtnA())
            {
                charge = Mathf.Clamp01(charge + 1.25f * dt);
            }
            else if (charge > 0f) // release starts dash
            {
                dashLeft = Mathf.Lerp(18f, 110f, charge); // distance tuning
                dashing  = true;
                grounded = false;
                onIndex  = -1;
                charge   = 0f;

                if (meta && meta.audioBus)
                {
                    meta.audioBus.BeepOnce(380f, 0.05f, 0.08f);
                    meta.audioBus.BeepOnce(520f, 0.05f, 0.07f);
                }
            }
        }

        // Dash motion
        if (dashing)
        {
            float step = Mathf.Min(dashSpeed * dt, dashLeft);
            px += step;
            dashLeft -= step;
            legAnim += step * 0.25f;

            if (dashLeft <= 0.0001f)
            {
                dashing = false;

                // Landing check
                int landed = -1;
                for (int i = 0; i < pillars.Length; i++)
                {
                    float half = pillars[i].w * 0.5f;
                    if (px >= pillars[i].x - half && px <= pillars[i].x + half)
                    { landed = i; break; }
                }

                if (landed >= 0)
                {
                    grounded = true;
                    if (landed != onIndex)
                    {
                        onIndex = landed;
                        ScoreP1++;
                        if (meta && meta.audioBus) meta.audioBus.BeepOnce(660f, 0.06f, 0.09f);
                    }

                    // Extend runway as we approach the end
                    if (onIndex >= pillars.Length - 2)
                    {
                        for (int i = 0; i < pillars.Length - 1; i++)
                            pillars[i] = pillars[i + 1];

                        var last = pillars[pillars.Length - 2];
                        int w = rng.Next(16, 30);
                        float nextX = last.x + last.w + rng.Next(22, 56) + w;
                        pillars[^1] = new Pillar { x = nextX, w = w };
                    }
                }
                else
                {
                    alive = false;
                    if (meta && meta.audioBus) meta.audioBus.BeepOnce(120f, 0.12f, 0.10f);
                }
            }
        }

        // Camera follow after pillar #1 so you can see what's next
        float targetCam = Mathf.Max(0f, px - RetroDraw.ViewW * 0.33f);
        float followLerp = (onIndex >= 1) ? 10f : 4f;
        camX = Mathf.Lerp(camX, targetCam, followLerp * Time.deltaTime);

        // Feet stay on the cap plane
        py = platformY + capH;
    }

    // --- tiny helper for clouds ---
    void Cloud(float wx, int wy, int w, int h, float parallax)
    {
        int sx = Mathf.RoundToInt(wx - camX * parallax);
        RetroDraw.PixelRect(sx, wy, w, h, sw, sh, new Color(1f, 1f, 1f, 0.85f));
        RetroDraw.PixelRect(sx + 3, wy + h, w - 6, 2, sw, sh, new Color(1f, 1f, 1f, 0.70f));
    }

    void OnGUI()
    {
        if (!Running) return;

        // Dynamic view (no bars, no crop, just more world)
        RetroDraw.Begin(sw, sh);
        int vw = RetroDraw.ViewW;
        int vh = RetroDraw.ViewH;

        // ---- Full blue sky ----
        RetroDraw.PixelRect(0, 0, vw, vh, sw, sh, new Color(0.29f, 0.52f, 0.96f, 1));

        // Clouds (wrap across full dynamic width)
        float t = Time.time * 8f;
        Cloud(( 20 + t) % (vw + 60) - 30, 150, 28, 6, 0.4f);
        Cloud((100 + t*0.7f) % (vw + 80) - 40, 138, 34, 7, 0.5f);
        Cloud((170 + t*1.1f) % (vw + 50) - 25, 160, 22, 5, 0.35f);

        // ---- Pillars (tall columns with bright caps) ----
        for (int i = 0; i < pillars.Length; i++)
        {
            int sx = Mathf.RoundToInt(pillars[i].x - camX);
            int w  = pillars[i].w;
            if (sx + w < -20 || sx - w > vw + 20) continue;

            var col = new Color(0.08f, 0.44f, 0.35f, 1);
            RetroDraw.PixelRect(sx - w/2, 0, w, (int)platformY, sw, sh, col);
            var cap = new Color(0.20f, 0.95f, 0.70f, 1);
            RetroDraw.PixelRect(sx - w/2, (int)platformY, w, capH, sw, sh, cap);
            RetroDraw.PixelRect(sx - w/2, (int)platformY + capH - 1, w, 1, sw, sh, new Color(0.9f,1f,0.95f,0.6f));
        }

        // ---- Player sprite (FEET at py; draw up) ----
        int ix = Mathf.RoundToInt(px - camX), iy = Mathf.RoundToInt(py);
        int swing = dashing ? (int)(Mathf.Sin(legAnim) * 2f) : 0;
        RetroDraw.PixelRect(ix - 3, iy - 1, 6, 1, sw, sh, new Color(0,0,0,0.25f));                // feet shadow
        RetroDraw.PixelRect(ix - 3 - swing, iy - 8, 2, 8, sw, sh, new Color(0.18f,0.75f,0.40f,1)); // leg L
        RetroDraw.PixelRect(ix + 1 + swing, iy - 8, 2, 8, sw, sh, new Color(0.18f,0.75f,0.40f,1)); // leg R
        RetroDraw.PixelRect(ix - 2, iy + 0, 4, 6, sw, sh, new Color(0.98f,0.82f,0.28f,1));         // torso
        RetroDraw.PixelRect(ix + 2, iy + 2, 2, 2, sw, sh, new Color(0.98f,0.93f,0.83f,1));         // arm
        RetroDraw.PixelRect(ix - 2, iy + 6, 4, 4, sw, sh, new Color(0.98f,0.93f,0.83f,1));         // head
        RetroDraw.PixelRect(ix - 3, iy + 10, 6, 1, sw, sh, new Color(1f,0.76f,0.2f,1));            // crown

        // ---- Charge meter uses dynamic width ----
        RetroDraw.PixelRect(20, 10, vw - 40, 6, sw, sh, new Color(0,0,0,0.55f));
        if (grounded && !dashing)
        {
            int fill = Mathf.RoundToInt((vw - 44) * charge);
            RetroDraw.PixelRect(22, 11, fill, 4, sw, sh, new Color(1f,0.56f,0.22f,1));
        }

        // ---- Game over card, centered in dynamic width ----
        if (!alive)
        {
            RetroDraw.PixelRect(vw/2 - 50, sh/2 - 18, 100, 36, sw, sh, new Color(0,0,0,0.85f));
            RetroDraw.PrintBig  (vw/2 - 30, sh/2 - 4, "YOU FELL", sw, sh, Color.white);
            RetroDraw.PrintSmall(vw/2 - 55, sh/2 - 14, "A: RETRY    Back: MENU", sw, sh, new Color(0.85f,0.9f,1f,1));
        }

        DrawCommonHUD(sw, sh);
    }
}
