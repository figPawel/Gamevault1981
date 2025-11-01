// UISelectBand.cs — FULL FILE
// Shows countdown "Unlocks in ..." OR score shortfall, and a pulsing NEW! badge until played.
// Replaces your current UISelectBand.cs. :contentReference[oaicite:1]{index=1}
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;
using System.IO;

public class UISelectBand : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerClickHandler, ISubmitHandler
{
    [Header("Band root (focus/click target)")]
    public Button bandButton;
    public Image  bandHighlightFrame;

    [Header("Cartridge visuals")]
    public RawImage cartridgeImage;
    public TMP_Text cartTitleText;
    public TMP_Text numberText;

    [Header("Right-side texts")]
    public TMP_Text titleText;
    public TMP_Text descText;
    public TMP_Text modesText;
    public TMP_Text statsText;

    [Header("Actions (optional)")]
    public Button btnPlay;

    [Header("Lock overlay text")]
    [Tooltip("TMP placed where you want the countdown/shortfall text.")]
    public TMP_Text unlockCountdownText;

    [Header("Badge for newly unlocked & unplayed (beyond defaults)")]
    public TMP_Text newBadgeText; // optional; set to a TMP on top of the cartridge (e.g., “NEW!”)

    [Header("Hide When Locked")]
    public GameObject[] hideWhenLocked;

    [Header("Label loading (StreamingAssets)")]
    [SerializeField] string labelsFolder = "labels";
    [SerializeField] Vector2 defaultFocus = new Vector2(0.5f, 0.5f);
    [Serializable] public struct LabelCropOverride { public string id; public Vector2 focus; }
    [SerializeField] LabelCropOverride[] cropOverrides;

    [Header("Highlight color")]
    [Tooltip("Leave alpha=0 to auto-generate from game number.")]
    public Color highlightOverride = new Color(0,0,0,0);

    public Action<RectTransform> onSelected;
    public RectTransform Rect => transform as RectTransform;

    GameDef _def;
    MetaGameManager _meta;
    Color _highlight;
    Color _dim;

    bool _locked;
    float _nextTick;
    string _lastCountdownText = "";
    float _badgePulse;

    public void Bind(GameDef def, MetaGameManager meta)
    {
        _def = def;
        _meta = meta;

        _highlight = (highlightOverride.a > 0.01f) ? highlightOverride : AutoColor(def);
        _dim       = new Color(_highlight.r, _highlight.g, _highlight.b, 0.18f);

        SafeSet(titleText,  def.title);
        SafeSet(descText,   def.desc);
        SafeSet(modesText,  Modes(def.flags));
        SafeSet(numberText, $"#{def.number}");
        SafeSet(cartTitleText, def.title);

        LoadAndCropLabel();

        if (bandButton)
        {
            bandButton.onClick.RemoveAllListeners();
            bandButton.onClick.AddListener(() => TryStartGame());
            ApplyColorBlock(bandButton, _highlight);
            SetHighlight(false);
        }

        if (btnPlay)
        {
            btnPlay.onClick.RemoveAllListeners();
            btnPlay.onClick.AddListener(() => TryStartGame());
            var nPlay = btnPlay.navigation;
            nPlay.mode = Navigation.Mode.Explicit;
            nPlay.selectOnLeft = bandButton ? bandButton : null;
            nPlay.selectOnRight = bandButton ? bandButton : null;
            btnPlay.navigation = nPlay;
        }

        if (bandButton && !bandButton.targetGraphic)
        {
            bandButton.targetGraphic = (bandHighlightFrame ? (Graphic)bandHighlightFrame
                                         : (Graphic)bandButton.GetComponent<Image>())
                                         ?? (Graphic)cartridgeImage;
        }

        UpdateLockVisuals(force:true);
        RefreshStats();
        UpdateNewBadge(force:true);
    }

    public void RefreshStats()
    {
        if (statsText == null || _def == null) return;
        statsText.text = BuildStats(_def.flags);
    }

    public void OnSelect(BaseEventData e)
    {
        SetHighlight(true);
        onSelected?.Invoke(Rect);
    }

    public void OnDeselect(BaseEventData e) => SetHighlight(false);

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        TryStartGame();
    }

    public void OnSubmit(BaseEventData e) { TryStartGame(); }

    void TryStartGame()
    {
        if (_locked || (bandButton && !bandButton.interactable))
        {
            _meta?.audioBus?.BeepOnce(180f, 0.06f, 0.20f);
            return;
        }
        _meta?.StartGame(_def);
        UpdateNewBadge(force:true); // clear on next paint after meta marks played
    }

    void SetHighlight(bool on)
    {
        if (bandHighlightFrame)
            bandHighlightFrame.color = on ? _highlight : _dim;

        if (cartTitleText)
            cartTitleText.color = on ? Color.Lerp(_highlight, Color.white, 0.35f) : new Color(1,1,1,0.85f);
    }

    void Update()
    {
        if (_def == null || _meta == null) return;

        if (Time.unscaledTime >= _nextTick)
        {
            _nextTick = Time.unscaledTime + 0.5f;
            UpdateLockVisuals(force:false);
        }

        // badge pulse
        if (newBadgeText && newBadgeText.gameObject.activeSelf)
        {
            _badgePulse += Time.unscaledDeltaTime * 3.0f;
            float s = 1.0f + 0.05f * Mathf.Sin(_badgePulse * 2.1f);
            float a = 0.70f + 0.30f * Mathf.Sin(_badgePulse);
            newBadgeText.rectTransform.localScale = new Vector3(s, s, 1f);
            var c = newBadgeText.color; c.a = a; newBadgeText.color = c;
        }
    }

    void UpdateNewBadge(bool force)
    {
        if (!newBadgeText) return;
        bool show = _meta.IsUnlocked(_def)
                  && _def.number > Mathf.Max(0, _meta.AlwaysUnlockedFirstN)
                  && !_meta.HasPlayed(_def);
        if (force || newBadgeText.gameObject.activeSelf != show)
        {
            newBadgeText.gameObject.SetActive(show);
            if (show && string.IsNullOrEmpty(newBadgeText.text)) newBadgeText.text = "NEW!";
        }
    }

    void UpdateLockVisuals(bool force)
    {
        bool newLocked = !_meta.IsUnlocked(_def);

        if (force || newLocked != _locked)
        {
            _locked = newLocked;

            if (unlockCountdownText)
                unlockCountdownText.gameObject.SetActive(_locked);

            if (hideWhenLocked != null)
                for (int i = 0; i < hideWhenLocked.Length; i++)
                    if (hideWhenLocked[i]) hideWhenLocked[i].SetActive(!_locked);

            if (bandButton) bandButton.interactable = !_locked;
            if (btnPlay)    btnPlay.interactable    = !_locked;
        }

        if (_locked && unlockCountdownText)
        {
            var left = _meta.TimeUntilUnlock(_def);
            if (left == TimeSpan.MaxValue) // time unlock disabled → just show score path
            {
                int shortfall = _meta.ScoreShortfallForUnlock(_def);
                unlockCountdownText.text = (shortfall == int.MaxValue)
                    ? "Unlock requirements disabled"
                    : $"Score {shortfall:N0} more to unlock!";
            }
            else
            {
                string txt = FormatCountdown(left);
                int shortfall = _meta.ScoreShortfallForUnlock(_def);
                string scoreClause = (shortfall == int.MaxValue) ? "" : $"  or score {shortfall:N0}!";
                string final = $"Unlocks in {txt}{scoreClause}";
                if (!string.Equals(final, _lastCountdownText))
                {
                    _lastCountdownText = final;
                    unlockCountdownText.text = final;
                }
            }
        }
        else
        {
            UpdateNewBadge(force:false);
        }
    }

    static string FormatCountdown(TimeSpan t)
    {
        int days = Mathf.FloorToInt((float)t.TotalDays);
        int hh = t.Hours, mm = t.Minutes, ss = t.Seconds;
        return $"{days:00}:{hh:00}:{mm:00}:{ss:00}";
    }

    void LoadAndCropLabel()
    {
        if (!cartridgeImage || _def == null) return;

        Texture2D tex = TryLoadLabelTexture();
        cartridgeImage.texture = tex;
        cartridgeImage.uvRect  = new Rect(0,0,1,1);

        if (tex == null) return;

        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;
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

        string[] names = { _def.id, _def.title };
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
        if (_def == null) return "";
        string id = _def.id ?? "";
        bool has1P = (f & GameFlags.Solo) != 0;
        bool has2P = (f & (GameFlags.Versus2P | GameFlags.Coop2P | GameFlags.Alt2P)) != 0;

        int hi1 = has1P ? PlayerPrefs.GetInt("hi1p_" + id, 0) : 0;
        int hi2 = has2P ? PlayerPrefs.GetInt("hi2p_" + id, 0) : 0;

        if (has1P && has2P) return $"1P best: {hi1}   2P best: {hi2}";
        if (has1P)          return $"1P best: {hi1}";
        if (has2P)          return $"2P best: {hi2}";
        return "";
    }

    Color AutoColor(GameDef def)
    {
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
        cb.normalColor     = new Color(1,1,1,1);
        cb.highlightedColor= Color.Lerp(accent, Color.white, 0.35f);
        cb.selectedColor   = Color.Lerp(accent, Color.white, 0.20f);
        cb.pressedColor    = Color.Lerp(accent, Color.black, 0.20f);
        cb.disabledColor   = new Color(0.5f,0.5f,0.5f,0.5f);
        s.transition = Selectable.Transition.ColorTint;
        s.colors = cb;
    }

    // Static helpers reused by UIManager for the in-game cartridge preview
    public static void PaintCartridgeForGame(GameDef def, RawImage cartridgeImage, TMP_Text cartTitleText, TMP_Text numberText)
    { PaintCartridgeForGame(def, cartridgeImage, cartTitleText, numberText, "labels"); }

    public static void PaintCartridgeForGame(GameDef def, RawImage cartridgeImage, TMP_Text cartTitleText, TMP_Text numberText, string labelsFolder)
    {
        if (def == null || cartridgeImage == null) return;

        if (cartTitleText) cartTitleText.text = def.title ?? "";
        if (numberText)    numberText.text    = $"#{def.number}";

        Texture2D tex = TryLoadLabelTextureStatic(def, labelsFolder);
        cartridgeImage.texture = tex;
        cartridgeImage.uvRect  = new Rect(0, 0, 1, 1);

        if (tex == null) return;

        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;
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

    public static Color AccentFor(GameDef def)
    {
        float hue = ((def.number * 37) % 360) / 360f;
        var c = Color.HSVToRGB(hue, 0.65f, 0.95f);
        c.a = 1f;
        return c;
    }
}
