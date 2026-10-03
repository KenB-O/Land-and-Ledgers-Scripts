using System;
using System.Collections.Generic;

namespace LandLedgers.Persistence
{
    public static class PersistentIdAllocatorMigration
    {
        public const string PersonIdAllocatorStepId = "MR-P003_PERSON_ID_ALLOCATOR";
        public const string HouseholdIdAllocatorStepId = "MR-P003_HOUSEHOLD_ID_ALLOCATOR";
        public const string BuildingIdAllocatorStepId = "MR-P003_BUILDING_ID_ALLOCATOR";

        public static bool TryMigrateOrValidate(LandLedgersSaveGameDto save, out string failureReason)
        {
            failureReason = string.Empty;
            if (save == null)
            {
                failureReason = "Save root DTO is null.";
                return false;
            }

            if (save.migrationManifest == null)
            {
                failureReason = "Save migration manifest is missing.";
                return false;
            }

            if (save.population == null)
            {
                save.population = new PopulationSaveDto();
            }

            if (save.world == null)
            {
                save.world = new WorldSaveDto();
            }

            if (!TryMigrateOrValidatePersonAllocator(save, out failureReason))
            {
                return false;
            }

            if (!TryMigrateOrValidateHouseholdAllocator(save, out failureReason))
            {
                return false;
            }

            if (!TryMigrateOrValidateBuildingAllocator(save, out failureReason))
            {
                return false;
            }

            return true;
        }

        private static bool TryMigrateOrValidatePersonAllocator(LandLedgersSaveGameDto save, out string failureReason)
        {
            failureReason = string.Empty;
            bool isApplied = SaveMigrationEnvelope.IsStepApplied(save.migrationManifest, PersonIdAllocatorStepId);

            if (!isApplied)
            {
                if (save.population.people == null || save.population.people.Count == 0)
                {
                    save.population.nextPersonId = 0;
                    SaveMigrationEnvelope.RecordStepSuccess(
                        save.migrationManifest,
                        PersonIdAllocatorStepId,
                        save.migrationManifest.schemaVersion,
                        save.migrationManifest.schemaVersion);
                    return true;
                }

                HashSet<int> seenIds = new();
                int maxId = -1;
                for (int i = 0; i < save.population.people.Count; i++)
                {
                    PersonSaveDto person = save.population.people[i];
                    if (person == null)
                    {
                        continue;
                    }

                    if (person.id < 0)
                    {
                        failureReason = $"Corrupt save: Person at index {i} has negative ID ({person.id}).";
                        return false;
                    }

                    if (!seenIds.Add(person.id))
                    {
                        failureReason = $"Corrupt save: duplicate Person ID {person.id} detected.";
                        return false;
                    }

                    if (person.id == int.MaxValue)
                    {
                        failureReason = "Corrupt save: Person ID is int.MaxValue; allocator space exhausted.";
                        return false;
                    }

                    if (person.id > maxId)
                    {
                        maxId = person.id;
                    }
                }

                save.population.nextPersonId = maxId == -1 ? 0 : maxId + 1;
                SaveMigrationEnvelope.RecordStepSuccess(
                    save.migrationManifest,
                    PersonIdAllocatorStepId,
                    save.migrationManifest.schemaVersion,
                    save.migrationManifest.schemaVersion);
                return true;
            }
            else
            {
                if (save.population.nextPersonId < 0)
                {
                    failureReason = $"Save validation error: persisted nextPersonId ({save.population.nextPersonId}) is negative.";
                    return false;
                }

                HashSet<int> seenIds = new();
                int maxId = -1;
                if (save.population.people != null)
                {
                    for (int i = 0; i < save.population.people.Count; i++)
                    {
                        PersonSaveDto person = save.population.people[i];
                        if (person == null)
                        {
                            continue;
                        }

                        if (person.id < 0)
                        {
                            failureReason = $"Corrupt save: Person at index {i} has negative ID ({person.id}).";
                            return false;
                        }

                        if (!seenIds.Add(person.id))
                        {
                            failureReason = $"Corrupt save: duplicate Person ID {person.id} detected.";
                            return false;
                        }

                        if (person.id > maxId)
                        {
                            maxId = person.id;
                        }
                    }
                }

                if (maxId >= 0 && save.population.nextPersonId <= maxId)
                {
                    failureReason = $"Save validation error: persisted nextPersonId ({save.population.nextPersonId}) <= maximum existing Person ID ({maxId}).";
                    return false;
                }

                return true;
            }
        }

        private static bool TryMigrateOrValidateHouseholdAllocator(LandLedgersSaveGameDto save, out string failureReason)
        {
            failureReason = string.Empty;
            bool isApplied = SaveMigrationEnvelope.IsStepApplied(save.migrationManifest, HouseholdIdAllocatorStepId);

            if (!isApplied)
            {
                if (save.population.households == null || save.population.households.Count == 0)
                {
                    save.population.nextHouseholdId = 0;
                    SaveMigrationEnvelope.RecordStepSuccess(
                        save.migrationManifest,
                        HouseholdIdAllocatorStepId,
                        save.migrationManifest.schemaVersion,
                        save.migrationManifest.schemaVersion);
                    return true;
                }

                HashSet<int> seenIds = new();
                int maxId = -1;
                for (int i = 0; i < save.population.households.Count; i++)
                {
                    HouseholdSaveDto household = save.population.households[i];
                    if (household == null)
                    {
                        continue;
                    }

                    if (household.id < 0)
                    {
                        failureReason = $"Corrupt save: Household at index {i} has negative ID ({household.id}).";
                        return false;
                    }

                    if (!seenIds.Add(household.id))
                    {
                        failureReason = $"Corrupt save: duplicate Household ID {household.id} detected.";
                        return false;
                    }

                    if (household.id == int.MaxValue)
                    {
                        failureReason = "Corrupt save: Household ID is int.MaxValue; allocator space exhausted.";
                        return false;
                    }

                    if (household.id > maxId)
                    {
                        maxId = household.id;
                    }
                }

                save.population.nextHouseholdId = maxId == -1 ? 0 : maxId + 1;
                SaveMigrationEnvelope.RecordStepSuccess(
                    save.migrationManifest,
                    HouseholdIdAllocatorStepId,
                    save.migrationManifest.schemaVersion,
                    save.migrationManifest.schemaVersion);
                return true;
            }
            else
            {
                if (save.population.nextHouseholdId < 0)
                {
                    failureReason = $"Save validation error: persisted nextHouseholdId ({save.population.nextHouseholdId}) is negative.";
                    return false;
                }

                HashSet<int> seenIds = new();
                int maxId = -1;
                if (save.population.households != null)
                {
                    for (int i = 0; i < save.population.households.Count; i++)
                    {
                        HouseholdSaveDto household = save.population.households[i];
                        if (household == null)
                        {
                            continue;
                        }

                        if (household.id < 0)
                        {
                            failureReason = $"Corrupt save: Household at index {i} has negative ID ({household.id}).";
                            return false;
                        }

                        if (!seenIds.Add(household.id))
                        {
                            failureReason = $"Corrupt save: duplicate Household ID {household.id} detected.";
                            return false;
                        }

                        if (household.id > maxId)
                        {
                            maxId = household.id;
                        }
                    }
                }

                if (maxId >= 0 && save.population.nextHouseholdId <= maxId)
                {
                    failureReason = $"Save validation error: persisted nextHouseholdId ({save.population.nextHouseholdId}) <= maximum existing Household ID ({maxId}).";
                    return false;
                }

                return true;
            }
        }

        private static bool TryMigrateOrValidateBuildingAllocator(LandLedgersSaveGameDto save, out string failureReason)
        {
            failureReason = string.Empty;
            bool isApplied = SaveMigrationEnvelope.IsStepApplied(save.migrationManifest, BuildingIdAllocatorStepId);

            if (!isApplied)
            {
                if (save.world.buildings == null || save.world.buildings.Count == 0)
                {
                    save.world.nextBuildingId = 0;
                    SaveMigrationEnvelope.RecordStepSuccess(
                        save.migrationManifest,
                        BuildingIdAllocatorStepId,
                        save.migrationManifest.schemaVersion,
                        save.migrationManifest.schemaVersion);
                    return true;
                }

                HashSet<int> seenIds = new();
                int maxId = -1;
                for (int i = 0; i < save.world.buildings.Count; i++)
                {
                    BuildingSaveDto building = save.world.buildings[i];
                    if (building == null)
                    {
                        failureReason = $"Corrupt save: Building at index {i} is null.";
                        return false;
                    }

                    if (building.id < 0)
                    {
                        failureReason = $"Corrupt save: Building at index {i} has negative ID ({building.id}).";
                        return false;
                    }

                    if (!seenIds.Add(building.id))
                    {
                        failureReason = $"Corrupt save: duplicate Building ID {building.id} detected.";
                        return false;
                    }

                    if (building.id == int.MaxValue)
                    {
                        failureReason = "Corrupt save: Building ID is int.MaxValue; allocator space exhausted.";
                        return false;
                    }

                    if (building.id != i)
                    {
                        failureReason = $"Compatibility error: Building at index {i} has non-dense ID ({building.id} != {i}). Non-dense Building IDs cannot safely load before MR-P005 decoupling.";
                        return false;
                    }

                    if (building.id > maxId)
                    {
                        maxId = building.id;
                    }
                }

                save.world.nextBuildingId = maxId == -1 ? 0 : maxId + 1;
                SaveMigrationEnvelope.RecordStepSuccess(
                    save.migrationManifest,
                    BuildingIdAllocatorStepId,
                    save.migrationManifest.schemaVersion,
                    save.migrationManifest.schemaVersion);
                return true;
            }
            else
            {
                if (save.world.nextBuildingId < 0)
                {
                    failureReason = $"Save validation error: persisted nextBuildingId ({save.world.nextBuildingId}) is negative.";
                    return false;
                }

                HashSet<int> seenIds = new();
                int maxId = -1;
                if (save.world.buildings != null)
                {
                    for (int i = 0; i < save.world.buildings.Count; i++)
                    {
                        BuildingSaveDto building = save.world.buildings[i];
                        if (building == null)
                        {
                            failureReason = $"Corrupt save: Building at index {i} is null.";
                            return false;
                        }

                        if (building.id < 0)
                        {
                            failureReason = $"Corrupt save: Building at index {i} has negative ID ({building.id}).";
                            return false;
                        }

                        if (!seenIds.Add(building.id))
                        {
                            failureReason = $"Corrupt save: duplicate Building ID {building.id} detected.";
                            return false;
                        }

                        if (building.id != i)
                        {
                            failureReason = $"Compatibility error: Building at index {i} has non-dense ID ({building.id} != {i}). Non-dense Building IDs cannot safely load before MR-P005 decoupling.";
                            return false;
                        }

                        if (building.id > maxId)
                        {
                            maxId = building.id;
                        }
                    }
                }

                if (maxId >= 0 && save.world.nextBuildingId <= maxId)
                {
                    failureReason = $"Save validation error: persisted nextBuildingId ({save.world.nextBuildingId}) <= maximum existing Building ID ({maxId}).";
                    return false;
                }

                return true;
            }
        }
    }
}
