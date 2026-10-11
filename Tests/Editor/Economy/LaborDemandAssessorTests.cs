using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker A — Phase A (real labor demand). Deterministic assessor tests
    /// (fixed day indices, no randomness). The assessor reports workload in
    /// minutes and derived evidence only; it must never produce headcount
    /// targets, multipliers, or quotas (brief §4; CAN-CE1-03.4).
    /// </summary>
    [TestFixture]
    public sealed class LaborDemandAssessorTests
    {
        private static string StoreId(int seq) =>
            EntityId.For(EntityKind.Business, seq).ToString();

        private static LaborDemandInputs NewInputs(int seq, int dayIndex)
        {
            return new LaborDemandInputs
            {
                BusinessInstanceId = StoreId(seq),
                DayIndex = dayIndex,
                ActiveEmployeeCount = 1,
                AvailableEmployeeMinutes = 480, // one full person-time budget
            };
        }

        private static LaborDemandLine FindLine(LaborDemandReport report, string family)
        {
            foreach (LaborDemandLine line in report.Lines)
            {
                if (line.TaskFamily == family)
                {
                    return line;
                }
            }

            return null;
        }

        [Test]
        public void OpenTasks_AreReportedAsDemandMinutesByFamily()
        {
            var assessor = new LaborDemandAssessor();
            var diag = new List<string>();
            LaborDemandInputs inputs = NewInputs(1, 400);
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = 40;
            inputs.OpenTaskMinutesByFamily["StockShelves"] = 24;
            inputs.OpenTaskMinutesByFamily["UnloadFreight"] = 36;

            LaborDemandReport report = assessor.Assess(inputs, diag);

            Assert.IsNotNull(report);
            Assert.AreEqual(3, report.Lines.Count);
            Assert.AreEqual(40, FindLine(report, "TendCustomer").OpenMinutes);
            Assert.AreEqual(24, FindLine(report, "StockShelves").OpenMinutes);
            Assert.AreEqual(36, FindLine(report, "UnloadFreight").OpenMinutes);
            Assert.AreEqual(100, report.TotalUnmetMinutes);
            Assert.AreEqual("B1", report.BusinessInstanceId);
            Assert.AreEqual(400, report.DayIndex);
            Assert.IsFalse(report.CoverageGap, "100 unmet minutes fit inside one 480-minute budget");
            Assert.IsFalse(report.HiringPressure, "no queue pressure and no coverage gap: no hiring pressure");
        }

        [Test]
        public void QueueBacklogAndBalks_ReportServicePressure_WithoutInventingHeadcount()
        {
            var assessor = new LaborDemandAssessor();
            var diag = new List<string>();
            LaborDemandInputs inputs = NewInputs(2, 401);
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = 60;
            inputs.WaitingCustomers = 8; // line past CustomerQueue.MaxToleratedQueueLength (5)
            inputs.RecentBalkCount = 3;   // recurring missed sales

            LaborDemandReport report = assessor.Assess(inputs, diag);

            Assert.IsTrue(report.HiringPressure, "service pressure: queue at balk tolerance plus recurring balks");
            Assert.IsFalse(report.CoverageGap, "60 minutes fit inside the 480-minute budget");
            Assert.AreEqual(60, report.TotalUnmetMinutes);

            // No headcount invented anywhere: the report and every line expose
            // only minutes, counts, evidence booleans, and text.
            AssertNoHeadcountFields(report);
            foreach (LaborDemandLine line in report.Lines)
            {
                AssertNoHeadcountFields(line);
            }
        }

        [Test]
        public void SameDemandShape_NeverYieldsRequiredWorkerCount()
        {
            var assessor = new LaborDemandAssessor();
            LaborDemandInputs inputs = NewInputs(3, 402);
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = 200;
            inputs.OpenTaskMinutesByFamily["StockShelves"] = 100;
            inputs.RequiresPersonPhaseMinutes = 60;
            inputs.ActiveEmployeeCount = 2;
            inputs.AvailableEmployeeMinutes = 480;

            LaborDemandReport first = assessor.Assess(inputs, new List<string>());
            LaborDemandReport second = assessor.Assess(inputs, new List<string>());

            // Minutes are reported; no "N workers required" shape exists.
            Assert.AreEqual(360, first.TotalUnmetMinutes);
            Assert.AreEqual(360, second.TotalUnmetMinutes);
            AssertNoHeadcountFields(first);

            // Determinism: identical inputs produce identical evidence.
            Assert.AreEqual(first.HiringPressure, second.HiringPressure);
            Assert.AreEqual(first.CoverageGap, second.CoverageGap);
            Assert.AreEqual(first.EvidenceSummary, second.EvidenceSummary);

            // Doubling demand with doubled budgets keeps the SAME evidence
            // shape: minutes and booleans scale with evidence, never a quota.
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = 400;
            inputs.OpenTaskMinutesByFamily["StockShelves"] = 200;
            inputs.RequiresPersonPhaseMinutes = 120;
            inputs.AvailableEmployeeMinutes = 960;
            LaborDemandReport scaled = assessor.Assess(inputs, new List<string>());
            Assert.AreEqual(720, scaled.TotalUnmetMinutes);
            AssertNoHeadcountFields(scaled);
        }

        [Test]
        public void OnePersonBusiness_DemandWithinOneBudget_HasNoHiringPressure()
        {
            var assessor = new LaborDemandAssessor();
            var diag = new List<string>();
            LaborDemandInputs inputs = NewInputs(4, 403);
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = 160;
            inputs.OpenTaskMinutesByFamily["StockShelves"] = 80;
            inputs.ActiveEmployeeCount = 1;
            inputs.AvailableEmployeeMinutes = 480; // one person can do the work

            LaborDemandReport report = assessor.Assess(inputs, diag);

            Assert.AreEqual(240, report.TotalUnmetMinutes);
            Assert.IsFalse(report.CoverageGap);
            Assert.IsFalse(report.HiringPressure, "a one-person business covers its own workload: no pressure");
            Assert.IsTrue(diag.Count > 0, "diagnostics are recorded");
        }

        [Test]
        public void ProductionPersonPhaseMinutes_AreCounted_AsProductionLine()
        {
            var assessor = new LaborDemandAssessor();
            LaborDemandInputs inputs = NewInputs(5, 404);
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = 50;
            inputs.RequiresPersonPhaseMinutes = 90; // e.g. blacksmith fabrication phases

            LaborDemandReport report = assessor.Assess(inputs, new List<string>());

            LaborDemandLine production = FindLine(report, LaborDemandAssessor.ProductionFamilyName);
            Assert.IsNotNull(production, "person-required production minutes get their own line");
            Assert.AreEqual(90, production.OpenMinutes);
            Assert.AreEqual(140, report.TotalUnmetMinutes);
            Assert.IsFalse(report.CoverageGap, "140 unmet minutes fit inside one budget");

            // Person-not-required phases are never reported: the caller only
            // supplies RequiresPerson phase minutes, and the assessor adds no
            // phantom lines beyond its inputs.
            Assert.AreEqual(2, report.Lines.Count);
        }

        [Test]
        public void UnmetMinutesBeyondBudget_SetsCoverageGap()
        {
            var assessor = new LaborDemandAssessor();
            LaborDemandInputs inputs = NewInputs(6, 405);
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = 400;
            inputs.OpenTaskMinutesByFamily["StockShelves"] = 200;
            inputs.ActiveEmployeeCount = 1;
            inputs.AvailableEmployeeMinutes = 480;

            LaborDemandReport report = assessor.Assess(inputs, new List<string>());

            Assert.AreEqual(600, report.TotalUnmetMinutes);
            Assert.IsTrue(report.CoverageGap, "work exists that no available person can cover in time");
            Assert.IsTrue(report.HiringPressure, "coverage gap is derived hiring-pressure evidence");
            AssertNoHeadcountFields(report);
        }

        [Test]
        public void NegativeInputs_AreClampedToZero()
        {
            var assessor = new LaborDemandAssessor();
            var diag = new List<string>();
            LaborDemandInputs inputs = NewInputs(7, 406);
            inputs.OpenTaskMinutesByFamily["TendCustomer"] = -50;
            inputs.RequiresPersonPhaseMinutes = -10;
            inputs.AvailableEmployeeMinutes = 480;

            LaborDemandReport report = assessor.Assess(inputs, diag);

            Assert.AreEqual(0, report.TotalUnmetMinutes);
            Assert.AreEqual(0, FindLine(report, "TendCustomer").OpenMinutes);
            Assert.IsFalse(report.CoverageGap);
            Assert.IsFalse(report.HiringPressure);
        }

        [Test]
        public void NullInputs_ReportZeroDemand_WithoutThrowing()
        {
            var assessor = new LaborDemandAssessor();
            LaborDemandReport report = assessor.Assess(null, null);

            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.TotalUnmetMinutes);
            Assert.IsFalse(report.CoverageGap);
            Assert.IsFalse(report.HiringPressure);
        }

        /// <summary>
        /// Structural guard: the demand report and its lines expose only
        /// minutes, counts, evidence booleans, and text. Any future
        /// headcount/quota/multiplier field fails this test by name.
        /// </summary>
        private static void AssertNoHeadcountFields(object target)
        {
            Assert.IsNotNull(target);
            foreach (FieldInfo field in target.GetType().GetFields())
            {
                string name = field.Name.ToLowerInvariant();
                Assert.IsFalse(
                    name.Contains("worker") || name.Contains("headcount") || name.Contains("staff")
                    || name.Contains("quota") || name.Contains("multiplier") || name.Contains("required")
                    || name.Contains("hire") || name.Contains("recruit"),
                    "report must never invent staffing fields; found: " + field.Name);
            }
        }
    }
}
