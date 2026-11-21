// Land & Ledgers — World Accessibility and Interaction Auditor tests (Lane 7, Tool 06).
//
// Known cases reproduced from the current tree's source-reviewed
// evidence (no run exists on this HEAD): the Firewood gap (B-03),
// stove-less households (M-13), the three Agent M regimes, the two
// Regime B gate holes, and the "[StartupAccess] " gap wording.

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class WorldAccessibilityAuditorTests
    {
        private static AccessPointCheck Check(string kind, bool found, bool reachable, bool requiresRoute, int buildingId)
        {
            return new AccessPointCheck
            {
                BuildingLabel = "Home 'test' (household 1, building " + buildingId + ")",
                BuildingId = buildingId,
                HouseholdId = 1,
                PointKind = kind,
                Found = found,
                ReachableFromMainEntrance = reachable,
                RequiresInteriorRoute = requiresRoute,
                LocationId = found ? kind.ToLowerInvariant() + "-point" : string.Empty,
                Reason = found ? string.Empty : "No reachable " + kind + " activity is authored.",
            };
        }

        // --- Gap classification -------------------------------------------------

        [Test]
        public void FirewoodGap_IsMissing_Class1()
        {
            // B-03: zero Firewood-type locations anywhere; fuel trips can never run.
            AccessPointCheck firewood = Check("Firewood", false, false, false, 3);
            Assert.AreEqual(AccessGapKind.Missing, WorldAccessibilityAuditor.ClassifyGap(firewood));
            Assert.AreEqual(RefusalCauseClass.DestinationMissing, WorldAccessibilityAuditor.RefusalClassFor(firewood));
        }

        [Test]
        public void AuthoredButUnreachableStove_IsInaccessible_Class2()
        {
            AccessPointCheck stove = Check("Stove", true, false, true, 3);
            Assert.AreEqual(AccessGapKind.Unreachable, WorldAccessibilityAuditor.ClassifyGap(stove));
            Assert.AreEqual(RefusalCauseClass.DestinationInaccessible, WorldAccessibilityAuditor.RefusalClassFor(stove));
        }

        [Test]
        public void CleanHome_HasNoGaps()
        {
            var checks = new List<AccessPointCheck>
            {
                Check("Stove", true, true, true, 3),
                Check("Cleaning", true, true, true, 3),
                Check("Firewood", true, false, false, 3),
                Check("Dining", true, true, true, 3),
            };
            WorldAccessibilityReport report = WorldAccessibilityAuditor.BuildReport(checks);
            Assert.AreEqual(0, report.Gaps.Count);
            Assert.AreEqual(0, report.MissingCount);
            Assert.AreEqual(0, report.UnreachableCount);
        }

        [Test]
        public void ExteriorFirewood_NeedsPresenceOnly()
        {
            // Firewood is exterior: presence suffices, no interior route required.
            AccessPointCheck firewood = Check("Firewood", true, false, false, 3);
            Assert.AreEqual(AccessGapKind.None, WorldAccessibilityAuditor.ClassifyGap(firewood));
        }

        [Test]
        public void GapLine_MatchesStartupAccessWording()
        {
            AccessPointCheck firewood = Check("Firewood", false, false, false, 3);
            string line = WorldAccessibilityAuditor.GapLine(firewood.BuildingLabel, firewood);
            Assert.IsTrue(line.Contains("has no Firewood activity point authored"));
            Assert.IsTrue(line.StartsWith("Home 'test'"));
        }

        [Test]
        public void Report_CountsMissingAndUnreachableSeparately()
        {
            var checks = new List<AccessPointCheck>
            {
                Check("Firewood", false, false, false, 3),
                Check("Cleaning", false, false, true, 3),
                Check("Stove", true, false, true, 3),
                Check("Dining", true, true, true, 3),
            };
            WorldAccessibilityReport report = WorldAccessibilityAuditor.BuildReport(checks);
            Assert.AreEqual(3, report.Gaps.Count);
            Assert.AreEqual(2, report.MissingCount);
            Assert.AreEqual(1, report.UnreachableCount);
            foreach (AccessGap gap in report.Gaps)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(gap.Detail));
                Assert.IsFalse(string.IsNullOrWhiteSpace(gap.Authority));
                Assert.IsFalse(string.IsNullOrWhiteSpace(gap.Evidence));
            }
        }

        // --- Config inventory (M-13 re-verified) ---------------------------------

        [Test]
        public void KnownConfigInventory_FirewoodZero_CounterOne_ReceivingOne()
        {
            IReadOnlyList<ConfigInventoryFact> inventory = WorldAccessibilityAuditor.KnownConfigInventory();
            Assert.AreEqual(0, inventory.First(f => f.PointKind == "Firewood").AuthoredCount);
            Assert.AreEqual(0, inventory.First(f => f.PointKind == "Cleaning").AuthoredCount);
            Assert.AreEqual(1, inventory.First(f => f.PointKind == "Counter").AuthoredCount);
            Assert.AreEqual(1, inventory.First(f => f.PointKind == "Receiving").AuthoredCount);
            Assert.AreEqual(12, inventory.First(f => f.PointKind == "Stove").AuthoredCount);
            Assert.AreEqual(33, inventory.First(f => f.PointKind == "Bed").AuthoredCount);
            Assert.AreEqual(19, inventory.First(f => f.PointKind == "Dining").AuthoredCount);
            Assert.IsTrue(inventory.First(f => f.PointKind == "Stove").Note.Contains("Shane Bryan"));
            Assert.IsTrue(inventory.First(f => f.PointKind == "Stove").Note.Contains("Millward"));
        }

        [Test]
        public void BuildingGateHoles_AreTwo()
        {
            Assert.AreEqual(2, WorldAccessibilityAuditor.BuildingGateHoles.Length);
            Assert.IsTrue(WorldAccessibilityAuditor.BuildingGateHoles[0].Contains("Work reservation"));
            Assert.IsTrue(WorldAccessibilityAuditor.BuildingGateHoles[1].Contains("CustomerRef"));
        }

        // --- Regime classification (Agent M §3) -----------------------------------

        [Test]
        public void ClassifyTaskFamily_DomesticMealPrep_IsRegimeA()
        {
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily("domestic.meal-prep");
            Assert.AreEqual(AccessRegime.PhysicalArrivalAtItem, regime.Regime);
            Assert.IsTrue(regime.DestinationExpectation.Contains("MISSING"));
        }

        [Test]
        public void ClassifyTaskFamily_TendCustomer_IsRegimeC_Timer()
        {
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily("gs.tend-customer");
            Assert.AreEqual(AccessRegime.TimerNoDestination, regime.Regime);
            Assert.IsTrue(regime.DestinationExpectation.Contains("timer"));
        }

        [Test]
        public void ClassifyTaskFamily_FreightReceiving_IsRegimeB()
        {
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily("fr.receive-freight");
            Assert.AreEqual(AccessRegime.BuildingArrivalOnly, regime.Regime);
        }

        [Test]
        public void ClassifyTaskFamily_FreightLoading_IsRegimeC()
        {
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily("fr.load-freight");
            Assert.AreEqual(AccessRegime.TimerNoDestination, regime.Regime);
            Assert.IsTrue(regime.DestinationExpectation.Contains("60 minutes"));
        }

        [Test]
        public void ClassifyTaskFamily_ConstructionLabor_IsRegimeC()
        {
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily("construction-labor:framing");
            Assert.AreEqual(AccessRegime.TimerNoDestination, regime.Regime);
        }

        [Test]
        public void ClassifyTaskFamily_BakeryStage_IsRegimeC_ReservationOnly()
        {
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily("bakery.bake");
            Assert.AreEqual(AccessRegime.TimerNoDestination, regime.Regime);
            Assert.IsTrue(regime.DestinationExpectation.Contains("reservation"));
        }

        [Test]
        public void ClassifyTaskFamily_UnknownFamily_IsUnknown_NeverGuessed()
        {
            TaskFamilyRegime regime = WorldAccessibilityAuditor.ClassifyTaskFamily("something.unheard-of");
            Assert.AreEqual(AccessRegime.Unknown, regime.Regime);
            Assert.IsTrue(regime.DestinationExpectation.Contains("does not guess"));
        }

        [Test]
        public void RegimeLabel_NamesAllThreeRegimes()
        {
            Assert.IsTrue(WorldAccessibilityAuditor.RegimeLabel(AccessRegime.PhysicalArrivalAtItem).StartsWith("A"));
            Assert.IsTrue(WorldAccessibilityAuditor.RegimeLabel(AccessRegime.BuildingArrivalOnly).StartsWith("B"));
            Assert.IsTrue(WorldAccessibilityAuditor.RegimeLabel(AccessRegime.TimerNoDestination).StartsWith("C"));
        }
    }
}
