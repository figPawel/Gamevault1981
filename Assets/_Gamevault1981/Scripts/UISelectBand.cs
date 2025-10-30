using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.IO;

public class UISelectBand : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerClickHandler, ISubmitHandler
{
    [Header("Band root (focus/click target)")]
    public Button bandButton;             // <- add a Button on the band root; assign here
    public Image  bandHighlightFrame;     // <- optional outline/underlay image to tint on focus

    [Header("Cartridge visuals")]
    public RawImage cartridgeImage;       // label goes here
    public TMP_Text cartTitleText;        // overlay title on the cartridge (optional)
    public TMP_Text numberText;           // "#<n>" badge (optional)

    [Header("Right-side texts")]
    public TMP_Text titleText;            // large title on the band (optional if you rely on cartridge only)
    public TMP_Text descText;
    public TMP_Text modesText;
    public TMP_Text statsText;

    [Header("Actions (optional)")]
    public Button btnPlay;                // you can keep a separate PLAY button if you like

    [Header("Unlock overlay (countdown only)")]
    [Tooltip("TMP text placed visually where you want the countdown (e.g., over the band).")]
    public TMP_Text unlockCountdownText;

    [Header("Label loading (StreamingAssets)")]
    [SerializeField] string labelsFolder = "labels";                 // StreamingAssets/labels/<id or title>.(png|jpg|jpeg)
    [SerializeField] Vector2 defaultFocus = new Vector2(0.5f, 0.5f); // crop focus 0..1 (x,y)
    [Serializable] public struct LabelCropOverride { public string id; public Vector2 focus; }
    [SerializeField] LabelCropOverride[] cropOverrides;

    [Header("Highlight color")]
    [Tooltip("Leave alpha=0 to auto-generate from game number.")]
    public Color highlightOverride = new Color(0,0,0,0);

    // NEW: lets UIManager auto-scroll this into view when it gets focus
    public Action<RectTransform> onSelected;
    public RectTransform Rect => transform as RectTransform;

    GameDef _def;
    MetaGameManager _meta;
    Color _highlight;
    Color _dim;

    // minimal unlock state tracking
    bool _locked;
    float _nextTick;
    string _lastCountdownText = "";

    // ---------- Public API ----------
    public void Bind(GameDef def, MetaGameManager meta)
    {
        _def = def;
        _meta = meta;

        // Choose highlight color
        _highlight = (highlightOverride.a > 0.01f) ? highlightOverride : AutoColor(def);
        _dim       = new Color(_highlight.r, _highlight.g, _highlight.b, 0.18f);

        // Texts
        SafeSet(titleText,  def.title);
        SafeSet(descText,   def.desc);
        SafeSet(modesText,  Modes(def.flags));
        SafeSet(statsText,  BuildStats(def.flags));   // << only change to stats
        SafeSet(numberText, $"#{def.number}");
        SafeSet(cartTitleText, def.title);

        // Label art
        LoadAndCropLabel();

        // Button wiring
        if (bandButton)
        {
            bandButton.onClick.RemoveAllListeners();
            bandButton.onClick.AddListener(() => _meta.StartGame(_def));
            ApplyColorBlock(bandButton, _highlight);
            SetHighlight(false);
        }

        if (btnPlay)
        {
            btnPlay.onClick.RemoveAllListeners();
            btnPlay.onClick.AddListener(() => _meta.StartGame(_def));

            // Make navigation sane: from PLAY -> band, from band -> PLAY (left/right)
            var nPlay = btnPlay.navigation;
            nPlay.mode = Navigation.Mode.Explicit;
            nPlay.selectOnLeft = bandButton ? bandButton : null;
            nPlay.selectOnRight = bandButton ? bandButton : null;
            btnPlay.navigation = nPlay;
        }

        if (bandButton && !bandButton.targetGraphic)
        {
            // Prefer a designated frame; else use the band’s background image; else use the cartridge
            bandButton.targetGraphic = (bandHighlightFrame ? (Graphic)bandHighlightFrame
                                         : (Graphic)bandButton.GetComponent<Image>())
                                         ?? (Graphic)cartridgeImage;
        }

        // initialize countdown visibility without altering layout
        UpdateLockVisuals(force:true);
    }

    // ---------- EventSystem hooks (controller/keyboard navigation) ----------
    public void OnSelect(BaseEventData e)
    {
        SetHighlight(true);
        onSelected?.Invoke(Rect); // NEW: tell UI to scroll me into view
    }

    public void OnDeselect(BaseEventData e) => SetHighlight(false);

    public void OnPointerClick(PointerEventData e)
    {
        if (bandButton && bandButton.interactable) _meta.StartGame(_def);
    }

    public void OnSubmit(BaseEventData e)
    {
        if (bandButton && bandButton.interactable) _meta.StartGame(_def);
    }

    void SetHighlight(bool on)
    {
        if (bandHighlightFrame)
            bandHighlightFrame.color = on ? _highlight : _dim;

        // If you want the cartridge title to glow a bit on focus:
        if (cartTitleText)
            cartTitleText.color = on ? Color.Lerp(_highlight, Color.white, 0.35f) : new Color(1,1,1,0.85f);
    }

    // ---------- Minimal unlock countdown (no other UI changes) ----------
    void Update()
    {
        if (_def == null || _meta == null) return;

        if (Time.unscaledTime >= _nextTick)
        {
            _nextTick = Time.unscaledTime + 0.5f; // ~2Hz tick
            UpdateLockVisuals(force:false);
        }
    }

    void UpdateLockVisuals(bool force)
{
    bool isUnlocked = _meta.IsUnlocked(_def);
    bool newLocked  = !isUnlocked;

    if (force || newLocked != _locked)
    {
        _locked = newLocked;

        if (unlockCountdownText)
            unlockCountdownText.gameObject.SetActive(_locked);

        if (descText)                           // NEW: hide description while locked
            descText.gameObject.SetActive(!_locked);
    }

    if (_locked && unlockCountdownText)
    {
        DateTime now = _meta.NowUtc;
        TimeSpan left = (_def.unlockAtUtc.HasValue ? (_def.unlockAtUtc.Value - now) : TimeSpan.Zero);
        if (left.TotalSeconds < 0) left = TimeSpan.Zero;

        string txt = FormatCountdown(left);
        if (!string.Equals(txt, _lastCountdownText))
        {
            _lastCountdownText = txt;
            unlockCountdownText.text = $"Unlocks in {txt}";
        }
    }
}

    static string FormatCountdown(TimeSpan t)
    {
        int days = Mathf.FloorToInt((float)t.TotalDays);
        int hh = t.Hours, mm = t.Minutes, ss = t.Seconds;
        return $"{days:00}:{hh:00}:{mm:00}:{ss:00}";
    }

    // ---------- Label loading & cropping ----------
    void LoadAndCropLabel()
    {
        if (!cartridgeImage || _def == null) return;

        Texture2D tex = TryLoadLabelTexture();
        cartridgeImage.texture = tex;
        cartridgeImage.uvRect  = new Rect(0,0,1,1);

        if (tex == null) return;

        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;

        var rt = cartridgeImage.rectTransform.rect;
        float targetAspect = (rt.height <= 0f) ? 1.0f : (rt.width / rt.height);
        ApplyCoverCrop(cartridgeImage, tex, targetAspect, FindFocus(_def.id));
    }

    Texture2D TryLoadLabelTexture()
    {
        string sa = Application.streamingAssetsPath;

        Texture2D Try(string p)
        {
            if (!File.Exists(p)) return null;
            var bytes = File.ReadAllBytes(p);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(bytes, false);
            t.name = Path.GetFileNameWithoutExtension(p);
            return t;
        }

        string[] names = { _def.id, _def.title };  // support IDs or legacy Title filenames
        string[] exts  = { ".png", ".jpg", ".jpeg" };
        string[] folders = { Path.Combine(sa, labelsFolder), Path.Combine(sa, "covers") };

        foreach (var folder in folders)
        foreach (var n in names)
        {
            if (string.IsNullOrEmpty(n)) continue;
            foreach (var e in exts)
            {
                var tex = Try(Path.Combine(folder, n + e));
                if (tex != null) return tex;
            }
        }
        return null;
    }

    Vector2 FindFocus(string id)
    {
        if (cropOverrides != null)
        {
            for (int i = 0; i < cropOverrides.Length; i++)
            {
                if (!string.IsNullOrEmpty(cropOverrides[i].id) &&
                    string.Equals(cropOverrides[i].id, id, StringComparison.OrdinalIgnoreCase))
                {
                    var f = cropOverrides[i].focus;
                    return new Vector2(Mathf.Clamp01(f.x), Mathf.Clamp01(f.y));
                }
            }
        }
        return new Vector2(Mathf.Clamp01(defaultFocus.x), Mathf.Clamp01(defaultFocus.y));
    }

    void ApplyCoverCrop(RawImage img, Texture2D tex, float targetAspect, Vector2 focus01)
    {
        if (tex == null) { img.uvRect = new Rect(0,0,1,1); return; }

        float srcAspect = (float)tex.width / Mathf.Max(1, tex.height);
        focus01 = new Vector2(Mathf.Clamp01(focus01.x), Mathf.Clamp01(focus01.y));

        if (srcAspect > targetAspect)
        {
            // Source wider → crop horizontally
            float uvW = targetAspect / srcAspect;
            float u   = Mathf.Clamp(focus01.x - uvW * 0.5f, 0f, 1f - uvW);
            img.uvRect = new Rect(u, 0f, uvW, 1f);
        }
        else
        {
            // Source taller → crop vertically
            float uvH = srcAspect / targetAspect;
            float v   = Mathf.Clamp(focus01.y - uvH * 0.5f, 0f, 1f - uvH);
            img.uvRect = new Rect(0f, v, 1f, uvH);
        }
    }

    // ---------- Helpers ----------
    void SafeSet(TMP_Text t, string s) { if (t) t.text = s ?? ""; }

    string Modes(GameFlags f)
    {
        bool s = (f & GameFlags.Solo)     != 0;
        bool v = (f & GameFlags.Versus2P) != 0;
        bool c = (f & GameFlags.Coop2P)   != 0;
        bool a = (f & GameFlags.Alt2P)    != 0;
        string m = "";
        if (s) m += "1P ";
        if (v) m += "2P VS ";
        if (c) m += "2P COOP ";
        if (a) m += "2P ALT ";
        return m.Trim();
    }

    string BuildStats(GameFlags f)
    {
        bool has1P = (f & GameFlags.Solo) != 0;
        bool has2P = (f & (GameFlags.Versus2P | GameFlags.Coop2P | GameFlags.Alt2P)) != 0;

        if (has1P && has2P) return "1P best: –   2P best: –";
        if (has1P)          return "1P best: –";
        if (has2P)          return "2P best: –";
        return ""; // exotic: no score categories
    }

    // Deterministic, nice-ish palette from game number
    Color AutoColor(GameDef def)
    {
        // Spread hues around the wheel; keep saturation/value punchy
        float hue = ((def.number * 37) % 360) / 360f;
        var c = Color.HSVToRGB(hue, 0.65f, 0.95f);
        c.a = 1f;
        return c;
    }

    void ApplyColorBlock(Selectable s, Color accent)
    {
        var cb = s.colors;
        cb.colorMultiplier = 1f;
        cb.fadeDuration    = 0.08f;
        cb.normalColor     = new Color(1,1,1,1);           // we tint via frame; keep neutral here
        cb.highlightedColor= Color.Lerp(accent, Color.white, 0.35f);
        cb.selectedColor   = Color.Lerp(accent, Color.white, 0.20f);
        cb.pressedColor    = Color.Lerp(accent, Color.black, 0.20f);
        cb.disabledColor   = new Color(0.5f,0.5f,0.5f,0.5f);
        s.transition = Selectable.Transition.ColorTint;
        s.colors = cb;
    }

    // ============================================================
    // ============  SHARED STATIC CARTRIDGE PAINTER  =============
    // ============================================================
    // Lets other UI (in-game menu) render the *same* cartridge without a new script.
    public static void PaintCartridgeForGame(
        GameDef def,
        RawImage cartridgeImage,
        TMP_Text cartTitleText,
        TMP_Text numberText)
    {
        PaintCartridgeForGame(def, cartridgeImage, cartTitleText, numberText,
                              "labels", new Vector2(0.5f, 0.5f), null);
    }

    public static void PaintCartridgeForGame(
        GameDef def,
        RawImage cartridgeImage,
        TMP_Text cartTitleText,
        TMP_Text numberText,
        string labelsFolder,
        Vector2 defaultFocus,
        LabelCropOverride[] cropOverrides)
    {
        if (def == null || cartridgeImage == null) return;

        if (cartTitleText) cartTitleText.text = def.title ?? "";
        if (numberText)    numberText.text    = $"#{def.number}";

        // Load texture from StreamingAssets/labels or /covers using id or title
        Texture2D tex = TryLoadLabelTextureStatic(def, labelsFolder);
        cartridgeImage.texture = tex;
        cartridgeImage.uvRect  = new Rect(0,0,1,1);

        if (tex == null) return;

        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;

        var rt = cartridgeImage.rectTransform.rect;
        float targetAspect = (rt.height <= 0f) ? 1.0f : (rt.width / rt.height);

        var focus = FindFocusStatic(def.id, defaultFocus, cropOverrides);
        ApplyCoverCropStatic(cartridgeImage, tex, targetAspect, focus);
    }

    static Texture2D TryLoadLabelTextureStatic(GameDef def, string labelsFolder)
    {
        string sa = Application.streamingAssetsPath;

        Texture2D Try(string p)
        {
            if (!File.Exists(p)) return null;
            var bytes = File.ReadAllBytes(p);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(bytes, false);
            t.name = Path.GetFileNameWithoutExtension(p);
            return t;
        }

        string[] names   = { def.id, def.title };
        string[] exts    = { ".png", ".jpg", ".jpeg" };
        string[] folders = { Path.Combine(sa, labelsFolder), Path.Combine(sa, "covers") };

        foreach (var folder in folders)
        foreach (var n in names)
        {
            if (string.IsNullOrEmpty(n)) continue;
            foreach (var e in exts)
            {
                var tex = Try(Path.Combine(folder, n + e));
                if (tex != null) return tex;
            }
        }
        return null;
    }

    static Vector2 FindFocusStatic(string id, Vector2 defaultFocus, LabelCropOverride[] cropOverrides)
    {
        if (cropOverrides != null)
        {
            for (int i = 0; i < cropOverrides.Length; i++)
            {
                if (!string.IsNullOrEmpty(cropOverrides[i].id) &&
                    string.Equals(cropOverrides[i].id, id, StringComparison.OrdinalIgnoreCase))
                {
                    var f = cropOverrides[i].focus;
                    return new Vector2(Mathf.Clamp01(f.x), Mathf.Clamp01(f.y));
                }
            }
        }
        return new Vector2(Mathf.Clamp01(defaultFocus.x), Mathf.Clamp01(defaultFocus.y));
    }

    public static Color AccentFor(GameDef def)
    {
        float hue = ((def.number * 37) % 360) / 360f;
        var c = Color.HSVToRGB(hue, 0.65f, 0.95f);
        c.a = 1f;
        return c;
    }

    static void ApplyCoverCropStatic(RawImage img, Texture2D tex, float targetAspect, Vector2 focus01)
    {
        if (tex == null) { img.uvRect = new Rect(0,0,1,1); return; }

        float srcAspect = (float)tex.width / Mathf.Max(1, tex.height);
        focus01 = new Vector2(Mathf.Clamp01(focus01.x), Mathf.Clamp01(focus01.y));

        if (srcAspect > targetAspect)
        {
            float uvW = targetAspect / srcAspect;
            float u   = Mathf.Clamp(focus01.x - uvW * 0.5f, 0f, 1f - uvW);
            img.uvRect = new Rect(u, 0f, uvW, 1f);
        }
        else
        {
            float uvH = srcAspect / targetAspect;
            float v   = Mathf.Clamp(focus01.y - uvH * 0.5f, 0f, 1f - uvH);
            img.uvRect = new Rect(0f, v, 1f, uvH);
        }
    }
}
