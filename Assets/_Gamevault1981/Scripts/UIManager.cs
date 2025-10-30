using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Title")]
    public CanvasGroup titleRoot;
    public Button btnPlay;
    public Button btnOptions;
    public Button btnQuit;

    [Header("Selection")]
    public CanvasGroup selectRoot;
    public RectTransform listRoot;          // Content
    public GameObject bandPrefab;
    public Button btnBackFromSelect;
    public Button btnTopOptions;
    public Button btnTopLeaderboards;
    public ScrollRect selectScroll;
    public RectTransform selectViewport;

    [Tooltip("Pixels of breathing room when centering a selected band.")]
    public float Viewportpad = 6.0f;

    [Header("In-Game Menu")]
    public CanvasGroup inGameMenuRoot;
    public Button btnInGameSolo;
    public Button btnInGameVs;
    public Button btnInGameCoop;
    public Button btnInGameAlt;
    public Button btnInGameQuit;

    [Header("In-Game Cartridge (wire a blank one here)")]
    public RawImage igCartridgeImage;
    public TMP_Text igCartTitle;
    public TMP_Text igCartNumber;
    [SerializeField] string igLabelsFolder = "labels";
    [SerializeField] Vector2 igDefaultFocus = new Vector2(0.5f, 0.5f);
    [SerializeField] UISelectBand.LabelCropOverride[] igCropOverrides;

    [Header("Edge Spacers")]
    public bool UseAutoEdgeMargins = false;
    [Range(0f, 1f)] public float AutoEdgeFactor = 0.50f;
    public float TopEdgeMargin = 0f;
    public float BottomEdgeMargin = 0f;

    [Header("Optional / Effects")]
    public GameObject crtEffectRoot;

    [Header("Optional / Small HUD")]
    public TMP_Text optionsToast;

    MetaGameManager _meta;
    readonly List<GameObject> _bands = new List<GameObject>();

    float _musicVol = 0.8f;
    float _sfxVol   = 1.0f;
    float _toastT;

    // Input routing
    float _backHold = 0f;
    const float BackHoldToQuit = 0.35f;

    RectTransform _spacerTop, _spacerBottom;
    bool _builtList = false;

    void Awake()
    {
        _musicVol = PlayerPrefs.GetFloat("opt_music", 0.8f);
        _sfxVol = PlayerPrefs.GetFloat("opt_sfx", 1.0f);
        AudioListener.volume = _musicVol;
        RetroAudio.GlobalSfxVolume = _sfxVol;
        if (crtEffectRoot) crtEffectRoot.SetActive(PlayerPrefs.GetInt("opt_crt", 1) != 0);
        if (optionsToast) optionsToast.gameObject.SetActive(false);
    }

    void Start()
    {
        ClearSelection();
        WireTitleNavigation();
        ShowInGameMenu(false);
        ShowSelect(false);
        ShowTitle(true);
        var first = FirstTitleButton();
        if (first) EventSystem.current?.SetSelectedGameObject(first.gameObject);
    }

    void Update()
    {
        // toast fade
        if (_toastT > 0f && optionsToast)
        {
            _toastT -= Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(Mathf.Min(_toastT, 0.25f) / 0.25f);
            var c = optionsToast.color; c.a = a; optionsToast.color = c;
            if (_toastT <= 0f) optionsToast.gameObject.SetActive(false);
        }

        // -------- Unified input routing (Back & Pause) --------
        bool backDown = false, backHeld = false, pauseDown = false;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var gp = UnityEngine.InputSystem.Gamepad.current;
        if (gp != null)
        {
            if (gp.bButton.wasPressedThisFrame) backDown = true;
            if (gp.bButton.isPressed) backHeld = true;
            if (gp.startButton.wasPressedThisFrame) pauseDown = true;
        }
#else
        if (Input.GetKeyDown(KeyCode.JoystickButton1)) backDown = true; // B
        if (Input.GetKey(KeyCode.JoystickButton1)) backHeld = true;     // B held
        if (Input.GetKeyDown(KeyCode.JoystickButton7)) pauseDown = true; // Start
#endif

        // Keyboard / mouse fallbacks
        if (Input.GetKeyDown(KeyCode.Backspace)) backDown = true;
        if (Input.GetKey(KeyCode.Backspace)) backHeld = true;
        if (Input.GetMouseButtonDown(1)) backDown = true;
        if (Input.GetMouseButton(1)) backHeld = true;

        if (Input.GetKeyDown(KeyCode.Escape)) pauseDown = true;

        // Back hold-to-quit timer
        if (backHeld) _backHold += Time.unscaledDeltaTime;
        else _backHold = 0f;

        if (_backHold >= BackHoldToQuit)
        {
            _backHold = 0f;
            QuitToBandsNow();
        }
        else
        {
            if (backDown) HandleBackSinglePress();
            if (pauseDown) HandlePauseToggle();
        }

        // Keep viewport pinned to top when a header button is focused
        if (selectRoot && selectRoot.interactable && selectScroll)
        {
            var es = EventSystem.current;
            var sel = es ? es.currentSelectedGameObject : null;
            if (sel &&
                ((btnTopLeaderboards && sel == btnTopLeaderboards.gameObject) ||
                 (btnTopOptions && sel == btnTopOptions.gameObject) ||
                 (btnBackFromSelect && sel == btnBackFromSelect.gameObject)))
            {
                selectScroll.verticalNormalizedPosition = 1f;
            }
        }
        if (TitleActive())
        {
            var es = EventSystem.current;
            if (es && (!es.currentSelectedGameObject || !es.currentSelectedGameObject.activeInHierarchy))
            {
                var first = FirstTitleButton();
                if (first) es.SetSelectedGameObject(first.gameObject);
            }
        }
        StickyFocusGuard();
    }

    public void Init(MetaGameManager meta)
    {
        _meta = meta;

        if (btnPlay)    btnPlay.onClick.AddListener(_meta.OpenSelection);
        if (btnQuit)    btnQuit.onClick.AddListener(_meta.QuitApp);
        if (btnOptions) btnOptions.onClick.AddListener(CycleBasicOptions);
        if (btnBackFromSelect) btnBackFromSelect.onClick.AddListener(_meta.OpenTitle);

        ShowTitle(false);
        ShowSelect(false);
        ShowInGameMenu(false);
    }

    // ---------- Visibility ----------
    public void ShowTitle(bool on)
    {
        if (!titleRoot) return;

        if (on)
        {
            if (selectRoot)   { selectRoot.interactable = false;   selectRoot.blocksRaycasts = false; }
            if (inGameMenuRoot){ inGameMenuRoot.interactable = false; inGameMenuRoot.blocksRaycasts = false; }
        }

        titleRoot.alpha = on ? 1 : 0;
        titleRoot.interactable = on;
        titleRoot.blocksRaycasts = on;

        if (on)
        {
            ClearSelection();
            WireTitleNavigation();
            var first = FirstTitleButton();
            if (first) EventSystem.current?.SetSelectedGameObject(first.gameObject);
        }
    }

    public void ShowSelect(bool on)
    {
        if (!selectRoot) return;
        if (on) ClearSelection();
        selectRoot.alpha = on ? 1 : 0;
        selectRoot.interactable = on;
        selectRoot.blocksRaycasts = on;

        if (on && _bands.Count > 0)
        {
            var firstBtn = _bands[0].GetComponentInChildren<Button>();
            if (firstBtn) EventSystem.current?.SetSelectedGameObject(firstBtn.gameObject);
            if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;
            ScrollToBand(_bands[0].GetComponent<UISelectBand>()?.Rect);
        }
    }

    public void ShowInGameMenu(bool on)
    {
        if (!inGameMenuRoot) return;
        if (on) ClearSelection();
        inGameMenuRoot.alpha = on ? 1 : 0;
        inGameMenuRoot.interactable = on;
        inGameMenuRoot.blocksRaycasts = on;

        if (selectRoot)
        {
            selectRoot.interactable  = !on && selectRoot.alpha > 0.5f;
            selectRoot.blocksRaycasts= !on && selectRoot.alpha > 0.5f;
        }

        if (on && btnInGameSolo)
            EventSystem.current?.SetSelectedGameObject(btnInGameSolo.gameObject);
    }

    // ---------- Selection list ----------
    public void BindSelection(List<GameDef> games)
    {
        if (_builtList) return;

        foreach (var g in games)
        {
            bool unlocked = _meta == null || _meta.IsUnlocked(g);
            if (!unlocked && _meta != null && !_meta.ShowLockedBands)
                continue; // hide locked completely if desired

            var go = Object.Instantiate(bandPrefab, listRoot);
            var band = go.GetComponent<UISelectBand>();
            if (band != null)
            {
                band.Bind(g, _meta); // band will query meta.NowUtc for live countdown & lock visuals
                band.onSelected = (rect) => { ScrollToBand(rect); _meta?.PreviewGameTrack(g); };
            }
            _bands.Add(go);
        }

        EnsureEdgeSpacers();
        WireVerticalNavigationForBands();

        _builtList = true;

        if (_bands.Count > 0)
        {
            var firstBtn = _bands[0].GetComponentInChildren<Button>();
            if (firstBtn) EventSystem.current?.SetSelectedGameObject(firstBtn.gameObject);
            if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;
        }
    }

    public void ScrollToBand(RectTransform item)
    {
        if (!selectRoot || !selectRoot.interactable || !selectScroll || !selectScroll.content || !selectViewport || !item)
            return;
        if (!item.IsChildOf(selectScroll.content)) return;

        // Make sure content size is current (prevents first-jump when options collapse)
        LayoutRebuilder.ForceRebuildLayoutImmediate(selectScroll.content);

        var content   = selectScroll.content;
        var viewport  = selectViewport;

        float contentH   = content.rect.height;
        float viewportH  = viewport.rect.height;
        float scrollable = contentH - viewportH;
        if (scrollable <= 0.001f) return;

        Vector3[] corners = new Vector3[4];
        item.GetWorldCorners(corners);
        Vector3 localTop    = content.InverseTransformPoint(corners[1]);
        Vector3 localBottom = content.InverseTransformPoint(corners[0]);
        float itemCenter    = (localTop.y + localBottom.y) * 0.5f;

        float contentTopY   = (1f - content.pivot.y) * contentH;
        float centerFromTop = contentTopY - itemCenter;

        float desiredFromTop = Mathf.Clamp(
            centerFromTop - (viewportH * 0.5f) + Mathf.Max(0f, Viewportpad * 0.25f),
            0f, scrollable
        );

        float targetNorm = 1f - Mathf.Clamp01(desiredFromTop / scrollable);
        selectScroll.verticalNormalizedPosition = targetNorm;
    }

    // ---------- In-Game Menu wiring ----------
    public void BindInGameMenu(GameManager gm)
    {
        if (gm != null && gm.Def != null && igCartridgeImage != null)
        {
            UISelectBand.PaintCartridgeForGame(
                gm.Def,
                igCartridgeImage,
                igCartTitle,
                igCartNumber,
                igLabelsFolder,
                igDefaultFocus,
                igCropOverrides
            );
            if (igCartTitle != null) igCartTitle.color = UISelectBand.AccentFor(gm.Def);
        }

        ShowInGameMenu(true);

        if (btnInGameSolo) btnInGameSolo.onClick.RemoveAllListeners();
        if (btnInGameVs) btnInGameVs.onClick.RemoveAllListeners();
        if (btnInGameCoop) btnInGameCoop.onClick.RemoveAllListeners();
        if (btnInGameAlt) btnInGameAlt.onClick.RemoveAllListeners();
        if (btnInGameQuit) btnInGameQuit.onClick.RemoveAllListeners();

        if (btnInGameQuit) btnInGameQuit.onClick.AddListener(() =>
        {
            ShowInGameMenu(false);
            _meta.QuitToSelection();
        });

        if (btnInGameSolo) btnInGameSolo.onClick.AddListener(() => { ShowInGameMenu(false); _meta.StartGame(gm.Def, GameMode.Solo); });
        if (btnInGameVs)   btnInGameVs  .onClick.AddListener(() => { ShowInGameMenu(false); _meta.StartGame(gm.Def, GameMode.Versus2P); });
        if (btnInGameCoop) btnInGameCoop.onClick.AddListener(() => { ShowInGameMenu(false); _meta.StartGame(gm.Def, GameMode.Coop2P); });
        if (btnInGameAlt)  btnInGameAlt .onClick.AddListener(() => { ShowInGameMenu(false); _meta.StartGame(gm.Def, GameMode.Alt2P); });
    }

    // ---------- Spacers ----------
    void EnsureEdgeSpacers()
    {
        if (!selectScroll || !selectScroll.content || !selectViewport) return;

        var content   = selectScroll.content;
        float vh      = selectViewport.rect.height;

        float topPad = UseAutoEdgeMargins
            ? (vh * Mathf.Clamp01(AutoEdgeFactor) + Mathf.Max(0f, Viewportpad))
            : Mathf.Max(0f, TopEdgeMargin);

        float botPad = UseAutoEdgeMargins
            ? (vh * Mathf.Clamp01(AutoEdgeFactor) + Mathf.Max(0f, Viewportpad))
            : Mathf.Max(0f, BottomEdgeMargin);

        RectTransform Make(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(content, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 0f; le.flexibleHeight = 0f;
            return rt;
        }

        if (!_spacerTop)
        {
            _spacerTop = Make("SpacerTop");
            _spacerTop.SetSiblingIndex(0);
        }
        if (!_spacerBottom)
        {
            _spacerBottom = Make("SpacerBottom");
            _spacerBottom.SetAsLastSibling();
        }

        _spacerTop.GetComponent<LayoutElement>().minHeight    = topPad;
        _spacerBottom.GetComponent<LayoutElement>().minHeight = botPad;
    }

    void WireVerticalNavigationForBands()
    {
        var bandButtons = new List<Button>();
        foreach (var go in _bands)
        {
            if (!go) continue;
            var band = go.GetComponent<UISelectBand>();
            if (band && band.bandButton) bandButtons.Add(band.bandButton);
        }

        Button topMid = btnBackFromSelect ? btnBackFromSelect
             : (btnTopLeaderboards ? btnTopLeaderboards
             : (btnTopOptions ? btnTopOptions : btnOptions));

        for (int i = 0; i < bandButtons.Count; i++)
        {
            var b = bandButtons[i];
            var nav = new Navigation { mode = Navigation.Mode.Explicit };
            nav.selectOnUp = (i > 0) ? bandButtons[i - 1] : topMid;
            nav.selectOnDown = (i < bandButtons.Count - 1) ? bandButtons[i + 1] : null;

            var prev = b.navigation;
            nav.selectOnLeft = prev.selectOnLeft;
            nav.selectOnRight = prev.selectOnRight;

            b.navigation = nav;
        }

        WireTopHeaderNavigation(bandButtons.Count > 0 ? bandButtons[0] : null);
    }

    void WireTopHeaderNavigation(Button firstBand)
    {
        if (btnTopOptions)
        {
            var n = btnTopOptions.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = btnTopLeaderboards ? btnTopLeaderboards : (firstBand ? firstBand : n.selectOnDown);
            btnTopOptions.navigation = n;
        }

        if (btnTopLeaderboards)
        {
            var n = btnTopLeaderboards.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnUp   = btnTopOptions;
            n.selectOnDown = btnBackFromSelect ? btnBackFromSelect : (firstBand ? firstBand : n.selectOnDown);
            btnTopLeaderboards.navigation = n;
        }

        if (btnBackFromSelect)
        {
            var n = btnBackFromSelect.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnUp   = btnTopLeaderboards ? btnTopLeaderboards : btnTopOptions;
            n.selectOnDown = firstBand;
            btnBackFromSelect.navigation = n;
        }

        if (!btnTopOptions && !btnBackFromSelect && btnOptions && firstBand)
        {
            var n = btnOptions.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = firstBand;
            btnOptions.navigation = n;
        }
    }

    void CycleBasicOptions()
    {
        float[] steps = { 1f, 0.6f, 0.3f, 0f };
        int nextM = (System.Array.IndexOf(steps, _musicVol) + 1) % steps.Length;
        int nextS = (System.Array.IndexOf(steps, _sfxVol) + 1) % steps.Length;

        _musicVol = steps[nextM];
        _sfxVol = steps[nextS];

        AudioListener.volume = _musicVol;
        RetroAudio.GlobalSfxVolume = _sfxVol;

        PlayerPrefs.SetFloat("opt_music", _musicVol);
        PlayerPrefs.SetFloat("opt_sfx", _sfxVol);

        bool crt = !(crtEffectRoot && crtEffectRoot.activeSelf);
        if (crtEffectRoot) crtEffectRoot.SetActive(crt);
        PlayerPrefs.SetInt("opt_crt", crt ? 1 : 0);

        ShowOptionsToast($"Music {Mathf.RoundToInt(_musicVol * 100)}%  •  SFX {Mathf.RoundToInt(_sfxVol * 100)}%  •  CRT {(crt ? "ON" : "OFF")}");
    }

    bool SelectionActive() =>
        selectRoot && selectRoot.alpha > 0.5f && selectRoot.interactable;

    bool InGameMenuActive() =>
        inGameMenuRoot && inGameMenuRoot.alpha > 0.5f && inGameMenuRoot.interactable;

    bool TitleActive() =>
        titleRoot && titleRoot.alpha > 0.5f && titleRoot.interactable;

    bool PlayingActive() =>
        !TitleActive() && !SelectionActive() && !InGameMenuActive();

    void HandleBackSinglePress()
    {
        if (TitleActive()) return;

        if (PlayingActive())
        {
            _meta.StopGame();
            ShowInGameMenu(true);
            if (btnInGameSolo)
                EventSystem.current?.SetSelectedGameObject(btnInGameSolo.gameObject);
            return;
        }

        if (InGameMenuActive())
        {
            ShowInGameMenu(false);
            return;
        }

        if (SelectionActive())
        {
            OpenTitleFromSelection();
            return;
        }
    }

    void HandlePauseToggle()
    {
        if (TitleActive()) return;

        if (PlayingActive())
        {
            ShowInGameMenu(true);
            if (btnInGameSolo) EventSystem.current?.SetSelectedGameObject(btnInGameSolo.gameObject);
        }
        else if (InGameMenuActive())
        {
            ShowInGameMenu(false);
        }
    }

    void QuitToBandsNow()
    {
        Time.timeScale = 1f;
        ShowInGameMenu(false);
        _meta.QuitToSelection();

        if (selectRoot && selectRoot.alpha > 0.5f && _bands.Count > 0)
        {
            var firstBtn = _bands[0].GetComponentInChildren<Button>();
            if (firstBtn) EventSystem.current?.SetSelectedGameObject(firstBtn.gameObject);
            if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;
        }
    }

    void OpenTitleFromSelection() => _meta.OpenTitle();

    void ClearSelection()
    {
        var es = EventSystem.current;
        if (es && es.currentSelectedGameObject != null)
            es.SetSelectedGameObject(null);
    }

    void WireTitleNavigation()
    {
        var list = new List<Button>();
        if (btnPlay)  list.Add(btnPlay);
        if (btnOptions) list.Add(btnOptions);
        if (btnQuit)  list.Add(btnQuit);

        if (list.Count == 0) return;

        for (int i = 0; i < list.Count; i++)
        {
            var n = list[i].navigation;
            n.mode = Navigation.Mode.Explicit;
            n.selectOnUp   = (i > 0) ? list[i - 1] : null;
            n.selectOnDown = (i < list.Count - 1) ? list[i + 1] : null;
            list[i].navigation = n;
        }
    }

    Button FirstTitleButton()
    {
        if (btnPlay) return btnPlay;
        if (btnOptions) return btnOptions;
        if (btnQuit) return btnQuit;
        return null;
    }

    Button FirstBandButton()
    {
        foreach (var go in _bands)
        {
            if (!go) continue;
            var b = go.GetComponent<UISelectBand>()?.bandButton;
            if (b && b.isActiveAndEnabled && b.interactable) return b;
        }
        return null;
    }

    void StickyFocusGuard()
    {
        var es = EventSystem.current;
        if (!es) return;

        // Title
        if (TitleActive())
        {
            if (!es.currentSelectedGameObject || !es.currentSelectedGameObject.activeInHierarchy ||
                !es.currentSelectedGameObject.transform.IsChildOf(titleRoot.transform))
            {
                if (btnPlay) es.SetSelectedGameObject(btnPlay.gameObject);
            }
            return;
        }

        // Selection (bands + header)
        if (SelectionActive())
        {
            var sel = es.currentSelectedGameObject;
            if (!sel || !sel.activeInHierarchy || !sel.transform.IsChildOf(selectRoot.transform))
            {
                var fb = FirstBandButton();
                GameObject g = fb ? fb.gameObject
                                  : (btnBackFromSelect ? btnBackFromSelect.gameObject
                                     : (btnTopLeaderboards ? btnTopLeaderboards.gameObject
                                        : (btnTopOptions ? btnTopOptions.gameObject : null)));
                if (g) es.SetSelectedGameObject(g);
            }
            return;
        }

        // In-Game menu
        if (InGameMenuActive())
        {
            var sel = es.currentSelectedGameObject;
            if (!sel || !sel.activeInHierarchy || !sel.transform.IsChildOf(inGameMenuRoot.transform))
            {
                if (btnInGameSolo)      es.SetSelectedGameObject(btnInGameSolo.gameObject);
                else if (btnInGameQuit) es.SetSelectedGameObject(btnInGameQuit.gameObject);
            }
        }
    }

    void ShowOptionsToast(string msg)
    {
        if (!optionsToast) return;
        optionsToast.text = msg;
        optionsToast.gameObject.SetActive(true);
        var c = optionsToast.color; c.a = 1f; optionsToast.color = c;
        _toastT = 1.25f;
    }
}
