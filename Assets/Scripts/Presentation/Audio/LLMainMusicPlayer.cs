using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

[AddComponentMenu("Land & Ledgers/Audio/LL Main Music Player")]
[DefaultExecutionOrder(-9000)]
[DisallowMultipleComponent]
public sealed class LLMainMusicPlayer : MonoBehaviour
{
    private const string RejectedTrackPreferencePrefix = "LandLedgers.Audio.RejectedTrack.v1.";

    private enum PlaybackMode
    {
        IntelligentShuffle,
        Sequential
    }

    [Header("Automation")]
    [SerializeField] private bool persistAcrossScenes = true;
    [SerializeField] private bool playOnAwake = true;
    [SerializeField] private bool autoPopulatePlaylistInEditor = true;
    [SerializeField] private bool debugLogging = true;

    [Header("Player Controls")]
    [Tooltip("Developer-only curation: hold Ctrl + Alt + Shift and press Pause/Break to remove the playing track from the playlist asset.")]
    [SerializeField] private Key removeCurrentTrackHotkey = Key.Pause;
    [SerializeField] private bool requireShiftControlAltForRemove = true;

    [Header("Playback")]
    [SerializeField] private PlaybackMode playbackMode = PlaybackMode.IntelligentShuffle;
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.85f;
    [Min(0.1f)] [SerializeField] private float crossfadeDuration = 5f;
    [Min(0.1f)] [SerializeField] private float startupFadeDuration = 3f;
    [Min(0.05f)] [SerializeField] private float scheduleLeadTime = 0.5f;
    [Min(0f)] [SerializeField] private float startupDelay = 0.05f;
    [Min(0.05f)] [SerializeField] private float skipFadeDuration = 0.5f;

    [Header("Playlist")]
    [SerializeField] private MainMusicPlaylist playlistAsset;
    [SerializeField] private AudioClip[] musicClips = Array.Empty<AudioClip>();
    [SerializeField] private string musicAssetFolder = "Assets/Core/Audio/MainMusic";
    [SerializeField] private string[] runtimeResourcesFolders = Array.Empty<string>();
    [Min(0)] [SerializeField] private int recentHistorySize = 3;
    [SerializeField] private bool avoidImmediateRepeats = true;

    [Header("Diagnostics")]
    [SerializeField] private string currentTrackName = string.Empty;
    [SerializeField] private string nextTrackName = string.Empty;
    [SerializeField] private int discoveredTrackCount;
    [SerializeField] private bool isSystemReady;
    [SerializeField] private bool isTransitioning;
    [SerializeField] private bool listenerFound;
    [SerializeField] private int listenerCount;
    [SerializeField] private bool isPlayingNow;

    private static LLMainMusicPlayer instance;

    private readonly List<AudioClip> sanitizedPlaylist = new List<AudioClip>();
    private readonly Queue<AudioClip> recentHistory = new Queue<AudioClip>();

    public bool IsTransitioning => isTransitioning;

    private Transform audioRigRoot;
    private AudioSource sourceA;
    private AudioSource sourceB;
    private AudioSource activeSource;
    private AudioSource standbySource;

    private AudioClip currentClip;
    private AudioClip queuedClip;

    private int sequentialIndex = -1;
    private bool hasBegun;
    private bool nextQueued;
    private bool swapPending;
    private bool startupFinished;

    private bool startupFadeActive;
    private double startupFadeStartDsp;
    private double startupFadeEndDsp;

    private double currentStartDsp;
    private double currentEndDsp;
    private double nextStartDsp;
    private double fadeStartDsp;
    private double fadeEndDsp;

    public static LLMainMusicPlayer Instance => instance;
    public string CurrentTrackName => currentTrackName;
    public string NextTrackName => nextTrackName;
    public bool IsReady => isSystemReady;
    public int TrackCount => discoveredTrackCount;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            StopDuplicateRuntimeMusic();
            enabled = false;
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        BuildOrFindRuntimeAudioRig();
        RefreshPlaylistRuntime();
        RefreshAudioListenerDiagnostics();
    }

    private IEnumerator Start()
    {
        yield return null;

        startupFinished = true;

        if (playOnAwake)
        {
            PlayIfNeeded();
        }
    }

    private void Update()
    {
        HandleSkipTrackHotkey();

#if UNITY_EDITOR
        HandleRemoveTrackHotkey();
#endif

        isPlayingNow = activeSource != null && activeSource.isPlaying;

        if (!isSystemReady || !hasBegun || activeSource == null || currentClip == null)
        {
            return;
        }

        double dspNow = AudioSettings.dspTime;

        UpdateVolumes(dspNow);

        if (swapPending && dspNow >= fadeEndDsp)
        {
            FinalizeSwap();
        }

        if (!nextQueued)
        {
            float safeFade = GetSafeCrossfadeDuration(currentClip, null);
            double scheduleMoment = currentEndDsp - safeFade - scheduleLeadTime;

            if (dspNow >= scheduleMoment)
            {
                QueueNextScheduledTrack();
            }
        }

        if (!swapPending && activeSource != null && !activeSource.isPlaying && dspNow > currentStartDsp + 0.25d)
        {
            if (debugLogging)
            {
                Debug.LogWarning("[LLMainMusicPlayer] Active source stopped unexpectedly. Attempting recovery.");
            }

            AudioClip fallback = SelectNextClip(currentClip);
            if (fallback != null)
            {
                StartFreshTrack(fallback, true);
            }
        }
    }

    public void PlayIfNeeded()
    {
        if (!startupFinished)
        {
            return;
        }

        BuildOrFindRuntimeAudioRig();
        RefreshPlaylistRuntime();
        RefreshAudioListenerDiagnostics();

        if (!listenerFound)
        {
            Debug.LogError("[LLMainMusicPlayer] No AudioListener found in the scene. Put one on the main camera.");
            return;
        }

        if (!isSystemReady)
        {
            Debug.LogError("[LLMainMusicPlayer] No music clips available. Playlist is empty.");
            return;
        }

        if (hasBegun && activeSource != null && activeSource.isPlaying)
        {
            return;
        }

        BeginPlayback();
    }

    public void RestartPlaylist()
    {
        StopAllInternalPlayback();
        recentHistory.Clear();
        sequentialIndex = -1;
        hasBegun = false;
        nextQueued = false;
        swapPending = false;
        isTransitioning = false;
        startupFadeActive = false;
        currentTrackName = string.Empty;
        nextTrackName = string.Empty;
        currentClip = null;
        queuedClip = null;

        PlayIfNeeded();
    }

    public void SkipToNext()
    {
        if (!isSystemReady || activeSource == null || currentClip == null)
        {
            return;
        }

        AudioClip next = SelectNextClip(currentClip);
        if (next == null)
        {
            return;
        }

        double startTime = AudioSettings.dspTime + 0.05d;
        float fade = Mathf.Min(skipFadeDuration, GetSafeCrossfadeDuration(currentClip, next));
        PrepareQueuedTrack(next, startTime, fade, true);
    }

    /// <summary>Immediately discards the current track from this runtime playback queue.</summary>
    public bool SkipCurrentTrack()
    {
        if (currentClip == null)
        {
            return false;
        }

        AudioClip skippedClip = currentClip;
        AudioClip alreadyQueuedNext = queuedClip != skippedClip ? queuedClip : null;
        int skippedIndex = sanitizedPlaylist.IndexOf(skippedClip);
        sanitizedPlaylist.RemoveAll(clip => clip == skippedClip);
        if (skippedIndex >= 0 && sequentialIndex >= skippedIndex)
        {
            sequentialIndex--;
        }

        RemoveFromRecentHistory(skippedClip);
        StopAllInternalPlayback();
        queuedClip = null;
        currentClip = null;
        nextQueued = false;
        swapPending = false;
        isTransitioning = false;
        startupFadeActive = false;
        currentTrackName = string.Empty;
        nextTrackName = string.Empty;
        hasBegun = false;
        discoveredTrackCount = sanitizedPlaylist.Count;
        isSystemReady = discoveredTrackCount > 0;

        AudioClip next = alreadyQueuedNext != null
            ? alreadyQueuedNext
            : SelectNextClip(skippedClip);
        if (next == null)
        {
            return true;
        }

        hasBegun = true;
        StartFreshTrack(next, false);
        return true;
    }

    /// <summary>
    /// Permanently excludes the current song from this player's future rotation
    /// without deleting the audio clip or modifying the curated playlist asset.
    /// This preference is independent of campaign save data.
    /// </summary>
    public bool RejectCurrentTrackPermanently()
    {
        AudioClip rejectedClip = currentClip != null
            ? currentClip
            : activeSource != null ? activeSource.clip : null;
        if (rejectedClip == null) return false;

        PlayerPrefs.SetInt(RejectedTrackPreferencePrefix + rejectedClip.name, 1);
        PlayerPrefs.Save();
        RefreshPlaylistRuntime();
        return SkipCurrentTrack();
    }

    /// <summary>
    /// Removes the currently playing clip from the developer-curated playlist asset.
    /// The source AudioClip file is left untouched.
    /// </summary>
    public bool RemoveCurrentTrackFromOrder()
    {
        AudioClip removedClip = currentClip != null
            ? currentClip
            : activeSource != null ? activeSource.clip : null;

        if (removedClip == null || playlistAsset == null)
        {
            return false;
        }

        int removedCount = playlistAsset.Remove(removedClip);
        if (removedCount == 0)
        {
            return false;
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(playlistAsset);
        if (UnityEditor.EditorUtility.IsPersistent(playlistAsset))
        {
            UnityEditor.AssetDatabase.SaveAssetIfDirty(playlistAsset);
        }
#endif
        sanitizedPlaylist.RemoveAll(clip => clip == removedClip);
        RemoveFromRecentHistory(removedClip);
        discoveredTrackCount = sanitizedPlaylist.Count;
        isSystemReady = discoveredTrackCount > 0;

        // Keep an already scheduled, different next track. If the current track had
        // queued itself (the one-track playlist case), cancel that copy before advancing.
        if (queuedClip == removedClip)
        {
            if (standbySource != null)
            {
                standbySource.Stop();
                standbySource.clip = null;
            }

            queuedClip = null;
            nextTrackName = string.Empty;
            nextQueued = false;
            swapPending = false;
            isTransitioning = false;
        }

        if (swapPending && queuedClip != null)
        {
            Debug.Log($"[LLMainMusicPlayer] Removed '{removedClip.name}' from the music order. '{queuedClip.name}' is already next.");
            return true;
        }

        AudioClip next = SelectNextClip(removedClip);
        if (activeSource != null && hasBegun)
        {
            double startTime = AudioSettings.dspTime + 0.05d;
            float fade = next != null
                ? Mathf.Min(skipFadeDuration, GetSafeCrossfadeDuration(removedClip, next))
                : skipFadeDuration;
            PrepareQueuedTrack(next, startTime, fade, true);
        }

        Debug.Log(next != null
            ? $"[LLMainMusicPlayer] Removed '{removedClip.name}' from playlist asset '{playlistAsset.name}'. The audio file was kept."
            : $"[LLMainMusicPlayer] Removed '{removedClip.name}' from playlist asset '{playlistAsset.name}'. The audio file was kept; no tracks remain.");
        return true;
    }

    public void StopMusic()
    {
        StopAllInternalPlayback();
        hasBegun = false;
        nextQueued = false;
        swapPending = false;
        isTransitioning = false;
        startupFadeActive = false;
        currentTrackName = string.Empty;
        nextTrackName = string.Empty;
        currentClip = null;
        queuedClip = null;
    }

    [ContextMenu("Refresh Playlist")]
    private void ContextRefreshPlaylist()
    {
#if UNITY_EDITOR
        AutoPopulatePlaylistEditor(true);
#endif
        RefreshPlaylistRuntime();
        RefreshAudioListenerDiagnostics();
        LogStartupDiagnostics();
    }

    [ContextMenu("Restart Music")]
    private void ContextRestartMusic()
    {
        RestartPlaylist();
    }

    [ContextMenu("Skip To Next")]
    private void ContextSkipToNext()
    {
        SkipToNext();
    }

    [ContextMenu("Log Diagnostics")]
    private void ContextLogDiagnostics()
    {
        RefreshPlaylistRuntime();
        RefreshAudioListenerDiagnostics();
        LogStartupDiagnostics();
    }

#if UNITY_EDITOR
    private void HandleRemoveTrackHotkey()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || removeCurrentTrackHotkey == Key.None ||
            !keyboard[removeCurrentTrackHotkey].wasPressedThisFrame)
        {
            return;
        }

        if (requireShiftControlAltForRemove &&
            (!keyboard.leftShiftKey.isPressed && !keyboard.rightShiftKey.isPressed ||
             !keyboard.leftCtrlKey.isPressed && !keyboard.rightCtrlKey.isPressed ||
             !keyboard.leftAltKey.isPressed && !keyboard.rightAltKey.isPressed))
        {
            return;
        }

        RemoveCurrentTrackFromOrder();
    }
#endif

    private void HandleSkipTrackHotkey()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
        {
            RejectCurrentTrackPermanently();
        }
    }

    private void RemoveFromRecentHistory(AudioClip removedClip)
    {
        if (recentHistory.Count == 0)
        {
            return;
        }

        AudioClip[] retained = recentHistory.ToArray();
        recentHistory.Clear();
        for (int i = 0; i < retained.Length; i++)
        {
            if (retained[i] != null && retained[i] != removedClip)
            {
                recentHistory.Enqueue(retained[i]);
            }
        }
    }

    private void BeginPlayback()
    {
        activeSource = sourceA;
        standbySource = sourceB;
        hasBegun = true;
        nextQueued = false;
        swapPending = false;
        isTransitioning = false;

        AudioClip firstClip = SelectNextClip(null);
        if (firstClip == null)
        {
            isSystemReady = false;
            Debug.LogError("[LLMainMusicPlayer] Could not select a first track. Playlist may be empty.");
            return;
        }

        LogStartupDiagnostics();
        StartFreshTrack(firstClip, true);
    }

    private void StartFreshTrack(AudioClip clip, bool useStartupFade)
    {
        if (clip == null || activeSource == null || standbySource == null)
        {
            return;
        }

        standbySource.Stop();
        standbySource.clip = null;
        standbySource.volume = 0f;

        activeSource.Stop();
        activeSource.clip = clip;
        activeSource.time = 0f;
        activeSource.volume = useStartupFade ? 0f : masterVolume;

        if (startupDelay <= 0.001f)
        {
            activeSource.Play();
            currentStartDsp = AudioSettings.dspTime;
        }
        else
        {
            double startDsp = AudioSettings.dspTime + startupDelay;
            activeSource.PlayScheduled(startDsp);
            currentStartDsp = startDsp;
        }

        currentClip = clip;
        queuedClip = null;
        currentEndDsp = currentStartDsp + clip.length;
        nextStartDsp = 0d;
        fadeStartDsp = 0d;
        fadeEndDsp = 0d;
        nextQueued = false;
        swapPending = false;
        isTransitioning = false;
        currentTrackName = clip.name;
        nextTrackName = string.Empty;

        startupFadeActive = useStartupFade;
        startupFadeStartDsp = currentStartDsp;
        startupFadeEndDsp = currentStartDsp + Mathf.Max(0.1f, startupFadeDuration);

        RecordHistory(clip);

        if (debugLogging)
        {
            Debug.Log($"[LLMainMusicPlayer] Now playing: {clip.name} | Tracks found: {discoveredTrackCount}");
        }
    }

    private void QueueNextScheduledTrack()
    {
        AudioClip next = SelectNextClip(currentClip);
        if (next == null)
        {
            return;
        }

        float fade = GetSafeCrossfadeDuration(currentClip, next);
        double preferredStart = currentEndDsp - fade;
        double now = AudioSettings.dspTime;
        double safeStart = Math.Max(preferredStart, now + 0.05d);

        PrepareQueuedTrack(next, safeStart, fade, false);
    }

    private void PrepareQueuedTrack(AudioClip clip, double startDsp, float fadeDuration, bool cutCurrentAtFadeEnd)
    {
        if (activeSource == null)
        {
            return;
        }

        fadeDuration = Mathf.Max(0.05f, fadeDuration);
        nextStartDsp = startDsp;
        fadeStartDsp = startDsp;
        fadeEndDsp = startDsp + fadeDuration;
        nextQueued = true;
        swapPending = true;
        isTransitioning = true;

        if (cutCurrentAtFadeEnd && activeSource.clip != null)
        {
            currentEndDsp = fadeEndDsp;
            activeSource.SetScheduledEndTime(fadeEndDsp);
        }

        if (clip == null || standbySource == null)
        {
            queuedClip = null;
            nextTrackName = string.Empty;
            return;
        }

        standbySource.Stop();
        standbySource.clip = clip;
        standbySource.time = 0f;
        standbySource.volume = 0f;
        standbySource.PlayScheduled(startDsp);

        queuedClip = clip;
        nextTrackName = clip.name;
    }

    private void UpdateVolumes(double dspNow)
    {
        if (activeSource == null)
        {
            return;
        }

        if (startupFadeActive)
        {
            if (dspNow <= startupFadeStartDsp)
            {
                activeSource.volume = 0f;
            }
            else if (dspNow >= startupFadeEndDsp)
            {
                activeSource.volume = masterVolume;
                startupFadeActive = false;
            }
            else
            {
                float t = (float)((dspNow - startupFadeStartDsp) / (startupFadeEndDsp - startupFadeStartDsp));
                t = Mathf.Clamp01(t);
                float eased = EaseInOut(t);
                activeSource.volume = masterVolume * eased;
            }

            if (standbySource != null && !swapPending)
            {
                standbySource.volume = 0f;
            }
        }

        if (!swapPending)
        {
            if (!startupFadeActive)
            {
                activeSource.volume = masterVolume;
            }

            if (standbySource != null)
            {
                standbySource.volume = 0f;
            }

            return;
        }

        if (dspNow <= fadeStartDsp)
        {
            if (!startupFadeActive)
            {
                activeSource.volume = masterVolume;
            }

            if (standbySource != null)
            {
                standbySource.volume = 0f;
            }

            return;
        }

        if (dspNow >= fadeEndDsp)
        {
            activeSource.volume = 0f;

            if (standbySource != null)
            {
                standbySource.volume = queuedClip != null ? masterVolume : 0f;
            }

            return;
        }

        float rawT = (float)((dspNow - fadeStartDsp) / (fadeEndDsp - fadeStartDsp));
        rawT = Mathf.Clamp01(rawT);

        float easedT = EaseInOut(rawT);

        float fadeIn = Mathf.Sin(easedT * Mathf.PI * 0.5f);
        float fadeOut = Mathf.Cos(easedT * Mathf.PI * 0.5f);

        activeSource.volume = masterVolume * fadeOut;

        if (standbySource != null)
        {
            standbySource.volume = queuedClip != null ? masterVolume * fadeIn : 0f;
        }
    }

    private void FinalizeSwap()
    {
        if (!swapPending)
        {
            return;
        }

        if (queuedClip == null)
        {
            StopMusic();
            return;
        }

        if (activeSource != null)
        {
            activeSource.Stop();
            activeSource.clip = null;
            activeSource.volume = 0f;
        }

        AudioSource previousActive = activeSource;
        activeSource = standbySource;
        standbySource = previousActive;

        currentClip = queuedClip;
        currentStartDsp = nextStartDsp;
        currentEndDsp = currentStartDsp + currentClip.length;
        currentTrackName = currentClip.name;

        RecordHistory(currentClip);

        queuedClip = null;
        nextQueued = false;
        swapPending = false;
        isTransitioning = false;
        startupFadeActive = false;
        nextTrackName = string.Empty;

        if (standbySource != null)
        {
            standbySource.Stop();
            standbySource.clip = null;
            standbySource.volume = 0f;
        }

        if (activeSource != null)
        {
            activeSource.volume = masterVolume;
        }
    }

    private float EaseInOut(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private float GetSafeCrossfadeDuration(AudioClip current, AudioClip next)
    {
        float currentMax = current != null ? Mathf.Max(0.05f, current.length - 0.05f) : crossfadeDuration;
        float nextMax = next != null ? Mathf.Max(0.05f, next.length - 0.05f) : crossfadeDuration;
        return Mathf.Clamp(crossfadeDuration, 0.05f, Mathf.Min(currentMax, nextMax));
    }

    private AudioClip SelectNextClip(AudioClip current)
    {
        if (sanitizedPlaylist.Count == 0)
        {
            return null;
        }

        if (sanitizedPlaylist.Count == 1)
        {
            return sanitizedPlaylist[0];
        }

        if (playbackMode == PlaybackMode.Sequential)
        {
            return SelectSequentialClip();
        }

        List<AudioClip> candidates = new List<AudioClip>(sanitizedPlaylist.Count);

        for (int i = 0; i < sanitizedPlaylist.Count; i++)
        {
            AudioClip clip = sanitizedPlaylist[i];
            if (clip == null)
            {
                continue;
            }

            if (avoidImmediateRepeats && clip == current)
            {
                continue;
            }

            if (IsInRecentHistory(clip))
            {
                continue;
            }

            candidates.Add(clip);
        }

        if (candidates.Count == 0)
        {
            for (int i = 0; i < sanitizedPlaylist.Count; i++)
            {
                AudioClip clip = sanitizedPlaylist[i];
                if (clip == null)
                {
                    continue;
                }

                if (avoidImmediateRepeats && clip == current)
                {
                    continue;
                }

                candidates.Add(clip);
            }
        }

        if (candidates.Count == 0)
        {
            for (int i = 0; i < sanitizedPlaylist.Count; i++)
            {
                AudioClip clip = sanitizedPlaylist[i];
                if (clip != null)
                {
                    candidates.Add(clip);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private AudioClip SelectSequentialClip()
    {
        if (sanitizedPlaylist.Count == 0)
        {
            return null;
        }

        int attempts = sanitizedPlaylist.Count;

        for (int i = 0; i < attempts; i++)
        {
            sequentialIndex = (sequentialIndex + 1) % sanitizedPlaylist.Count;
            AudioClip clip = sanitizedPlaylist[sequentialIndex];

            if (clip == null)
            {
                continue;
            }

            if (avoidImmediateRepeats && clip == currentClip && sanitizedPlaylist.Count > 1)
            {
                continue;
            }

            return clip;
        }

        return sanitizedPlaylist[0];
    }

    private bool IsInRecentHistory(AudioClip clip)
    {
        if (clip == null || recentHistorySize <= 0 || recentHistory.Count == 0)
        {
            return false;
        }

        foreach (AudioClip recent in recentHistory)
        {
            if (recent == clip)
            {
                return true;
            }
        }

        return false;
    }

    private void RecordHistory(AudioClip clip)
    {
        if (clip == null || recentHistorySize <= 0)
        {
            return;
        }

        recentHistory.Enqueue(clip);

        while (recentHistory.Count > recentHistorySize)
        {
            recentHistory.Dequeue();
        }
    }

    private void RefreshPlaylistRuntime()
    {
        sanitizedPlaylist.Clear();
        HashSet<AudioClip> seen = new HashSet<AudioClip>();

        if (playlistAsset != null)
        {
            AppendUniqueClips(playlistAsset.Tracks, seen);
        }
        else
        {
            AppendUniqueClips(musicClips, seen);
        }

        if (playlistAsset == null && sanitizedPlaylist.Count == 0)
        {
            LoadFallbackResources(seen);
        }

        discoveredTrackCount = sanitizedPlaylist.Count;
        isSystemReady = discoveredTrackCount > 0;
    }

    private void AppendUniqueClips(IReadOnlyList<AudioClip> sourceClips, HashSet<AudioClip> seen)
    {
        if (sourceClips == null)
        {
            return;
        }

        for (int i = 0; i < sourceClips.Count; i++)
        {
            AudioClip clip = sourceClips[i];
            if (clip == null)
            {
                continue;
            }

            if (PlayerPrefs.GetInt(RejectedTrackPreferencePrefix + clip.name, 0) != 0) continue;

            if (seen.Add(clip))
            {
                sanitizedPlaylist.Add(clip);
            }
        }
    }

    private void LoadFallbackResources(HashSet<AudioClip> seen)
    {
        if (runtimeResourcesFolders == null || runtimeResourcesFolders.Length == 0)
        {
            return;
        }

        for (int i = 0; i < runtimeResourcesFolders.Length; i++)
        {
            string folder = runtimeResourcesFolders[i];
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            AudioClip[] loaded = Resources.LoadAll<AudioClip>(folder.Trim());
            AppendUniqueClips(loaded, seen);
        }
    }

    private void BuildOrFindRuntimeAudioRig()
    {
        if (audioRigRoot == null)
        {
            Transform existingRoot = transform.Find("Runtime Music Audio Rig");
            if (existingRoot != null)
            {
                audioRigRoot = existingRoot;
            }
            else
            {
                GameObject rig = new GameObject("Runtime Music Audio Rig");
                rig.transform.SetParent(transform, false);
                audioRigRoot = rig.transform;
            }
        }

        sourceA = GetOrCreateChildSource(audioRigRoot, "Music Source A");
        sourceB = GetOrCreateChildSource(audioRigRoot, "Music Source B");

        ConfigureAudioSource(sourceA);
        ConfigureAudioSource(sourceB);

        if (activeSource == null)
        {
            activeSource = sourceA;
        }

        if (standbySource == null)
        {
            standbySource = sourceB;
        }
    }

    private AudioSource GetOrCreateChildSource(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        GameObject childObject;

        if (child == null)
        {
            childObject = new GameObject(childName);
            childObject.transform.SetParent(parent, false);
        }
        else
        {
            childObject = child.gameObject;
        }

        AudioSource source = childObject.GetComponent<AudioSource>();
        if (source == null)
        {
            source = childObject.AddComponent<AudioSource>();
        }

        return source;
    }

    private void ConfigureAudioSource(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        source.enabled = true;
        source.playOnAwake = false;
        source.loop = false;
        source.mute = false;
        source.volume = 0f;
        source.pitch = 1f;
        source.panStereo = 0f;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.spread = 0f;
        source.priority = 16;
        source.minDistance = 1f;
        source.maxDistance = 500f;
        source.reverbZoneMix = 0f;
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = true;
        source.ignoreListenerPause = true;
        source.ignoreListenerVolume = true;
    }

    private void RefreshAudioListenerDiagnostics()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>();
        listenerCount = listeners != null ? listeners.Length : 0;
        listenerFound = listenerCount > 0;
    }

    private void LogStartupDiagnostics()
    {
        if (!debugLogging)
        {
            return;
        }

        string activeName = activeSource != null ? activeSource.name : "null";
        string standbyName = standbySource != null ? standbySource.name : "null";

        Debug.Log(
            "[LLMainMusicPlayer] Diagnostics\n" +
            $"- Tracks found: {discoveredTrackCount}\n" +
            $"- Listener found: {listenerFound}\n" +
            $"- Listener count: {listenerCount}\n" +
            $"- Active source: {activeName}\n" +
            $"- Standby source: {standbyName}\n" +
            $"- GameObject activeInHierarchy: {gameObject.activeInHierarchy}"
        );

        if (listenerCount > 1)
        {
            Debug.LogWarning("[LLMainMusicPlayer] More than one AudioListener exists in the scene.");
        }

        if (discoveredTrackCount == 0)
        {
            Debug.LogWarning("[LLMainMusicPlayer] No tracks were discovered. Check Assets/Core/Audio/MainMusic.");
        }
    }

    private void StopAllInternalPlayback()
    {
        if (sourceA != null)
        {
            sourceA.Stop();
            sourceA.clip = null;
            sourceA.volume = 0f;
        }

        if (sourceB != null)
        {
            sourceB.Stop();
            sourceB.clip = null;
            sourceB.volume = 0f;
        }
    }

    private void StopDuplicateRuntimeMusic()
    {
        Transform rig = transform.Find("Runtime Music Audio Rig");
        if (rig == null)
        {
            return;
        }

        AudioSource[] musicSources = rig.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < musicSources.Length; i++)
        {
            AudioSource source = musicSources[i];
            if (source == null)
            {
                continue;
            }

            source.Stop();
            source.clip = null;
            source.volume = 0f;
        }
    }

    private void OnValidate()
    {
        masterVolume = Mathf.Clamp01(masterVolume);
        crossfadeDuration = Mathf.Max(0.1f, crossfadeDuration);
        startupFadeDuration = Mathf.Max(0.1f, startupFadeDuration);
        scheduleLeadTime = Mathf.Max(0.05f, scheduleLeadTime);
        startupDelay = Mathf.Max(0f, startupDelay);
        skipFadeDuration = Mathf.Max(0.05f, skipFadeDuration);
        recentHistorySize = Mathf.Max(0, recentHistorySize);

#if UNITY_EDITOR
        if (!Application.isPlaying &&
            playlistAsset == null && autoPopulatePlaylistInEditor &&
            !EditorApplication.isCompiling &&
            !EditorApplication.isUpdating)
        {
            AutoPopulatePlaylistEditor(false);
        }
#endif
    }

#if UNITY_EDITOR
    private void Reset()
    {
        if (!Application.isPlaying)
        {
            AutoPopulatePlaylistEditor(true);
            EditorUtility.SetDirty(this);
        }
    }

    private void AutoPopulatePlaylistEditor(bool forceDirty)
    {
        if (playlistAsset != null)
        {
            return;
        }

        List<AudioClip> found = new List<AudioClip>();

        string folder = string.IsNullOrWhiteSpace(musicAssetFolder)
            ? string.Empty
            : musicAssetFolder.Trim().Replace('\\', '/');

        if (!string.IsNullOrWhiteSpace(folder) && AssetDatabase.IsValidFolder(folder))
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folder });

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

                if (clip != null)
                {
                    found.Add(clip);
                }
            }
        }

        found.Sort((a, b) =>
        {
            string pathA = AssetDatabase.GetAssetPath(a);
            string pathB = AssetDatabase.GetAssetPath(b);
            return string.CompareOrdinal(pathA, pathB);
        });

        bool changed = AreDifferent(found, musicClips);

        if (!changed && !forceDirty)
        {
            discoveredTrackCount = found.Count;
            return;
        }

        musicClips = found.ToArray();
        discoveredTrackCount = musicClips.Length;
        EditorUtility.SetDirty(this);
    }

    private bool AreDifferent(List<AudioClip> newClips, AudioClip[] existing)
    {
        if (existing == null || newClips.Count != existing.Length)
        {
            return true;
        }

        for (int i = 0; i < newClips.Count; i++)
        {
            if (newClips[i] != existing[i])
            {
                return true;
            }
        }

        return false;
    }
#endif
}
