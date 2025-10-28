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

public Button btnTopOptions;        // the secondary Options (in selection screen)
public Button btnTopLeaderboards;   // the Leaderboards button
    public ScrollRect selectScroll;         // assign your ScrollRect
    public RectTransform selectViewport;    // assign ScrollRect.viewport

    

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
    public bool UseAutoEdgeMargins = false;   // force OFF to avoid header shift
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

    RectTransform _spacerTop, _spacerBottom;
    bool _builtList = false;

    void Awake()
    {
        _musicVol = PlayerPrefs.GetFloat("opt_music", 0.8f);
        _sfxVol   = PlayerPrefs.GetFloat("opt_sfx",   1.0f);
        AudioListener.volume = _musicVol;
        RetroAudio.GlobalSfxVolume = _sfxVol;
        if (crtEffectRoot) crtEffectRoot.SetActive(PlayerPrefs.GetInt("opt_crt", 1) != 0);
        if (optionsToast) optionsToast.gameObject.SetActive(false);
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

    // Gamepad "Back" (B)
    bool backPressed = false;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    if (UnityEngine.InputSystem.Gamepad.current != null &&
        UnityEngine.InputSystem.Gamepad.current.bButton.wasPressedThisFrame)
        backPressed = true;
#else
    if (Input.GetButtonDown("Cancel") || Input.GetKeyDown(KeyCode.JoystickButton1))
        backPressed = true;
#endif
    if (backPressed) HandleBack();

    // When a header button is focused, hard-pin list to top (prevents "snap back")
    if (selectRoot && selectRoot.interactable && selectScroll)
    {
        var es  = EventSystem.current;
        var sel = es ? es.currentSelectedGameObject : null;
        if (sel &&
            ((btnTopLeaderboards && sel == btnTopLeaderboards.gameObject) ||
             (btnTopOptions      && sel == btnTopOptions.gameObject)      ||
             (btnBackFromSelect  && sel == btnBackFromSelect.gameObject)))
        {
            selectScroll.verticalNormalizedPosition = 1f;
        }
    }
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
        titleRoot.alpha = on ? 1 : 0;
        titleRoot.interactable = on;
        titleRoot.blocksRaycasts = on;

        if (on && btnPlay)
            EventSystem.current?.SetSelectedGameObject(btnPlay.gameObject);
    }

    public void ShowSelect(bool on)
    {
        if (!selectRoot) return;
        selectRoot.alpha = on ? 1 : 0;
        selectRoot.interactable = on;
        selectRoot.blocksRaycasts = on;

        if (on && _bands.Count > 0)
        {
            // Always go to the first band; hard reset scroll to top
            var firstBtn = _bands[0].GetComponentInChildren<Button>();
            if (firstBtn) EventSystem.current?.SetSelectedGameObject(firstBtn.gameObject);
            if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;
        }
    }

    public void ShowInGameMenu(bool on)
    {
        if (!inGameMenuRoot) return;
        inGameMenuRoot.alpha = on ? 1 : 0;
        inGameMenuRoot.interactable = on;
        inGameMenuRoot.blocksRaycasts = on;

        // While in-game menu is open, selection is inert.
        if (selectRoot)
        {
            selectRoot.interactable  = !on && selectRoot.alpha > 0.5f;
            selectRoot.blocksRaycasts= !on && selectRoot.alpha > 0.5f;
        }

        if (on && btnInGameSolo)
            EventSystem.current?.SetSelectedGameObject(btnInGameSolo.gameObject);
    }

    // ---------- Selection list ----------
    // Build once; reuse across opens for instant menu.
    public void BindSelection(List<GameDef> games)
    {
        // Build once; reuse across opens for instant menu.
        if (_builtList) return;

        foreach (var g in games)
        {
            var go = Object.Instantiate(bandPrefab, listRoot);
            var band = go.GetComponent<UISelectBand>();
            if (band != null)
            {
                band.Bind(g, _meta);
                // RESTORED: auto-scroll when a band gains focus (gamepad/keyboard select)
                band.onSelected = (rect) => ScrollToBand(rect);
            }
            _bands.Add(go);
        }

        EnsureEdgeSpacers();          // spacers are locked to 0 in EnsureEdgeSpacers()
        WireVerticalNavigationForBands();

        _builtList = true;

        // Select the first band + snap to very top immediately
        if (_bands.Count > 0)
        {
            var firstBtn = _bands[0].GetComponentInChildren<Button>();
            if (firstBtn) EventSystem.current?.SetSelectedGameObject(firstBtn.gameObject);
            if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;
        }
    }

void ScrollToBand(RectTransform item)
{
    // Only when selection is active & interactive
    if (!selectRoot || !selectRoot.interactable || !selectScroll || !selectScroll.content || !selectViewport || !item)
        return;

    // Ensure the item actually belongs to this content
    if (item.transform == null || item.transform.parent != selectScroll.content) return;

    var content   = selectScroll.content;
    var viewport  = selectViewport;

    float contentH   = content.rect.height;
    float viewportH  = viewport.rect.height;
    float scrollable = contentH - viewportH;
    if (scrollable <= 0.001f) return;

    // Find item's center in content space
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

    // RESTORED: instant snap (no lerp) so gamepad feels crisp
    float targetNorm = 1f - Mathf.Clamp01(desiredFromTop / scrollable);
    selectScroll.verticalNormalizedPosition = targetNorm;
}

    // ---------- In-Game Menu wiring ----------
    public void BindInGameMenu(GameManager gm)
    {
        // Render the same cartridge and tint the title
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

        if (btnInGameSolo) btnInGameSolo.onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Solo); });
        if (btnInGameVs) btnInGameVs.onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Versus2P); });
        if (btnInGameCoop) btnInGameCoop.onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Coop2P); });
        if (btnInGameAlt) btnInGameAlt.onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Alt2P); });
    }

    // ---------- Spacers (locked to zero to avoid pushing header/logo) ----------
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

    // Snap to top when header buttons are focused
    void SnapToTop()
    {
        if (selectScroll) selectScroll.verticalNormalizedPosition = 1f;
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

    // Choose the "top" target for going up from the first band
    Button topUp = btnTopLeaderboards ? btnTopLeaderboards
                 : (btnTopOptions ? btnTopOptions
                 : (btnBackFromSelect ? btnBackFromSelect : btnOptions));

    for (int i = 0; i < bandButtons.Count; i++)
    {
        var b = bandButtons[i];
        var nav = new Navigation { mode = Navigation.Mode.Explicit };

        nav.selectOnUp   = (i > 0) ? bandButtons[i - 1] : topUp;
        nav.selectOnDown = (i < bandButtons.Count - 1) ? bandButtons[i + 1] : null;

        // keep existing left/right if you set them elsewhere
        var prev = b.navigation;
        nav.selectOnLeft  = prev.selectOnLeft;
        nav.selectOnRight = prev.selectOnRight;

        b.navigation = nav;
    }

    // Make the top buttons go DOWN into the first band
    if (bandButtons.Count > 0)
    {
        var firstBand = bandButtons[0];

        if (btnTopLeaderboards)
        {
            var n = btnTopLeaderboards.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = firstBand;
            btnTopLeaderboards.navigation = n;
        }
        if (btnTopOptions)
        {
            var n = btnTopOptions.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = firstBand;
            btnTopOptions.navigation = n;
        }
        if (btnBackFromSelect)
        {
            var n = btnBackFromSelect.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = firstBand;
            btnBackFromSelect.navigation = n;
        }
        if (btnOptions)
        {
            var n = btnOptions.navigation; n.mode = Navigation.Mode.Explicit;
            n.selectOnDown = firstBand;
            btnOptions.navigation = n;
        }
    }
}

    // ---------- Options ----------
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
    
    void HandleBack()
{
    // If in-game menu is open, B = Quit back to band list
    if (inGameMenuRoot && inGameMenuRoot.interactable && inGameMenuRoot.alpha > 0.5f)
    {
        ShowInGameMenu(false);
        _meta.QuitToSelection();
        return;
    }

    // If actively playing (no in-game menu, selection hidden), B = open in-game menu
    if (selectRoot && (selectRoot.alpha < 0.5f || !selectRoot.interactable))
    {
        ShowInGameMenu(true);
        if (btnInGameQuit) EventSystem.current?.SetSelectedGameObject(btnInGameQuit.gameObject);
        return;
    }

    // If on selection screen, B = back to title
    if (selectRoot && selectRoot.interactable && selectRoot.alpha > 0.5f)
    {
        OpenTitleFromSelection();
        return;
    }
}

// Small wrapper so you can tweak later if needed
void OpenTitleFromSelection()
{
    _meta.OpenTitle();
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
