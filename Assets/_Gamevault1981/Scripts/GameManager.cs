using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// --- Keep this file OUT of an Editor/ folder and OUTSIDE any namespace ---
public enum GameMode
{
    Solo     = 0,
    Versus2P = 1,
    Coop2P   = 2,
    Alt2P    = 3,
}

public abstract class GameManager : MonoBehaviour
{
    public MetaGameManager meta;  // assign from scene
    public GameDef Def;           // set by Meta before StartMode
    public GameMode Mode;

    public int ScoreP1, ScoreP2;
    public bool Running { get; protected set; }

    protected bool Paused;
    float _pauseCooldown;

    // GUI-safe latches for inputs that may be read inside OnGUI
    bool _guiBackLatchDown, _guiBackLatchHeld;
    bool _guiALatchDown,    _guiALatchHeld;

    // ------------ Lifecycle ------------
    public virtual void Begin() { }
    public virtual void OnStartMode() { }
    public virtual void StopMode() { Running = false; }
    public virtual void ResetScores() { ScoreP1 = 0; ScoreP2 = 0; }

    public virtual void StartMode(GameMode mode)
    {
        Mode = mode;
        Running = true;
        Paused = false;
        _pauseCooldown = 0f;
        OnStartMode();
    }

    public virtual void QuitToMenu()
    {
        Running = false;
        Paused = false;
        if (meta) meta.QuitToSelection();
    }

    // ------------ INPUT (keyboard/mouse/gamepad) ------------
    protected bool BtnA(int p = 1)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool kb = k != null && (
            k.spaceKey.isPressed || k.enterKey.isPressed ||
            k.eKey.isPressed || k.rKey.isPressed ||
            k.gKey.isPressed || k.hKey.isPressed ||
            k.zKey.isPressed || k.xKey.isPressed ||
            k.leftCtrlKey.isPressed);
        bool ms = m != null && (m.leftButton.isPressed || m.rightButton.isPressed);
        bool gp = g != null && (
            g.buttonSouth.isPressed || g.buttonWest.isPressed || g.buttonNorth.isPressed ||
            g.rightTrigger.ReadValue() > 0.3f || g.leftTrigger.ReadValue() > 0.3f);
        return kb || ms || gp;
#else
        return Input.GetMouseButton(0) || Input.GetMouseButton(1) ||
               Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) ||
               Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.R) ||
               Input.GetKey(KeyCode.G) || Input.GetKey(KeyCode.H) ||
               Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.X) ||
               Input.GetKey(KeyCode.LeftControl) || Input.GetButton("Fire1");
#endif
    }

    protected bool BtnADown(int p = 1)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool kb = k != null && (
            k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame ||
            k.eKey.wasPressedThisFrame || k.rKey.wasPressedThisFrame ||
            k.gKey.wasPressedThisFrame || k.hKey.wasPressedThisFrame ||
            k.zKey.wasPressedThisFrame || k.xKey.wasPressedThisFrame ||
            k.leftCtrlKey.wasPressedThisFrame);
        bool ms = m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame);
        bool gp = g != null && (
            g.buttonSouth.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame ||
            g.buttonNorth.wasPressedThisFrame ||
            g.rightTrigger.wasPressedThisFrame || g.leftTrigger.wasPressedThisFrame);
        return kb || ms || gp;
#else
        return Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
               Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
               Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.R) ||
               Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.H) ||
               Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.X) ||
               Input.GetKeyDown(KeyCode.LeftControl);
#endif
    }

    // GUI-safe edge for A (use in OnGUI)
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

    // Pause (Esc/P or Start/Select)
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

    // Back/Cancel
    protected bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        return (k != null && (k.backspaceKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame))
            || (m != null && m.rightButton.wasPressedThisFrame)
            || (g != null && (g.buttonEast.wasPressedThisFrame || g.selectButton.wasPressedThisFrame));
#else
        return Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Escape) ||
               Input.GetMouseButtonDown(1);
#endif
    }

    // GUI-safe edge for Back (use in OnGUI)
    protected bool BackPressedUI()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool held = (k != null && (k.backspaceKey.isPressed || k.escapeKey.isPressed))
                 || (m != null && m.rightButton.isPressed)
                 || (g != null && (g.buttonEast.isPressed || g.selectButton.isPressed));
#else
        bool held = Input.GetKey(KeyCode.Backspace) || Input.GetKey(KeyCode.Escape) || Input.GetMouseButton(1);
#endif
        bool edge = held && !_guiBackLatchHeld;
        _guiBackLatchHeld = held;
        if (edge) _guiBackLatchDown = true;
        bool fired = _guiBackLatchDown;
        _guiBackLatchDown = false;
        return fired;
    }

    // Call this at the TOP of Update() in each game.
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
            if (BackPressed()) QuitToMenu(); // Back from pause returns to menu
            return true; // consume update while paused
        }
        return false;
    }

    // Minimal HUD + pause card using RetroDraw (opaque, no translucent bars)
    protected void DrawCommonHUD(int sw, int sh)
    {
        // Score (top-left) — solid black plate behind text
        RetroDraw.PixelRect(2, sh - 11, 68, 9, sw, sh, new Color(0, 0, 0, 1f));
        RetroDraw.PrintSmall(6, sh - 10, $"SCORE {ScoreP1:0000}", sw, sh, Color.white);

        if (Paused)
        {
            RetroDraw.PixelRect(sw / 2 - 50, sh / 2 - 16, 100, 32, sw, sh, new Color(0, 0, 0, 1f));
            RetroDraw.PrintBig(sw / 2 - 24, sh / 2 - 4, "PAUSED", sw, sh, new Color(1, 1, 0.8f, 1));
            RetroDraw.PrintSmall(sw / 2 - 40, sh / 2 - 14, "B = MENU", sw, sh, new Color(0.8f, 0.9f, 1f, 1));
        }
    }
}
