using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class UISelectBand : MonoBehaviour
{
    public RawImage cartridgeImage;
    public TMP_Text titleText;
    public TMP_Text numberText;
    public TMP_Text descText;
    public TMP_Text modesText;
    public Button btnPlay;
    public Button btnInfo;
    public GameObject infoPanel;
    public TMP_Text statsText;

    GameDef _def;
    MetaGameManager _meta;

   
public void Bind(GameDef def, MetaGameManager meta)
{
    _def=def; _meta=meta;
    titleText.text=def.title; numberText.text="#"+def.number; descText.text=def.desc; modesText.text=Modes(def.flags);
    var p = Path.Combine(Application.streamingAssetsPath, $"covers/{def.id}.jpg");
    if (File.Exists(p)){ var tex=new Texture2D(2,2,TextureFormat.RGBA32,false); tex.LoadImage(File.ReadAllBytes(p)); cartridgeImage.texture=tex; }
    btnPlay.onClick.RemoveAllListeners(); btnPlay.onClick.AddListener(()=>_meta.StartGame(_def));
    btnInfo.onClick.RemoveAllListeners(); btnInfo.onClick.AddListener(()=>ToggleInfo());
    infoPanel.SetActive(false); statsText.text="1P best: –   2P best: –";
}

    string Modes(GameFlags f)
    {
        bool s=(f&GameFlags.Solo)!=0, v=(f&GameFlags.Versus2P)!=0, c=(f&GameFlags.Coop2P)!=0, a=(f&GameFlags.Alt2P)!=0;
        string m="";
        if(s) m+="1P ";
        if(v) m+="2P VS ";
        if(c) m+="2P COOP ";
        if(a) m+="2P ALT ";
        return m.Trim();
    }

    void ToggleInfo() => infoPanel.SetActive(!infoPanel.activeSelf);
}
