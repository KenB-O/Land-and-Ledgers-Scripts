using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Review and remove soundtrack files that are no longer in the curated playlist.</summary>
public sealed class MainMusicPlaylistCleanupWindow : EditorWindow
{
    private MainMusicPlaylist playlist;
    private readonly List<string> unlistedPaths = new List<string>();
    private Vector2 scroll;

    [MenuItem("Tools/Land & Ledgers/Music Playlist Cleanup")]
    private static void Open()
    {
        MainMusicPlaylistCleanupWindow window = GetWindow<MainMusicPlaylistCleanupWindow>("Music Cleanup");
        window.minSize = new Vector2(460f, 300f);
        window.Refresh();
    }

    private void OnEnable()
    {
        if (playlist == null)
            playlist = AssetDatabase.LoadAssetAtPath<MainMusicPlaylist>("Assets/Core/Audio/MainMusicPlaylist.asset");
        Refresh();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Main Music Folder Cleanup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Files shown below exist in the playlist's source folder but are not referenced by the playlist asset. " +
            "Scanning is read-only. Deletion requires the separate confirmation button.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        playlist = (MainMusicPlaylist)EditorGUILayout.ObjectField("Playlist", playlist, typeof(MainMusicPlaylist), false);
        if (EditorGUI.EndChangeCheck()) Refresh();

        if (playlist == null)
        {
            EditorGUILayout.HelpBox("Assign the MainMusicPlaylist asset to scan.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("Source folder", playlist.SourceFolder);
        int trackCount = playlist.Tracks != null ? playlist.Tracks.Count : 0;
        EditorGUILayout.LabelField("Playlist entries", trackCount.ToString());
        EditorGUILayout.LabelField("Unlisted audio files", unlistedPaths.Count.ToString());

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Scan")) Refresh();
            using (new EditorGUI.DisabledScope(trackCount == 0 || unlistedPaths.Count == 0))
            {
                if (GUILayout.Button($"Delete {unlistedPaths.Count} Unlisted File(s)")) DeleteUnlistedFiles();
            }
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (unlistedPaths.Count == 0)
        {
            EditorGUILayout.LabelField("No unlisted audio files found.");
        }
        else
        {
            foreach (string path in unlistedPaths)
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.Width(210f));
                    EditorGUILayout.LabelField(path);
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void Refresh()
    {
        unlistedPaths.Clear();
        if (playlist == null || string.IsNullOrWhiteSpace(playlist.SourceFolder)
            || !AssetDatabase.IsValidFolder(playlist.SourceFolder))
        {
            Repaint();
            return;
        }

        var listed = new HashSet<AudioClip>();
        if (playlist.Tracks != null)
            foreach (AudioClip clip in playlist.Tracks)
                if (clip != null) listed.Add(clip);

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { playlist.SourceFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null && !listed.Contains(clip)) unlistedPaths.Add(path);
        }

        unlistedPaths.Sort(System.StringComparer.OrdinalIgnoreCase);
        Repaint();
    }

    private void DeleteUnlistedFiles()
    {
        if (playlist == null || playlist.Tracks == null || playlist.Tracks.Count == 0)
        {
            EditorUtility.DisplayDialog("Music cleanup unavailable",
                "The playlist is empty. Add or restore tracks before deleting unlisted files.", "OK");
            return;
        }

        Refresh();
        if (unlistedPaths.Count == 0) return;

        string message = $"Permanently delete {unlistedPaths.Count} audio asset(s) from '{playlist.SourceFolder}'?\n\n" +
            "Only files still absent from the selected playlist will be deleted. This also removes their Unity metadata files.";
        if (!EditorUtility.DisplayDialog("Delete unlisted music files?", message, "Delete Files", "Cancel")) return;

        int deleted = 0;
        foreach (string path in new List<string>(unlistedPaths))
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null && !playlist.Contains(clip))
            {
                if (AssetDatabase.DeleteAsset(path)) deleted++;
            }
        }

        AssetDatabase.Refresh();
        Refresh();
        Debug.Log($"[MainMusicPlaylistCleanup] Deleted {deleted} audio file(s) that were not in '{playlist.name}'.");
    }
}
