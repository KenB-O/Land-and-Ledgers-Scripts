using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Animals
{
    /// <summary>Species supported by the animal authority. Breed is a free string.</summary>
    public enum AnimalSpecies
    {
        Unspecified = 0,
        Chicken = 1,
        Cattle = 2,
        Pig = 3,
        Sheep = 4,
        Horse = 5,
        Goat = 6,
        Other = 7,
        /// <summary>
        /// EQP-5: mules and oxen as first-class motive power (Tech X §3.6 lists
        /// human, horse, mule, ox, water, steam, electric). Previously these
        /// registered as Other, losing their draft identity.
        /// </summary>
        Mule = 8,
        Ox = 9,
    }

    /// <summary>Sex when known. Tech X §2.3: male chicks remain present in records.</summary>
    public enum AnimalSex
    {
        Unknown = 0,
        Female = 1,
        Male = 2,
    }

    /// <summary>
    /// Biological/reproductive state (Tech X §4.4, simplified to gameplay-relevant stages).
    /// Parity/calving history is stored independently of this state (Tech X §4.5).
    /// </summary>
    public enum AnimalBiologicalState
    {
        Unspecified = 0,
        Juvenile = 1,
        NotBreedingReady = 2,
        EligibleOrCycling = 3,
        Serviced = 4,
        PregnancySuspected = 5,
        LateGestation = 6,
        Postpartum = 7,
        Lactating = 8,
        Dry = 9,
        ReproductiveFailureEvidence = 10,
        Neutered = 11,
    }

    /// <summary>
    /// Market-facing commercial classification (Tech X §6.2: derived read model values plus
    /// terminal disposition states). Classification derives from animal state; the terminal
    /// states below record final disposition, never delete history.
    /// </summary>
    public enum AnimalCommercialStatus
    {
        Unspecified = 0,
        BreedingStock = 1,
        BreedingProspect = 2,
        StockerFeeder = 3,
        FinishedCommercial = 4,
        Cull = 5,
        Sold = 6,
        Slaughtered = 7,
        Deceased = 8,
        Lost = 9,
    }

    /// <summary>Who can own an animal.</summary>
    public enum AnimalOwnerKind
    {
        Unspecified = 0,
        Person = 1,
        Household = 2,
        Business = 3,
    }

    /// <summary>
    /// One ownership tenure. History is append-only: transfers close the open record and
    /// append a new one; past records are never rewritten (Tech X §3.2).
    /// </summary>
    [Serializable]
    public sealed class AnimalOwnershipRecord
    {
        public AnimalOwnerKind OwnerKind;
        public string OwnerId = string.Empty;
        public int StartDayIndex;
        public int EndDayIndex = -1; // -1 = current
        public string Reason = string.Empty;

        public bool IsCurrent => EndDayIndex < 0;
    }

    /// <summary>
    /// A pedigree CLAIM (Tech X §4.1: ActualPedigree vs RecordedPedigree). Actual biological
    /// links live on <see cref="AnimalState.Dam"/> / <see cref="AnimalState.Sire"/>; claims
    /// carry source, confidence and claimant and never overwrite the actual links.
    /// Unknown ancestry propagates as Invalid links, never as zero relatedness.
    /// </summary>
    [Serializable]
    public sealed class PedigreeClaim
    {
        public EntityId ClaimedDam = EntityId.Invalid;
        public EntityId ClaimedSire = EntityId.Invalid;
        public string Source = string.Empty;
        public int DayRecorded;
        public float Confidence01;
        public string Documentation = string.Empty;
        public string Claimant = string.Empty;
    }

    /// <summary>One production/reproductive history entry (eggs, milk, calving, ...).</summary>
    [Serializable]
    public sealed class AnimalProductionRecord
    {
        public int DayIndex;
        public string Kind = string.Empty;
        public int Quantity;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// HF-2: the persistent individual animal authority (Tech X Part III §3.1).
    /// Identity is an HF-1 <see cref="EntityId"/> of kind Animal — never reused
    /// (Tech X §3.2 TECH LOCK).
    /// </summary>
    [Serializable]
    public sealed class AnimalState
    {
        public EntityId AnimalId = EntityId.Invalid;
        public AnimalSpecies Species;
        public string Breed = string.Empty;
        public AnimalSex Sex;
        public int BirthDayIndex = -1; // -1 = unknown; never fabricate an exact date (Tech X §8.x)
        public string BirthPlace = string.Empty;
        public AnimalOwnerKind OwnerKind;
        public string OwnerId = string.Empty;
        public int LocationBuildingId = -1;
        public EntityId Dam = EntityId.Invalid; // actual biological dam (Tech X §4.1)
        public EntityId Sire = EntityId.Invalid; // actual biological sire
        public AnimalBiologicalState BiologicalState;
        public int Parity; // calving/hatching count, independent of BiologicalState (Tech X §4.5)
        public AnimalCommercialStatus CommercialStatus;
        public string Disposition = string.Empty; // terminal detail, e.g. "sold at market", "died: cold snap"
        public bool IsActive = true;
        public EntityId CohortId = EntityId.Invalid; // set when promoted from a cohort (Tech X §3.4)
        public List<AnimalOwnershipRecord> OwnershipHistory = new List<AnimalOwnershipRecord>();
        public List<PedigreeClaim> PedigreeClaims = new List<PedigreeClaim>();
        public List<AnimalProductionRecord> ProductionHistory = new List<AnimalProductionRecord>();
        public string HealthNotes = string.Empty;
    }

    /// <summary>
    /// HF-2: lightweight historical record (Tech X §3.3). Inactive animals compact here;
    /// identifiers and pedigree-relevant fields are retained for audit and lineage.
    /// </summary>
    [Serializable]
    public sealed class HistoricalAnimalRecord
    {
        public EntityId AnimalId = EntityId.Invalid;
        public AnimalSpecies Species;
        public string Breed = string.Empty;
        public AnimalSex Sex;
        public int BirthDayIndex = -1;
        public EntityId Dam = EntityId.Invalid;
        public EntityId Sire = EntityId.Invalid;
        public AnimalCommercialStatus FinalStatus;
        public string DispositionSummary = string.Empty;
        public int LastActiveDayIndex = -1;
    }

    /// <summary>
    /// HF-2: cohort representation for large commercial groups where individual
    /// differentiation is immaterial (Tech X §3.4). Promotion to individual representation
    /// moves a head out of the cohort count — never duplicating ownership.
    /// </summary>
    [Serializable]
    public sealed class LivestockCohort
    {
        public EntityId CohortId = EntityId.Invalid;
        public AnimalSpecies Species;
        public string Breed = string.Empty;
        public int HeadCount;
        public int LocationBuildingId = -1;
        public int AcquisitionDayIndex;
        public AnimalOwnerKind OwnerKind;
        public string OwnerId = string.Empty;
        public string Notes = string.Empty;

        public bool IsValid => CohortId.IsValid && CohortId.Kind == EntityKind.AnimalCohort && HeadCount >= 0;
    }

    /// <summary>
    /// HF-2: egg batch / clutch record (Tech X §2.3 poultry lifecycle). The batch is an
    /// economic Lot (perishable provenance, Tech X §4.1); hatching promotes eggs to
    /// individual chick <see cref="AnimalState"/> records with parentage.
    /// </summary>
    [Serializable]
    public sealed class EggBatchState
    {
        public EntityId BatchId = EntityId.Invalid; // EntityKind.Lot
        public EntityId DamId = EntityId.Invalid;
        public EntityId SireId = EntityId.Invalid; // Invalid = unknown sire
        public int LaidDayIndex;
        public int EggCount;
        public int FertileEggCount = -1; // -1 = unknown
        public int HatchDayIndex = -1;
        public int HatchedChickCount;
        public int LocationBuildingId = -1;
        public string Notes = string.Empty;
    }

    /// <summary>
    /// HF-2: shared causal exposure record (Tech X §5.5 TECH LOCK). Weather and disease
    /// events affect multiple animals through ONE shared record — never independent
    /// identical per-animal rolls. Identity is a local sequence; promote to a dedicated
    /// EntityKind via the HF-1 adoption path if incidents need cross-system references.
    /// </summary>
    [Serializable]
    public sealed class AnimalHealthIncident
    {
        public int IncidentIndex;
        public int DayIndex;
        public string Cause = string.Empty;
        public List<EntityId> AffectedAnimalIds = new List<EntityId>();
        public string Notes = string.Empty;
    }

    /// <summary>
    /// HF-2: the persistent animal authority. Owns individual records, cohorts, egg batches
    /// and health incidents. IDs come from the shared HF-1 <see cref="EntityIdRegistry"/>
    /// (kind Animal / AnimalCohort / Lot) and are never reused.
    /// </summary>
    public sealed class AnimalRegistry
    {
        private readonly EntityIdRegistry idRegistry;
        private readonly Dictionary<EntityId, AnimalState> animals = new Dictionary<EntityId, AnimalState>();
        private readonly Dictionary<EntityId, HistoricalAnimalRecord> historical =
            new Dictionary<EntityId, HistoricalAnimalRecord>();
        private readonly Dictionary<EntityId, LivestockCohort> cohorts = new Dictionary<EntityId, LivestockCohort>();
        private readonly Dictionary<EntityId, EggBatchState> eggBatches = new Dictionary<EntityId, EggBatchState>();
        private readonly List<AnimalHealthIncident> healthIncidents = new List<AnimalHealthIncident>();
        private readonly List<string> diagnostics = new List<string>();
        private int nextIncidentIndex;

        public AnimalRegistry(EntityIdRegistry idRegistry)
        {
            this.idRegistry = idRegistry ?? throw new ArgumentNullException(nameof(idRegistry));
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Registers a new individual animal, allocating its permanent AnimalId.</summary>
        public AnimalState RegisterAnimal(
            AnimalSpecies species,
            string breed,
            AnimalSex sex,
            AnimalOwnerKind ownerKind,
            string ownerId,
            int dayIndex,
            string reason)
        {
            var animal = new AnimalState
            {
                AnimalId = idRegistry.Allocate(EntityKind.Animal),
                Species = species,
                Breed = breed ?? string.Empty,
                Sex = sex,
                OwnerKind = ownerKind,
                OwnerId = ownerId ?? string.Empty,
                BiologicalState = AnimalBiologicalState.Juvenile,
                CommercialStatus = AnimalCommercialStatus.BreedingProspect,
                IsActive = true,
            };
            animal.OwnershipHistory.Add(new AnimalOwnershipRecord
            {
                OwnerKind = ownerKind,
                OwnerId = ownerId ?? string.Empty,
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
                Reason = reason ?? "registered",
            });

            string rejection = ValidateAndAdd(animal);
            if (rejection != null)
            {
                diagnostics.Add(rejection);
                return null;
            }

            return animal;
        }

        public AnimalState GetAnimal(EntityId animalId)
        {
            return animals.TryGetValue(animalId, out AnimalState animal) ? animal : null;
        }

        public HistoricalAnimalRecord GetHistorical(EntityId animalId)
        {
            return historical.TryGetValue(animalId, out HistoricalAnimalRecord record) ? record : null;
        }

        public int ActiveCount => animals.Count;
        public int HistoricalCount => historical.Count;

        /// <summary>
        /// Ownership transfer: closes the current tenure and appends a new one. History is
        /// never rewritten (Tech X §3.2).
        /// </summary>
        public string TransferOwnership(
            EntityId animalId,
            AnimalOwnerKind newOwnerKind,
            string newOwnerId,
            int dayIndex,
            string reason)
        {
            if (!animals.TryGetValue(animalId, out AnimalState animal))
            {
                return $"TransferOwnership: unknown animal {animalId}.";
            }

            for (int i = 0; i < animal.OwnershipHistory.Count; i++)
            {
                AnimalOwnershipRecord record = animal.OwnershipHistory[i];
                if (record != null && record.IsCurrent)
                {
                    record.EndDayIndex = dayIndex;
                }
            }

            animal.OwnerKind = newOwnerKind;
            animal.OwnerId = newOwnerId ?? string.Empty;
            animal.OwnershipHistory.Add(new AnimalOwnershipRecord
            {
                OwnerKind = newOwnerKind,
                OwnerId = newOwnerId ?? string.Empty,
                StartDayIndex = dayIndex,
                EndDayIndex = -1,
                Reason = reason ?? "transfer",
            });
            return null;
        }

        /// <summary>
        /// Records final disposition. Terminal states retire the animal to historical
        /// records on the next <see cref="CompactInactive"/>; the ID is never reused.
        /// </summary>
        public string RecordDisposition(EntityId animalId, AnimalCommercialStatus status, int dayIndex, string notes)
        {
            if (!animals.TryGetValue(animalId, out AnimalState animal))
            {
                return $"RecordDisposition: unknown animal {animalId}.";
            }

            if (!IsTerminalStatus(status))
            {
                return $"RecordDisposition: {status} is not a terminal disposition for {animalId}.";
            }

            animal.CommercialStatus = status;
            animal.Disposition = notes ?? string.Empty;
            animal.IsActive = false;
            animal.HealthNotes = string.IsNullOrWhiteSpace(animal.HealthNotes)
                ? $"Disposition day {dayIndex}: {notes}"
                : $"{animal.HealthNotes} | Disposition day {dayIndex}: {notes}";
            return null;
        }

        public bool IsTerminalStatus(AnimalCommercialStatus status)
        {
            return status == AnimalCommercialStatus.Sold
                || status == AnimalCommercialStatus.Slaughtered
                || status == AnimalCommercialStatus.Deceased
                || status == AnimalCommercialStatus.Lost;
        }

        /// <summary>
        /// Compacts inactive animals into lightweight historical records (Tech X §3.3),
        /// retaining identifiers and pedigree fields. Returns the compacted count.
        /// </summary>
        public int CompactInactive(int dayIndex)
        {
            var toCompact = new List<EntityId>();
            foreach (KeyValuePair<EntityId, AnimalState> entry in animals)
            {
                if (entry.Value != null && !entry.Value.IsActive && IsTerminalStatus(entry.Value.CommercialStatus))
                {
                    toCompact.Add(entry.Key);
                }
            }

            foreach (EntityId id in toCompact)
            {
                AnimalState animal = animals[id];
                historical[id] = new HistoricalAnimalRecord
                {
                    AnimalId = animal.AnimalId,
                    Species = animal.Species,
                    Breed = animal.Breed,
                    Sex = animal.Sex,
                    BirthDayIndex = animal.BirthDayIndex,
                    Dam = animal.Dam,
                    Sire = animal.Sire,
                    FinalStatus = animal.CommercialStatus,
                    DispositionSummary = animal.Disposition,
                    LastActiveDayIndex = dayIndex,
                };
                animals.Remove(id);
            }

            return toCompact.Count;
        }

        /// <summary>
        /// Creates a cohort (Tech X §3.4). Individual differentiation is immaterial until
        /// promotion.
        /// </summary>
        public LivestockCohort RegisterCohort(
            AnimalSpecies species,
            string breed,
            int headCount,
            AnimalOwnerKind ownerKind,
            string ownerId,
            int acquisitionDayIndex)
        {
            if (headCount < 0)
            {
                diagnostics.Add("RegisterCohort: head count cannot be negative.");
                return null;
            }

            var cohort = new LivestockCohort
            {
                CohortId = idRegistry.Allocate(EntityKind.AnimalCohort),
                Species = species,
                Breed = breed ?? string.Empty,
                HeadCount = headCount,
                OwnerKind = ownerKind,
                OwnerId = ownerId ?? string.Empty,
                AcquisitionDayIndex = acquisitionDayIndex,
            };
            cohorts[cohort.CohortId] = cohort;
            return cohort;
        }

        public LivestockCohort GetCohort(EntityId cohortId)
        {
            return cohorts.TryGetValue(cohortId, out LivestockCohort cohort) ? cohort : null;
        }

        /// <summary>
        /// Promotes one head from a cohort to individual representation (Tech X §3.4): the
        /// cohort count decrements and the new individual links back to the cohort. No
        /// duplicate ownership is created.
        /// </summary>
        public AnimalState PromoteCohortHead(EntityId cohortId, AnimalSex sex, int dayIndex, string notes)
        {
            if (!cohorts.TryGetValue(cohortId, out LivestockCohort cohort))
            {
                diagnostics.Add($"PromoteCohortHead: unknown cohort {cohortId}.");
                return null;
            }

            if (cohort.HeadCount <= 0)
            {
                diagnostics.Add($"PromoteCohortHead: cohort {cohortId} has no heads left to promote.");
                return null;
            }

            cohort.HeadCount--;
            AnimalState animal = RegisterAnimal(
                cohort.Species,
                cohort.Breed,
                sex,
                cohort.OwnerKind,
                cohort.OwnerId,
                dayIndex,
                string.IsNullOrWhiteSpace(notes) ? $"promoted from cohort {cohortId}" : notes);
            if (animal != null)
            {
                animal.CohortId = cohortId;
            }

            return animal;
        }

        /// <summary>
        /// Records one shared causal exposure affecting multiple animals (Tech X §5.5).
        /// </summary>
        public AnimalHealthIncident RecordHealthIncident(
            int dayIndex,
            string cause,
            IEnumerable<EntityId> affectedAnimalIds,
            string notes)
        {
            var incident = new AnimalHealthIncident
            {
                IncidentIndex = nextIncidentIndex++,
                DayIndex = dayIndex,
                Cause = cause ?? string.Empty,
                Notes = notes ?? string.Empty,
            };
            if (affectedAnimalIds != null)
            {
                foreach (EntityId id in affectedAnimalIds)
                {
                    if (id.IsValid && id.Kind == EntityKind.Animal)
                    {
                        incident.AffectedAnimalIds.Add(id);
                    }
                }
            }

            healthIncidents.Add(incident);
            return incident;
        }

        public IReadOnlyList<AnimalHealthIncident> HealthIncidents => healthIncidents;

        public string ValidateAndAdd(AnimalState animal)
        {
            if (animal == null || !animal.AnimalId.IsValid || animal.AnimalId.Kind != EntityKind.Animal)
            {
                return "Rejected animal registration: missing or mistyped AnimalId.";
            }

            if (animals.ContainsKey(animal.AnimalId) || historical.ContainsKey(animal.AnimalId))
            {
                return $"Rejected duplicate animal registration: {animal.AnimalId} already exists (IDs are never reused).";
            }

            animals[animal.AnimalId] = animal;
            return null;
        }

        internal EntityIdRegistry IdRegistry => idRegistry;
        internal IDictionary<EntityId, EggBatchState> EggBatches => eggBatches;

        /// <summary>
        /// Save path: exports all registry state into plain serializable lists.
        /// ID cursors persist separately via <c>EntityIdSaveAdapter</c>.
        /// </summary>
        public void ExportState(
            List<AnimalState> outAnimals,
            List<HistoricalAnimalRecord> outHistorical,
            List<LivestockCohort> outCohorts,
            List<EggBatchState> outEggBatches)
        {
            outAnimals?.AddRange(animals.Values);
            outHistorical?.AddRange(historical.Values);
            outCohorts?.AddRange(cohorts.Values);
            outEggBatches?.AddRange(eggBatches.Values);
        }

        /// <summary>
        /// Load path: imports registry state. Duplicate IDs are rejected with diagnostics;
        /// the ID registry cursors must already be restored via <c>EntityIdSaveAdapter</c>.
        /// </summary>
        public void ImportState(
            IEnumerable<AnimalState> inAnimals,
            IEnumerable<HistoricalAnimalRecord> inHistorical,
            IEnumerable<LivestockCohort> inCohorts,
            IEnumerable<EggBatchState> inEggBatches)
        {
            if (inAnimals != null)
            {
                foreach (AnimalState animal in inAnimals)
                {
                    string rejection = ValidateAndAdd(animal);
                    if (rejection != null)
                    {
                        diagnostics.Add($"ImportState: {rejection}");
                    }
                }
            }

            if (inHistorical != null)
            {
                foreach (HistoricalAnimalRecord record in inHistorical)
                {
                    if (record == null || !record.AnimalId.IsValid)
                    {
                        continue;
                    }

                    if (animals.ContainsKey(record.AnimalId) || historical.ContainsKey(record.AnimalId))
                    {
                        diagnostics.Add($"ImportState: duplicate historical animal {record.AnimalId} skipped.");
                        continue;
                    }

                    historical[record.AnimalId] = record;
                }
            }

            if (inCohorts != null)
            {
                foreach (LivestockCohort cohort in inCohorts)
                {
                    if (cohort == null || !cohort.IsValid)
                    {
                        continue;
                    }

                    if (cohorts.ContainsKey(cohort.CohortId))
                    {
                        diagnostics.Add($"ImportState: duplicate cohort {cohort.CohortId} skipped.");
                        continue;
                    }

                    cohorts[cohort.CohortId] = cohort;
                }
            }

            if (inEggBatches != null)
            {
                foreach (EggBatchState batch in inEggBatches)
                {
                    if (batch == null || !batch.BatchId.IsValid)
                    {
                        continue;
                    }

                    if (eggBatches.ContainsKey(batch.BatchId))
                    {
                        diagnostics.Add($"ImportState: duplicate egg batch {batch.BatchId} skipped.");
                        continue;
                    }

                    eggBatches[batch.BatchId] = batch;
                }
            }
        }
    }

    /// <summary>
    /// HF-2: poultry lifecycle hooks (Tech X §2.3). Egg/fertility/incubation/hatch with
    /// parentage; male chicks remain present in records. Correcting a missing
    /// classification may backfill biological history but must never fabricate sales.
    /// </summary>
    public static class PoultryLifecycle
    {
        /// <summary>Lays a clutch: creates the egg-batch Lot record.</summary>
        public static EggBatchState LayClutch(
            AnimalRegistry registry,
            EntityId damId,
            EntityId sireId,
            int eggCount,
            int layDayIndex,
            int locationBuildingId)
        {
            if (registry == null || eggCount <= 0)
            {
                return null;
            }

            var batch = new EggBatchState
            {
                BatchId = registry.IdRegistry.Allocate(EntityKind.Lot),
                DamId = damId,
                SireId = sireId,
                LaidDayIndex = layDayIndex,
                EggCount = eggCount,
                LocationBuildingId = locationBuildingId,
            };
            registry.EggBatches[batch.BatchId] = batch;
            return batch;
        }

        /// <summary>
        /// Hatches a clutch: creates one individual chick <see cref="AnimalState"/> per
        /// hatched egg with dam/sire parentage. Sex counts are explicit so male chicks are
        /// never silently dropped (Tech X §2.3).
        /// </summary>
        public static List<AnimalState> RecordHatch(
            AnimalRegistry registry,
            EntityId batchId,
            int hatchDayIndex,
            int maleChicks,
            int femaleChicks,
            int unknownSexChicks)
        {
            var chicks = new List<AnimalState>();
            if (registry == null || !registry.EggBatches.TryGetValue(batchId, out EggBatchState batch))
            {
                return chicks;
            }

            if (maleChicks < 0 || femaleChicks < 0 || unknownSexChicks < 0)
            {
                return chicks;
            }

            int total = maleChicks + femaleChicks + unknownSexChicks;
            AddChicks(registry, batch, hatchDayIndex, AnimalSex.Male, maleChicks, chicks);
            AddChicks(registry, batch, hatchDayIndex, AnimalSex.Female, femaleChicks, chicks);
            AddChicks(registry, batch, hatchDayIndex, AnimalSex.Unknown, unknownSexChicks, chicks);

            batch.HatchDayIndex = hatchDayIndex;
            batch.HatchedChickCount += total;
            return chicks;
        }

        private static void AddChicks(
            AnimalRegistry registry,
            EggBatchState batch,
            int hatchDayIndex,
            AnimalSex sex,
            int count,
            List<AnimalState> chicks)
        {
            for (int i = 0; i < count; i++)
            {
                AnimalState chick = registry.RegisterAnimal(
                    AnimalSpecies.Chicken,
                    string.Empty,
                    sex,
                    AnimalOwnerKind.Unspecified,
                    string.Empty,
                    hatchDayIndex,
                    $"hatched from batch {batch.BatchId}");
                if (chick == null)
                {
                    continue;
                }

                chick.BirthDayIndex = hatchDayIndex;
                chick.BirthPlace = $"batch {batch.BatchId}";
                chick.Dam = batch.DamId;
                chick.Sire = batch.SireId;
                chick.BiologicalState = AnimalBiologicalState.Juvenile;
                chick.LocationBuildingId = batch.LocationBuildingId;
                chicks.Add(chick);
            }
        }
    }
}
