using System;
using System.Collections.Generic;

namespace LandLedgers.Persistence
{
    public static class SaveMigrationEnvelope
    {
        public const int CurrentFormatVersion = 6;
        public const int MinimumSupportedFormatVersion = 0;
        public const int LegacyUnmanifestedFormatVersion = 5;
        public const string BootstrapMigrationStepId = "MR-P002_MIGRATION_MANIFEST_BOOTSTRAP";

        public static bool IsRawFormatVersionSupported(int formatVersion, string path, out string failureReason)
        {
            if (formatVersion < MinimumSupportedFormatVersion || formatVersion > CurrentFormatVersion)
            {
                failureReason = !string.IsNullOrWhiteSpace(path)
                    ? $"Unsupported save format {formatVersion} in {path}. Supported range is {MinimumSupportedFormatVersion} to {CurrentFormatVersion}."
                    : $"Unsupported save format {formatVersion}. Supported range is {MinimumSupportedFormatVersion} to {CurrentFormatVersion}.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        public static bool IsRawFormatVersionSupported(int formatVersion, out string failureReason)
        {
            return IsRawFormatVersionSupported(formatVersion, null, out failureReason);
        }

        public static bool TryInterpretAndValidateEnvelope(
            LandLedgersSaveGameDto save,
            int rawFormatVersion,
            out string failureReason)
        {
            failureReason = string.Empty;
            if (save == null)
            {
                failureReason = "Save root DTO is null.";
                return false;
            }

            // Legacy condition: formatVersion <= 5 and migrationManifest is missing.
            if (rawFormatVersion <= LegacyUnmanifestedFormatVersion && save.migrationManifest == null)
            {
                save.migrationManifest = BootstrapLegacyEnvelope(rawFormatVersion);
                return true;
            }

            // Version 6 missing manifest: corrupt M1 save.
            if (save.migrationManifest == null)
            {
                failureReason = $"Save format v{rawFormatVersion} requires a migrationManifest envelope, but none was found.";
                return false;
            }

            // Manifest is present: validate structure and consistency.
            return ValidateMigrationManifest(save.migrationManifest, rawFormatVersion, out failureReason);
        }

        public static bool ValidateMigrationManifest(
            MigrationManifestDto manifest,
            int rawFormatVersion,
            out string failureReason)
        {
            if (manifest == null)
            {
                failureReason = "Migration manifest is null.";
                return false;
            }

            if (manifest.schemaVersion < MinimumSupportedFormatVersion || manifest.schemaVersion > CurrentFormatVersion)
            {
                failureReason = $"Migration manifest schema version {manifest.schemaVersion} is outside supported range {MinimumSupportedFormatVersion} to {CurrentFormatVersion}.";
                return false;
            }

            if (rawFormatVersion == CurrentFormatVersion && manifest.schemaVersion != CurrentFormatVersion)
            {
                failureReason = $"Migration manifest schema version {manifest.schemaVersion} does not match root format version {rawFormatVersion}.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(manifest.migrationRunId))
            {
                failureReason = "Migration manifest migrationRunId is empty or whitespace.";
                return false;
            }

            if (manifest.originalSchemaVersion < MinimumSupportedFormatVersion || manifest.originalSchemaVersion > manifest.schemaVersion)
            {
                failureReason = $"Migration manifest originalSchemaVersion {manifest.originalSchemaVersion} is invalid relative to schemaVersion {manifest.schemaVersion}.";
                return false;
            }

            if (manifest.appliedSteps == null)
            {
                failureReason = "Migration manifest appliedSteps collection is null.";
                return false;
            }

            HashSet<string> seenStepIds = new(StringComparer.Ordinal);
            for (int i = 0; i < manifest.appliedSteps.Count; i++)
            {
                MigrationStepRecordDto step = manifest.appliedSteps[i];
                if (step == null)
                {
                    failureReason = $"Migration manifest applied step at index {i} is null.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(step.stepId))
                {
                    failureReason = $"Migration manifest applied step at index {i} has an empty or whitespace stepId.";
                    return false;
                }

                if (!seenStepIds.Add(step.stepId))
                {
                    failureReason = $"Migration manifest contains duplicate applied step id '{step.stepId}'.";
                    return false;
                }

                if (step.fromVersion < MinimumSupportedFormatVersion || step.toVersion < MinimumSupportedFormatVersion || step.toVersion < step.fromVersion)
                {
                    failureReason = $"Migration manifest applied step '{step.stepId}' has an invalid version range {step.fromVersion} -> {step.toVersion}.";
                    return false;
                }
            }

            failureReason = string.Empty;
            return true;
        }

        public static MigrationManifestDto BootstrapLegacyEnvelope(int sourceVersion)
        {
            MigrationManifestDto manifest = new()
            {
                schemaVersion = CurrentFormatVersion,
                originalSchemaVersion = sourceVersion,
                migrationRunId = GenerateMigrationRunId(),
                appliedSteps = new List<MigrationStepRecordDto>()
            };

            RecordStepSuccess(manifest, BootstrapMigrationStepId, sourceVersion, CurrentFormatVersion);
            return manifest;
        }

        public static bool IsStepApplied(MigrationManifestDto manifest, string stepId)
        {
            if (manifest == null || manifest.appliedSteps == null || string.IsNullOrWhiteSpace(stepId))
            {
                return false;
            }

            for (int i = 0; i < manifest.appliedSteps.Count; i++)
            {
                MigrationStepRecordDto step = manifest.appliedSteps[i];
                if (step != null && string.Equals(step.stepId, stepId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool RecordStepSuccess(MigrationManifestDto manifest, string stepId, int fromVersion, int toVersion)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (string.IsNullOrWhiteSpace(stepId))
            {
                throw new ArgumentException("Step ID cannot be null or whitespace.", nameof(stepId));
            }

            manifest.appliedSteps ??= new List<MigrationStepRecordDto>();

            if (IsStepApplied(manifest, stepId))
            {
                return false;
            }

            manifest.appliedSteps.Add(new MigrationStepRecordDto
            {
                stepId = stepId,
                appliedAtUtc = DateTime.UtcNow.ToString("O"),
                fromVersion = fromVersion,
                toVersion = toVersion
            });

            if (toVersion > manifest.schemaVersion)
            {
                manifest.schemaVersion = toVersion;
            }

            return true;
        }

        public static MigrationManifestDto CreateFreshManifest()
        {
            return new MigrationManifestDto
            {
                schemaVersion = CurrentFormatVersion,
                originalSchemaVersion = CurrentFormatVersion,
                migrationRunId = GenerateMigrationRunId(),
                appliedSteps = new List<MigrationStepRecordDto>()
            };
        }

        public static MigrationManifestDto CaptureForSave(MigrationManifestDto existing)
        {
            if (existing == null)
            {
                return CreateFreshManifest();
            }

            MigrationManifestDto copy = new()
            {
                schemaVersion = CurrentFormatVersion,
                originalSchemaVersion = existing.originalSchemaVersion >= MinimumSupportedFormatVersion && existing.originalSchemaVersion <= CurrentFormatVersion
                    ? existing.originalSchemaVersion
                    : CurrentFormatVersion,
                migrationRunId = !string.IsNullOrWhiteSpace(existing.migrationRunId)
                    ? existing.migrationRunId
                    : GenerateMigrationRunId(),
                appliedSteps = new List<MigrationStepRecordDto>(existing.appliedSteps != null ? existing.appliedSteps.Count : 0)
            };

            if (existing.appliedSteps != null)
            {
                for (int i = 0; i < existing.appliedSteps.Count; i++)
                {
                    MigrationStepRecordDto step = existing.appliedSteps[i];
                    if (step != null)
                    {
                        copy.appliedSteps.Add(new MigrationStepRecordDto
                        {
                            stepId = step.stepId,
                            appliedAtUtc = step.appliedAtUtc,
                            fromVersion = step.fromVersion,
                            toVersion = step.toVersion
                        });
                    }
                }
            }

            return copy;
        }

        public static string GenerateMigrationRunId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
