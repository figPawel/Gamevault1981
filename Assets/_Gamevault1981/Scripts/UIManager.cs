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

    [Header("Edge Spacers")]
[Tooltip("If ON, spacer = (ViewportHeight * AutoEdgeFactor) + Viewportpad. If OFF, use explicit margins below.")]
public bool UseAutoEdgeMargins = true;
[Range(0f, 1f)] public float AutoEdgeFactor = 0.50f; // half viewport by default
public float TopEdgeMargin = 0f;     // used when UseAutoEdgeMargins = false
public float BottomEdgeMargin = 0f;  // used when UseAutoEdgeMargins = false

    [Header("Optional / Effects")]
    public GameObject crtEffectRoot;

    [Header("Optional / Small HUD")]
    public TMP_Text optionsToast;           // tiny label for feedback

    MetaGameManager _meta;
    readonly List<GameObject> _bands = new List<GameObject>();

    // persisted options (cycled by Options button; shown via optionsToast)
    float _musicVol = 0.8f;
    float _sfxVol   = 1.0f;
    float _toastT;

    // edge spacers so first/last items can center
    RectTransform _spacerTop, _spacerBottom;

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
        // fade-out for options toast
        if (_toastT > 0f && optionsToast)
        {
            _toastT -= Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(Mathf.Min(_toastT, 0.25f) / 0.25f);
            var c = optionsToast.color; c.a = a; optionsToast.color = c;
            if (_toastT <= 0f) optionsToast.gameObject.SetActive(false);
        }

        // When header buttons are focused while selection screen is active, snap list to top
        if (selectRoot && selectRoot.interactable)
        {
            var es = EventSystem.current;
            var sel = es ? es.currentSelectedGameObject : null;
            if (sel)
            {
                if ((btnBackFromSelect && sel == btnBackFromSelect.gameObject) ||
                    (btnOptions       && sel == btnOptions.gameObject))
                {
                    SnapToTop();
                }
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

    // ---------- Visibility helpers ----------
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
            var firstBtn = _bands[0].GetComponentInChildren<Button>();
            if (firstBtn) EventSystem.current?.SetSelectedGameObject(firstBtn.gameObject);

            var firstBand = _bands[0].GetComponent<UISelectBand>();
            if (firstBand) ScrollToBand(firstBand.Rect);
        }
    }

    public void ShowInGameMenu(bool on)
    {
        if (!inGameMenuRoot) return;
        inGameMenuRoot.alpha = on ? 1 : 0;
        inGameMenuRoot.interactable = on;
        inGameMenuRoot.blocksRaycasts = on;

        if (on && btnInGameSolo)
            EventSystem.current?.SetSelectedGameObject(btnInGameSolo.gameObject);
    }

    // ---------- Selection list ----------
    public void BindSelection(List<GameDef> games)
    {
        foreach (var b in _bands) if (b) Destroy(b);
        _bands.Clear();

        foreach (var g in games)
        {
            var go = Instantiate(bandPrefab, listRoot);
            var band = go.GetComponent<UISelectBand>();
            if (band != null)
            {
                band.Bind(g, _meta);
                band.onSelected = (rect) => ScrollToBand(rect); // auto-scroll when focused
            }
            _bands.Add(go);
        }

        EnsureEdgeSpacers();

        if (_bands.Count > 0)
        {
            WireVerticalNavigationForBands();
            var first = _bands[0].GetComponentInChildren<Button>();
            if (first) EventSystem.current?.SetSelectedGameObject(first.gameObject);
        }
    }

    // ---------- In-Game Menu wiring ----------
    public void OpenInGameMenuFor(GameManager gm)
    {
        ShowInGameMenu(true);

        if (btnInGameSolo) btnInGameSolo.onClick.RemoveAllListeners();
        if (btnInGameVs)   btnInGameVs.onClick.RemoveAllListeners();
        if (btnInGameCoop) btnInGameCoop.onClick.RemoveAllListeners();
        if (btnInGameAlt)  btnInGameAlt.onClick.RemoveAllListeners();
        if (btnInGameQuit) btnInGameQuit.onClick.RemoveAllListeners();

        if (btnInGameQuit) btnInGameQuit.onClick.AddListener(() =>
        {
            ShowInGameMenu(false);
            _meta.QuitToSelection();
        });

        if (btnInGameSolo) btnInGameSolo.onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Solo); });
        if (btnInGameVs)   btnInGameVs  .onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Versus2P); });
        if (btnInGameCoop) btnInGameCoop.onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Coop2P); });
        if (btnInGameAlt)  btnInGameAlt .onClick.AddListener(() => { ShowInGameMenu(false); gm.StartMode(GameMode.Alt2P); });
    }

    // Backwards-compatible alias:
    public void BindInGameMenu(GameManager gm) => OpenInGameMenuFor(gm);

    // ---------- Auto-scroll logic ----------
    // Centers the selected band in the viewport by driving verticalNormalizedPosition.
    void ScrollToBand(RectTransform item)
    {
        if (!selectScroll || !selectScroll.content || !selectViewport || !item) return;

        var content   = selectScroll.content;
        var viewport  = selectViewport;

        float contentH   = content.rect.height;
        float viewportH  = viewport.rect.height;
        float scrollable = contentH - viewportH;
        if (scrollable <= 0.001f) return;

        // Item top/bottom in CONTENT local space
        Vector3[] corners = new Vector3[4];
        item.GetWorldCorners(corners);
        Vector3 localTop    = content.InverseTransformPoint(corners[1]);
        Vector3 localBottom = content.InverseTransformPoint(corners[0]);

        float itemCenter = (localTop.y + localBottom.y) * 0.5f;

        // In content space, top edge y = (1 - pivot.y) * height
        float contentTopY   = (1f - content.pivot.y) * contentH;
        float centerFromTop = contentTopY - itemCenter;

        // Aim dead-center (+ tiny breathing room)
        float desiredFromTop = Mathf.Clamp(
            centerFromTop - (viewportH * 0.5f) + Mathf.Max(0f, Viewportpad * 0.25f),
            0f, scrollable
        );

        float targetNorm = 1f - Mathf.Clamp01(desiredFromTop / scrollable);

        // Snappy but not jarring; set directly for instant snap
        selectScroll.verticalNormalizedPosition =
            Mathf.Lerp(selectScroll.verticalNormalizedPosition, targetNorm, 0.8f);
    }

    // Create/update top/bottom spacers so first/last bands can center in the viewport.
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

    // ---------- Navigation lane for controller/D-pad ----------
    void WireVerticalNavigationForBands()
    {
        var bandButtons = new List<Button>();
        foreach (var go in _bands)
        {
            if (!go) continue;
            var band = go.GetComponent<UISelectBand>();
            if (band && band.bandButton) bandButtons.Add(band.bandButton);
        }

        for (int i = 0; i < bandButtons.Count; i++)
        {
            var b = bandButtons[i];
            var nav = new Navigation { mode = Navigation.Mode.Explicit };

            nav.selectOnUp   = (i > 0) ? bandButtons[i - 1] : (btnBackFromSelect ? btnBackFromSelect : btnOptions);
            nav.selectOnDown = (i < bandButtons.Count - 1) ? bandButtons[i + 1] : null;

            // Preserve left/right links (UISelectBand may have set them)
            var prev = b.navigation;
            nav.selectOnLeft  = prev.selectOnLeft;
            nav.selectOnRight = prev.selectOnRight;

            b.navigation = nav;
        }

        // Header buttons point back down into the list
        if (btnBackFromSelect && bandButtons.Count > 0)
        {
            var nav = btnBackFromSelect.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnDown = bandButtons[0];
            btnBackFromSelect.navigation = nav;
        }
        if (btnOptions && bandButtons.Count > 0)
        {
            var nav = btnOptions.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnDown = bandButtons[0];
            btnOptions.navigation = nav;
        }
    }

    // ---------- Options (no new screens) ----------
    // Cycles music & sfx volumes (100 → 60 → 30 → 0 → …) and toggles CRT on/off.
    void CycleBasicOptions()
    {
        float[] steps = { 1f, 0.6f, 0.3f, 0f };
        int nextM = (System.Array.IndexOf(steps, _musicVol) + 1) % steps.Length;
        int nextS = (System.Array.IndexOf(steps, _sfxVol)   + 1) % steps.Length;

        _musicVol = steps[nextM];
        _sfxVol   = steps[nextS];

        AudioListener.volume = _musicVol;
        RetroAudio.GlobalSfxVolume = _sfxVol;

        PlayerPrefs.SetFloat("opt_music", _musicVol);
        PlayerPrefs.SetFloat("opt_sfx",   _sfxVol);

        bool crt = !(crtEffectRoot && crtEffectRoot.activeSelf);
        if (crtEffectRoot) crtEffectRoot.SetActive(crt);
        PlayerPrefs.SetInt("opt_crt", crt ? 1 : 0);

        ShowOptionsToast($"Music {Mathf.RoundToInt(_musicVol*100)}%  •  SFX {Mathf.RoundToInt(_sfxVol*100)}%  •  CRT {(crt ? "ON" : "OFF")}");
    }

    void ShowOptionsToast(string msg)
    {
        if (!optionsToast) return;
        optionsToast.text = msg;
        optionsToast.gameObject.SetActive(true);
        var c = optionsToast.color; c.a = 1f; optionsToast.color = c;
        _toastT = 1.25f; // seconds on screen
    }
}
