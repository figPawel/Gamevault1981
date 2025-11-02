using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class LeaderboardBand : MonoBehaviour, IPointerClickHandler, ISubmitHandler, ISelectHandler, IDeselectHandler, IMoveHandler
{
    [Header("Config")]
    public string gameId;
    public bool hasTwoPlayer;
    public bool useTwoPlayerBoard;

    [Header("UI")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bestText;
    public RectTransform expandedRoot;
    public Transform rowsRoot;
    public GameObject rowPrefab;
    public TextMeshProUGUI viewHint;

    [Header("Input (optional)")]
    public Button toggleButton;  // usually the root Button on this band
    public Button leftButton;    // purely visual "<", can be non-interactable
    public Button rightButton;   // purely visual ">", can be non-interactable

    [Header("Layout (auto)")]
public RectTransform headerRoot; // optional; if null we derive from the band rect
LayoutElement _le;
RectTransform _rt;
float _headerH = -1f;
float _rowH = 40f;
float _rowSpacing = 0f;

    LeaderboardView view = LeaderboardView.GlobalTop;
    bool expanded;
    Button _bandButton;

    public Image bandHighlightFrame;       // optional highlight image (like UISelectBand)
    public string prettyTitleOverride;     // set by UIManager to the game's pretty name


void Awake()
{
    // Ensure there is a Button and it's clickable
    _bandButton = toggleButton ? toggleButton : GetComponent<Button>();
    if (!_bandButton) _bandButton = gameObject.AddComponent<Button>();
    _bandButton.interactable = true;
    _bandButton.enabled = true;

    // Button click -> expand/collapse
    _bandButton.onClick.RemoveListener(OnClickToggle);
    _bandButton.onClick.AddListener(OnClickToggle);

    // Keep focus on this row for Left/Right
    var nav = _bandButton.navigation;
    nav.mode = Navigation.Mode.Explicit;
    nav.selectOnLeft = null;
    nav.selectOnRight = null;
    _bandButton.navigation = nav;

    // Use first child as row template if no prefab provided
    if (!rowPrefab && rowsRoot && rowsRoot.childCount > 0)
        rowPrefab = rowsRoot.GetChild(0).gameObject;

    // Raycast target so clicks land
    var img = GetComponent<Image>();
    if (img) img.raycastTarget = true;

    // Layout handles
    _rt = transform as RectTransform;
    _le = GetComponent<LayoutElement>();
    if (!_le) _le = gameObject.AddComponent<LayoutElement>();
}


    void OnClickToggle()
    {
        // Always expand/collapse regardless of whether Steam found a board.
        ToggleExpanded();
    }
void Start()
{
    // Respect pretty title if UIManager set it. Otherwise try to be nice.
    if (titleText)
    {
        if (!string.IsNullOrEmpty(prettyTitleOverride))
            titleText.text = $"{prettyTitleOverride} Leaderboard";
        else if (!string.IsNullOrEmpty(gameId))
            titleText.text = $"{gameId} Leaderboard";
        else
            titleText.text = "Leaderboard";
    }

    if (expandedRoot) expandedRoot.gameObject.SetActive(false);

    CacheLayoutNumbers();         // measure header & row sizes
    RecomputePreferredHeight(0);  // collapsed height
    RefreshSummary();
    UpdateViewHint();
}


    void OnEnable()
    {
        if (PlayerDataManager.I != null)
            PlayerDataManager.I.OnLeaderboardToggleChanged += OnGlobalToggle;
    }

    void OnDisable()
    {
        if (PlayerDataManager.I != null)
            PlayerDataManager.I.OnLeaderboardToggleChanged -= OnGlobalToggle;
    }

    void OnGlobalToggle(bool on)
    {
        gameObject.SetActive(on);
    }

    // --- Focus & input like Options rows ---
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_bandButton) EventSystem.current?.SetSelectedGameObject(_bandButton.gameObject);
        ToggleExpanded();
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (_bandButton) EventSystem.current?.SetSelectedGameObject(_bandButton.gameObject);
        ToggleExpanded();
    }

    public void OnSelect(BaseEventData eventData)
    {
        // simple highlight like UISelectBand
        if (bandHighlightFrame) bandHighlightFrame.color = _accentOn;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (expanded) ToggleExpanded(force: false, target: false);
        if (bandHighlightFrame) bandHighlightFrame.color = _accentOff;
    }

 void SwitchView(int delta)
    {
        var vals = (LeaderboardView[])System.Enum.GetValues(typeof(LeaderboardView));
        int idx = System.Array.IndexOf(vals, view);
        idx = (idx + delta + vals.Length) % vals.Length;
        view = vals[idx];
        UpdateViewHint();
        if (expanded) RefreshList();
    }

    public void OnMove(AxisEventData eventData)
    {
        if (!IsFocused()) return;

        if (eventData.moveDir == MoveDirection.Left)
        {
            SwitchView(-1);
            eventData.Use(); // consume so focus doesn’t leave
        }
        else if (eventData.moveDir == MoveDirection.Right)
        {
            SwitchView(+1);
            eventData.Use();
        }
        // Up/Down fall through to normal navigation
    }

    bool IsFocused()
    {
        var es = EventSystem.current;
        if (!es) return false;
        var go = es.currentSelectedGameObject;
        return go && (_bandButton == null || go == _bandButton.gameObject || go == gameObject);
    }

    // --- Data refresh ---
    void RefreshSummary()
    {
        if (PlayerDataManager.I == null)
        {
            if (bestText) bestText.text = "Leaderboards unavailable";
            return;
        }

        PlayerDataManager.I.GetTopSummary(gameId, useTwoPlayerBoard, result =>
        {
            var (ok, best) = result;
            if (bestText) bestText.text = ok ? $"World's best: {best:N0}" : "Leaderboards off";
        });
    }

void ToggleExpanded(bool force = false, bool target = true)
{
    expanded = force ? target : !expanded;
    if (expandedRoot) expandedRoot.gameObject.SetActive(expanded);

    // Push layout even before data has arrived (show one row’s worth so it feels responsive)
    RecomputePreferredHeight(expanded ? Mathf.Max(1, CountVisibleRows()) : 0);

    if (expanded) RefreshList();
}

    void UpdateViewHint()
    {
        if (!viewHint) return;
        switch (view)
        {
            case LeaderboardView.GlobalTop: viewHint.text = "Global"; break;
            case LeaderboardView.FriendsTop: viewHint.text = "Friends"; break;
            case LeaderboardView.AroundPlayer: viewHint.text = "Around You"; break;
        }
    }

    void RefreshList()
{
    if (!rowsRoot || !rowPrefab) { RecomputePreferredHeight(1); return; }

    // Clear old rows but KEEP the template object so we can reuse it as row #1.
    for (int i = rowsRoot.childCount - 1; i >= 0; i--)
    {
        var t = rowsRoot.GetChild(i);
        if (t && t.gameObject != rowPrefab) Destroy(t.gameObject);
    }

    rowPrefab.SetActive(false);

    int size = 10; // show 10 rows by default
    if (PlayerDataManager.I == null) { RecomputePreferredHeight(1); return; }

    PlayerDataManager.I.GetFullBoard(gameId, useTwoPlayerBoard, view, size, entries =>
    {
        var list = entries ?? new List<LeaderboardEntry>();

        if (list.Count == 0)
        {
            rowPrefab.SetActive(false);
            RecomputePreferredHeight(1); // still give the expanded area some height
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            GameObject rowGO;
            if (i == 0)
            {
                rowGO = rowPrefab;
                rowGO.transform.SetAsLastSibling();
                rowGO.SetActive(true);
            }
            else
            {
                rowGO = Instantiate(rowPrefab, rowsRoot);
                rowGO.SetActive(true);
            }

            var texts = rowGO.GetComponentsInChildren<TextMeshProUGUI>(true);
            int rank = list[i].globalRank;
            string name = list[i].userName;
            int score = list[i].score;

            if (texts.Length >= 3)
            {
                texts[0].text = rank.ToString();
                texts[1].text = name;
                texts[2].text = score.ToString("N0");
            }
            else if (texts.Length >= 1)
            {
                texts[0].text = $"{rank,4}. {name} — {score:N0}";
            }
        }

        // Now that the number of rows is known, resize the band so it pushes the list down
        RecomputePreferredHeight(list.Count);
    });
}

    Color _accentOn = new Color(1, 1, 1, 0);  // set via SetAccent
    Color _accentOff = new Color(1, 1, 1, 0);

    public void SetAccent(Color baseColor)
    {
        // Match UISelectBand behavior: bright when selected, dim when idle.
        _accentOn = baseColor;
        _accentOff = new Color(baseColor.r, baseColor.g, baseColor.b, 0.18f);

        // If we have a highlight frame, start dim
        if (bandHighlightFrame) bandHighlightFrame.color = _accentOff;

        // Also give the button a ColorBlock so it highlights on focus/hover
        if (_bandButton)
        {
            var cb = _bandButton.colors;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            cb.normalColor = Color.white;
            cb.highlightedColor = Color.Lerp(baseColor, Color.white, 0.35f);
            cb.selectedColor = Color.Lerp(baseColor, Color.white, 0.20f);
            cb.pressedColor = Color.Lerp(baseColor, Color.black, 0.20f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            _bandButton.transition = Selectable.Transition.ColorTint;
            _bandButton.colors = cb;

            // Use our own Image (if present) as targetGraphic so tinting shows
            if (!_bandButton.targetGraphic)
                _bandButton.targetGraphic = GetComponent<Image>();
        }
    }


void CacheLayoutNumbers()
{
    // Temporarily ensure the expanded root is hidden while we measure collapsed height
    bool wasExpanded = expandedRoot && expandedRoot.gameObject.activeSelf;
    if (expandedRoot) expandedRoot.gameObject.SetActive(false);
    Canvas.ForceUpdateCanvases();

    // Header height (prefer headerRoot if present; otherwise the band rect)
    if (_headerH < 0f)
        _headerH = headerRoot ? LayoutUtility.GetPreferredHeight(headerRoot)
                              : (_rt ? _rt.rect.height : 48f);

    // Row height from prefab (fallback ~36)
    if (rowPrefab)
    {
        var rrt = rowPrefab.GetComponent<RectTransform>();
        if (rrt && rrt.rect.height > 1f) _rowH = rrt.rect.height;
    }

    // Spacing if a VerticalLayoutGroup is on rowsRoot
    var vlg = rowsRoot ? rowsRoot.GetComponent<VerticalLayoutGroup>() : null;
    _rowSpacing = vlg ? vlg.spacing : 0f;

    if (expandedRoot) expandedRoot.gameObject.SetActive(wasExpanded);
}

int CountVisibleRows()
{
    if (!rowsRoot) return 0;
    int c = 0;
    for (int i = 0; i < rowsRoot.childCount; i++)
        if (rowsRoot.GetChild(i).gameObject.activeSelf) c++;
    return c;
}

void RecomputePreferredHeight(int rowsForLayout)
{
    CacheLayoutNumbers();

    float rowsBlock = 0f;
    if (expanded && rowsForLayout > 0)
        rowsBlock = rowsForLayout * _rowH + Mathf.Max(0, rowsForLayout - 1) * _rowSpacing;

    float target = expanded ? (_headerH + rowsBlock) : _headerH;

    // Drive the band’s height so the parent VerticalLayoutGroup reflows the list
    _le.preferredHeight = target;
    _le.minHeight       = target;

    Canvas.ForceUpdateCanvases();
    LayoutRebuilder.ForceRebuildLayoutImmediate(_rt);
    var parent = _rt ? _rt.parent as RectTransform : null;
    if (parent) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
}


}
