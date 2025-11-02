// SteamLeaderboardCreator.cs — auto-populates from MetaGameManager / GameCatalog
// Self-contained: no SteamManager dependency. Will attempt SteamAPI.Init() if needed.
// Run via: Inspector (custom editor), F9, or Auto Run On Start.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; // new Input System
#endif

#if STEAMWORKS
using Steamworks;
#endif

public class SteamLeaderboardCreator : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("If ON, pull game ids from MetaGameManager.I.Games (your JSON catalog). If OFF, use the list below.")]
    public bool populateFromCatalog = true;

    [Tooltip("Fallback / extra ids. Used when 'populateFromCatalog' is OFF, or merged in if you add custom ids here.")]
    public List<string> gameIds = new List<string>();

    [Header("2P Boards")]
    [Tooltip("Create a '-2p' leaderboard alongside the 1P one.")]
    public bool includeTwoPlayerBoards = true;

    [Tooltip("Only create '-2p' boards for games that actually have a 2P mode in the catalog flags.")]
    public bool requireTwoPlayerFlag = true;

    [Header("Behavior")]
    [Tooltip("Create on Start automatically.")]
    public bool autoRunOnStart = false;

    [Tooltip("Hotkey to trigger creation at runtime.")]
    public KeyCode runHotkey = KeyCode.F9;

    [Header("Steam settings")]
    [Tooltip("Leaderboard scoring sort. Descending (higher is better) is the usual choice.")]
    public bool sortDescending = true;

    [Tooltip("Leaderboard display type. Numeric works for scores; switch to TimeMilliSeconds if you store times.")]
    public bool displayNumeric = true;

    [Header("Init / Log")]
    [Tooltip("If true, this component will call SteamAPI.Init() when needed.")]
    public bool attemptInitIfNeeded = true;

    [Tooltip("Verbose console logging.")]
    public bool verbose = true;

#if STEAMWORKS
    private Queue<string> _pending;
    private CallResult<LeaderboardFindResult_t> _findCb;
    private int _ok, _fail;
    private bool _running;

    // Steam init state we manage locally (so we don't fight other init code)
    private static bool _steamReady = false;
    private static bool _weInitializedSteam = false;

    void Awake()
    {
        _findCb = CallResult<LeaderboardFindResult_t>.Create(OnFindResult);
    }
#endif

    void Start()
    {
#if STEAMWORKS
        if (autoRunOnStart) StartCreateAll();
#endif
    }

    void Update()
    {
#if STEAMWORKS
        if (WasHotkeyPressedThisFrame())
            StartCreateAll();

        // Run callbacks only if we initialized Steam here.
        if (_weInitializedSteam && _steamReady)
        {
            try { SteamAPI.RunCallbacks(); } catch { /* ignore */ }
        }
#endif
    }

    void OnDestroy()
    {
#if STEAMWORKS
        // Only shutdown if we were the one who initialized it.
        if (_weInitializedSteam && _steamReady)
        {
            try { SteamAPI.Shutdown(); } catch { /* ignore */ }
            _steamReady = false;
            _weInitializedSteam = false;
        }
#endif
    }

    /// <summary>Called by the Inspector button and by hotkey/autorun.</summary>
    public void StartCreateAll()
    {
#if STEAMWORKS
        if (_running)
        {
            if (verbose) Debug.Log("[SteamLeaderboards] Already running.");
            return;
        }

        if (!EnsureSteamReady()) return;

        var ids = CollectIds(); // auto-populate here

        if (ids.Count == 0)
        {
            Debug.LogWarning("[SteamLeaderboards] No ids to create. Check 'Populate From Catalog' or add items to 'Game Ids'.");
            return;
        }

        _pending = new Queue<string>(ids);
        _ok = 0; _fail = 0; _running = true;
        if (verbose) Debug.Log($"[SteamLeaderboards] Creating {ids.Count} boards...");
        CreateNext();
#else
        Debug.LogWarning("SteamLeaderboardCreator: STEAMWORKS define not set. Define STEAMWORKS, add steam_appid.txt, and run with Steam client.");
#endif
    }

    /// <summary>Used by the custom inspector to show a live preview.</summary>
    public List<string> GetPreviewIds() => CollectIds();

    // ---------- ID collection ----------
    List<string> CollectIds()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (populateFromCatalog && MetaGameManager.I != null && MetaGameManager.I.Games != null && MetaGameManager.I.Games.Count > 0)
        {
            foreach (var g in MetaGameManager.I.Games)
            {
                if (string.IsNullOrWhiteSpace(g.id)) continue;
                var id = g.id.Trim();
                set.Add(id);

                if (includeTwoPlayerBoards)
                {
                    bool has2p = (g.flags & (GameFlags.Versus2P | GameFlags.Coop2P | GameFlags.Alt2P)) != 0;
                    if (!requireTwoPlayerFlag || has2p)
                        set.Add(id + "-2p");
                }
            }
        }

        // Merge manual overrides
        if (gameIds != null)
        {
            foreach (var gid in gameIds)
            {
                if (string.IsNullOrWhiteSpace(gid)) continue;
                var id = gid.Trim();
                set.Add(id);
                if (includeTwoPlayerBoards && !requireTwoPlayerFlag) set.Add(id + "-2p");
            }
        }

        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

#if STEAMWORKS
    // ------------- Steam wiring (no SteamManager dependency) ----------------
    bool EnsureSteamReady()
    {
        if (_steamReady) return true;

        // Try a harmless Steam call to detect whether someone else already inited.
        try
        {
            // If not initialized, this will throw an exception in Steamworks.NET.
            var idProbe = SteamUtils.GetAppID(); // accessing forces "is inited?"
            _steamReady = true;
            if (verbose) Debug.Log($"[SteamLeaderboards] Steam already initialized (AppID {idProbe}).");
            return true;
        }
        catch
        {
            // Not initialized yet.
        }

        if (!attemptInitIfNeeded)
        {
            Debug.LogError("[SteamLeaderboards] Steam is not initialized and 'Attempt Init If Needed' is OFF. Please initialize SteamAPI elsewhere (e.g., your bootstrap) before running this tool.");
            return false;
        }

        // Attempt to initialize SteamAPI ourselves.
        try
        {
            // You need steam_appid.txt in the project root and the Steam client running.
            _steamReady = SteamAPI.Init();
            _weInitializedSteam = _steamReady;

            if (!_steamReady)
            {
                Debug.LogError("[SteamLeaderboards] SteamAPI.Init() returned false. Make sure the Steam client is running and steam_appid.txt contains your AppID.");
                return false;
            }

            if (verbose) Debug.Log("[SteamLeaderboards] SteamAPI.Init() OK (initialized by creator).");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[SteamLeaderboards] SteamAPI.Init() failed: " + e.Message);
            _steamReady = false;
            _weInitializedSteam = false;
            return false;
        }
    }

    void CreateNext()
    {
        if (_pending == null || _pending.Count == 0)
        {
            _running = false;
            Debug.Log($"[SteamLeaderboards] Done. Created/Found OK: {_ok}, Failed: {_fail}.");
            return;
        }

        string boardId = _pending.Dequeue();

        var sort = sortDescending
            ? ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending
            : ELeaderboardSortMethod.k_ELeaderboardSortMethodAscending;

        var display = displayNumeric
            ? ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric
            : ELeaderboardDisplayType.k_ELeaderboardDisplayTypeTimeMilliSeconds;

        if (verbose) Debug.Log($"[SteamLeaderboards] FindOrCreate: {boardId} ({sort}, {display})");

        var call = SteamUserStats.FindOrCreateLeaderboard(boardId, sort, display);
        _findCb.Set(call);
    }

    void OnFindResult(LeaderboardFindResult_t r, bool ioFailure)
    {
        bool ok = (r.m_bLeaderboardFound != 0) && !ioFailure;
        if (ok) _ok++; else _fail++;

        if (verbose)
        {
            string msg = ok
                ? $"[SteamLeaderboards] OK: {r.m_hSteamLeaderboard.m_SteamLeaderboard}"
                : "[SteamLeaderboards] FAILED to create/find.";
            Debug.Log(msg);
        }

        StartCoroutine(_NextTick());
    }

    IEnumerator _NextTick()
    {
        yield return null;
        yield return new WaitForSeconds(0.05f);
        CreateNext();
    }
#endif

    // ---------- Hotkey (new Input System safe) ----------
    bool WasHotkeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;

        switch (runHotkey)
        {
            // Function keys
            case KeyCode.F1:  return kb.f1Key.wasPressedThisFrame;
            case KeyCode.F2:  return kb.f2Key.wasPressedThisFrame;
            case KeyCode.F3:  return kb.f3Key.wasPressedThisFrame;
            case KeyCode.F4:  return kb.f4Key.wasPressedThisFrame;
            case KeyCode.F5:  return kb.f5Key.wasPressedThisFrame;
            case KeyCode.F6:  return kb.f6Key.wasPressedThisFrame;
            case KeyCode.F7:  return kb.f7Key.wasPressedThisFrame;
            case KeyCode.F8:  return kb.f8Key.wasPressedThisFrame;
            case KeyCode.F9:  return kb.f9Key.wasPressedThisFrame;
            case KeyCode.F10: return kb.f10Key.wasPressedThisFrame;
            case KeyCode.F11: return kb.f11Key.wasPressedThisFrame;
            case KeyCode.F12: return kb.f12Key.wasPressedThisFrame;

            // Digits
            case KeyCode.Alpha0: return kb.digit0Key.wasPressedThisFrame;
            case KeyCode.Alpha1: return kb.digit1Key.wasPressedThisFrame;
            case KeyCode.Alpha2: return kb.digit2Key.wasPressedThisFrame;
            case KeyCode.Alpha3: return kb.digit3Key.wasPressedThisFrame;
            case KeyCode.Alpha4: return kb.digit4Key.wasPressedThisFrame;
            case KeyCode.Alpha5: return kb.digit5Key.wasPressedThisFrame;
            case KeyCode.Alpha6: return kb.digit6Key.wasPressedThisFrame;
            case KeyCode.Alpha7: return kb.digit7Key.wasPressedThisFrame;
            case KeyCode.Alpha8: return kb.digit8Key.wasPressedThisFrame;
            case KeyCode.Alpha9: return kb.digit9Key.wasPressedThisFrame;

            // Common controls
            case KeyCode.Space:       return kb.spaceKey.wasPressedThisFrame;
            case KeyCode.Return:
            case KeyCode.KeypadEnter: return kb.enterKey.wasPressedThisFrame;
            case KeyCode.Escape:      return kb.escapeKey.wasPressedThisFrame;

            // Letters A..Z
            case KeyCode.A: return kb.aKey.wasPressedThisFrame;
            case KeyCode.B: return kb.bKey.wasPressedThisFrame;
            case KeyCode.C: return kb.cKey.wasPressedThisFrame;
            case KeyCode.D: return kb.dKey.wasPressedThisFrame;
            case KeyCode.E: return kb.eKey.wasPressedThisFrame;
            case KeyCode.F: return kb.fKey.wasPressedThisFrame;
            case KeyCode.G: return kb.gKey.wasPressedThisFrame;
            case KeyCode.H: return kb.hKey.wasPressedThisFrame;
            case KeyCode.I: return kb.iKey.wasPressedThisFrame;
            case KeyCode.J: return kb.jKey.wasPressedThisFrame;
            case KeyCode.K: return kb.kKey.wasPressedThisFrame;
            case KeyCode.L: return kb.lKey.wasPressedThisFrame;
            case KeyCode.M: return kb.mKey.wasPressedThisFrame;
            case KeyCode.N: return kb.nKey.wasPressedThisFrame;
            case KeyCode.O: return kb.oKey.wasPressedThisFrame;
            case KeyCode.P: return kb.pKey.wasPressedThisFrame;
            case KeyCode.Q: return kb.qKey.wasPressedThisFrame;
            case KeyCode.R: return kb.rKey.wasPressedThisFrame;
            case KeyCode.S: return kb.sKey.wasPressedThisFrame;
            case KeyCode.T: return kb.tKey.wasPressedThisFrame;
            case KeyCode.U: return kb.uKey.wasPressedThisFrame;
            case KeyCode.V: return kb.vKey.wasPressedThisFrame;
            case KeyCode.W: return kb.wKey.wasPressedThisFrame;
            case KeyCode.X: return kb.xKey.wasPressedThisFrame;
            case KeyCode.Y: return kb.yKey.wasPressedThisFrame;
            case KeyCode.Z: return kb.zKey.wasPressedThisFrame;

            default: return false;
        }
#else
        // Old Input Manager fallback
        return Input.GetKeyDown(runHotkey);
#endif
    }
}
