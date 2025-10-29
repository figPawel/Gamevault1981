using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.EventSystems;
using System.IO;

[System.Serializable] class Catalog { public string timezone; public Entry[] games; }
[System.Serializable] class Entry { public string id; public int number; public string title; public string[] modes; public string desc; public string cover; public string unlock_at_utc; }

[Flags] public enum GameFlags { Solo = 1, Versus2P = 2, Coop2P = 4, Alt2P = 8 }

public class GameDef
{
    public string id, title; public int number; public string desc; public GameFlags flags; public Type implType;
    public GameDef(string id,string title,int number,string desc,GameFlags flags,Type implType)
    { this.id=id; this.title=title; this.number=number; this.desc=desc; this.flags=flags; this.implType=implType; }
}

public class MetaGameManager : MonoBehaviour
{
    public static MetaGameManager I;

    [Header("Scene Hooks")]
    public UIManager ui;
    public Transform gameHost;

    [Header("Catalog")]
    public readonly List<GameDef> Games = new List<GameDef>();

    [Header("Music (fallbacks)")]
    public AudioClip titleMusic;
    public AudioClip selectionMusic;

    // --- Audio ---
    AudioSource _music;                 // music-only AudioSource (looping)
    public RetroAudio audioBus;         // SFX/“beep” bus (separate source)

    GameManager _current;

    // remember last band the user focused (for title playback)
    GameDef _lastFocusedDef;

    // preview debounce
    int _previewToken = 0;
    const float PREVIEW_DELAY = 0.30f;

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        if (!ui) ui = FindObjectOfType<UIManager>(true);
        if (!gameHost) gameHost = new GameObject("GameHost").transform;

        if (FindObjectOfType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

        // Music source lives on this object
        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.volume = 0.8f;

        // Beep/SFX bus uses its own AudioSource on a child so it never interrupts music
        if (!audioBus)
        {
            var busGO = new GameObject("AudioBus");
            busGO.transform.SetParent(transform, false);
            audioBus = busGO.AddComponent<RetroAudio>();     // RetroAudio adds/uses its own AudioSource
            var src = busGO.GetComponent<AudioSource>();
            src.playOnAwake = false; src.loop = false; src.spatialBlend = 0f; src.volume = 1f;
        }

        // Ensure SFX volume matches saved setting
        RetroAudio.GlobalSfxVolume = PlayerPrefs.GetFloat("opt_sfx", 1.0f);

        BuildGameList();
        if (ui) ui.Init(this);
        OpenTitle();
    }

    void BuildGameList()
    {
        Games.Clear();
        var path = Path.Combine(Application.streamingAssetsPath, "GameCatalog.json");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[Gamevault] Missing catalog at {path}");
            return;
        }
        var json = File.ReadAllText(path);
        var cat  = JsonUtility.FromJson<Catalog>(json);

        string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.ToLowerInvariant();
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        // ---- implementation map (restored) ----
        System.Type beamer        = typeof(BeamerGame);
        System.Type pillarprince  = typeof(PillarPrinceGame);
        System.Type lasertango    = typeof(LaserTangoGame);

        var implMap = new Dictionary<string, System.Type>
        {
            { "beamer", beamer },
            { "pillarprince", pillarprince },
            { "lasertango", lasertango },

            { "soundbound", typeof(SoundBoundGame) },
            { "stickout",          null }, { "puzzleracer",       null }, { "pokethebabushka",   null },
            { "snakegrow",         null }, { "spaceape",          null }, { "rrbbyy",            null },
            { "cannonman",         null }, { "railtanks",         null }, { "fractalario",       null },
            { "twelvesecondbricks",null }, { "looplander",        null }, { "colorcircuit",      null },
            { "roguepixel",        null }, { "towerborn",         null }, { "orbithopper",       null },
            { "pixelquest",        null }, { "gridclash",         null }, { "chipforge",         null },
            { "echobeam",          null }, { "depthcourier",      null }, { "bytegarden",        null },
            { "phantompaddle",     null }, { "swarmtune",         null }, { "cannonhook",        null },
            { "reconfigmaze",      null }, { "circulaire",        null }, { "ballsort",          null },
            { "patterncomplete",   null }, { "blindmaze",         null }, { "outwardbound",      null },
            { "bossfight",         null }, { "pulserunner",       null }, { "fliptower",         null },
            { "magnetron",         null }, { "driftorbit",        null }, { "blipforge",         null },
            { "panicreactor",      null }, { "hexhopper",         null }, { "sectordiver",       null },
            { "bridgebreaker",     null }, { "hexforge",          null }, { "pulseriders",       null },
            { "dungeoncore",       null }, { "quantumcubes",      null }, { "riverraid",         null },
            { "starminer",         null }, { "duelity",           null }, { "chromocast",        null },
            { "chronohopper",      null }, { "outpostdelta",      null }, { "mindmaze",          null },
            { "signalloop",        null }, { "gridprophet",       null }, { "roguerails",        null },
            { "dualcore",          null }, { "echorun",           null }, { "holoheist",         null },
            { "logiclifter",       null }, { "chronoseed",        null }, { "bitdungeon",        null },
            { "patternpilot",      null }, { "datarunner",        null }, { "pixelprophet",      null },
            { "synthrider",        null }, { "roguecolony",       null }, { "bitarena",          null },
            { "skylinesprint",     null }, { "codebreaker",       null }, { "cosmicpostman",     null },
            { "shardfield",        null }, { "duelcircuit",       null }, { "thegreatvault",     null },
        };
        // --------------------------------------

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

    // ---------- Music control ----------
    void PlayMusic(AudioClip clip)
    {
        if (_music == null) return;
        if (clip == null) { _music.Stop(); _music.clip = null; return; }
        if (_music.clip == clip && _music.isPlaying) return;
        _music.clip = clip; _music.loop = true; _music.volume = PlayerPrefs.GetFloat("opt_music", 0.8f);
        _music.Play();
    }

    public void PlayTitleMusic()     { PlayMusic(titleMusic); }
    public void PlaySelectionMusic() { PlayMusic(selectionMusic); }
    public void StopMusic()          { PlayMusic(null); }

    public void OpenTitle()
    {
        StopGame();
        ui?.ShowInGameMenu(false);
        ui?.ShowTitle(true);
        ui?.ShowSelect(false);

        // If user wants per-game track on Title and we know the last focused game, play it.
        bool perGameOnTitle = PlayerPrefs.GetInt("msk_title", 1) != 0;
        if (perGameOnTitle && _lastFocusedDef != null)
        {
            StartCoroutine(PlayTrackRoutine(_lastFocusedDef.title,
                PlayerPrefs.GetFloat("opt_music", 0.8f), loop:true));
        }
        else
        {
            PlayTitleMusic();
        }

        // Make sure controller can move immediately on the title
        if (ui && ui.btnPlay) EventSystem.current?.SetSelectedGameObject(ui.btnPlay.gameObject);
    }

    public void OpenSelection()
    {
        StopGame();
        ui?.ShowInGameMenu(false);
        ui?.BindSelection(Games);
        ui?.ShowTitle(false);
        ui?.ShowSelect(true);

        bool playPerGame = PlayerPrefs.GetInt("msk_sel", 1) != 0;
        if (playPerGame) StopMusic();
        else if (selectionMusic) PlaySelectionMusic();
        else if (titleMusic) PlayTitleMusic();
        else StopMusic();
    }

    public void StartGame(GameDef def, GameMode? autoMode = null)
    {
        if (def == null) return;

        // Decide music policy before spawning the game.
        bool wantInGameMusic = PlayerPrefs.GetInt("msk_game", 0) != 0;

        if (wantInGameMusic)
        {
            float vol = Mathf.Clamp01(PlayerPrefs.GetFloat("msk_game_vol", 0.35f));
            StartCoroutine(PlayTrackRoutine(def.title, vol, loop:true));   // don’t stop; replace seamlessly
        }
        else
        {
            // If user didn’t ask for in-game music, don’t forcibly stop
            // anything that’s already playing (title/selection/per-game).
        }

        if (def.implType == null)
        {
            Debug.LogWarning($"[Gamevault] StartGame blocked: '{def.title}' (id='{def.id}') not implemented yet.");
            return;
        }

        StopGame();

        Debug.Log($"[Gamevault] StartGame: '{def.title}' (id='{def.id}', impl='{def.implType.Name}')");
        var go = new GameObject(def.id);
        go.transform.SetParent(gameHost, false);

        _current = (GameManager)go.AddComponent(def.implType);
        _current.meta = this;
        _current.Def  = def;
        _current.Begin();

        ui?.ShowTitle(false);
        ui?.ShowSelect(false);

        if (autoMode.HasValue) _current.StartMode(autoMode.Value);
        else ui?.BindInGameMenu(_current);
    }

    public void StopGame()
    {
        if (_current) { Destroy(_current.gameObject); _current = null; }
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

    // -------- Per-game soundtrack preview on band focus --------
    public void PreviewGameTrack(GameDef def)
    {
        if (def == null) return;
        _lastFocusedDef = def;

        if (PlayerPrefs.GetInt("msk_sel", 1) == 0) return; // user disabled previews

        _previewToken++;
        StartCoroutine(PreviewRoutine(_previewToken, def.title));
    }

    // Shared loader used by preview/title/game
    System.Collections.IEnumerator PlayTrackRoutine(string title, float volume, bool loop)
    {
        string sa = Application.streamingAssetsPath;
        string tracks = Path.Combine(sa, "Tracks");
        string mp3 = Path.Combine(tracks, $"{title}.mp3");
        string wav = Path.Combine(tracks, $"{title}.wav");

        string chosen = null;
        try { if (File.Exists(mp3)) chosen = mp3; else if (File.Exists(wav)) chosen = wav; }
        catch { }

        if (string.IsNullOrEmpty(chosen))
        {
            Debug.Log($"[Gamevault] soundtrack not found for '{title}' in StreamingAssets/Tracks (expected '{title}.mp3' or .wav).");
            yield break;
        }

        string url = "file://" + chosen.Replace('\\','/');
        AudioType type = chosen.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.MPEG;

        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
        {
            yield return req.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogWarning($"[Gamevault] failed to load soundtrack '{chosen}': {req.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null) { Debug.LogWarning($"[Gamevault] empty audio clip for '{chosen}'."); yield break; }

            _music.loop = loop;
            _music.clip = clip;
            _music.volume = Mathf.Clamp01(volume) * PlayerPrefs.GetFloat("opt_music", 0.8f);
            _music.Play();
            Debug.Log($"[Gamevault] playing soundtrack → {Path.GetFileName(chosen)} (vol={_music.volume:0.00})");
        }
    }

    System.Collections.IEnumerator PreviewRoutine(int token, string title)
    {
        yield return new WaitForSecondsRealtime(PREVIEW_DELAY);
        if (token != _previewToken) yield break; // superseded by a newer focus
        float vol = PlayerPrefs.GetFloat("opt_music", 0.8f);
        yield return PlayTrackRoutine(title, vol, loop:true);
    }
}
