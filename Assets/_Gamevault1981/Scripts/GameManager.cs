// === GameManager.cs ===
using UnityEngine;
using UnityEngine.InputSystem;

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
        Paused = false;
        _gameOver = false;
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

    // ---------------- A / FIRE ----------------
    protected bool BtnA(int p = 1)
    {
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool kb = k != null && (
            k.spaceKey.isPressed || k.enterKey.isPressed ||
            k.eKey.isPressed || k.rKey.isPressed ||
            k.gKey.isPressed || k.hKey.isPressed ||
            k.zKey.isPressed || k.xKey.isPressed ||
            k.leftCtrlKey.isPressed
        );
        bool ms = m != null && m.leftButton.isPressed;
        bool gp = g != null && (
            g.buttonSouth.isPressed || g.buttonWest.isPressed || g.buttonNorth.isPressed ||
            g.rightTrigger.ReadValue() > 0.3f || g.leftTrigger.ReadValue() > 0.3f ||
            g.leftShoulder.isPressed || g.rightShoulder.isPressed
        );
        return kb || ms || gp;
    }

    protected bool BtnADown(int p = 1)
    {
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool kb = k != null && (
            k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame ||
            k.eKey.wasPressedThisFrame || k.rKey.wasPressedThisFrame ||
            k.gKey.wasPressedThisFrame || k.hKey.wasPressedThisFrame ||
            k.zKey.wasPressedThisFrame || k.xKey.wasPressedThisFrame ||
            k.leftCtrlKey.wasPressedThisFrame
        );
        bool ms = m != null && m.leftButton.wasPressedThisFrame;
        bool gp = g != null && (
            g.buttonSouth.wasPressedThisFrame ||
            g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame ||
            g.rightTrigger.wasPressedThisFrame || g.leftTrigger.wasPressedThisFrame ||
            g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame
        );
        return kb || ms || gp;
    }

    // ---------------- BACK ----------------
    protected bool BackPressed()
    {
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool kb = k != null && k.backspaceKey.isPressed;
        bool gp = g != null && g.buttonEast.isPressed;
        bool ms = m != null && m.rightButton.isPressed;
        return kb || gp || ms;
    }

    protected bool BackPressedUI()
    {
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool kb = k != null && k.backspaceKey.wasPressedThisFrame;
        bool gp = g != null && g.buttonEast.wasPressedThisFrame;
        bool ms = m != null && m.rightButton.wasPressedThisFrame;
        return kb || gp || ms;
    }

    // ---------------- PAUSE ----------------
    protected bool PausePressed()
    {
        var k = Keyboard.current; var g = Gamepad.current;
        return (k != null && (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame))
            || (g != null && (g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame));
    }

    // ---------------- HANDLERS ----------------

    // 1. Game Over
    protected bool HandleGameOver()
    {
        if (!Running) return true;
        if (!_gameOver) return false;

        // Apply cooldown before accepting Fire after unpause
        _pauseCooldown = Mathf.Max(0f, _pauseCooldown - Time.unscaledDeltaTime);
        if (_pauseCooldown > 0f) return true;

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

    // 2. Pause
    protected bool HandlePause()
    {
        if (!Running) return true;

        _pauseCooldown = Mathf.Max(0f, _pauseCooldown - Time.unscaledDeltaTime);

        if (_quitConfirmArmed)
        {
            _quitConfirmTimer -= Time.unscaledDeltaTime;
            if (_quitConfirmTimer <= 0f) _quitConfirmArmed = false;
        }

        // Toggle pause
        if (_pauseCooldown <= 0f && PausePressed())
        {
            Paused = !Paused;
            _quitConfirmArmed = false;
            _pauseCooldown = 0.15f;
            return true;
        }

        if (!Paused) return false;

        // Paused HUD controls
        if (BtnADown())
        {
            Paused = false;
            _quitConfirmArmed = false;
            _pauseCooldown = 0.2f; // prevent input leak
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

    // ---------- HUD ----------
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

            if (_quitConfirmArmed)
            {
                const string CONFIRM = "PRESS BACK AGAIN TO QUIT";
                int cW = CONFIRM.Length * SMALL_W;
                RetroDraw.PrintSmall(cx - cW / 2, cy - 28, CONFIRM, sw, sh, new Color(1f, 0.85f, 0.85f, 1));
            }
        }
    }
}
