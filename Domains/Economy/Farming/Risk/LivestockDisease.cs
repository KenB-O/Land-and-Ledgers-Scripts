using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Risk
{
    /// <summary>
    /// NX-2B: how a disease moves. Contact = same-farm proximity;
    /// vector = ticks/carriers (Texas fever); soil = environmental reservoir (blackleg/anthrax).
    /// </summary>
    public enum DiseaseTransmission
    {
        Unspecified = 0,
        Contact = 1,
        Vector = 2,
        Soil = 3,
    }

    /// <summary>
    /// NX-2B: a named, historically justified livestock disease (Canon 9.6A:
    /// "Named diseases should only be used where their period, geography, and
    /// transmission are historically justified; do not rely on one generic
    /// cattle-epidemic event"). All rates are calibration per Canon Part XV.
    /// </summary>
    [Serializable]
    public sealed class LivestockDiseaseDef
    {
        public string DiseaseId = string.Empty;
        public string DisplayName = string.Empty;
        public List<AnimalSpecies> Species = new List<AnimalSpecies>();
        public DiseaseTransmission Transmission;
        public float DailySpreadP01 = 0.1f;   // per-day infection probability per exposed animal (calibration)
        public float Mortality01 = 0.2f;      // of infected, per-day death probability (calibration)
        public int MinSickDays = 3;           // before recovery/death resolves (calibration)
        public string HistoricalNote = string.Empty;

        public LivestockDiseaseDef() { }

        public bool Affects(AnimalSpecies species) => Species != null && Species.Contains(species);
    }

    /// <summary>NX-2B: one ranch-localized outbreak.</summary>
    [Serializable]
    public sealed class DiseaseOutbreak
    {
        public string OutbreakId = string.Empty;
        public string DiseaseId = string.Empty;
        public string FarmId = string.Empty; // ranch-localized (Canon 9.6A)
        public int StartDayIndex;
        public List<EntityId> SickAnimalIds = new List<EntityId>();
        public Dictionary<EntityId, int> SickSinceDay = new Dictionary<EntityId, int>();
        public List<EntityId> DeadAnimalIds = new List<EntityId>();
        public List<EntityId> RecoveredAnimalIds = new List<EntityId>();
        public int HealthIncidentIndex = -1; // HF-2 shared record — the T1F vet treats against this
        public bool Resolved;

        public DiseaseOutbreak() { }

        public bool IsActivelySick(EntityId animalId)
        {
            return SickAnimalIds != null && SickAnimalIds.Contains(animalId);
        }
    }

    /// <summary>NX-2B: save data for one outbreak (dictionary flattened for Unity serialization).</summary>
    [Serializable]
    public sealed class DiseaseOutbreakSaveDto
    {
        public string OutbreakId = string.Empty;
        public string DiseaseId = string.Empty;
        public string FarmId = string.Empty;
        public int StartDayIndex;
        public List<EntityId> SickAnimalIds = new List<EntityId>();
        public List<EntityId> SickSinceKeys = new List<EntityId>();
        public List<int> SickSinceValues = new List<int>();
        public List<EntityId> DeadAnimalIds = new List<EntityId>();
        public List<EntityId> RecoveredAnimalIds = new List<EntityId>();
        public int HealthIncidentIndex = -1;
        public bool Resolved;
    }

    /// <summary>NX-2B: save data for the disease service.</summary>
    [Serializable]
    public sealed class LivestockDiseaseSaveDto
    {
        public List<DiseaseOutbreakSaveDto> outbreaks = new List<DiseaseOutbreakSaveDto>();
    }

    /// <summary>
    /// NX-2B: livestock disease. Canon 9.6A: a disease affecting one ranch
    /// damages that ranch and dependent buyers WITHOUT automatically moving the
    /// entire regional market. Spread is seeded and ranch-local; the service
    /// never touches prices. Interacts with the T1F vet (individual treatments
    /// reference the outbreak's HealthIncidentIndex) and the feed loop (sick
    /// animals are flagged; the caller reduces their intake honestly).
    ///
    /// Historical basis (researched per standing instruction):
    /// - Texas fever (bovine babesiosis): tick-borne; the great scourge of the
    ///   Texas cattle drives 1860s-1880s — northern herds had no immunity.
    /// - Blackleg (Clostridium chauvoei): soil-borne bacterial disease of young
    ///   cattle; a leading 1870s killer on the Plains.
    /// - Anthrax (Bacillus anthracis): sporadic, soil-borne; known and feared
    ///   throughout the period.
    /// - Hog cholera (classical swine fever): ravaging American hog herds from
    ///   the 1830s, at epizootic scale by the 1870s-80s.
    /// - Fowl cholera (Pasteurella multocida): studied by Pasteur in 1879 —
    ///   period-appropriate for the late campaign.
    /// </summary>
    public sealed class LivestockDiseaseService
    {
        public static readonly List<LivestockDiseaseDef> Catalog = new List<LivestockDiseaseDef>
        {
            new LivestockDiseaseDef
            {
                DiseaseId = "texas-fever", DisplayName = "Texas fever (bovine babesiosis)",
                Species = new List<AnimalSpecies> { AnimalSpecies.Cattle },
                Transmission = DiseaseTransmission.Vector,
                DailySpreadP01 = 0.15f, Mortality01 = 0.35f, MinSickDays = 5,
                HistoricalNote = "Tick-borne; devastated tick-naive northern herds on the 1870s cattle trails.",
            },
            new LivestockDiseaseDef
            {
                DiseaseId = "blackleg", DisplayName = "Blackleg",
                Species = new List<AnimalSpecies> { AnimalSpecies.Cattle },
                Transmission = DiseaseTransmission.Soil,
                DailySpreadP01 = 0.08f, Mortality01 = 0.60f, MinSickDays = 2,
                HistoricalNote = "Clostridium chauvoei; sudden death in young stock — a leading 1870s Plains killer.",
            },
            new LivestockDiseaseDef
            {
                DiseaseId = "anthrax", DisplayName = "Anthrax",
                Species = new List<AnimalSpecies> { AnimalSpecies.Cattle, AnimalSpecies.Sheep, AnimalSpecies.Horse },
                Transmission = DiseaseTransmission.Soil,
                DailySpreadP01 = 0.05f, Mortality01 = 0.70f, MinSickDays = 2,
                HistoricalNote = "Bacillus anthracis; sporadic soil-borne outbreaks throughout the period.",
            },
            new LivestockDiseaseDef
            {
                DiseaseId = "hog-cholera", DisplayName = "Hog cholera",
                Species = new List<AnimalSpecies> { AnimalSpecies.Pig },
                Transmission = DiseaseTransmission.Contact,
                DailySpreadP01 = 0.25f, Mortality01 = 0.40f, MinSickDays = 6,
                HistoricalNote = "Classical swine fever; at epizootic scale in American hog country by the 1870s-80s.",
            },
            new LivestockDiseaseDef
            {
                DiseaseId = "fowl-cholera", DisplayName = "Fowl cholera",
                Species = new List<AnimalSpecies> { AnimalSpecies.Chicken },
                Transmission = DiseaseTransmission.Contact,
                DailySpreadP01 = 0.30f, Mortality01 = 0.50f, MinSickDays = 3,
                HistoricalNote = "Pasteurella multocida; studied by Pasteur in 1879 — late-campaign appropriate.",
            },
        };

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        private readonly List<DiseaseOutbreak> outbreaks = new List<DiseaseOutbreak>();
        /// <summary>All outbreaks this service is tracking (for the game loop and save path).</summary>
        public IReadOnlyList<DiseaseOutbreak> Outbreaks => outbreaks;

        public static LivestockDiseaseDef FindDef(string diseaseId)
        {
            foreach (LivestockDiseaseDef def in Catalog)
                if (string.Equals(def.DiseaseId, diseaseId, StringComparison.OrdinalIgnoreCase))
                    return def;
            return null;
        }

        /// <summary>
        /// Starts an outbreak at one farm. Seeds infection in susceptible animals
        /// AT THAT FARM ONLY — ranch-localized per Canon 9.6A. Records the HF-2
        /// health incident the T1F vet treats against.
        /// </summary>
        public DiseaseOutbreak StartOutbreak(
            string diseaseId, string farmId, IEnumerable<AnimalState> animals,
            AnimalRegistry registry, int seed, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            LivestockDiseaseDef def = FindDef(diseaseId);
            if (def == null)
            {
                diag.Add($"LivestockDiseaseService.StartOutbreak: unknown disease '{diseaseId}' — no generic epidemics (Canon 9.6A).");
                return null;
            }
            if (string.IsNullOrWhiteSpace(farmId) || animals == null || registry == null)
            {
                diag.Add("LivestockDiseaseService.StartOutbreak: farm, animals, and registry are required.");
                return null;
            }

            var rng = new System.Random(seed * 1013904223 + dayIndex * 13 + 2);
            var initial = new List<EntityId>();
            foreach (AnimalState animal in animals)
            {
                if (animal == null || !animal.IsActive) continue;
                if (!string.Equals(animal.OwnerId, farmId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!def.Affects(animal.Species)) continue;
                // Index case: one seeded animal (soil/vector) or a few (contact).
                if (rng.NextDouble() < (def.Transmission == DiseaseTransmission.Contact ? 0.30 : 0.12))
                    initial.Add(animal.AnimalId);
            }
            if (initial.Count == 0)
            {
                diag.Add($"LivestockDiseaseService: no susceptible {def.DisplayName} hosts at farm '{farmId}' — no outbreak.");
                return null;
            }

            var outbreak = new DiseaseOutbreak
            {
                OutbreakId = $"outbreak-{diseaseId}-{farmId}-{dayIndex}",
                DiseaseId = diseaseId,
                FarmId = farmId,
                StartDayIndex = dayIndex,
            };
            foreach (EntityId id in initial)
            {
                outbreak.SickAnimalIds.Add(id);
                outbreak.SickSinceDay[id] = dayIndex;
                MarkSick(registry, id, def, dayIndex);
            }
            AnimalHealthIncident incident = registry.RecordHealthIncident(
                dayIndex, def.DisplayName, initial,
                $"NX-2B outbreak {outbreak.OutbreakId} at farm '{farmId}'.");
            outbreak.HealthIncidentIndex = incident.IncidentIndex;
            outbreaks.Add(outbreak);

            diag.Add($"LivestockDiseaseService: {def.DisplayName} outbreak at farm '{farmId}' — " +
                $"{initial.Count} index cases (incident #{incident.IncidentIndex}). Ranch-localized; no market effect (Canon 9.6A).");
            return outbreak;
        }

        /// <summary>
        /// Advances one outbreak a day: seeded spread among same-farm animals,
        /// mortality rolls, recoveries. Deaths use the registry's terminal
        /// disposition (IDs never reused). Never touches prices or markets.
        /// </summary>
        public void AdvanceOutbreak(
            DiseaseOutbreak outbreak, IEnumerable<AnimalState> animals,
            AnimalRegistry registry, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (outbreak == null || outbreak.Resolved || registry == null) return;
            LivestockDiseaseDef def = FindDef(outbreak.DiseaseId);
            if (def == null) return;

            var rng = new System.Random(outbreak.OutbreakId.GetHashCode() + dayIndex * 97);
            var byId = new Dictionary<EntityId, AnimalState>();
            if (animals != null)
                foreach (AnimalState a in animals)
                    if (a != null && a.IsActive) byId[a.AnimalId] = a;

            // Spread: susceptible same-farm animals of affected species.
            var newCases = new List<EntityId>();
            foreach (var kvp in byId)
            {
                AnimalState animal = kvp.Value;
                if (!string.Equals(animal.OwnerId, outbreak.FarmId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!def.Affects(animal.Species)) continue;
                if (outbreak.IsActivelySick(animal.AnimalId)) continue;
                if (outbreak.DeadAnimalIds.Contains(animal.AnimalId)) continue;
                if (outbreak.RecoveredAnimalIds.Contains(animal.AnimalId)) continue;
                if (rng.NextDouble() < def.DailySpreadP01)
                    newCases.Add(animal.AnimalId);
            }
            foreach (EntityId id in newCases)
            {
                outbreak.SickAnimalIds.Add(id);
                outbreak.SickSinceDay[id] = dayIndex;
                MarkSick(registry, id, def, dayIndex);
            }

            // Resolution: death or recovery after the minimum sick days.
            var resolved = new List<EntityId>();
            foreach (EntityId id in outbreak.SickAnimalIds)
            {
                int sickDays = dayIndex - (outbreak.SickSinceDay.TryGetValue(id, out int since) ? since : dayIndex);
                if (sickDays < def.MinSickDays) continue;
                if (rng.NextDouble() < def.Mortality01)
                {
                    string problem = registry.RecordDisposition(id, AnimalCommercialStatus.Deceased, dayIndex,
                        $"died: {def.DisplayName} (outbreak {outbreak.OutbreakId})");
                    if (problem == null)
                    {
                        outbreak.DeadAnimalIds.Add(id);
                        resolved.Add(id);
                        diag.Add($"LivestockDiseaseService: {id} died of {def.DisplayName}.");
                    }
                    else
                    {
                        diag.Add($"LivestockDiseaseService: {problem}");
                    }
                }
                else
                {
                    outbreak.RecoveredAnimalIds.Add(id);
                    resolved.Add(id);
                    AnimalState animal = registry.GetAnimal(id);
                    if (animal != null)
                        animal.HealthNotes = string.IsNullOrWhiteSpace(animal.HealthNotes)
                            ? $"Recovered from {def.DisplayName}, day {dayIndex}."
                            : $"{animal.HealthNotes} | Recovered from {def.DisplayName}, day {dayIndex}.";
                }
            }
            foreach (EntityId id in resolved) outbreak.SickAnimalIds.Remove(id);

            if (outbreak.SickAnimalIds.Count == 0)
            {
                outbreak.Resolved = true;
                diag.Add($"LivestockDiseaseService: outbreak {outbreak.OutbreakId} resolved — " +
                    $"{outbreak.DeadAnimalIds.Count} dead, {outbreak.RecoveredAnimalIds.Count} recovered.");
            }
            else if (newCases.Count > 0)
            {
                diag.Add($"LivestockDiseaseService: outbreak {outbreak.OutbreakId} — {newCases.Count} new cases, " +
                    $"{outbreak.SickAnimalIds.Count} sick.");
            }
        }

        /// <summary>
        /// Sale eligibility gate: actively sick animals cannot be sold or
        /// slaughtered into the food chain. The market flows consult this.
        /// </summary>
        public bool IsAnimalSaleEligible(EntityId animalId, IEnumerable<DiseaseOutbreak> outbreaks, out string reason)
        {
            reason = string.Empty;
            if (outbreaks == null) return true;
            foreach (DiseaseOutbreak outbreak in outbreaks)
            {
                if (outbreak == null || outbreak.Resolved) continue;
                if (outbreak.IsActivelySick(animalId))
                {
                    LivestockDiseaseDef def = FindDef(outbreak.DiseaseId);
                    reason = $"Actively sick with {(def != null ? def.DisplayName : outbreak.DiseaseId)} " +
                        $"(outbreak {outbreak.OutbreakId}) — not sale eligible.";
                    return false;
                }
            }
            return true;
        }

        private static void MarkSick(AnimalRegistry registry, EntityId animalId, LivestockDiseaseDef def, int dayIndex)
        {
            AnimalState animal = registry.GetAnimal(animalId);
            if (animal == null) return;
            string note = $"Sick: {def.DisplayName} (day {dayIndex}) — not sale eligible.";
            animal.HealthNotes = string.IsNullOrWhiteSpace(animal.HealthNotes) ? note : $"{animal.HealthNotes} | {note}";
        }

        public LivestockDiseaseSaveDto CaptureSaveDto()
        {
            var dto = new LivestockDiseaseSaveDto();
            foreach (DiseaseOutbreak outbreak in outbreaks)
            {
                if (outbreak == null) continue;
                var o = new DiseaseOutbreakSaveDto
                {
                    OutbreakId = outbreak.OutbreakId,
                    DiseaseId = outbreak.DiseaseId,
                    FarmId = outbreak.FarmId,
                    StartDayIndex = outbreak.StartDayIndex,
                    SickAnimalIds = new List<EntityId>(outbreak.SickAnimalIds),
                    DeadAnimalIds = new List<EntityId>(outbreak.DeadAnimalIds),
                    RecoveredAnimalIds = new List<EntityId>(outbreak.RecoveredAnimalIds),
                    HealthIncidentIndex = outbreak.HealthIncidentIndex,
                    Resolved = outbreak.Resolved,
                };
                if (outbreak.SickSinceDay != null)
                {
                    foreach (var kvp in outbreak.SickSinceDay)
                    {
                        o.SickSinceKeys.Add(kvp.Key);
                        o.SickSinceValues.Add(kvp.Value);
                    }
                }
                dto.outbreaks.Add(o);
            }
            return dto;
        }

        public void LoadFromSaveDto(LivestockDiseaseSaveDto dto)
        {
            outbreaks.Clear();
            if (dto == null || dto.outbreaks == null) return;
            foreach (DiseaseOutbreakSaveDto o in dto.outbreaks)
            {
                if (o == null || string.IsNullOrEmpty(o.OutbreakId)) continue;
                var outbreak = new DiseaseOutbreak
                {
                    OutbreakId = o.OutbreakId,
                    DiseaseId = o.DiseaseId,
                    FarmId = o.FarmId,
                    StartDayIndex = o.StartDayIndex,
                    SickAnimalIds = o.SickAnimalIds ?? new List<EntityId>(),
                    DeadAnimalIds = o.DeadAnimalIds ?? new List<EntityId>(),
                    RecoveredAnimalIds = o.RecoveredAnimalIds ?? new List<EntityId>(),
                    HealthIncidentIndex = o.HealthIncidentIndex,
                    Resolved = o.Resolved,
                };
                if (o.SickSinceKeys != null && o.SickSinceValues != null)
                {
                    int n = Math.Min(o.SickSinceKeys.Count, o.SickSinceValues.Count);
                    for (int i = 0; i < n; i++)
                        outbreak.SickSinceDay[o.SickSinceKeys[i]] = o.SickSinceValues[i];
                }
                outbreaks.Add(outbreak);
            }
        }
    }
}
