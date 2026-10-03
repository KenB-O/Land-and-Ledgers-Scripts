using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Farming.Delivery;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Dairy
{
    /// <summary>
    /// FVS-2: per-cow dairy state (Tech X §8.1). Milk availability derives from
    /// actual lactating animals plus parity, days in milk, nutrition, calf
    /// nursing, and milking practice — never from an abstract Dairy Output.
    /// </summary>
    [Serializable]
    public sealed class DairyCowState
    {
        public EntityId CowId = EntityId.Invalid;
        public int LastCalvingDayIndex = -1;
        public int DaysInMilk;          // days since last calving while lactating
        public float YieldVariance;     // 0.85..1.15 seeded at bootstrap (calibration)
        public bool CalfNursing;        // calf takes a share before milking (Canon §9.3)
        public float Condition01 = 1f;  // nutrition/condition, 0..1 (FVS-4 feed loop writes this)

        public DairyCowState() { }

        public DairyCowState(EntityId cowId, int calvingDayIndex, float yieldVariance)
        {
            CowId = cowId;
            LastCalvingDayIndex = calvingDayIndex;
            YieldVariance = Mathf.Clamp(yieldVariance, 0.5f, 1.5f);
        }
    }

    /// <summary>
    /// FVS-2: a perishable milk lot with provenance (Tech X §8.4, Part IV).
    /// Milk does not wait: condition degrades with age and spoiled milk can
    /// neither be sold nor churned (Canon §9.1: spoilage is a real outcome).
    /// </summary>
    [Serializable]
    public sealed class MilkLot : ITransitLot
    {
        /// <summary>Calibration: days of saleable/churnable life at good handling.</summary>
        public const int FreshDays = 2;

        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public EntityId CowId = EntityId.Invalid; // source animal (provenance)
        public string FarmId = string.Empty;
        public int ProducedDayIndex;
        public int QuantityUnits;      // remaining units
        public int SpoiledUnits;       // aged out; never saleable
        public EntityId MilkedBy = EntityId.Invalid; // worker person id (labor provenance)
        public string Notes = string.Empty;

        public MilkLot() { }

        public bool IsSpoiledFully(int dayIndex)
        {
            return QuantityUnits <= 0 || (dayIndex - ProducedDayIndex) > FreshDays;
        }

        // ITransitLot (FVS-3): milk rides delivery jobs and ages in transit.
        public string TransitLotId => LotId.ToString();
        public int TransitQuantityUnits => Math.Max(0, QuantityUnits);
        public void AgeInTransit(int transitDays)
        {
            AgeMilkLot(this, ProducedDayIndex + Math.Max(0, transitDays));
        }
        public int TransitSaleableUnits(int dayIndex) => SaleableUnits(dayIndex);
        public void MarkTransitConsumed() { QuantityUnits = 0; }

        public int SaleableUnits(int dayIndex)
        {
            if ((dayIndex - ProducedDayIndex) > FreshDays) return 0;
            return Math.Max(0, QuantityUnits);
        }
    }

    /// <summary>
    /// FVS-2: processing batch per Tech X §8.3 — input lots, process type,
    /// worker/task, workspace/equipment, yield, and byproducts. Butter is the
    /// supported path; cheese reuses this structure when built.
    /// </summary>
    [Serializable]
    public sealed class DairyProcessingBatch
    {
        public EntityId BatchId = EntityId.Invalid; // EntityKind.Lot
        public string ProcessType = "churn-butter";
        public List<EntityId> InputLotIds = new List<EntityId>();
        public int InputUnits;
        public int OutputUnits;        // butter
        public int ByproductUnits;      // buttermilk (Canon §9.7: feed/household use)
        public EntityId WorkerId = EntityId.Invalid;
        public int StartDayIndex;
        public int EndDayIndex = -1;
        public string Equipment = string.Empty; // e.g. "churn"
        public string Notes = string.Empty;
    }

    /// <summary>
    /// FVS-2: Tech X §8.2 milk allocation — gross milk flow split across real
    /// uses rather than converted straight to revenue (Canon §9.1 LOCK).
    /// </summary>
    [Serializable]
    public sealed class MilkAllocation
    {
        public int NursingUnits;      // calf share (already accounted at milking)
        public int HouseholdUseUnits; // household/employee consumption
        public int FreshSaleUnits;    // sold as fluid milk
        public int ProcessingUnits;   // to butter/cheese
        public int FeedUseUnits;      // byproduct/animal feed
        public int LossUnits;         // spillage/spoilage

        public int Total => NursingUnits + HouseholdUseUnits + FreshSaleUnits
            + ProcessingUnits + FeedUseUnits + LossUnits;
    }

    /// <summary>
    /// FVS-2: the dairy production authority. Lactation gating, yield math,
    /// milking/churning tasks, lot provenance, and spoilage. Butter is never
    /// auto-produced: every unit passes through a milking task and a churning
    /// task performed by a real person against their work-time budget (TTS-1/2).
    ///
    /// Calibration constants below are gameplay tuning, not canon. Canon fixes
    /// the structure (Tech X §8.1–§8.4); the numbers stay tunable.
    /// </summary>
    public sealed class DairyChain
    {
        public const string MilkCowTaskId = "milk-cow";
        public const string ChurnButterTaskId = "churn-butter";

        /// <summary>TTS-3 extension-path skill: dairy processing (churning).</summary>
        public const string DairyProcessingSkillId = "dairy-processing";

        // Calibration: base daily milk per cow in abstract units.
        public const int BaseMilkUnitsPerCowPerDay = 24;
        // Milking: minutes per head at skill level 1 (TTS-1 minute quantum).
        public const int MilkingMinutesPerHead = 8;
        // Churning: minutes per batch of up to BatchMilkUnits.
        public const int ChurnMinutesPerBatch = 45;
        public const int ChurnBatchMilkUnits = 48;
        // Butter yield: units of butter per 10 units of milk (calibration).
        public const int ButterUnitsPer10Milk = 4;
        // Buttermilk byproduct per 10 units of milk.
        public const int ButtermilkUnitsPer10Milk = 5;

        private readonly Dictionary<EntityId, DairyCowState> cowStates =
            new Dictionary<EntityId, DairyCowState>();

        /// <summary>Registers the dairy skill via the TTS-3 extension path (not a starter skill).</summary>
        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("DairyChain: no SkillService — dairy-processing skill not registered.");
                return;
            }

            if (skillService.GetSkill(DairyProcessingSkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(DairyProcessingSkillId, "Dairy Processing",
                        "Churning butter, handling milk, and dairy hygiene. Registered by the dairy chain (TTS-3 extension path)."),
                    out rejection))
                {
                    diagnostics.Add("DairyChain: dairy-processing skill rejected: " + rejection);
                }
            }
        }

        /// <summary>Registers milking + churning task definitions (TTS-2).</summary>
        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;

            var milk = new TaskDefinition(MilkCowTaskId, "Milk cow", MilkingMinutesPerHead);
            milk.SetRequiredSkill(SkillIds.AnimalHusbandry, new[] { "milking" });
            authority.RegisterDefinition(milk, out _);

            var churn = new TaskDefinition(ChurnButterTaskId, "Churn butter", ChurnMinutesPerBatch);
            churn.SetRequiredSkill(DairyProcessingSkillId, new[] { "churning" });
            authority.RegisterDefinition(churn, out _);
        }

        public void RegisterCow(DairyCowState state)
        {
            if (state == null || !state.CowId.IsValid) return;
            cowStates[state.CowId] = state;
        }

        public DairyCowState GetCowState(EntityId cowId)
        {
            DairyCowState state;
            return cowStates.TryGetValue(cowId, out state) ? state : null;
        }

        /// <summary>
        /// Tech X §8.1: daily milk from one actual lactating cow. Returns 0 for
        /// dry/non-lactating animals (lactation gating). Factors: lactation
        /// curve by days in milk, parity, nutrition/condition, calf nursing,
        /// milking practice (skill), seeded variance.
        /// </summary>
        public int DailyMilkYield(AnimalState cow, int milkerSkillLevel)
        {
            if (cow == null || !cow.IsActive) return 0;
            if (cow.Species != AnimalSpecies.Cattle || cow.Sex != AnimalSex.Female) return 0;
            if (cow.BiologicalState != AnimalBiologicalState.Lactating) return 0; // dry yields nothing

            DairyCowState state = GetCowState(cow.AnimalId);

            // Lactation curve: ramps to peak ~day 60, declines after (calibration).
            int daysInMilk = state != null ? Math.Max(0, state.DaysInMilk) : 60;
            float curve = daysInMilk < 60
                ? 0.6f + 0.4f * (daysInMilk / 60f)
                : Math.Max(0.35f, 1f - (daysInMilk - 60f) / 400f);

            // Parity: first-calf heifers give less (Tech X §4.5 parity matters).
            float parityFactor = cow.Parity <= 1 ? 0.8f : cow.Parity <= 3 ? 1f : 0.92f;

            // Nutrition/condition from the feed loop (FVS-4 writes this; default 1).
            float nutrition = state != null ? Mathf.Clamp01(state.Condition01) : 1f;

            // Calf nursing takes its share first (Canon §9.3 allocation).
            float nursingShare = (state != null && state.CalfNursing) ? 0.35f : 0f;

            // Milking practice: skilled hands get a fuller yield (TTS-3).
            float practice = 0.85f + 0.03f * Math.Min(5, Math.Max(1, milkerSkillLevel));

            float variance = state != null ? state.YieldVariance : 1f;

            float gross = BaseMilkUnitsPerCowPerDay * curve * parityFactor * nutrition * practice * variance;
            int net = Mathf.RoundToInt(gross * (1f - nursingShare));
            return Math.Max(0, net);
        }

        /// <summary>
        /// Milks one cow: consumes the milking task's labor (caller assigns the
        /// task through TaskAuthority) and mints a provenance milk lot. The lot
        /// records the cow, the worker, and the day — never anonymous.
        /// </summary>
        public MilkLot MilkCow(
            AnimalRegistry registry,
            EntityIdRegistry idRegistry,
            EntityId cowId,
            EntityId workerId,
            string farmId,
            int dayIndex,
            int milkerSkillLevel,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (registry == null || idRegistry == null)
            {
                diagnostics.Add("DairyChain.MilkCow: registry missing.");
                return null;
            }

            AnimalState cow = registry.GetAnimal(cowId);
            if (cow == null || !cow.IsActive)
            {
                diagnostics.Add($"DairyChain.MilkCow: unknown or inactive animal {cowId} — no milk from nothing.");
                return null;
            }

            int units = DailyMilkYield(cow, milkerSkillLevel);
            if (units <= 0)
            {
                diagnostics.Add($"DairyChain.MilkCow: {cowId} is {cow.BiologicalState}, not lactating — dry cows yield nothing (Canon §3.4).");
                return null;
            }

            var lot = new MilkLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                CowId = cowId,
                FarmId = farmId ?? string.Empty,
                ProducedDayIndex = dayIndex,
                QuantityUnits = units,
                MilkedBy = workerId,
            };

            cow.ProductionHistory.Add(new AnimalProductionRecord
            {
                DayIndex = dayIndex,
                Kind = "milk",
                Quantity = units,
                Notes = $"lot {lot.LotId}, worker {workerId}",
            });

            DairyCowState state = GetCowState(cowId);
            if (state != null) state.DaysInMilk++;

            return lot;
        }

        /// <summary>
        /// Churns milk lots into butter (Tech X §8.3 processing batch). Spoiled
        /// lots are refused; butter + buttermilk come out with the batch as
        /// provenance. Requires the churn task's labor (caller assigns it).
        /// </summary>
        public DairyProcessingBatch ChurnButter(
            EntityIdRegistry idRegistry,
            List<MilkLot> inputLots,
            EntityId workerId,
            int dayIndex,
            string equipment,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null)
            {
                diagnostics.Add("DairyChain.ChurnButter: id registry missing.");
                return null;
            }

            if (inputLots == null || inputLots.Count == 0)
            {
                diagnostics.Add("DairyChain.ChurnButter: no milk lots supplied — butter is not conjured (Canon §9.5).");
                return null;
            }

            int freshUnits = 0;
            var usedLotIds = new List<EntityId>();
            foreach (var lot in inputLots)
            {
                if (lot == null) continue;
                int saleable = lot.SaleableUnits(dayIndex);
                if (saleable <= 0)
                {
                    diagnostics.Add($"DairyChain.ChurnButter: lot {lot.LotId} is spoiled — spoiled milk can never become butter (Canon §9.1).");
                    return null;
                }
                freshUnits += saleable;
                usedLotIds.Add(lot.LotId);
            }

            if (freshUnits <= 0)
            {
                diagnostics.Add("DairyChain.ChurnButter: no fresh milk units to churn.");
                return null;
            }

            var batch = new DairyProcessingBatch
            {
                BatchId = idRegistry.Allocate(EntityKind.Lot),
                ProcessType = "churn-butter",
                InputLotIds = usedLotIds,
                InputUnits = freshUnits,
                WorkerId = workerId,
                StartDayIndex = dayIndex,
                EndDayIndex = dayIndex,
                Equipment = equipment ?? "churn",
            };
            batch.OutputUnits = (freshUnits * ButterUnitsPer10Milk) / 10;
            batch.ByproductUnits = (freshUnits * ButtermilkUnitsPer10Milk) / 10; // Canon §9.7: feed/household use

            foreach (var lot in inputLots)
            {
                if (lot != null) lot.QuantityUnits = 0; // consumed into the batch
            }

            return batch;
        }

        /// <summary>
        /// Ages a milk lot: units past FreshDays become spoiled (loss allocation,
        /// Tech X §8.2). Spoiled units are recorded, never silently dropped.
        /// </summary>
        public static void AgeMilkLot(MilkLot lot, int dayIndex)
        {
            if (lot == null) return;
            int age = dayIndex - lot.ProducedDayIndex;
            if (age > MilkLot.FreshDays && lot.QuantityUnits > 0)
            {
                lot.SpoiledUnits += lot.QuantityUnits;
                lot.QuantityUnits = 0;
            }
        }

        /// <summary>
        /// Calving transitions a cow into lactation (Canon §3.4: lactation is a
        /// state). Parity increments; the dairy state resets its lactation clock.
        /// </summary>
        public string RecordCalving(AnimalRegistry registry, EntityId cowId, int dayIndex, bool calfNursing)
        {
            if (registry == null) return "DairyChain.RecordCalving: no registry.";
            AnimalState cow = registry.GetAnimal(cowId);
            if (cow == null || !cow.IsActive) return $"DairyChain.RecordCalving: unknown animal {cowId}.";

            cow.BiologicalState = AnimalBiologicalState.Lactating;
            cow.Parity++;
            RegisterCow(new DairyCowState(cowId, dayIndex, 0.85f + (float)new System.Random(cowId.GetHashCode()).NextDouble() * 0.3f)
            {
                DaysInMilk = 0,
                CalfNursing = calfNursing,
            });
            return null;
        }

        /// <summary>Drying off ends lactation: the cow yields nothing until she freshens again.</summary>
        public string DryOff(AnimalRegistry registry, EntityId cowId)
        {
            if (registry == null) return "DairyChain.DryOff: no registry.";
            AnimalState cow = registry.GetAnimal(cowId);
            if (cow == null || !cow.IsActive) return $"DairyChain.DryOff: unknown animal {cowId}.";
            cow.BiologicalState = AnimalBiologicalState.Dry;
            return null;
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public DairyChainSaveDto CaptureSaveDto()
        {
            return new DairyChainSaveDto { cows = new List<DairyCowState>(cowStates.Values) };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(DairyChainSaveDto dto)
        {
            cowStates.Clear();
            if (dto == null || dto.cows == null) return;
            foreach (var cow in dto.cows)
            {
                if (cow != null && cow.CowId.IsValid) cowStates[cow.CowId] = cow;
            }
        }
    }
}
