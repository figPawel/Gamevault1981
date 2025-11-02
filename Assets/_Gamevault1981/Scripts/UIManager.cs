// UIManager.cs — FULL FILE (now uses InputManager & disables Back in gameplay and in-game menu)
// Note: Back during gameplay DOES NOTHING now. Use Pause to open/close the in-game menu.
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

    [Header("Selection: New-Unlocks Banner (Scene-placed)")]
    public CanvasGroup newUnlocksBanner;
    public TMP_Text    newUnlocksBannerText;

    [Header("Selection")]
    public CanvasGroup selectRoot;
    public RectTransform listRoot;
    public GameObject bandPrefab;
    public GameObject unlockBannerPrefab;
    public Button btnBackFromSelect;
    public Button btnTopOptions;
    public Button btnTopLeaderboards;
    public ScrollRect selectScroll;
    public RectTransform selectViewport;
    public RectTransform bannerInsertAfter;

    [Header("Banner visual")]
    public float bannerRainbowSpeed = 0.35f;
    public float bannerRainbowSpread = 0.12f;

    [Header("Viewport pad")]
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

    public GameObject leaderboardBandPrefab; // <-- assign your LeaderboardBand prefab in the Inspector


    MetaGameManager _meta;
    readonly List<GameObject> _bands = new List<GameObject>();

    float _musicVol = 0.8f;
    float _sfxVol   = 1.0f;

readonly List<GameDef> _bandDefs = new List<GameDef>();


    RectTransform _spacerTop, _spacerBottom;
    bool _builtList = false;

    GameObject _lastSel;

    // --- Leaderboards toggle (header button) ---
TMP_Text _lbBtnLabel;
bool _lbSubscribedToPDM = false;

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

        // banner pulse + rainbow
        if (_bannerGO && _bannerGO.activeInHierarchy)
        {
            _bannerPulseT += Time.unscaledDeltaTime * 2.6f;
            float s = 1f + 0.04f * Mathf.Sin(_bannerPulseT * 1.9f);
            float a = 0.85f + 0.15f * Mathf.Sin(_bannerPulseT);
            _bannerGO.transform.localScale = new Vector3(s, s, 1f);
            if (_bannerTxt)
            {
                var c = _bannerTxt.color; c.a = a; _bannerTxt.color = c;
                AnimateRainbowTMP(_bannerTxt, bannerRainbowSpeed, bannerRainbowSpread);
            }
        }

        // mouse wheel scrolling works immediately when over viewport
        if (SelectionActive() && selectScroll && selectViewport && Mouse.current != null)
        {
            float wheel = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                Vector2 sp = Mouse.current.position.ReadValue();
                if (RectTransformUtility.RectangleContainsScreenPoint(selectViewport, sp, null))
                {
                    float sens = 0.0016f;
                    float v = Mathf.Clamp01(selectScroll.verticalNormalizedPosition + wheel * sens);
                    selectScroll.verticalNormalizedPosition = v;
                }
            }
        }

        // unified input via InputManager
        bool backDown = InputManager.I && InputManager.I.UIBackDown();
        bool backHeld = InputManager.I && InputManager.I.UIBackHeld();
        bool pauseDown = InputManager.I && InputManager.I.UIPauseDown();

        // We DO NOT react to Back during gameplay or in-game menu anymore.
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

       if (SelectionActive())
{
    if (backDown) HandleBackSinglePress();
}

        if (PlayingActive())
        {
            if (pauseDown) HandlePauseToggle(); // Pause opens menu
        }
        else if (InGameMenuActive())
        {
            if (pauseDown) HandlePauseToggle(); // Pause closes menu
        }

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

    // --- Leaderboards header button wiring ---
    if (btnTopLeaderboards)
    {
        _lbBtnLabel = btnTopLeaderboards.GetComponentInChildren<TMP_Text>(true);
        btnTopLeaderboards.onClick.RemoveListener(OnLeaderboardsButtonClicked);
        btnTopLeaderboards.onClick.AddListener(OnLeaderboardsButtonClicked);
    }

    // Subscribe to global toggle so UI stays in sync even if some other code flips it
    if (PlayerDataManager.I != null && !_lbSubscribedToPDM)
    {
        PlayerDataManager.I.OnLeaderboardToggleChanged += OnLeaderboardsToggled;
        _lbSubscribedToPDM = true;
    }

    // Initial state: leaderboards are ON by default; reflect that in label and rows
    RefreshLeaderboardsHeaderAndRows();

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

        RefreshLeaderboardsHeaderAndRows(); // <- make sure header text & rows match state
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

    _bandDefs.Clear();
    _bands.Clear();

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
        _bandDefs.Add(g);
    }

    // Insert Leaderboard bands (after each game band) if leaderboards are currently enabled
    EnsureLeaderboardBands(ensureOn: PlayerDataManager.I == null ? true : PlayerDataManager.I.leaderboardsEnabled);

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
        if (newUnlocksBanner)
        {
            if (newUnlocksBannerText)
                newUnlocksBannerText.text = $"{count} NEW GAME{(count == 1 ? "" : "S")} UNLOCKED!";

            newUnlocksBanner.gameObject.SetActive(true);
            newUnlocksBanner.alpha = 1f;

            _bannerGO   = newUnlocksBanner.gameObject;
            _bannerTxt  = newUnlocksBannerText;
            _bannerPulseT = 0f;
            return;
        }

        if (!unlockBannerPrefab || !listRoot) return;
        HideNewUnlocksBanner();
        _bannerGO = Instantiate(unlockBannerPrefab, listRoot);
        _bannerTxt = _bannerGO.GetComponentInChildren<TMP_Text>(true);
        if (_bannerTxt) _bannerTxt.text = $"{count} NEW GAME{(count == 1 ? "" : "S")} UNLOCKED!";
        _bannerPulseT = 0f;
        _bannerGO.transform.SetAsFirstSibling();
    }

    public void HideNewUnlocksBanner()
    {
        if (newUnlocksBanner)
        {
            newUnlocksBanner.alpha = 0f;
            newUnlocksBanner.gameObject.SetActive(false);
        }
        if (_bannerGO && (!newUnlocksBanner || _bannerGO != newUnlocksBanner.gameObject))
            Destroy(_bannerGO);

        _bannerGO = null;
        _bannerTxt = null;
        _bannerPulseT = 0f;
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
    // Build the linear navigation list by scanning listRoot in hierarchy order.
    // Include BOTH regular game bands (UISelectBand) and leaderboard bands (LeaderboardBand).
    var orderedButtons = new List<Button>();
    for (int i = 0; i < listRoot.childCount; i++)
    {
        var t = listRoot.GetChild(i);
        if (!t || !t.gameObject.activeInHierarchy) continue;

        Button b = null;

        var gameBand = t.GetComponent<UISelectBand>();
        if (gameBand && gameBand.bandButton) b = gameBand.bandButton;

        if (b == null)
        {
            var lb = t.GetComponent<LeaderboardBand>();
            if (lb) b = t.GetComponent<Button>(); // LeaderboardBand's toggle lives on root
        }

        if (b && b.isActiveAndEnabled && b.interactable)
            orderedButtons.Add(b);
    }

    // Top header target if the player presses Up on the very first row.
    Button topMid = btnBackFromSelect ?? btnTopLeaderboards ?? btnTopOptions;

    for (int i = 0; i < orderedButtons.Count; i++)
    {
        var b = orderedButtons[i];
        var nav = new Navigation { mode = Navigation.Mode.Explicit };
        nav.selectOnUp   = (i > 0) ? orderedButtons[i - 1] : topMid;
        nav.selectOnDown = (i < orderedButtons.Count - 1) ? orderedButtons[i + 1] : null;

        // preserve existing left/right on the button (some bands may use them)
        var prev = b.navigation;
        nav.selectOnLeft  = prev.selectOnLeft;
        nav.selectOnRight = prev.selectOnRight;

        b.navigation = nav;
    }

    // Header wiring (so Up/Down from the header lands on the first row)
    WireTopHeaderNavigation(orderedButtons.Count > 0 ? orderedButtons[0] : null);
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

        if (SelectionActive())
        {
            OpenTitleFromSelection();
            return;
        }

        // During gameplay: do nothing (no accidental quit)
        // In in-game menu: do nothing (Back is disabled here)
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
    // First selectable among game bands OR leaderboard bands, in on-screen order.
    for (int i = 0; i < listRoot.childCount; i++)
    {
        var t = listRoot.GetChild(i);
        if (!t) continue;

        var sel = t.GetComponent<UISelectBand>();
        if (sel && sel.bandButton && sel.bandButton.isActiveAndEnabled && sel.bandButton.interactable)
            return sel.bandButton;

        var lb = t.GetComponent<LeaderboardBand>();
        if (lb)
        {
            var btn = t.GetComponent<Button>();
            if (btn && btn.isActiveAndEnabled && btn.interactable) return btn;
        }
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
        _mainTo = Mathf.Max(_mainFrom, to);
        _mainCurrent = _mainFrom;
        _mainShown = -1;
        _mainCounting = (_mainTo > _mainFrom);
        mainScoreText.text = _mainFrom.ToString("N0");
    }
    
    void OnLeaderboardsButtonClicked()
{
    if (PlayerDataManager.I == null) return;
    PlayerDataManager.I.ToggleLeaderboards();
    // OnLeaderboardToggleChanged event from PlayerDataManager will call our refresh,
    // but refresh immediately so the label flips even if no one’s listening.
    RefreshLeaderboardsHeaderAndRows();
}

void OnLeaderboardsToggled(bool on)
{
    RefreshLeaderboardsHeaderAndRows();
}

void RefreshLeaderboardsHeaderAndRows()
{
    bool on = PlayerDataManager.I == null ? true : PlayerDataManager.I.leaderboardsEnabled;

    if (_lbBtnLabel)
        _lbBtnLabel.text = $"Leaderboards: {(on ? "On" : "Off")}";

    EnsureLeaderboardBands(ensureOn: on);

    var bands = GetComponentsInChildren<LeaderboardBand>(true);
    for (int i = 0; i < bands.Length; i++)
        if (bands[i]) bands[i].gameObject.SetActive(on);

    WireVerticalNavigationForBands(); // rebuild nav after show/hide
}




    public void RefreshBandStats()
    {
        for (int i = 0; i < _bands.Count; i++)
        {
            var band = _bands[i] ? _bands[i].GetComponent<UISelectBand>() : null;
            if (band) band.RefreshStats();
        }
    }

    // ---------- Helpers ----------
    static void AnimateRainbowTMP(TMP_Text t, float speed, float spread)
    {
        if (!t || t.textInfo == null) return;
        t.ForceMeshUpdate();
        var ti = t.textInfo;
        float baseHue = Mathf.Repeat(Time.unscaledTime * Mathf.Max(0.01f, speed), 1f);

        for (int i = 0; i < ti.characterCount; i++)
        {
            var ch = ti.characterInfo[i];
            if (!ch.isVisible) continue;

            float h = Mathf.Repeat(baseHue + i * Mathf.Max(0.001f, spread), 1f);
            Color col = Color.HSVToRGB(h, 1f, 1f);
            var meshInfo = ti.meshInfo[ch.materialReferenceIndex];
            int vi = ch.vertexIndex;
            if (meshInfo.colors32 == null || meshInfo.colors32.Length == 0) continue;
            meshInfo.colors32[vi + 0] = col;
            meshInfo.colors32[vi + 1] = col;
            meshInfo.colors32[vi + 2] = col;
            meshInfo.colors32[vi + 3] = col;
            ti.meshInfo[ch.materialReferenceIndex].colors32 = meshInfo.colors32;
        }
        t.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

   void EnsureLeaderboardBands(bool ensureOn)
{
    if (!leaderboardBandPrefab) return;

    for (int i = 0; i < _bands.Count; i++)
    {
        var gameGO = _bands[i];
        if (!gameGO) continue;

        int myIndex = gameGO.transform.GetSiblingIndex();
        LeaderboardBand nextLB = null;
        if (myIndex + 1 < listRoot.childCount)
            nextLB = listRoot.GetChild(myIndex + 1).GetComponent<LeaderboardBand>();

        if (!nextLB && ensureOn)
        {
            var def = (i < _bandDefs.Count) ? _bandDefs[i] : null;
            var lbGO = Instantiate(leaderboardBandPrefab, listRoot);
            lbGO.transform.SetSiblingIndex(myIndex + 1);
            lbGO.SetActive(true);

            nextLB = lbGO.GetComponent<LeaderboardBand>();
            if (def != null && nextLB)
            {
                // Pretty + id
                nextLB.prettyTitleOverride = def.title;
                nextLB.gameId = def.id;

                // 2P capability info
                bool has2P = (def.flags & (GameFlags.Versus2P | GameFlags.Coop2P | GameFlags.Alt2P)) != 0;
                nextLB.hasTwoPlayer = has2P;
                nextLB.useTwoPlayerBoard = false;

                // Match color (exact)
                var gameImg = gameGO.GetComponent<Image>();
                var lbImg   = nextLB.GetComponent<Image>();
                if (gameImg && lbImg) lbImg.color = gameImg.color;

                // Give the band the same accent feel as a game band (tint/highlight)
                var accent = UISelectBand.AccentFor(def);
                nextLB.SetAccent(accent);

                // Ensure the band is selectable
                var btn = lbGO.GetComponent<Button>();
                if (btn)
                {
                    var nav = btn.navigation; nav.mode = Navigation.Mode.Explicit;
                    nav.selectOnLeft = null; nav.selectOnRight = null;
                    btn.navigation = nav;
                    btn.interactable = true; btn.enabled = true;
                }
            }
        }
        // When ensureOn == false we keep instances; visibility is handled elsewhere.
    }

    WireVerticalNavigationForBands(); // Make sure they're in the focus chain
}




void SetLeaderboardBandAccent(LeaderboardBand lb, Color accent)
{
    var img = lb ? lb.GetComponent<Image>() : null;
    if (img)
    {
        var c = accent; c.a = 0.20f;
        img.color = c;
    }
}


    void OnDestroy()
{
    if (_lbSubscribedToPDM && PlayerDataManager.I != null)
        PlayerDataManager.I.OnLeaderboardToggleChanged -= OnLeaderboardsToggled;
    _lbSubscribedToPDM = false;
}
}
