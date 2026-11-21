// Lane 9c — Movement and access repairs (Agent M fixes M-R1, M-R4,
// M-R8, M-R9; B-7 + M-R13 startup access validation), Nov 20, 2026.
//
// Two test kinds, same file:
//  (1) Real unit tests for the four pure helpers the lane extracts so
//      the harness can pin them (SimulationDrivers, the pathing
//      director and the bootstrap initializers are too Unity-bound
//      for the Roslyn closure):
//        - DomesticActivityLocationResolver (M-R1): stove id preference
//          and legacy fallback; chores Cleaning -> Porch -> literal.
//        - DomesticAnimalCareGate (M-R4): scheduling refused without a
//          destination, in every unknown state.
//        - BusinessTaskPresentationGate (M-R8): a worker with no body
//          (no actor / off-map / dead / Hidden) cannot accrue minutes.
//        - StartupAccessValidator (B-7 + M-R13): missing vs unreachable
//          gaps, presence-only firewood, clean buildings.
//        - PathingRequest.FormatFailureWarning (M-R9): the historical
//          warning prefix is preserved verbatim and the attribution
//          (label, task family, destination cells) rides as a suffix.
//  (2) Structural tests pinning the Unity-bound wiring by reading the
//      production files — the resolver is called from a production
//      path, the animal-care gate is consulted, the presentation gate
//      is applied in AdvanceAutonomousWork, and the startup validation
//      runs for homes and businesses. The repo root comes from
//      LAND_LEDGERS_REPO, then a walk up (DEF-046 precedent). A
//      missing repo fails loudly, never silently passes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCLane9cMovementAccessTests
    {
        private const string SaveLoadManagerPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string DomesticLaborDriverPath =
            "Assets/Scripts/Domains/Population/DomesticLaborDriver.cs";
        private const string PathingDirectorPath =
            "Assets/Scripts/Domains/Population/PopulationPathingDirector.cs";
        private const string PathingManagerPath =
            "Assets/Scripts/Domains/World/Pathing/PathingManager.cs";
        private const string PathTypesPath =
            "Assets/Scripts/Domains/World/Pathing/PathTypes.cs";
        private const string AgentMoverPath =
            "Assets/Scripts/Domains/World/Pathing/AgentMover.cs";
        private const string BusinessInteriorInitializerPath =
            "Assets/Scripts/Orchestration/Bootstrap/DevelopmentBusinessInteriorInitializer.cs";
        private const string FurnishingInitializerPath =
            "Assets/Scripts/Orchestration/Bootstrap/DevelopmentTownFurnishingInitializer.cs";

        // ---------- M-R1: domestic activity location resolver ----------

        [Test]
        public void StoveLocation_PrefersRegistryIdOverLiteral()
        {
            Assert.AreEqual("stove-h1-a-stove",
                DomesticActivityLocationResolver.ResolveStoveLocationId("stove-h1-a-stove"));
        }

        [Test]
        public void StoveLocation_FallsBackToLiteralWhenRegistryHasNoStove()
        {
            Assert.AreEqual("stove", DomesticActivityLocationResolver.ResolveStoveLocationId(null));
            Assert.AreEqual("stove", DomesticActivityLocationResolver.ResolveStoveLocationId(""));
            Assert.AreEqual("stove", DomesticActivityLocationResolver.ResolveStoveLocationId("   "));
        }

        // ---------- Fix #1 (A4): fuel-handling leg 0 Firewood fallback ----------

        [Test]
        public void FuelLeg0_PrefersAuthoredFirewoodPoint()
        {
            string resolved = DomesticActivityLocationResolver.ResolveFuelLeg0LocationId(
                () => "firewood-h1-a", "stove-h1-a-stove", out string fallbackReason);
            Assert.AreEqual("firewood-h1-a", resolved);
            Assert.IsNull(fallbackReason, "no fallback when the authored point exists");
        }

        [Test]
        public void FuelLeg0_FallsBackToStoveWithReasonWhenNoFirewoodPoint()
        {
            string resolved = DomesticActivityLocationResolver.ResolveFuelLeg0LocationId(
                () => null, "stove-h1-a-stove", out string fallbackReason);
            Assert.AreEqual("stove-h1-a-stove", resolved);
            Assert.IsNotNull(fallbackReason);
            Assert.IsTrue(fallbackReason.Contains("no Firewood activity location is authored"),
                "the reason must name the missing authoring: " + fallbackReason);
            Assert.IsFalse(resolved.Contains("porch"), "the fallback never aliases the porch point");
        }

        [Test]
        public void FuelLeg0_FallsBackToLiteralStoveWhenRegistryHasNoStove()
        {
            string resolved = DomesticActivityLocationResolver.ResolveFuelLeg0LocationId(
                () => null, null, out string fallbackReason);
            Assert.AreEqual("stove", resolved, "a stove-less household keeps the legacy literal");
            Assert.IsNotNull(fallbackReason);
        }

        [Test]
        public void Chores_PrefersReachableCleaningPoint()
        {
            var tried = new List<string>();
            string resolved = DomesticActivityLocationResolver.ResolveChoreLocationId(0, type =>
            {
                tried.Add(type);
                return type == DomesticActivityLocationResolver.CleaningTypeName ? "cleaning-h1-a" : null;
            });
            Assert.AreEqual("cleaning-h1-a", resolved);
            Assert.AreEqual(1, tried.Count);
            Assert.AreEqual(DomesticActivityLocationResolver.CleaningTypeName, tried[0]);
        }

        [Test]
        public void Chores_FallsBackToPorchWhenNoCleaningPoint()
        {
            var tried = new List<string>();
            string resolved = DomesticActivityLocationResolver.ResolveChoreLocationId(3, type =>
            {
                tried.Add(type);
                return type == DomesticActivityLocationResolver.PorchTypeName ? "porch-h1-a" : null;
            });
            Assert.AreEqual("porch-h1-a", resolved);
            Assert.AreEqual(2, tried.Count);
            Assert.AreEqual(DomesticActivityLocationResolver.CleaningTypeName, tried[0]);
            Assert.AreEqual(DomesticActivityLocationResolver.PorchTypeName, tried[1]);
        }

        [Test]
        public void Chores_FallsBackToLegacyLiteralsByDayParity()
        {
            Assert.AreEqual("cleaning",
                DomesticActivityLocationResolver.ResolveChoreLocationId(0, _ => null));
            Assert.AreEqual("porch-dropoff",
                DomesticActivityLocationResolver.ResolveChoreLocationId(1, _ => null));
            Assert.AreEqual("porch-dropoff",
                DomesticActivityLocationResolver.ResolveChoreLocationId(7, null));
        }

        // ---------- M-R4: animal-care scheduling gate ----------

        [Test]
        public void AnimalCare_SchedulesOnlyWhenDestinationExists()
        {
            Assert.IsTrue(DomesticAnimalCareGate.ShouldScheduleAnimalCare(true, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void AnimalCare_RefusesWhenDestinationMissingOrUnknown()
        {
            Assert.IsFalse(DomesticAnimalCareGate.ShouldScheduleAnimalCare(false, out string refused));
            Assert.IsTrue(refused.Contains("no animal-care destination"), refused);
            Assert.IsFalse(DomesticAnimalCareGate.ShouldScheduleAnimalCare(null, out string unknown));
            Assert.IsTrue(unknown.Contains("no animal-care destination"), unknown);
        }

        // ---------- M-R8: business-task presentation gate ----------

        [Test]
        public void PresentationGate_AllowsWorkWhenWorkerHasABody()
        {
            Assert.IsFalse(BusinessTaskPresentationGate.ShouldSkipWorkAccrual(
                hasManagedActor: true, isOffMap: false, isDead: false, isHidden: false, out string reason));
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void PresentationGate_SkipsNoActorWorker()
        {
            Assert.IsTrue(BusinessTaskPresentationGate.ShouldSkipWorkAccrual(
                hasManagedActor: false, isOffMap: false, isDead: false, isHidden: false, out string reason));
            Assert.IsTrue(reason.Contains("no managed on-map physical actor"), reason);
        }

        [Test]
        public void PresentationGate_SkipsOffMapWorker()
        {
            Assert.IsTrue(BusinessTaskPresentationGate.ShouldSkipWorkAccrual(
                hasManagedActor: true, isOffMap: true, isDead: false, isHidden: false, out string reason));
            Assert.IsTrue(reason.Contains("off-map"), reason);
        }

        [Test]
        public void PresentationGate_SkipsDeadWorker()
        {
            Assert.IsTrue(BusinessTaskPresentationGate.ShouldSkipWorkAccrual(
                hasManagedActor: true, isOffMap: false, isDead: true, isHidden: false, out string reason));
            Assert.IsTrue(reason.Contains("dead"), reason);
        }

        [Test]
        public void PresentationGate_SkipsHiddenWorker()
        {
            Assert.IsTrue(BusinessTaskPresentationGate.ShouldSkipWorkAccrual(
                hasManagedActor: true, isOffMap: false, isDead: false, isHidden: true, out string reason));
            Assert.IsTrue(reason.Contains("Hidden"), reason);
        }

        // ---------- B-7 + M-R13: startup access validator ----------

        private static StartupAccessValidator.ActivityAccessCheck Check(
            string type, bool found, bool reachable, bool requiresRoute = true, string locationId = null, string reason = null)
        {
            return new StartupAccessValidator.ActivityAccessCheck
            {
                TypeLabel = type,
                Found = found,
                ReachableFromMainEntrance = reachable,
                RequiresInteriorRoute = requiresRoute,
                LocationId = locationId,
                Reason = reason ?? string.Empty,
            };
        }

        [Test]
        public void StartupAccess_CleanBuildingHasNoGaps()
        {
            var gaps = StartupAccessValidator.AppendAccessGaps("Business 'General Store' (store, building 3)",
                new[]
                {
                    Check("Counter", found: true, reachable: true, locationId: "general-store-counter"),
                    Check("Receiving", found: true, reachable: true, locationId: "store-receiving"),
                }, new List<string>());
            Assert.AreEqual(0, gaps.Count);
        }

        [Test]
        public void StartupAccess_MissingPointIsAGap()
        {
            var gaps = StartupAccessValidator.AppendAccessGaps("Business 'Bakery' (bakery, building 4)",
                new[]
                {
                    Check("Counter", found: false, reachable: false, reason: "No reachable Counter activity is authored on 'Bakery'."),
                    Check("Receiving", found: false, reachable: false),
                }, new List<string>());
            Assert.AreEqual(2, gaps.Count);
            Assert.IsTrue(gaps[0].Contains("Bakery"), gaps[0]);
            Assert.IsTrue(gaps[0].Contains("no Counter activity point authored"), gaps[0]);
            Assert.IsTrue(gaps[1].Contains("no Receiving activity point authored"), gaps[1]);
        }

        [Test]
        public void StartupAccess_AuthoredButUnreachablePointIsADistinctGap()
        {
            var gaps = StartupAccessValidator.AppendAccessGaps("Home 'Millward' (household 8, building 7)",
                new[]
                {
                    Check("Stove", found: true, reachable: false, locationId: "stove-h8-a-stove",
                        reason: "Activity 'stove-h8-a-stove' has no readable matching floor mesh or walkable polygon."),
                }, new List<string>());
            Assert.AreEqual(1, gaps.Count);
            Assert.IsTrue(gaps[0].Contains("not reachable from main-entrance"), gaps[0]);
            Assert.IsTrue(gaps[0].Contains("stove-h8-a-stove"), gaps[0]);
            Assert.IsTrue(gaps[0].Contains("no readable matching floor mesh"), gaps[0]);
        }

        [Test]
        public void StartupAccess_PresenceOnlyFirewoodNeedsNoRoute()
        {
            var gaps = StartupAccessValidator.AppendAccessGaps("Home 'Pierce' (household 1, building 1)",
                new[]
                {
                    Check("Firewood", found: true, reachable: false, requiresRoute: false, locationId: "firewood-h1-a"),
                }, new List<string>());
            Assert.AreEqual(0, gaps.Count);
        }

        [Test]
        public void StartupAccess_NullChecksProduceNoGaps()
        {
            var gaps = StartupAccessValidator.AppendAccessGaps("Business 'X' (x, building 0)", null, null);
            Assert.AreEqual(0, gaps.Count);
        }

        // ---------- M-R9: pathing failure attribution ----------

        [Test]
        public void FailureWarning_PreservesHistoricalPrefixAndAddsAttribution()
        {
            var request = new PathingRequest(
                new GridCoord(12, 30), new GridCoord(45, 60), null,
                "Person 003 Glen Pierce door-to-door", PathingProfile.Pedestrian,
                attribution: "task='domestic.meal-prep' activity='stove-h1-a-stove'");
            string warning = PathingRequest.FormatFailureWarning(
                request, PathingFailureCategory.StartNotWalkable,
                "Point is outside the authored walkable floor area on floor 0.");
            Assert.IsTrue(warning.StartsWith(
                "Pathing request failed (StartNotWalkable): Point is outside the authored walkable floor area on floor 0.", StringComparison.Ordinal), warning);
            Assert.IsTrue(warning.Contains("Person 003 Glen Pierce door-to-door"), warning);
            Assert.IsTrue(warning.Contains("task='domestic.meal-prep'"), warning);
            Assert.IsTrue(warning.Contains("activity='stove-h1-a-stove'"), warning);
            Assert.IsTrue(warning.Contains("from (12, 30) to (45, 60)"), warning);
        }

        [Test]
        public void FailureWarning_OmitsEmptyAttributionButKeepsCells()
        {
            var request = new PathingRequest(new GridCoord(1, 2), new GridCoord(3, 4));
            string warning = PathingRequest.FormatFailureWarning(
                request, PathingFailureCategory.NoRoute, "No path found from (1, 2) to (3, 4).");
            Assert.IsTrue(warning.StartsWith("Pathing request failed (NoRoute): No path found from (1, 2) to (3, 4).", StringComparison.Ordinal), warning);
            Assert.IsTrue(warning.Contains("from (1, 2) to (3, 4)"), warning);
        }

        [Test]
        public void FailureWarning_IncludesTargetBuildingWhenKnown()
        {
            var request = new PathingRequest(
                new GridCoord(1, 2), new GridCoord(3, 4), null, "Person 001 Kennedy Baldwin-ooms door-to-door",
                targetInteriorBuildingId: 7);
            string warning = PathingRequest.FormatFailureWarning(
                request, PathingFailureCategory.TargetNotWalkable, "Target cell (3, 4) is not walkable.");
            Assert.IsTrue(warning.Contains("target building 7"), warning);
        }

        // ---------- Structural pins: the production wiring ----------

        [Test]
        public void Wiring_ResolverIsCalledFromProductionMovementPath()
        {
            string drivers = ReadRepoFile(DriversPath);
            Assert.IsTrue(drivers.Contains("TryFindReachableActivityLocation"),
                "SimulationDrivers must call the type-based resolver (M-R1): it had zero production callers.");
            Assert.IsTrue(drivers.Contains("DomesticActivityLocationResolver.ResolveStoveLocationId"),
                "Meal prep / fuel leg 1 must resolve the stove id through the helper (M-R1).");
            Assert.IsTrue(drivers.Contains("DomesticActivityLocationResolver.ResolveFuelLeg0LocationId"),
                "Fuel-handling leg 0 must resolve through the helper (fix #1, A4 fallback).");
            Assert.IsTrue(drivers.Contains("DomesticActivityLocationResolver.ResolveChoreLocationId"),
                "Chores must resolve by activity type through the helper (M-R1).");
        }

        [Test]
        public void Wiring_AnimalCareGateIsConsultedAndDestinationDefaultsToAbsent()
        {
            string driver = ReadRepoFile(DomesticLaborDriverPath);
            Assert.IsTrue(driver.Contains("DomesticAnimalCareGate.ShouldScheduleAnimalCare"),
                "DomesticLaborDriver must gate animal-care scheduling on a destination existing (M-R4).");
            Assert.IsTrue(driver.Contains("AnimalCareDestinationAvailable"),
                "DomesticLaborDriver must carry the animal-care destination input (M-R4).");
            string drivers = ReadRepoFile(DriversPath);
            Assert.IsTrue(drivers.Contains("AnimalCareDestinationAvailable = false"),
                "Production must supply the animal-care destination as absent until a pen/pasture destination is authored (M-R4).");
        }

        [Test]
        public void Wiring_PresentationGateIsAppliedInAdvanceAutonomousWork()
        {
            string drivers = ReadRepoFile(DriversPath);
            Assert.IsTrue(drivers.Contains("BusinessTaskPresentationGate.ShouldSkipWorkAccrual"),
                "AdvanceAutonomousWork must apply the presentation gate (M-R8).");
            Assert.IsTrue(drivers.Contains("ShouldSkipBusinessWorkAccrual"),
                "AdvanceAutonomousWork must gather the presentation facts (M-R8).");
            string director = ReadRepoFile(PathingDirectorPath);
            Assert.IsTrue(director.Contains("TryGetPersonPresentationReadout"),
                "The pathing director must expose the presentation readout the gate reads (M-R8).");
        }

        [Test]
        public void Wiring_FailureWarningCarriesAttribution()
        {
            string manager = ReadRepoFile(PathingManagerPath);
            Assert.IsTrue(manager.Contains("FormatFailureWarning"),
                "PathingManager.Fail must format the warning with attribution (M-R9).");
            Assert.IsTrue(manager.Contains("attribution"),
                "PathingManager.TryFindPath must thread caller attribution into the request (M-R9).");
            string mover = ReadRepoFile(AgentMoverPath);
            Assert.IsTrue(mover.Contains("attribution"),
                "AgentMover route starts must accept caller attribution (M-R9).");
            string director = ReadRepoFile(PathingDirectorPath);
            Assert.IsTrue(director.Contains("task='"),
                "The domestic path must supply its task family as attribution (M-R9).");
            Assert.IsTrue(director.Contains("destination=building:"),
                "Explicit routes must supply their destination building as attribution (M-R9).");
        }

        [Test]
        public void Wiring_StartupValidationRunsForBusinessesAndHomes()
        {
            string business = ReadRepoFile(BusinessInteriorInitializerPath);
            Assert.IsTrue(business.Contains("ValidateBusinessAccess"),
                "Business interiors must validate counter/receiving access at startup (M-R13).");
            Assert.IsTrue(business.Contains("BuildingActivityLocationType.Counter"),
                "Business validation must require a Counter (service point) (M-R13).");
            Assert.IsTrue(business.Contains("BuildingActivityLocationType.Receiving"),
                "Business validation must require a Receiving point (M-R13).");
            string furnishing = ReadRepoFile(FurnishingInitializerPath);
            Assert.IsTrue(furnishing.Contains("ValidateHomeAccess"),
                "Homes must validate stove/cleaning/firewood/dining access at startup (B-7).");
        }

        // ---------- Repo helpers (DEF-046 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, SaveLoadManagerPath)))
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
                    tried.AppendLine("  " + dir);
                    if (File.Exists(Path.Combine(dir, SaveLoadManagerPath)))
                    {
                        return dir;
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (Lane 9c): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Expected production file to exist: " + full);
            return File.ReadAllText(full);
        }
    }
}
