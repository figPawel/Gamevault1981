using UnityEngine;

public enum GameMode { Solo = 0, Versus2P = 1, Coop2P = 2, Alt2P = 3 }

public abstract class GameManager : MonoBehaviour
{
    public MetaGameManager meta;
    public GameDef Def;
    public GameMode Mode;

    public int ScoreP1, ScoreP2;
    public bool Running { get; protected set; }

    // Pause quit confirmation (kept)
    bool _quitConfirmArmed = false;
    float _quitConfirmTimer = 0f;
    const float QuitConfirmWindow = 10f;

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

        // Return to selection & re-enable UI
        meta?.QuitToSelection();
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

        // Back quits from GAME OVER
        if (BackPressedUI())
        {
            QuitToMenu();
            return true;
        }

        // Fire to restart
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

        // Fire resumes
        if (BtnADown())
        {
            Paused = false;
            _quitConfirmArmed = false;
            _pauseCooldown = 0.2f;
            return true;
        }

        // Back requires confirm while paused (visual shown in DrawCommonHUD)
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
            RetroDraw.PrintBig(cx - titleW / 2, cy - 4, "PAUSED", sw, sh, new Color(1f, 1f, 0.8f, 1));

            // If Back was pressed once, show blinking confirmation with countdown
            if (_quitConfirmArmed)
            {
                float t = Mathf.Clamp01(_quitConfirmTimer / QuitConfirmWindow);
                // Blink alpha while counting down
                float a = 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f));
                var col = new Color(1f, 0.6f, 0.6f, a);

                string msg = "PRESS BACK AGAIN TO QUIT";
                int w = msg.Length * SMALL_W;
                RetroDraw.PrintSmall(cx - w / 2, cy - 24, msg, sw, sh, col);

                // Optional tiny countdown indicator under it
                string secs = $"{_quitConfirmTimer:0.0}s";
                int w2 = secs.Length * SMALL_W;
                RetroDraw.PrintSmall(cx - w2 / 2, cy - 32, secs, sw, sh, new Color(1f, 0.75f, 0.75f, a));
            }
            else
            {
                // Normal hint when not in confirm mode
                int hintW = HINT.Length * SMALL_W;
                RetroDraw.PrintSmall(cx - hintW / 2, cy - 16, HINT, sw, sh, new Color(0.85f, 0.9f, 1f, 1));
            }
        }
    }
}
