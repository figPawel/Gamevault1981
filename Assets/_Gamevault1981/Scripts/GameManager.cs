using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum GameMode { Solo, Versus2P, Coop2P, Alt2P }

public abstract class GameManager : MonoBehaviour
{
    public MetaGameManager meta;
    public GameDef Def;
    public GameMode Mode;
    public int ScoreP1, ScoreP2;
    public bool Running { get; protected set; }

    // Pause
    protected bool Paused;
    float _pauseCooldown;

    // GUI latches: make Back/A work even when read inside OnGUI (no missed frames)
    bool _guiBackLatchDown, _guiBackLatchHeld;
    bool _guiALatchDown,    _guiALatchHeld;

    public virtual void Begin() { }
    public virtual void OnStartMode() { }
    public virtual void StopMode()  { Running = false; }
    public virtual void ResetScores(){ ScoreP1 = 0; ScoreP2 = 0; }

    public virtual void StartMode(GameMode mode)
    {
        Mode = mode;
        Running = true;
        Paused  = false;
        _pauseCooldown = 0f;
        OnStartMode();
    }

    public virtual void QuitToMenu()
    {
        Running = false;
        Paused  = false;
        if (meta) meta.QuitToSelection();
    }

    // ---------- Input helpers (both backends) ----------
    protected bool BtnA(int p = 1)
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current;
        bool kb = p == 1 ? (k != null && (k.zKey.isPressed || k.leftCtrlKey.isPressed))
                         : (k != null &&  k.commaKey.isPressed);
        bool gp = g != null && (p == 1 ? g.buttonSouth.isPressed : g.buttonEast.isPressed);
        return kb || gp;
    #else
        return p == 1 ? Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.LeftControl)
                      : Input.GetKey(KeyCode.Comma);
    #endif
    }

    protected bool BtnADown(int p = 1)
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current;
        bool kb = p == 1 ? (k != null && (k.zKey.wasPressedThisFrame || k.leftCtrlKey.wasPressedThisFrame))
                         : (k != null &&  k.commaKey.wasPressedThisFrame);
        bool gp = g != null && (p == 1 ? g.buttonSouth.wasPressedThisFrame : g.buttonEast.wasPressedThisFrame);
        return kb || gp;
    #else
        return p == 1 ? Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.LeftControl)
                      : Input.GetKeyDown(KeyCode.Comma);
    #endif
    }

    // GUI-safe edge for A (works even when Update early-outs)
    protected bool BtnADownUI(int p = 1)
    {
        bool held = BtnA(p);
        bool edge = held && !_guiALatchHeld;
        _guiALatchHeld = held;
        if (edge) _guiALatchDown = true;
        bool fired = _guiALatchDown;
        _guiALatchDown = false;
        return fired;
    }

    protected bool PausePressed()
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current;
        return (k != null && (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame))
            || (g != null && (g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame));
    #else
        return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);
    #endif
    }

    protected bool BackPressed()
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current;
        return (k != null && k.backspaceKey.wasPressedThisFrame)
            || (g != null && g.selectButton.wasPressedThisFrame);
    #else
        return Input.GetKeyDown(KeyCode.Backspace);
    #endif
    }

    // GUI-safe edge for Back
    protected bool BackPressedUI()
    {
    #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current;
        bool held = (k != null && k.backspaceKey.isPressed) || (g != null && g.selectButton.isPressed);
    #else
        bool held = Input.GetKey(KeyCode.Backspace);
    #endif
        bool edge = held && !_guiBackLatchHeld;
        _guiBackLatchHeld = held;
        if (edge) _guiBackLatchDown = true;
        bool fired = _guiBackLatchDown;
        _guiBackLatchDown = false;
        return fired;
    }

    // Call this at the TOP of your Update in each game.
    protected bool HandleCommonPause()
    {
        if (!Running) return true;

        _pauseCooldown = Mathf.Max(0f, _pauseCooldown - Time.unscaledDeltaTime);

        if (_pauseCooldown <= 0f && PausePressed())
        {
            Paused = !Paused;
            _pauseCooldown = 0.15f;
            if (meta && meta.audioBus) meta.audioBus.BeepOnce(Paused ? 260 : 320, 0.05f);
        }

        if (Paused)
        {
            if (BackPressed()) QuitToMenu(); // Back always exits while paused
            return true;
        }
        return false;
    }

    // Minimal, always-available HUD (now uses dynamic RetroDraw height)
    protected void DrawCommonHUD(int sw, int sh)
    {
        int vh = Mathf.Max(1, RetroDraw.ViewH);
        int vw = Mathf.Max(1, RetroDraw.ViewW);

        // Score box at top-left
        RetroDraw.PixelRect(2, vh - 11, 68, 9, sw, sh, new Color(0, 0, 0, 0.65f));
        RetroDraw.PrintSmall(6, vh - 10, $"SCORE {ScoreP1:0000}", sw, sh, Color.white);

        if (Paused)
        {
            RetroDraw.PixelRect(vw/2 - 50, vh/2 - 16, 100, 32, sw, sh, new Color(0,0,0,0.80f));
            RetroDraw.PrintBig  (vw/2 - 24, vh/2 - 4, "PAUSED", sw, sh, new Color(1,1,0.8f,1));
            RetroDraw.PrintSmall(vw/2 - 40, vh/2 - 14, "B = MENU", sw, sh, new Color(0.8f,0.9f,1f,1));
        }
    }
}
