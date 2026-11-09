using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class LLMainMusicPlayerTests
{
    private GameObject playerObject;
    private MainMusicPlaylist playlist;
    private AudioClip removedClip;
    private AudioClip retainedClip;

    [SetUp]
    public void SetUp()
    {
        playerObject = new GameObject("Music Player Test");
        LLMainMusicPlayer player = playerObject.AddComponent<LLMainMusicPlayer>();
        playlist = ScriptableObject.CreateInstance<MainMusicPlaylist>();
        removedClip = AudioClip.Create("RejectedTestTrack", 44100, 1, 44100, false);
        retainedClip = AudioClip.Create("RetainedTestTrack", 44100, 1, 44100, false);

        playlist.TracksForTesting().Add(removedClip);
        playlist.TracksForTesting().Add(retainedClip);
        SetField(player, "playlistAsset", playlist);
        SetField(player, "musicClips", new[] { removedClip, retainedClip });
        SetField(player, "currentClip", removedClip);
        Invoke(player, "RefreshPlaylistRuntime");
    }

    [TearDown]
    public void TearDown()
    {
        if (playerObject != null) Object.DestroyImmediate(playerObject);
        if (playlist != null) Object.DestroyImmediate(playlist);
        if (removedClip != null) Object.DestroyImmediate(removedClip);
        if (retainedClip != null) Object.DestroyImmediate(retainedClip);
    }

    [Test]
    public void RemovalEditsPlaylistAssetButKeepsAudioClipAndOtherTracks()
    {
        LLMainMusicPlayer player = playerObject.GetComponent<LLMainMusicPlayer>();

        Assert.IsTrue(player.RemoveCurrentTrackFromOrder());
        Invoke(player, "RefreshPlaylistRuntime");

        Assert.IsFalse(playlist.Contains(removedClip));
        Assert.IsTrue(playlist.Contains(retainedClip));
        Assert.IsNotNull(removedClip, "Curation must not delete the source audio file.");
        Assert.AreEqual(1, player.TrackCount);
    }

    [Test]
    public void RemovalWithoutCurrentTrackLeavesPlaylistUnchanged()
    {
        LLMainMusicPlayer player = playerObject.GetComponent<LLMainMusicPlayer>();
        SetField<AudioClip>(player, "currentClip", null);

        Assert.IsFalse(player.RemoveCurrentTrackFromOrder());
        Assert.AreEqual(2, playlist.Tracks.Count);
    }

    [Test]
    public void RemovalWithoutPlaylistAssetDoesNotSilentlyEditLegacyArray()
    {
        LLMainMusicPlayer player = playerObject.GetComponent<LLMainMusicPlayer>();
        SetField<MainMusicPlaylist>(player, "playlistAsset", null);

        Assert.IsFalse(player.RemoveCurrentTrackFromOrder());
        Assert.AreEqual(2, GetField<AudioClip[]>(player, "musicClips").Length);
    }

    private static void Invoke(object target, string methodName)
    {
        target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        object value = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
        return (T)value;
    }

    private static void SetField<T>(object target, string fieldName, T value)
    {
        target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }
}

internal static class MainMusicPlaylistTestExtensions
{
    public static System.Collections.Generic.List<AudioClip> TracksForTesting(this MainMusicPlaylist playlist)
    {
        return (System.Collections.Generic.List<AudioClip>)typeof(MainMusicPlaylist)
            .GetField("tracks", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(playlist);
    }
}
