// CloudSave.cs — single JSON save + verbose logs + editor tools (Input System only)
using System;
using System.IO;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[Serializable]
class SaveData
{
    public int    main_score;
    public string first_open_utc; // ISO 8601 or empty
    public string updated_utc;    // bookkeeping
}

public enum CloudPullAction
{
    NoCloudFile_CreatedFromLocal,
    PulledHigherFromCloud,
    KeptLocalAndRewroteFile,
    NoChange
}

public static class CloudSave
{
    static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

    // PlayerPrefs keys used by MetaGameManager
    const string PP_SCORE      = "main_score";
    const string PP_FIRST_OPEN = "first_open_utc";
    const string PP_BOOT       = "prefs_bootstrap_done";

    // ---------- BOOT MERGE ----------
    public static CloudPullAction PullPreferHigher()
    {
        int localScore = PlayerPrefs.GetInt(PP_SCORE, 0);
        string localFirst = PlayerPrefs.GetString(PP_FIRST_OPEN, "");

        bool blank =
            !PlayerPrefs.HasKey(PP_SCORE) ||
            PlayerPrefs.GetInt(PP_SCORE, 0) == 0 ||
            PlayerPrefs.GetInt(PP_BOOT, 0) == 0;

        if (!File.Exists(FilePath))
        {
            // Seed save.json from local prefs (even if zero) so Auto-Cloud can track it.
            SaveAll(localScore, localFirst, logReason: "seeded (no existing save.json)");
            StampBoot();
            Debug.Log($"[GV Cloud] No save.json → created from local score={localScore}, first_open='{localFirst}'.");
            return CloudPullAction.NoCloudFile_CreatedFromLocal;
        }

        try
        {
            var sd = Read();
            int   cloudScore = Mathf.Max(0, sd.main_score);
            string cloudFirst = sd.first_open_utc ?? "";

            // Merge: score = max; first_open_utc = prefer earliest non-empty
            int chosenScore = blank ? cloudScore : Math.Max(localScore, cloudScore);
            string chosenFirst =
                string.IsNullOrEmpty(localFirst) ? cloudFirst :
                string.IsNullOrEmpty(cloudFirst) ? localFirst :
                (Parse(localFirst) <= Parse(cloudFirst) ? localFirst : cloudFirst);

            bool wrotePrefs = false;
            if (chosenScore != localScore)
            {
                PlayerPrefs.SetInt(PP_SCORE, chosenScore);
                wrotePrefs = true;
            }
            if (string.IsNullOrEmpty(localFirst) && !string.IsNullOrEmpty(chosenFirst))
            {
                PlayerPrefs.SetString(PP_FIRST_OPEN, chosenFirst);
                wrotePrefs = true;
            }
            if (wrotePrefs) { StampBoot(); PlayerPrefs.Save(); }

            // Ensure file mirrors the (possibly higher) local values
            int nowScore = PlayerPrefs.GetInt(PP_SCORE, 0);
            string nowFirst = PlayerPrefs.GetString(PP_FIRST_OPEN, "");
            SaveAll(nowScore, nowFirst, logReason: "sync after pull/merge");

            if (chosenScore > localScore)
            {
                Debug.Log($"[GV Cloud] Pulled higher score from save.json: cloud={cloudScore} > local={localScore} → now {chosenScore}.");
                return CloudPullAction.PulledHigherFromCloud;
            }
            if (chosenScore < localScore || blank)
            {
                Debug.Log($"[GV Cloud] Kept local (score={localScore}, cloud={cloudScore}) → rewrote save.json.");
                return CloudPullAction.KeptLocalAndRewroteFile;
            }

            Debug.Log($"[GV Cloud] In sync (score={nowScore}, first_open='{nowFirst}').");
            return CloudPullAction.NoChange;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GV Cloud] Pull failed: {e.Message}");
            StampBoot();
            PlayerPrefs.Save();
            return CloudPullAction.NoChange;
        }
    }

    // ---------- SAVE HELPERS ----------
    public static void SaveAllFromPrefs(string reason = "manual")
        => SaveAll(PlayerPrefs.GetInt(PP_SCORE, 0), PlayerPrefs.GetString(PP_FIRST_OPEN, ""), reason);

    public static void SaveAll(int score, string firstOpenUtc, string logReason = "auto")
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var payload = new SaveData
            {
                main_score = Mathf.Max(0, score),
                first_open_utc = firstOpenUtc ?? "",
                updated_utc = DateTime.UtcNow.ToString("o")
            };
            string json = JsonUtility.ToJson(payload);

            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);

            Debug.Log($"[GV Cloud] Wrote save.json ({logReason}) → score={payload.main_score}, first_open='{payload.first_open_utc}'\nPath: {FilePath}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GV Cloud] Save failed: {e.Message}");
        }
    }

    // ---------- RESET (player-facing) ----------
    public static void ResetAllNow()
    {
        // Hard reset score and first-open so unlocks are recalculated as a brand new profile.
        PlayerPrefs.SetInt(PP_SCORE, 0);
        PlayerPrefs.SetString(PP_FIRST_OPEN, DateTime.UtcNow.ToString("o")); // "new install" timing
        StampBoot();
        PlayerPrefs.Save();

        SaveAllFromPrefs("reset");
        // NOTE: With Auto-Cloud, Steam will sync this in the background or on exit. There is no official “force now”
        // for Auto-Cloud without switching to the RemoteStorage API for saves.
        Debug.Log("[GV Cloud] ResetAllNow: local prefs zeroed and save.json written.");
    }

    public static void DeleteLocalOnly()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            PlayerPrefs.DeleteKey(PP_SCORE);
            PlayerPrefs.DeleteKey(PP_FIRST_OPEN);
            PlayerPrefs.DeleteKey(PP_BOOT);
            PlayerPrefs.Save();
            Debug.Log("[GV Cloud] Deleted local save.json and related PlayerPrefs.");
        }
        catch (Exception e) { Debug.LogWarning($"[GV Cloud] DeleteLocalOnly failed: {e.Message}"); }
    }

    // ---------- DEV: Input System hotkey (Ctrl+Shift+D) ----------
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && ENABLE_INPUT_SYSTEM
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallDevHotkey()
    {
        var go = new GameObject("GV_CloudHotkey_DEV");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<_GVCloudHotkeyDev>();
    }
    class _GVCloudHotkeyDev : MonoBehaviour
    {
        void Update()
        {
            var k = Keyboard.current;
            if (k != null &&
                k.leftCtrlKey.isPressed &&
                k.leftShiftKey.isPressed &&
                k.dKey.wasPressedThisFrame)
            {
                ResetAllNow(); // immediate local reset + save.json write
            }
        }
    }
#endif

    // ---------- Editor menu ----------
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Gamevault/Cloud/Log State")]
    static void Menu_LogState()
    {
        int score = PlayerPrefs.GetInt(PP_SCORE, -1);
        string first = PlayerPrefs.GetString(PP_FIRST_OPEN, "(unset)");
        string exists = File.Exists(FilePath) ? "exists" : "missing";
        string json = exists == "exists" ? File.ReadAllText(FilePath) : "(no file)";
        Debug.Log($"[GV Cloud] State\n- PlayerPrefs score={score}\n- first_open='{first}'\n- save.json {exists}\n- content: {json}");
    }

    [UnityEditor.MenuItem("Gamevault/Cloud/Reset All Now (score=0, new first_open)")]
    static void Menu_ResetAll() => ResetAllNow();

    [UnityEditor.MenuItem("Gamevault/Cloud/Delete Local Only")]
    static void Menu_DeleteLocal() => DeleteLocalOnly();
#endif

    // ---------- internals ----------
    static SaveData Read()
    {
        var json = File.ReadAllText(FilePath);
        var d = JsonUtility.FromJson<SaveData>(json);
        return d ?? new SaveData();
    }

    static DateTime Parse(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return DateTime.MaxValue;
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) return t;
        return DateTime.MaxValue;
    }

    static void StampBoot()
    {
        PlayerPrefs.SetInt(PP_BOOT, 1);
    }
}
