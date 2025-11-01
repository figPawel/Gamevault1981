// GameManager.cs — FULL FILE (now reads input via InputManager)
using UnityEngine;

public enum GameMode { Solo = 0, Versus2P = 1, Coop2P = 2, Alt2P = 3 }

public abstract class GameManager : MonoBehaviour
{
    public MetaGameManager meta;
    public GameDef Def;
    public GameMode Mode;

    public int ScoreP1, ScoreP2;
    public bool Running { get; protected set; }

    bool _quitConfirmArmed = false;
    float _quitConfirmTimer = 0f;
    const float QuitConfirmWindow = 1.25f;

    protected bool Paused;
    bool _gameOver;
    float _pauseCooldown;

    public virtual void Begin() { }
    public virtual void OnStartMode() { }
    public virtual void StopMode() { Running = false; }
    public virtual void ResetScores() { ScoreP1 = 0; ScoreP2 = 0; }

    public virtual void StartMode(GameMode mode)
    {
        Mode = mode;
        Running = true;
        Paused = false;
        _gameOver = false;
        _pauseCooldown = 0f;
        _quitConfirmArmed = false;
        OnStartMode();
    }

public virtual void QuitToMenu()
{
    if (meta)
        meta.ReportRun(Def, Mode, ScoreP1, ScoreP2);

    Running = false;
    Paused  = false;
    _gameOver = false;


    meta?.ui?.BindInGameMenu(this);
}

    protected void GameOverNow()
    {
        if (_gameOver) return;
        _gameOver = true;
        _quitConfirmArmed = false;
        Paused = false;

        if (meta && Def != null)
            meta.ReportRun(Def, Mode, ScoreP1, ScoreP2);
    }

    // --------------- Input wrappers (via InputManager) ---------------
    protected Vector2 Move(int p = 1) => InputManager.I ? InputManager.I.Move(p) : Vector2.zero;
    protected bool BtnA(int p = 1)    => InputManager.I && InputManager.I.Fire(p);
    protected bool BtnADown(int p=1)  => InputManager.I && InputManager.I.FireDown(p);

    protected bool PausePressed()
    {
        if (!InputManager.I) return false;
        return InputManager.I.UIPauseDown();
    }

    // Back is used ONLY inside pause as a double-press-to-quit.
    protected bool BackPressedUI()
    {
        if (!InputManager.I) return false;
        return InputManager.I.UIBackDown();
    }

    // --------------- Pause/GameOver handlers ---------------
   protected bool HandleGameOver()
{
    if (!Running) return true;
    if (!_gameOver) return false;

    _pauseCooldown = Mathf.Max(0f, _pauseCooldown - Time.unscaledDeltaTime);
    if (_pauseCooldown > 0f) return true;

    // NEW: allow Back to quit from GAME OVER
    if (BackPressedUI())
    {
        QuitToMenu();
        return true;
    }

    // Existing: Fire to continue
    if (BtnADown())
    {
        _gameOver = false;
        Paused = false;
        _quitConfirmArmed = false;
        _pauseCooldown = 1.0f;
        OnStartMode();
    }
    return true;
}


    protected bool HandlePause()
    {
        if (!Running) return true;

        _pauseCooldown = Mathf.Max(0f, _pauseCooldown - Time.unscaledDeltaTime);

        if (_quitConfirmArmed)
        {
            _quitConfirmTimer -= Time.unscaledDeltaTime;
            if (_quitConfirmTimer <= 0f) _quitConfirmArmed = false;
        }

        if (_pauseCooldown <= 0f && PausePressed())
        {
            Paused = !Paused;
            _quitConfirmArmed = false;
            _pauseCooldown = 0.15f;
            return true;
        }

        if (!Paused) return false;

        if (BtnADown())
        {
            Paused = false;
            _quitConfirmArmed = false;
            _pauseCooldown = 0.2f;
            return true;
        }

        if (BackPressedUI())
        {
            if (!_quitConfirmArmed)
            {
                _quitConfirmArmed = true;
                _quitConfirmTimer = QuitConfirmWindow;
                return true;
            }

            _quitConfirmArmed = false;
            QuitToMenu();
            return true;
        }

        return true;
    }

    protected void DrawCommonHUD(int sw, int sh)
    {
        RetroDraw.PrintSmall(6, RetroDraw.ViewH - 10, $"SCORE {ScoreP1:0000}", sw, sh, Color.white);

        int vw = RetroDraw.ViewW, vh = RetroDraw.ViewH;
        const int BIG_W = 8, SMALL_W = 5;
        const string HINT = "FIRE: CONTINUE  BACK: QUIT";

        int cx = vw / 2, cy = vh / 2;

        if (_gameOver)
        {
            int titleW = "GAME OVER".Length * BIG_W;
            int hintW = HINT.Length * SMALL_W;
            RetroDraw.PrintBig(cx - titleW / 2, cy - 4, "GAME OVER", sw, sh, Color.white);
            RetroDraw.PrintSmall(cx - hintW / 2, cy - 16, HINT, sw, sh, new Color(0.9f, 0.9f, 1f, 1));
            return;
        }

        if (Paused)
        {
            int titleW = "PAUSED".Length * BIG_W;
            int hintW = HINT.Length * SMALL_W;
            RetroDraw.PrintBig(cx - titleW / 2, cy - 4, "PAUSED", sw, sh, new Color(1f, 1f, 0.8f, 1));
            RetroDraw.PrintSmall(cx - hintW / 2, cy - 16, HINT, sw, sh, new Color(0.85f, 0.9f, 1f, 1));
        }
    }
}
