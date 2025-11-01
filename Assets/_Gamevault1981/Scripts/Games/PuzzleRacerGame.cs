// === PuzzleRacerGame.cs (v2) ===
// Pseudo-3D lane runner with equation building: drive over [number] -> [+] -> [number],
// then smash the correct sum. Enhanced road centering, horizon perspective, background,
// simple engine "hum", and on-road signage with depth shading.
//
// Drop-in replacement for previous PuzzleRacerGame.cs.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PuzzleRacerGame : GameManager
{
    // ---------- Tunables ----------
    const int    LANES              = 3;           // -1,0,+1
    const float  LANE_COOLDOWN      = 0.10f;
    const float  BASE_SPEED         = 0.62f;       // world z/sec
    const float  SPEED_STEP         = 0.07f;       // per level
    const float  Z_COLLIDE          = 0.070f;      // hit when <= this
    const float  Z_SPAWN            = 1.12f;       // beyond horizon
    const float  TRAFFIC_TIME       = 0.90f;       // seconds of ambient between phases
    const int    ROUTE_LEN_BASE     = 3;           // solves per level
    const int    SCORE_PER_SOLVE    = 300;

    // Visuals (computed per-frame for center & width from RetroDraw)
    static readonly Color COL_BG_TOP     = new Color(0.04f,0.05f,0.09f,1);
    static readonly Color COL_BG_BOT     = new Color(0.02f,0.02f,0.03f,1);
    static readonly Color COL_MOUNTAIN   = new Color(0.10f,0.20f,0.28f,0.80f);
    static readonly Color COL_ROAD_A     = new Color(0.11f,0.11f,0.16f,1);
    static readonly Color COL_ROAD_B     = new Color(0.13f,0.13f,0.19f,1);
    static readonly Color COL_EDGE       = new Color(0.30f,0.80f,1.00f, 0.95f);
    static readonly Color COL_LANE       = new Color(0.95f,0.95f,1f,0.85f);
    static readonly Color COL_BAD        = new Color(1.00f, 0.35f, 0.35f, 0.95f);
    static readonly Color COL_GOOD       = new Color(0.35f, 1.00f, 0.55f, 0.95f);
    static readonly Color COL_AMBIENT    = new Color(0.92f, 0.92f, 1f, 0.92f);
    static readonly Color COL_UI         = new Color(0.95f, 0.96f, 1f, 1);
    static readonly Color COL_UI_DIM     = new Color(0.85f, 0.88f, 0.96f, 0.85f);

    int   _lane = 0;                // -1..+1
    float _steerHold;
    float _speed;
    int   _level = 1;
    int   _routeLen;
    int   _solvedThisLevel;

    float _phaseTimer;
    Phase _phase;

    Problem _built;     // current a + b
    bool _hasA, _hasPlus, _hasB;

    readonly System.Random _rng = new System.Random();
    readonly List<Token> _tokens = new List<Token>();

    float _engineTimer;

    enum Phase { Traffic, PickA, PickPlus, PickB, Answers }
    enum TKind { Obstacle, AmbientNum, Operand, Plus, Candidate }

    struct Token
    {
        public TKind kind;
        public int lane;    // -1..+1
        public float z;     // 0..1
        public int value;   // number or candidate
        public bool isAnswer;
    }

    struct Problem { public int a, b; public int sum; public override string ToString()=> $"{a}+{b}"; }

    // --- Configurable geometry (recomputed) ---
    int HORIZON_Y, NEAR_Y, CENTER_X, ROAD_NEAR, ROAD_FAR;

    public override void Begin()
    {
        base.Begin();
        if (!meta) meta = MetaGameManager.I;
        Def = Def ?? new GameDef("PuzzleRacer","Puzzle Racer", 6, "Build an equation on the road, then hit the sum.", GameFlags.Solo, typeof(PuzzleRacerGame));
    }

    public override void OnStartMode()
    {
        base.OnStartMode();
        ScoreP1 = 0;

        _lane = 0; _steerHold = 0f;
        _speed = BASE_SPEED;
        _level = 1;
        _routeLen = ROUTE_LEN_BASE;
        _solvedThisLevel = 0;

        _tokens.Clear();
        _phase = Phase.PickA;
        _phaseTimer = 0f;
        _hasA = _hasPlus = _hasB = false;
        _built = new Problem { a = 0, b = 0, sum = 0 };

        Running = true;
    }

    void Update()
    {
        if (!Running) return;
        if (HandleGameOver()) return;
        if (HandlePause()) return;

        float dt = Mathf.Min(0.033f, Time.deltaTime);

        // steering
        HandleSteer(dt);

        // world advance
        StepWorld(dt);

        // spawn by phase
        SpawnByPhase(dt);

        // engine hum
        EngineHum(dt);
    }

    void HandleSteer(float dt)
    {
        _steerHold -= dt;

        int want = _lane;

        var k = Keyboard.current; var g = Gamepad.current;
        bool left  = (k!=null && (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame)) ||
                     (g!=null && (g.dpad.left.wasPressedThisFrame || g.leftStick.left.wasPressedThisFrame));
        bool right = (k!=null && (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame)) ||
                     (g!=null && (g.dpad.right.wasPressedThisFrame || g.leftStick.right.wasPressedThisFrame));

        if (_steerHold <= 0f)
        {
            if (left)  { want = Mathf.Clamp(_lane - 1, -1, +1); _steerHold = LANE_COOLDOWN; }
            if (right) { want = Mathf.Clamp(_lane + 1, -1, +1); _steerHold = LANE_COOLDOWN; }
        }
        _lane = want;
    }

    void StepWorld(float dt)
    {
        float zStep = _speed * dt;

        for (int i = _tokens.Count - 1; i >= 0; i--)
        {
            var t = _tokens[i];
            t.z -= zStep;

            if (t.z <= Z_COLLIDE)
            {
                bool sameLane = (t.lane == _lane);
                if (sameLane)
                {
                    switch (t.kind)
                    {
                        case TKind.Obstacle:
                            Crash(); return;
                        case TKind.AmbientNum:
                            Ping(700f, 0.02f, 0.06f);
                            break;
                        case TKind.Operand:
                            if (!_hasA) { _hasA = true; _built.a = t.value; _phase = Phase.PickPlus; _phaseTimer = 0f; Ping(820,0.05f,0.18f); }
                            else if (!_hasB) { _hasB = true; _built.b = t.value; _built.sum = _built.a + _built.b; _phase = Phase.Traffic; _phaseTimer = 0f; Ping(900,0.05f,0.18f); }
                            break;
                        case TKind.Plus:
                            if (_hasA && !_hasPlus) { _hasPlus = true; _phase = Phase.PickB; _phaseTimer = 0f; Ping(750,0.05f,0.18f); }
                            break;
                        case TKind.Candidate:
                            if (t.isAnswer) Solve();
                            else Crash();
                            return;
                    }
                }
                _tokens.RemoveAt(i);
                continue;
            }

            if (t.z < -0.25f) { _tokens.RemoveAt(i); continue; }
            _tokens[i] = t;
        }
    }

    void SpawnByPhase(float dt)
    {
        _phaseTimer += dt;

        if (_phase == Phase.Traffic)
        {
            // allow some ambient + obstacles, then answers
            if (_phaseTimer < TRAFFIC_TIME)
            {
                if (Rnd() < 1.6f * dt) SpawnAmbient();
                if (Rnd() < (0.7f + 0.12f * _level) * dt) SpawnObstacle();
                return;
            }
            SpawnAnswers();
            _phase = Phase.Answers;
            _phaseTimer = 0f;
            return;
        }

        if (_phase == Phase.Answers)
        {
            // wait until candidates cleared, then start next round
            bool any = _tokens.Exists(t => t.kind == TKind.Candidate);
            if (!any && _phaseTimer >= 0.35f)
            {
                NextRound();
            }
            return;
        }

        // Build steps constantly present 1 "choice set" at horizon, respawn if missed
        if (_phase == Phase.PickA || _phase == Phase.PickB)
        {
            bool hasOperandOnRoad = _tokens.Exists(t => t.kind == TKind.Operand);
            if (!hasOperandOnRoad)
            {
                // spawn three numbers across lanes
                int[] lanes = { -1, 0, +1 };
                Shuffle(lanes);
                for (int i=0;i<3;i++)
                {
                    var tok = new Token { kind=TKind.Operand, lane=lanes[i], z=Z_SPAWN, value=_rng.Next(0,10) };
                    _tokens.Add(tok);
                }
            }
            // random ambient for spice
            if (Rnd() < 0.8f*dt) SpawnAmbient();
            if (Rnd() < (0.4f + 0.10f*_level)*dt) SpawnObstacle();
            return;
        }

        if (_phase == Phase.PickPlus)
        {
            bool hasPlus = _tokens.Exists(t => t.kind == TKind.Plus);
            if (!hasPlus)
            {
                var tok = new Token { kind=TKind.Plus, lane=RandomLane(), z=Z_SPAWN, value=0 };
                _tokens.Add(tok);
            }
            if (Rnd() < 0.6f*dt) SpawnAmbient();
            if (Rnd() < (0.35f + 0.10f*_level)*dt) SpawnObstacle();
        }
    }

    void NextRound()
    {
        _solvedThisLevel = Mathf.Clamp(_solvedThisLevel, 0, _routeLen);
        _hasA = _hasPlus = _hasB = false;
        _built = new Problem();
        _phase = Phase.PickA;
        _phaseTimer = 0f;
    }

    void SpawnAmbient()
    {
        var t = new Token
        {
            kind = TKind.AmbientNum,
            lane = RandomLane(),
            z = Z_SPAWN,
            value = _rng.Next(0, 10),
            isAnswer = false
        };
        _tokens.Add(t);
    }

    void SpawnObstacle()
    {
        int lane = RandomLane(exclude: _lane);
        var t = new Token { kind=TKind.Obstacle, lane=lane, z=Z_SPAWN, value=-1, isAnswer=false };
        _tokens.Add(t);
    }

    void SpawnAnswers()
    {
        int correct = _built.sum;
        int wrong1 = correct + _rng.Next(-8,9);
        int wrong2 = correct + _rng.Next(-8,9);
        if (wrong1 == correct) wrong1 += 1;
        if (wrong2 == correct || wrong2 == wrong1) wrong2 -= 2;

        int[] values = { correct, wrong1, wrong2 };
        Shuffle(values);
        int[] lanes = { -1, 0, +1 };
        Shuffle(lanes);

        for (int i=0;i<3;i++)
        {
            _tokens.Add(new Token{ kind=TKind.Candidate, lane=lanes[i], z=Z_SPAWN, value=values[i], isAnswer=(values[i]==correct)});
        }
    }

    void Solve()
    {
        ScoreP1 += SCORE_PER_SOLVE + (_level - 1) * 35;
        _solvedThisLevel++;
        Ping(1050f, 0.10f, 0.25f);

        if (_solvedThisLevel >= _routeLen)
        {
            _level++;
            _solvedThisLevel = 0;
            _routeLen = ROUTE_LEN_BASE + Mathf.Min(8, _level/2);
            _speed = BASE_SPEED + (_level-1) * SPEED_STEP;
            Ping(1250f, 0.08f, 0.30f);
        }

        // clear candidates
        for (int i=_tokens.Count-1;i>=0;i--) if (_tokens[i].kind==TKind.Candidate) _tokens.RemoveAt(i);

        _phase = Phase.Traffic;
        _phaseTimer = 0f;
    }

    void Crash()
    {
        Ping(180f, 0.12f, 0.37f);
        GameOverNow();
    }

    // ---------- Drawing ----------
    void OnGUI()
    {
        if (!Running) return;

        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;
        RetroDraw.Begin(sw, sh);

        // geometry recompute
        ComputeGeo(sw, sh);

        // background gradient + mountains
        DrawBackground(sw, sh);

        // road strips
        DrawRoad(sw, sh);

        // tokens
        DrawTokens(sw, sh);

        // car
        DrawCar(sw, sh);

        // HUD
        DrawHUD(sw, sh);

        DrawCommonHUD(sw, sh);
    }

    void ComputeGeo(int sw, int sh)
    {
        HORIZON_Y = Mathf.RoundToInt(sh * 0.78f);
        NEAR_Y    = Mathf.RoundToInt(sh * 0.16f);
        CENTER_X  = sw / 2;
        ROAD_NEAR = Mathf.RoundToInt(sw * 0.70f);
        ROAD_FAR  = Mathf.RoundToInt(sw * 0.18f);
    }

    void DrawBackground(int sw, int sh)
    {
        // simple vertical grad
        for (int y=0; y<sh; y+=2)
        {
            float t = y / (float)sh;
            Color c = Color.Lerp(COL_BG_BOT, COL_BG_TOP, t);
            RetroDraw.PixelRect(0, y, sw, 2, sw, sh, c);
        }
        // mountains
        int baseY = HORIZON_Y + 6;
        for (int i=0;i<3;i++)
        {
            int w = Mathf.RoundToInt(sw * (0.25f + 0.12f*i));
            int h = Mathf.RoundToInt(20 + 12*i);
            int x = Mathf.RoundToInt(CENTER_X + (i-1)*w*0.62f - w/2f);
            RetroDraw.PixelRect(x, baseY, w, 2, sw, sh, COL_MOUNTAIN);
            // stepped triangle fill
            for (int s=0; s<h; s+=2)
            {
                float t = 1f - s/(float)h;
                int segW = Mathf.RoundToInt(w * t);
                int sx = x + (w - segW)/2;
                RetroDraw.PixelRect(sx, baseY - s, segW, 2, sw, sh, new Color(COL_MOUNTAIN.r, COL_MOUNTAIN.g, COL_MOUNTAIN.b, 0.35f + 0.4f*t));
            }
        }
    }

    void DrawRoad(int sw, int sh)
    {
        int step = 3;
        for (int y = NEAR_Y; y <= HORIZON_Y; y += step)
        {
            float t = Mathf.InverseLerp(NEAR_Y, HORIZON_Y, y);
            int w = Mathf.RoundToInt(Mathf.Lerp(ROAD_NEAR, ROAD_FAR, t));
            int x = CENTER_X - (w / 2);
            Color c = ((y / step) % 2 == 0) ? COL_ROAD_A : COL_ROAD_B;
            RetroDraw.PixelRect(x, y, w, step, sw, sh, c);

            // center dashed lane
            if (((y / (step*2)) % 2) == 0)
                RetroDraw.PixelRect(CENTER_X - 1, y, 2, 2, sw, sh, COL_LANE);

            // edges glow
            RetroDraw.PixelRect(x, y, 1, step, sw, sh, COL_EDGE);
            RetroDraw.PixelRect(x + w - 1, y, 1, step, sw, sh, COL_EDGE);
        }
    }

    (int,int,int) LaneToScreen(int lane, float z)
    {
        // returns (x,y, laneWidth)
        int y = Mathf.RoundToInt(Mathf.Lerp(NEAR_Y, HORIZON_Y, Mathf.Clamp01(z)));
        int roadW = Mathf.RoundToInt(Mathf.Lerp(ROAD_NEAR, ROAD_FAR, Mathf.InverseLerp(NEAR_Y, HORIZON_Y, y)));
        int laneW = roadW / LANES;
        int laneCenter = CENTER_X + lane * (laneW / 2);
        return (laneCenter, y, laneW);
    }

    void DrawTokens(int sw, int sh)
    {
        foreach (var t in _tokens)
        {
            var (cx, y, laneW) = LaneToScreen(t.lane, t.z);

            int tokW = Mathf.Max(12, Mathf.RoundToInt(laneW * 0.44f));
            int tokH = Mathf.Max(9, Mathf.RoundToInt(tokW * 0.65f));
            int x = cx - tokW/2;

            Color baseC =
                t.kind == TKind.Obstacle  ? COL_BAD :
                t.kind == TKind.Candidate ? (t.isAnswer ? COL_GOOD : new Color(1f,0.65f,0.35f,0.95f)) :
                t.kind == TKind.Plus      ? new Color(0.95f,0.95f,0.65f,0.95f) :
                t.kind == TKind.Operand   ? new Color(0.65f,0.95f,0.80f,0.95f) :
                                             COL_AMBIENT;

            // cart sign with a tiny "tilt" shading
            RetroDraw.PixelRect(x, y, tokW, tokH, sw, sh, baseC);
            RetroDraw.PixelRect(x+1, y+1, tokW-2, tokH-2, sw, sh, new Color(0,0,0,0.2f));
            RetroDraw.PixelRect(x, y+tokH-2, tokW, 2, sw, sh, new Color(1,1,1,0.08f));

            string label =
                t.kind == TKind.Plus ? "+" :
                t.kind == TKind.Obstacle ? "■" :
                $"{t.value}";

            int tw = label.Length*5;
            RetroDraw.PrintSmall(cx - tw/2 + 1, y + (tokH/2) - 3, label, sw, sh, Color.black);
            RetroDraw.PrintSmall(cx - tw/2,     y + (tokH/2) - 4, label, sw, sh, Color.white);
        }
    }

    void DrawCar(int sw, int sh)
    {
        var (cx, y, laneW) = LaneToScreen(_lane, 0f);
        int cW = Mathf.RoundToInt(Mathf.Lerp(16, 22, 0.5f));
        int cH = 11;
        int x = cx - cW/2;
        int yy = NEAR_Y - 5;

        // body
        Color body = new Color(0.38f, 0.92f, 0.78f, 1);
        RetroDraw.PixelRect(x, yy, cW, cH, sw, sh, body);
        RetroDraw.PixelRect(x+2, yy+2, cW-4, cH-4, sw, sh, new Color(0,0,0,0.25f));
        // windshield
        RetroDraw.PixelRect(x+4, yy+2, cW-8, 3, sw, sh, new Color(0.85f,0.95f,1f,0.7f));
        // wheels
        RetroDraw.PixelRect(x+1, yy-1, 4, 2, sw, sh, new Color(0,0,0,0.6f));
        RetroDraw.PixelRect(x+cW-5, yy-1, 4, 2, sw, sh, new Color(0,0,0,0.6f));
        // headlights
        RetroDraw.PixelRect(x, yy+1, 2, cH-2, sw, sh, new Color(1f,1f,0.7f,0.5f));
        RetroDraw.PixelRect(x+cW-2, yy+1, 2, cH-2, sw, sh, new Color(1f,1f,0.7f,0.5f));
    }

    void DrawHUD(int sw, int sh)
    {
        string build =
            !_hasA ? "PICK A NUMBER" :
            !_hasPlus ? "DRIVE OVER +" :
            !_hasB ? "PICK SECOND NUMBER" :
            "HIT THE SUM";

        int mw = build.Length*5;
        RetroDraw.PrintSmall(CENTER_X - mw/2 + 1, HORIZON_Y + 10, build, sw, sh, new Color(0,0,0,0.35f));
        RetroDraw.PrintSmall(CENTER_X - mw/2    , HORIZON_Y + 11, build, sw, sh, new Color(1,1,0.75f,0.95f));

        string lv = $"LV {_level}   ROUTE {_solvedThisLevel}/{_routeLen}";
        RetroDraw.PrintSmall(6, sh-12, lv, sw, sh, COL_UI_DIM);

        // equation
        string eq = $"{(_hasA?_built.a:"?")} + {(_hasB?_built.b:"?")} = {(_phase==Phase.Answers?"?":"")}";
        int tw = eq.Length*5;
        RetroDraw.PrintSmall(sw - (tw + 6), sh-12, eq, sw, sh, COL_UI);
    }

    void EngineHum(float dt)
    {
        _engineTimer -= dt;
        if (_engineTimer <= 0f)
        {
            float rate = 7f + _speed*6f;
            _engineTimer = 1f / rate;
            float hz = 180f + _speed*120f + (_rng.Next(-8,9));
            Ping(hz, 0.015f, 0.08f);
        }
    }

    // ---------- Helpers ----------
    int RandomLane(int? exclude=null)
    {
        int[] lanes = { -1,0,+1 };
        if (exclude.HasValue)
        {
            int ex = exclude.Value;
            var pool = new System.Collections.Generic.List<int>();
            foreach (var l in lanes) if (l!=ex) pool.Add(l);
            return pool[_rng.Next(pool.Count)];
        }
        return lanes[_rng.Next(lanes.Length)];
    }
    float Rnd() => (float)_rng.NextDouble();
    void Shuffle<T>(IList<T> a)
    {
        for (int i=a.Count-1;i>0;i--)
        { int j=_rng.Next(i+1); T t=a[i]; a[i]=a[j]; a[j]=t; }
    }

    void Ping(float hz, float seconds, float vol)
    {
        if (meta && meta.audioBus) meta.audioBus.BeepOnce(hz, seconds, vol);
    }
}
