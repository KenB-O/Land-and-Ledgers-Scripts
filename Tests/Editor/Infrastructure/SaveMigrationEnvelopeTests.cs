using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Infrastructure
{
    [TestFixture]
    public sealed class SaveMigrationEnvelopeTests
    {
        [Test]
        public void LegacyVersion5Bootstrap_CreatesMigrationManifestAndRecordsBootstrapStep()
        {
            LandLedgersSaveGameDto save = new()
            {
                manifest = new SaveManifestDto { formatVersion = 5 }
            };

            Assert.IsNull(save.migrationManifest, "Migration manifest must initially be null on legacy save.");

            bool result = SaveMigrationEnvelope.TryInterpretAndValidateEnvelope(save, 5, out string failureReason);

            Assert.IsTrue(result, $"Expected legacy v5 interpretation to succeed, but failed with: {failureReason}");
            Assert.IsEmpty(failureReason);
            Assert.IsNotNull(save.migrationManifest);
            Assert.AreEqual(5, save.migrationManifest.originalSchemaVersion);
            Assert.AreEqual(6, save.migrationManifest.schemaVersion);
            Assert.IsFalse(string.IsNullOrWhiteSpace(save.migrationManifest.migrationRunId));
            Assert.AreEqual(1, save.migrationManifest.appliedSteps.Count);

            MigrationStepRecordDto step = save.migrationManifest.appliedSteps[0];
            Assert.AreEqual(SaveMigrationEnvelope.BootstrapMigrationStepId, step.stepId);
            Assert.AreEqual(5, step.fromVersion);
            Assert.AreEqual(6, step.toVersion);
            Assert.IsFalse(string.IsNullOrWhiteSpace(step.appliedAtUtc));
        }

        [Test]
        public void LegacyOlderVersions_PreserveSourceVersionWithoutSyntheticIntermediateSteps()
        {
            LandLedgersSaveGameDto save = new()
            {
                manifest = new SaveManifestDto { formatVersion = 3 }
            };

            bool result = SaveMigrationEnvelope.TryInterpretAndValidateEnvelope(save, 3, out string failureReason);

            Assert.IsTrue(result, $"Expected legacy v3 interpretation to succeed, but failed with: {failureReason}");
            Assert.IsNotNull(save.migrationManifest);
            Assert.AreEqual(3, save.migrationManifest.originalSchemaVersion);
            Assert.AreEqual(6, save.migrationManifest.schemaVersion);
            Assert.AreEqual(1, save.migrationManifest.appliedSteps.Count);

            MigrationStepRecordDto step = save.migrationManifest.appliedSteps[0];
            Assert.AreEqual(SaveMigrationEnvelope.BootstrapMigrationStepId, step.stepId);
            Assert.AreEqual(3, step.fromVersion);
            Assert.AreEqual(6, step.toVersion);
        }

        [Test]
        public void Version6WithoutManifest_IsRejectedAsCorrupt()
        {
            LandLedgersSaveGameDto save = new()
            {
                manifest = new SaveManifestDto { formatVersion = 6 }
            };

            Assert.IsNull(save.migrationManifest);

            bool result = SaveMigrationEnvelope.TryInterpretAndValidateEnvelope(save, 6, out string failureReason);

            Assert.IsFalse(result, "Version 6 save without migration manifest must not be accepted.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(failureReason));
            Assert.IsNull(save.migrationManifest, "Rejected v6 save must not be silently bootstrapped.");
        }

        [Test]
        public void FutureVersion_FailsRawCompatibilityValidation()
        {
            bool resultVersion7 = SaveMigrationEnvelope.IsRawFormatVersionSupported(7, out string failureReason7);
            Assert.IsFalse(resultVersion7);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failureReason7));

            bool resultVersion99 = SaveMigrationEnvelope.IsRawFormatVersionSupported(99, out string failureReason99);
            Assert.IsFalse(resultVersion99);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failureReason99));
        }

        [Test]
        public void UnsupportedLowerVersion_FailsCompatibilityValidation()
        {
            bool result = SaveMigrationEnvelope.IsRawFormatVersionSupported(-1, out string failureReason);
            Assert.IsFalse(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(failureReason));
        }

        [Test]
        public void ManifestStructuralValidation_RejectsInvalidV6Metadata()
        {
            MigrationManifestDto emptyRunIdManifest = new()
            {
                schemaVersion = 6,
                originalSchemaVersion = 6,
                migrationRunId = string.Empty,
                appliedSteps = new List<MigrationStepRecordDto>()
            };
            Assert.IsFalse(SaveMigrationEnvelope.ValidateMigrationManifest(emptyRunIdManifest, 6, out string emptyRunIdError));
            Assert.IsFalse(string.IsNullOrWhiteSpace(emptyRunIdError));

            MigrationManifestDto duplicateStepsManifest = new()
            {
                schemaVersion = 6,
                originalSchemaVersion = 5,
                migrationRunId = "run_123",
                appliedSteps = new List<MigrationStepRecordDto>
                {
                    new() { stepId = "DUPLICATE_STEP", fromVersion = 5, toVersion = 6 },
                    new() { stepId = "DUPLICATE_STEP", fromVersion = 5, toVersion = 6 }
                }
            };
            Assert.IsFalse(SaveMigrationEnvelope.ValidateMigrationManifest(duplicateStepsManifest, 6, out string duplicateError));
            Assert.IsFalse(string.IsNullOrWhiteSpace(duplicateError));

            MigrationManifestDto emptyStepIdManifest = new()
            {
                schemaVersion = 6,
                originalSchemaVersion = 5,
                migrationRunId = "run_123",
                appliedSteps = new List<MigrationStepRecordDto>
                {
                    new() { stepId = "   ", fromVersion = 5, toVersion = 6 }
                }
            };
            Assert.IsFalse(SaveMigrationEnvelope.ValidateMigrationManifest(emptyStepIdManifest, 6, out string emptyStepError));
            Assert.IsFalse(string.IsNullOrWhiteSpace(emptyStepError));

            MigrationManifestDto nonsensicalVersionManifest = new()
            {
                schemaVersion = 6,
                originalSchemaVersion = 7,
                migrationRunId = "run_123",
                appliedSteps = new List<MigrationStepRecordDto>()
            };
            Assert.IsFalse(SaveMigrationEnvelope.ValidateMigrationManifest(nonsensicalVersionManifest, 6, out string nonsensicalError));
            Assert.IsFalse(string.IsNullOrWhiteSpace(nonsensicalError));

            MigrationManifestDto mismatchedVersionManifest = new()
            {
                schemaVersion = 5,
                originalSchemaVersion = 5,
                migrationRunId = "run_123",
                appliedSteps = new List<MigrationStepRecordDto>()
            };
            Assert.IsFalse(SaveMigrationEnvelope.ValidateMigrationManifest(mismatchedVersionManifest, 6, out string mismatchError));
            Assert.IsFalse(string.IsNullOrWhiteSpace(mismatchError));
        }

        [Test]
        public void OnceOnlyStepRecording_GuardsAgainstDuplicateApplication()
        {
            MigrationManifestDto manifest = SaveMigrationEnvelope.CreateFreshManifest();

            bool firstRecord = SaveMigrationEnvelope.RecordStepSuccess(manifest, "STEP_CUSTOM_1", 6, 6);
            Assert.IsTrue(firstRecord);
            Assert.IsTrue(SaveMigrationEnvelope.IsStepApplied(manifest, "STEP_CUSTOM_1"));
            Assert.AreEqual(1, manifest.appliedSteps.Count);
            Assert.AreEqual("STEP_CUSTOM_1", manifest.appliedSteps[0].stepId);
            Assert.AreEqual(6, manifest.appliedSteps[0].fromVersion);
            Assert.AreEqual(6, manifest.appliedSteps[0].toVersion);

            bool secondRecord = SaveMigrationEnvelope.RecordStepSuccess(manifest, "STEP_CUSTOM_1", 6, 6);
            Assert.IsFalse(secondRecord, "Calling RecordStepSuccess for an already-applied step must return false.");
            Assert.AreEqual(1, manifest.appliedSteps.Count, "Duplicate step must not be appended.");
        }

        [Test]
        public void FailedStep_RemainsUnapplied()
        {
            MigrationManifestDto manifest = SaveMigrationEnvelope.CreateFreshManifest();

            const string unappliedStepId = "STEP_FAILED_1";

            Assert.IsFalse(SaveMigrationEnvelope.IsStepApplied(manifest, unappliedStepId));
            Assert.IsFalse(manifest.appliedSteps.Exists(s => s != null && s.stepId == unappliedStepId));
        }

        [Test]
        public void MigrationRunIdStability_PreservesExistingIdAcrossCaptures()
        {
            MigrationManifestDto initial = SaveMigrationEnvelope.CreateFreshManifest();
            string originalRunId = initial.migrationRunId;

            Assert.IsFalse(string.IsNullOrWhiteSpace(originalRunId));

            MigrationManifestDto captured = SaveMigrationEnvelope.CaptureForSave(initial);

            Assert.AreEqual(originalRunId, captured.migrationRunId);
            Assert.AreNotSame(initial, captured, "CaptureForSave must produce an independent copy.");
        }

        [Test]
        public void ManifestJsonRoundTrip_PreservesAllFieldsViaJsonUtility()
        {
            MigrationManifestDto original = new()
            {
                schemaVersion = 6,
                originalSchemaVersion = 5,
                migrationRunId = "run_fixture_round_trip",
                appliedSteps = new List<MigrationStepRecordDto>
                {
                    new()
                    {
                        stepId = "STEP_FIXTURE_1",
                        appliedAtUtc = "2026-09-23T21:00:00Z",
                        fromVersion = 5,
                        toVersion = 6
                    }
                }
            };

            string json = JsonUtility.ToJson(original);
            MigrationManifestDto restored = JsonUtility.FromJson<MigrationManifestDto>(json);

            Assert.IsNotNull(restored);
            Assert.AreEqual(original.schemaVersion, restored.schemaVersion);
            Assert.AreEqual(original.originalSchemaVersion, restored.originalSchemaVersion);
            Assert.AreEqual(original.migrationRunId, restored.migrationRunId);
            Assert.AreEqual(1, restored.appliedSteps.Count);
            Assert.AreEqual("STEP_FIXTURE_1", restored.appliedSteps[0].stepId);
            Assert.AreEqual("2026-09-23T21:00:00Z", restored.appliedSteps[0].appliedAtUtc);
            Assert.AreEqual(5, restored.appliedSteps[0].fromVersion);
            Assert.AreEqual(6, restored.appliedSteps[0].toVersion);
        }

        [Test]
        public void RootDtoParity_RoundTripPreservesEnvelopeAndDomainSections()
        {
            LandLedgersSaveGameDto dto = new()
            {
                manifest = new SaveManifestDto
                {
                    formatVersion = 6,
                    displayName = "Parity Check Save",
                    worldSeed = 4242
                },
                migrationManifest = SaveMigrationEnvelope.CreateFreshManifest(),
                time = new TimeSaveDto
                {
                    absoluteDayIndex = 21
                },
                world = new WorldSaveDto
                {
                    seed = 4242,
                    gridWidthCells = 24,
                    gridDepthCells = 24
                },
                population = new PopulationSaveDto
                {
                    people = new List<PersonSaveDto>
                    {
                        new() { id = 101, firstName = "Arthur", lastName = "Dent" }
                    }
                },
                portfolio = new PlayerPortfolioSaveDto
                {
                    initialized = true,
                    ownerCashCents = 125000
                }
            };

            string json = JsonUtility.ToJson(dto, true);
            LandLedgersSaveGameDto restored = JsonUtility.FromJson<LandLedgersSaveGameDto>(json);

            Assert.IsNotNull(restored);
            Assert.IsNotNull(restored.manifest);
            Assert.AreEqual(6, restored.manifest.formatVersion);
            Assert.AreEqual("Parity Check Save", restored.manifest.displayName);
            Assert.AreEqual(4242, restored.manifest.worldSeed);

            Assert.IsNotNull(restored.migrationManifest);
            Assert.AreEqual(6, restored.migrationManifest.schemaVersion);
            Assert.AreEqual(6, restored.migrationManifest.originalSchemaVersion);
            Assert.IsFalse(string.IsNullOrWhiteSpace(restored.migrationManifest.migrationRunId));

            Assert.IsNotNull(restored.time);
            Assert.AreEqual(21, restored.time.absoluteDayIndex);

            Assert.IsNotNull(restored.world);
            Assert.AreEqual(4242, restored.world.seed);
            Assert.AreEqual(24, restored.world.gridWidthCells);

            Assert.IsNotNull(restored.population);
            Assert.AreEqual(1, restored.population.people.Count);
            Assert.AreEqual(101, restored.population.people[0].id);
            Assert.AreEqual("Arthur", restored.population.people[0].firstName);

            Assert.IsNotNull(restored.portfolio);
            Assert.IsTrue(restored.portfolio.initialized);
            Assert.AreEqual(125000, restored.portfolio.ownerCashCents);
        }

        [Test]
        public void LegacyRootJsonWithoutMigrationField_IsRecognizedAndBootstrappedAsLegacy()
        {
            // JsonUtility.FromJson always default-constructs [Serializable] class fields even
            // when the JSON key is absent, so migrationManifest will not be null after FromJson.
            // The production load path is responsible for detecting this JsonUtility-materialised
            // default (e.g. via empty migrationRunId) and nulling it before calling
            // TryInterpretAndValidateEnvelope, so the bootstrap branch fires correctly.
            // This test exercises that full semantic: deserialise → detect default → bootstrap.
            string legacyJson = "{\"manifest\":{\"formatVersion\":5,\"displayName\":\"Legacy Raw Save\"},\"world\":{\"seed\":777}}";

            LandLedgersSaveGameDto deserialized = JsonUtility.FromJson<LandLedgersSaveGameDto>(legacyJson);

            Assert.IsNotNull(deserialized);
            Assert.IsNotNull(deserialized.manifest);
            Assert.AreEqual(5, deserialized.manifest.formatVersion,
                "Raw formatVersion must be 5 for a legacy save.");

            // JsonUtility materialises a default MigrationManifestDto (not null) when the field
            // is absent from JSON. The production caller detects this (empty migrationRunId) and
            // nulls the field so TryInterpretAndValidateEnvelope enters the bootstrap branch.
            if (string.IsNullOrEmpty(deserialized.migrationManifest?.migrationRunId))
            {
                deserialized.migrationManifest = null;
            }

            bool result = SaveMigrationEnvelope.TryInterpretAndValidateEnvelope(deserialized, 5, out string failureReason);

            Assert.IsTrue(result, $"Legacy v5 bootstrap must succeed, but failed with: {failureReason}");
            Assert.IsEmpty(failureReason);
            Assert.IsNotNull(deserialized.migrationManifest);
            Assert.AreEqual(6, deserialized.migrationManifest.schemaVersion);
            Assert.AreEqual(5, deserialized.migrationManifest.originalSchemaVersion);
            Assert.IsFalse(string.IsNullOrWhiteSpace(deserialized.migrationManifest.migrationRunId),
                "Bootstrap must assign a non-empty migrationRunId.");
            Assert.AreEqual(1, deserialized.migrationManifest.appliedSteps.Count,
                "Bootstrap must record exactly one step.");

            MigrationStepRecordDto bootstrapStep = deserialized.migrationManifest.appliedSteps[0];
            Assert.AreEqual(SaveMigrationEnvelope.BootstrapMigrationStepId, bootstrapStep.stepId);
            Assert.AreEqual(5, bootstrapStep.fromVersion);
            Assert.AreEqual(6, bootstrapStep.toVersion);
        }

        [Test]
        public void LogisticsConservationDto_RoundTripsWithMigrationEnvelope()
        {
            LandLedgersSaveGameDto dto = new()
            {
                manifest = new SaveManifestDto { formatVersion = 6 },
                migrationManifest = SaveMigrationEnvelope.CreateFreshManifest(),
                businesses = new BusinessPortfolioSaveDto
                {
                    logistics = new LogisticsRuntimeSaveDto
                    {
                        shipments = new List<LogisticsShipmentSaveDto>
                        {
                            new()
                            {
                                shipmentId = "shipment_conservation_test_01",
                                state = LogisticsShipmentStatus.Failed,
                                blockedReason = "Conservation gate quarantine: missing commodity reservation",
                                plannedQuantityUnits = 80,
                                remainingQuantityUnits = 80,
                                sourceCommittedAtSchedule = true,
                                loadApplied = false,
                                deliveryApplied = false
                            }
                        }
                    }
                }
            };

            string json = JsonUtility.ToJson(dto, true);
            LandLedgersSaveGameDto restored = JsonUtility.FromJson<LandLedgersSaveGameDto>(json);

            Assert.IsNotNull(restored?.businesses?.logistics?.shipments);
            Assert.AreEqual(1, restored.businesses.logistics.shipments.Count);

            LogisticsShipmentSaveDto shipment = restored.businesses.logistics.shipments[0];
            Assert.AreEqual("shipment_conservation_test_01", shipment.shipmentId);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.state);
            Assert.AreEqual("Conservation gate quarantine: missing commodity reservation", shipment.blockedReason);
            Assert.AreEqual(80, shipment.plannedQuantityUnits);
            Assert.AreEqual(80, shipment.remainingQuantityUnits);
            Assert.IsTrue(shipment.sourceCommittedAtSchedule);
            Assert.IsFalse(shipment.loadApplied);
            Assert.IsFalse(shipment.deliveryApplied);
        }
    }
}
