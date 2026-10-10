using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Phase D (Real People): NPC home construction — the missing
    /// Project/WorkPackage execution authority on top of the contract book.
    /// The staged pipeline is: design → work packages → materials (real
    /// lots, provenance) → labor (person-time, no double-booking) →
    /// completed building → accommodation spaces → occupancy. No stage can
    /// be skipped; no structure is created from a bare need flag.
    /// </summary>
    [TestFixture]
    public sealed class NpcHomeConstructionTests
    {
        private sealed class FakeMaterialSource : IConstructionMaterialSource
        {
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public readonly List<string> ConsumedLabels = new List<string>();

            public int AvailableUnits(ConstructionMaterialRequirement requirement)
            {
                return requirement == null ? 0
                    : Stock.TryGetValue(requirement.MaterialDisplayName, out int units) ? units : 0;
            }

            public string TryConsume(ConstructionMaterialRequirement requirement, int units,
                string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel)
            {
                provenanceLabel = string.Empty;
                int have = AvailableUnits(requirement);
                if (have < units)
                    return $"FakeMaterialSource: only {have} of '{requirement.MaterialDisplayName}' in stock.";
                Stock[requirement.MaterialDisplayName] = have - units;
                provenanceLabel = $"lot '{requirement.MaterialDisplayName}' #{units} from FakeMaterialSource ({projectLabel}, day {dayIndex})";
                ConsumedLabels.Add(provenanceLabel);
                return null;
            }
        }

        private sealed class FakeToolCustody : IHouseholdToolCustody
        {
            private readonly HashSet<string> held = new HashSet<string>();
            public void Grant(int householdId, string toolId) => held.Add(householdId + ":" + toolId);
            public bool HouseholdHoldsTool(int householdId, string toolItemId) =>
                held.Contains(householdId + ":" + toolItemId);
        }

        private static ConstructionMaterialRequirement Material(string name, int units, string unitLabel)
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-" + name,
                MaterialKind = ConstructionMaterialKind.Other,
                MaterialDisplayName = name,
                RequiredUnits = units,
                UnitLabel = unitLabel,
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            };
        }

        private BuildingDesign CabinDesign(string designId, string prefabId = "")
        {
            var design = new BuildingDesign
            {
                DesignId = designId,
                DisplayName = "Settler cabin",
                WorksKind = ConstructionWorksKind.Unspecified,
                KindLabel = "cabin",
                RequiredPrefabId = prefabId ?? string.Empty,
            };
            design.PrefabExposedContract.Add("footprint anchor: 24x36 ft");
            design.PrefabExposedContract.Add("door socket: front wall center");
            var foundation = new BuildingDesignPhase
            {
                PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240, Notes = "stone footings",
            };
            foundation.Materials.Add(Material("fieldstone", 40, "perch"));
            foundation.RequiredToolItemIds.Add("shovel");
            var framing = new BuildingDesignPhase
            {
                PhaseName = "Framing", Sequence = 1, LaborMinutes = 480, Notes = "log walls and roof",
            };
            framing.Materials.Add(Material("lumber", 600, "board feet"));
            framing.RequiredToolItemIds.Add("saw");
            design.Phases.Add(foundation);
            design.Phases.Add(framing);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            return design;
        }

        private FakeMaterialSource StockedSource()
        {
            var source = new FakeMaterialSource();
            source.Stock["fieldstone"] = 100;
            source.Stock["lumber"] = 1000;
            return source;
        }

        private FakeToolCustody EquippedCustody()
        {
            var custody = new FakeToolCustody();
            custody.Grant(1, "shovel");
            custody.Grant(1, "saw");
            return custody;
        }

        [Test]
        public void FullPipelineDesignToOccupiedHome()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            Assert.IsNull(catalog.RegisterDesign(CabinDesign("cabin-a"), diag));

            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            Assert.IsNotNull(project);
            Assert.IsFalse(project.VisualPlacementPending);

            Assert.IsNull(executor.CreateWorkPackages(project, catalog.FindDesign("cabin-a"), 10, diag));
            Assert.AreEqual(2, project.WorkPackages.Count);

            var source = StockedSource();
            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            WorkPackage foundation = project.WorkPackages[0];
            WorkPackage framing = project.WorkPackages[1];

            Assert.IsNull(executor.StagePackageMaterials(project, foundation, source, 11, diag));
            Assert.AreEqual(WorkPackageStatus.MaterialsStaged, foundation.Status);
            Assert.AreEqual(60, source.Stock["fieldstone"]); // 100 - 40 consumed, real lots

            Assert.IsNull(executor.CommitLabor(project, foundation, 5, tracker, custody, 11, 360, 240, "build", diag));
            Assert.AreEqual(WorkPackageStatus.InProgress, foundation.Status);
            Assert.IsNull(executor.AdvancePackage(project, foundation, 11, diag));
            Assert.AreEqual(WorkPackageStatus.Complete, foundation.Status);

            Assert.IsNull(executor.StagePackageMaterials(project, framing, source, 12, diag));
            Assert.IsNull(executor.CommitLabor(project, framing, 5, tracker, custody, 12, 360, 480, "build", diag));
            Assert.IsNull(executor.AdvancePackage(project, framing, 12, diag));

            var housing = new HousingAuthority();
            Assert.IsNull(executor.CompleteProject(project, catalog.FindDesign("cabin-a"), housing, 12, diag));
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
            Assert.IsFalse(string.IsNullOrWhiteSpace(project.BuildingId));

            // Completed building yields real accommodation spaces from the design spec.
            List<AccommodationSpace> spaces = housing.SpacesForBuilding(project.BuildingId);
            Assert.AreEqual(1, spaces.Count);
            Assert.AreEqual(4, spaces[0].SleepingCapacity);

            // Materials consumed carry provenance; labor minutes are real.
            Assert.AreEqual(2, project.MaterialsConsumed.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(project.MaterialsConsumed[0].Provenance));
            Assert.AreEqual(720, project.TotalLaborMinutesCommitted);

            // The commissioning household moves in as owner-occupiers.
            Assert.IsNull(executor.OccupyCompletedHome(project, housing,
                new List<int> { 5, 6 }, AccommodationArrangement.OwnerOccupied, 13, diag));
            Assert.AreEqual(AccommodationArrangement.OwnerOccupied,
                housing.CurrentOccupanciesForPerson(5)[0].Arrangement);
            Assert.AreEqual(AccommodationArrangement.OwnerOccupied,
                housing.CurrentOccupanciesForPerson(6)[0].Arrangement);
        }

        [Test]
        public void StagesCannotBeSkipped()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            catalog.RegisterDesign(CabinDesign("cabin-a"), diag);
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            executor.CreateWorkPackages(project, catalog.FindDesign("cabin-a"), 10, diag);
            var source = StockedSource();
            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();

            WorkPackage framing = project.WorkPackages[1];
            executor.StagePackageMaterials(project, framing, source, 11, diag);
            executor.CommitLabor(project, framing, 5, tracker, custody, 11, 360, 480, "build", diag);

            // Foundation is not complete — framing must wait.
            string refusal = executor.AdvancePackage(project, framing, 11, diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("waits on", refusal);
            Assert.AreNotEqual(WorkPackageStatus.Complete, framing.Status);
        }

        [Test]
        public void InsufficientMaterialsRefuseWithoutSyntheticStock()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            catalog.RegisterDesign(CabinDesign("cabin-a"), diag);
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            executor.CreateWorkPackages(project, catalog.FindDesign("cabin-a"), 10, diag);

            var source = new FakeMaterialSource();
            source.Stock["fieldstone"] = 10; // needs 40

            string refusal = executor.StagePackageMaterials(project, project.WorkPackages[0], source, 11, diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("no synthetic stock", refusal);
            Assert.AreEqual(10, source.Stock["fieldstone"]); // nothing consumed
            Assert.AreEqual(WorkPackageStatus.Ready, project.WorkPackages[0].Status);
        }

        [Test]
        public void DoubleBookedLaborIsRefusedLoudly()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            catalog.RegisterDesign(CabinDesign("cabin-a"), diag);
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            executor.CreateWorkPackages(project, catalog.FindDesign("cabin-a"), 10, diag);
            executor.StagePackageMaterials(project, project.WorkPackages[0], StockedSource(), 11, diag);

            var tracker = new PersonScheduleTracker();
            // P5 already works 6:00-14:00 on day 11.
            Assert.IsNull(tracker.TryReserve(5, PersonActivityKind.Work, 11, 360, 480, "day job"));

            string refusal = executor.CommitLabor(project, project.WorkPackages[0], 5, tracker,
                EquippedCustody(), 11, 420, 240, "build", diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("already", refusal);
            Assert.AreEqual(0, project.TotalLaborMinutesCommitted);
        }

        [Test]
        public void MissingToolsRefuseLabor()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            catalog.RegisterDesign(CabinDesign("cabin-a"), diag);
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            executor.CreateWorkPackages(project, catalog.FindDesign("cabin-a"), 10, diag);
            executor.StagePackageMaterials(project, project.WorkPackages[0], StockedSource(), 11, diag);

            var custody = new FakeToolCustody(); // holds nothing
            string refusal = executor.CommitLabor(project, project.WorkPackages[0], 5,
                new PersonScheduleTracker(), custody, 11, 360, 240, "build", diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("shovel", refusal);
        }

        [Test]
        public void MissingPrefabFilesCodexHandoffWithoutBlockingData()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            catalog.RegisterDesign(CabinDesign("cabin-b", "settler_cabin_prefab"), diag);

            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-b", "parcel-9", 10, diag);

            Assert.IsNotNull(project);
            Assert.IsTrue(project.VisualPlacementPending);
            Assert.AreEqual(1, catalog.Handoffs.Count);
            PrefabHandoffRecord handoff = catalog.Handoffs[0];
            Assert.AreEqual("settler_cabin_prefab", handoff.RequiredPrefabId);
            Assert.AreEqual("cabin-b", handoff.DesignId);
            Assert.AreEqual(PrefabHandoffStatus.PendingCodex, handoff.Status);
            Assert.AreEqual(2, handoff.ExposedContract.Count); // the exact contract Codex must satisfy
            Assert.AreEqual(project.PrefabHandoffRecordId, handoff.RecordId);

            // The data pipeline still runs — only visual placement is pending.
            executor.CreateWorkPackages(project, catalog.FindDesign("cabin-b"), 10, diag);
            var source = StockedSource();
            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            Assert.IsNull(executor.StagePackageMaterials(project, project.WorkPackages[0], source, 11, diag));
            Assert.IsNull(executor.CommitLabor(project, project.WorkPackages[0], 5, tracker, custody, 11, 360, 240, "build", diag));
            Assert.IsNull(executor.AdvancePackage(project, project.WorkPackages[0], 11, diag));
            Assert.IsNull(executor.StagePackageMaterials(project, project.WorkPackages[1], source, 12, diag));
            Assert.IsNull(executor.CommitLabor(project, project.WorkPackages[1], 5, tracker, custody, 12, 360, 480, "build", diag));
            Assert.IsNull(executor.AdvancePackage(project, project.WorkPackages[1], 12, diag));
            var housing = new HousingAuthority();
            Assert.IsNull(executor.CompleteProject(project, catalog.FindDesign("cabin-b"), housing, 12, diag));
            Assert.AreEqual(ConstructionProjectStatus.Complete, project.Status);
            Assert.IsTrue(project.VisualPlacementPending); // still honest about visuals
        }

        [Test]
        public void NoCompletionWithoutFinishedWork()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            catalog.RegisterDesign(CabinDesign("cabin-a"), diag);
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            executor.CreateWorkPackages(project, catalog.FindDesign("cabin-a"), 10, diag);

            string refusal = executor.CompleteProject(project, catalog.FindDesign("cabin-a"), new HousingAuthority(), 12, diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("no fake completions", refusal);
            Assert.IsTrue(string.IsNullOrWhiteSpace(project.BuildingId));
        }

        [Test]
        public void SaveLoadRoundTripHasNoDuplicatedProjectsOrPackages()
        {
            var diag = new List<string>();
            var catalog = new BuildingDesignCatalog();
            catalog.RegisterDesign(CabinDesign("cabin-a"), diag);
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = executor.CreateProject(catalog, 1, "cabin-a", "parcel-9", 10, diag);
            executor.CreateWorkPackages(project, catalog.FindDesign("cabin-a"), 10, diag);

            ConstructionProjectExecutor.ConstructionProjectSaveDto dto =
                executor.CaptureSaveDto(new List<ConstructionProject> { project });
            List<ConstructionProject> once = executor.LoadFromSaveDto(dto, diag);
            List<ConstructionProject> twice = executor.LoadFromSaveDto(dto, diag);

            Assert.AreEqual(1, once.Count);
            Assert.AreEqual(1, twice.Count);
            Assert.AreEqual(2, once[0].WorkPackages.Count);
            Assert.AreEqual(2, twice[0].WorkPackages.Count);

            // A corrupt DTO with a duplicated project id is quarantined.
            dto.Projects.Add(project);
            List<ConstructionProject> deduped = executor.LoadFromSaveDto(dto, diag);
            Assert.AreEqual(1, deduped.Count);
        }
    }
}
