// === CannonManGame.cs ===
// Gamevault 1981 — Cannon Man (1P)
// Aim a ground cannon, press Fire to launch, steer mid-air, collect items,
// and land on the moving safe zone to bank your haul. Crash = lose most haul
// and end the run. Later rounds add wind and moving hazards.
//
// Dependencies: GameManager, RetroDraw, RetroAudio, MetaGameManager
// Input: Fire via GameManager.BtnADown(); steering uses arrows/WASD or gamepad stick.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CannonManGame : GameManager
{
    // -------- Tuning (feel first, realism never) --------
    const float PIX = 1f;            // "fat pixel" knob for art blocks
    const float GRAVITY = 52f;       // px/s^2 downward
    const float STEER_ACC = 85f;     // px/s^2 side thrust in air
    const float AIR_DRAG = 0.18f;    // damp side slip a touch
    const float MAX_HSPEED = 90f;    // cap horizontal speed

    const float ANG_MIN = 10f;
    const float ANG_MAX = 78f;
    const float PWR_MIN = 32f;       // muzzle speed (px/s)
    const float PWR_MAX = 115f;

    const int   HAUL_VALUE_ITEM = 100;
    const int   KEEP_ON_CRASH_PERCENT = 25; // crash keeps 25% of haul

    const float SAFE_W_MIN = 34f;
    const float SAFE_W_MAX = 64f;

    const float WIND_CHANGE_EVERY = 2.75f;  // seconds between gust targets
    const float WIND_SMOOTH = 1.8f;         // how quickly wind homes to target

    const int   ITEMS_PER_ROUND_BASE = 7;
    const int   HAZARDS_PER_ROUND_BASE = 1;

    // -------- Colors (Atari-ish, limited) --------
    static readonly Color SKY_TOP  = new (0.08f,0.10f,0.18f,1);
    static readonly Color SKY_BOT  = new (0.03f,0.04f,0.07f,1);
    static readonly Color HILL     = new (0.10f,0.22f,0.28f,0.9f);
    static readonly Color CLOUD    = new (0.90f,0.95f,1f,0.7f);
    static readonly Color GROUND   = new (0.10f,0.12f,0.14f,1);
    static readonly Color SAFE_A   = new (0.25f,0.95f,0.55f,1);
    static readonly Color SAFE_B   = new (0.18f,0.75f,0.42f,1);
    static readonly Color UI       = new (0.95f,0.96f,1f,1);
    static readonly Color UIDIM    = new (0.85f,0.88f,0.96f,0.85f);
    static readonly Color ITEMCOL  = new (0.98f,0.88f,0.25f,1);
    static readonly Color HAZARD   = new (0.95f,0.35f,0.35f,1);
    static readonly Color MAN      = new (0.95f,0.75f,0.50f,1);
    static readonly Color CANNON   = new (0.35f,0.65f,0.95f,1);

    // -------- State --------
    enum Phase { Aim, Flight, Resolve, Between, Dead }
    Phase phase = Phase.Aim;

    // world metrics
    float groundY;      // top of ground band
    float safeX, safeW; // safe zone
    bool  safeMoves;
    float safeOsc;      // phase for movement

    // cannon
    Vector2 cannonPos;
    float angleDeg = 45f;
    float power    = 72f;

    // player
    Vector2 pos, vel;
    float radius = 4f;

    // round & scoring
    int level = 1;
    int haul = 0;               // collected this shot, not yet banked
    float betweenTimer = 0f;

    // wind
    float wind = 0f;
    float windTarget = 0f;
    float windTimer = 0f;

    // VFX
    float flash = 0f;           // screen white flash after fire/crash
    float muzzleFlash = 0f;
    readonly System.Random rng = new System.Random();

    struct Item { public float x,y; public float vx,vy; public float bobT; public bool taken; }
    List<Item> items = new List<Item>();

    struct Hazard { public float x,y; public float vx; public float r; }
    List<Hazard> hazards = new List<Hazard>();

    struct Puff { public float x,y; public float life; }
    List<Puff> puffs = new List<Puff>();

    public override void Begin()
    {
        base.Begin();
        if (!meta) meta = MetaGameManager.I;
        Def = Def ?? new GameDef("CannonMan", "Cannon Man", 8,
            "Aim, fire, steer, and land on the safe zone to bank the haul.", GameFlags.Solo, typeof(CannonManGame));
    }

    public override void OnStartMode()
    {
        base.OnStartMode();
        ResetScores();

        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;

        groundY   = 16f;
        cannonPos = new Vector2(18f, groundY + 4f);

        level = 1;
        haul = 0;

        SetupRound();
        Running = true;
    }

    // ----- Round setup -----
    void SetupRound()
    {
        phase = Phase.Aim;
        vel = Vector2.zero;
        pos = cannonPos + new Vector2(8f, 8f); // rest near muzzle
        angleDeg = Mathf.Clamp(angleDeg, ANG_MIN, ANG_MAX);
        power    = Mathf.Clamp(power,    PWR_MIN, PWR_MAX);

        flash = 0f; muzzleFlash = 0f;
        items.Clear();
        hazards.Clear();
        puffs.Clear();
        haul = 0;

        // safe zone: width shrinks with level; starts mid–to–right, later moves
        int sw = RetroDraw.ViewW; int sh = RetroDraw.ViewH;
        float w = Mathf.Lerp(SAFE_W_MAX, SAFE_W_MIN, Mathf.Clamp01((level-1) / 12f));
        safeW = w;
        safeX = Mathf.Lerp(sw*0.45f, sw*0.85f, (float)rng.NextDouble());
        safeMoves = level >= 3;
        safeOsc = (float)rng.NextDouble() * Mathf.PI * 2f;

        // wind baseline scales with level; target will meander every few seconds
        wind = 0f;
        windTarget = RandRange(-WindMax(), WindMax());
        windTimer = 0f;

        // spawn a handful of items along interesting arcs
        int itemsCount = ITEMS_PER_ROUND_BASE + Mathf.Min(10, level*2);
        for (int i = 0; i < itemsCount; i++)
        {
            float x = Mathf.Lerp(sw*0.18f, sw*0.95f, (float)rng.NextDouble());
            float y = Mathf.Lerp(groundY + 18f, Mathf.Min(sh-12f, groundY + 95f), (float)rng.NextDouble());
            float vx = RandRange(-10f, 10f);
            float vy = RandRange(-6f, 6f);
            items.Add(new Item{ x=x, y=y, vx=vx, vy=vy, bobT=RandRange(0f, 100f), taken=false });
        }

        // hazards: horizontally moving “drones”
        int hzCount = HAZARDS_PER_ROUND_BASE + Mathf.Min(8, (level-1));
        for (int i = 0; i < hzCount; i++)
        {
            float y = Mathf.Lerp(groundY + 22f, groundY + 90f, (float)rng.NextDouble());
            float x = Mathf.Lerp(40f, sw-16f, (float)rng.NextDouble());
            float vx = RandRange(18f, 36f) * (rng.NextDouble() < 0.5 ? -1f : 1f) * (1f + level*0.05f);
            hazards.Add(new Hazard{ x=x, y=y, vx=vx, r=5.5f });
        }
    }

    // ----- Update -----
    void Update()
    {
        if (!Running) return;
        if (HandleGameOver()) return;
        if (HandlePause()) return;

        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;

        // input (Fire from GameManager; steering uses arrows/pad)
        var k = Keyboard.current;
        var g = Gamepad.current;

        // phase logic
        switch (phase)
        {
            case Phase.Aim:
            {
                // aim: left/right = angle, up/down = power
                float a = 0f, p = 0f;
                if (k != null)
                {
                    if (k.leftArrowKey.isPressed  || k.aKey.isPressed) a -= 1f;
                    if (k.rightArrowKey.isPressed || k.dKey.isPressed) a += 1f;
                    if (k.upArrowKey.isPressed    || k.wKey.isPressed) p += 1f;
                    if (k.downArrowKey.isPressed  || k.sKey.isPressed) p -= 1f;
                }
                if (g != null)
                {
                    var ls = g.leftStick.ReadValue();
                    a += Mathf.Sign(ls.x) * Mathf.Min(Mathf.Abs(ls.x), 1f);
                    p += Mathf.Sign(ls.y) * Mathf.Min(Mathf.Abs(ls.y), 1f);
                }

                angleDeg = Mathf.Clamp(angleDeg + a * 70f * dt, ANG_MIN, ANG_MAX);
                power    = Mathf.Clamp(power    + p * 90f * dt, PWR_MIN, PWR_MAX);

                // Fire
                if (BtnADown())
                {
                    float ang = angleDeg * Mathf.Deg2Rad;
                    vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * power;
                    pos = cannonPos + new Vector2(Mathf.Cos(ang)*8f, Mathf.Sin(ang)*8f);
                    phase = Phase.Flight;
                    muzzleFlash = 0.22f;
                    flash = 0.25f;
                    Ping(680, 0.05f, 0.28f);
                    Ping(1120, 0.04f, 0.22f);
                    // first puff
                    puffs.Add(new Puff{ x=pos.x-2f, y=pos.y-2f, life=0.35f });
                }
                break;
            }

            case Phase.Flight:
            {
                // wind target drifts over time
                windTimer += dt;
                if (windTimer >= WIND_CHANGE_EVERY)
                {
                    windTimer = 0f;
                    windTarget = RandRange(-WindMax(), WindMax());
                }
                // approach target
                wind = Mathf.Lerp(wind, windTarget, 1f - Mathf.Exp(-WIND_SMOOTH*dt));

                // mid-air steering
                float steer = 0f;
                if (k != null)
                {
                    if (k.leftArrowKey.isPressed  || k.aKey.isPressed) steer -= 1f;
                    if (k.rightArrowKey.isPressed || k.dKey.isPressed) steer += 1f;
                }
                if (g != null) steer += g.leftStick.ReadValue().x;

                vel.x += Mathf.Clamp(steer, -1f, 1f) * STEER_ACC * dt;

                // wind + gravity + drag
                vel.x += wind * dt;
                vel.x = Mathf.Clamp(vel.x, -MAX_HSPEED, MAX_HSPEED);
                vel.y -= GRAVITY * dt;
                vel.x *= Mathf.Exp(-AIR_DRAG*dt);

                // integrate
                pos += vel * dt;

                // bounds (left/right walls bounce lightly)
                if (pos.x < 2f) { pos.x = 2f; vel.x = Mathf.Abs(vel.x)*0.6f; }
                if (pos.x > sw-2f) { pos.x = sw-2f; vel.x = -Mathf.Abs(vel.x)*0.6f; }

                // spawn trailing puffs
                if (rng.NextDouble() < 0.25 * dt * (1.0 + vel.magnitude/80.0))
                    puffs.Add(new Puff{ x=pos.x-vel.x*0.02f, y=pos.y-vel.y*0.02f, life=0.35f });

                // collect items
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it.taken) continue;

                    // drift a little
                    it.bobT += dt;
                    it.x += it.vx * dt;
                    it.y += it.vy * dt + Mathf.Sin(it.bobT*3.2f) * 0.12f;

                    if (Dist(pos.x,pos.y,it.x,it.y) < radius + 4.5f)
                    {
                        it.taken = true;
                        haul += HAUL_VALUE_ITEM;
                        Ping(750 + (haul%400), 0.05f, 0.20f);
                        // tiny flash on pickup
                        flash = Mathf.Max(flash, 0.10f);
                    }

                    // keep in bounds
                    if (it.x < 4f)  { it.x = 4f;  it.vx = Mathf.Abs(it.vx); }
                    if (it.x > sw-4){ it.x = sw-4; it.vx = -Mathf.Abs(it.vx); }
                    if (it.y < groundY + 12f) { it.y = groundY + 12f; it.vy = Mathf.Abs(it.vy); }
                    if (it.y > sh-8f) { it.y = sh-8f; it.vy = -Mathf.Abs(it.vy); }

                    items[i] = it;
                }

                // hazards move
                for (int i = 0; i < hazards.Count; i++)
                {
                    var h = hazards[i];
                    h.x += h.vx * dt;
                    if (h.x < 6f)  { h.x = 6f;  h.vx = Mathf.Abs(h.vx); }
                    if (h.x > sw-6f){ h.x = sw-6f; h.vx = -Mathf.Abs(h.vx); }
                    hazards[i] = h;

                    if (Dist(pos.x,pos.y,h.x,h.y) < radius + h.r)
                    {
                        Crash();
                        return;
                    }
                }

                // safe zone motion
                if (safeMoves)
                {
                    safeOsc += dt * (0.7f + level*0.05f);
                    safeX += Mathf.Sin(safeOsc) * 18f * dt;
                    safeX = Mathf.Clamp(safeX, 14f, sw - 14f);
                }

                // landing?
                if (pos.y <= groundY + radius)
                {
                    bool inSafe = (pos.x >= safeX - safeW*0.5f && pos.x <= safeX + safeW*0.5f);
                    if (inSafe)
                    {
                        // bank haul, next round
                        ScoreP1 += haul;
                        Ping(1050, 0.08f, 0.28f);
                        flash = 0.18f;
                        phase = Phase.Resolve;
                        betweenTimer = 0.6f;
                    }
                    else
                    {
                        Crash();
                        return;
                    }
                }
                break;
            }

            case Phase.Resolve:
            {
                betweenTimer -= dt;
                if (betweenTimer <= 0f)
                {
                    level++;
                    SetupRound();
                    phase = Phase.Aim;
                }
                break;
            }

            case Phase.Between:
            case Phase.Dead:
                break;
        }

        // fade flashes & puffs
        flash = Mathf.Max(0f, flash - dt*0.9f);
        muzzleFlash = Mathf.Max(0f, muzzleFlash - dt*3.2f);
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            var p = puffs[i];
            p.life -= dt;
            p.y += 6f * dt;
            if (p.life <= 0f) puffs.RemoveAt(i); else puffs[i] = p;
        }
    }

    void Crash()
    {
        // keep a sliver of the haul
        int keep = Mathf.RoundToInt(haul * (KEEP_ON_CRASH_PERCENT/100f));
        if (keep > 0) ScoreP1 += keep;

        Ping(220, 0.10f, 0.35f);
        Ping(140, 0.13f, 0.35f);
        flash = 0.35f;

        GameOverNow();
        phase = Phase.Dead;
    }

    // ----- Drawing -----
    void OnGUI()
    {
        if (!Running) return;

        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;
        RetroDraw.Begin(sw, sh);

        DrawBackground(sw, sh);
        DrawGround(sw, sh);
        DrawSafeZone(sw, sh);

        DrawCannon(sw, sh);

        // items
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i]; if (it.taken) continue;
            int w=6,h=6;
            int x = Mathf.RoundToInt(it.x - w/2), y = Mathf.RoundToInt(it.y - h/2);
            RetroDraw.PixelRect(x, y, w, h, sw, sh, ITEMCOL);
            RetroDraw.PixelRect(x+1, y+1, w-2, h-2, sw, sh, new Color(0,0,0,0.2f));
        }

        // hazards (simple blinking saucers)
        for (int i = 0; i < hazards.Count; i++)
        {
            var h = hazards[i];
            int r = Mathf.RoundToInt(h.r);
            int x = Mathf.RoundToInt(h.x - r), y = Mathf.RoundToInt(h.y - r);
            RetroDraw.PixelRect(x, y, r*2, r, sw, sh, HAZARD);
            RetroDraw.PixelRect(x+1, y+1, r*2-2, r-2, sw, sh, new Color(0,0,0,0.25f));
        }

        // puffs
        foreach (var p in puffs)
        {
            int s = Mathf.RoundToInt(6 * Mathf.Clamp01(p.life/0.35f));
            int x = Mathf.RoundToInt(p.x - s/2f), y = Mathf.RoundToInt(p.y - s/2f);
            var c = new Color(1,1,1, Mathf.Clamp01(p.life*2.2f));
            RetroDraw.PixelRect(x, y, s, s, sw, sh, c);
        }

        // player (little capsule)
        DrawMan(sw, sh);

        // HUD
        DrawHUD(sw, sh);

        // flash overlay
        if (flash > 0f)
            RetroDraw.PixelRect(0, 0, sw, sh, sw, sh, new Color(1,1,1, Mathf.Clamp01(flash)));

        DrawCommonHUD(sw, sh);
    }

    void DrawBackground(int sw, int sh)
    {
        // vertical sky gradient
        for (int y = 0; y < sh; y+=2)
        {
            float t = y / (float)sh;
            var c = Color.Lerp(SKY_BOT, SKY_TOP, t);
            RetroDraw.PixelRect(0, y, sw, 2, sw, sh, c);
        }
        // chunky mountains
        int baseY = Mathf.RoundToInt(groundY + 26f);
        for (int i=0;i<3;i++)
        {
            int w = Mathf.RoundToInt(sw * (0.22f + 0.10f*i));
            int h = 18 + i*10;
            int cx = Mathf.RoundToInt(sw*(0.30f + 0.30f*i));
            for (int s=0;s<h;s+=2)
            {
                float t = 1f - s/(float)h;
                int segW = Mathf.RoundToInt(w * t);
                int x = cx - segW/2;
                RetroDraw.PixelRect(x, baseY + s, segW, 2, sw, sh, new Color(HILL.r,HILL.g,HILL.b,0.25f + 0.5f*t));
            }
        }
        // primitive clouds
        for (int i=0;i<4;i++)
        {
            int cx = (i*43 + 20) % (sw-40) + 20;
            int cy = Mathf.RoundToInt(groundY + 70 + 12*Mathf.Sin((i+0.3f)));
            RetroDraw.PixelRect(cx-10, cy, 20, 4, sw, sh, CLOUD);
            RetroDraw.PixelRect(cx-6,  cy+4, 12, 4, sw, sh, CLOUD);
        }
    }

    void DrawGround(int sw, int sh)
    {
        // ground bands
        RetroDraw.PixelRect(0, 0, sw, Mathf.RoundToInt(groundY), sw, sh, new Color(GROUND.r,GROUND.g,GROUND.b,1));
        for (int y=0; y<groundY; y+=4)
        {
            RetroDraw.PixelRect(0, y, sw, 2, sw, sh, new Color(1,1,1,0.04f));
        }
    }

    void DrawSafeZone(int sw, int sh)
    {
        int w = Mathf.RoundToInt(safeW);
        int x0 = Mathf.RoundToInt(safeX - w/2);
        int y0 = Mathf.RoundToInt(groundY - 2);
        for (int i=0;i<w;i+=4)
        {
            Color c = ((i/4)%2==0) ? SAFE_A : SAFE_B;
            RetroDraw.PixelRect(x0+i, y0, Mathf.Min(4, x0+w-(x0+i)), 4, sw, sh, c);
        }
        // small pillars
        RetroDraw.PixelRect(x0,     y0, 2, 6, sw, sh, SAFE_B);
        RetroDraw.PixelRect(x0+w-2, y0, 2, 6, sw, sh, SAFE_B);
    }

    void DrawCannon(int sw, int sh)
    {
        // base
        int bx = Mathf.RoundToInt(cannonPos.x - 8);
        int by = Mathf.RoundToInt(cannonPos.y - 6);
        RetroDraw.PixelRect(bx, by, 16, 6, sw, sh, CANNON);
        RetroDraw.PixelRect(bx+2, by+2, 12, 2, sw, sh, new Color(0,0,0,0.3f));

        // barrel (fake rotate by drawing a stepped rectangle)
        float ang = Mathf.Deg2Rad * angleDeg;
        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        Vector2 tip = cannonPos + dir * 12f;

        // draw as a 3-step line to look "rotated"
        for (int s=0; s<3; s++)
        {
            float t0 = s/3f, t1=(s+1)/3f;
            Vector2 a = cannonPos + dir * (8f + 10f*t0);
            Vector2 b = cannonPos + dir * (8f + 10f*t1);
            int rx = Mathf.RoundToInt(Mathf.Min(a.x,b.x));
            int ry = Mathf.RoundToInt(Mathf.Min(a.y,b.y));
            int rw = Mathf.RoundToInt(Mathf.Abs(a.x-b.x)) + 1;
            int rh = Mathf.RoundToInt(Mathf.Abs(a.y-b.y)) + 2;
            RetroDraw.PixelRect(rx, ry, Mathf.Max(2,rw), Mathf.Max(2,rh), sw, sh, CANNON);
        }

        // muzzle flash
        if (muzzleFlash > 0f)
        {
            int s = 8;
            int mx = Mathf.RoundToInt(tip.x - s/2f);
            int my = Mathf.RoundToInt(tip.y - s/2f);
            RetroDraw.PixelRect(mx, my, s, s, sw, sh, new Color(1,0.9f,0.4f, muzzleFlash));
        }
    }

    void DrawMan(int sw, int sh)
    {
        // "capsule" body
        int x = Mathf.RoundToInt(pos.x), y = Mathf.RoundToInt(pos.y);
        int w = 6, h = 8;
        RetroDraw.PixelRect(x-3, y-4, w, h, sw, sh, MAN);
        RetroDraw.PixelRect(x-2, y-3, w-2, h-2, sw, sh, new Color(0,0,0,0.22f));
        // tiny nose cone hint
        RetroDraw.PixelRect(x+2, y-1, 2, 2, sw, sh, new Color(1,1,1,0.2f));
    }

    void DrawHUD(int sw, int sh)
    {
        // left: angle/power
        string a = $"ANG {(int)angleDeg}°";
        string p = $"PWR {(int)power}";
        RetroDraw.PrintSmall(6, sh-12, a, sw, sh, UIDIM);
        RetroDraw.PrintSmall(6, sh-22, p, sw, sh, UIDIM);

        // center: wind indicator
        string wtxt = $"WIND {wind:+0.0;-0.0;0.0}";
        int mw = wtxt.Length*5;
        RetroDraw.PrintSmall(sw/2 - mw/2 +1, sh-12, wtxt, sw, sh, new Color(0,0,0,0.35f));
        RetroDraw.PrintSmall(sw/2 - mw/2,     sh-11, wtxt, sw, sh, UI);

        // right: level & haul & score
        string r = $"LV {level}   HAUL {haul}";
        int tw = r.Length*5;
        RetroDraw.PrintSmall(sw-(tw+6), sh-12, r, sw, sh, UI);

        // phase tip
        if (phase == Phase.Aim)
        {
            string tip = "AIM  \u2190\u2192  /  POWER  \u2191\u2193   •   FIRE!";
            int w = tip.Length*5;
            RetroDraw.PrintSmall(sw/2 - w/2, 8, tip, sw, sh, UIDIM);
        }
        else if (phase == Phase.Resolve)
        {
            string tip = "LANDED! BANKING...";
            int w = tip.Length*5;
            RetroDraw.PrintSmall(sw/2 - w/2, 8, tip, sw, sh, UI);
        }
    }

    // ----- helpers -----
    float RandRange(float a, float b) => a + (float)rng.NextDouble()*(b-a);
    float Dist(float ax,float ay,float bx,float by)
    { float dx=ax-bx, dy=ay-by; return Mathf.Sqrt(dx*dx+dy*dy); }

    float WindMax()
    {
        // grows with level; feels “gusty but manageable”
        return 14f + level * 2.5f;
    }

    void Ping(float hz, float seconds, float vol)
    {
        if (meta && meta.audioBus) meta.audioBus.BeepOnce(hz, seconds, vol);
    }
}
