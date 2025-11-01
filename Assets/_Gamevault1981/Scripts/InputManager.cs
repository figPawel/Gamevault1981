// InputManager.cs — FULL FILE (Unity New Input System)
// 1P: any device works (dynamic switching). 2P: respect Options mappings.
//
// Drop this on a singleton object in your boot scene.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager I;

    [Header("General")]
    [Tooltip("When only 1P is active, if P1 hits a different device (not assigned to P2), switch P1 to that device. (Not needed for 1P-anything mode, but kept for legacy.)")]
    public bool autoHotSwapP1 = true;

    [Header("Keyboard 1 (defaults: WASD + Space/E/F)")]
    public KeyboardProfile keyboard1 = KeyboardProfile.DefaultK1();

    [Header("Keyboard 2 (defaults: Arrows + RightCtrl/RightShift/L)")]
    public KeyboardProfile keyboard2 = KeyboardProfile.DefaultK2();

    [Header("Mouse (defaults: LMB = Fire, RMB = Back)")]
    public MouseProfile mouse = MouseProfile.Default();

    [Header("Gamepad (generic mapping)")]
    public GamepadProfile gamepad = GamepadProfile.Default();

    // ------------ Public API ------------
    // In 1P mode: P1 can use ANY device; P2 is ignored.
    // In 2P mode: P1/P2 use their mapped device (Options -> map_p1/map_p2).
    public Vector2 Move(int player)
    {
        if (_onePlayerContext && player == 1)
            return ReadMoveAnyDevice(); // dynamic
        return ReadMoveMapped(player);
    }

    public bool Fire(int player)
    {
        if (_onePlayerContext && player == 1)
            return ReadFireAnyDevice(down:false); // dynamic
        return ReadFireMapped(player, down:false);
    }

    public bool FireDown(int player)
    {
        if (_onePlayerContext && player == 1)
            return ReadFireAnyDevice(down:true); // dynamic
        return ReadFireMapped(player, down:true);
    }

    // UI / Meta actions (menu/overlay). Keep broad so pause/back work regardless of mapping.
    public bool UIBackDown()
    {
        if (WasPressed(_uiBack)) return true;

        var kb = Keyboard.current;
        if (kb != null && kb.backspaceKey.wasPressedThisFrame) return true;

        var ms = Mouse.current;
        if (ms != null && ms.rightButton.wasPressedThisFrame) return true;

        foreach (var pad in Gamepad.all)
            if (pad != null && pad.buttonEast.wasPressedThisFrame) return true;

        return false;
    }
    public bool UIBackHeld()
    {
        if (ReadPressed(_uiBack)) return true;

        var kb = Keyboard.current;
        if (kb != null && kb.backspaceKey.isPressed) return true;

        var ms = Mouse.current;
        if (ms != null && ms.rightButton.isPressed) return true;

        foreach (var pad in Gamepad.all)
            if (pad != null && pad.buttonEast.isPressed) return true;

        return false;
    }
    public bool UIPauseDown()
    {
        if (WasPressed(_uiPause)) return true;

        var kb = Keyboard.current;
        if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame)) return true;

        foreach (var pad in Gamepad.all)
            if (pad != null && (pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame)) return true;

        return false;
    }

    // Flip 1P/2P behavior from the game (call when mode changes).
    public void SetOnePlayerContext(bool on) => _onePlayerContext = on;

    // Call after Options change device mapping.
    public void ApplyPlayerDevicePrefs()
    {
        _p1Choice = PlayerPrefs.GetInt("map_p1", 0);
        _p2Choice = PlayerPrefs.GetInt("map_p2", 1);
        // No per-action devices filtering needed; we scope reads at call sites.
    }

    // ------------ Implementation ------------
    [Serializable]
    public class KeyboardProfile
    {
        public Key up, down, left, right;
        public Key fire1, fire2, fire3;
        public Key back;
        public Key pause;

        public static KeyboardProfile DefaultK1() => new KeyboardProfile
        {
            up = Key.W, down = Key.S, left = Key.A, right = Key.D,
            fire1 = Key.Space, fire2 = Key.E, fire3 = Key.F,
            back = Key.Backspace, pause = Key.Escape
        };
        public static KeyboardProfile DefaultK2() => new KeyboardProfile
        {
            up = Key.UpArrow, down = Key.DownArrow, left = Key.LeftArrow, right = Key.RightArrow,
            fire1 = Key.RightCtrl, fire2 = Key.RightShift, fire3 = Key.L,
            back = Key.Backspace, pause = Key.Escape
        };
    }

    [Serializable]
    public class MouseProfile
    {
        public bool useDeltaForMove = false;
        public float deltaSensitivity = 0.75f;
        public MouseButton fireButton = MouseButton.Left;
        public MouseButton altFireButton = MouseButton.Middle;
        public MouseButton backButton = MouseButton.Right;
        public bool pauseOnMiddle = false;

        public static MouseProfile Default() => new MouseProfile();
    }
    public enum MouseButton { Left, Right, Middle }

    [Serializable]
    public class GamepadProfile
    {
        public bool fireSouth = true;
        public bool fireWest  = true;
        public bool fireNorth = false;
        public bool fireShoulders = true;
        public bool fireTriggers  = true;
        public bool backEast = true;
        public bool pauseStart = true;
        public bool pauseSelect = true;

        public static GamepadProfile Default() => new GamepadProfile();
    }

    struct ControlSet
    {
        public InputAction moveAction;
        public InputAction fireAction;
        public List<InputAction> owns; // enable/disable/dispose
    }

    ControlSet _k1, _k2, _mouse, _padP1, _padP2; // pads share bindings; actual pad picked at read time
    InputAction _uiBack, _uiPause;

    // Options mapping (0:K1, 1:K2, 2:Mouse, 3+:Gamepad#)
    int _p1Choice = 0, _p2Choice = 1;

    // Context: true => 1P-anything; false => 2P-respect-mapping
    bool _onePlayerContext = true;

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        BuildAllActions();
        ApplyPlayerDevicePrefs();
    }

    void OnEnable()  => EnableAll(true);
    void OnDisable() => EnableAll(false);

    void OnDestroy()
    {
        DisposeAll();
        if (I == this) I = null;
    }

    // -------- Build Actions --------
    void BuildAllActions()
    {
        DisposeAll();

        _k1 = BuildKeyboardSet(keyboard1);
        _k2 = BuildKeyboardSet(keyboard2);
        _mouse = BuildMouseSet(mouse);
        _padP1 = BuildGamepadSet(gamepad);
        _padP2 = BuildGamepadSet(gamepad);

        _uiBack  = new InputAction("uiBack",  InputActionType.Button);
        _uiPause = new InputAction("uiPause", InputActionType.Button);

        _uiBack.AddBinding("<Keyboard>/backspace");
        _uiBack.AddBinding("<Mouse>/rightButton");
        _uiBack.AddBinding("<Gamepad>/buttonEast");

        _uiPause.AddBinding("<Keyboard>/escape");
        _uiPause.AddBinding("<Gamepad>/start");
        _uiPause.AddBinding("<Gamepad>/select");
        _uiPause.AddBinding("<Keyboard>/p");
    }

    ControlSet BuildKeyboardSet(KeyboardProfile prof)
    {
        var set = new ControlSet { owns = new List<InputAction>() };

        var move = new InputAction("kbMove", InputActionType.Value, expectedControlType: "Vector2");
        var c = move.AddCompositeBinding("2DVector");
        c.With("Up",    $"<Keyboard>/{KeyToPath(prof.up)}");
        c.With("Down",  $"<Keyboard>/{KeyToPath(prof.down)}");
        c.With("Left",  $"<Keyboard>/{KeyToPath(prof.left)}");
        c.With("Right", $"<Keyboard>/{KeyToPath(prof.right)}");

        var fire = new InputAction("kbFire", InputActionType.Button);
        fire.AddBinding($"<Keyboard>/{KeyToPath(prof.fire1)}");
        fire.AddBinding($"<Keyboard>/{KeyToPath(prof.fire2)}");
        fire.AddBinding($"<Keyboard>/{KeyToPath(prof.fire3)}");
        fire.AddBinding("<Keyboard>/enter");

        set.moveAction = move; set.fireAction = fire;
        set.owns.Add(move); set.owns.Add(fire);
        return set;
    }

    ControlSet BuildMouseSet(MouseProfile prof)
    {
        var set = new ControlSet { owns = new List<InputAction>() };

        var move = new InputAction("mouseMove", InputActionType.Value, expectedControlType: "Vector2");
        if (prof.useDeltaForMove) move.AddBinding("<Mouse>/delta");
        else                      move.AddBinding("<Mouse>/position");

        var fire = new InputAction("mouseFire", InputActionType.Button);
        fire.AddBinding(MouseButtonPath(prof.fireButton));
        if (prof.altFireButton != prof.fireButton)
            fire.AddBinding(MouseButtonPath(prof.altFireButton));

        set.moveAction = move; set.fireAction = fire;
        set.owns.Add(move); set.owns.Add(fire);
        return set;
    }

    ControlSet BuildGamepadSet(GamepadProfile prof)
    {
        var set = new ControlSet { owns = new List<InputAction>() };

        var move = new InputAction("padMove", InputActionType.Value, expectedControlType: "Vector2");
        move.AddBinding("<Gamepad>/leftStick");
        move.AddBinding("<Gamepad>/dpad");

        var fire = new InputAction("padFire", InputActionType.Button);
        if (prof.fireSouth)  fire.AddBinding("<Gamepad>/buttonSouth");
        if (prof.fireWest)   fire.AddBinding("<Gamepad>/buttonWest");
        if (prof.fireNorth)  fire.AddBinding("<Gamepad>/buttonNorth");
        if (prof.fireShoulders)
        {
            fire.AddBinding("<Gamepad>/leftShoulder");
            fire.AddBinding("<Gamepad>/rightShoulder");
        }
        if (prof.fireTriggers)
        {
            fire.AddBinding("<Gamepad>/leftTrigger");
            fire.AddBinding("<Gamepad>/rightTrigger");
        }

        set.moveAction = move; set.fireAction = fire;
        set.owns.Add(move); set.owns.Add(fire);
        return set;
    }

    // ---- Helpers: control paths ----
    static string KeyToPath(Key k)
    {
        switch (k)
        {
            case Key.A: return "a"; case Key.B: return "b"; case Key.C: return "c";
            case Key.D: return "d"; case Key.E: return "e"; case Key.F: return "f";
            case Key.G: return "g"; case Key.H: return "h"; case Key.I: return "i";
            case Key.J: return "j"; case Key.K: return "k"; case Key.L: return "l";
            case Key.M: return "m"; case Key.N: return "n"; case Key.O: return "o";
            case Key.P: return "p"; case Key.Q: return "q"; case Key.R: return "r";
            case Key.S: return "s"; case Key.T: return "t"; case Key.U: return "u";
            case Key.V: return "v"; case Key.W: return "w"; case Key.X: return "x";
            case Key.Y: return "y"; case Key.Z: return "z";

            case Key.UpArrow: return "upArrow";
            case Key.DownArrow: return "downArrow";
            case Key.LeftArrow: return "leftArrow";
            case Key.RightArrow: return "rightArrow";

            case Key.RightCtrl: return "rightCtrl";
            case Key.RightShift: return "rightShift";
            case Key.LeftCtrl: return "leftCtrl";
            case Key.LeftShift: return "leftShift";
            case Key.Space: return "space";
            case Key.Escape: return "escape";
            case Key.Backspace: return "backspace";
            case Key.Enter: return "enter";
        }
        return k.ToString().Substring(0, 1).ToLower();
    }

    static string MouseButtonPath(MouseButton b) =>
        b == MouseButton.Left ? "<Mouse>/leftButton" :
        b == MouseButton.Right ? "<Mouse>/rightButton" : "<Mouse>/middleButton";

    void EnableAll(bool on)
    {
        void Set(ControlSet s) { if (s.owns == null) return; foreach (var a in s.owns) { if (on) a.Enable(); else a.Disable(); } }
        Set(_k1); Set(_k2); Set(_mouse); Set(_padP1); Set(_padP2);
        if (on) { _uiBack.Enable(); _uiPause.Enable(); } else { _uiBack.Disable(); _uiPause.Disable(); }
    }

    void DisposeAll()
    {
        void Kill(ControlSet s) { if (s.owns == null) return; foreach (var a in s.owns) a.Dispose(); s.owns.Clear(); }
        Kill(_k1); Kill(_k2); Kill(_mouse); Kill(_padP1); Kill(_padP2);
        _uiBack?.Dispose(); _uiPause?.Dispose();
    }

    // -------- 1P: ANY DEVICE --------
    Vector2 ReadMoveAnyDevice()
    {
        Vector2 best = Vector2.zero;
        float bestMag = 0f;

        // K1
        var v = _k1.moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        float m = v.sqrMagnitude;
        if (m > bestMag) { best = v; bestMag = m; }

        // K2
        v = (_k2.moveAction?.ReadValue<Vector2>() ?? Vector2.zero);
        m = v.sqrMagnitude;
        if (m > bestMag) { best = v; bestMag = m; }

        // Mouse (optional)
        v = (_mouse.moveAction?.ReadValue<Vector2>() ?? Vector2.zero);
        if (mouse.useDeltaForMove)
        {
            // Convert delta pixels to a [-1,1] feel
            var dv = v * (mouse.deltaSensitivity / 50f);
            dv = Vector2.ClampMagnitude(dv, 1f);
            m = dv.sqrMagnitude;
            if (m > bestMag) { best = dv; bestMag = m; }
        }

        // All gamepads: pick the strongest
        foreach (var pad in Gamepad.all)
        {
            if (pad == null) continue;
            Vector2 pv = pad.leftStick.ReadValue();
            pv += new Vector2(pad.dpad.x.ReadValue(), pad.dpad.y.ReadValue());
            pv = Vector2.ClampMagnitude(pv, 1f);
            m = pv.sqrMagnitude;
            if (m > bestMag) { best = pv; bestMag = m; }
        }

        return best;
    }

    bool ReadFireAnyDevice(bool down)
    {
        // K1
        if (down ? WasPressed(_k1.fireAction) : ReadPressed(_k1.fireAction)) return true;
        // K2
        if (down ? WasPressed(_k2.fireAction) : ReadPressed(_k2.fireAction)) return true;
        // Mouse
        if (down ? WasPressed(_mouse.fireAction) : ReadPressed(_mouse.fireAction)) return true;
        // Any pad
        foreach (var pad in Gamepad.all)
        {
            if (pad == null) continue;
            if (ReadPadFireRaw(pad, down)) return true;
        }
        return false;
    }

    // -------- 2P: RESPECT MAPPING --------
    Vector2 ReadMoveMapped(int player)
    {
        int choice = (player == 2) ? _p2Choice : _p1Choice;
        if (choice >= 3) return ReadPadMove(choice - 3);

        if (choice == 2) return _mouse.moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        if (choice == 1) return _k2.moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
        return _k1.moveAction?.ReadValue<Vector2>() ?? Vector2.zero;
    }

    bool ReadFireMapped(int player, bool down)
    {
        int choice = (player == 2) ? _p2Choice : _p1Choice;
        if (choice >= 3) return ReadPadFire(choice - 3, down);
        if (choice == 2) return down ? WasPressed(_mouse.fireAction) : ReadPressed(_mouse.fireAction);
        if (choice == 1) return down ? WasPressed(_k2.fireAction)    : ReadPressed(_k2.fireAction);
        return down ? WasPressed(_k1.fireAction) : ReadPressed(_k1.fireAction);
    }

    // -------- Pad helpers --------
    Vector2 ReadPadMove(int padIndex)
    {
        var pads = Gamepad.all;
        if (padIndex < 0 || padIndex >= pads.Count) return Vector2.zero;
        var p = pads[padIndex];
        Vector2 v = p.leftStick.ReadValue();
        v += new Vector2(p.dpad.x.ReadValue(), p.dpad.y.ReadValue());
        return Vector2.ClampMagnitude(v, 1f);
    }

    bool ReadPadFire(int padIndex, bool down)
    {
        var pads = Gamepad.all;
        if (padIndex < 0 || padIndex >= pads.Count) return false;
        return ReadPadFireRaw(pads[padIndex], down);
    }

    bool ReadPadFireRaw(Gamepad p, bool down)
    {
        if (p == null) return false;
        bool pressed = false;

        if (gamepad.fireSouth) pressed |= down ? p.buttonSouth.wasPressedThisFrame : p.buttonSouth.isPressed;
        if (gamepad.fireWest)  pressed |= down ? p.buttonWest.wasPressedThisFrame  : p.buttonWest.isPressed;
        if (gamepad.fireNorth) pressed |= down ? p.buttonNorth.wasPressedThisFrame : p.buttonNorth.isPressed;

        if (gamepad.fireShoulders)
        {
            pressed |= down ? p.leftShoulder.wasPressedThisFrame  : p.leftShoulder.isPressed;
            pressed |= down ? p.rightShoulder.wasPressedThisFrame : p.rightShoulder.isPressed;
        }

        if (gamepad.fireTriggers)
        {
            if (down)
            {
                pressed |= p.leftTrigger.wasPressedThisFrame;
                pressed |= p.rightTrigger.wasPressedThisFrame;
            }
            else
            {
                pressed |= p.leftTrigger.ReadValue()  > 0.5f;
                pressed |= p.rightTrigger.ReadValue() > 0.5f;
            }
        }

        return pressed;
    }

    // -------- Misc helpers --------
    bool WasPressed(InputAction a)
    {
        if (a == null) return false;
        try { return a.WasPressedThisFrame(); }
        catch { return a.triggered; }
    }
    bool ReadPressed(InputAction a) => a != null && a.ReadValue<float>() > 0.5f;

    // Optional legacy hot-swap (left enabled only in 1P context; harmless otherwise)
    void LateUpdate()
    {
        if (!autoHotSwapP1 || !_onePlayerContext) return;

        // Soft-detect major device use and keep _p1Choice loosely aligned (non-functional in 1P-anything, but keeps prefs tidy)
        var ms = Mouse.current;
        if (ms != null && ms.leftButton.wasPressedThisFrame)
        {
            _p1Choice = 2;
            PlayerPrefs.SetInt("map_p1", _p1Choice);
            return;
        }

        var pads = Gamepad.all;
        for (int i = 0; i < pads.Count; i++)
        {
            var pad = pads[i];
            if (pad == null) continue;

            bool any =
                pad.buttonSouth.wasPressedThisFrame ||
                pad.buttonWest.wasPressedThisFrame  ||
                pad.buttonNorth.wasPressedThisFrame ||
                pad.buttonEast.wasPressedThisFrame  ||
                pad.leftTrigger.wasPressedThisFrame ||
                pad.rightTrigger.wasPressedThisFrame||
                pad.leftShoulder.wasPressedThisFrame||
                pad.rightShoulder.wasPressedThisFrame||
                pad.startButton.wasPressedThisFrame ||
                pad.selectButton.wasPressedThisFrame;

            if (any)
            {
                _p1Choice = 3 + i;
                PlayerPrefs.SetInt("map_p1", _p1Choice);
                return;
            }
        }
    }
}
