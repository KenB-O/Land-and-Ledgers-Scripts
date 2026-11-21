// Land & Ledgers — Regression Comparator tests (Lane 8, Tool 11, Agent N
// §3.8).
//
// The comparability gate is binding: the retained 60-day run is BASELINE
// SHAPE ONLY, never a same-source comparator. A disappeared signature
// without repair attribution is UNEXPLAINED, never silently a repair.

using System.Collections.Generic;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class RegressionComparatorTests
    {
        private static RunIdentity SameSourceIdentity()
        {
            return new RunIdentity
            {
                RunId = "run-a",
                Seed = 1886,
                Scenario = "millbrook-development-town",
                StartupMode = "NewGame",
                TargetDays = 15,
                CompletedDays = 15,
                Speed = "Speed10x",
                Branch = "muse/muse-pass-2026-11-20",
                CommitSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                DirtyTree = false,
            };
        }

        private static RunSummary SummaryWith(RunIdentity identity, int mealsEaten, int mealsMissed, int warnings, int errors)
        {
            var summary = new RunSummary
            {
                RunId = identity.RunId,
                Identity = identity,
                MealsEaten = mealsEaten,
                MealsMissed = mealsMissed,
                WarningCount = warnings,
                ErrorCount = errors,
            };
            return summary;
        }

        // --- Gate -----------------------------------------------------------------

        [Test]
        public void Gate_ComparableWhenSameSourceState()
        {
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                SameSourceIdentity(), SameSourceIdentity(), false, false, out notes);
            Assert.AreEqual(Comparability.Comparable, comparability);
            Assert.IsTrue(notes[0].Contains("Comparable"));
        }

        [Test]
        public void Gate_RefusesDifferentSeed()
        {
            RunIdentity candidate = SameSourceIdentity();
            candidate.Seed = 1887;
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                SameSourceIdentity(), candidate, false, false, out notes);
            Assert.AreEqual(Comparability.Refused, comparability);
            Assert.IsTrue(notes[0].Contains("seeds differ"));
        }

        [Test]
        public void Gate_RefusesDifferentTargetDays()
        {
            RunIdentity candidate = SameSourceIdentity();
            candidate.TargetDays = 60;
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                SameSourceIdentity(), candidate, false, false, out notes);
            Assert.AreEqual(Comparability.Refused, comparability);
            Assert.IsTrue(notes[0].Contains("target days differ"));
        }

        [Test]
        public void Gate_RefusesDirtyAgainstCleanTree()
        {
            RunIdentity candidate = SameSourceIdentity();
            candidate.DirtyTree = true;
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                SameSourceIdentity(), candidate, false, false, out notes);
            Assert.AreEqual(Comparability.Refused, comparability);
            Assert.IsTrue(notes[0].Contains("dirty tree"));
        }

        [Test]
        public void Gate_RefusesUnrecordedSpeedDifference()
        {
            RunIdentity candidate = SameSourceIdentity();
            candidate.Speed = "Speed250x";
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                SameSourceIdentity(), candidate, false, false, out notes);
            Assert.AreEqual(Comparability.Refused, comparability);
            Assert.IsTrue(notes[0].Contains("speeds differ"));
        }

        [Test]
        public void Gate_RecordedSpeedDifferenceIsComparableWithCaveat()
        {
            RunIdentity candidate = SameSourceIdentity();
            candidate.Speed = "Speed250x";
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                SameSourceIdentity(), candidate, true, false, out notes);
            Assert.AreEqual(Comparability.Comparable, comparability);
            Assert.IsTrue(notes[0].Contains("tick-granularity caveat"));
        }

        [Test]
        public void Gate_BaselineShapeOnlyForRetained60DayRun()
        {
            RunIdentity baseline = RunIdentity.Retained60DayBaseline();
            RunIdentity candidate = SameSourceIdentity();
            candidate.TargetDays = 60;
            candidate.Speed = "Speed250x";
            candidate.DirtyTree = true;
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                baseline, candidate, false, false, out notes);
            Assert.AreEqual(Comparability.BaselineShapeOnly, comparability);
            Assert.IsTrue(notes[0].Contains("BASELINE SHAPE ONLY"));
            Assert.IsTrue(notes[0].Contains("Never 'before/after'"));
        }

        [Test]
        public void Gate_SourceChangeAsSubjectIsComparable()
        {
            RunIdentity candidate = SameSourceIdentity();
            candidate.CommitSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            List<string> notes;
            Comparability comparability = RegressionComparator.CheckComparability(
                SameSourceIdentity(), candidate, false, true, out notes);
            Assert.AreEqual(Comparability.Comparable, comparability);
            Assert.IsTrue(notes[0].Contains("explicit subject"));
        }

        [Test]
        public void Retained60DayBaseline_IdentityMatchesMetadata()
        {
            RunIdentity baseline = RunIdentity.Retained60DayBaseline();
            Assert.AreEqual("20261120T192311Z-343a8cd2", baseline.RunId);
            Assert.AreEqual(1886, baseline.Seed);
            Assert.AreEqual("millbrook-development-town", baseline.Scenario);
            Assert.AreEqual("NewGame", baseline.StartupMode);
            Assert.AreEqual(60, baseline.TargetDays);
            Assert.AreEqual("Speed250x", baseline.Speed);
            Assert.AreEqual("8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8", baseline.CommitSha);
            Assert.IsTrue(baseline.DirtyTree);
        }

        // --- Comparison -------------------------------------------------------------

        [Test]
        public void Compare_Refused_YieldsNoDeltas()
        {
            RunIdentity candidateIdentity = SameSourceIdentity();
            candidateIdentity.Seed = 1;
            RunComparisonResult result = RegressionComparator.Compare(
                SummaryWith(SameSourceIdentity(), 100, 10, 5, 0),
                SummaryWith(candidateIdentity, 200, 0, 1, 0),
                false, false, null);
            Assert.AreEqual(Comparability.Refused, result.Comparability);
            Assert.AreEqual(0, result.SignatureDeltas.Count);
            Assert.IsFalse(result.IsBeforeAfter);
            Assert.IsTrue(result.ToMarkdown().Contains("REFUSED"));
        }

        [Test]
        public void Compare_ReportsOutcomeDeltas()
        {
            RunComparisonResult result = RegressionComparator.Compare(
                SummaryWith(SameSourceIdentity(), 100, 50, 10, 1),
                SummaryWith(SameSourceIdentity(), 300, 5, 4, 0),
                false, false, null);
            Assert.AreEqual(Comparability.Comparable, result.Comparability);
            Assert.IsTrue(result.IsBeforeAfter);
            Assert.AreEqual(200, result.MealsEatenDelta);
            Assert.AreEqual(-45, result.MealsMissedDelta);
            Assert.AreEqual(-6, result.WarningDelta);
            Assert.AreEqual(-1, result.ErrorDelta);
        }

        [Test]
        public void Compare_DisappearedSignatureWithoutAttribution_IsUnexplained()
        {
            RunSummary baseline = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            baseline.Signatures.Add(new RunSignatureCount("Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor N.", string.Empty, 2878));
            RunSummary candidate = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            RunComparisonResult result = RegressionComparator.Compare(baseline, candidate, false, false, null);
            Assert.AreEqual(1, result.SignatureDeltas.Count);
            Assert.IsTrue(result.SignatureDeltas[0].Disappeared);
            Assert.IsTrue(result.SignatureDeltas[0].DisappearedUnexplained);
            Assert.AreEqual(1, result.UnexplainedDisappearances);
            Assert.IsTrue(result.ToMarkdown().Contains("UNEXPLAINED"));
        }

        [Test]
        public void Compare_DisappearedSignatureWithAttribution_IsExplained()
        {
            RunSummary baseline = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            baseline.Signatures.Add(new RunSignatureCount("sig", "building:N", 850));
            RunSummary candidate = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            var attributions = new List<RepairAttribution>
            {
                new RepairAttribution("sig", "building:N", "B-01 seller-location authoring", "Assets/Scripts/Domains/World/Buildings/BuildingFootprintAuthority.cs:2709"),
            };
            RunComparisonResult result = RegressionComparator.Compare(baseline, candidate, false, false, attributions);
            Assert.AreEqual(0, result.UnexplainedDisappearances);
            Assert.IsTrue(result.ToMarkdown().Contains("removed by B-01 seller-location authoring"));
        }

        [Test]
        public void Compare_NewSignature_IsNewRegressionCandidate()
        {
            RunSummary baseline = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            RunSummary candidate = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            candidate.Signatures.Add(new RunSignatureCount("brand new failure", string.Empty, 3));
            RunComparisonResult result = RegressionComparator.Compare(baseline, candidate, false, false, null);
            Assert.AreEqual(1, result.NewSignatures);
            Assert.IsTrue(result.SignatureDeltas[0].IsNew);
            Assert.IsTrue(result.ToMarkdown().Contains("NEW REGRESSION candidate"));
        }

        [Test]
        public void Compare_SignaturesGroupedByDestinationNotSignatureAlone()
        {
            RunSummary baseline = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            baseline.Signatures.Add(new RunSignatureCount("abort", "building:N", 100));
            baseline.Signatures.Add(new RunSignatureCount("abort", "building:M", 50));
            RunSummary candidate = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            candidate.Signatures.Add(new RunSignatureCount("abort", "building:N", 0 + 0));
            candidate.Signatures.Add(new RunSignatureCount("abort", "building:M", 50));
            RunComparisonResult result = RegressionComparator.Compare(baseline, candidate, false, false, null);
            Assert.AreEqual(2, result.SignatureDeltas.Count);
            Assert.AreEqual(-100, result.SignatureDeltas[0].Delta);
            Assert.AreEqual(0, result.SignatureDeltas[1].Delta);
        }

        [Test]
        public void Compare_MealPathPurchasesNotCaptured_IsNoted()
        {
            RunSummary baseline = SummaryWith(SameSourceIdentity(), 0, 0, 0, 0);
            baseline.MealPathPurchasesCaptured = false;
            RunComparisonResult result = RegressionComparator.Compare(
                baseline, SummaryWith(SameSourceIdentity(), 0, 0, 0, 0), false, false, null);
            Assert.AreEqual(Comparability.Comparable, result.Comparability);
            Assert.IsTrue(result.ToMarkdown().Contains("mealPathPurchases is NOT_CAPTURED"));
        }
    }
}
