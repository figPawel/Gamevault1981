// === MetaGameManager.cs ===
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.Audio;

[Serializable] class Catalog { public string timezone; public Entry[] games; }
[Serializable] class Entry { public string id; public int number; public string title; public string[] modes; public string desc; public string cover; public string unlock_at_utc; }

[Flags] public enum GameFlags { Solo = 1, Versus2P = 2, Coop2P = 4, Alt2P = 8 }

public class GameDef
{
    public string id, title;
    public int number;
    public string desc;
    public GameFlags flags;
    public Type implType;
    public DateTime? unlockAtUtc;

    public GameDef(string id, string title, int number, string desc, GameFlags flags, Type implType)
    {
        this.id = id; this.title = title; this.number = number; this.desc = desc; this.flags = flags; this.implType = implType;
    }
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

    [Header("Unlocks")]
    [Tooltip("If ON, locked games appear as disabled bands with a live countdown. If OFF, locked games are hidden from the list.")]
    public bool ShowLockedBands = true;

    [Tooltip("First N games are always unlocked (ship with the release).")]
    public int AlwaysUnlockedFirstN = 20;

    [Tooltip("Offset (seconds) to add to local UTC. When you wire Supabase server time, set this once at boot.")]
    public double serverTimeOffsetSeconds = 0;

    [Header("Audio Mixer")]
    public AudioMixer mixer;
    public AudioMixerGroup mixerMusicGroup;
    public AudioMixerGroup mixerSfxGroup;
    [Tooltip("Exposed mixer parameter for music volume in dB.")]
    public string musicVolumeParam = "MusicVolDb";
    [Tooltip("Exposed mixer parameter for SFX volume in dB.")]
    public string sfxVolumeParam = "SfxVolDb";

    // --- Audio ---
    AudioSource _music;
    float _currentBaseMusicVol = 1f; // per-track base, separate from master

    public RetroAudio audioBus;

    GameManager _current;
    GameDef _lastFocusedDef;

    int _previewToken = 0;
    const float PREVIEW_DELAY = 0.30f;

    // ---------- Track cache / prewarm ----------
    readonly Dictionary<string, AudioClip> _trackCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _trackMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Coroutine _prewarmCoro;
    int _playSeq = 0;

    [Header("Soundtrack Preload")]
    public bool PreloadTracksOnSelection = true;

    // ---------- Meta Scores ----------
    int _sessionScore = 0;                   // accumulates across many runs until we return to Selection
    int _mainScore = 0;                      // persistent, shown under logo on Selection
    public int MainScore => _mainScore;      // read-only for UI

    public DateTime NowUtc => DateTime.UtcNow.AddSeconds(serverTimeOffsetSeconds);

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        if (!ui) ui = FindObjectOfType<UIManager>(true);
        if (!gameHost) gameHost = new GameObject("GameHost").transform;

        if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

        // Music source
        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.volume = 1f;
        if (mixerMusicGroup) _music.outputAudioMixerGroup = mixerMusicGroup;

        // SFX bus (RetroAudio)
        if (!audioBus)
        {
            var busGO = new GameObject("AudioBus");
            busGO.transform.SetParent(transform, false);
            audioBus = busGO.AddComponent<RetroAudio>();
            var src = busGO.GetComponent<AudioSource>();
            src.playOnAwake = false; src.loop = false; src.spatialBlend = 0f; src.volume = 1f;
            if (mixerSfxGroup) src.outputAudioMixerGroup = mixerSfxGroup;
        }

        // Apply saved volumes
        RetroAudio.GlobalSfxVolume = PlayerPrefs.GetFloat("opt_sfx", 1.0f);
        ApplyVolumesFromPrefs();

        // Load persistent main score
        _mainScore = PlayerPrefs.GetInt("main_score", 0);

        BuildGameList();
        if (ui) ui.Init(this);
        ui?.BindSelection(Games);
        OpenTitle();
    }

    static float LinearToDb(float x)
    {
        x = Mathf.Clamp(x, 0f, 1f);
        if (x <= 0.0001f) return -80f; // effectively mute
        return 20f * Mathf.Log10(x);
    }

    public void SetMusicVolumeLinear(float linear)
    {
        linear = Mathf.Clamp01(linear);
        PlayerPrefs.SetFloat("opt_music", linear);
        if (mixer) mixer.SetFloat(musicVolumeParam, LinearToDb(linear));
        else ApplyMusicVolumeToSource(); // fall back to per-source if no mixer
    }

    public void SetSfxVolumeLinear(float linear)
    {
        linear = Mathf.Clamp01(linear);
        PlayerPrefs.SetFloat("opt_sfx", linear);
        RetroAudio.GlobalSfxVolume = linear;
        if (mixer) mixer.SetFloat(sfxVolumeParam, LinearToDb(linear));
    }

    public void ApplyVolumesFromPrefs()
    {
        SetMusicVolumeLinear(PlayerPrefs.GetFloat("opt_music", 0.8f));
        SetSfxVolumeLinear(PlayerPrefs.GetFloat("opt_sfx", 1.0f));
    }

    void ApplyMusicVolumeToSource()
    {
        if (_music == null) return;
        float master = mixer ? 1f : PlayerPrefs.GetFloat("opt_music", 0.8f);
        _music.volume = Mathf.Clamp01(_currentBaseMusicVol * master);
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

        System.Type beamer       = typeof(BeamerGame);
        System.Type pillarprince = typeof(PillarPrinceGame);

        var implMap = new Dictionary<string, System.Type>
        {
            { "beamer", beamer }, { "pillarprince", pillarprince },
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

            if (!string.IsNullOrEmpty(e.unlock_at_utc))
            {
                if (DateTime.TryParse(e.unlock_at_utc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                {
                    def.unlockAtUtc = when;
                }
            }

            Games.Add(def);
        }
    }

    // ---------- Unlock helpers ----------
    public bool IsUnlocked(GameDef def)
    {
        if (def == null) return true;
        // FIRST N ARE ALWAYS UNLOCKED
        if (def.number > 0 && def.number <= Mathf.Max(0, AlwaysUnlockedFirstN)) return true;
        // Then fall back to time gate
        return !def.unlockAtUtc.HasValue || NowUtc >= def.unlockAtUtc.Value;
    }

    // ---------- Music control ----------
    void PlayMusic(AudioClip clip)
    {
        if (_music == null) return;
        if (clip == null)
        {
            _music.Stop();
            _music.clip = null;
            return;
        }
        if (_music.clip == clip && _music.isPlaying) return;

        _music.clip = clip;
        _music.loop = true;
        _currentBaseMusicVol = 1f; // title/selection base
        ApplyMusicVolumeToSource();
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

        bool perGameOnTitle = PlayerPrefs.GetInt("msk_title", 1) != 0;
        if (perGameOnTitle && _lastFocusedDef != null)
        {
            StartCoroutine(PlayTrackRoutineCached(_lastFocusedDef.title, 1f, loop: true));
        }
        else
        {
            PlayTitleMusic();
        }

        if (ui && ui.btnGameSelection) EventSystem.current?.SetSelectedGameObject(ui.btnGameSelection.gameObject);
    }

    public void OpenSelection()
    {
        StopGame();
        ui?.ShowInGameMenu(false);
        ui?.BindSelection(Games);
        ui?.ShowTitle(false);

        // --- Cash out the session into main score, then animate count-up on Selection
        int from = _mainScore;
        if (_sessionScore > 0)
        {
            _mainScore += _sessionScore;
            _sessionScore = 0;
            PlayerPrefs.SetInt("main_score", _mainScore);
            PlayerPrefs.Save();
        }

        ui?.ShowSelect(true);
        ui?.RefreshBandStats();                // update highs on already-built bands
        ui?.BeginMainScoreCount(from, _mainScore); // animate count-up under the logo

        bool playPerGame = PlayerPrefs.GetInt("msk_sel", 1) != 0;
        if (playPerGame) StopMusic();
        else if (selectionMusic) PlaySelectionMusic();
        else if (titleMusic) PlayTitleMusic();
        else StopMusic();

        if (PreloadTracksOnSelection)
        {
            if (_prewarmCoro != null) StopCoroutine(_prewarmCoro);
            _prewarmCoro = StartCoroutine(PrewarmAllTracksRoutine());
        }
    }

    public void StartGame(GameDef def, GameMode? autoMode = null)
    {
        if (def == null) return;

        bool wantInGameMusic = PlayerPrefs.GetInt("msk_game", 0) != 0;
        if (wantInGameMusic)
        {
            float vol = Mathf.Clamp01(PlayerPrefs.GetFloat("msk_game_vol", 0.35f));
            StartCoroutine(PlayTrackRoutineCached(def.title, vol, loop: true));
        }
        else
        {
            // ensure title/selection/preview music doesn’t bleed into gameplay
            StopMusic();
        }

        if (def.implType == null)
        {
            Debug.LogWarning($"[Gamevault] StartGame blocked: '{def.title}' (id='{def.id}') not implemented yet.");
            return;
        }

        StopGame();

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

        if (PlayerPrefs.GetInt("msk_sel", 1) == 0) return;

        _previewToken++;
        StartCoroutine(PreviewRoutine(_previewToken, def.title));
    }

    // ---------- Track cache helpers ----------
    string FindTrackPath(string title, out AudioType type)
    {
        type = AudioType.MPEG;
        string sa = Application.streamingAssetsPath;
        string dir = Path.Combine(sa, "Tracks");
        try
        {
            string mp3 = Path.Combine(dir, $"{title}.mp3");
            string wav = Path.Combine(dir, $"{title}.wav");
            if (File.Exists(wav)) { type = AudioType.WAV; return wav; }
            if (File.Exists(mp3)) { type = AudioType.MPEG; return mp3; }
        }
        catch { }
        return null;
    }

    IEnumerator LoadTrackIntoCacheRoutine(string title)
    {
        if (string.IsNullOrEmpty(title)) yield break;
        if (_trackCache.ContainsKey(title) || _trackMissing.Contains(title)) yield break;

        AudioType type;
        string path = FindTrackPath(title, out type);
        if (string.IsNullOrEmpty(path))
        {
            _trackMissing.Add(title);
            Debug.Log($"[Gamevault] soundtrack not found for '{title}' in StreamingAssets/Tracks.");
            yield break;
        }

        string url = "file://" + path.Replace('\\', '/');
        using (var req = UnityWebRequestMultimedia.GetAudioClip(url, type))
        {
            yield return req.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogWarning($"[Gamevault] failed to load soundtrack '{path}': {req.error}");
                _trackMissing.Add(title);
                yield break;
            }
            var clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null) { _trackMissing.Add(title); yield break; }
            _trackCache[title] = clip;
        }
    }

    IEnumerator PlayTrackRoutineCached(string title, float volume, bool loop)
    {
        int mySeq = ++_playSeq;

        if (!_trackCache.ContainsKey(title) && !_trackMissing.Contains(title))
            yield return LoadTrackIntoCacheRoutine(title);

        if (mySeq != _playSeq) yield break;

        if (_trackCache.TryGetValue(title, out var clip) && clip)
        {
            _music.loop = loop;
            _music.clip = clip;
            _currentBaseMusicVol = Mathf.Clamp01(volume); // base only; master is via mixer param
            ApplyMusicVolumeToSource();
            _music.Play();
        }
    }

    IEnumerator PrewarmAllTracksRoutine()
    {
        foreach (var g in Games)
        {
            if (_trackCache.ContainsKey(g.title) || _trackMissing.Contains(g.title)) continue;
            yield return LoadTrackIntoCacheRoutine(g.title);
            yield return null;
        }
    }

    IEnumerator PreviewRoutine(int token, string title)
    {
        yield return new WaitForSecondsRealtime(PREVIEW_DELAY);
        if (token != _previewToken) yield break;
        yield return PlayTrackRoutineCached(title, 1f, loop: true);
    }

    // ---------- Chiptune gating ----------
    // If the player wants the modern soundtrack during gameplay, chiptune music from games should not play.
    public bool AllowChiptuneNow()
    {
        bool allowChip = PlayerPrefs.GetInt("chip_ingame", 1) != 0;   // default ON
        bool modernInGame = PlayerPrefs.GetInt("msk_game", 0) != 0;   // modern soundtrack preference
        return allowChip && !modernInGame;
    }

    // ---------- Session / High score API ----------
    public void ReportRun(GameDef def, GameMode mode, int scoreP1, int scoreP2)
    {
        int add = Mathf.Max(0, scoreP1) + Mathf.Max(0, scoreP2);
        _sessionScore += add;

        if (def == null) return;
        string id = def.id ?? "";

        // Update per-game highs
        if (mode == GameMode.Solo)
        {
            string k = $"hi1p_{id}";
            int cur = PlayerPrefs.GetInt(k, 0);
            if (scoreP1 > cur) PlayerPrefs.SetInt(k, scoreP1);
        }
        else
        {
            int sum2 = Mathf.Max(0, scoreP1) + Mathf.Max(0, scoreP2);
            string k = $"hi2p_{id}";
            int cur = PlayerPrefs.GetInt(k, 0);
            if (sum2 > cur) PlayerPrefs.SetInt(k, sum2);
        }
        PlayerPrefs.Save();
    }
}
