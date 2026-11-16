// Campaign C (DEF-045) — saves must record the active time settings so a
// Continue resumes with the settings the save was made under, instead of
// silently reinterpreting clock state under whatever the scene asset holds.
// The pure capture/apply logic on TimeSettingsSnapshot is unit-tested
// directly (SimulationTimeSettings is a plain field bag; `new` is used
// instead of ScriptableObject.CreateInstance so the same tests run in the
// Roslyn harness and in Unity's Test Runner). The TimeManager wiring
// (CaptureSaveDto records, LoadFromSaveDto restores with a warning) is
// pinned structurally by reading the production files, because TimeManager
// is too large a closure for the focused harness — the same precedent as
// the package-3 hotkey tests. The repo root comes from LAND_LEDGERS_REPO,
// then a walk up from the current directory and the test assembly location.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Time
{
    [TestFixture]
    public sealed class CampaignCTimeSettingsPersistenceTests
    {
        private static SimulationTimeSettings NonDefaultSettings()
        {
            SimulationTimeSettings settings = new SimulationTimeSettings();
            settings.realSecondsPerGameDayAt1x = 720f;
            settings.shortTickIntervalGameMinutes = 30f;
            settings.speed1xMultiplier = 1f;
            settings.speed2xMultiplier = 2f;
            settings.speed4xMultiplier = 3f;
            settings.speed5xMultiplier = 5f;
            settings.speed6xMultiplier = 6f;
            settings.speed10xMultiplier = 9f;
            settings.speed25xMultiplier = 24f;
            settings.speed50xMultiplier = 49f;
            settings.speed100xMultiplier = 90f;
            settings.speed250xMultiplier = 200f;
            settings.speed500xMultiplier = 400f;
            settings.maxShortTicksPerFrame = 32;
            settings.maxDayRolloversPerFrame = 3;
            return settings;
        }

        private static void AssertSettingsMatchSnapshot(SimulationTimeSettings settings, TimeSettingsSnapshot snapshot)
        {
            Assert.AreEqual(snapshot.realSecondsPerGameDayAt1x, settings.realSecondsPerGameDayAt1x, 0.0001f);
            Assert.AreEqual(snapshot.shortTickIntervalGameMinutes, settings.shortTickIntervalGameMinutes, 0.0001f);
            Assert.AreEqual(snapshot.speed1xMultiplier, settings.speed1xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed2xMultiplier, settings.speed2xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed4xMultiplier, settings.speed4xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed5xMultiplier, settings.speed5xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed6xMultiplier, settings.speed6xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed10xMultiplier, settings.speed10xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed25xMultiplier, settings.speed25xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed50xMultiplier, settings.speed50xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed100xMultiplier, settings.speed100xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed250xMultiplier, settings.speed250xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.speed500xMultiplier, settings.speed500xMultiplier, 0.0001f);
            Assert.AreEqual(snapshot.maxShortTicksPerFrame, settings.maxShortTicksPerFrame);
            Assert.AreEqual(snapshot.maxDayRolloversPerFrame, settings.maxDayRolloversPerFrame);
        }

        [Test]
        public void CaptureRecordsAllRuntimeSettings()
        {
            SimulationTimeSettings settings = NonDefaultSettings();

            TimeSettingsSnapshot snapshot = TimeSettingsSnapshot.Capture(settings);

            Assert.IsTrue(snapshot.hasSettings);
            AssertSettingsMatchSnapshot(settings, snapshot);
        }

        [Test]
        public void CaptureWithNullSettingsReturnsUnsetSnapshot()
        {
            TimeSettingsSnapshot snapshot = TimeSettingsSnapshot.Capture(null);

            Assert.IsFalse(snapshot.hasSettings);
        }

        [Test]
        public void ApplyToRestoresCapturedValuesAndNamesDifferences()
        {
            SimulationTimeSettings source = NonDefaultSettings();
            TimeSettingsSnapshot snapshot = TimeSettingsSnapshot.Capture(source);
            SimulationTimeSettings target = new SimulationTimeSettings();

            List<string> differing = snapshot.ApplyTo(target);

            AssertSettingsMatchSnapshot(target, snapshot);
            Assert.IsTrue(differing.Contains("realSecondsPerGameDayAt1x"));
            Assert.IsTrue(differing.Contains("shortTickIntervalGameMinutes"));
            Assert.IsTrue(differing.Contains("speed100xMultiplier"));
            Assert.IsTrue(differing.Contains("maxShortTicksPerFrame"));
            Assert.IsTrue(differing.Contains("maxDayRolloversPerFrame"));
        }

        [Test]
        public void ApplyToIsSilentWhenIdentical()
        {
            SimulationTimeSettings source = NonDefaultSettings();
            TimeSettingsSnapshot snapshot = TimeSettingsSnapshot.Capture(source);
            SimulationTimeSettings target = NonDefaultSettings();

            List<string> differing = snapshot.ApplyTo(target);

            Assert.AreEqual(0, differing.Count);
            AssertSettingsMatchSnapshot(target, snapshot);
        }

        [Test]
        public void ApplyToIgnoresUnsetSnapshot()
        {
            TimeSettingsSnapshot snapshot = new TimeSettingsSnapshot();
            SimulationTimeSettings target = NonDefaultSettings();
            float dayLengthBefore = target.realSecondsPerGameDayAt1x;
            int capBefore = target.maxShortTicksPerFrame;

            List<string> differing = snapshot.ApplyTo(target);

            Assert.AreEqual(0, differing.Count);
            Assert.AreEqual(dayLengthBefore, target.realSecondsPerGameDayAt1x, 0.0001f);
            Assert.AreEqual(capBefore, target.maxShortTicksPerFrame);
        }

        [Test]
        public void ApplyToWithNullTargetReturnsEmpty()
        {
            TimeSettingsSnapshot snapshot = TimeSettingsSnapshot.Capture(NonDefaultSettings());

            List<string> differing = snapshot.ApplyTo(null);

            Assert.AreEqual(0, differing.Count);
        }

        [Test]
        public void ApplyToSanitizesOutOfRangeValues()
        {
            TimeSettingsSnapshot snapshot = new TimeSettingsSnapshot
            {
                hasSettings = true,
                realSecondsPerGameDayAt1x = 0f,
                speed500xMultiplier = 900f,
                maxShortTicksPerFrame = 0,
            };
            SimulationTimeSettings target = new SimulationTimeSettings();

            snapshot.ApplyTo(target);

            Assert.AreEqual(1f, target.realSecondsPerGameDayAt1x, 0.0001f);
            Assert.AreEqual(500f, target.speed500xMultiplier, 0.0001f);
            Assert.AreEqual(1, target.maxShortTicksPerFrame);
        }

        [Test]
        public void ApplyToDoesNotTouchCalendarFields()
        {
            SimulationTimeSettings source = NonDefaultSettings();
            source.anchorYear = 1870;
            source.startingYear = 1900;
            TimeSettingsSnapshot snapshot = TimeSettingsSnapshot.Capture(source);
            SimulationTimeSettings target = new SimulationTimeSettings();
            target.anchorYear = 1880;
            target.startingYear = 1892;

            snapshot.ApplyTo(target);

            Assert.AreEqual(1880, target.anchorYear);
            Assert.AreEqual(1892, target.startingYear);
        }

        private const string SaveDtosPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveDtos.cs";
        private const string TimeManagerPath =
            "Assets/Scripts/Core/Time/TimeManager.cs";

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, SaveDtosPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                System.IO.Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-c"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, SaveDtosPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-045): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-045): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        [Test]
        public void TimeSaveDtoCarriesSettingsSnapshot()
        {
            string saveDtos = ReadRepoFile(SaveDtosPath);
            int timeDtoStart = saveDtos.IndexOf("public sealed class TimeSaveDto", StringComparison.Ordinal);
            Assert.IsTrue(timeDtoStart >= 0, "TimeSaveDto must exist in SaveDtos.cs");
            int nextClass = saveDtos.IndexOf("public sealed class", timeDtoStart + 1, StringComparison.Ordinal);
            string timeDto = nextClass > 0
                ? saveDtos.Substring(timeDtoStart, nextClass - timeDtoStart)
                : saveDtos.Substring(timeDtoStart);
            Assert.IsTrue(timeDto.Contains("public TimeSettingsSnapshot settings"),
                "TimeSaveDto must carry a settings snapshot (DEF-045)");
            Assert.IsTrue(timeDto.Contains("hasSettings"),
                "TimeSaveDto settings must document the old-save hasSettings contract (DEF-045)");
        }

        [Test]
        public void CaptureSaveDtoRecordsSettings()
        {
            string timeManager = ReadRepoFile(TimeManagerPath);
            int captureStart = timeManager.IndexOf("public TimeSaveDto CaptureSaveDto()", StringComparison.Ordinal);
            Assert.IsTrue(captureStart >= 0, "TimeManager.CaptureSaveDto must exist");
            int captureEnd = timeManager.IndexOf("public void LoadFromSaveDto", captureStart, StringComparison.Ordinal);
            string capture = captureEnd > 0
                ? timeManager.Substring(captureStart, captureEnd - captureStart)
                : timeManager.Substring(captureStart);
            Assert.IsTrue(capture.Contains("TimeSettingsSnapshot.Capture(this.settings)"),
                "CaptureSaveDto must record the live time settings (DEF-045)");
        }

        [Test]
        public void LoadFromSaveDtoRestoresSettingsWithWarning()
        {
            string timeManager = ReadRepoFile(TimeManagerPath);
            int loadStart = timeManager.IndexOf("public void LoadFromSaveDto(TimeSaveDto dto)", StringComparison.Ordinal);
            Assert.IsTrue(loadStart >= 0, "TimeManager.LoadFromSaveDto must exist");
            string load = timeManager.Substring(loadStart);
            Assert.IsTrue(load.Contains("dto.settings.ApplyTo(settings)"),
                "LoadFromSaveDto must apply the saved time settings (DEF-045)");
            Assert.IsTrue(load.Contains("dto.settings.hasSettings"),
                "LoadFromSaveDto must keep current settings for old saves without recorded settings (DEF-045)");
            Assert.IsTrue(load.Contains("DEF-045") && load.Contains("LogWarning"),
                "LoadFromSaveDto must warn when the saved settings differ from the current ones (DEF-045)");
        }
    }
}
