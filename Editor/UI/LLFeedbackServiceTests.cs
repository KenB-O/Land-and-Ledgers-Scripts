using LandLedgers.UI;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;

namespace LandLedgers.EditorTests.UI
{
    public sealed class LLFeedbackServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            LLFeedbackService.ClearMissingClipWarningMemoryForTests();
        }

        [Test]
        public void MissingClipWarningNamesRecommendedSoundAndContext()
        {
            bool logged = LLFeedbackService.TryReserveMissingClipWarning(
                LLFeedbackKind.HeavyCash,
                "property acquisition close",
                out string warning);

            Assert.IsTrue(logged);
            StringAssert.Contains("[LL Audio]", warning);
            StringAssert.Contains("money thud", warning);
            StringAssert.Contains("property acquisition close", warning);
        }

        [Test]
        public void MissingClipWarningIsOneShotPerKindRecommendationAndContext()
        {
            Assert.IsTrue(LLFeedbackService.TryReserveMissingClipWarning(
                LLFeedbackKind.SmallCash,
                "General Store sale confirm",
                out _));

            Assert.IsFalse(LLFeedbackService.TryReserveMissingClipWarning(
                LLFeedbackKind.SmallCash,
                "General Store sale confirm",
                out string repeatedWarning));
            Assert.AreEqual(string.Empty, repeatedWarning);
        }

        [Test]
        public void EnsureAudioSourceReplacesDestroyedCachedSource()
        {
            GameObject serviceObject = new("LL Feedback Service Test");
            try
            {
                LLFeedbackService service = serviceObject.AddComponent<LLFeedbackService>();
                AudioSource staleSource = serviceObject.AddComponent<AudioSource>();
                typeof(LLFeedbackService)
                    .GetField("audioSource", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(service, staleSource);
                UnityEngine.Object.DestroyImmediate(staleSource);

                Assert.DoesNotThrow(() => typeof(LLFeedbackService)
                    .GetMethod("EnsureAudioSource", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(service, null));

                AudioSource repairedSource = serviceObject.GetComponent<AudioSource>();
                Assert.NotNull(repairedSource);
                Assert.IsFalse(repairedSource.playOnAwake);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(serviceObject);
            }
        }
    }
}
