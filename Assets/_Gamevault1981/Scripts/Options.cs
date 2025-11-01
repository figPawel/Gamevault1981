// Options.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class Options : MonoBehaviour
{
    [Header("Scene / UI references")]
    public UIManager ui;
    public RectTransform content;
    public Button btnHeaderOptions;
    public Button btnHeaderLeaderboards;
    public Button btnHeaderBack;
    public GameObject optionBandPrefab;

    [Header("Behavior")]
    public string containerName = "OptionsContainer";

    RectTransform _container;
    readonly List<OptionBand> _rows = new List<OptionBand>();
    bool _built, _visible;
    float _guardAfterOpen = 0f;

    // audio/video
    float _musicVol;
    float _sfxVol;
    bool  _crtOn;

    // soundtrack toggles
    bool  _playOnSelection, _playOnTitle, _playInGame;
    float _inGameMusicVol;
    bool  _allowChipInGame;

    // input
    bool _invertY1, _invertY2;
    int  _mapP1, _mapP2;
    List<string> _deviceChoices = new List<string>();

    void Awake()
    {
        if (!ui) ui = GetComponent<UIManager>();

        _musicVol = PlayerPrefs.GetFloat("opt_music", 0.8f);
        _sfxVol   = PlayerPrefs.GetFloat("opt_sfx", 1.0f);
        _crtOn    = PlayerPrefs.GetInt("opt_crt", 1) != 0;

        _playOnSelection = PlayerPrefs.GetInt("msk_sel",   1) != 0;
        _playOnTitle     = PlayerPrefs.GetInt("msk_title", 1) != 0;
        _playInGame      = PlayerPrefs.GetInt("msk_game",  0) != 0;
        _inGameMusicVol  = PlayerPrefs.GetFloat("msk_game_vol", 0.35f);
        _allowChipInGame = PlayerPrefs.GetInt("chip_ingame", 1) != 0;

        _invertY1   = PlayerPrefs.GetInt("invY1", 0) != 0;
        _invertY2   = PlayerPrefs.GetInt("invY2", 0) != 0;
        _mapP1      = PlayerPrefs.GetInt("map_p1", 0);
        _mapP2      = PlayerPrefs.GetInt("map_p2", 1);

        MetaGameManager.I?.SetMusicVolumeLinear(_musicVol);
        MetaGameManager.I?.SetSfxVolumeLinear(_sfxVol);
    }

    void Start()
    {
        if (!content && ui) content = ui.listRoot;
        if (!btnHeaderOptions && ui) btnHeaderOptions = ui.btnTopOptions;
        if (!btnHeaderLeaderboards && ui) btnHeaderLeaderboards = ui.btnTopLeaderboards;
        if (!btnHeaderBack && ui) btnHeaderBack = ui.btnBackFromSelect;

        if (btnHeaderOptions)
        {
            btnHeaderOptions.onClick.RemoveAllListeners();
            btnHeaderOptions.onClick.AddListener(ShowOptionsNow);
        }
    }

    void Update()
    {
        if (!_visible || _container == null) return;

        if (_guardAfterOpen > 0f) { _guardAfterOpen -= Time.unscaledDeltaTime; return; }

        var es = EventSystem.current;
        var sel = es ? es.currentSelectedGameObject : null;
        bool inMine = sel && sel.transform.IsChildOf(_container);
        if (!inMine) Hide();
    }

    // ---------- build / show / hide ----------
    void EnsureContainer()
    {
        if (_container) return;

        var go = new GameObject(containerName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        _container = go.GetComponent<RectTransform>();
        _container.SetParent(content, false);

        if (btnHeaderBack)
        {
            var headerGroup = btnHeaderBack.transform.parent as RectTransform;
            int headerIndex = headerGroup ? headerGroup.GetSiblingIndex() : 0;
            _container.SetSiblingIndex(headerIndex + 1);
        }

        var v = go.GetComponent<VerticalLayoutGroup>();
        v.childControlHeight = true; v.childForceExpandHeight = false;
        v.childControlWidth  = true; v.childForceExpandWidth  = true;
        v.spacing = -5f;
        v.padding = new RectOffset(0,0,0,0);

        var f = go.GetComponent<ContentSizeFitter>();
        f.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        f.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        go.SetActive(false);
    }

    void BuildOnce()
    {
        if (_built) return;
        EnsureContainer();
        _rows.Clear();

        RebuildDeviceChoices();
        EnsureDistinctMappings();

        // AUDIO / VIDEO
        AddSliderRow("Master Music Volume", () => _musicVol, v => {
            _musicVol = v;
            PlayerPrefs.SetFloat("opt_music", _musicVol);
            MetaGameManager.I?.SetMusicVolumeLinear(_musicVol);
        });
        AddSliderRow("Master SFX Volume",   () => _sfxVol,   v => {
            _sfxVol = v;
            PlayerPrefs.SetFloat("opt_sfx", _sfxVol);
            MetaGameManager.I?.SetSfxVolumeLinear(_sfxVol);
        });

        // SOUNDTRACK ROUTING
        AddToggleRow("Play soundtrack on Selection", () => _playOnSelection, on => { _playOnSelection = on; PlayerPrefs.SetInt("msk_sel", on?1:0); });
        AddToggleRow("Play soundtrack on Title",     () => _playOnTitle,     on => { _playOnTitle = on; PlayerPrefs.SetInt("msk_title", on?1:0); });
        AddToggleRow("Play soundtrack during Game",  () => _playInGame,      on => { _playInGame  = on; PlayerPrefs.SetInt("msk_game", on?1:0); });
        AddSliderRow("In-game music volume", () => _inGameMusicVol, v => { _inGameMusicVol = v; PlayerPrefs.SetFloat("msk_game_vol", v); });
        AddToggleRow("Allow old-school chiptune music in-game", () => _allowChipInGame, on => {
            _allowChipInGame = on;
            PlayerPrefs.SetInt("chip_ingame", on ? 1 : 0);
        });

        // INPUT
        AddToggleRow("Invert Y — Player 1", () => _invertY1, on => { _invertY1 = on; PlayerPrefs.SetInt("invY1", on?1:0); });
        AddToggleRow("Invert Y — Player 2", () => _invertY2, on => { _invertY2 = on; PlayerPrefs.SetInt("invY2", on?1:0); });

        // Device rows that SKIP the other player's current device while cycling
        AddPlayerDeviceRow("Player 1 device", 1);
        AddPlayerDeviceRow("Player 2 device", 2);

        WireNavigation();
        _built = true;
    }

    public void ShowOptionsNow()
    {
        if (ui && (ui.selectRoot == null || ui.selectRoot.alpha < 0.5f)) return;

        RebuildDeviceChoices();
        EnsureDistinctMappings();

        if (_built) foreach (var r in _rows) r.Refresh(); else BuildOnce();

        EnsureContainer();

        _container.gameObject.SetActive(true);
        _visible = true;
        _guardAfterOpen = 0.12f;

        Canvas.ForceUpdateCanvases();
        if (ui && ui.selectScroll) ui.selectScroll.verticalNormalizedPosition = 1f;

        if (_rows.Count > 0 && _rows[0] && _rows[0].bandButton)
        {
            EventSystem.current?.SetSelectedGameObject(_rows[0].bandButton.gameObject);
            ui?.ScrollToBand(_rows[0].Rect);
        }
    }

    void Hide()
    {
        if (!_visible || !_container) return;
        _container.gameObject.SetActive(false);
        _visible = false;

        var firstGameBtn = FindFirstGameBandButton();

        Canvas.ForceUpdateCanvases();
        if (content) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (firstGameBtn) ui?.ScrollToBand(firstGameBtn.transform as RectTransform);

        if (btnHeaderBack)
        {
            var n = btnHeaderBack.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = firstGameBtn;
            n.selectOnUp   = btnHeaderLeaderboards ? btnHeaderLeaderboards : n.selectOnUp;
            btnHeaderBack.navigation = n;
        }
        if (firstGameBtn)
        {
            var n = firstGameBtn.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnUp = btnHeaderBack ? btnHeaderBack : btnHeaderOptions;
            firstGameBtn.navigation = n;
        }
    }

    // ---------- device list & constraints ----------
    void RebuildDeviceChoices()
    {
        _deviceChoices.Clear();
        _deviceChoices.Add("Keyboard 1");
        _deviceChoices.Add("Keyboard 2");
        _deviceChoices.Add("Mouse");

       int gp = Gamepad.all.Count;
        for (int i = 0; i < gp; i++) _deviceChoices.Add($"Gamepad {i + 1}");

        _mapP1 = ClampChoice(_mapP1);
        _mapP2 = ClampChoice(_mapP2);
    }

    int ClampChoice(int i)
    {
        int count = _deviceChoices.Count;
        if (count <= 0) return 0;
        int m = i % count; if (m < 0) m += count;
        return m;
    }

    void EnsureDistinctMappings()
    {
        if (_deviceChoices.Count <= 1) { _mapP1 = 0; _mapP2 = 0; return; }
        if (_mapP1 == _mapP2)
            _mapP2 = (_mapP1 + 1) % _deviceChoices.Count;
        PlayerPrefs.SetInt("map_p1", _mapP1);
        PlayerPrefs.SetInt("map_p2", _mapP2);
    }

    void ResolveConflict(int pushOther)
    {
        if (_deviceChoices.Count <= 1) return;

        if (pushOther == 2 && _mapP1 == _mapP2)
        {
            _mapP2 = (_mapP1 + 1) % _deviceChoices.Count;
            PlayerPrefs.SetInt("map_p2", _mapP2);
        }
        else if (pushOther == 1 && _mapP1 == _mapP2)
        {
            _mapP1 = (_mapP2 + 1) % _deviceChoices.Count;
            PlayerPrefs.SetInt("map_p1", _mapP1);
        }
        foreach (var r in _rows) r.Refresh();
    }

    // ---------- row builders ----------
    void AddToggleRow(string label, Func<bool> get, Action<bool> set)
    {
        bool Get() => get();
        void FlipLeft()  { set(!Get()); RefreshLast(); }
        void FlipRight() { set(!Get()); RefreshLast(); }
        string Value() => Get() ? "ON" : "OFF";
        MakeRow(label, Value, FlipLeft, FlipRight);
    }

    void AddChoiceRow(string label, string[] choices, Func<int> get, Action<int> set)
    {
        int count = (choices != null) ? choices.Length : 0;

        int Clamp(int i)
        {
            if (count <= 0) return 0;
            int m = i % count;
            return (m < 0) ? m + count : m;
        }

        void Left()  { set(Clamp(get() - 1)); RefreshLast(); }
        void Right() { set(Clamp(get() + 1)); RefreshLast(); }
        string Value() => (count == 0) ? "-" : choices[Clamp(get())];

        MakeRow(label, Value, Left, Right);
    }

    void AddSliderRow(string label, Func<float> get, Action<float> set)
    {
        float[] steps = { 0f, 0.12f, 0.25f, 0.4f, 0.6f, 0.8f, 1f };

        int IndexOf(float v)
        {
            int best = 0; float d = 999f;
            for (int i = 0; i < steps.Length; i++)
            {
                float dd = Mathf.Abs(steps[i] - v);
                if (dd < d) { d = dd; best = i; }
            }
            return best;
        }

        int  Wrap(int i) { int m = i % steps.Length; return m < 0 ? m + steps.Length : m; }
        void SetIdx(int i) { set(steps[Wrap(i)]); }
        void Left()  { SetIdx(IndexOf(get()) - 1); RefreshLast(); }
        void Right() { SetIdx(IndexOf(get()) + 1); RefreshLast(); }
        string Value() => $"{Mathf.RoundToInt(Mathf.Clamp01(get()) * 100f)}%";

        MakeRow(label, Value, Left, Right);
    }

    Color AccentForIndex(int i)
    {
        float hue = (((i + 1) * 37) % 360) / 360f;
        var c = Color.HSVToRGB(hue, 0.65f, 0.95f);
        c.a = 1f;
        return c;
    }

    void MakeRow(string label, Func<string> getValue, Action onLeft, Action onRight)
    {
        var go = Instantiate(optionBandPrefab, _container);
        var row = go.GetComponent<OptionBand>();
        row.accent = AccentForIndex(_rows.Count);
        row.Bind(label, getValue, onLeft, onRight);
        row.onSelected = ui != null ? (Action<RectTransform>)ui.ScrollToBand : null;
        _rows.Add(row);
    }

    void RefreshLast()
    {
        if (_rows.Count > 0) _rows[_rows.Count - 1].Refresh();
    }

    // ---------- navigation & scroll ----------
    void WireNavigation()
    {
        if (_rows.Count == 0) return;

        var buttons = new List<Button>();
        foreach (var r in _rows) if (r && r.bandButton) buttons.Add(r.bandButton);

        Button upAnchor = btnHeaderBack ? btnHeaderBack
                          : (btnHeaderLeaderboards ? btnHeaderLeaderboards : btnHeaderOptions);

        var firstGame = FindFirstGameBandButton();

        for (int i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            var n = new Navigation { mode = Navigation.Mode.Explicit };
            n.selectOnUp   = (i > 0) ? buttons[i - 1] : upAnchor;
            n.selectOnDown = (i < buttons.Count - 1) ? buttons[i + 1] : firstGame;
            b.navigation = n;
        }

        if (btnHeaderBack)
        {
            var n = btnHeaderBack.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = buttons[0];
            n.selectOnUp   = btnHeaderLeaderboards ? btnHeaderLeaderboards : n.selectOnUp;
            btnHeaderBack.navigation = n;
        }

        if (firstGame)
        {
            var n = firstGame.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnUp = buttons[buttons.Count - 1];
            firstGame.navigation = n;
        }
    }

    Button FindFirstGameBandButton()
    {
        if (!content) return null;
        var bands = content.GetComponentsInChildren<UISelectBand>(true);
        foreach (var b in bands)
            if (b && b.bandButton && b.gameObject.activeInHierarchy)
                return b.bandButton;
        return null;
    }

    // ---------- Player device rows (skip the other player's choice) ----------
    void AddPlayerDeviceRow(string label, int playerIndex)
    {
        string Value()
        {
            int idx = playerIndex == 1 ? _mapP1 : _mapP2;
            if (_deviceChoices.Count == 0) return "-";
            idx = ClampChoice(idx);
            return _deviceChoices[idx];
        }

        int OtherMap() => playerIndex == 1 ? _mapP2 : _mapP1;
        int ThisMap()  => playerIndex == 1 ? _mapP1 : _mapP2;

        int NextFreeFrom(int current, int dir, int other)
        {
            int count = _deviceChoices.Count;
            if (count <= 1) return current;
            int i = current;
            for (int step = 0; step < count; step++)
            {
                i = ClampChoice(i + dir);
                if (i != other) return i;
            }
            return current;
        }

        void SetThis(int idx)
        {
            idx = ClampChoice(idx);
            if (playerIndex == 1)
            {
                _mapP1 = idx;
                PlayerPrefs.SetInt("map_p1", _mapP1);
            }
            else
            {
                _mapP2 = idx;
                PlayerPrefs.SetInt("map_p2", _mapP2);
            }
        }

        void Left()
        {
            int next = NextFreeFrom(ThisMap(), -1, OtherMap());
            SetThis(next);
            RefreshLast();
        }

        void Right()
        {
            int next = NextFreeFrom(ThisMap(), +1, OtherMap());
            SetThis(next);
            RefreshLast();
        }

        MakeRow(label, Value, Left, Right);
    }
}
