using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// --- Make sure this is NOT in an Editor/ folder and NOT inside any namespace ---
public enum GameMode
{
    Solo   = 0,
    Versus2P = 1,
    Coop2P = 2,
    Alt2P  = 3
}


public abstract class GameManager : MonoBehaviour
{
    public MetaGameManager meta;
    public GameDef Def;
    public GameMode Mode;
    public int ScoreP1, ScoreP2;
    public bool Running { get; protected set; }

    // Pause state shared by all games
    protected bool Paused;
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
        _pauseCooldown = 0f;
        OnStartMode();
    }

    public virtual void QuitToMenu()
    {
        Running = false;
        Paused = false;
        meta.QuitToSelection();
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
        return p == 1 ? Input.GetButton("Fire1") || Input.GetKey(KeyCode.Z)
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
        return p == 1 ? Input.GetKeyDown(KeyCode.Z)
                      : Input.GetKeyDown(KeyCode.Comma);
#endif
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

    // Call this at the TOP of your Update in each game.
    protected bool HandleCommonPause()
    {
        if (!Running) return true;

        _pauseCooldown = Mathf.Max(0f, _pauseCooldown - Time.unscaledDeltaTime);

        if (_pauseCooldown <= 0f && PausePressed())
        {
            Paused = !Paused;
            _pauseCooldown = 0.15f;
            meta.audioBus.BeepOnce(Paused ? 260 : 320, 0.05f);
        }

        if (Paused)
        {
            // Allow quick exit while paused
            if (BackPressed())
                QuitToMenu();
            return true; // tell caller to early-out its Update
        }
        return false;
    }

    // Minimal, always-available HUD
    protected void DrawCommonHUD(int sw, int sh)
    {
        // Score (top-left)
        RetroDraw.PixelRect(2, sh - 11, 46, 9, sw, sh, new Color(0, 0, 0, 0.65f));
        RetroDraw.PrintSmall(6, sh - 10, $"SCORE {ScoreP1:0000}", sw, sh, Color.white);

        if (Paused)
        {
            RetroDraw.PixelRect(sw / 2 - 50, sh / 2 - 16, 100, 32, sw, sh, new Color(0, 0, 0, 0.80f));
            RetroDraw.PrintBig(sw / 2 - 24, sh / 2 - 4, "PAUSED", sw, sh, new Color(1, 1, 0.8f, 1));
            RetroDraw.PrintSmall(sw / 2 - 40, sh / 2 - 14, "B = MENU", sw, sh, new Color(0.8f, 0.9f, 1f, 1));
        }
    }
}
