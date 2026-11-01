using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Persistence
{
    [TestFixture]
    public sealed class IdAllocatorMigrationTests
    {
        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static LandLedgersSaveGameDto MakeSaveWithManifest(
            bool personApplied = false,
            bool householdApplied = false,
            bool buildingApplied = false)
        {
            LandLedgersSaveGameDto save = new()
            {
                manifest = new SaveManifestDto { formatVersion = 6 },
                migrationManifest = new MigrationManifestDto
                {
                    migrationRunId = "test-run-id",
                    schemaVersion = 6,
                    originalSchemaVersion = 6,
                    appliedSteps = new List<MigrationStepRecordDto>()
                },
                population = new PopulationSaveDto
                {
                    people = new List<PersonSaveDto>(),
                    households = new List<HouseholdSaveDto>()
                },
                world = new WorldSaveDto
                {
                    buildings = new List<BuildingSaveDto>()
                }
            };

            if (personApplied)
            {
                save.migrationManifest.appliedSteps.Add(new MigrationStepRecordDto
                {
                    stepId = PersistentIdAllocatorMigration.PersonIdAllocatorStepId,
                    fromVersion = 6,
                    toVersion = 6,
                    appliedAtUtc = "2026-01-01T00:00:00Z"
                });
            }

            if (householdApplied)
            {
                save.migrationManifest.appliedSteps.Add(new MigrationStepRecordDto
                {
                    stepId = PersistentIdAllocatorMigration.HouseholdIdAllocatorStepId,
                    fromVersion = 6,
                    toVersion = 6,
                    appliedAtUtc = "2026-01-01T00:00:00Z"
                });
            }

            if (buildingApplied)
            {
                save.migrationManifest.appliedSteps.Add(new MigrationStepRecordDto
                {
                    stepId = PersistentIdAllocatorMigration.BuildingIdAllocatorStepId,
                    fromVersion = 6,
                    toVersion = 6,
                    appliedAtUtc = "2026-01-01T00:00:00Z"
                });
            }

            return save;
        }

        private static PersonSaveDto MakePerson(int id) => new() { id = id };
        private static HouseholdSaveDto MakeHousehold(int id) => new() { id = id };
        private static BuildingSaveDto MakeBuilding(int id) => new() { id = id };

        // ---------------------------------------------------------------
        // Guard tests
        // ---------------------------------------------------------------

        [Test]
        public void NullSave_ReturnsFalse()
        {
            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(null, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void NullMigrationManifest_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = new()
            {
                manifest = new SaveManifestDto { formatVersion = 6 },
                migrationManifest = null,
                population = new PopulationSaveDto()
            };

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        // ---------------------------------------------------------------
        // Legacy migration — Person (unapplied branch)
        // ---------------------------------------------------------------

        [Test]
        public void LegacyMigration_NoPeople_SetsNextPersonIdToZeroAndRecordsStep()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest();
            // no people added

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(0, save.population.nextPersonId);
            Assert.IsTrue(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.PersonIdAllocatorStepId));
        }

        [Test]
        public void LegacyMigration_WithPeople_SetsNextPersonIdToMaxPlusOne()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest();
            save.population.people.Add(MakePerson(0));
            save.population.people.Add(MakePerson(3));
            save.population.people.Add(MakePerson(7));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(8, save.population.nextPersonId);
        }

        [Test]
        public void LegacyMigration_PersonWithNegativeId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest();
            save.population.people.Add(MakePerson(-1));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void LegacyMigration_DuplicatePersonId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest();
            save.population.people.Add(MakePerson(5));
            save.population.people.Add(MakePerson(5));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void LegacyMigration_PersonIdIsIntMaxValue_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest();
            save.population.people.Add(MakePerson(int.MaxValue));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        // ---------------------------------------------------------------
        // Legacy migration — Household (unapplied branch)
        // ---------------------------------------------------------------

        [Test]
        public void LegacyMigration_NoHouseholds_SetsNextHouseholdIdToZeroAndRecordsStep()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true);
            // no households added

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(0, save.population.nextHouseholdId);
            Assert.IsTrue(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.HouseholdIdAllocatorStepId));
        }

        [Test]
        public void LegacyMigration_WithHouseholds_SetsNextHouseholdIdToMaxPlusOne()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true);
            save.population.households.Add(MakeHousehold(2));
            save.population.households.Add(MakeHousehold(10));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(11, save.population.nextHouseholdId);
        }

        [Test]
        public void LegacyMigration_HouseholdWithNegativeId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true);
            save.population.households.Add(MakeHousehold(-5));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void LegacyMigration_DuplicateHouseholdId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true);
            save.population.households.Add(MakeHousehold(3));
            save.population.households.Add(MakeHousehold(3));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void LegacyMigration_HouseholdIdIsIntMaxValue_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true);
            save.population.households.Add(MakeHousehold(int.MaxValue));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        // ---------------------------------------------------------------
        // Validation — already-applied branch
        // ---------------------------------------------------------------

        [Test]
        public void Validation_ValidPersistedNextIds_ReturnsTrue()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true, buildingApplied: true);
            save.population.people.Add(MakePerson(0));
            save.population.people.Add(MakePerson(1));
            save.population.nextPersonId = 2;
            save.population.households.Add(MakeHousehold(0));
            save.population.nextHouseholdId = 1;
            save.world.buildings.Add(MakeBuilding(0));
            save.world.nextBuildingId = 1;

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
        }

        [Test]
        public void Validation_NegativePersistedNextPersonId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.population.nextPersonId = -1;
            save.population.nextHouseholdId = 0;

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void Validation_NextPersonIdBehindMaxExistingId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.population.people.Add(MakePerson(5));
            save.population.nextPersonId = 5; // must be strictly greater than 5
            save.population.nextHouseholdId = 0;

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void Validation_NegativePersistedNextHouseholdId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.population.nextPersonId = 0;
            save.population.nextHouseholdId = -1;

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void Validation_NextHouseholdIdBehindMaxExistingId_ReturnsFalse()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.population.nextPersonId = 0;
            save.population.households.Add(MakeHousehold(4));
            save.population.nextHouseholdId = 4; // must be strictly greater than 4

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        // ---------------------------------------------------------------
        // Legacy migration — Building (unapplied branch)
        // ---------------------------------------------------------------

        [Test]
        public void LegacyBuildingIds_EmptyCollection_SeedsZero()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            // save.world.buildings has 0 elements

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(0, save.world.nextBuildingId);
            Assert.IsTrue(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.BuildingIdAllocatorStepId));
        }

        [Test]
        public void LegacyBuildingIds_DenseCollection_SeedsMaxPlusOne()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.world.buildings.Add(MakeBuilding(0));
            save.world.buildings.Add(MakeBuilding(1));
            save.world.buildings.Add(MakeBuilding(2));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(3, save.world.nextBuildingId);
            Assert.IsTrue(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.BuildingIdAllocatorStepId));
        }

        [Test]
        public void LegacyBuildingIds_DuplicateIds_BlockMigration()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.world.buildings.Add(MakeBuilding(0));
            save.world.buildings.Add(MakeBuilding(0));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void LegacyBuildingIds_IntMaxValue_BlocksMigration()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.world.buildings.Add(MakeBuilding(int.MaxValue));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void LegacyBuildingIds_NegativeEntityId_BlocksIfUnsupported()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.world.buildings.Add(MakeBuilding(-1));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void LegacyBuildingIds_NonDenseIds_BlockPreP005()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.world.buildings.Add(MakeBuilding(0));
            save.world.buildings.Add(MakeBuilding(1));
            save.world.buildings.Add(MakeBuilding(5)); // list position 2, stored ID 5

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
            StringAssert.Contains("MR-P005", reason);
            Assert.AreEqual(5, save.world.buildings[2].id, "Existing building ID must not be renumbered.");
        }

        [Test]
        public void ExistingBuildingIds_RemainUnchangedThroughMigration()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.world.buildings.Add(MakeBuilding(0));
            save.world.buildings.Add(MakeBuilding(1));
            save.world.buildings.Add(MakeBuilding(2));

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(0, save.world.buildings[0].id);
            Assert.AreEqual(1, save.world.buildings[1].id);
            Assert.AreEqual(2, save.world.buildings[2].id);
            Assert.AreEqual(3, save.world.nextBuildingId);
        }

        // ---------------------------------------------------------------
        // Validation — Building (already-applied branch)
        // ---------------------------------------------------------------

        [Test]
        public void AppliedBuildingMigration_DoesNotReseed()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true, buildingApplied: true);
            save.world.buildings.Add(MakeBuilding(0));
            save.world.buildings.Add(MakeBuilding(1));
            save.world.nextBuildingId = 10;

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsTrue(result, reason);
            Assert.AreEqual(10, save.world.nextBuildingId, "Must not reseed nextBuildingId when already applied.");
        }

        [Test]
        public void PersistedBuildingNextIdBehindMax_Blocks()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true, buildingApplied: true);
            save.world.buildings.Add(MakeBuilding(0));
            save.world.buildings.Add(MakeBuilding(1));
            save.world.nextBuildingId = 1; // equal to max, must be strictly greater than 1

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void NegativePersistedBuildingNextId_Blocks()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true, buildingApplied: true);
            save.world.nextBuildingId = -1;

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void BuildingAllocatorState_RoundTripsThroughSaveCaptureAndLoad()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.seed = 12345;
            settings.gridWidthCells = 24;
            settings.gridDepthCells = 24;
            settings.cellSizeMeters = 2f;
            settings.generateRegionalFoundation = false;
            settings.Sanitize();

            GameObject root = new("Town World Allocator RoundTrip Test");
            TownWorldController controller = root.AddComponent<TownWorldController>();
            controller.Configure(settings, null, null);

            try
            {
                controller.RestoreBuildingIdAllocator(42);
                Assert.AreEqual(42, controller.NextBuildingId);

                WorldSaveDto captured = controller.CaptureSaveDto();
                Assert.AreEqual(42, captured.nextBuildingId);

                controller.ClearGeneratedTown();
                Assert.AreEqual(0, controller.NextBuildingId);

                bool loaded = controller.LoadFromSaveDto(captured, new SaveReferenceResolver(controller), out string message);
                Assert.IsTrue(loaded, message);
                Assert.AreEqual(42, controller.NextBuildingId);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void MigrationStep_RecordedExactlyOnce()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true, householdApplied: true);
            save.world.buildings.Add(MakeBuilding(0));

            bool firstRun = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason1);
            Assert.IsTrue(firstRun, reason1);
            Assert.IsTrue(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.BuildingIdAllocatorStepId));

            int stepCount = 0;
            for (int i = 0; i < save.migrationManifest.appliedSteps.Count; i++)
            {
                if (save.migrationManifest.appliedSteps[i].stepId == PersistentIdAllocatorMigration.BuildingIdAllocatorStepId)
                {
                    stepCount++;
                }
            }
            Assert.AreEqual(1, stepCount);

            bool secondRun = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason2);
            Assert.IsTrue(secondRun, reason2);

            stepCount = 0;
            for (int i = 0; i < save.migrationManifest.appliedSteps.Count; i++)
            {
                if (save.migrationManifest.appliedSteps[i].stepId == PersistentIdAllocatorMigration.BuildingIdAllocatorStepId)
                {
                    stepCount++;
                }
            }
            Assert.AreEqual(1, stepCount, "Migration step must be recorded exactly once.");
        }

        // ---------------------------------------------------------------
        // Atomicity
        // ---------------------------------------------------------------

        [Test]
        public void Atomicity_PersonStepFailsBeforeHouseholdStep_LoadFails()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest();
            save.population.people.Add(MakePerson(1));
            save.population.people.Add(MakePerson(1)); // duplicate → person fails

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
            Assert.IsFalse(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.HouseholdIdAllocatorStepId));
            Assert.IsFalse(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.BuildingIdAllocatorStepId));
        }

        [Test]
        public void Atomicity_HouseholdStepFailsBeforeBuildingStep_LoadFails()
        {
            LandLedgersSaveGameDto save = MakeSaveWithManifest(personApplied: true);
            save.population.households.Add(MakeHousehold(1));
            save.population.households.Add(MakeHousehold(1)); // duplicate → household fails

            bool result = PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string reason);

            Assert.IsFalse(result);
            Assert.IsNotEmpty(reason);
            Assert.IsFalse(SaveMigrationEnvelope.IsStepApplied(
                save.migrationManifest, PersistentIdAllocatorMigration.BuildingIdAllocatorStepId));
        }
    }
}
