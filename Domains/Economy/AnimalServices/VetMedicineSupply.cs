using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Trade;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.AnimalServices
{
    /// <summary>
    /// W10A: one veterinary medicine lot with full upstream provenance. Closes the
    /// FVS-flagged hole ("Veterinary/medicine inputs: no supplier modeled yet").
    /// Every consumed dose traces to a real lot from a real supplier — a declared
    /// off-map import order (EQU-1 precedent) or a named on-map merchant purchase —
    /// or to the explicit one-time bootstrap endowment. No orphan lots, no synthetic
    /// stock. Pattern follows W1A DoctorMedicineLot; the vet's chest is a separate
    /// stock because human and veterinary remedies are different goods.
    ///
    /// Canon-gating note: Canon XXVII lists "bandages/medicines" in the veterinarian's
    /// kit (business equipment) but models no shelf life — so no expiry is modeled
    /// here either. FIFO dispensing is the only stock discipline.
    /// </summary>
    [Serializable]
    public sealed class VetMedicineLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string MedicineName = string.Empty;
        public int Doses;
        public int AcquiredDayIndex;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map origin, e.g. "Off-map wholesale drug house, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>On-map merchant purchase: the supplying business instance id (empty when imported).</summary>
        public string SupplierBusinessId = string.Empty;

        /// <summary>On-map merchant purchase: the supplying merchant's display name.</summary>
        public string SupplierBusinessName = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="VetMedicineBootstrap"/>. Explicitly marked, never silently
        /// replenished — reorder goes through import orders or merchant purchases only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public VetMedicineLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via import orders or merchant purchases";
            }

            if (!string.IsNullOrWhiteSpace(SupplierBusinessId))
            {
                string name = string.IsNullOrWhiteSpace(SupplierBusinessName) ? SupplierBusinessId : SupplierBusinessName;
                return $"lot {LotId} / merchant purchase from {name} ({SupplierBusinessId}), day {AcquiredDayIndex}";
            }

            string order = string.IsNullOrWhiteSpace(ImportOrderId) ? "no-order" : ImportOrderId;
            string origin = string.IsNullOrWhiteSpace(OriginName) ? "unnamed-origin" : OriginName;
            return $"lot {LotId} / import order {order} / {origin}";
        }
    }

    /// <summary>
    /// W10A: one line of a dispense result — which lot the doses came from and the
    /// full upstream chain. Animal treatments carry these so every dose is traceable.
    /// </summary>
    [Serializable]
    public sealed class VetMedicineDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string MedicineName = string.Empty;
        public int DosesTaken;
        public string ProvenanceChain = string.Empty;

        public VetMedicineDispenseLine() { }
    }

    /// <summary>
    /// W10A: the vet's medicine chest. Doses dispense FIFO (oldest stock first);
    /// every dispense returns the lots consumed so treatment records carry provenance.
    /// Refusals are loud — stock shortfalls are never faked.
    /// </summary>
    public sealed class VetMedicineStock
    {
        private readonly List<VetMedicineLot> lots = new List<VetMedicineLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<VetMedicineLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the chest. Lots must name their supplier chain
        /// (import order + origin, or a merchant purchase) or carry the explicit
        /// bootstrap flag. Returns a rejection string, or null on success.
        /// </summary>
        public string ReceiveLot(VetMedicineLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "VetMedicineStock: null lot refused — no medicine without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.MedicineName))
                return "VetMedicineStock: lot refused — medicine name is required.";
            if (lot.Doses <= 0)
                return $"VetMedicineStock: lot refused — '{lot.MedicineName}' needs a positive dose count.";
            if (!lot.IsBootstrapEndowment
                && string.IsNullOrWhiteSpace(lot.SupplierBusinessId)
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"VetMedicineStock: lot refused — '{lot.MedicineName}' names no import order, origin, or merchant supplier. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (VetMedicineLot existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"VetMedicineStock: lot refused — lot {lot.LotId} already in the chest.";
                }
            }

            lots.Add(lot);
            diag.Add($"VetMedicineStock: received {lot.Doses} doses '{lot.MedicineName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses doses FIFO (oldest first). Returns null with a diagnostic
        /// when the chest cannot cover the request — no doses are conjured.
        /// </summary>
        public List<VetMedicineDispenseLine> TryDispenseDoses(string medicineName, int doses, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<VetMedicineDispenseLine>();
            if (string.IsNullOrWhiteSpace(medicineName) || doses <= 0)
            {
                diag.Add("VetMedicineStock: dispense refused — medicine name and positive dose count required.");
                return null;
            }

            int available = 0;
            foreach (VetMedicineLot lot in lots)
            {
                if (string.Equals(lot.MedicineName, medicineName, StringComparison.OrdinalIgnoreCase))
                {
                    available += lot.Doses;
                }
            }

            if (available < doses)
            {
                diag.Add($"VetMedicineStock: only {available} doses of '{medicineName}' on hand, need {doses} — shortfall, no doses conjured.");
                return null;
            }

            var ordered = new List<VetMedicineLot>();
            foreach (VetMedicineLot lot in lots)
            {
                if (string.Equals(lot.MedicineName, medicineName, StringComparison.OrdinalIgnoreCase))
                {
                    ordered.Add(lot);
                }
            }

            ordered.Sort((a, b) => a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex));

            int remaining = doses;
            foreach (VetMedicineLot lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Doses);
                lot.Doses -= take;
                remaining -= take;
                lines.Add(new VetMedicineDispenseLine
                {
                    LotId = lot.LotId,
                    MedicineName = lot.MedicineName,
                    DosesTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the chest never carries ghost stock.
            lots.RemoveAll(l => l.Doses <= 0);

            diag.Add($"VetMedicineStock: dispensed {doses} doses '{medicineName}' (day {dayIndex}) from {lines.Count} lot(s).");
            return lines;
        }

        /// <summary>Counts all on-hand doses of a medicine.</summary>
        public int DosesOnHand(string medicineName)
        {
            int total = 0;
            foreach (VetMedicineLot lot in lots)
            {
                if (string.Equals(lot.MedicineName, medicineName, StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.Doses;
                }
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class VetMedicineStockSaveDto
        {
            public List<VetMedicineLot> Lots = new List<VetMedicineLot>();
        }

        public VetMedicineStockSaveDto CaptureSaveDto()
        {
            var dto = new VetMedicineStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(VetMedicineStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// W10A: the one-time opening endowment of the vet's medicine chest.
    /// Explicitly marked BOOTSTRAP — it stands in for the practitioner's opening
    /// stock (a traveling vet arrived with a stocked case) and is NEVER silently
    /// replenished: later stock comes only from import orders or merchant purchases.
    /// </summary>
    public static class VetMedicineBootstrap
    {
        /// <summary>TUNING: opening remedy doses in the vet's case.</summary>
        public const int BootstrapRemedyDoses = 40;

        /// <summary>TUNING: opening wound-dressing units in the vet's case.</summary>
        public const int BootstrapDressingUnits = 20;

        public static void ApplyBootstrapEndowment(
            VetMedicineStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("VetMedicineBootstrap: no medicine stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("VetMedicineBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new VetMedicineLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                MedicineName = "veterinary-remedies",
                Doses = BootstrapRemedyDoses,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"VetMedicineBootstrap: {rejection}");
                return;
            }

            rejection = stock.ReceiveLot(new VetMedicineLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                MedicineName = "veterinary-dressings",
                Doses = BootstrapDressingUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"VetMedicineBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("VetMedicineBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W10A: the veterinary medicine supply link. Two honest restock paths:
    /// (a) a DECLARED off-map trade link (EQU-1 precedent: named origin, real
    /// distance and transit days), matching how frontier vets ordered prepared
    /// remedies from eastern wholesale drug houses by rail; (b) a named on-map
    /// merchant purchase (the apothecary or general store — "the store can
    /// buy/sell whatever", FVS), recorded with the merchant's identity as the
    /// lot's provenance. No synthetic stock on either path.
    /// </summary>
    public static class VetMedicineSupply
    {
        public const string VetMedicineMaterialId = "veterinary-remedies";

        /// <summary>
        /// Registers veterinary remedies as an importable material (EQU-1 extension
        /// path). Historical: 1870s frontier vets ordered prepared remedies from
        /// eastern wholesale drug houses shipped by rail — a named origin, real
        /// transit, real cost. Prices/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureVetMedicineImportable(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                VetMedicineMaterialId, "Veterinary remedies (doses)",
                "Off-map wholesale drug house, via railhead", 250, 14, 20));
            if (problem != null && diag != null)
            {
                diag.Add($"VetMedicineSupply: vet medicine import registration: {problem}");
            }
        }

        /// <summary>
        /// Receives an import order's arrival into the chest as a named lot.
        /// The caller moves real lots; this only records custody with provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            VetMedicineStock stock,
            string medicineName,
            int doses,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "VetMedicineSupply.ReceiveImportArrival: no medicine stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "VetMedicineSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "VetMedicineSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new VetMedicineLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                MedicineName = medicineName ?? string.Empty,
                Doses = doses,
                AcquiredDayIndex = dayIndex,
                ImportOrderId = importOrderId,
                OriginName = originName ?? string.Empty,
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Records a purchase of remedies from a named on-map merchant (apothecary
        /// or general store). The merchant's business id is the lot's provenance;
        /// the purchase itself is the vet's ledger event (the caller records the
        /// outflow against the merchant). No merchant, no purchase.
        /// </summary>
        public static string ReceiveMerchantPurchase(
            VetMedicineStock stock,
            string medicineName,
            int doses,
            string supplierBusinessId,
            string supplierBusinessName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "VetMedicineSupply.ReceiveMerchantPurchase: no medicine stock.";
            if (string.IsNullOrWhiteSpace(supplierBusinessId))
                return "VetMedicineSupply.ReceiveMerchantPurchase: the merchant must be named — no anonymous suppliers.";
            if (idRegistry == null) return "VetMedicineSupply.ReceiveMerchantPurchase: no id registry.";

            return stock.ReceiveLot(new VetMedicineLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                MedicineName = medicineName ?? string.Empty,
                Doses = doses,
                AcquiredDayIndex = dayIndex,
                SupplierBusinessId = supplierBusinessId,
                SupplierBusinessName = supplierBusinessName ?? string.Empty,
                IsBootstrapEndowment = false,
            }, diag);
        }
    }

    /// <summary>
    /// W10A: dispensing real doses against a T1F AnimalTreatment. The treatment's
    /// MedicineSource is stamped with the dispense lines' provenance chains, so an
    /// NX-2B disease treatment traces dose-by-dose to a real lot. A stock shortfall
    /// leaves the treatment unmedicated and LOUD — never silently medicated.
    /// </summary>
    public static class VetMedicineDispensing
    {
        /// <summary>
        /// Dispenses the named medicine for one animal treatment. Returns null on
        /// success; on shortfall the treatment is left with MedicineSource
        /// "unmedicated — stock shortfall" and the diagnostic explains why.
        /// </summary>
        public static string DispenseForTreatment(
            VetMedicineStock stock,
            AnimalTreatment treatment,
            string medicineName,
            int doses,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null) return "VetMedicineDispensing: no medicine stock.";
            if (treatment == null) return "VetMedicineDispensing: no treatment record.";

            if (string.IsNullOrWhiteSpace(medicineName) || doses <= 0)
            {
                treatment.MedicineSource = "unmedicated";
                diagnostics.Add("VetMedicineDispensing: no medicine named for this treatment — recorded as unmedicated.");
                return null;
            }

            List<VetMedicineDispenseLine> lines = stock.TryDispenseDoses(medicineName, doses, dayIndex, diagnostics);
            if (lines == null)
            {
                treatment.MedicineSource = "unmedicated — stock shortfall";
                diagnostics.Add($"VetMedicineDispensing: animal {treatment.AnimalId} treated without medicine — " +
                    $"chest cannot cover {doses} dose(s) of '{medicineName}'. The treatment record says so.");
                return $"VetMedicineDispensing: shortfall for '{medicineName}' — treatment recorded unmedicated.";
            }

            var chains = new List<string>();
            foreach (VetMedicineDispenseLine line in lines)
            {
                chains.Add($"{line.DosesTaken}x {line.MedicineName} ({line.ProvenanceChain})");
            }

            treatment.MedicineSource = string.Join("; ", chains.ToArray());
            return null;
        }
    }

    /// <summary>
    /// W10A: closes the T1F-deferred "vet invoice settlement wiring". A T1F VetInvoice
    /// is an ordinary commercial obligation: recording it books a real payable on the
    /// farm/business (via BusinessLiabilityLedger.BuyOnCredit, counterparty = the named
    /// practitioner), and paying it is a real outflow with provenance. No fiat.
    /// </summary>
    public static class VetInvoiceSettlement
    {
        /// <summary>
        /// Records the invoice as a payable: the farm/business owes the practitioner.
        /// Returns the liability, or null with a diagnostic when the invoice cannot
        /// be recorded (zero total, missing parties).
        /// </summary>
        public static BusinessLiability RecordInvoicePayable(
            VetInvoice invoice,
            BusinessLiabilityLedger liabilityLedger,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (invoice == null)
            {
                diagnostics.Add("VetInvoiceSettlement: no invoice — nothing to record.");
                return null;
            }

            if (liabilityLedger == null || idRegistry == null)
            {
                diagnostics.Add("VetInvoiceSettlement: need a liability ledger and id registry — the payable is unrecorded, not free.");
                return null;
            }

            int total = invoice.TotalCents;
            if (total <= 0)
            {
                diagnostics.Add("VetInvoiceSettlement: invoice total is zero — no payable recorded for a zero invoice.");
                return null;
            }

            string counterparty = string.IsNullOrWhiteSpace(invoice.PractitionerName)
                ? invoice.PractitionerId
                : invoice.PractitionerName;

            string description = $"vet invoice day {invoice.DayIndex}: call-out {invoice.CallOutFeeCents}c + " +
                $"travel {invoice.TravelFeeCents}c ({invoice.TravelMiles:F1} mi both ways) + " +
                $"{invoice.Treatments.Count} treatment(s) {invoice.TreatmentTotalCents}c";

            BusinessLiability liability = liabilityLedger.BuyOnCredit(
                idRegistry, invoice.FarmOrBusinessId, counterparty, total,
                description, dayIndex, diagnostics);

            if (liability != null)
            {
                diagnostics.Add($"VetInvoiceSettlement: recorded payable {liability.LiabilityId} — " +
                    $"{invoice.FarmOrBusinessId} owes {counterparty} {total}c.");
            }

            return liability;
        }

        /// <summary>
        /// Pays a recorded invoice payable from the payer's household ledger.
        /// Delegates to BusinessLiabilityLedger.Repay — provenance and
        /// no-overpayment rules are the ledger's, not reimplemented here.
        /// </summary>
        public static string PayInvoice(
            BusinessLiability liability,
            BusinessLiabilityLedger liabilityLedger,
            HouseholdLedger payerLedger,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (liability == null) return "VetInvoiceSettlement: no liability to pay.";
            if (liabilityLedger == null) return "VetInvoiceSettlement: no liability ledger.";

            return liabilityLedger.Repay(
                liability.LiabilityId.ToString(), liability.BalanceCents,
                dayIndex, payerLedger, diagnostics);
        }
    }

    /// <summary>
    /// W10A: the vet as a skilled occupation. There is no occupation enum to extend —
    /// EmploymentRelationship carries RoleDisplayName — so this records the canonical
    /// display names and builds the relationship record for a hired or contracted vet
    /// (on-map; off-map practitioners stay person/business references on
    /// VetPractitioner per Canon XXVII §6.2).
    /// </summary>
    public static class VetOccupation
    {
        /// <summary>Canonical display name for a hired veterinarian.</summary>
        public const string RoleVeterinarian = "Veterinarian";

        /// <summary>Canonical display name for a vet's assistant.</summary>
        public const string RoleVeterinaryAssistant = "Veterinary assistant";

        /// <summary>Canonical display name for a farrier.</summary>
        public const string RoleFarrier = "Farrier";

        /// <summary>
        /// Builds an employment relationship for a vet, assistant, or farrier.
        /// Compensation lives on the relationship (EmploymentRelationship is the
        /// authority); the caller registers it with the registry.
        /// </summary>
        public static EmploymentRelationship CreateEmployment(
            int employeePersonId,
            string employerBusinessId,
            string roleDisplayName,
            EmploymentKind kind,
            int agreedWeeklyWageCents,
            int startDayIndex,
            string notes)
        {
            string role = roleDisplayName;
            if (string.IsNullOrWhiteSpace(role)) role = RoleVeterinarian;

            return new EmploymentRelationship
            {
                EmployeePersonId = employeePersonId,
                EmployerBusinessId = employerBusinessId ?? string.Empty,
                RoleDisplayName = role,
                Kind = kind,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = new CompensationTerms { AgreedWeeklyWageCents = Math.Max(0, agreedWeeklyWageCents) },
                StartDayIndex = startDayIndex,
                EndDayIndex = -1,
                Source = EmploymentSource.Authored,
                LegacyNotes = notes ?? string.Empty,
            };
        }
    }
}
