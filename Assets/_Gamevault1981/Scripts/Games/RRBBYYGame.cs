// === RRBBYYGame.cs (fix: Fire via GameManager, saner spawns) ===
// 1P color-matcher. Move freely; press Fire (BtnADown) to cycle R->B->Y.
// Matching eats blocks for points/combos; wrong color or a hit ends the run.
// Blocks that touch ground drain HP. Stage ramps speed and spawn rate.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class RRBBYYGame : GameManager
{
    // --- Tunables ---
    const float PLAYER_SPEED       = 85f;
    const float PLAYER_SIZE        = 12f;
    const float BLOCK_SIZE         = 12f;

    const float BASE_FALL_SPEED    = 22f;
    const float SPEED_PER_STAGE    = 4.5f;

    const float SPAWN_EVERY_BASE   = 0.85f;   // seconds between bursts (base)
    const float SPAWN_STEP         = 0.06f;   // faster per stage
    const float SPAWN_EVERY_MIN    = 0.45f;   // absolute floor so it never becomes chaos

    const int   MAX_ACTIVE_BASE    = 7;       // active block cap baseline
    const int   MAX_ACTIVE_STEP    = 2;       // +2 capacity every ~2 stages

    const float GROUND_Y_PAD       = 8f;
    const int   SCORE_BASE         = 50;
    const int   HP_MAX             = 100;
    const int   HP_LOSS_MISS       = 8;

    // Palette
    static readonly Color COL_BG    = new Color(0.06f,0.06f,0.09f,1);
    static readonly Color COL_UI    = new Color(0.95f,0.96f,1f,1);
    static readonly Color COL_DIM   = new Color(0.85f,0.88f,0.96f,0.9f);
    static readonly Color COL_HP_BG = new Color(0.15f,0.18f,0.24f,1);
    static readonly Color COL_HP    = new Color(0.30f,0.95f,0.55f,1);
    static readonly Color COL_HP_BAD= new Color(0.95f,0.35f,0.35f,1);
    static readonly Color COL_FOAM  = new Color(1f,1f,1f,0.06f);

    static readonly Color COL_RED   = new Color(0.95f,0.35f,0.35f,1);
    static readonly Color COL_BLUE  = new Color(0.35f,0.60f,0.95f,1);
    static readonly Color COL_YELL  = new Color(0.98f,0.90f,0.35f,1);

    // State
    Vector2 player;
    int playerColor = 0; // 0=R,1=B,2=Y
    float spawnTimer;
    int stage = 1;
    float fallSpeed;
    float spawnEvery;

    int combo = 0;
    int lastColorForCombo = -1;

    int hp = HP_MAX;
    float timeSinceStart;

    readonly System.Random rng = new System.Random();

    struct Block { public float x,y; public int color; public float vy; public bool lethal; }
    List<Block> blocks = new List<Block>();

    struct Floater { public float x,y; public float vy; public float life; public string text; public Color c; }
    List<Floater> floaters = new List<Floater>();

    public override void Begin()
    {
        base.Begin();
        if (!meta) meta = MetaGameManager.I;
        Def = Def ?? new GameDef("RRBBYY","RRBBYY", 7, "Cycle colors and chomp falling blocks.", GameFlags.Solo, typeof(RRBBYYGame));
    }

    public override void OnStartMode()
    {
        base.OnStartMode();
        ResetScores();
        Running = true;

        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;
        player = new Vector2(sw/2, sh*0.30f);
        playerColor = 0;
        stage = 1;

        fallSpeed  = BASE_FALL_SPEED;
        spawnEvery = SPAWN_EVERY_BASE;

        combo = 0; lastColorForCombo = -1;
        blocks.Clear(); floaters.Clear();
        hp = HP_MAX; timeSinceStart = 0f; spawnTimer = 0f;
    }

    void Update()
    {
        if (!Running) return;
        if (HandleGameOver()) return;
        if (HandlePause()) return;

        float dt = Mathf.Min(0.033f, Time.deltaTime);
        timeSinceStart += dt;

        // --- Movement (free) ---
        Vector2 move = Vector2.zero;
        var k = Keyboard.current; var g = Gamepad.current;
        if (k != null)
        {
            if (k.leftArrowKey.isPressed || k.aKey.isPressed)  move.x -= 1;
            if (k.rightArrowKey.isPressed|| k.dKey.isPressed)  move.x += 1;
            if (k.downArrowKey.isPressed || k.sKey.isPressed)  move.y -= 1;
            if (k.upArrowKey.isPressed   || k.wKey.isPressed)  move.y += 1;
        }
        if (g != null) move += g.leftStick.ReadValue();
        if (move.sqrMagnitude > 1f) move.Normalize();

        player += move * (PLAYER_SPEED * dt);

        // Clamp to screen
        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;
        float minX = PLAYER_SIZE*0.5f + 2, maxX = sw - (PLAYER_SIZE*0.5f + 2);
        float minY = PLAYER_SIZE*0.5f + 2, maxY = sh - (PLAYER_SIZE*0.5f + 10);
        player.x = Mathf.Clamp(player.x, minX, maxX);
        player.y = Mathf.Clamp(player.y, minY, maxY);

        // --- Fire: use GameManager helpers so LMB etc. work everywhere ---
        if (BtnADown()) CycleColor();

        // --- Spawning with cap ---
        spawnTimer += dt;
        if (spawnTimer >= spawnEvery)
        {
            spawnTimer = 0f;
            SpawnBurst(stage);
        }

        // --- Advance blocks ---
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            var b = blocks[i];
            b.y -= b.vy * dt;

            // collision
            float dx = Mathf.Abs(b.x - player.x);
            float dy = Mathf.Abs(b.y - player.y);
            float hitDist = (PLAYER_SIZE + BLOCK_SIZE) * 0.5f;
            if (dx < hitDist && dy < hitDist)
            {
                if (b.color == playerColor) { EatBlock(i, b); continue; }
                Crash(); return; // wrong color
            }

            // ground hit -> HP loss
            if (b.y <= GROUND_Y_PAD + BLOCK_SIZE*0.5f)
            {
                blocks.RemoveAt(i);
                LoseHP(HP_LOSS_MISS);
                continue;
            }

            blocks[i] = b;
        }

        // --- Stage ramp ---
        if (timeSinceStart > 15f + stage * 8f)
        {
            stage++;
            fallSpeed += SPEED_PER_STAGE;
            spawnEvery = Mathf.Max(SPAWN_EVERY_MIN, SPAWN_EVERY_BASE - SPAWN_STEP * stage);
            Ping(900, 0.08f, 0.25f);
        }

        // --- Floaters ---
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            var f = floaters[i];
            f.y += f.vy * dt;
            f.life -= dt;
            if (f.life <= 0) { floaters.RemoveAt(i); continue; }
            floaters[i] = f;
        }
    }

    // Cap grows slowly with stage; prevents on-screen floods.
    int ActiveCap() => MAX_ACTIVE_BASE + ((stage - 1) / 2) * MAX_ACTIVE_STEP;

    void SpawnBurst(int stg)
    {
        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;
        float xmargin = 12f;

        // burst size scales gently: 1..2 at start, up to 3..4 later
        int maxBurst = Mathf.Clamp(2 + stg / 3, 2, 4);
        int want = rng.Next(1, maxBurst + 1);

        // respect active cap
        int room = ActiveCap() - blocks.Count;
        if (room <= 0) return;
        int count = Mathf.Min(want, room);

        for (int i = 0; i < count; i++)
        {
            float x = Mathf.Lerp(xmargin, sw - xmargin, (float)rng.NextDouble());
            float y = sh + BLOCK_SIZE + i * 10f;
            int c = rng.Next(0, 3);
            var b = new Block {
                x = x, y = y,
                vy = fallSpeed * (0.92f + 0.18f*(float)rng.NextDouble()),
                color = c, lethal = true
            };
            blocks.Add(b);
        }
    }

    void EatBlock(int index, Block b)
    {
        blocks.RemoveAt(index);

        // combo
        if (b.color == lastColorForCombo) combo++; else { combo = 1; lastColorForCombo = b.color; }

        int gained = SCORE_BASE * combo;
        ScoreP1 += gained;

        var fc = (b.color==0?COL_RED:(b.color==1?COL_BLUE:COL_YELL));
        floaters.Add(new Floater { x=b.x, y=b.y + 6f, vy=18f, life=0.9f, text=$"+{gained}", c=fc });

        Ping(500 + b.color*120 + combo*25, 0.06f, 0.20f);
    }

    void LoseHP(int amt)
    {
        hp -= amt;
        if (hp <= 0) { Crash(); return; }
        Ping(220, 0.04f, 0.20f);
    }

    void Crash()
    {
        Ping(160, 0.12f, 0.35f);
        GameOverNow();
    }

    void CycleColor()
    {
        playerColor = (playerColor + 1) % 3;
        Ping(700 + playerColor*120, 0.035f, 0.18f);
    }

    // --- Drawing ---
    void OnGUI()
    {
        if (!Running) return;
        int sw = RetroDraw.ViewW, sh = RetroDraw.ViewH;
        RetroDraw.Begin(sw, sh);

        // bg
        RetroDraw.PixelRect(0, 0, sw, sh, sw, sh, COL_BG);
        RetroDraw.PixelRect(0, 0, sw, 2, sw, sh, new Color(1,1,1,0.02f));
        RetroDraw.PixelRect(0, 3, sw, 1, sw, sh, COL_FOAM);

        // blocks
        foreach (var b in blocks)
        {
            Color bc = (b.color==0?COL_RED:(b.color==1?COL_BLUE:COL_YELL));
            DrawBlock(b.x, b.y, bc, sw, sh);
        }

        // player
        Color pc = (playerColor==0?COL_RED:(playerColor==1?COL_BLUE:COL_YELL));
        DrawPlayer(player.x, player.y, pc, sw, sh);

        // floaters
        for (int i=0;i<floaters.Count;i++)
        {
            var f = floaters[i];
            int tx = Mathf.RoundToInt(f.x) - (f.text.Length*5)/2;
            int ty = Mathf.RoundToInt(f.y);
            var c = new Color(f.c.r, f.c.g, f.c.b, Mathf.Clamp01(f.life));
            RetroDraw.PrintSmall(tx+1, ty-1, f.text, sw, sh, new Color(0,0,0,0.35f));
            RetroDraw.PrintSmall(tx, ty, f.text, sw, sh, c);
        }

        DrawHUD(sw, sh);
        DrawCommonHUD(sw, sh);
    }

    void DrawHUD(int sw, int sh)
    {
        // Stage
        string st = $"STAGE {stage}";
        RetroDraw.PrintSmall(6, sh-12, st, sw, sh, COL_DIM);

        // HP bar
        int barW = 70, barH = 6;
        int x = sw - (barW + 8), y = sh - (barH + 8);
        RetroDraw.PixelRect(x, y, barW, barH, sw, sh, COL_HP_BG);
        float t = Mathf.Clamp01(hp / (float)HP_MAX);
        int fill = Mathf.RoundToInt(t * (barW-2));
        Color fillC = t < 0.35f ? COL_HP_BAD : COL_HP;
        RetroDraw.PixelRect(x+1, y+1, fill, barH-2, sw, sh, fillC);
        RetroDraw.PrintSmall(x, y-10, "HP", sw, sh, COL_DIM);
    }

    void DrawBlock(float cx, float cy, Color c, int sw, int sh)
    {
        int w = Mathf.RoundToInt(BLOCK_SIZE), h = w;
        int x = Mathf.RoundToInt(cx - w/2f);
        int y = Mathf.RoundToInt(cy - h/2f);
        RetroDraw.PixelRect(x, y, w, h, sw, sh, c);
        RetroDraw.PixelRect(x+1, y+1, w-2, h-2, sw, sh, new Color(0,0,0,0.2f));
        RetroDraw.PixelRect(x, y+h-2, w, 2, sw, sh, new Color(1,1,1,0.08f));
    }

    void DrawPlayer(float cx, float cy, Color c, int sw, int sh)
    {
        int w = Mathf.RoundToInt(PLAYER_SIZE), h = w;
        int x = Mathf.RoundToInt(cx - w/2f);
        int y = Mathf.RoundToInt(cy - h/2f);
        RetroDraw.PixelRect(x, y, w, h, sw, sh, c);
        RetroDraw.PixelRect(x+2, y+2, w-4, h-4, sw, sh, new Color(0,0,0,0.25f));
        RetroDraw.PixelRect(x, y, w, 1, sw, sh, new Color(1,1,1,0.25f));
        RetroDraw.PixelRect(x, y, 1, h, sw, sh, new Color(1,1,1,0.15f));
    }

    void Ping(float hz, float seconds, float vol)
    {
        if (meta && meta.audioBus) meta.audioBus.BeepOnce(hz, seconds, vol);
    }
}
