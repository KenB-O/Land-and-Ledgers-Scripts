using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class LLMainMusicPlayerTests
{
    private const string RejectedTrackPreferencePrefix = "LandLedgers.Audio.RejectedTrack.v1.";

    private GameObject playerObject;
    private MainMusicPlaylist playlist;
    private AudioClip removedClip;
    private AudioClip retainedClip;

    [SetUp]
    public void SetUp()
    {
        playerObject = new GameObject("Music Player Test");
        LLMainMusicPlayer player = playerObject.AddComponent<LLMainMusicPlayer>();
        EnsureMusicRig(player);
        playlist = ScriptableObject.CreateInstance<MainMusicPlaylist>();
        removedClip = AudioClip.Create("RejectedTestTrack", 44100, 1, 44100, false);
        retainedClip = AudioClip.Create("RetainedTestTrack", 44100, 1, 44100, false);
        PlayerPrefs.DeleteKey(RejectedTrackPreferencePrefix + removedClip.name);

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
        if (removedClip != null)
        {
            PlayerPrefs.DeleteKey(RejectedTrackPreferencePrefix + removedClip.name);
            PlayerPrefs.Save();
        }
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

    [Test]
    public void SkipCurrentTrackStartsQueuedDifferentTrackWithoutEditingPlaylistAsset()
    {
        LLMainMusicPlayer player = playerObject.GetComponent<LLMainMusicPlayer>();
        AudioSource active = GetField<AudioSource>(player, "sourceA");
        AudioSource standby = GetField<AudioSource>(player, "sourceB");
        SetField(player, "activeSource", active);
        SetField(player, "standbySource", standby);
        SetField(player, "currentClip", removedClip);
        SetField(player, "queuedClip", retainedClip);
        SetField(player, "hasBegun", true);
        active.clip = removedClip;
        standby.clip = retainedClip;

        Assert.IsTrue(player.SkipCurrentTrack());

        Assert.AreSame(retainedClip, GetField<AudioClip>(player, "currentClip"));
        Assert.AreEqual(retainedClip.name, player.CurrentTrackName);
        Assert.AreEqual(1, player.TrackCount);
        Assert.IsTrue(playlist.Contains(removedClip), "Runtime skip must not edit the authored playlist asset.");
        Assert.IsNull(GetField<AudioClip>(player, "queuedClip"));
    }

    [Test]
    public void PermanentRejectionPersistsAcrossPlaylistRefreshAndKeepsAudioAsset()
    {
        LLMainMusicPlayer player = playerObject.GetComponent<LLMainMusicPlayer>();
        AudioSource active = GetField<AudioSource>(player, "sourceA");
        SetField(player, "currentClip", removedClip);
        SetField(player, "hasBegun", true);
        active.clip = removedClip;

        Assert.IsTrue(player.RejectCurrentTrackPermanently());

        Assert.AreEqual(1, PlayerPrefs.GetInt(RejectedTrackPreferencePrefix + removedClip.name, 0));
        Assert.IsTrue(playlist.Contains(removedClip), "Rejection must not delete the curated clip reference or source audio asset.");
        Assert.AreSame(retainedClip, GetField<AudioClip>(player, "currentClip"), "The player should advance to another eligible song.");

        Invoke(player, "RefreshPlaylistRuntime");
        Assert.AreEqual(1, player.TrackCount, "A later playlist refresh must keep the rejected song excluded.");
    }

    [Test]
    public void SkippingLastTrackLeavesBothMusicSourcesSilent()
    {
        LLMainMusicPlayer player = playerObject.GetComponent<LLMainMusicPlayer>();
        playlist.TracksForTesting().Remove(retainedClip);
        Invoke(player, "RefreshPlaylistRuntime");
        AudioSource active = GetField<AudioSource>(player, "sourceA");
        AudioSource standby = GetField<AudioSource>(player, "sourceB");
        SetField(player, "activeSource", active);
        SetField(player, "standbySource", standby);
        SetField(player, "currentClip", removedClip);
        SetField(player, "hasBegun", true);
        active.clip = removedClip;

        Assert.IsTrue(player.SkipCurrentTrack());

        Assert.AreEqual(0, player.TrackCount);
        Assert.IsNull(active.clip);
        Assert.IsNull(standby.clip);
        Assert.IsFalse(GetField<bool>(player, "hasBegun"));
        Assert.IsTrue(playlist.Contains(removedClip), "Runtime skip must not edit the authored playlist asset.");
    }

    [Test]
    public void DuplicateManagerCleanupStopsOnlyItsRuntimeMusicRig()
    {
        LLMainMusicPlayer player = playerObject.GetComponent<LLMainMusicPlayer>();
        AudioSource musicSource = GetField<AudioSource>(player, "sourceA");
        AudioSource unrelatedSource = playerObject.AddComponent<AudioSource>();
        musicSource.clip = removedClip;
        musicSource.volume = 0.7f;
        unrelatedSource.clip = retainedClip;
        unrelatedSource.volume = 0.6f;

        Invoke(player, "StopDuplicateRuntimeMusic");

        Assert.IsNull(musicSource.clip);
        Assert.AreEqual(0f, musicSource.volume);
        Assert.AreSame(retainedClip, unrelatedSource.clip, "Duplicate cleanup must not stop non-music audio.");
        Assert.AreEqual(0.6f, unrelatedSource.volume);
    }

    private static void Invoke(object target, string methodName)
    {
        target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
    }

    private static void EnsureMusicRig(LLMainMusicPlayer player)
    {
        Transform rig = player.transform.Find("Runtime Music Audio Rig");
        if (rig == null)
        {
            GameObject rigObject = new GameObject("Runtime Music Audio Rig");
            rigObject.transform.SetParent(player.transform, false);
            rig = rigObject.transform;
        }

        AudioSource sourceA = EnsureSource(rig, "Music Source A");
        AudioSource sourceB = EnsureSource(rig, "Music Source B");
        SetField(player, "audioRigRoot", rig);
        SetField(player, "sourceA", sourceA);
        SetField(player, "sourceB", sourceB);
        SetField(player, "activeSource", sourceA);
        SetField(player, "standbySource", sourceB);
    }

    private static AudioSource EnsureSource(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            GameObject sourceObject = new GameObject(name);
            sourceObject.transform.SetParent(parent, false);
            child = sourceObject.transform;
        }

        AudioSource source = child.GetComponent<AudioSource>();
        return source != null ? source : child.gameObject.AddComponent<AudioSource>();
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
