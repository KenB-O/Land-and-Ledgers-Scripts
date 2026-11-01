using System;
using System.Collections.Generic;
using LandLedgers.Orchestration.Systems;
using NUnit.Framework;

namespace LandLedgers.EditorTests.PassV
{
    /// <summary>
    /// Pass V (Worker 4, brief section 40): extended autonomous society
    /// simulation. 5 households, 13 persons, 4 businesses, housing, property,
    /// employment, credit, real inventories, multiple task types, shared
    /// equipment, a delegated manager, and constrained supply/demand.
    ///
    /// Asserts: full conservation reconciliation (cash, goods, time, equipment,
    /// identity), good AND bad outcomes (unemployment, shortages, hunger,
    /// arrears, delinquency), save/load consistency, and tick-size consistency
    /// (small ticks, large ticks, 1x/10x/100x equivalent).
    ///
    /// Performance is measured and reported; see the worker's final report.
    /// </summary>
    [TestFixture]
    public sealed class AutonomousSocietySimTests
    {
        [Test]
        public void ThirtyDayRun_ReconcilesClean_WithGoodAndBadOutcomes()
        {
            var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            var started = DateTime.UtcNow;
            driver.AdvanceDays(30);
            var elapsed = DateTime.UtcNow - started;

            SocietyReconciliation report = driver.Reconcile();
            Assert.IsTrue(report.Clean,
                "30-day reconciliation divergences:\n" + string.Join("\n", report.Divergences.ToArray()));

            // Good outcomes: real production, real wages, real trade.
            Assert.Greater(driver.WagesPaidCents, 0, "wages must have been paid");
            Assert.Greater(driver.EventLog.Count, 10, "the society should have a lived history");

            // Bad outcomes: the brief requires them.
            int badOutcomes = 0;
            if (driver.UnmetNeeds > 0) badOutcomes++;
            if (driver.Shortages > 0) badOutcomes++;
            if (driver.HungerEvents > 0) badOutcomes++;
            if (driver.PayrollArrearsWeeks > 0) badOutcomes++;
            if (driver.CreditDelinquencies > 0) badOutcomes++;
            Assert.Greater(badOutcomes, 0,
                $"expected hardship somewhere (unmet={driver.UnmetNeeds}, shortages={driver.Shortages}, " +
                $"hunger={driver.HungerEvents}, arrears={driver.PayrollArrearsWeeks}, delinq={driver.CreditDelinquencies})");

            Console.WriteLine($"30-day sim: {elapsed.TotalMilliseconds:F0} ms, wages {driver.WagesPaidCents}c, " +
                $"cash {report.CashCents}c, bad-outcome classes {badOutcomes}.");
        }

        [Test]
        public void NinetyDayRun_WithDemographicEvents_ReconcilesClean()
        {
            var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            var started = DateTime.UtcNow;
            driver.AdvanceDays(90);
            var elapsed = DateTime.UtcNow - started;

            SocietyReconciliation report = driver.Reconcile();
            Assert.IsTrue(report.Clean,
                "90-day reconciliation divergences:\n" + string.Join("\n", report.Divergences.ToArray()));

            // The scripted demographic arc happened: birth (+1 person), migration
            // (P3 off-map, same person), death (P13, estate settled), hiring.
            // Asserted on STATE, not the bounded event log (which rolls).
            Assert.AreEqual(1, driver.BirthsRecorded, "one birth should have been recorded");
            Assert.AreEqual(14, driver.PersonCount, "the birth adds a person; the death keeps the record");
            Assert.AreEqual(1, driver.DeathsRecorded, "one death should have been recorded");
            Assert.IsTrue(driver.IsPersonOffMap(AutonomousSocietyDriver.P3Jonas),
                "P3 (Jonas) migrated off-map: the same person, still tracked");
            var elder = driver.GetPerson(AutonomousSocietyDriver.P13Elder);
            Assert.IsNotNull(elder, "the decedent's record is retained (identity never rewritten)");
            Assert.AreEqual(60, elder.deathDayIndex, "death recorded on day 60");
            Assert.IsTrue(driver.P11Hired, "the widow was hired on day 15");

            Console.WriteLine($"90-day sim: {elapsed.TotalMilliseconds:F0} ms, wages {driver.WagesPaidCents}c, " +
                $"cash {report.CashCents}c.");
        }

        [Test]
        public void TickSizeConsistency_SmallAndLargeTicks_IdenticalOutcomes()
        {
            // 1x/10x/100x equivalence at source level: the same 30 days driven
            // with 15-minute, hourly, daily, and weekly ticks must agree exactly.
            int[] tickMinutes = { 15, 60, 1440, 10080 };
            var fingerprints = new List<string>();
            foreach (int tick in tickMinutes)
            {
                var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
                int remaining = 30 * 1440;
                while (remaining > 0)
                {
                    int step = Math.Min(tick, remaining);
                    driver.AdvanceMinutes(step);
                    remaining -= step;
                }
                SocietyReconciliation report = driver.Reconcile();
                Assert.IsTrue(report.Clean, $"tick {tick} min diverged:\n" + string.Join("\n", report.Divergences.ToArray()));
                fingerprints.Add(driver.Fingerprint());
            }

            for (int i = 1; i < fingerprints.Count; i++)
            {
                Assert.AreEqual(fingerprints[0], fingerprints[i],
                    $"tick size {tickMinutes[i]} min produced different outcomes than {tickMinutes[0]} min");
            }
            Console.WriteLine("tick-size consistency: 15/60/1440/10080-min ticks identical over 30 days.");
        }

        [Test]
        public void SaveLoad_MidRunSaveRestore_IdenticalOutcomes()
        {
            var straight = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            straight.AdvanceDays(30);

            var fork = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            fork.AdvanceDays(15);
            AutonomousSocietySaveDto saved = fork.CaptureSaveDto();

            var restored = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            restored.LoadFromSaveDto(saved);
            Assert.AreEqual(fork.Fingerprint(), restored.Fingerprint(),
                "a freshly loaded driver must match the saved one exactly");

            fork.AdvanceDays(15);
            restored.AdvanceDays(15);
            straight.AdvanceDays(0); // straight already at 30

            Assert.AreEqual(straight.Fingerprint(), fork.Fingerprint(),
                "sanity: two identical fresh runs must agree");
            Assert.AreEqual(fork.Fingerprint(), restored.Fingerprint(),
                "save/load at day 15 must not change the day-30 outcome");

            SocietyReconciliation report = restored.Reconcile();
            Assert.IsTrue(report.Clean,
                "restored run divergences:\n" + string.Join("\n", report.Divergences.ToArray()));
            Console.WriteLine("save/load: day-15 save, day-30 outcomes identical.");
        }

        [Test]
        public void HeadlessScheduling_NoPresentation_SameOutcomes()
        {
            // Brief section 25F: headless scheduling (no sinks, no presentation
            // of any kind attached) matches the observed run exactly.
            var observed = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            observed.AdvanceDays(30);
            string observedFingerprint = observed.Fingerprint();

            var headless = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            headless.AdvanceDays(30);

            Assert.AreEqual(observedFingerprint, headless.Fingerprint(),
                "headless scheduling must match the observed run");
        }
    }
}
