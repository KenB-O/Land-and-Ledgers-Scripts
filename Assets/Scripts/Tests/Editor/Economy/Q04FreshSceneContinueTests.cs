using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Persistence;
using NUnit.Framework;

namespace LandLedgers.Tests.Economy
{
    /// <summary>
    /// Priority 5 Slice 0 — Q04 fresh-scene Continue pins (tests only).
    /// On a fresh-scene Continue the bootstrapper returns early before
    /// ConfigureLiveBakerySaveOwner, ConfigureMillbrookAidTable, and
    /// RegisterNotionsProcurement ever run, so SaveLoadManager's Pass 9
    /// state, Millbrook runtime, and procurement service are all null
    /// when LoadDefaultSlot restores. The coordinator then takes its
    /// null-state branches and drops the saved sections — with a
    /// diagnostic line each, and no crash. These pins record the drop
    /// exactly as the coordinator emits it, so Slices 1-3 have something
    /// to close. Roslyn harness only. No Unity compilation, Test Runner,
    /// PlayMode, or scene wiring is claimed by this file.
    ///
    /// NOTE on the "exact diagnostic" wording: the plan quotes the
    /// diagnostic text without the coordinator's "PassXlSliceSaveCoordinator: "
    /// prefix or the trailing Codex sentence. The emitted line is the
    /// plan's quoted text embedded verbatim in the longer line, so each
    /// test asserts BOTH: the joined diagnostics contain the plan's exact
    /// quoted substring, and the single diagnostic line mentioning the
    /// section equals the full line the source actually emits. Nothing
    /// is weakened — the full-line assertion is strictly stronger.
    /// </summary>
    [TestFixture]
    public sealed class Q04FreshSceneContinueTests
    {
        // The 9-argument Restore is the production call shape
        // (SaveLoadManager.LoadDefaultSlot). All holders null is the
        // fresh-scene Continue shape: no Pass 9 state, no Millbrook
        // runtime, no procurement service, no D3/Pass 10 state.
        private static List<string> RestoreWithNoHolders(PassXlSliceSaveDto dto)
        {
            return PassXlSliceSaveCoordinator.Restore(
                dto, null, null, null, null, null, null, null, null);
        }

        [Test]
        public void ContinueDrop_Pin_Pass9SectionPresent_NullState_LogsExactDiagnostic()
        {
            var dto = new PassXlSliceSaveDto
            {
                series2Pass9 = new Series2Pass9SaveDto(),
            };

            List<string> diagnostics = RestoreWithNoHolders(dto);

            string joined = string.Join("; ", diagnostics);
            Assert.IsTrue(joined.Contains(
                    "Pass 9 section present in save but no Series2Pass9SaveState supplied; Pass 9 state NOT restored."),
                "the Pass 9 drop diagnostic must appear verbatim; diag: " + joined);
            List<string> pass9Lines = diagnostics.Where(d => d.Contains("Pass 9")).ToList();
            Assert.AreEqual(1, pass9Lines.Count,
                "exactly one diagnostic mentions Pass 9, and it is the drop line; diag: " + joined);
            Assert.AreEqual(
                "PassXlSliceSaveCoordinator: Pass 9 section present in save but no Series2Pass9SaveState supplied; Pass 9 state NOT restored. Codex must set SaveLoadManager.Series2Pass9State before loading, exactly as for procurement.",
                pass9Lines[0],
                "the full emitted line, prefix and trailing sentence included");
            Assert.IsTrue(!diagnostics.Any(d => d.Contains("Series2Pass9Save: restore complete")),
                "no Pass 9 restore ran at all — the section was dropped, not partially restored; diag: " + joined);
            Assert.IsNotNull(dto.series2Pass9,
                "restore must not consume or clear the dropped section from the DTO");
        }

        [Test]
        public void ContinueDrop_Pin_MillbrookSectionPresent_NullRuntime_LogsExactDiagnostic()
        {
            var dto = new PassXlSliceSaveDto
            {
                millbrookAid = new OffMapAidBusinessSaveDto { BusinessId = "millbrook-aid-1" },
            };

            List<string> diagnostics = RestoreWithNoHolders(dto);

            string joined = string.Join("; ", diagnostics);
            Assert.IsTrue(joined.Contains(
                    "Millbrook aid section is present but no live aid runtime was supplied; state was not restored."),
                "the Millbrook drop diagnostic must appear verbatim; diag: " + joined);
            List<string> millbrookLines = diagnostics.Where(d => d.Contains("Millbrook")).ToList();
            Assert.AreEqual(1, millbrookLines.Count,
                "exactly one diagnostic mentions Millbrook, and it is the drop line; diag: " + joined);
            Assert.AreEqual(
                "PassXlSliceSaveCoordinator: Millbrook aid section is present but no live aid runtime was supplied; state was not restored.",
                millbrookLines[0],
                "the full emitted line, prefix included");
            Assert.IsTrue(!diagnostics.Any(d => d.Contains("restored Millbrook aid table")),
                "no Millbrook restore ran at all — the section was dropped, not partially restored; diag: " + joined);
            Assert.IsNotNull(dto.millbrookAid,
                "restore must not consume or clear the dropped section from the DTO");
        }

        [Test]
        public void ContinueDrop_Pin_ProcurementSectionPresent_NullService_LogsExactDiagnostic()
        {
            var dto = new PassXlSliceSaveDto
            {
                procurement = new NamedSkuProcurementSaveDto { BusinessId = "bakery-notions" },
            };

            List<string> diagnostics = RestoreWithNoHolders(dto);

            string joined = string.Join("; ", diagnostics);
            Assert.IsTrue(joined.Contains(
                    "no procurement service supplied; procurement state not restored."),
                "the procurement drop diagnostic must appear verbatim; diag: " + joined);
            List<string> procurementLines = diagnostics.Where(d => d.Contains("procurement")).ToList();
            Assert.AreEqual(1, procurementLines.Count,
                "exactly one diagnostic mentions procurement, and it is the drop line; diag: " + joined);
            Assert.AreEqual(
                "PassXlSliceSaveCoordinator: no procurement service supplied; procurement state not restored. Codex must register the production procurement service with SaveLoadManager before saving, or procurement will not persist.",
                procurementLines[0],
                "the full emitted line, prefix and trailing sentence included");
            Assert.IsTrue(!diagnostics.Any(d => d.Contains("restored off-map procurement")),
                "no procurement restore ran at all — the section was dropped, not partially restored; diag: " + joined);
            Assert.IsNotNull(dto.procurement,
                "restore must not consume or clear the dropped section from the DTO");
        }

        [Test]
        public void ContinueDrop_Pin_CoordinatorCapture_NullHolders_WritesNullSections()
        {
            // The 7-argument Capture is the production call shape
            // (SaveLoadManager.BuildSaveDto): procurement service,
            // Pass 9 state, Pass 10 state, D3 state, Millbrook runtime.
            // All null is the fresh-scene Continue shape — a player
            // who Continues and then saves overwrites the good save
            // with null sections (the save-after-Continue amplifier).
            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(
                null, null, null, null, null, null, null);

            Assert.IsNotNull(dto, "capture always returns a DTO, never null");
            Assert.IsNull(dto.procurement,
                "a null procurement service captures a null procurement section");
            Assert.IsNull(dto.series2Pass9,
                "a null Pass 9 state captures a null Pass 9 section");
            Assert.IsNull(dto.millbrookAid,
                "a null Millbrook runtime captures a null Millbrook section");
        }
    }
}
