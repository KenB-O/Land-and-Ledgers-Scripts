using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Developer-curated source of truth for the soundtrack. Removing a clip from
/// this asset changes future playback only; it never deletes the audio file.
/// </summary>
[CreateAssetMenu(fileName = "MainMusicPlaylist", menuName = "Land & Ledgers/Audio/Main Music Playlist")]
public sealed class MainMusicPlaylist : ScriptableObject
{
    [SerializeField] private string sourceFolder = "Assets/Core/Audio/MainMusic";
    [SerializeField] private List<AudioClip> tracks = new List<AudioClip>();

    public string SourceFolder => sourceFolder;
    public IReadOnlyList<AudioClip> Tracks => tracks;

    public bool Contains(AudioClip clip)
    {
        if (clip == null || tracks == null) return false;
        return tracks.Contains(clip);
    }

    /// <summary>Removes every occurrence of this exact clip reference from the curated order.</summary>
    public int Remove(AudioClip clip)
    {
        if (clip == null || tracks == null) return 0;
        return tracks.RemoveAll(track => track == clip);
    }
}
