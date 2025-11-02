// SteamLeaderboardCreatorEditor.cs
// Adds a "Create Now (Steam)" button and a live preview of collected IDs.

using UnityEditor;
using UnityEngine;
using System.Linq;

[CustomEditor(typeof(SteamLeaderboardCreator))]
public class SteamLeaderboardCreatorEditor : Editor
{
    SerializedProperty populateFromCatalog;
    SerializedProperty gameIds;
    SerializedProperty includeTwoPlayerBoards;
    SerializedProperty requireTwoPlayerFlag;
    SerializedProperty autoRunOnStart;
    SerializedProperty runHotkey;
    SerializedProperty sortDescending;
    SerializedProperty displayNumeric;
    SerializedProperty verbose;

    void OnEnable()
    {
        populateFromCatalog    = serializedObject.FindProperty("populateFromCatalog");
        gameIds                = serializedObject.FindProperty("gameIds");
        includeTwoPlayerBoards = serializedObject.FindProperty("includeTwoPlayerBoards");
        requireTwoPlayerFlag   = serializedObject.FindProperty("requireTwoPlayerFlag");
        autoRunOnStart         = serializedObject.FindProperty("autoRunOnStart");
        runHotkey              = serializedObject.FindProperty("runHotkey");
        sortDescending         = serializedObject.FindProperty("sortDescending");
        displayNumeric         = serializedObject.FindProperty("displayNumeric");
        verbose                = serializedObject.FindProperty("verbose");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(populateFromCatalog, new GUIContent("Populate From Catalog"));
        if (!populateFromCatalog.boolValue)
        {
            EditorGUILayout.PropertyField(gameIds, new GUIContent("Game Ids"), true);
        }
        else
        {
            EditorGUILayout.HelpBox("IDs will be read from MetaGameManager at runtime. Add items to 'Game Ids' only for manual overrides.", MessageType.Info);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("2P Boards", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(includeTwoPlayerBoards, new GUIContent("Include Two Player Boards"));
        EditorGUILayout.PropertyField(requireTwoPlayerFlag,   new GUIContent("Require Two Player Flag"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(autoRunOnStart, new GUIContent("Auto Run On Start"));
        EditorGUILayout.PropertyField(runHotkey,      new GUIContent("Run Hotkey"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Steam settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(sortDescending, new GUIContent("Sort Descending"));
        EditorGUILayout.PropertyField(displayNumeric, new GUIContent("Display Numeric"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(verbose, new GUIContent("Verbose"));

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        var tool = (SteamLeaderboardCreator)target;

        // Preview (in Play Mode we can show the live collected IDs)
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            var ids = Application.isPlaying ? tool.GetPreviewIds() : null;
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField($"Preview (Play Mode): {ids.Count} ids", EditorStyles.boldLabel);
                if (ids.Count == 0)
                    EditorGUILayout.HelpBox("No ids collected yet. Ensure MetaGameManager loaded the catalog.", MessageType.Warning);
                else
                {
                    foreach (var id in ids.OrderBy(s => s))
                        EditorGUILayout.LabelField("• " + id);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Enter Play Mode to preview collected IDs from the catalog.", MessageType.Info);
            }
        }

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Create Now (Steam)", GUILayout.Height(32)))
            {
                ((SteamLeaderboardCreator)target).StartCreateAll();
            }
        }
    }
}
