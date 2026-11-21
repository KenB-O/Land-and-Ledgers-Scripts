// Land & Ledgers — World Accessibility and Interaction Auditor tests (Lane 7, Tool 06).
//
// Historical classification cases plus current source-authored asset
// checks: the pre-fix Firewood gap (B-03), stove-less households (M-13),
// the three Agent M regimes, the two
// Regime B gate holes, and the "[StartupAccess] " gap wording.

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Population;
using LandLedgers.Diagnostics;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
            // Negative-control classification for historical B-03 evidence;
            // the reusable development home now authors this destination.
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
        public void KnownConfigInventory_SharedHomeFirewoodAnchorOne_CounterOne_ReceivingOne()
        {
            IReadOnlyList<ConfigInventoryFact> inventory = WorldAccessibilityAuditor.KnownConfigInventory();
            Assert.AreEqual(1, inventory.First(f => f.PointKind == "Firewood").AuthoredCount);
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
        public void SharedDevelopmentHome_HasDistinctExteriorFirewoodHandlingDestination()
        {
            const string homeDefinitionPath = "Assets/Core/World/Buildings/HouseNaturalWood.asset";
            const string configPath = "Assets/Core/Population/DevelopmentTownConfiguration.asset";
            BuildingDefinition homeDefinition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(homeDefinitionPath);
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(configPath);
            Assert.IsNotNull(homeDefinition);
            Assert.IsNotNull(configuration);
            Assert.AreEqual(8, configuration.Households.Count);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 8),
                configuration.Households.Select(household => household.homeBuildingOrdinal));
            Assert.AreEqual(4, configuration.Households.Single(household => household.key == "shane-bryan").homeBuildingOrdinal);
            Assert.AreEqual(7, configuration.Households.Single(household => household.key == "millward").homeBuildingOrdinal);
            foreach (string stoveLessKey in new[] { "shane-bryan", "millward" })
            {
                DevelopmentTownConfiguration.HouseholdDefinition stoveLess = configuration.Households
                    .Single(household => household.key == stoveLessKey);
                Assert.IsFalse(stoveLess.openingFurnishingVariants
                    .SelectMany(variant => variant.items)
                    .Any(item => item != null && item.kind == "furniture.cooking-stove"),
                    stoveLessKey + " must remain stove-less until an authorized acquisition/installation exists.");
            }
            CollectionAssert.AreEquivalent(new[]
            {
                "baldwin-ooms", "pierce", "boniecki", "ethan-tia",
                "shane-bryan", "richard-sithole", "kwindt", "millward"
            }, configuration.Households.Select(household => household.key));

            GameObject homePrefab = homeDefinition.VisualPrefab;
            Assert.IsNotNull(homePrefab, "The household building definition must retain its authored visual prefab.");
            BuildingFootprintAuthority authority = homePrefab.GetComponent<BuildingFootprintAuthority>();
            Assert.IsNotNull(authority);
            BuildingActivityLocationAuthoring firewood = authority.ActivityLocations.SingleOrDefault(location =>
                location != null && location.type == BuildingActivityLocationType.Firewood);
            Assert.IsNotNull(firewood, "The shared home prefab needs one actual Firewood activity destination.");
            Assert.AreEqual("firewood-handling", firewood.id);
            Assert.IsFalse(firewood.requiresWalkableInterior,
                "Firewood handling is authored outside the interior floor area.");
            Assert.IsNull(firewood.sourceObject,
                "The destination must not claim an un-authored firewood pile prop as its source object.");
            Assert.AreNotEqual("porch-dropoff", firewood.id);
            Assert.AreNotEqual("stove", firewood.id);
            Assert.Greater(firewood.positionLocal.x, authority.BackRightCornerLocal.x + 2f,
                "The target is offset beyond the east side of the authored footprint for exterior route clearance.");
            Assert.LessOrEqual(firewood.positionLocal.z, authority.BackRightCornerLocal.z,
                "The target sits at the rear edge rather than on the front porch or in the generic interior.");
            Assert.AreEqual(2.25f, firewood.positionLocal.x - authority.BackRightCornerLocal.x, 0.01f,
                "The exterior approach is placed from the measured rear-east footprint corner with documented side clearance.");
            Assert.Less(Vector3.Distance(firewood.positionLocal, authority.FrontDoorLocalPoint), 15f,
                "The household handling point remains within practical yard distance of the entrance.");

            Assert.IsTrue(authority.TryGetActivityLocation(firewood.id, 6,
                out ResolvedBuildingActivityLocation resolved, out string reason), reason);
            Assert.AreEqual(firewood.id, resolved.Id);
            Assert.AreEqual(BuildingActivityLocationType.Firewood, resolved.Type);
            Assert.IsFalse(resolved.RequiresWalkableInterior);
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
