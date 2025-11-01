// UIManager.cs — DROP-IN (banner placement + mouse wheel scrolling fix)
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Title")]
    public CanvasGroup titleRoot;
    public Button btnGameSelection;
    public Button btnQuit;

    [Header("Selection")]
    public CanvasGroup selectRoot;
    public RectTransform listRoot;          // Content
    public GameObject bandPrefab;
    public GameObject unlockBannerPrefab;   // assign slim prefab with one TMP
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

    [Header("Edge Spacers")]
    public bool UseAutoEdgeMargins = false;
    [Range(0f, 1f)] public float AutoEdgeFactor = 0.50f;
    public float TopEdgeMargin = 0f;
    public float BottomEdgeMargin = 0f;

    [Header("Selection: Main Score")]
    public TMP_Text mainScoreText;
    public float mainScoreCountSpeed = 600f;
    public float mainScoreTickHz = 660f;
    public float mainScoreFinishHz = 880f;

    MetaGameManager _meta;
    readonly List<GameObject> _bands = new List<GameObject>();

    float _musicVol = 0.8f;
    float _sfxVol   = 1.0f;

    float _backHold = 0f;
    const float BackHoldToQuit = 0.35f;

    RectTransform _spacerTop, _spacerBottom;
    bool _builtList = false;

    GameObject _lastSel;

    bool  _mainCounting;
    int   _mainFrom, _mainTo;
    float _mainCurrent;
    int   _mainShown = -1;

    GameObject _bannerGO;
    TMP_Text _bannerTxt;
    float _bannerPulseT;

    void Awake()
    {
        _musicVol = PlayerPrefs.GetFloat("opt_music", 0.8f);
        _sfxVol = PlayerPrefs.GetFloat("opt_sfx", 1.0f);
        AudioListener.volume = _musicVol;
        RetroAudio.GlobalSfxVolume = _sfxVol;
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
        // score count-up
        if (_mainCounting)
        {
            float step = Mathf.Max(1f, mainScoreCountSpeed) * Time.unscaledDeltaTime;
            _mainCurrent = Mathf.Min(_mainCurrent + step, _mainTo);
            int disp = Mathf.FloorToInt(_mainCurrent);

            if (disp != _mainShown)
            {
                _mainShown = disp;
                if (mainScoreText) mainScoreText.text = _mainShown.ToString("N0");
                if (_meta && _meta.audioBus && mainScoreTickHz > 0f)
                    _meta.audioBus.BeepOnce(mainScoreTickHz, 0.015f, 0.09f);
            }

            if (Mathf.Approximately(_mainCurrent, _mainTo))
            {
                _mainCounting = false;
                if (mainScoreText) mainScoreText.text = _mainTo.ToString("N0");
                if (_meta && _meta.audioBus && mainScoreFinishHz > 0f)
                    _meta.audioBus.BeepOnce(mainScoreFinishHz, 0.10f, 0.25f);
            }
        }

        // banner pulse
        if (_bannerGO && _bannerGO.activeInHierarchy)
        {
            _bannerPulseT += Time.unscaledDeltaTime * 2.6f;
            float s = 1f + 0.04f * Mathf.Sin(_bannerPulseT * 1.9f);
            float a = 0.85f + 0.15f * Mathf.Sin(_bannerPulseT);
            _bannerGO.transform.localScale = new Vector3(s, s, 1f);
            if (_bannerTxt) { var c = _bannerTxt.color; c.a = a; _bannerTxt.color = c; }
        }

        // --- mouse wheel scrolling works immediately when over viewport ---
        if (SelectionActive() && selectScroll && selectViewport && Mouse.current != null)
        {
            float wheel = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                Vector2 sp = Mouse.current.position.ReadValue();
                if (RectTransformUtility.RectangleContainsScreenPoint(selectViewport, sp, null))
                {
                    // Normalize: positive wheel = scroll up
                    float sens = 0.0016f; // tweak if needed
                    float v = Mathf.Clamp01(selectScroll.verticalNormalizedPosition + wheel * sens);
                    selectScroll.verticalNormalizedPosition = v;
                }
            }
        }

        // unified input
        bool backDown = false, backHeld = false, pauseDown = false;

        var gp = Gamepad.current;
        if (gp != null)
        {
            if (gp.buttonEast.wasPressedThisFrame) backDown = true;
            if (gp.buttonEast.isPressed) backHeld = true;
            if (gp.startButton.wasPressedThisFrame) pauseDown = true;
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.backspaceKey.wasPressedThisFrame) backDown = true;
            if (kb.backspaceKey.isPressed) backHeld = true;
            if (kb.escapeKey.wasPressedThisFrame) pauseDown = true;
        }

        var ms = Mouse.current;
        if (ms != null)
        {
            if (ms.rightButton.wasPressedThisFrame) backDown = true;
            if (ms.rightButton.isPressed) backHeld = true;
        }

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

        // viewport guard
        if (selectRoot && selectRoot.interactable && selectScroll)
        {
            var es0 = EventSystem.current;
            var sel0 = es0 ? es0.currentSelectedGameObject : null;
            if (sel0 &&
                ((btnTopLeaderboards && sel0 == btnTopLeaderboards.gameObject) ||
                 (btnTopOptions && sel0 == btnTopOptions.gameObject) ||
                 (btnBackFromSelect && sel0 == btnBackFromSelect.gameObject)))
            {
                selectScroll.verticalNormalizedPosition = 1f;
            }
        }
        if (TitleActive())
        {
            var es1 = EventSystem.current;
            if (es1 && (!es1.currentSelectedGameObject || !es1.currentSelectedGameObject.activeInHierarchy))
            {
                var first = FirstTitleButton();
                if (first) es1.SetSelectedGameObject(first.gameObject);
            }
        }
        StickyFocusGuard();

        // focus beep
        var es = EventSystem.current;
        var cur = es ? es.currentSelectedGameObject : null;
        if (cur != null && cur != _lastSel)
        {
            _lastSel = cur;
            if (_meta && _meta.audioBus) _meta.audioBus.BeepOnce(520f, 0.02f, 0.08f);
        }
        if (cur == null) _lastSel = null;
    }

    public void Init(MetaGameManager meta)
    {
        _meta = meta;

        if (btnGameSelection)    btnGameSelection.onClick.AddListener(_meta.OpenSelection);
        if (btnQuit)             btnQuit.onClick.AddListener(_meta.QuitApp);
        if (btnBackFromSelect)   btnBackFromSelect.onClick.AddListener(_meta.OpenTitle);

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
            if (selectRoot)    { selectRoot.interactable = false;   selectRoot.blocksRaycasts = false; }
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

        if (on)
        {
            GameObject header =
                (btnTopOptions      ? btnTopOptions.gameObject      : null) ??
                (btnTopLeaderboards ? btnTopLeaderboards.gameObject : null) ??
                (btnBackFromSelect  ? btnBackFromSelect.gameObject  : null);

            if (header) EventSystem.current?.SetSelectedGameObject(header);
            if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;

            if (!_mainCounting && mainScoreText && _meta != null)
                mainScoreText.text = _meta.MainScore.ToString("N0");
        }
        else
        {
            HideNewUnlocksBanner();
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
                continue;

            var go = Object.Instantiate(bandPrefab, listRoot);
            var band = go.GetComponent<UISelectBand>();
            if (band != null)
            {
                band.Bind(g, _meta);
                band.onSelected = (rect) => { ScrollToBand(rect); _meta?.PreviewGameTrack(g); };
            }
            _bands.Add(go);
        }

        EnsureEdgeSpacers();
        WireVerticalNavigationForBands();

        _builtList = true;

        if (_bands.Count > 0)
        {
            GameObject header =
                (btnTopOptions      ? btnTopOptions.gameObject      : null) ??
                (btnTopLeaderboards ? btnTopLeaderboards.gameObject : null) ??
                (btnBackFromSelect  ? btnBackFromSelect.gameObject  : null);

            if (header) EventSystem.current?.SetSelectedGameObject(header);
            if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;
        }
    }

    public void ScrollToBand(RectTransform item)
    {
        if (!selectRoot || !selectRoot.interactable || !selectScroll || !selectScroll.content || !selectViewport || !item)
            return;
        if (!item.IsChildOf(selectScroll.content)) return;

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

    // ---------- New-unlocks banner ----------
    public void ShowNewUnlocksBanner(int count)
    {
        if (!unlockBannerPrefab || !listRoot) return;
        HideNewUnlocksBanner();

        _bannerGO = Instantiate(unlockBannerPrefab, listRoot);
        _bannerTxt = _bannerGO.GetComponentInChildren<TMP_Text>(true);
        if (_bannerTxt) _bannerTxt.text = $"NEW GAMES UNLOCKED!";
        _bannerPulseT = 0f;

        // Place just BELOW the header group (logo/score/buttons)
        var headerGroup = btnBackFromSelect ? btnBackFromSelect.transform.parent as RectTransform : null;
        if (headerGroup && headerGroup.parent == listRoot)
            _bannerGO.transform.SetSiblingIndex(headerGroup.GetSiblingIndex() + 1);
        else
            _bannerGO.transform.SetSiblingIndex(1);
    }

    public void HideNewUnlocksBanner()
    {
        if (_bannerGO) Destroy(_bannerGO);
        _bannerGO = null; _bannerTxt = null; _bannerPulseT = 0f;
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
                igLabelsFolder
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
            if (gm) gm.QuitToMenu();
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

        Button topMid = btnBackFromSelect ?? btnTopLeaderboards ?? btnTopOptions;

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

        if (selectRoot && selectRoot.alpha > 0.5f)
        {
            GameObject header =
                (btnTopOptions      ? btnTopOptions.gameObject      : null) ??
                (btnTopLeaderboards ? btnTopLeaderboards.gameObject : null) ??
                (btnBackFromSelect  ? btnBackFromSelect.gameObject  : null);

            if (header) EventSystem.current?.SetSelectedGameObject(header);
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
        if (btnGameSelection)  list.Add(btnGameSelection);
        if (btnQuit)           list.Add(btnQuit);

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
        if (btnGameSelection) return btnGameSelection;
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

        if (TitleActive())
        {
            if (!es.currentSelectedGameObject || !es.currentSelectedGameObject.activeInHierarchy ||
                !es.currentSelectedGameObject.transform.IsChildOf(titleRoot.transform))
            {
                if (btnGameSelection) es.SetSelectedGameObject(btnGameSelection.gameObject);
            }
            return;
        }

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

    public void BeginMainScoreCount(int from, int to)
    {
        if (!mainScoreText) return;
        _mainFrom = Mathf.Max(0, from);
        _mainTo   = Mathf.Max(_mainFrom, to);
        _mainCurrent = _mainFrom;
        _mainShown = -1;
        _mainCounting = (_mainTo > _mainFrom);
        mainScoreText.text = _mainFrom.ToString("N0");
    }

    public void RefreshBandStats()
    {
        for (int i = 0; i < _bands.Count; i++)
        {
            var band = _bands[i] ? _bands[i].GetComponent<UISelectBand>() : null;
            if (band) band.RefreshStats();
        }
    }
}
