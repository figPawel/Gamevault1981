using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// One-line, gamepad/keyboard/mouse friendly option row.
/// - Up/Down: normal navigation
/// - Left/Right: adjust value (consumed locally)
/// - Click: first selects, second changes (Right)
/// - Submit/A: move to next row
public class OptionBand : MonoBehaviour,
    ISelectHandler, IDeselectHandler, ISubmitHandler, IMoveHandler, IPointerClickHandler
{
    [Header("Hook up in prefab")]
    public Button bandButton;           // focus target
    public Image  highlightFrame;       // background/outline to tint
    public TMP_Text labelText;          // left label
    public TMP_Text valueText;          // right value
    public Image arrowLeft;             // optional ← icon
    public Image arrowRight;            // optional → icon

    [Header("Colors")]
    public Color accent = new Color(0.25f, 0.9f, 1f, 1f);

    // Called when this band gets focus so the parent can auto-scroll.
    public Action<RectTransform> onSelected;

    // Value callbacks (provided by Options controller).
    Func<string> _get;
    Action _left, _right;

    bool _selected;
    Color _dim;
    public RectTransform Rect => transform as RectTransform;

    public void Bind(string label, Func<string> getValue, Action onLeft, Action onRight)
    {
        _get   = getValue;
        _left  = onLeft;
        _right = onRight;

        if (labelText) labelText.text = label ?? "";
        _dim = new Color(accent.r, accent.g, accent.b, 0.22f);   // base tint uses THIS row's accent
        Refresh();

        if (bandButton)
        {
            bandButton.onClick.RemoveAllListeners(); // use IPointerClick so first click selects
            var nav = bandButton.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnLeft  = null;   // keep focus so IMove consumes L/R
            nav.selectOnRight = null;
            bandButton.navigation = nav;

            if (!bandButton.targetGraphic)
                bandButton.targetGraphic = highlightFrame ? (Graphic)highlightFrame
                                                          : bandButton.GetComponent<Image>();
            ApplyColors(bandButton);
            SetHighlight(false);
        }

        if (arrowLeft)  arrowLeft.enabled  = false;
        if (arrowRight) arrowRight.enabled = false;
    }

    public void Refresh()
    {
        if (valueText != null && _get != null) valueText.text = _get();
    }

    void ApplyColors(Selectable s)
    {
        var cb = s.colors;
        cb.colorMultiplier  = 1f;
        cb.fadeDuration     = 0.08f;
        cb.normalColor      = Color.white;
        cb.highlightedColor = Color.Lerp(accent, Color.white, 0.35f);
        cb.selectedColor    = Color.Lerp(accent, Color.white, 0.20f);
        cb.pressedColor     = Color.Lerp(accent, Color.black, 0.20f);
        cb.disabledColor    = new Color(0.5f,0.5f,0.5f,0.5f);
        s.transition = Selectable.Transition.ColorTint;
        s.colors = cb;
    }

    void SetHighlight(bool on)
    {
        if (highlightFrame) highlightFrame.color = on ? accent : _dim;  // base row tinted
        if (labelText) labelText.color = on ? Color.Lerp(accent, Color.white, 0.35f)
                                            : new Color(1,1,1,0.90f);
        if (valueText) valueText.color = on ? Color.white : new Color(1,1,1,0.85f);
        if (arrowLeft)  arrowLeft.enabled  = on;
        if (arrowRight) arrowRight.enabled = on;
    }

    // ---------------- EventSystem hooks ----------------
    public void OnSelect(BaseEventData e)
    {
        _selected = true;
        SetHighlight(true);
        onSelected?.Invoke(Rect);
    }

    public void OnDeselect(BaseEventData e)
    {
        _selected = false;
        SetHighlight(false);
    }

    public void OnMove(AxisEventData eventData)
    {
        if (!_selected) return;

        if (eventData.moveDir == MoveDirection.Left)
        {
            _left?.Invoke();  Refresh();
            eventData.Use();
        }
        else if (eventData.moveDir == MoveDirection.Right)
        {
            _right?.Invoke(); Refresh();
            eventData.Use();
        }
    }

    public void OnSubmit(BaseEventData e) => SubmitOrNext();

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || bandButton == null) return;

        var es = EventSystem.current;
        if (es == null) return;

        // First click selects, second click changes value (Right)
        if (es.currentSelectedGameObject != bandButton.gameObject)
        {
            es.SetSelectedGameObject(bandButton.gameObject);
            return;
        }

        _right?.Invoke();
        Refresh();
    }

    void SubmitOrNext()
    {
        if (!bandButton) return;
        var next = bandButton.navigation.selectOnDown;
        if (next && next.gameObject.activeInHierarchy)
            EventSystem.current?.SetSelectedGameObject(next.gameObject);
    }
}
