using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public CanvasGroup titleRoot;
    public Button btnPlay;
    public Button btnOptions;
    public Button btnQuit;

    public CanvasGroup selectRoot;
    public RectTransform listRoot;
    public GameObject bandPrefab;
    public Button btnBackFromSelect;

    public CanvasGroup inGameMenuRoot;
    public TMP_Text inGameTitle;
    public Button btnInGameSolo;
    public Button btnInGameVs;
    public Button btnInGameCoop;
    public Button btnInGameAlt;
    public Button btnInGameQuit;

    public GameObject menuGridRoot;

    MetaGameManager _meta;
    List<GameObject> _bands = new();

    public void Init(MetaGameManager meta)
    {
        _meta = meta;
        btnPlay.onClick.AddListener(_meta.OpenSelection);
        btnQuit.onClick.AddListener(_meta.QuitApp);
        btnBackFromSelect.onClick.AddListener(_meta.OpenTitle);
        ShowTitle(false); ShowSelect(false); ShowInGameMenu(false);
    }

    public void ShowTitle(bool on)
    {
        titleRoot.alpha = on?1:0;
        titleRoot.interactable = on;
        titleRoot.blocksRaycasts = on;
         titleRoot.alpha = on?1:0; titleRoot.interactable = on; titleRoot.blocksRaycasts = on;
    if (menuGridRoot) menuGridRoot.SetActive(on);
    }

    public void ShowSelect(bool on)
    {
        selectRoot.alpha = on?1:0;
        selectRoot.interactable = on;
        selectRoot.blocksRaycasts = on;
        
    
    }

    public void ShowInGameMenu(bool on)
    {
        inGameMenuRoot.alpha = on?1:0;
        inGameMenuRoot.interactable = on;
        inGameMenuRoot.blocksRaycasts = on;
        inGameMenuRoot.alpha = on?1:0; inGameMenuRoot.interactable = on; inGameMenuRoot.blocksRaycasts = on;
    if (menuGridRoot && on) menuGridRoot.SetActive(false);
    }

    public void BindSelection(List<GameDef> games)
    {
        foreach (var b in _bands) Destroy(b);
        _bands.Clear();
        foreach (var g in games)
        {
            var go = Instantiate(bandPrefab, listRoot);
            var band = go.GetComponent<UISelectBand>();
            band.Bind(g, _meta);
            _bands.Add(go);
        }
    }

    public void BindInGameMenu(GameManager gm)
    {
        inGameTitle.text = gm.Def.title;
        btnInGameSolo.gameObject.SetActive((gm.Def.flags & GameFlags.Solo)!=0);
        btnInGameVs.gameObject.SetActive((gm.Def.flags & GameFlags.Versus2P)!=0);
        btnInGameCoop.gameObject.SetActive((gm.Def.flags & GameFlags.Coop2P)!=0);
        btnInGameAlt.gameObject.SetActive((gm.Def.flags & GameFlags.Alt2P)!=0);

        btnInGameSolo.onClick.RemoveAllListeners();
        btnInGameVs.onClick.RemoveAllListeners();
        btnInGameCoop.onClick.RemoveAllListeners();
        btnInGameAlt.onClick.RemoveAllListeners();
        btnInGameQuit.onClick.RemoveAllListeners();
btnInGameQuit.onClick.AddListener(() =>
{
    ShowInGameMenu(false);   // <<< hide the menu overlay
    _meta.OpenSelection();   // back to the main game selection (or your title)
});

        btnInGameSolo.onClick.AddListener(()=>{ ShowInGameMenu(false); gm.StartMode(GameMode.Solo); });
        btnInGameVs.onClick.AddListener(()=>{ ShowInGameMenu(false); gm.StartMode(GameMode.Versus2P); });
        btnInGameCoop.onClick.AddListener(()=>{ ShowInGameMenu(false); gm.StartMode(GameMode.Coop2P); });
        btnInGameAlt.onClick.AddListener(()=>{ ShowInGameMenu(false); gm.StartMode(GameMode.Alt2P); });
        btnInGameQuit.onClick.AddListener(_meta.OpenSelection);

        ShowInGameMenu(true);
    }
}
