
using System;
using System.Collections.Generic;
using UnityEngine;





public enum SortMethod { Ascending, Descending }
public enum DisplayType { Numeric, TimeMilliSeconds }
public enum LeaderboardView { GlobalTop, FriendsTop, AroundPlayer }



[Serializable]
public struct LeaderboardEntry
{
    public int globalRank;
    public string userName;
    public int score;
    public ulong userId;        // platform user id when available (SteamID64)
    public int[] details;       // optional extra data
}

public interface IPlatformServices
{
    string PlatformName { get; }
    string PlayerDisplayName { get; }
    Texture2D PlayerAvatar { get; }
    event Action OnProfileUpdated;

    // lifecycle
    void Initialize(Action onReady);
    void Tick(); // called from Update (for avatar polling etc.)

    // Achievements
    bool IsAchievementUnlocked(string achId);
    void UnlockAchievement(string achId);
    void IndicateProgress(string statOrAchId, int current, int max); // generic helper (ignored on Steam if not mapped)

    // Leaderboards
    void EnsureBoard(string boardId, SortMethod sort, DisplayType display, Action<bool> onReady);
    void UploadScore(string boardId, int score, bool keepBest, int[] details, Action<bool, int, int> onUploaded);
    void GetTopN(string boardId, int n, Action<bool, List<LeaderboardEntry>> onDone);
    void GetFriendsTopN(string boardId, int n, Action<bool, List<LeaderboardEntry>> onDone);
    void GetAroundUser(string boardId, int radius, Action<bool, List<LeaderboardEntry>> onDone);
}

public class PlayerDataManager : MonoBehaviour
{
    public static PlayerDataManager I { get; private set; }

    [Header("Settings")]
    [Tooltip("Default sort for all score leaderboards.")]
    public SortMethod defaultSort = SortMethod.Descending;
    [Tooltip("Default display for all score leaderboards.")]
    public DisplayType defaultDisplay = DisplayType.Numeric;
    [Tooltip("Prewarm top rows count for summaries.")]
    public int summaryTopCount = 10;
    [Tooltip("Cache expiry in seconds for summaries/full boards.")]
    public float cacheSeconds = 20f;

    [Header("Runtime State")]
    public bool leaderboardsEnabled = true;

    public string PlayerName => platform?.PlayerDisplayName ?? "Player";
    public Texture2D PlayerAvatar => platform?.PlayerAvatar;

    public event Action OnProfileUpdated;
    public event Action<bool> OnLeaderboardToggleChanged;

    // ==== Achievements: IDs (keep centralized) ====
const string ACH_FIRST_RUN       = "ACH_FIRST_RUN";
const string ACH_PLAYED_2P       = "ACH_PLAYED_2P";
const string ACH_PLAYED_ALL      = "ACH_PLAYED_ALL";
const string ACH_UNLOCK_FIRST    = "ACH_UNLOCK_FIRST";
const string ACH_UNLOCK_10       = "ACH_UNLOCK_10";
const string ACH_UNLOCK_25       = "ACH_UNLOCK_25";
const string ACH_UNLOCK_ALL      = "ACH_UNLOCK_ALL";
const string ACH_SCORE_100       = "ACH_SCORE_100";
const string ACH_SCORE_1000      = "ACH_SCORE_1000";
const string ACH_SCORE_10000     = "ACH_SCORE_10000";
const string ACH_SCORE_100000    = "ACH_SCORE_100000";
const string ACH_SCORE_1000000   = "ACH_SCORE_1000000";
const string ACH_SCORE_10000000  = "ACH_SCORE_10000000";
const string ACH_SCORE_100000000 = "ACH_SCORE_100000000";

// Persist unique-played set
const string PP_PLAYED_SET = "gv_played_set";
const string PP_FIRST_RUN  = "gv_first_run_done";

    IPlatformServices platform;

    // cache
    class CacheItem<T> { public T data; public float time; }
    Dictionary<string, CacheItem<List<LeaderboardEntry>>> topCache = new();
    Dictionary<string, CacheItem<List<LeaderboardEntry>>> friendsCache = new();
    Dictionary<string, CacheItem<List<LeaderboardEntry>>> aroundCache = new();
    Dictionary<string, CacheItem<int>> bestScoreCache = new();

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

#if STEAMWORKS
        platform = new SteamPlatformServices();
#else
        platform = new NullPlatformServices();
#endif
        platform.OnProfileUpdated += () =>
        {
            OnProfileUpdated?.Invoke();
        };
        platform.Initialize(() =>
        {
            // warm anything if you want
        });
    }

    void Update()
    {
        platform?.Tick();
    }

    // === Public API ==========================================================

    public void ToggleLeaderboards()
    {
        leaderboardsEnabled = !leaderboardsEnabled;
        OnLeaderboardToggleChanged?.Invoke(leaderboardsEnabled);
    }

public string BoardId(string gameId, bool twoPlayer)
    => twoPlayer ? $"{gameId}-2p" : gameId;

    // -- Summaries (for the extra band) --
    public void GetTopSummary(string gameId, bool twoPlayer, Action<(bool ok, int bestScore)> onDone)
    {
        if (!leaderboardsEnabled) { onDone?.Invoke((false, 0)); return; }

        string id = BoardId(gameId, twoPlayer);
        if (TryGetCached(bestScoreCache, id, out int cached))
        {
            onDone?.Invoke((true, cached));
            return;
        }

        EnsureBoardReady(id, () =>
        {
            platform.GetTopN(id, 1, (ok, list) =>
            {
                int best = (ok && list.Count > 0) ? list[0].score : 0;
                SetCache(bestScoreCache, id, best);
                onDone?.Invoke((ok, best));
            });
        });
    }

    // -- Full lists for the expanded panel --
    public void GetFullBoard(string gameId, bool twoPlayer, LeaderboardView view, int size, Action<List<LeaderboardEntry>> onDone)
    {
        if (!leaderboardsEnabled) { onDone?.Invoke(new List<LeaderboardEntry>()); return; }

        string id = BoardId(gameId, twoPlayer);

        // cache key per view
        string ck = $"{id}:{view}:{size}";
        if (view == LeaderboardView.GlobalTop && TryGetCached(topCache, ck, out var t)) { onDone?.Invoke(t); return; }
        if (view == LeaderboardView.FriendsTop && TryGetCached(friendsCache, ck, out var f)) { onDone?.Invoke(f); return; }
        if (view == LeaderboardView.AroundPlayer && TryGetCached(aroundCache, ck, out var a)) { onDone?.Invoke(a); return; }

        EnsureBoardReady(id, () =>
        {
            switch (view)
            {
                case LeaderboardView.GlobalTop:
                    platform.GetTopN(id, size, (ok, list) =>
                    {
                        if (ok) SetCache(topCache, ck, list);
                        onDone?.Invoke(list ?? new List<LeaderboardEntry>());
                    });
                    break;
                case LeaderboardView.FriendsTop:
                    platform.GetFriendsTopN(id, size, (ok, list) =>
                    {
                        if (ok) SetCache(friendsCache, ck, list);
                        onDone?.Invoke(list ?? new List<LeaderboardEntry>());
                    });
                    break;
                case LeaderboardView.AroundPlayer:
                    int radius = Mathf.Max(1, size / 2);
                    platform.GetAroundUser(id, radius, (ok, list) =>
                    {
                        if (ok) SetCache(aroundCache, ck, list);
                        onDone?.Invoke(list ?? new List<LeaderboardEntry>());
                    });
                    break;
            }
        });
    }

    // ---- Score upload (keeps your existing board-id scheme) ----
   public void ReportScore(string gameId, int score, bool twoPlayer, int[] details = null)
{
    if (string.IsNullOrEmpty(gameId) || platform == null) return;

    // Unlock global thresholds
    TryUnlockScoreThresholds(score);

    // Upload to the correct board (1P or -2p)
    string boardId = twoPlayer ? $"{gameId}-2p" : gameId;

    // Ensure board exists then upload
    platform.EnsureBoard(boardId, SortMethod.Descending, DisplayType.Numeric, ok =>
    {
        if (!ok) return;
        platform.UploadScore(boardId, score, keepBest: true, details: details, onUploaded: null);
    });
}



    public void MarkFirstRun() => platform.UnlockAchievement("ACH_FIRST_RUN");
    public void ReportPlayedTwoPlayer() => platform.UnlockAchievement("ACH_PLAYED_2P");
    public void ReportUnlockedGames(int unlockedCount, int totalGames)
{
    if (unlockedCount >= 1)  UnlockAchievement(ACH_UNLOCK_FIRST);
    if (unlockedCount >= 10) UnlockAchievement(ACH_UNLOCK_10);
    if (unlockedCount >= 25) UnlockAchievement(ACH_UNLOCK_25);
    if (totalGames > 0 && unlockedCount >= totalGames) UnlockAchievement(ACH_UNLOCK_ALL);
}

    // === helpers =============================================================

    void EnsureBoardReady(string boardId, Action onReady)
    {
        platform.EnsureBoard(boardId, defaultSort, defaultDisplay, ok =>
        {
            if (!ok) { onReady?.Invoke(); return; }
            onReady?.Invoke();
        });
    }

    void BustCaches(string boardIdPrefix)
    {
        // nuke anything matching this board
        Bust(topCache, boardIdPrefix);
        Bust(friendsCache, boardIdPrefix);
        Bust(aroundCache, boardIdPrefix);
        Bust(bestScoreCache, boardIdPrefix);
        void Bust<T>(Dictionary<string, CacheItem<T>> dict, string prefix)
        {
            var keys = new List<string>(dict.Keys);
            foreach (var k in keys) if (k.StartsWith(boardIdPrefix)) dict.Remove(k);
        }
    }

    bool TryGetCached(Dictionary<string, CacheItem<List<LeaderboardEntry>>> dict, string key, out List<LeaderboardEntry> list)
    {
        list = null;
        if (dict.TryGetValue(key, out var c) && Time.realtimeSinceStartup - c.time <= cacheSeconds)
        { list = c.data; return true; }
        return false;
    }
    bool TryGetCached(Dictionary<string, CacheItem<int>> dict, string key, out int val)
    {
        val = 0;
        if (dict.TryGetValue(key, out var c) && Time.realtimeSinceStartup - c.time <= cacheSeconds)
        { val = c.data; return true; }
        return false;
    }
    void SetCache(Dictionary<string, CacheItem<List<LeaderboardEntry>>> dict, string key, List<LeaderboardEntry> list)
    {
        dict[key] = new CacheItem<List<LeaderboardEntry>> { data = list, time = Time.realtimeSinceStartup };
    }
    void SetCache(Dictionary<string, CacheItem<int>> dict, string key, int val)
    {
        dict[key] = new CacheItem<int> { data = val, time = Time.realtimeSinceStartup };
    }

    // === Achievement threshold helpers ======================================

    static readonly int[] GLOBAL_SCORE_STEPS = new[] { 100, 1000, 10000, 100000, 1000000, 10000000, 100000000 };
 void TryUnlockScoreThresholds(int score)
{
    if (score >= 100)         UnlockAchievement(ACH_SCORE_100);
    if (score >= 1000)        UnlockAchievement(ACH_SCORE_1000);
    if (score >= 10000)       UnlockAchievement(ACH_SCORE_10000);
    if (score >= 100000)      UnlockAchievement(ACH_SCORE_100000);
    if (score >= 1000000)     UnlockAchievement(ACH_SCORE_1000000);
    if (score >= 10000000)    UnlockAchievement(ACH_SCORE_10000000);
    if (score >= 100000000)   UnlockAchievement(ACH_SCORE_100000000);
}
    void TryUnlockPerGameScoreThresholds(string gameId, int score)
    {
        foreach (var t in new[] { 100, 1000, 10000, 100000 })
        {
            if (score >= t) platform.UnlockAchievement($"ACH_{gameId.ToUpperInvariant()}_SCORE_{t}");
        }
    }

    // ---- Played-games tracking (unique) ----


public void ReportPlayedGame(string gameId, int totalGames)
{
    if (string.IsNullOrEmpty(gameId)) return;

    string raw = PlayerPrefs.GetString(PP_PLAYED_SET, "");
    var set = new System.Collections.Generic.HashSet<string>(
        string.IsNullOrEmpty(raw) ? System.Array.Empty<string>() : raw.Split('|'),
        System.StringComparer.OrdinalIgnoreCase);

    if (!set.Contains(gameId))
    {
        set.Add(gameId);
        PlayerPrefs.SetString(PP_PLAYED_SET, string.Join("|", set));
        PlayerPrefs.Save();
    }

    if (totalGames > 0 && set.Count >= totalGames)
        UnlockAchievement(ACH_PLAYED_ALL);
}

// expose UnlockAchievement so the helper can call it cleanly
void UnlockAchievement(string id) => platform?.UnlockAchievement(id);

}
