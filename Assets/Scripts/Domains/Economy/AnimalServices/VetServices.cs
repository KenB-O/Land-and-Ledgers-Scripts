using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
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
    }
}
