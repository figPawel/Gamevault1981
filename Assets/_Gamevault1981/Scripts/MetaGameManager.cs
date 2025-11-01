// MetaGameManager.cs  — FULL FILE
// Adds local time/score unlock rules, first-open stamp, "new" tracking, and banner trigger.
// Replaces your current MetaGameManager.cs. :contentReference[oaicite:0]{index=0}
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
    public DateTime? unlockAtUtc; // kept but unused for logic now (we show our own countdown)

    public GameDef(string id, string title, int number, string desc, GameFlags flags, Type implType)
    { this.id = id; this.title = title; this.number = number; this.desc = desc; this.flags = flags; this.implType = implType; }
}

public enum UnlockTimeCurve { Linear, Exponential }

public class MetaGameManager : MonoBehaviour
{
    [ContextMenu("Reset Main Score (testing)")]
    public void ResetMainScoreForTesting()
    {
        _mainScore = 0;
        PlayerPrefs.DeleteKey("main_score");
        PlayerPrefs.Save();
        ui?.BeginMainScoreCount(0, 0);
    }

    public static MetaGameManager I;

    [Header("Scene Hooks")]
    public UIManager ui;
    public Transform gameHost;

    [Header("Catalog")]
    public readonly List<GameDef> Games = new List<GameDef>();

    [Header("Music (fallbacks)")]
    public AudioClip titleMusic;

    [Header("Unlocks (local rules)")]
    [Tooltip("If ON, locked games appear as disabled bands. If OFF, locked bands are hidden.")]
    public bool ShowLockedBands = true;

    [Tooltip("First N games are unlocked immediately for every player.")]
    public int AlwaysUnlockedFirstN = 20;

    [Tooltip("Server time offset if you provide one; unused for logic but kept for completeness.")]
    public double serverTimeOffsetSeconds = 0;

    [Space(6)]
    [Tooltip("Enable time-based unlocks: every Interval a new game unlocks.")]
    public bool enableTimeUnlocks = true;
    [Tooltip("Base interval (minutes) for unlocking the next game.")]
    public double baseIntervalMinutes = 60.0;
    [Tooltip("Linear = every step costs baseInterval. Exponential = cumulative time grows by 'intervalGrowth'.")]
    public UnlockTimeCurve timeCurve = UnlockTimeCurve.Linear;
    [Min(1.0f)] public double intervalGrowth = 1.25;

    [Space(6)]
    [Tooltip("Enable score-based unlocks: reaching thresholds unlocks more games.")]
    public bool enableScoreUnlocks = true;
    [Tooltip("Base score required for unlock progression.")]
    public int baseScorePerUnlock = 5000;
    [Tooltip("If 1 → linear (base*k). If >1 → exponential cumulative (base*(g^k-1)/(g-1)).")]
    [Min(1f)] public float scoreGrowth = 1.0f;

    [Header("Audio Mixer")]
    public AudioMixer mixer;
    public AudioMixerGroup mixerMusicGroup;
    public AudioMixerGroup mixerSfxGroup;
    [Tooltip("Exposed mixer parameter for music volume in dB.")]
    public string musicVolumeParam = "MusicVolDb";
    [Tooltip("Exposed mixer parameter for SFX volume in dB.")]
    public string sfxVolumeParam = "SfxVolDb";

    AudioSource _music;
    float _currentBaseMusicVol = 1f;

    public RetroAudio audioBus;

    GameManager _current;
    GameDef _lastFocusedDef;

    int _previewToken = 0;
    const float PREVIEW_DELAY = 0.30f;

    readonly Dictionary<string, AudioClip> _trackCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _trackMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    Coroutine _prewarmCoro;
    int _playSeq = 0;

    [Header("Soundtrack Preload")]
    public bool PreloadTracksOnSelection = true;

    int _sessionScore = 0;
    int _mainScore = 0;
    public int MainScore => _mainScore;

    public DateTime NowUtc => DateTime.UtcNow.AddSeconds(serverTimeOffsetSeconds);

    // ---------- PlayerPrefs keys ----------
    const string PREF_MAIN_SCORE = "main_score";
    const string PREF_FIRST_OPEN = "first_open_utc";
    const string PREF_UNLOCK_SEEN_COUNT = "unlocked_seen_count"; // extra unlocked beyond AlwaysUnlockedFirstN the user has already been told about
    static string PrefPlayed(string id) => $"played_{id}";

    DateTime FirstOpenUtc
    {
        get
        {
            if (!PlayerPrefs.HasKey(PREF_FIRST_OPEN)) return DateTime.MinValue;
            if (DateTime.TryParse(PlayerPrefs.GetString(PREF_FIRST_OPEN), null, DateTimeStyles.RoundtripKind, out var t)) return t;
            return DateTime.MinValue;
        }
    }
    void EnsureFirstOpenStamp()
    {
        if (!PlayerPrefs.HasKey(PREF_FIRST_OPEN))
        {
            PlayerPrefs.SetString(PREF_FIRST_OPEN, NowUtc.ToString("o"));
            PlayerPrefs.Save();
        }
    }

    void Awake()
    {
        if (I && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        if (!ui) ui = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        if (!gameHost) gameHost = new GameObject("GameHost").transform;

        if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.volume = 1f;
        if (mixerMusicGroup) _music.outputAudioMixerGroup = mixerMusicGroup;

        if (!audioBus)
        {
            var busGO = new GameObject("AudioBus");
            busGO.transform.SetParent(transform, false);
            audioBus = busGO.AddComponent<RetroAudio>();
            var src = busGO.GetComponent<AudioSource>();
            src.playOnAwake = false; src.loop = false; src.spatialBlend = 0f; src.volume = 1f;
            if (mixerSfxGroup) src.outputAudioMixerGroup = mixerSfxGroup;
        }

        RetroAudio.GlobalSfxVolume = PlayerPrefs.GetFloat("opt_sfx", 1.0f);
        ApplyVolumesFromPrefs();

        _mainScore = PlayerPrefs.GetInt(PREF_MAIN_SCORE, 0);

        BuildGameList();
        if (ui) ui.Init(this);

        // stamp first-open moment here (we want time to start as soon as app is first run)
        EnsureFirstOpenStamp();

        ui?.BindSelection(Games);
        OpenTitle();
    }

    static float LinearToDb(float x)
    {
        x = Mathf.Clamp(x, 0f, 1f);
        if (x <= 0.0001f) return -80f;
        return 20f * Mathf.Log10(x);
    }

    public void SetMusicVolumeLinear(float linear)
    {
        linear = Mathf.Clamp01(linear);
        PlayerPrefs.SetFloat("opt_music", linear);
        if (mixer) mixer.SetFloat(musicVolumeParam, LinearToDb(linear));
        else ApplyMusicVolumeToSource();
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
        System.Type puzzleracer  = typeof(PuzzleRacerGame);
        System.Type rrbbyy       = typeof(RRBBYYGame);
        System.Type cannonman    = typeof(CannonManGame);
        System.Type circulaire   = typeof(CirculaireGame);

        var implMap = new Dictionary<string, System.Type>
        {
            { "beamer", beamer },
            { "pillarprince", pillarprince },
            { "puzzleracer", puzzleracer },
            { "rrbbyy", rrbbyy },
            { "cannonman", cannonman },
            { "circulaire", circulaire },
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

            // keep legacy field so existing catalog files don't break; logic uses local rules now
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

    // ---------- Unlock math ----------
    int StepsForTimeElapsed(double minutesElapsed)
    {
        if (!enableTimeUnlocks || baseIntervalMinutes <= 0.0001) return 0;
        if (timeCurve == UnlockTimeCurve.Linear)
            return Mathf.Max(0, (int)Math.Floor(minutesElapsed / baseIntervalMinutes));

        // Exponential (cumulative): find largest k s.t. minutesElapsed >= base * (g^k - 1) / (g - 1)
        double g = Math.Max(1.000001, intervalGrowth);
        double baseM = baseIntervalMinutes;
        int k = 0;
        double needCum = 0.0;
        for (int i = 0; i < Games.Count; i++)
        {
            needCum = baseM * (Math.Pow(g, i + 1) - 1.0) / (g - 1.0);
            if (minutesElapsed + 1e-6 >= needCum) k = i + 1; else break;
        }
        return k;
    }

    int StepsForScore(int score)
    {
        if (!enableScoreUnlocks || baseScorePerUnlock <= 0) return 0;
        if (scoreGrowth <= 1.000001f)
            return Mathf.Max(0, score / Mathf.Max(1, baseScorePerUnlock));

        // Exponential cumulative: base*(g^k - 1)/(g-1) <= score
        double g = Math.Max(1.000001, (double)scoreGrowth);
        double baseS = Math.Max(1, baseScorePerUnlock);
        int k = 0;
        for (int i = 0; i < Games.Count; i++)
        {
            double need = baseS * (Math.Pow(g, i + 1) - 1.0) / (g - 1.0);
            if (score + 1e-6 >= need) k = i + 1; else break;
        }
        return k;
    }

    public int ExtraUnlockedCountNow()
    {
        int byTime  = StepsForTimeElapsed((NowUtc - FirstOpenUtc).TotalMinutes);
        int byScore = StepsForScore(_mainScore);
        return Mathf.Max(byTime, byScore);
    }

    public bool IsUnlocked(GameDef def)
    {
        if (def == null) return true;
        if (def.number > 0 && def.number <= Mathf.Max(0, AlwaysUnlockedFirstN)) return true;
        int wantIndex = Mathf.Max(0, def.number - AlwaysUnlockedFirstN);
        int extra = ExtraUnlockedCountNow();
        return extra >= wantIndex;
    }

    // For UI: time remaining and score shortfall for *this* game’s step
    int StepIndexFor(GameDef def) => Mathf.Max(1, def.number - AlwaysUnlockedFirstN); // 1-based within the progression
    public TimeSpan TimeUntilUnlock(GameDef def)
    {
        if (!enableTimeUnlocks || baseIntervalMinutes <= 0.0001) return TimeSpan.MaxValue;

        double g = Math.Max(1.000001, timeCurve == UnlockTimeCurve.Linear ? 1.0 : intervalGrowth);
        double baseM = baseIntervalMinutes;
        int k = StepIndexFor(def);

        double targetMinutes;
        if (timeCurve == UnlockTimeCurve.Linear) targetMinutes = baseM * k;
        else targetMinutes = baseM * (Math.Pow(g, k) - 1.0) / (g - 1.0);

        double elapsed = (NowUtc - FirstOpenUtc).TotalMinutes;
        double leftMin = Math.Max(0.0, targetMinutes - elapsed);
        return TimeSpan.FromMinutes(leftMin);
    }

    public int ScoreShortfallForUnlock(GameDef def)
    {
        if (!enableScoreUnlocks || baseScorePerUnlock <= 0) return int.MaxValue;

        double g = Math.Max(1.000001, (double)scoreGrowth);
        double baseS = Math.Max(1, baseScorePerUnlock);
        int k = StepIndexFor(def);

        double need;
        if (scoreGrowth <= 1.000001f) need = baseS * k;
        else need = baseS * (Math.Pow(g, k) - 1.0) / (g - 1.0);

        int shortfall = (int)Math.Max(0.0, Math.Ceiling(need - _mainScore));
        return shortfall;
    }

    public bool HasPlayed(GameDef def)
    {
        if (def == null) return false;
        return PlayerPrefs.GetInt(PrefPlayed(def.id ?? def.title ?? $"#{def.number}"), 0) != 0;
    }

    public void MarkPlayed(GameDef def)
    {
        if (def == null) return;
        PlayerPrefs.SetInt(PrefPlayed(def.id ?? def.title ?? $"#{def.number}"), 1);
        PlayerPrefs.Save();
    }

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
        _currentBaseMusicVol = 1f;
        ApplyMusicVolumeToSource();
        _music.Play();
    }

    public void PlayTitleMusic() { PlayMusic(titleMusic); }
    public void StopMusic() { PlayMusic(null); }

    public void OpenTitle()
    {
        StopGame();
        ui?.ShowInGameMenu(false);
        ui?.ShowTitle(true);
        ui?.ShowSelect(false);
        PlayTitleMusic();

        if (ui && ui.btnGameSelection)
            EventSystem.current?.SetSelectedGameObject(ui.btnGameSelection.gameObject);
    }

    public void OpenSelection()
    {
        StopGame();
        ui?.ShowInGameMenu(false);
        ui?.BindSelection(Games);
        ui?.ShowTitle(false);

        // Score payout
        int from = _mainScore;
        bool hadSession = _sessionScore > 0;
        if (hadSession)
        {
            _mainScore += _sessionScore;
            _sessionScore = 0;
            PlayerPrefs.SetInt(PREF_MAIN_SCORE, _mainScore);
            PlayerPrefs.Save();
        }

        ui?.ShowSelect(true);
        FocusSelectionHeader();
        ui?.RefreshBandStats();

        if (hadSession && ui && ui.mainScoreText)
            ui.mainScoreText.text = from.ToString("N0");

        float delay = hadSession ? 0.35f : 0f;
        StartCoroutine(BeginMainScoreCountAfterDelay(from, _mainScore, delay));

        // After payout finishes, check for new unlocks and show banner
        StartCoroutine(ShowNewUnlocksBannerAfterPayout(from, _mainScore, delay));

        bool playPerGame = PlayerPrefs.GetInt("msk_sel", 1) != 0;
        if (!playPerGame)
        {
            if (titleMusic) PlayTitleMusic();
            else            StopMusic();
        }
        if (PreloadTracksOnSelection)
        {
            if (_prewarmCoro != null) StopCoroutine(_prewarmCoro);
            _prewarmCoro = StartCoroutine(PrewarmAllTracksRoutine());
        }
    }

    IEnumerator ShowNewUnlocksBannerAfterPayout(int fromScore, int toScore, float delayBeforeStart)
    {
        // approximate payout duration from UI speed
        float countSpeed = (ui && ui.mainScoreCountSpeed > 0f) ? ui.mainScoreCountSpeed : 600f;
        float payoutSeconds = Mathf.Clamp((toScore - fromScore) / Mathf.Max(1f, countSpeed), 0f, 6f);
        yield return new WaitForSecondsRealtime(delayBeforeStart + payoutSeconds + 0.15f);

        int extraNow = ExtraUnlockedCountNow();
        int prevSeen = PlayerPrefs.GetInt(PREF_UNLOCK_SEEN_COUNT, 0);
        int newly = Mathf.Max(0, extraNow - prevSeen);
        if (newly > 0)
        {
            ui?.ShowNewUnlocksBanner(newly);
            PlayerPrefs.SetInt(PREF_UNLOCK_SEEN_COUNT, extraNow);
            PlayerPrefs.Save();
        }
    }

    public void StartGame(GameDef def, GameMode? autoMode = null)
    {
        if (def == null) return;

        // mark as played to clear "NEW!" badge once visited
        MarkPlayed(def);

        bool wantInGameMusic = PlayerPrefs.GetInt("msk_game", 0) != 0;
        if (wantInGameMusic)
        {
            float vol = Mathf.Clamp01(PlayerPrefs.GetFloat("msk_game_vol", 0.35f));
            StartCoroutine(PlayTrackRoutineCached(def.title, vol, loop: true));
        }
        else
        {
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

    public void PreviewGameTrack(GameDef def)
    {
        if (def == null) return;
        _lastFocusedDef = def;

        if (PlayerPrefs.GetInt("msk_sel", 1) == 0) return;

        _previewToken++;
        StartCoroutine(PreviewRoutine(_previewToken, def.title));
    }

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
            _currentBaseMusicVol = Mathf.Clamp01(volume);
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

    public bool AllowChiptuneNow()
    {
        bool allowChip = PlayerPrefs.GetInt("chip_ingame", 1) != 0;
        bool modernInGame = PlayerPrefs.GetInt("msk_game", 0) != 0;
        return allowChip && !modernInGame;
    }

    public void ReportRun(GameDef def, GameMode mode, int scoreP1, int scoreP2)
    {
        int add = Mathf.Max(0, scoreP1) + Mathf.Max(0, scoreP2);
        _sessionScore += add;

        if (def != null)
            Debug.Log($"[Gamevault] ReportRun: '{def.title}' mode={mode} p1={scoreP1} p2={scoreP2} add={add} session_total={_sessionScore}");
        else
            Debug.Log($"[Gamevault] ReportRun: <null def> mode={mode} p1={scoreP1} p2={scoreP2} add={add} session_total={_sessionScore}");

        if (def == null) return;
        string id = def.id ?? "";

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

    void FocusSelectionHeader()
    {
        if (ui == null) return;

        var es = EventSystem.current;
        GameObject header =
            (ui.btnTopOptions      ? ui.btnTopOptions.gameObject      : null) ??
            (ui.btnTopLeaderboards ? ui.btnTopLeaderboards.gameObject : null) ??
            (ui.btnBackFromSelect  ? ui.btnBackFromSelect.gameObject  : null);

        if (header && es != null) es.SetSelectedGameObject(header);
        if (ui.selectScroll) ui.selectScroll.verticalNormalizedPosition = 1f;
    }

    IEnumerator BeginMainScoreCountAfterDelay(int from, int to, float delay)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        ui?.BeginMainScoreCount(from, to);
    }
}
