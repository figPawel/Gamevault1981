using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;

public class UISelectBand : MonoBehaviour
{
    [Header("Card visuals")]
    public RawImage cartridgeImage;
    public TMP_Text titleText;
    public TMP_Text numberText;
    public TMP_Text descText;
    public TMP_Text modesText;
    public TMP_Text statsText;

    [Header("Cartridge Labels")]
    [SerializeField] string labelsFolder = "labels";          // StreamingAssets/<labelsFolder>/<id>.(png|jpg|jpeg)
    [SerializeField] Vector2 defaultFocus = new Vector2(0.5f, 0.5f); // 0..1 center by default
    [Serializable] public struct LabelCropOverride { public string id; public Vector2 focus; }
    [SerializeField] LabelCropOverride[] cropOverrides;

    [Header("Actions")]
    public Button btnPlay;

    GameDef _def;
    MetaGameManager _meta;

    // ---------- Public API ----------
    public void Bind(GameDef def, MetaGameManager meta)
    {
        _def  = def;
        _meta = meta;

        SetText(titleText,  def.title);
        SetText(numberText, $"#{def.number}");
        SetText(descText,   def.desc);
        SetText(modesText,  Modes(def.flags));
        SetText(statsText,  "1P best: –   2P best: –");

        LoadAndCropLabel();

        if (btnPlay)
        {
            btnPlay.onClick.RemoveAllListeners();
            btnPlay.onClick.AddListener(() => _meta.StartGame(_def));
        }
    }

    // ---------- Label loading & cropping ----------
    void LoadAndCropLabel()
    {
        if (!cartridgeImage || _def == null) return;

        Texture2D tex = TryLoadLabelTexture();
        if (tex == null)
        {
            // no label found; clear but keep full UV
            cartridgeImage.texture = null;
            cartridgeImage.uvRect  = new Rect(0, 0, 1, 1);
            return;
        }

        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;

        // Compute current window aspect from the RawImage’s rect
        var rt = cartridgeImage.rectTransform.rect;
        float targetAspect = (rt.height <= 0f) ? 1.0f : (rt.width / rt.height);

        ApplyCoverCrop(cartridgeImage, tex, targetAspect, FindFocus(_def.id));
    }

    Texture2D TryLoadLabelTexture()
    {
        string sa = Application.streamingAssetsPath;

        // Helper to attempt a file
        Texture2D Try(string absPath)
        {
            if (!File.Exists(absPath)) return null;
            var bytes = File.ReadAllBytes(absPath);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(bytes, false);
            t.name = Path.GetFileNameWithoutExtension(absPath);
            return t;
        }

        // We try in order:
        // 1) StreamingAssets/labels/<id>.(png|jpg|jpeg)
        // 2) StreamingAssets/labels/<title>.(png|jpg|jpeg)   (legacy assets with spaces)
        // 3) StreamingAssets/covers/<id>.(png|jpg|jpeg)      (fallback)
        // 4) StreamingAssets/covers/<title>.(png|jpg|jpeg)
        string[] names = {
            _def.id,
            _def.title
        };
        string[] exts = { ".png", ".jpg", ".jpeg" };
        string[] folders = {
            Path.Combine(sa, labelsFolder),
            Path.Combine(sa, "covers")
        };

        foreach (var folder in folders)
        {
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                foreach (var e in exts)
                {
                    var p = Path.Combine(folder, n + e);
                    var tex = Try(p);
                    if (tex != null) return tex;
                }
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

    // Cover-style crop in UV space (no re-sampling). Matches the RawImage window aspect.
    void ApplyCoverCrop(RawImage img, Texture2D tex, float targetAspect, Vector2 focus01)
    {
        img.texture = tex;
        if (tex == null)
        {
            img.uvRect = new Rect(0, 0, 1, 1);
            return;
        }

        float srcAspect = (float)tex.width / Mathf.Max(1, tex.height);
        focus01 = new Vector2(Mathf.Clamp01(focus01.x), Mathf.Clamp01(focus01.y));

        if (srcAspect > targetAspect)
        {
            // Source wider → crop horizontally
            float uvW = targetAspect / srcAspect;        // fraction of width we show
            float u   = Mathf.Clamp(focus01.x - uvW * 0.5f, 0f, 1f - uvW);
            img.uvRect = new Rect(u, 0f, uvW, 1f);
        }
        else
        {
            // Source taller → crop vertically
            float uvH = srcAspect / targetAspect;        // fraction of height we show
            float v   = Mathf.Clamp(focus01.y - uvH * 0.5f, 0f, 1f - uvH);
            img.uvRect = new Rect(0f, v, 1f, uvH);
        }
    }

    // ---------- Misc ----------
    void SetText(TMP_Text t, string s)
    {
        if (t) t.text = s ?? "";
    }

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
}
