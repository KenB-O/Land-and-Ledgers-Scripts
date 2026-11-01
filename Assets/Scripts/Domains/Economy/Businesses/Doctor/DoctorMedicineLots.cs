using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// W1A: one medicine lot with full upstream provenance. Every consumed dose
    /// must trace to a real lot from a real supplier — a declared import order
    /// from the named off-map wholesale drug house — or to the explicit one-time
    /// bootstrap endowment. No orphan lots, no synthetic stock.
    /// </summary>
    [Serializable]
    public sealed class DoctorMedicineLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string MedicineName = string.Empty;
        public int Doses;
        public int AcquiredDayIndex;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map origin, e.g. "Off-map wholesale drug house, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>Any extra upstream link (wholesale house name, local compounding).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="DoctorMedicineBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public DoctorMedicineLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via import orders";
            }

            string order = string.IsNullOrWhiteSpace(ImportOrderId) ? "no-order" : ImportOrderId;
            string origin = string.IsNullOrWhiteSpace(OriginName) ? "unnamed-origin" : OriginName;
            string chain = $"lot {LotId} / import order {order} / {origin}";
            if (!string.IsNullOrWhiteSpace(SupplierNote))
            {
                chain += $" / {SupplierNote}";
            }

            return chain;
        }
    }

    /// <summary>
    /// W1A: one line of a dispense result — which lot the doses came from and the
    /// full upstream chain. Treatment records carry these so every dose is traceable.
    /// </summary>
    [Serializable]
    public sealed class DoctorMedicineDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string MedicineName = string.Empty;
        public int DosesTaken;
        public string ProvenanceChain = string.Empty;

        public DoctorMedicineDispenseLine() { }
    }

    /// <summary>
    /// W1A: the doctor's medicine cabinet. Doses dispense FIFO (oldest stock
    /// first); every dispense returns the lots consumed so treatments record
    /// provenance. Refusals are loud — stock shortfalls are never faked.
    /// </summary>
    public sealed class DoctorMedicineStock
    {
        private readonly List<DoctorMedicineLot> lots = new List<DoctorMedicineLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<DoctorMedicineLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the cabinet. Lots must name their supplier chain
        /// (import order + origin) or carry the explicit bootstrap flag.
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string ReceiveLot(DoctorMedicineLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "DoctorMedicineStock: null lot refused — no medicine without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.MedicineName))
                return "DoctorMedicineStock: lot refused — medicine name is required.";
            if (lot.Doses <= 0)
                return $"DoctorMedicineStock: lot refused — '{lot.MedicineName}' needs a positive dose count.";
            if (!lot.IsBootstrapEndowment
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"DoctorMedicineStock: lot refused — '{lot.MedicineName}' names no import order or origin. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"DoctorMedicineStock: lot refused — lot {lot.LotId} already in the cabinet.";
                }
            }

            lots.Add(lot);
            diag.Add($"DoctorMedicineStock: received {lot.Doses} doses '{lot.MedicineName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses doses FIFO (oldest first). Returns null with a diagnostic
        /// when the cabinet cannot cover the request — no doses are conjured.
        /// </summary>
        public List<DoctorMedicineDispenseLine> TryDispenseDoses(string medicineName, int doses, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<DoctorMedicineDispenseLine>();
            if (string.IsNullOrWhiteSpace(medicineName) || doses <= 0)
            {
                diag.Add("DoctorMedicineStock: dispense refused — medicine name and positive dose count required.");
                return null;
            }

            int available = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.MedicineName, medicineName, StringComparison.OrdinalIgnoreCase))
                {
                    available += lot.Doses;
                }
            }

            if (available < doses)
            {
                diag.Add($"DoctorMedicineStock: only {available} doses of '{medicineName}' on hand, need {doses} — shortfall, no doses conjured.");
                return null;
            }

            // FIFO by acquisition day.
            var ordered = new List<DoctorMedicineLot>();
            foreach (var lot in lots)
            {
                if (string.Equals(lot.MedicineName, medicineName, StringComparison.OrdinalIgnoreCase))
                {
                    ordered.Add(lot);
                }
            }

            ordered.Sort((a, b) => a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex));

            int remaining = doses;
            foreach (var lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Doses);
                lot.Doses -= take;
                remaining -= take;
                lines.Add(new DoctorMedicineDispenseLine
                {
                    LotId = lot.LotId,
                    MedicineName = lot.MedicineName,
                    DosesTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the cabinet never carries ghost stock.
            lots.RemoveAll(l => l.Doses <= 0);

            diag.Add($"DoctorMedicineStock: dispensed {doses} doses '{medicineName}' (day {dayIndex}) from {lines.Count} lot(s).");
            return lines;
        }

        /// <summary>Counts all unexpired on-hand doses of a medicine.</summary>
        public int DosesOnHand(string medicineName)
        {
            int total = 0;
            foreach (var lot in lots)
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
        public sealed class DoctorMedicineStockSaveDto
        {
            public List<DoctorMedicineLot> Lots = new List<DoctorMedicineLot>();
        }

        public DoctorMedicineStockSaveDto CaptureSaveDto()
        {
            var dto = new DoctorMedicineStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(DoctorMedicineStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// W1A: the one-time opening endowment of the doctor's medicine cabinet.
    /// Explicitly marked BOOTSTRAP — it stands in for the practice's opening
    /// stocking order (1870s doctors arrived with a stocked medicine chest) and
    /// is NEVER silently replenished: later stock comes only from import orders
    /// of <see cref="DoctorMedicineSupply.MedicineStockMaterialId"/>.
    /// </summary>
    public static class DoctorMedicineBootstrap
    {
        /// <summary>TUNING: opening remedy doses in the doctor's chest.</summary>
        public const int BootstrapRemedyDoses = 60;

        /// <summary>TUNING: opening wound-dressing units in the doctor's chest.</summary>
        public const int BootstrapDressingUnits = 30;

        public static void ApplyBootstrapEndowment(
            DoctorMedicineStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("DoctorMedicineBootstrap: no medicine stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("DoctorMedicineBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new DoctorMedicineLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                MedicineName = DoctorTreatmentCatalog.MedicineDoseItemId,
                Doses = BootstrapRemedyDoses,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening medicine chest (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"DoctorMedicineBootstrap: {rejection}");
                return;
            }

            rejection = stock.ReceiveLot(new DoctorMedicineLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                MedicineName = DoctorTreatmentCatalog.DressingItemId,
                Doses = BootstrapDressingUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening medicine chest (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"DoctorMedicineBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("DoctorMedicineBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W1A: the medicine supply link. Survey result: no medicine supplier exists
    /// anywhere in the import catalog — so the honest path is a DECLARED off-map
    /// trade link (EQU-1 precedent: named origin, real distance and transit days),
    /// matching how frontier doctors stocked from eastern wholesale drug houses
    /// by rail. The doctor reorders through <see cref="ImportService"/>.
    /// </summary>
    public static class DoctorMedicineSupply
    {
        public const string MedicineStockMaterialId = "medicine-stock";

        /// <summary>
        /// Registers prepared medicines as an importable material (EQU-1 extension
        /// path). Historical: 1870s frontier doctors ordered prepared medicines
        /// from eastern wholesale drug houses shipped by rail — a named origin,
        /// real transit, real cost. Prices/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureMedicineImportable(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                MedicineStockMaterialId, "Prepared medicines (doses)",
                "Off-map wholesale drug house, via railhead", 250, 14, 20));
            if (problem != null && diag != null)
            {
                diag.Add($"DoctorMedicineSupply: medicine import registration: {problem}");
            }
        }

        /// <summary>
        /// Receives an import order's arrival into the cabinet as a named lot.
        /// The caller moves real lots; this only records custody with provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            DoctorMedicineStock stock,
            string medicineName,
            int doses,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "DoctorMedicineSupply.ReceiveImportArrival: no medicine stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "DoctorMedicineSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "DoctorMedicineSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new DoctorMedicineLot
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
    }
}
