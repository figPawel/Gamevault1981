// === GameManager.cs — DROP-IN ===
// Purged Legacy Input. Input System only. Provides A/Fire, Back, Pause helpers used across games.
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

    protected bool Paused;
    float _pauseCooldown;

    bool _guiBackLatchDown, _guiBackLatchHeld;
    bool _guiALatchDown,    _guiALatchHeld;

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
            g.buttonWest.wasPressedThisFrame  || g.buttonNorth.wasPressedThisFrame ||
            g.rightTrigger.wasPressedThisFrame || g.leftTrigger.wasPressedThisFrame ||
            g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame
        );
        return kb || ms || gp;
    }

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

    // ---------------- BACK ----------------
    protected bool BackPressed()
    {
        var k = Keyboard.current; var g = Gamepad.current; var m = Mouse.current;
        bool kb = k != null && k.backspaceKey.isPressed;
        bool gp = g != null && g.buttonEast.isPressed; // B
        bool ms = m != null && m.rightButton.isPressed;
        return kb || gp || ms;
    }

    protected bool BackPressedUI()
    {
        bool held = BackPressed();
        bool edge = held && !_guiBackLatchHeld;
        _guiBackLatchHeld = held;
        if (edge) _guiBackLatchDown = true;
        bool fired = _guiBackLatchDown;
        _guiBackLatchDown = false;
        return fired;
    }

    // ---------------- PAUSE ----------------
    protected bool PausePressed()
    {
        var k = Keyboard.current; var g = Gamepad.current;
        return (k != null && (k.escapeKey.wasPressedThisFrame || k.pKey.wasPressedThisFrame))
            || (g != null && (g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame));
    }

    // Call at TOP of each game’s Update()
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

        return Paused;
    }

    protected void DrawCommonHUD(int sw, int sh)
    {
        RetroDraw.PixelRect(2, sh - 11, 68, 9, sw, sh, new Color(0, 0, 0, 1f));
        RetroDraw.PrintSmall(6, sh - 10, $"SCORE {ScoreP1:0000}", sw, sh, Color.white);

        if (Paused)
        {
            RetroDraw.PixelRect(sw / 2 - 50, sh / 2 - 16, 100, 32, sw, sh, new Color(0, 0, 0, 1f));
            RetroDraw.PrintBig(sw / 2 - 36, sh / 2 - 4, "PAUSED", sw, sh, new Color(1, 1, 0.8f, 1));
            RetroDraw.PrintSmall(sw / 2 - 56, sh / 2 - 14, "START/ESC = MENU", sw, sh, new Color(0.8f, 0.9f, 1f, 1));
        }
    }
}
