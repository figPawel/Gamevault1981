using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using System.IO;

[System.Serializable] class Catalog { public string timezone; public Entry[] games; }
[System.Serializable] class Entry { public string id; public int number; public string title; public string[] modes; public string desc; public string cover; public string unlock_at_utc; }

[Flags]
public enum GameFlags { Solo = 1, Versus2P = 2, Coop2P = 4, Alt2P = 8 }

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

public class MetaGameManager : MonoBehaviour
{
    public static MetaGameManager I;

    [Header("Scene Hooks")]
    public UIManager ui;
    public Transform gameHost;
    public RetroAudio audioBus;

    [Header("Catalog")]
    public readonly List<GameDef> Games = new List<GameDef>();

    [Header("Music (MP3/WAV) — assign in Inspector")]
    public AudioClip titleMusic;
    public AudioClip selectionMusic;
    public AudioClip genericGameMusic;

    AudioSource _music;
    GameManager _current;

    // NEW: remember which band launched the game so we can restore it on return
    GameDef _lastSelectedOnList;

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        if (!ui) ui = FindObjectOfType<UIManager>(true);
        if (!gameHost) gameHost = new GameObject("GameHost").transform;
        if (!audioBus) audioBus = gameObject.AddComponent<RetroAudio>();

        if (FindObjectOfType<AudioListener>() == null)
            gameObject.AddComponent<AudioListener>();

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.volume = 0.8f;

        BuildGameList();
        ui.Init(this);
        OpenTitle();

    }
    
    public void ShowInGameMenu(bool on)
{
    if (ui) ui.ShowInGameMenu(on);
}

    void BuildGameList()
    {
        Games.Clear();
        var path = System.IO.Path.Combine(Application.streamingAssetsPath, "GameCatalog.json");
        var json = System.IO.File.ReadAllText(path);
        var cat  = JsonUtility.FromJson<Catalog>(json);

        string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant();
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        System.Type beamer        = typeof(BeamerGame);
        System.Type pillarprince  = typeof(PillarPrinceGame);
        System.Type lasertango    = typeof(LaserTangoGame);

        var implMap = new Dictionary<string, System.Type>
        {
            { "beamer", beamer },
            { "pillarprince", pillarprince },
            { "lasertango", lasertango },

            { "soundbound", typeof(SoundBoundGame) },
            { "stickout",          null },
            { "puzzleracer",       null },
            { "pokethebabushka",   null },
            { "snakegrow",         null },
            { "spaceape",          null },
            { "rrbbyy",            null },
            { "cannonman",         null },
            { "railtanks",         null },
            { "fractalario",       null },
            { "twelvesecondbricks",null },
            { "looplander",        null },
            { "colorcircuit",      null },
            { "roguepixel",        null },
            { "towerborn",         null },
            { "orbithopper",       null },
            { "pixelquest",        null },
            { "gridclash",         null },
            { "chipforge",         null },
            { "echobeam",          null },
            { "depthcourier",      null },
            { "bytegarden",        null },
            { "phantompaddle",     null },
            { "swarmtune",         null },
            { "cannonhook",        null },
            { "reconfigmaze",      null },
            { "circulaire",        null },
            { "ballsort",          null },
            { "patterncomplete",   null },
            { "blindmaze",         null },
            { "outwardbound",      null },
            { "bossfight",         null },
            { "pulserunner",       null },
            { "fliptower",         null },
            { "magnetron",         null },
            { "driftorbit",        null },
            { "blipforge",         null },
            { "panicreactor",      null },
            { "hexhopper",         null },
            { "sectordiver",       null },
            { "bridgebreaker",     null },
            { "hexforge",          null },
            { "pulseriders",       null },
            { "dungeoncore",       null },
            { "quantumcubes",      null },
            { "riverraid",         null },
            { "starminer",         null },
            { "duelity",           null },
            { "chromocast",        null },
            { "chronohopper",      null },
            { "outpostdelta",      null },
            { "mindmaze",          null },
            { "signalloop",        null },
            { "gridprophet",       null },
            { "roguerails",        null },
            { "dualcore",          null },
            { "echorun",           null },
            { "holoheist",         null },
            { "logiclifter",       null },
            { "chronoseed",        null },
            { "bitdungeon",        null },
            { "patternpilot",      null },
            { "datarunner",        null },
            { "pixelprophet",      null },
            { "synthrider",        null },
            { "roguecolony",       null },
            { "bitarena",          null },
            { "skylinesprint",     null },
            { "codebreaker",       null },
            { "cosmicpostman",     null },
            { "shardfield",        null },
            { "duelcircuit",       null },
            { "thegreatvault",     null },
        };

        foreach (var e in cat.games)
        {
            var idNorm = Norm(e.id);

            GameFlags f = 0;
            if (e.modes != null)
            {
                foreach (var m in e.modes)
                {
                    if (m == "1P")      f |= GameFlags.Solo;
                    if (m == "2P_VS")   f |= GameFlags.Versus2P;
                    if (m == "2P_COOP") f |= GameFlags.Coop2P;
                    if (m == "2P_ALT")  f |= GameFlags.Alt2P;
                }
            }

            implMap.TryGetValue(idNorm, out var implType);

            var def = new GameDef(e.id, e.title, e.number, e.desc, f, implType);
            Games.Add(def);

            string implName = implType != null ? implType.Name : "UNIMPLEMENTED";
            Debug.Log($"[Gamevault] Catalog: id='{e.id}' → norm='{idNorm}' → impl={implName}");
        }
    }

    void PlayMusic(AudioClip clip)
    {
        if (_music == null) return;
        if (clip == null) { _music.Stop(); _music.clip = null; return; }
        if (_music.clip == clip && _music.isPlaying) return;
        _music.clip = clip;
        _music.Play();
    }

    public void PlayTitleMusic()     { PlayMusic(titleMusic); }
    public void PlaySelectionMusic() { PlayMusic(selectionMusic); }
    public void StopMusic()          { PlayMusic(null); }

    public void OpenTitle()
{
    StopGame();
    ui.ShowInGameMenu(false);   // <— add
    ui.ShowTitle(true);
    ui.ShowSelect(false);
    PlayTitleMusic();
}

public void OpenSelection()
{
    StopGame();
    ui.ShowInGameMenu(false);   
    ui.BindSelection(Games);
    ui.ShowTitle(false);
    ui.ShowSelect(true);

    if (selectionMusic) PlaySelectionMusic();
    else if (titleMusic) PlayTitleMusic();
    else StopMusic();
}

    public void StartGame(GameDef def, GameMode? autoMode = null)
{
    if (def == null) return;

    StopMusic();

    if (def.implType == null)
    {
        Debug.LogWarning($"[Gamevault] StartGame blocked: '{def.title}' (id='{def.id}') not implemented yet.");
        return;
    }

    // Remember which band launched this game
    _lastSelectedOnList = def;

    // Kill any previous instance
    StopGame();

    Debug.Log($"[Gamevault] StartGame: '{def.title}' (id='{def.id}', impl='{def.implType.Name}')");
    var go = new GameObject(def.id);
    go.transform.SetParent(gameHost, false);

    _current = (GameManager)go.AddComponent(def.implType);
    _current.meta = this;
    _current.Def  = def;
    _current.Begin();

    // Meta UI state
    ui.ShowTitle(false);
    ui.ShowSelect(false);

    if (autoMode.HasValue)
    {
        // Start requested mode immediately, keep menu hidden
        _current.StartMode(autoMode.Value);
        // (Optional: play per-game music if you have it)
        // if (genericGameMusic) PlayMusic(genericGameMusic);
    }
    else
    {
        // Normal path: show the in-game menu bound to this fresh instance
        ui.BindInGameMenu(_current);
    }
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
