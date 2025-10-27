using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using System.IO;

[System.Serializable] class Catalog { public string timezone; public Entry[] games; }
[System.Serializable] class Entry { public string id; public int number; public string title; public string[] modes; public string desc; public string cover; public string unlock_at_utc; }


public class MetaGameManager : MonoBehaviour
{
    public static MetaGameManager I;
    public UIManager ui;
    public Transform gameHost;

    public readonly List<GameDef> Games = new List<GameDef>();
    public RetroAudio audioBus;

    GameManager _current;

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        if (!ui) ui = FindObjectOfType<UIManager>(true);
        if (!gameHost) gameHost = new GameObject("GameHost").transform;
        if (!audioBus) audioBus = gameObject.AddComponent<RetroAudio>();
        BuildGameList();
        ui.Init(this);
        OpenTitle();
    }

  void BuildGameList()
{
    Games.Clear();
    var path = System.IO.Path.Combine(Application.streamingAssetsPath, "GameCatalog.json");
    var json = System.IO.File.ReadAllText(path);
    var cat = JsonUtility.FromJson<Catalog>(json);

    string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant();
        // keep letters+digits only
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    foreach (var e in cat.games)
    {
        var idNorm = Norm(e.id);
        GameFlags f = 0;
        foreach (var m in e.modes)
        {
            if (m=="1P") f |= GameFlags.Solo;
            if (m=="2P_VS") f |= GameFlags.Versus2P;
            if (m=="2P_COOP") f |= GameFlags.Coop2P;
            if (m=="2P_ALT") f |= GameFlags.Alt2P;
        }

        // tolerant mapping
        System.Type impl =
            idNorm == "beamer"        ? typeof(BeamerGame) :
            idNorm == "pillarprince"  ? typeof(PillarPrinceGame) :
            typeof(BeamerGame);

        var def = new GameDef(e.id, e.title, e.number, e.desc, f, impl);
        Games.Add(def);
        Debug.Log($"[Gamevault] Catalog: id='{e.id}' → norm='{idNorm}' → impl='{impl.Name}'");
    }
}
    public void OpenTitle()
    {
        StopGame();
        ui.ShowTitle(true);
        ui.ShowSelect(false);
    }

    public void OpenSelection()
    {
        StopGame();
        ui.BindSelection(Games);
        ui.ShowTitle(false);
        ui.ShowSelect(true);
    }

 public void StartGame(GameDef def)
{
    StopGame();
    Debug.Log($"[Gamevault] StartGame: '{def.title}' (id='{def.id}', impl='{def.implType.Name}')");
    var go = new GameObject(def.id);
    go.transform.SetParent(gameHost, false);
    _current = (GameManager)go.AddComponent(def.implType);
    _current.meta = this;
    _current.Def = def;
    _current.Begin();
    ui.ShowTitle(false);
    ui.ShowSelect(false);
    ui.BindInGameMenu(_current);
}
    public void StopGame()
    {
        if (_current)
        {
            Destroy(_current.gameObject);
            _current = null;
        }
    }

    public void QuitToSelection()
    {
        StopGame();
        OpenSelection();
    }

    public void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

[Flags]
public enum GameFlags { Solo=1, Versus2P=2, Coop2P=4, Alt2P=8 }

public class GameDef
{
    public string id;
    public string title;
    public int number;
    public string desc;
    public GameFlags flags;
    public Type implType;
    public GameDef(string id,string title,int number,string desc,GameFlags flags,Type implType)
    { this.id=id; this.title=title; this.number=number; this.desc=desc; this.flags=flags; this.implType=implType; }
}
