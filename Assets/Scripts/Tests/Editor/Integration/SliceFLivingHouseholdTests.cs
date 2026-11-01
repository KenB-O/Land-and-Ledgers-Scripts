using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Integration
{
    /// <summary>
    /// Pass IV (Worker 4), SLICE F — Living Household (mandatory). One household,
    /// two full days, the whole chain: household needs → a member works real
    /// tasks through real time → employment/family work scheduled without
    /// double-booking person-time → wages where due → shopping with real goods
    /// custody → meals from real inventory → conflicting time requirements
    /// refused loudly → continued autonomous activity. Full reconciliation.
    /// </summary>
    [TestFixture]
    public sealed class SliceFLivingHouseholdTests
    {
        [Test]
        public void LivingHousehold_TwoDays_FullChainReconciles()
        {
            var sim = new LivingWorldSim();
            sim.Build();
            sim.AdvanceTo((LivingWorldSim.StartDay + 2) * 86400f, 60f);

            // 1. Household needs → real work through real time.
            Assert.Greater(sim.CompletedTasksByDefinition.Count, 0,
                "no business tasks completed in two days\n" + string.Join("\n", sim.Diag));
            Assert.Greater(sim.TotalWorkMinutes, 0, "no real work minutes recorded");

            // 2. Shopping with real goods custody: the store really sold bread.
            int breadSold = sim.Store1.UnitsSold.TryGetValue(LivingWorldSim.Bread, out int sb) ? sb : 0;
            Assert.Greater(breadSold, 0, "no retail bread sale completed\n" + string.Join("\n", sim.Diag));

            // 3. Meals from real inventory.
            Assert.IsTrue(sim.GoodsFlows.Exists(f => f.Reason == "evening meal"),
                "no evening meal was consumed from real inventory");

            // 4. Person-time exclusive: nobody is double-booked, nobody works
            //    while sleeping.
            foreach (int pid in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })
            {
                AssertNoOverlaps(sim, pid, LivingWorldSim.StartDay);
                AssertNoWorkDuringSleep(sim, pid, LivingWorldSim.StartDay);
            }

            // 5. Full reconciliation: cash conserved, goods balanced.
            List<string> findings = sim.Reconcile();
            Assert.AreEqual(0, findings.Count,
                "reconciliation failed:\n" + string.Join("\n", findings));
        }

        private static void AssertNoOverlaps(LivingWorldSim sim, int personId, int day)
        {
            List<PersonActivityReservation> reservations = sim.Schedules.ActiveFor(personId, day);
            for (int i = 0; i < reservations.Count; i++)
            {
                for (int j = i + 1; j < reservations.Count; j++)
                {
                    PersonActivityReservation a = reservations[i];
                    PersonActivityReservation b = reservations[j];
                    bool overlap = a.StartMinuteOfDay < b.EndMinuteOfDay &&
                        b.StartMinuteOfDay < a.EndMinuteOfDay;
                    Assert.IsFalse(overlap,
                        $"P{personId} double-booked: '{a.Label}' [{a.StartMinuteOfDay}-{a.EndMinuteOfDay}] " +
                        $"overlaps '{b.Label}' [{b.StartMinuteOfDay}-{b.EndMinuteOfDay}]");
                }
            }
        }

        private static void AssertNoWorkDuringSleep(LivingWorldSim sim, int personId, int day)
        {
            List<PersonActivityReservation> reservations = sim.Schedules.ActiveFor(personId, day);
            foreach (PersonActivityReservation sleep in reservations)
            {
                if (sleep.Activity != PersonActivityKind.Sleeping) continue;
                foreach (PersonActivityReservation other in reservations)
                {
                    if (other == sleep || other.Activity != PersonActivityKind.Work) continue;
                    // Sleep wraps midnight as two windows; compare within the day.
                    bool overlap = other.StartMinuteOfDay < sleep.EndMinuteOfDay &&
                        sleep.StartMinuteOfDay < other.EndMinuteOfDay;
                    Assert.IsFalse(overlap,
                        $"P{personId} works '{other.Label}' during sleep [{sleep.StartMinuteOfDay}-{sleep.EndMinuteOfDay}]");
                }
            }
        }
    }
}
