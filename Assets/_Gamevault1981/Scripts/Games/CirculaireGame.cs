// === CirculaireGame.cs ===
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CirculaireGame : GameManager
{
    const int   MAX_CIRCLES      = 24;
    const float BASE_SPEED       = 0.55f;
    const float SPEED_PER_STAGE  = 0.06f;
    const float TAP_WINDOW       = 0.28f;
    const float SNAP_WINDOW_DEG  = 14f;

    float LEFT_MIN_X => 14f;
    float LEFT_MAX_X => RetroDraw.ViewW * 0.47f;
    float RIGHT_MIN_X => RetroDraw.ViewW * 0.53f;
    float RIGHT_MAX_X => RetroDraw.ViewW - 14f;
    float MIN_Y => RetroDraw.ViewH * 0.20f;
    float MAX_Y => RetroDraw.ViewH * 0.86f;

    static readonly Color COL_BG    = new (0.05f,0.06f,0.09f,1);
    static readonly Color COL_NET   = new (0.35f,0.85f,1.00f,0.95f);
    static readonly Color COL_BURNT = new (0.18f,0.20f,0.25f,0.55f);
    static readonly Color COL_NODE  = new (1.00f,0.95f,0.45f,0.95f);
    static readonly Color COL_PICK  = new (1.00f,0.55f,0.30f,0.95f);
    static readonly Color COL_P1    = new (0.45f,1.00f,0.75f,1);
    static readonly Color COL_P2    = new (1.00f,0.55f,0.95f,1);
    static readonly Color UI        = new (0.95f,0.96f,1f,1);
    static readonly Color UIDIM     = new (0.85f,0.88f,0.96f,0.85f);

    struct Circle
    {
        public Vector2 baseC; public float r;
        public bool moving; public Vector2 amp; public float freq; public float phase;
        public Vector2 Center(float t) => moving
            ? new Vector2(baseC.x + Mathf.Sin(phase + t*freq)*amp.x,
                          baseC.y + Mathf.Sin(phase*0.7f + t*freq*0.9f)*amp.y)
            : baseC;
    }

    struct Node
    {
        public int a, b; public Vector2 p; public float angA, angB;
        public bool hasPickup; public bool taken; public int sideHint;
    }

    struct Player
    {
        public bool alive; public int circle; public float theta; public float speed;
        public bool[] burnt; public bool tapArmed; public float tapTimer;
        public int nearNodeIdx; public float nearNodeDeltaDeg;
    }

    System.Random rng = new System.Random();
    List<Circle> circlesLeft = new List<Circle>();
    List<Node>   nodes = new List<Node>();

    Player p1, p2;
    int stage = 1;
    int pickupsThisStage = 0;
    int stageTarget = 0;
    float tGlobal = 0f;
    bool vsMode = false;

    public override void Begin()
    {
        base.Begin();
        if (!meta) meta = MetaGameManager.I;
        Def = Def ?? new GameDef("Circulaire", "Circulaire", 9,
            "Run circles. Double-tap to jump at overlaps.", GameFlags.Solo | GameFlags.Versus2P, typeof(CirculaireGame));
    }

    public override void OnStartMode()
    {
        base.OnStartMode();
        vsMode = (Mode == GameMode.Versus2P);
        ScoreP1 = 0; ScoreP2 = 0;
        stage = 1;
        BuildStage();
        Running = true;
    }

    void BuildStage()
    {
        circlesLeft.Clear(); nodes.Clear();
        pickupsThisStage = 0;
        stageTarget = 5 + Mathf.Min(20, stage * 2);

        int N = Mathf.Clamp(6 + stage * 2, 6, MAX_CIRCLES);
        float cx = Mathf.Lerp(LEFT_MIN_X, LEFT_MAX_X, 0.5f);
        float cy = Mathf.Lerp(MIN_Y, MAX_Y, 0.5f);
        float Rx = (LEFT_MAX_X - LEFT_MIN_X) * 0.40f;
        float Ry = (MAX_Y - MIN_Y) * 0.36f;

        for (int i = 0; i < N; i++)
        {
            float th = (Mathf.PI*2f) * i / N;
            Vector2 c = new Vector2(cx + Mathf.Cos(th)*Rx, cy + Mathf.Sin(th)*Ry);
            c.x += rng.Next(-8, 9); c.y += rng.Next(-8, 9);
            float r = Mathf.Lerp(18f, 28f, (float)rng.NextDouble());
            bool moving = i < Mathf.Min(N, stage);
            Vector2 amp = new Vector2(rng.Next(0, 2) == 0 ? 0f : Mathf.Lerp(1.2f, 5.0f, (float)rng.NextDouble()),
                                      rng.Next(0, 2) == 0 ? 0f : Mathf.Lerp(1.2f, 5.0f, (float)rng.NextDouble()));
            float freq  = Mathf.Lerp(0.6f, 1.4f, (float)rng.NextDouble());
            float ph    = (float)rng.NextDouble() * Mathf.PI * 2f;
            circlesLeft.Add(new Circle { baseC = c, r = r, moving = moving, amp = amp, freq = freq, phase = ph });
        }

        RecomputeNodes(0f);
        for (int i = 0; i < nodes.Count; i++) { var n = nodes[i]; n.hasPickup = (rng.NextDouble() < 0.5); nodes[i] = n; }

        p1 = NewPlayer(BASE_SPEED + SPEED_PER_STAGE*(stage-1), circlesLeft.Count);
        if (vsMode) p2 = NewPlayer(BASE_SPEED + SPEED_PER_STAGE*(stage-1), circlesLeft.Count);
    }

    Player NewPlayer(float speed, int circleCount)
    {
        return new Player {
            alive = true, circle = 0, theta = 0f, speed = speed,
            burnt = new bool[circleCount], tapArmed = false, tapTimer = 0f,
            nearNodeIdx = -1, nearNodeDeltaDeg = 999f
        };
    }

    void Update()
    {
        if (!Running) return;
        if (HandleGameOver()) return;
        if (HandlePause()) return;

        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        tGlobal += dt;

        RecomputeNodes(tGlobal);

        if (p1.alive) StepPlayer(ref p1, 0, dt);
        if (vsMode && p2.alive) StepPlayer(ref p2, 1, dt);

        if (vsMode)
        {
            bool noPickupsLeft = true;
            for (int i = 0; i < nodes.Count; i++) { if (nodes[i].hasPickup && !nodes[i].taken) { noPickupsLeft = false; break; } }
            if ((!p1.alive && !p2.alive) || noPickupsLeft) { GameOverNow(); return; }
        }
    }

    void StepPlayer(ref Player pl, int playerIndex, float dt)
    {
        pl.theta += pl.speed * dt;
        pl.theta = WrapAngle(pl.theta);

        var c = circlesLeft[pl.circle];
        Vector2 cc = c.Center(tGlobal);

        int bestIdx = -1; float bestDelta = 999f;
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            float ang = n.a == pl.circle ? n.angA : (n.b == pl.circle ? n.angB : float.NaN);
            if (float.IsNaN(ang)) continue;
            float d = Mathf.DeltaAngle(pl.theta * Mathf.Rad2Deg, ang * Mathf.Rad2Deg);
            float ad = Mathf.Abs(d);
            if (ad < bestDelta) { bestDelta = ad; bestIdx = i; }
        }
        pl.nearNodeIdx = bestIdx;
        pl.nearNodeDeltaDeg = bestDelta;

        pl.tapTimer -= dt;
        if (pl.tapTimer <= 0f) { pl.tapTimer = 0f; pl.tapArmed = false; }

        if (FireDown(playerIndex))
        {
            if (!pl.tapArmed)
            {
                pl.tapArmed = true; pl.tapTimer = TAP_WINDOW;
                Ping(700 + playerIndex*150, 0.02f, 0.10f);
            }
            else
            {
                bool near = (pl.nearNodeIdx >= 0) && (pl.nearNodeDeltaDeg <= SNAP_WINDOW_DEG);
                if (!near) { KillPlayer(ref pl, playerIndex); return; }

                var n = nodes[pl.nearNodeIdx];
                int from = pl.circle;
                int to   = (n.a == from) ? n.b : n.a;
                if (pl.burnt[to]) { KillPlayer(ref pl, playerIndex); return; }

                float newTheta = (n.a == from) ? n.angB : n.angA;
                pl.circle = to; pl.theta = newTheta;
                pl.burnt[from] = true;

                if (n.hasPickup && !n.taken)
                {
                    var nn = nodes[pl.nearNodeIdx];
                    nn.taken = true; nodes[pl.nearNodeIdx] = nn;

                    if (playerIndex == 0) ScoreP1 += 100; else ScoreP2 += 100;
                    pickupsThisStage++;
                    Ping(950 + playerIndex*180, 0.06f, 0.24f);

                    if (!vsMode && pickupsThisStage >= stageTarget)
                    {
                        stage++;
                        p1.speed = BASE_SPEED + SPEED_PER_STAGE*(stage-1);
                        BuildStage();
                        Ping(1200, 0.09f, 0.30f);
                        return;
                    }
                }
                else
                {
                    Ping(820 + playerIndex*120, 0.04f, 0.18f);
                }

                pl.tapArmed = false; pl.tapTimer = 0f;
            }
        }

        if (playerIndex == 0) p1 = pl; else p2 = pl;
    }

    void KillPlayer(ref Player pl, int playerIndex)
    {
        pl.alive = false;
        Ping(180, 0.09f, 0.35f);
        if (!vsMode) GameOverNow();
    }

    void RecomputeNodes(float time)
    {
        nodes.Clear();
        int N = circlesLeft.Count;
        for (int i = 0; i < N; i++)
        {
            var ca = circlesLeft[i]; Vector2 A = ca.Center(time); float ra = ca.r;
            for (int j = i+1; j < N; j++)
            {
                var cb = circlesLeft[j]; Vector2 B = cb.Center(time); float rb = cb.r;

                Vector2 d = B - A; float dist = d.magnitude;
                if (dist <= Mathf.Epsilon) continue;
                if (dist > ra + rb) continue;
                if (dist < Mathf.Abs(ra - rb)) continue;

                float a  = (ra*ra - rb*rb + dist*dist) / (2f * dist);
                float h2 = ra*ra - a*a; if (h2 < 0f) continue;
                float h = Mathf.Sqrt(h2);
                Vector2 mid = A + d * (a / dist);
                Vector2 off = new Vector2(-d.y, d.x) * (h / dist);

                Vector2 P1 = mid + off; Vector2 P2 = mid - off;
                MakeNode(i, j, A, ra, B, rb, P1, 0);
                MakeNode(i, j, A, ra, B, rb, P2, 1);
            }
        }
    }

    void MakeNode(int ia, int ib, Vector2 A, float ra, Vector2 B, float rb, Vector2 P, int side)
    {
        float angA = Mathf.Atan2(P.y - A.y, P.x - A.x);
        float angB = Mathf.Atan2(P.y - B.y, P.x - B.x);
        nodes.Add(new Node { a = ia, b = ib, p = P, angA = angA, angB = angB, hasPickup = false, taken = false, sideHint = side });
    }

    void OnGUI()
    {
        if (!Running) return;
        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;
        RetroDraw.Begin(sw, sh);

        RetroDraw.PixelRect(0, 0, sw, sh, sw, sh, COL_BG);
        DrawNetwork(0, sw, sh);
        if (vsMode) DrawNetwork(1, sw, sh);
        if (p1.alive) DrawPlayer(ref p1, 0, COL_P1, sw, sh);
        if (vsMode && p2.alive) DrawPlayer(ref p2, 1, COL_P2, sw, sh);
        DrawHUD(sw, sh);
        DrawCommonHUD(sw, sh);
    }

    void DrawNetwork(int side, int sw, int sh)
    {
        for (int i = 0; i < circlesLeft.Count; i++)
        {
            var c = circlesLeft[i];
            Vector2 centerL = c.Center(tGlobal);
            Vector2 center  = (side == 0) ? centerL : new Vector2(sw - centerL.x, centerL.y);

            int steps = 64;
            for (int s = 0; s < steps; s++)
            {
                float th = (Mathf.PI*2f) * s / steps;
                Vector2 p = new Vector2(center.x + Mathf.Cos(th)*c.r, center.y + Mathf.Sin(th)*c.r);
                bool burnt = (side==0 ? p1.burnt[i] : p2.burnt[i]);
                Color edge = burnt ? COL_BURNT : COL_NET;
                RetroDraw.PixelRect(Mathf.RoundToInt(p.x)-1, Mathf.RoundToInt(p.y)-1, 2, 2, sw, sh, edge);
            }
        }

        for (int n = 0; n < nodes.Count; n++)
        {
            var nd = nodes[n]; if (nd.taken) continue;
            Vector2 p = (side==0) ? nd.p : new Vector2(sw - nd.p.x, nd.p.y);
            RetroDraw.PixelRect(Mathf.RoundToInt(p.x)-1, Mathf.RoundToInt(p.y)-1, 2, 2, sw, sh, COL_NODE);
            if (nd.hasPickup)
            {
                RetroDraw.PixelRect(Mathf.RoundToInt(p.x)-2, Mathf.RoundToInt(p.y)-2, 4, 1, sw, sh, COL_PICK);
                RetroDraw.PixelRect(Mathf.RoundToInt(p.x)-2, Mathf.RoundToInt(p.y)+1, 4, 1, sw, sh, COL_PICK);
                RetroDraw.PixelRect(Mathf.RoundToInt(p.x)-2, Mathf.RoundToInt(p.y)-2, 1, 4, sw, sh, COL_PICK);
                RetroDraw.PixelRect(Mathf.RoundToInt(p.x)+1, Mathf.RoundToInt(p.y)-2, 1, 4, sw, sh, COL_PICK);
            }
        }
    }

    void DrawPlayer(ref Player pl, int side, Color col, int sw, int sh)
    {
        var c = circlesLeft[pl.circle];
        Vector2 centerL = c.Center(tGlobal);
        Vector2 center  = (side==0) ? centerL : new Vector2(sw - centerL.x, centerL.y);
        Vector2 pos = new Vector2(center.x + Mathf.Cos(pl.theta)*c.r, center.y + Mathf.Sin(pl.theta)*c.r);

        RetroDraw.PixelRect(Mathf.RoundToInt(pos.x)-1, Mathf.RoundToInt(pos.y)-1, 3, 3, sw, sh, col);

        if (pl.tapArmed && pl.tapTimer > 0f)
        {
            Color halo = new Color(col.r, col.g, col.b, 0.25f);
            RetroDraw.PixelRect(Mathf.RoundToInt(pos.x)-3, Mathf.RoundToInt(pos.y)-3, 7, 1, sw, sh, halo);
            RetroDraw.PixelRect(Mathf.RoundToInt(pos.x)-3, Mathf.RoundToInt(pos.y)+3, 7, 1, sw, sh, halo);
            RetroDraw.PixelRect(Mathf.RoundToInt(pos.x)-3, Mathf.RoundToInt(pos.y)-3, 1, 7, sw, sh, halo);
            RetroDraw.PixelRect(Mathf.RoundToInt(pos.x)+3, Mathf.RoundToInt(pos.y)-3, 1, 7, sw, sh, halo);
        }

        if (pl.nearNodeIdx >= 0 && pl.nearNodeDeltaDeg <= SNAP_WINDOW_DEG)
        {
            var nd = nodes[pl.nearNodeIdx];
            Vector2 np = (side==0) ? nd.p : new Vector2(sw - nd.p.x, nd.p.y);
            RetroDraw.PixelRect(Mathf.RoundToInt(np.x)-1, Mathf.RoundToInt(np.y)-1, 3, 3, sw, sh, new Color(1,1,1,0.25f));
        }
    }

    void DrawHUD(int sw, int sh)
    {
        RetroDraw.PrintSmall(6, sh-12, $"STAGE {stage}", sw, sh, UIDIM);

        if (!vsMode)
        {
            string tgt = $"PICKUPS {pickupsThisStage}/{stageTarget}";
            int tw = tgt.Length*5;
            RetroDraw.PrintSmall(sw - (tw+6), sh-12, tgt, sw, sh, UI);
            string tip = "DOUBLE-TAP to jump at overlaps";
            int mw = tip.Length*5;
            RetroDraw.PrintSmall(sw/2 - mw/2, 8, tip, sw, sh, UIDIM);
        }
        else
        {
            string s1 = $"P1 {ScoreP1}";
            RetroDraw.PrintSmall(6, sh-12, s1, sw, sh, UI);
            string s2 = $"{ScoreP2} P2";
            int tw = s2.Length*5;
            RetroDraw.PrintSmall(sw - (tw+6), sh-12, s2, sw, sh, UI);
        }
    }

    static float WrapAngle(float a)
    {
        while (a > Mathf.PI*2f) a -= Mathf.PI*2f;
        while (a < 0f) a += Mathf.PI*2f;
        return a;
    }

    bool FireDown(int player)
    {
        if (player == 0) return BtnADown();
        var m = Mouse.current; var g = Gamepad.current; var k = Keyboard.current;
        bool ms = m != null && m.rightButton.wasPressedThisFrame;
        bool gp = g != null && g.buttonEast.wasPressedThisFrame;
        bool kb = k != null && k.rightCtrlKey.wasPressedThisFrame;
        return ms || gp || kb;
    }

    void Ping(float hz, float seconds, float vol)
    {
        if (meta && meta.audioBus) meta.audioBus.BeepOnce(hz, seconds, vol);
    }
}
