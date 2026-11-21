// Land & Ledgers — Save/Continue State Comparator tests (Lane 7, Tool 07).
//
// Known cases reproduced from the current tree's source-reviewed
// evidence (no run exists on this HEAD — no Continue round-trip has
// been observed): the Agent F §5 persistence table, an actual ActiveProcesses
// loss outcome (which must still be caught), the duplicated-settled-lot defect, the Lane 9a
// legitimate staple_food transformation, and the Lane 9f loud refusal.

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class SaveContinueStateComparatorTests
    {
        private static PersistedField Field(string system, string field, string value, int count)
        {
            return new PersistedField { System = system, Field = field, ValueSummary = value, Count = count, Present = true };
        }

        [Test]
        public void KnownPersistence_CoversBusinessActiveProcessesButKeepsRuntimeComparison()
        {
            IReadOnlyList<PersistenceExpectation> table = SaveContinueStateComparator.KnownPersistence();
            Assert.IsTrue(table.Any(e => e.System == "Furniture" && e.Field == "Purchase orders" && e.Persisted));
            Assert.IsTrue(table.Any(e => e.System == "Freight" && e.Field == "Shipments" && e.Persisted));
            Assert.IsTrue(table.Any(e => e.System == "Business" && e.Field.Contains("Customer queues") && e.Persisted));
            PersistenceExpectation active = table.First(e => e.Field.Contains("ActiveProcesses"));
            Assert.IsTrue(active.Persisted);
            Assert.IsTrue(active.Authority.Contains("BusinessInstanceState.CaptureSaveDto"));
            Assert.IsTrue(active.Note.Contains("Main Scene fresh-scene Continue"));
        }

        [Test]
        public void CompareField_EqualSnapshots_AreEqual()
        {
            StateFieldComparison comparison = SaveContinueStateComparator.CompareField(
                Field("Furniture", "Purchase orders", "3 orders", 3),
                Field("Furniture", "Purchase orders", "3 orders", 3));
            Assert.AreEqual(StateComparisonOutcome.Equal, comparison.Outcome);
            Assert.IsFalse(comparison.IsDefect);
        }

        [Test]
        public void CompareField_ActiveProcessesLost_IsLostDefect_NamingFR2()
        {
            // Even with a source DTO path, any observed loss remains a defect.
            StateFieldComparison comparison = SaveContinueStateComparator.CompareField(
                Field("Production", "ActiveProcesses", "batch furniture-7 phase 2, inputs consumed", 1),
                new PersistedField { System = "Production", Field = "ActiveProcesses", Present = false });
            Assert.AreEqual(StateComparisonOutcome.Lost, comparison.Outcome);
            Assert.IsTrue(comparison.IsDefect);
        }

        [Test]
        public void CompareField_DuplicatedSettledLot_IsDuplicatedDefect()
        {
            StateFieldComparison comparison = SaveContinueStateComparator.CompareField(
                Field("Business", "Retail lots", "lot bread-1 x24", 1),
                Field("Business", "Retail lots", "lot bread-1 x24 (twice)", 2));
            Assert.AreEqual(StateComparisonOutcome.Duplicated, comparison.Outcome);
            Assert.IsTrue(comparison.IsDefect);
        }

        [Test]
        public void CompareField_LegacyStapleFoodFlagged_IsLegitimateTransformation()
        {
            // Lane 9a: legacy unspecified lots are flagged unidentified, never silently converted.
            StateFieldComparison comparison = SaveContinueStateComparator.CompareField(
                Field("HouseholdInventory", "UnspecifiedStapleFoodLots", "staple_food lot x10 (edible)", 1),
                Field("HouseholdInventory", "UnspecifiedStapleFoodLots", "staple_food lot x10 (flagged unidentified, cannot serve)", 1));
            Assert.AreEqual(StateComparisonOutcome.LegitimateTransformation, comparison.Outcome);
            Assert.IsFalse(comparison.IsDefect);
            Assert.IsTrue(comparison.Detail.Contains("Lane 9a"));
        }

        [Test]
        public void CompareField_UnexplainedChange_IsChangedDefect()
        {
            StateFieldComparison comparison = SaveContinueStateComparator.CompareField(
                Field("Money", "Financial obligations", "obligation A 500c", 1),
                Field("Money", "Financial obligations", "obligation A 900c", 1));
            Assert.AreEqual(StateComparisonOutcome.Changed, comparison.Outcome);
            Assert.IsTrue(comparison.IsDefect);
        }

        [Test]
        public void CompareField_AppearedState_IsDefect_NeverAssumedLegitimate()
        {
            StateFieldComparison comparison = SaveContinueStateComparator.CompareField(
                new PersistedField { System = "Money", Field = "Financial obligations", Present = false },
                Field("Money", "Financial obligations", "obligation B 300c", 1));
            Assert.AreEqual(StateComparisonOutcome.Appeared, comparison.Outcome);
            Assert.IsTrue(comparison.IsDefect);
        }

        [Test]
        public void CompareField_LoudRefusal_IsFlagged_NeverAPass()
        {
            // Lane 9f: RestoreProcess refuses loudly instead of silently losing the batch.
            var post = Field("Production", "ActiveProcesses", "batch furniture-7 phase 2", 1);
            post.RefusedLoudly = true;
            post.RefusalReason = "Phase task rebind refused: no open task for phase 2.";
            StateFieldComparison comparison = SaveContinueStateComparator.CompareField(
                Field("Production", "ActiveProcesses", "batch furniture-7 phase 2", 1), post);
            Assert.AreEqual(StateComparisonOutcome.RefusedLoudly, comparison.Outcome);
            Assert.IsFalse(comparison.IsDefect);
            Assert.IsTrue(comparison.Detail.Contains("NOT restored"));
        }

        [Test]
        public void Compare_ProductionFields_ComeFirst()
        {
            var pre = new List<PersistedField>
            {
                Field("Furniture", "Purchase orders", "3 orders", 3),
                Field("Production", "ActiveProcesses", "batch furniture-7 phase 2", 1),
                Field("Freight", "Shipments", "2 shipments", 2),
            };
            var post = new List<PersistedField>
            {
                Field("Furniture", "Purchase orders", "3 orders", 3),
                Field("Production", "ActiveProcesses", "batch furniture-7 phase 2", 1),
                Field("Freight", "Shipments", "2 shipments", 2),
            };
            SaveContinueComparisonReport report = SaveContinueStateComparator.Compare(pre, post);
            Assert.AreEqual(0, report.DefectCount);
            Assert.AreEqual("Production", report.Comparisons[0].System);
        }

        [Test]
        public void Compare_CountsDefectsRefusalsAndTransformations()
        {
            var pre = new List<PersistedField>
            {
                Field("Production", "ActiveProcesses", "batch furniture-7 phase 2", 1),
                Field("HouseholdInventory", "UnspecifiedStapleFoodLots", "staple_food lot x10 (edible)", 1),
            };
            var refused = Field("Production", "ActiveProcesses", "batch furniture-7 phase 2", 1);
            refused.RefusedLoudly = true;
            refused.RefusalReason = "No open task for phase 2.";
            var post = new List<PersistedField>
            {
                refused,
                Field("HouseholdInventory", "UnspecifiedStapleFoodLots", "staple_food lot x10 (flagged unidentified, cannot serve)", 1),
            };
            SaveContinueComparisonReport report = SaveContinueStateComparator.Compare(pre, post);
            Assert.AreEqual(0, report.DefectCount);
            Assert.AreEqual(1, report.RefusedLoudlyCount);
            Assert.AreEqual(1, report.LegitimateTransformationCount);
        }
    }
}
