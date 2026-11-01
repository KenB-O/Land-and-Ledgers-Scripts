using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandLedgers.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    [DefaultExecutionOrder(900)]
    public sealed class LLFeedbackService : MonoBehaviour
    {
        private const string LogPrefix = "[LL Audio]";
#if UNITY_EDITOR
        private const string UiConfirmClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/FastUISounds/SFX_FastUiClickWood03.wav";
        private const string UiBackClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/Objects/Paper Flap 3.wav";
        private const string UiPanelClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/Objects/Box Slide Open.wav";
        private const string UiSelectClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/FastUISounds/SFX_FastUiChangeOption02.wav";
        private const string NoticeClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/Objects/Paper Flap 1.wav";
        private const string WarningClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/FastUISounds/SFX_FastUiDenied02.wav";
        private const string SmallCashClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/Objects/Paper Flap 2.wav";
        private const string MediumCashClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/Objects/Box Slide Closed.wav";
        private const string HeavyCashClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/Objects/Wood Item Drop 2.wav";
        private const string StampApprovalClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/FastUISounds/SFX_FastUiClickImpact04.wav";
        private const string LedgerCloseClipPath = "Assets/Asset Packs/Sound Effects/Western Demo Audio Assets/Objects/Bar Bump 2.wav";
#endif
        private static readonly HashSet<string> MissingClipWarnings = new();
        private static LLFeedbackService instance;

        [Header("UI Clips")]
        [SerializeField] private AudioClip uiConfirmClip;
        [SerializeField] private AudioClip uiBackClip;
        [SerializeField] private AudioClip uiPanelClip;
        [SerializeField] private AudioClip uiSelectClip;
        [SerializeField] private AudioClip noticeClip;
        [SerializeField] private AudioClip warningClip;

        [Header("Money Clips")]
        [SerializeField] private AudioClip smallCashClip;
        [SerializeField] private AudioClip mediumCashClip;
        [SerializeField] private AudioClip heavyCashClip;
        [SerializeField] private AudioClip stampApprovalClip;
        [SerializeField] private AudioClip ledgerCloseClip;

        [Header("Playback")]
        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.75f;
        [SerializeField] private bool showMoneyOverlay = true;
        [SerializeField, Range(0f, 1f)] private float duplicateFeedbackCooldownSeconds = 0.18f;

        private AudioSource audioSource;
        private LLMoneyFeedbackOverlay overlay;
        private readonly Dictionary<string, float> recentFeedbackTimes = new();

        public static LLFeedbackService Instance
        {
            get
            {
                if (instance == null && Application.isPlaying)
                {
                    instance = FindAnyObjectByType<LLFeedbackService>();
                    if (instance == null)
                    {
                        GameObject serviceObject = new("LL Feedback Service");
                        instance = serviceObject.AddComponent<LLFeedbackService>();
                    }
                }

                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            AssignDefaultClipsIfNeeded();
            EnsureAudioSource();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            AssignDefaultClipsIfNeeded();
        }
#endif

        public static void Play(LLFeedbackKind kind, string context, Canvas canvas = null, Vector2? anchoredPosition = null)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Instance?.PlayInternal(kind, context, canvas, anchoredPosition);
        }

        public static string GetRecommendedSound(LLFeedbackKind kind)
        {
            return kind switch
            {
                LLFeedbackKind.SmallCash => "coin clink",
                LLFeedbackKind.MediumCash => "envelope set-down",
                LLFeedbackKind.HeavyCash => "money thud",
                LLFeedbackKind.StampApproval => "approval stamp",
                LLFeedbackKind.LedgerClose => "ledger close",
                LLFeedbackKind.UIBack => "paper fold",
                LLFeedbackKind.UIPanel => "paper slide",
                LLFeedbackKind.UISelect => "paper tick",
                LLFeedbackKind.Notice => "paper flick",
                LLFeedbackKind.Warning => "ledger tap",
                _ => "paper flick"
            };
        }

        public static bool TryReserveMissingClipWarning(LLFeedbackKind kind, string context, out string warning)
        {
            string resolvedContext = string.IsNullOrWhiteSpace(context) ? "unspecified context" : context.Trim();
            string recommendation = GetRecommendedSound(kind);
            string key = $"{kind}|{recommendation}|{resolvedContext}";
            if (!MissingClipWarnings.Add(key))
            {
                warning = string.Empty;
                return false;
            }

            warning = $"{LogPrefix} Sound left blank. Play {recommendation} for {resolvedContext}.";
            return true;
        }

        public static void ClearMissingClipWarningMemoryForTests()
        {
            MissingClipWarnings.Clear();
        }

        private void PlayInternal(LLFeedbackKind kind, string context, Canvas canvas, Vector2? anchoredPosition)
        {
            // Retain the serialized setting for scene compatibility; the money splash
            // is intentionally disabled for normal gameplay.
            _ = showMoneyOverlay;
            if (!ShouldPlay(kind, context))
            {
                return;
            }

            AudioClip clip = ResolveClip(kind);
            if (clip != null)
            {
                EnsureAudioSource();
                audioSource?.PlayOneShot(clip, masterVolume);
            }
            else if (TryReserveMissingClipWarning(kind, context, out string warning))
            {
                Debug.LogWarning(warning, this);
            }

            // Money remains audibly and textually accounted for by the ledger/UI
            // readouts, but normal transactions must not splash a floating cash
            // graphic over the game view. Keep the overlay implementation available
            // for legacy asset compatibility; it is intentionally not invoked.
        }

        private bool ShouldPlay(LLFeedbackKind kind, string context)
        {
            if (duplicateFeedbackCooldownSeconds <= 0f)
            {
                return true;
            }

            float now = UnityEngine.Time.unscaledTime;
            string key = BuildFeedbackKey(kind, context);
            if (recentFeedbackTimes.TryGetValue(key, out float lastTime)
                && now - lastTime < duplicateFeedbackCooldownSeconds)
            {
                return false;
            }

            recentFeedbackTimes[key] = now;
            return true;
        }

        private static string BuildFeedbackKey(LLFeedbackKind kind, string context)
        {
            string normalizedContext = string.IsNullOrWhiteSpace(context)
                ? string.Empty
                : context.Trim().Replace('\n', ' ').Replace('\r', ' ');
            return $"{kind}|{normalizedContext}";
        }

        private AudioClip ResolveClip(LLFeedbackKind kind)
        {
            return kind switch
            {
                LLFeedbackKind.UIConfirm => uiConfirmClip,
                LLFeedbackKind.UIBack => uiBackClip,
                LLFeedbackKind.UIPanel => uiPanelClip,
                LLFeedbackKind.UISelect => uiSelectClip,
                LLFeedbackKind.Notice => noticeClip,
                LLFeedbackKind.Warning => warningClip,
                LLFeedbackKind.SmallCash => smallCashClip,
                LLFeedbackKind.MediumCash => mediumCashClip,
                LLFeedbackKind.HeavyCash => heavyCashClip,
                LLFeedbackKind.StampApproval => stampApprovalClip,
                LLFeedbackKind.LedgerClose => ledgerCloseClip,
                _ => null
            };
        }

        private void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (audioSource != null)
            {
                audioSource.playOnAwake = false;
            }
        }

        private void AssignDefaultClipsIfNeeded()
        {
#if UNITY_EDITOR
            uiConfirmClip = uiConfirmClip != null ? uiConfirmClip : LoadEditorClip(UiConfirmClipPath);
            uiBackClip = uiBackClip != null ? uiBackClip : LoadEditorClip(UiBackClipPath);
            uiPanelClip = uiPanelClip != null ? uiPanelClip : LoadEditorClip(UiPanelClipPath);
            uiSelectClip = uiSelectClip != null ? uiSelectClip : LoadEditorClip(UiSelectClipPath);
            noticeClip = noticeClip != null ? noticeClip : LoadEditorClip(NoticeClipPath);
            warningClip = warningClip != null ? warningClip : LoadEditorClip(WarningClipPath);
            smallCashClip = smallCashClip != null ? smallCashClip : LoadEditorClip(SmallCashClipPath);
            mediumCashClip = mediumCashClip != null ? mediumCashClip : LoadEditorClip(MediumCashClipPath);
            heavyCashClip = heavyCashClip != null ? heavyCashClip : LoadEditorClip(HeavyCashClipPath);
            stampApprovalClip = stampApprovalClip != null ? stampApprovalClip : LoadEditorClip(StampApprovalClipPath);
            ledgerCloseClip = ledgerCloseClip != null ? ledgerCloseClip : LoadEditorClip(LedgerCloseClipPath);
#endif
        }

#if UNITY_EDITOR
        private static AudioClip LoadEditorClip(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
#endif

        private LLMoneyFeedbackOverlay EnsureOverlay(Canvas canvas)
        {
            if (overlay == null || overlay.gameObject == null)
            {
                overlay = null;
                Canvas targetCanvas = canvas != null ? canvas : FindAnyObjectByType<Canvas>();
                overlay = LLMoneyFeedbackOverlay.GetOrCreate(targetCanvas);
                return overlay;
            }

            return overlay;
        }

        private static bool IsMoneyKind(LLFeedbackKind kind)
        {
            return kind == LLFeedbackKind.SmallCash
                || kind == LLFeedbackKind.MediumCash
                || kind == LLFeedbackKind.HeavyCash
                || kind == LLFeedbackKind.StampApproval
                || kind == LLFeedbackKind.LedgerClose;
        }
    }
}
