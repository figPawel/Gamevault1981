// SteamPlatformServices.cs
// Steamworks.NET-backed implementation. Compiles out on non-Steam builds.
// Self-inits Steam if needed (Editor/dev runs). No Heathen dependency.

using System;
using System.Collections.Generic;
using UnityEngine;

#if STEAMWORKS
using Steamworks;
#endif

public class SteamPlatformServices : IPlatformServices
{
    public string PlatformName => "Steam";
    public event Action OnProfileUpdated;

#if STEAMWORKS
    private Texture2D _avatar;
    private bool _avatarRequested;
    private readonly Dictionary<string, SteamLeaderboard_t> _boards = new();
#endif

    // ---- lifecycle ----
    public void Initialize(Action onReady)
    {
#if STEAMWORKS
        // Detect init; if not, try to Init (use steam_appid.txt + Steam client running).
        try { var _ = SteamUtils.GetAppID(); }
        catch
        {
            try
            {
                if (!SteamAPI.Init())
                    Debug.LogWarning("[SteamPlatform] SteamAPI.Init() returned false. Run via Steam client & ensure steam_appid.txt exists.");
            }
            catch (Exception e)
            {
                Debug.LogError("[SteamPlatform] Steam init failed: " + e.Message);
            }
        }
#endif
        onReady?.Invoke();
    }

    public void Tick()
    {
#if STEAMWORKS
        // Drive callbacks if we were able to init
        try { SteamAPI.RunCallbacks(); } catch { /* ignore */ }

        if (!_avatarRequested)
        {
            _avatarRequested = true;
            TryBuildAvatar();
        }
#endif
    }

    // ---- profile ----
    public string PlayerDisplayName
    {
        get
        {
#if STEAMWORKS
            try { return SteamFriends.GetPersonaName(); } catch { return "Player"; }
#else
            return "Player";
#endif
        }
    }

    public Texture2D PlayerAvatar
    {
        get
        {
#if STEAMWORKS
            return _avatar;
#else
            return null;
#endif
        }
    }

    // ---- achievements ----
    public bool IsAchievementUnlocked(string achId)
    {
#if STEAMWORKS
        try { SteamUserStats.GetAchievement(achId, out bool achieved); return achieved; }
        catch { return false; }
#else
        return false;
#endif
    }

    public void UnlockAchievement(string achId)
    {
#if STEAMWORKS
        if (string.IsNullOrEmpty(achId)) return;
        try
        {
            if (SteamUserStats.GetAchievement(achId, out bool achieved) && achieved) return;
            SteamUserStats.SetAchievement(achId);
            SteamUserStats.StoreStats();
        }
        catch { /* ignore */ }
#endif
    }

    public void IndicateProgress(string statOrAchId, int current, int max)
    {
#if STEAMWORKS
        try
        {
            if (max > 0)
            {
                SteamUserStats.SetStat(statOrAchId, current);
                if (current >= max) UnlockAchievement(statOrAchId);
                SteamUserStats.StoreStats();
            }
        }
        catch { /* ignore */ }
#endif
    }

    // ---- leaderboards ----
    public void EnsureBoard(string boardId, SortMethod sort, DisplayType display, Action<bool> onReady)
    {
#if STEAMWORKS
        if (_boards.ContainsKey(boardId)) { onReady?.Invoke(true); return; }

        // Ensure Steam is usable
        try { var _ = SteamUtils.GetAppID(); }
        catch
        {
            try
            {
                if (!SteamAPI.Init())
                {
                    Debug.LogWarning("[SteamPlatform] EnsureBoard while Steam not ready.");
                    onReady?.Invoke(false);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamPlatform] EnsureBoard init failed: " + e.Message);
                onReady?.Invoke(false);
                return;
            }
        }

        var sortSteam = sort == SortMethod.Ascending
            ? ELeaderboardSortMethod.k_ELeaderboardSortMethodAscending
            : ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending;

        var displaySteam = display == DisplayType.TimeMilliSeconds
            ? ELeaderboardDisplayType.k_ELeaderboardDisplayTypeTimeMilliSeconds
            : ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric;

        var call = SteamUserStats.FindOrCreateLeaderboard(boardId, sortSteam, displaySteam);

        // Per-call handler; no global cb fields required.
        var perCall = CallResult<LeaderboardFindResult_t>.Create((res, fail) =>
        {
            if (fail || res.m_bLeaderboardFound == 0) { onReady?.Invoke(false); return; }
            _boards[boardId] = res.m_hSteamLeaderboard;
            onReady?.Invoke(true);
        });
        perCall.Set(call);
#else
        onReady?.Invoke(true);
#endif
    }

    public void UploadScore(string boardId, int score, bool keepBest, int[] details,
                            Action<bool, int, int> onUploaded)
    {
#if STEAMWORKS
        EnsureBoard(boardId, SortMethod.Descending, DisplayType.Numeric, ok =>
        {
            if (!ok) { onUploaded?.Invoke(false, score, 0); return; }

            var handle = _boards[boardId];
            var method = keepBest
                ? ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest
                : ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodForceUpdate;

            var d = details ?? Array.Empty<int>();
            var call = SteamUserStats.UploadLeaderboardScore(handle, method, score, d, d.Length);

            var perCall = CallResult<LeaderboardScoreUploaded_t>.Create((res, fail) =>
            {
                bool success = !fail && res.m_bSuccess == 1;
                onUploaded?.Invoke(success, res.m_nScore, res.m_nGlobalRankNew);
            });
            perCall.Set(call);
        });
#else
        onUploaded?.Invoke(false, score, 0);
#endif
    }

    public void GetTopN(string boardId, int n, Action<bool, List<LeaderboardEntry>> onDone)
    {
#if STEAMWORKS
        EnsureBoard(boardId, SortMethod.Descending, DisplayType.Numeric, ok =>
        {
            if (!ok) { onDone?.Invoke(false, null); return; }
            var handle = _boards[boardId];
            var call = SteamUserStats.DownloadLeaderboardEntries(
                handle, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobal, 1, n);

            var perCall = CallResult<LeaderboardScoresDownloaded_t>.Create((res, fail) =>
            {
                onDone?.Invoke(!fail, BuildEntries(res));
            });
            perCall.Set(call);
        });
#else
        onDone?.Invoke(true, new List<LeaderboardEntry>());
#endif
    }

    public void GetFriendsTopN(string boardId, int n, Action<bool, List<LeaderboardEntry>> onDone)
    {
#if STEAMWORKS
        EnsureBoard(boardId, SortMethod.Descending, DisplayType.Numeric, ok =>
        {
            if (!ok) { onDone?.Invoke(false, null); return; }
            var handle = _boards[boardId];
            var call = SteamUserStats.DownloadLeaderboardEntries(
                handle, ELeaderboardDataRequest.k_ELeaderboardDataRequestFriends, 0, n);

            var perCall = CallResult<LeaderboardScoresDownloaded_t>.Create((res, fail) =>
            {
                onDone?.Invoke(!fail, BuildEntries(res));
            });
            perCall.Set(call);
        });
#else
        onDone?.Invoke(true, new List<LeaderboardEntry>());
#endif
    }

    public void GetAroundUser(string boardId, int radius, Action<bool, List<LeaderboardEntry>> onDone)
    {
#if STEAMWORKS
        EnsureBoard(boardId, SortMethod.Descending, DisplayType.Numeric, ok =>
        {
            if (!ok) { onDone?.Invoke(false, null); return; }
            var handle = _boards[boardId];
            int start = -Mathf.Abs(radius);
            int end   =  Mathf.Abs(radius);

            var call = SteamUserStats.DownloadLeaderboardEntries(
                handle, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobalAroundUser, start, end);

            var perCall = CallResult<LeaderboardScoresDownloaded_t>.Create((res, fail) =>
            {
                onDone?.Invoke(!fail, BuildEntries(res));
            });
            perCall.Set(call);
        });
#else
        onDone?.Invoke(true, new List<LeaderboardEntry>());
#endif
    }

#if STEAMWORKS
    private static List<LeaderboardEntry> BuildEntries(LeaderboardScoresDownloaded_t res)
    {
        var list = new List<LeaderboardEntry>();
        try
        {
            if (res.m_cEntryCount > 0)
            {
                for (int i = 0; i < res.m_cEntryCount; i++)
                {
                    LeaderboardEntry_t e;
                    int[] det = new int[64];
                    if (SteamUserStats.GetDownloadedLeaderboardEntry(res.m_hSteamLeaderboardEntries, i, out e, det, det.Length))
                    {
                        list.Add(new LeaderboardEntry
                        {
                            globalRank = e.m_nGlobalRank,
                            userName   = SteamFriends.GetFriendPersonaName(e.m_steamIDUser),
                            score      = e.m_nScore,
                            userId     = e.m_steamIDUser.m_SteamID,
                            details    = det
                        });
                    }
                }
            }
        }
        catch { /* ignore */ }
        return list;
    }

    private void TryBuildAvatar()
    {
        try
        {
            var me = SteamUser.GetSteamID();
            int imgId = SteamFriends.GetLargeFriendAvatar(me);
            if (imgId <= 0) return; // not ready yet

            uint w, h;
            if (!SteamUtils.GetImageSize(imgId, out w, out h)) return;
            byte[] raw = new byte[w * h * 4];
            if (!SteamUtils.GetImageRGBA(imgId, raw, raw.Length)) return;

            _avatar = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false);
            _avatar.LoadRawTextureData(raw);
            _avatar.Apply(false, false);
            OnProfileUpdated?.Invoke();
        }
        catch { /* ignore */ }
    }
#endif
}

// Null (non-Steam) implementation so everything compiles elsewhere.
public class NullPlatformServices : IPlatformServices
{
    public string PlatformName => "Null";
    public string PlayerDisplayName => "Player";
    public Texture2D PlayerAvatar => null;
    public event Action OnProfileUpdated;

    public void Initialize(Action onReady) => onReady?.Invoke();
    public void Tick() { }

    public bool IsAchievementUnlocked(string achId) => false;
    public void UnlockAchievement(string achId) { }
    public void IndicateProgress(string id, int current, int max) { }

    public void EnsureBoard(string boardId, SortMethod sort, DisplayType display, Action<bool> onReady) => onReady?.Invoke(true);
    public void UploadScore(string boardId, int score, bool keepBest, int[] details, Action<bool, int, int> onUploaded) => onUploaded?.Invoke(false, score, 0);
    public void GetTopN(string boardId, int n, Action<bool, List<LeaderboardEntry>> onDone) => onDone?.Invoke(true, new List<LeaderboardEntry>());
    public void GetFriendsTopN(string boardId, int n, Action<bool, List<LeaderboardEntry>> onDone) => onDone?.Invoke(true, new List<LeaderboardEntry>());
    public void GetAroundUser(string boardId, int radius, Action<bool, List<LeaderboardEntry>> onDone) => onDone?.Invoke(true, new List<LeaderboardEntry>());
}
