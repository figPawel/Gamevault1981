using UnityEngine;
using UnityEngine.UI;
using TMPro;
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

    [Header("Actions")]
    public Button btnPlay;

    GameDef _def;
    MetaGameManager _meta;

    public void Bind(GameDef def, MetaGameManager meta)
    {
        _def  = def;
        _meta = meta;

        SetText(titleText,  def.title);
        SetText(numberText, $"#{def.number}");
        SetText(descText,   def.desc);
        SetText(modesText,  Modes(def.flags));
        SetText(statsText,  "1P best: –   2P best: –");

        LoadCover(def.id);

        if (btnPlay)
        {
            btnPlay.onClick.RemoveAllListeners();
            btnPlay.onClick.AddListener(() => _meta.StartGame(_def));
        }
    }

    void SetText(TMP_Text t, string s)
    {
        if (t) t.text = s ?? "";
    }

    void LoadCover(string id)
    {
        if (!cartridgeImage) return;

        try
        {
            var p = Path.Combine(Application.streamingAssetsPath, $"covers/{id}.jpg");
            if (File.Exists(p))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode   = TextureWrapMode.Clamp;
                tex.LoadImage(File.ReadAllBytes(p));
                cartridgeImage.texture = tex;
            }
            else
            {
                cartridgeImage.texture = null; // no cover found
            }
        }
        catch
        {
            cartridgeImage.texture = null;
        }
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
