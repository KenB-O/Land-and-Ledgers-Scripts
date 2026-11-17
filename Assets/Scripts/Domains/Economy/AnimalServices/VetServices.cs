using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.AnimalServices
{
    /// <summary>
    /// T1F: a veterinary/farrier practitioner. Historical model (1870s): the practitioner
    /// TRAVELS to the farm — per-visit call-out fee plus travel charged both ways, then
    /// per-animal work on top. There were no herd-wide magic treatments; every animal
    /// is handled individually.
    /// </summary>
    [Serializable]
    public sealed class VetPractitioner
    {
        public string PractitionerId = string.Empty;
        public string DisplayName = string.Empty;
        public int PersonId = -1; // the actual person (for travel time accounting)
        public string BusinessId = string.Empty; // or a business, if any
        public string LocationId = string.Empty; // JRN-1 home base
        public bool IsOffMap;
        public int CallOutFeeCents;
        public int PerMileTravelCents; // charged out AND home (historical traveling-practitioner economics)
        public List<string> SpeciesServiced = new List<string>();

        public VetPractitioner() { }

        public bool ServicesSpecies(string species)
        {
            if (SpeciesServiced == null || SpeciesServiced.Count == 0) return true;
            foreach (string s in SpeciesServiced)
            {
                if (string.Equals(s, species, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// T1F: a standing service agreement between a farm/business and a practitioner.
    /// Terms are written, not assumed: call-out fee, scope, and duration.
    /// </summary>
    [Serializable]
    public sealed class VetServiceAgreement
    {
        public EntityId AgreementId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public string FarmOrBusinessId = string.Empty;
        public string PractitionerId = string.Empty;
        public int StartDayIndex;
        public int EndDayIndex = -1; // -1 = open-ended
        public bool Active = true;
        public int AgreedCallOutFeeCents;
        public string ScopeNotes = string.Empty;

        public VetServiceAgreement() { }

        public bool IsActiveOn(int dayIndex)
        {
            return Active && dayIndex >= StartDayIndex && (EndDayIndex < 0 || dayIndex <= EndDayIndex);
        }
    }

    /// <summary>
    /// T1F: one animal's treatment — animal-level work, never herd-wide. Links to the
    /// HF-2 AnimalHealthIncident when the treatment answers a recorded exposure.
    /// </summary>
    [Serializable]
    public sealed class AnimalTreatment
    {
        public int Sequence;
        public EntityId AnimalId = EntityId.Invalid; // EntityKind.Animal
        public int IncidentIndex = -1; // HF-2 shared exposure record, if any
        public string Ailment = string.Empty;
        public string Treatment = string.Empty;
        public int DayIndex;
        public int TreatedByPersonId = -1;
        public int WorkCostCents;
        public int MedicineCostCents;
        public string MedicineSource = string.Empty; // provenance: which store/apothecary
        public string Outcome = string.Empty;

        public AnimalTreatment() { }

        public int TotalCostCents => Math.Max(0, WorkCostCents) + Math.Max(0, MedicineCostCents);
    }

    /// <summary>
    /// T1F: a medicine lot with provenance — bought from a named supplier, never conjured.
    /// </summary>
    [Serializable]
    public sealed class MedicineLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string MedicineName = string.Empty;
        public int Doses;
        public string SupplierName = string.Empty; // upstream provenance
        public int AcquiredDayIndex;

        public MedicineLot() { }
    }

    /// <summary>
    /// T1F: one visit's invoice — line items, never a lump sum without basis.
    /// The caller settles it as an ordinary ledger outflow with provenance.
    /// </summary>
    [Serializable]
    public sealed class VetInvoice
    {
        public int DayIndex;
        public string PractitionerId = string.Empty;
        public string PractitionerName = string.Empty;
        public string FarmOrBusinessId = string.Empty;
        public int CallOutFeeCents;
        public int TravelFeeCents;
        public float TravelMiles;
        public List<AnimalTreatment> Treatments = new List<AnimalTreatment>();
        public string Notes = string.Empty;

        public int TreatmentTotalCents
        {
            get
            {
                int total = 0;
                foreach (AnimalTreatment t in Treatments) total += t.TotalCostCents;
                return total;
            }
        }

        public int TotalCents => Math.Max(0, CallOutFeeCents) + Math.Max(0, TravelFeeCents) + TreatmentTotalCents;
    }

    /// <summary>
    /// T1F: the veterinary service authority. A call-out travels a real JRN route
    /// (both ways), treats each animal individually, and invoices every line.
    /// No practitioner, no route, no treatment — refusals are loud, never silent.
    /// </summary>
    public sealed class VetService
    {
        private readonly List<VetPractitioner> practitioners = new List<VetPractitioner>();
        private readonly List<VetServiceAgreement> agreements = new List<VetServiceAgreement>();
        private readonly List<AnimalTreatment> treatmentHistory = new List<AnimalTreatment>();
        private int nextTreatmentSequence;

        public void RegisterPractitioner(VetPractitioner practitioner)
        {
            if (practitioner != null) practitioners.Add(practitioner);
        }

        public void RegisterAgreement(VetServiceAgreement agreement)
        {
            if (agreement != null) agreements.Add(agreement);
        }

        public IReadOnlyList<AnimalTreatment> TreatmentHistory => treatmentHistory;

        /// <summary>
        /// Calls the practitioner out to the farm. Each animal is treated individually;
        /// the invoice itemizes call-out, travel (both ways), and per-animal work.
        /// </summary>
        public VetInvoice CallOut(
            string practitionerId,
            string farmOrBusinessId,
            string farmLocationId,
            List<(EntityId animalId, string species, string ailment, string treatment, int workCostCents, int medicineCostCents, string medicineSource)> cases,
            int dayIndex,
            JourneyModel journeys,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            VetPractitioner practitioner = null;
            foreach (VetPractitioner p in practitioners)
            {
                if (string.Equals(p.PractitionerId, practitionerId, StringComparison.Ordinal))
                {
                    practitioner = p;
                    break;
                }
            }

            if (practitioner == null)
            {
                diagnostics.Add($"VetService: no practitioner '{practitionerId}' registered — no treatment without a practitioner.");
                return null;
            }

            // Travel burden is real: route both ways, fee per mile each way.
            float miles = 0f;
            if (journeys != null && !string.IsNullOrWhiteSpace(practitioner.LocationId)
                && !string.IsNullOrWhiteSpace(farmLocationId))
            {
                JourneyRoute route = journeys.FindRoute(practitioner.LocationId, farmLocationId, TravelMode.Horseback);
                if (!route.Found)
                {
                    diagnostics.Add($"VetService: {practitioner.DisplayName} cannot reach '{farmLocationId}' — {route.Diagnostic}");
                    return null;
                }

                miles = route.TotalMiles;
            }
            else if (!practitioner.IsOffMap)
            {
                diagnostics.Add($"VetService: {practitioner.DisplayName} has no usable route to '{farmLocationId}' — call-out refused.");
                return null;
            }

            int callOutFee = practitioner.CallOutFeeCents;
            foreach (VetServiceAgreement agreement in agreements)
            {
                if (agreement.IsActiveOn(dayIndex)
                    && string.Equals(agreement.PractitionerId, practitionerId, StringComparison.Ordinal)
                    && string.Equals(agreement.FarmOrBusinessId, farmOrBusinessId, StringComparison.Ordinal))
                {
                    callOutFee = agreement.AgreedCallOutFeeCents;
                    break;
                }
            }

            var invoice = new VetInvoice
            {
                DayIndex = dayIndex,
                PractitionerId = practitioner.PractitionerId,
                PractitionerName = practitioner.DisplayName,
                FarmOrBusinessId = farmOrBusinessId,
                CallOutFeeCents = Math.Max(0, callOutFee),
                TravelMiles = miles,
                TravelFeeCents = Mathf.RoundToInt(miles * 2f * Math.Max(0, practitioner.PerMileTravelCents)),
            };

            if (cases != null)
            {
                foreach (var c in cases)
                {
                    if (!c.animalId.IsValid || c.animalId.Kind != EntityKind.Animal)
                    {
                        diagnostics.Add("VetService: treatment refused — not a valid animal id. No herd-wide treatments.");
                        continue;
                    }

                    if (!practitioner.ServicesSpecies(c.species))
                    {
                        diagnostics.Add($"VetService: {practitioner.DisplayName} does not service '{c.species}' — animal {c.animalId} untreated.");
                        continue;
                    }

                    var treatment = new AnimalTreatment
                    {
                        Sequence = nextTreatmentSequence++,
                        AnimalId = c.animalId,
                        Ailment = c.ailment ?? string.Empty,
                        Treatment = c.treatment ?? string.Empty,
                        DayIndex = dayIndex,
                        TreatedByPersonId = practitioner.PersonId,
                        WorkCostCents = Math.Max(0, c.workCostCents),
                        MedicineCostCents = Math.Max(0, c.medicineCostCents),
                        MedicineSource = c.medicineSource ?? string.Empty,
                        Outcome = "treated",
                    };
                    treatmentHistory.Add(treatment);
                    invoice.Treatments.Add(treatment);
                }
            }

            return invoice;
        }
    }

    /// <summary>
    /// T1F: farrier work — horseshoeing, per horse, per hoof where it matters. Shoes come
    /// from the blacksmith (EQU-2) with a named source; the farrier's labor is per animal.
    /// A horse is never "serviced" as part of a herd batch.
    /// </summary>
    public sealed class FarrierService
    {
        /// <summary>Calibration: minutes to shoe one horse (four hooves).</summary>
        public const int ShoeMinutesPerHorse = 45;

        /// <summary>
        /// Shoes one horse. Shoes are consumed from the named supply; the work is recorded
        /// against the individual animal.
        /// </summary>
        public string ShoeHorse(
            EntityId horseId,
            int shoesNeeded,
            ref int shoeStockUnits,
            string shoeSource,
            int pricePerShoeCents,
            int laborCents,
            int dayIndex,
            int farrierPersonId,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (!horseId.IsValid || horseId.Kind != EntityKind.Animal)
            {
                return "FarrierService: not a valid animal id — shoeing is per-animal work.";
            }

            if (shoesNeeded <= 0)
            {
                return "FarrierService: no shoes needed.";
            }

            if (string.IsNullOrWhiteSpace(shoeSource))
            {
                return "FarrierService: horseshoes must name their source (blacksmith) — no orphan inputs.";
            }

            if (shoeStockUnits < shoesNeeded)
            {
                diagnostics.Add($"FarrierService: only {shoeStockUnits} shoes on hand, horse {horseId} needs {shoesNeeded} — shortfall, no shoeing faked.");
                return $"FarrierService: insufficient shoes for horse {horseId}.";
            }

            shoeStockUnits -= shoesNeeded;
            int materialCost = shoesNeeded * Math.Max(0, pricePerShoeCents);
            diagnostics.Add($"FarrierService: horse {horseId} shod ({shoesNeeded} shoes from {shoeSource}, {materialCost}c materials + {laborCents}c labor, day {dayIndex}, farrier P{farrierPersonId}).");
            return null;
        }

        /// <summary>
        /// D1: minutes to fit one hoof at this capability. A full shoeing
        /// visit runs 1-2 hours per horse (four hooves), so a single hoof
        /// takes ~10 minutes for a master and ~22 for a novice; trimming
        /// alone is about half that work.
        /// </summary>
        public static int FittingMinutesPerHoofForCapability(double capability)
            => SkillOutcomeCurves.MinutesForCapability(22, 10, capability);

        /// <summary>
        /// D1: asset-backed farrier fitting — the canonical path. Manufacturing
        /// never calls this; a shoe is forged (BlacksmithRuntime.ForgeHorseshoe)
        /// and inventoried first, then the farrier fits it here, one hoof at a
        /// time. Per hoof the farrier inspects the hoof, trims or prepares it,
        /// selects and shapes the best usable shoe, secures it, checks the fit,
        /// and the work is charged (materials per shoe + labour per hoof
        /// worked). Service orders are any size — one shoe, two, or a
        /// complete four-hoof shoeing; replacing one shoe never requires a
        /// four-shoe batch, and a horse is never assumed to need all four
        /// shoes: the caller assesses use, surface, wear and hoof condition
        /// and passes one HoofNeed per hoof that needs work. No hoof-condition
        /// authority exists yet in the simulation, so the assessment is
        /// caller-supplied data, not invented here.
        /// Consumes exactly the shoes needed; refuses with nothing consumed
        /// when usable shoes are short. Settlement stays with the caller,
        /// as with ShoeHorse: the result reports the costs, it does not move
        /// cash. Returns a result with Failure set (and nothing consumed)
        /// on any refusal.
        /// </summary>
        public FarrierFittingResult FitShoes(
            EntityId horseId,
            List<HoofNeed> hooves,
            List<EquipmentAsset> shoeStock,
            string shoeSource,
            int pricePerShoeCents,
            int laborCentsPerHoof,
            int dayIndex,
            EntityId farrierPersonId,
            SkillService skillService,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var result = new FarrierFittingResult();

            if (!horseId.IsValid || horseId.Kind != EntityKind.Animal)
            {
                result.Failure = "FarrierService: not a valid animal id — fitting is per-animal work.";
                diagnostics.Add(result.Failure);
                return result;
            }

            if (hooves == null || hooves.Count == 0)
            {
                result.Failure = "FarrierService: no hooves assessed — fitting needs a per-hoof assessment.";
                diagnostics.Add(result.Failure);
                return result;
            }

            if (shoeStock == null)
            {
                result.Failure = "FarrierService: no shoe stock given — fitting consumes real horseshoes.";
                diagnostics.Add(result.Failure);
                return result;
            }

            if (string.IsNullOrWhiteSpace(shoeSource))
            {
                result.Failure = "FarrierService: horseshoes must name their source (blacksmith) — no orphan inputs.";
                diagnostics.Add(result.Failure);
                return result;
            }

            if (skillService == null)
            {
                result.Failure = "FarrierService: no SkillService; cannot assess the farrier.";
                diagnostics.Add(result.Failure);
                return result;
            }

            int shoesNeeded = 0;
            int hoovesToWork = 0;
            foreach (HoofNeed hoof in hooves)
            {
                if (hoof == null) continue;
                if (hoof.NeedsShoe) shoesNeeded++;
                if (hoof.NeedsShoe || hoof.NeedsTrim) hoovesToWork++;
            }

            if (hoovesToWork == 0)
            {
                result.Failure = "FarrierService: no hoof needs a shoe or a trim — nothing to fit.";
                diagnostics.Add(result.Failure);
                return result;
            }

            // Sufficiency first: refuse with nothing consumed.
            int usableShoes = 0;
            foreach (EquipmentAsset asset in shoeStock)
            {
                if (asset != null && string.Equals(asset.Kind, BlacksmithRuntime.HorseshoeKind, StringComparison.Ordinal) && asset.Condition01 > 0.05f) usableShoes++;
            }
            if (usableShoes < shoesNeeded)
            {
                result.Failure = $"FarrierService: only {usableShoes} usable horseshoes on hand, horse {horseId} needs {shoesNeeded} — shortfall, no fitting faked.";
                diagnostics.Add(result.Failure);
                return result;
            }

            double capability = skillService.GetCapability(
                farrierPersonId, BlacksmithRuntime.FarrierSkillId);
            int minutesPerHoof = FittingMinutesPerHoofForCapability(capability);

            foreach (HoofNeed hoof in hooves)
            {
                if (hoof == null || (!hoof.NeedsShoe && !hoof.NeedsTrim)) continue;

                var record = new HoofFittingRecord
                {
                    HoofId = hoof.HoofId ?? string.Empty,
                    Inspected = true,
                    Trimmed = hoof.NeedsTrim,
                    HoofCondition01 = hoof.HoofCondition01,
                };

                int laborMinutes = minutesPerHoof;
                string note = $"inspected (condition {hoof.HoofCondition01:0.00})";
                if (hoof.NeedsTrim) note += ", trimmed";
                if (hoof.HoofCondition01 < 0.30f) note += " — hoof in poor condition, veterinary attention advised";

                if (hoof.NeedsShoe)
                {
                    // Best usable shoe first: highest condition, first listed on a tie.
                    EquipmentAsset best = null;
                    foreach (EquipmentAsset asset in shoeStock)
                    {
                        if (asset == null) continue;
                        if (!string.Equals(asset.Kind, BlacksmithRuntime.HorseshoeKind, StringComparison.Ordinal)) continue;
                        if (asset.Condition01 <= 0.05f) continue;
                        if (best == null || asset.Condition01 > best.Condition01) best = asset;
                    }

                    if (best == null)
                    {
                        // Unreachable after the sufficiency check above, but never
                        // leave a half-fitted horse behind if the stock changed.
                        result.Failure = $"FarrierService: ran out of usable horseshoes mid-fitting on horse {horseId}.";
                        diagnostics.Add(result.Failure);
                        return result;
                    }

                    shoeStock.Remove(best);
                    record.ShoeAssetId = best.AssetId;
                    record.ShoeShaped = true;
                    record.Secured = true;
                    record.FitChecked = true;
                    record.FitQuality01 = (float)SkillOutcomeCurves.QualityForCapability(capability);
                    result.ShoesConsumed++;
                    note += $", shoe {best.AssetId} shaped and secured, fit checked (quality {record.FitQuality01:0.00})";
                    if (record.FitQuality01 < 0.70f) note += " — poor fit, refit advised";
                }
                else
                {
                    // Trim-only hoof: about half the per-hoof work.
                    laborMinutes = (minutesPerHoof + 1) / 2;
                    record.FitQuality01 = 1f; // no shoe, nothing to fit-check
                    note += ", trim only, no shoe fitted";
                }

                record.LaborMinutes = laborMinutes;
                record.Notes = note;
                result.Hooves.Add(record);
                result.LaborMinutes += laborMinutes;
            }

            result.MaterialCents = result.ShoesConsumed * Math.Max(0, pricePerShoeCents);
            result.LaborCents = result.Hooves.Count * Math.Max(0, laborCentsPerHoof);
            result.TotalCents = result.MaterialCents + result.LaborCents;
            diagnostics.Add($"FarrierService: horse {horseId} fitted ({result.ShoesConsumed} shoes from {shoeSource}, {result.Hooves.Count} hooves worked, {result.LaborMinutes} min, {result.MaterialCents}c materials + {result.LaborCents}c labor, day {dayIndex}, farrier {farrierPersonId}).");
            return result;
        }
    }

    /// <summary>
    /// D1: one hoof's assessment, supplied by the caller. No hoof-condition
    /// authority exists yet in the simulation, so the condition and the
    /// needs are assessed by the caller (use, surface, wear, hoof
    /// condition) and passed in as data.
    /// </summary>
    [Serializable]
    public sealed class HoofNeed
    {
        public string HoofId = string.Empty; // e.g. "front-left"
        public float HoofCondition01 = 1f;
        public bool NeedsShoe;
        public bool NeedsTrim;
        public string Reason = string.Empty;

        public HoofNeed() { }

        public HoofNeed(string hoofId, float hoofCondition01, bool needsShoe, bool needsTrim, string reason)
        {
            HoofId = hoofId ?? string.Empty;
            HoofCondition01 = hoofCondition01;
            NeedsShoe = needsShoe;
            NeedsTrim = needsTrim;
            Reason = reason ?? string.Empty;
        }
    }

    /// <summary>D1: the record of one hoof's fitting — inspection, trim, shoe, fit check.</summary>
    [Serializable]
    public sealed class HoofFittingRecord
    {
        public string HoofId = string.Empty;
        public string ShoeAssetId = string.Empty; // empty when no shoe was fitted
        public bool Inspected;
        public bool Trimmed;
        public bool ShoeShaped;
        public bool Secured;
        public bool FitChecked;
        public float FitQuality01; // 0.60..1.00 when a shoe was fitted; 1.00 for trim-only
        public float HoofCondition01;
        public int LaborMinutes;
        public string Notes = string.Empty;

        public HoofFittingRecord() { }
    }

    /// <summary>D1: the outcome of one FitShoes service order. Failure is null on success.</summary>
    [Serializable]
    public sealed class FarrierFittingResult
    {
        public List<HoofFittingRecord> Hooves = new List<HoofFittingRecord>();
        public int ShoesConsumed;
        public int LaborMinutes;
        public int MaterialCents;
        public int LaborCents;
        public int TotalCents;
        public string Failure;

        public bool Succeeded => string.IsNullOrWhiteSpace(Failure);

        public FarrierFittingResult() { }
    }
}
