using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Pass VIII Worker 3: property-side authorization for construction.
    /// The runtime adapter implements this against the real
    /// <see cref="TitleAuthority"/> (parcel registered + held by the
    /// commissioning household). Tests fake it. A project is never created
    /// on a parcel nobody holds.
    /// </summary>
    public interface IConstructionParcelAuthority
    {
        /// <summary>
        /// Returns null when householdId may build on parcelId; a refusal
        /// otherwise. Refusals leave no project behind.
        /// </summary>
        string AuthorizeBuild(int householdId, string parcelId, int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// Production parcel gate backed by the existing title chain. This only
    /// authorizes a household that currently holds the named parcel; leases
    /// and easements need their own explicit authority before they can grant
    /// construction rights.
    /// </summary>
    public sealed class TitleConstructionParcelAuthority : IConstructionParcelAuthority
    {
        private readonly TitleAuthority titles;

        public TitleConstructionParcelAuthority(TitleAuthority titles)
        {
            this.titles = titles;
        }

        public string AuthorizeBuild(int householdId, string parcelId, int dayIndex, List<string> diagnostics)
        {
            if (householdId < 0) return "a valid commissioning household is required.";
            if (string.IsNullOrWhiteSpace(parcelId)) return "a registered parcel id is required.";
            if (titles == null) return "the live TitleAuthority is unavailable.";
            string holder = titles.CurrentHolder(parcelId);
            if (holder == null) return $"parcel '{parcelId}' is not registered in TitleAuthority.";
            string expected = "household:" + householdId;
            if (!string.Equals(holder, expected, StringComparison.Ordinal))
                return $"parcel '{parcelId}' is held by '{holder}', not '{expected}'.";
            diagnostics?.Add($"TitleConstructionParcelAuthority: H{householdId} holds parcel '{parcelId}' and may commission construction.");
            return null;
        }
    }

    /// <summary>
    /// Pass VIII Worker 3: returns staged material lines to their source
    /// (the supplier's real lots) when a project is cancelled. The port is
    /// all-or-nothing per line: a line is either fully returned or not at
    /// all — never half-returned, never duplicated.
    /// </summary>
    public interface IConstructionMaterialRestock
    {
        /// <summary>
        /// Returns one consumed line to the source lots. Returns null on
        /// success, a refusal otherwise (nothing returned on refusal).
        /// </summary>
        string TryReturn(ConsumedMaterialLine line, string projectLabel, int dayIndex,
            List<string> diagnostics);
    }

    /// <summary>
    /// Pass VIII Worker 3: one sold material line inside a supplier sale —
    /// real lots, real provenance, the supplier's real unit price. Money
    /// itself moves only through ledger authorities, never here: the sale
    /// records WHAT was sold for WHAT agreed price.
    /// </summary>
    [Serializable]
    public sealed class ConstructionMaterialSaleLine
    {
        public string DisplayName = string.Empty;
        public int Units;
        public string UnitLabel = string.Empty;
        public int UnitPriceCents;
        public string SupplierBusinessId = string.Empty;
        public string Provenance = string.Empty;

        public ConstructionMaterialSaleLine() { }

        public int LineTotalCents => Math.Max(0, Units) * Math.Max(0, UnitPriceCents);
    }

    /// <summary>
    /// Pass VIII Worker 3: the audit record of one supplier sale to a
    /// construction project package — the procurement leg of the chain.
    /// </summary>
    [Serializable]
    public sealed class ConstructionMaterialSale
    {
        public string SaleId = string.Empty;
        public string SupplierBusinessId = string.Empty;
        public string ProjectId = string.Empty;
        public string PackageId = string.Empty;
        public int DayIndex;
        public List<ConstructionMaterialSaleLine> Lines = new List<ConstructionMaterialSaleLine>();

        public ConstructionMaterialSale() { }

        public int TotalCents
        {
            get
            {
                int total = 0;
                foreach (ConstructionMaterialSaleLine line in Lines)
                    if (line != null) total += line.LineTotalCents;
                return total;
            }
        }

        public int TotalUnits
        {
            get
            {
                int total = 0;
                foreach (ConstructionMaterialSaleLine line in Lines)
                    if (line != null) total += Math.Max(0, line.Units);
                return total;
            }
        }
    }

    /// <summary>
    /// Pass VIII Worker 3: the supplier-side sale authority for construction
    /// procurement. The runtime adapter implements this against the real
    /// business runtimes (e.g. the lumber yard's
    /// TrySellToConstructionProject, which dispenses real lots with
    /// provenance); tests fake it with finite stock. The port MUST price
    /// lines from the same price list the commissioner priced from — the
    /// commissioner verifies the sale total matches the priced total and
    /// refuses loudly on any silent overcharge.
    /// </summary>
    public interface IConstructionMaterialSalePort
    {
        /// <summary>
        /// Sells real lots to the named project package. Returns null on
        /// success (with the recorded sale); a refusal otherwise — nothing
        /// is sold on refusal.
        /// </summary>
        string TrySellToProject(
            string supplierBusinessId,
            string projectId,
            string packageId,
            List<ConstructionMaterialRequirement> requirements,
            IConstructionMaterialPricePort pricePort,
            int dayIndex,
            List<string> diagnostics,
            out ConstructionMaterialSale sale);
    }

    /// <summary>
    /// Pass VIII Worker 3: shared cash-moving helper for construction-side
    /// authorities. Real cash stores only, through the CreditCashBridge —
    /// the one cash writer stays the HouseholdLedger-backed bridge.
    /// </summary>
    public static class ConstructionCashMover
    {
        public static string MoveCash(CreditCashBridge cash, string payer, string payee,
            int amountCents, int dayIndex, string purpose, List<string> diag)
        {
            if (amountCents <= 0) return null;
            if (cash == null) return "ConstructionCashMover: no cash bridge — real cash only.";
            CreditCashAccount payerCash = cash.OpenWindow(payer, diag);
            CreditCashAccount payeeCash = cash.OpenWindow(payee, diag);
            if (payerCash == null || payeeCash == null)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"no real cash store for '{payer}' or '{payee}'.";
            }
            if (payerCash.BalanceCents < amountCents)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"'{payer}' holds {payerCash.BalanceCents}c, needs {amountCents}c.";
            }
            payerCash.BalanceCents -= amountCents;
            payeeCash.BalanceCents += amountCents;
            string p1 = cash.CommitWindow(payerCash, dayIndex, purpose, payee, diag);
            string p2 = cash.CommitWindow(payeeCash, dayIndex, purpose, payer, diag);
            return p1 ?? p2;
        }
    }

    /// <summary>
    /// Pass VIII Worker 3: the project's on-site staging area. Freighted
    /// materials land HERE (custody proven by the shipment chain), and
    /// work packages stage FROM here — materials never teleport from the
    /// supplier's lots into a package. Units are conserved: receives add,
    /// staging consumes, cancellation drains; the same units are never
    /// counted twice.
    /// </summary>
    [Serializable]
    public sealed class ConstructionSiteStockpile
    {
        [Serializable]
        public sealed class StockpileLine
        {
            public string DisplayName = string.Empty;
            public string UnitLabel = string.Empty;
            public int Units;
            public string Provenance = string.Empty;

            public StockpileLine() { }
        }

        public string ProjectId = string.Empty;
        public List<StockpileLine> Lines = new List<StockpileLine>();

        public ConstructionSiteStockpile() { }

        public int AvailableUnits(string displayName)
        {
            int total = 0;
            foreach (StockpileLine line in Lines)
            {
                if (line == null) continue;
                if (string.Equals(line.DisplayName, displayName, StringComparison.Ordinal))
                    total += Math.Max(0, line.Units);
            }
            return total;
        }

        /// <summary>Receives delivered sale lines into the stockpile.</summary>
        public string Receive(ConstructionMaterialSale sale, List<string> diag)
        {
            if (sale == null) return "ConstructionSiteStockpile.Receive: no sale.";
            if (!string.IsNullOrWhiteSpace(ProjectId)
                && !string.IsNullOrWhiteSpace(sale.ProjectId)
                && !string.Equals(ProjectId, sale.ProjectId, StringComparison.Ordinal))
                return $"ConstructionSiteStockpile.Receive: sale '{sale.SaleId}' is for project '{sale.ProjectId}', not '{ProjectId}' — refusing misdelivered goods.";
            foreach (ConstructionMaterialSaleLine saleLine in sale.Lines)
            {
                if (saleLine == null || saleLine.Units <= 0) continue;
                Lines.Add(new StockpileLine
                {
                    DisplayName = saleLine.DisplayName ?? string.Empty,
                    UnitLabel = saleLine.UnitLabel ?? string.Empty,
                    Units = saleLine.Units,
                    Provenance = saleLine.Provenance ?? string.Empty,
                });
            }
            diag?.Add($"ConstructionSiteStockpile: received sale '{sale.SaleId}' ({sale.TotalUnits} unit(s)) for project '{ProjectId}'.");
            return null;
        }

        /// <summary>
        /// All-or-nothing check: every requirement covered in full by the
        /// stockpile. Nothing is consumed by the check itself.
        /// </summary>
        public bool CoversAll(List<ConstructionMaterialRequirement> requirements)
        {
            if (requirements == null) return true;
            foreach (ConstructionMaterialRequirement requirement in requirements)
            {
                if (requirement == null || !requirement.IsCoherent) return false;
                if (AvailableUnits(requirement.MaterialDisplayName) < requirement.RequiredUnits)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Consumes the requirements FIFO with provenance. The caller must
        /// have verified <see cref="CoversAll"/> first; on any shortfall this
        /// refuses and consumes NOTHING.
        /// </summary>
        public string ConsumeForPackage(WorkPackage package, int dayIndex, List<string> diag,
            out List<ConsumedMaterialLine> consumed)
        {
            consumed = new List<ConsumedMaterialLine>();
            if (package == null) return "ConstructionSiteStockpile.ConsumeForPackage: no package.";
            if (!CoversAll(package.Materials))
                return $"ConstructionSiteStockpile: package '{package.PackageId}' is not fully covered by the on-site stockpile — nothing consumed, no synthetic stock.";
            foreach (ConstructionMaterialRequirement requirement in package.Materials)
            {
                int needed = Math.Max(0, requirement.RequiredUnits);
                var provenanceParts = new List<string>();
                foreach (StockpileLine line in Lines)
                {
                    if (needed <= 0) break;
                    if (line == null || line.Units <= 0) continue;
                    if (!string.Equals(line.DisplayName, requirement.MaterialDisplayName, StringComparison.Ordinal)) continue;
                    int take = Math.Min(needed, line.Units);
                    line.Units -= take;
                    needed -= take;
                    if (!string.IsNullOrWhiteSpace(line.Provenance))
                        provenanceParts.Add(line.Provenance);
                }
                if (needed > 0)
                    return $"ConstructionSiteStockpile: shortfall on '{requirement.MaterialDisplayName}' mid-consume — refusing (this is a bug: CoversAll passed).";
                consumed.Add(new ConsumedMaterialLine
                {
                    DisplayName = requirement.MaterialDisplayName,
                    Units = requirement.RequiredUnits,
                    UnitLabel = requirement.UnitLabel ?? string.Empty,
                    Provenance = string.Join(" | ", provenanceParts.ToArray()),
                });
            }
            Lines.RemoveAll(l => l == null || l.Units <= 0);
            diag?.Add($"ConstructionSiteStockpile: package '{package.PackageId}' consumed {consumed.Count} line(s) from the on-site stockpile (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Drains every remaining unit for cancellation accounting. The
        /// stockpile is empty afterwards; drained lines are returned to the
        /// caller for restock — each unit leaves exactly once.
        /// </summary>
        public List<ConsumedMaterialLine> DrainAll(string projectLabel, int dayIndex)
        {
            var drained = new List<ConsumedMaterialLine>();
            foreach (StockpileLine line in Lines)
            {
                if (line == null || line.Units <= 0) continue;
                drained.Add(new ConsumedMaterialLine
                {
                    DisplayName = line.DisplayName,
                    Units = line.Units,
                    UnitLabel = line.UnitLabel ?? string.Empty,
                    Provenance = (line.Provenance ?? string.Empty) +
                        $" (drained from site stockpile {projectLabel}, day {dayIndex})",
                });
            }
            Lines.Clear();
            return drained;
        }

        #region Save / Load
        [Serializable]
        public sealed class ConstructionSiteStockpileSaveDto
        {
            public string ProjectId = string.Empty;
            public List<StockpileLine> Lines = new List<StockpileLine>();
        }

        public ConstructionSiteStockpileSaveDto CaptureSaveDto()
        {
            var dto = new ConstructionSiteStockpileSaveDto { ProjectId = ProjectId };
            foreach (StockpileLine line in Lines)
                if (line != null && line.Units > 0) dto.Lines.Add(line);
            return dto;
        }

        /// <summary>Clear-then-load: loading the same snapshot twice never duplicates units.</summary>
        public void LoadFromSaveDto(ConstructionSiteStockpileSaveDto dto, List<string> diag)
        {
            Lines.Clear();
            ProjectId = string.Empty;
            if (dto == null) return;
            ProjectId = dto.ProjectId ?? string.Empty;
            foreach (StockpileLine line in dto.Lines ?? new List<StockpileLine>())
            {
                if (line == null || line.Units <= 0) continue;
                Lines.Add(line);
            }
            diag?.Add($"ConstructionSiteStockpile: loaded {Lines.Count} line(s) for project '{ProjectId}' — clear-then-load, no duplicated units.");
        }
        #endregion
    }

    /// <summary>
    /// Pass VIII Worker 3: everything the freight leg needs, stated up
    /// front. The wagon, driver, loader and unloader are real; the freight
    /// charge is a REAL quoted charge (a freight business quote or the
    /// supplier's delivery policy) — never invented. Zero charge means the
    /// buyer hauls with their own transport.
    /// </summary>
    public sealed class ConstructionFreightPlan
    {
        public FreightShipmentService Freight;
        public EquipmentAsset Wagon;
        public EntityId DriverId = EntityId.Invalid;
        public List<EntityId> DraftAnimalIds = new List<EntityId>();
        public EntityId LoaderId = EntityId.Invalid;
        public EntityId UnloaderId = EntityId.Invalid;
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public PersonScheduleTracker Tracker;
        public string SiteLabel = string.Empty;
        /// <summary>The quoted total freight charge across all trips. The commissioner's overrun gate and funding record read the total.</summary>
        public int FreightChargeCents;
        /// <summary>DEF-016: how many wagon trips the quoted load needs. 0 on a hand-built plan reads as 1.</summary>
        public int TripCount;
        /// <summary>DEF-016: the charge for one trip. 0 on a hand-built plan reads as the whole FreightChargeCents.</summary>
        public int FreightChargeCentsPerTrip;
        public string CarrierPayeeLabel = string.Empty;
        public CreditCashBridge Cash;
        public string BuyerOwnerName = string.Empty;
        public FreightCargoSpec CargoSpec = new FreightCargoSpec();
        /// <summary>Transit drive seconds applied in one deterministic step.</summary>
        public float TransitGameSeconds = 10000f;
    }

    /// <summary>
    /// Pass VIII Worker 3: moves procured materials from the supplier to
    /// the building site through the LEGITIMATE shipment chain — the
    /// sibling-owned <see cref="FreightShipmentService"/> is consumed, never
    /// modified. Loading, dispatch, transit, unloading and conservation
    /// verification all run for real; only conserved goods reach the site
    /// stockpile.
    /// </summary>
    public sealed class ConstructionMaterialFreightCoordinator
    {
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>DEF-016: total freight actually paid by the last FreightSaleToSite call (0 when the call paid nothing or refused before payment). Reset per call.</summary>
        public int LastFreightPaidCents { get; private set; }

        /// <summary>QA-20: the coordinator holds no save slot and is rebuilt
        /// after a load, so its sequence restarts at 0. Seed it from the
        /// freight service's own tracked shipments before minting, so a new
        /// id never collides with one the service already holds (the service
        /// persists its shipments through its own save DTO, and restores them
        /// into the tracked list). Only the cship- prefix is ours; other
        /// shipment ids are ignored. Never moves the sequence backward.</summary>
        private void SeedSequenceFromService(FreightShipmentService service)
        {
            if (service == null) return;
            foreach (LogisticsShipmentState existing in service.TrackedShipments)
            {
                if (existing == null) continue;
                string id = existing.ShipmentId;
                if (string.IsNullOrEmpty(id)
                    || !id.StartsWith("cship-", StringComparison.Ordinal)) continue;
                if (int.TryParse(id.Substring("cship-".Length), out int n) && n >= sequence)
                    sequence = n + 1;
            }
        }

        /// <summary>
        /// Freights one recorded sale to the site. Returns null on success
        /// (goods in the stockpile); a refusal otherwise — on refusal the
        /// sale still stands at the supplier (real purchase, buyer's
        /// goods), and only the trips that completed before the failure
        /// reached the stockpile (DEF-016: each trip's goods are received
        /// as its trip completes, so a later failure never strands
        /// delivered goods in a completed shipment with no inventory
        /// holding them).
        /// </summary>
        public string FreightSaleToSite(
            ConstructionMaterialSale sale,
            ConstructionFreightPlan plan,
            ConstructionSiteStockpile stockpile,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            LastFreightPaidCents = 0;
            if (sale == null || plan == null || stockpile == null)
                return "ConstructionMaterialFreightCoordinator: sale, freight plan and stockpile required.";
            if (plan.Freight == null)
                return "ConstructionMaterialFreightCoordinator: no FreightShipmentService — materials move through the real shipment chain or not at all.";
            if (plan.Wagon == null || string.IsNullOrWhiteSpace(plan.Wagon.AssetId))
                return "ConstructionMaterialFreightCoordinator: no wagon — refusing phantom transport.";
            if (plan.Tasks == null || plan.Budgets == null)
                return "ConstructionMaterialFreightCoordinator: freight handling is real work — TaskAuthority and budgets required.";
            if (sale.TotalUnits <= 0)
                return $"ConstructionMaterialFreightCoordinator: sale '{sale.SaleId}' has no units to freight.";
            if (plan.FreightChargeCents < 0)
                return "ConstructionMaterialFreightCoordinator: a negative freight charge is not a quote — refusing.";
            if (plan.FreightChargeCents > 0 && string.IsNullOrWhiteSpace(plan.CarrierPayeeLabel))
                return "ConstructionMaterialFreightCoordinator: a freight charge with no carrier payee — refusing.";

            // DEF-016: the plan is the quoting authority. Reconcile it
            // (trips x per-trip must equal the total) and check it can
            // actually carry the load at this wagon, BEFORE any money
            // moves — a plan that quotes too few trips refuses here with
            // "blocked pre-load", instead of paying first and then
            // blocking at the shipment service as before.
            int trips = Math.Max(1, plan.TripCount);
            int perTripCharge = plan.FreightChargeCentsPerTrip > 0
                ? plan.FreightChargeCentsPerTrip : plan.FreightChargeCents;
            if ((long)trips * perTripCharge != plan.FreightChargeCents)
                return "ConstructionMaterialFreightCoordinator: freight plan charge does not reconcile — "
                    + trips + " trip(s) x " + perTripCharge + "c is not " + plan.FreightChargeCents
                    + "c; refusing.";
            int maxPerTrip = FreightCargoCapacity.MaxUnitsPerTrip(plan.CargoSpec, plan.Wagon);
            int tripsNeeded = FreightCargoCapacity.TripsNeeded(sale.TotalUnits, plan.CargoSpec, plan.Wagon);
            if (tripsNeeded < 0 || tripsNeeded > trips)
                return "ConstructionMaterialFreightCoordinator: freight plan quotes " + trips
                    + " trip(s) but the load needs "
                    + (tripsNeeded < 0 ? "more than the wagon can ever carry" : tripsNeeded + " trip(s)")
                    + " at this wagon capacity — shipment blocked pre-load; goods stay with the supplier, nothing freighted.";

            List<ConstructionMaterialSale> tripSales = SplitSaleIntoTrips(sale, trips, maxPerTrip, out string splitRefusal);
            if (splitRefusal != null)
                return "ConstructionMaterialFreightCoordinator: " + splitRefusal;

            // Person-time exclusivity for the handling crew: the loader and
            // unloader are real people who cannot be double-booked while
            // they work the shipment. One whole-day reservation covers
            // every trip of this call (the trips run back to back today).
            if (!plan.LoaderId.IsValid || plan.LoaderId.Kind != EntityKind.Person)
                return "ConstructionMaterialFreightCoordinator: freight loading needs a real loader person — refusing phantom crew.";
            if (!plan.UnloaderId.IsValid || plan.UnloaderId.Kind != EntityKind.Person)
                return "ConstructionMaterialFreightCoordinator: freight unloading needs a real unloader person — refusing phantom crew.";
            var crewReservations = new List<string>();
            if (plan.Tracker != null)
            {
                string loaderRes = plan.Tracker.TryReserve(
                    plan.LoaderId.Id, PersonActivityKind.Work, dayIndex, 0, 1440,
                    $"freight load {sale.SaleId}");
                if (loaderRes == null) crewReservations.Add(LastReservationId(plan.Tracker, plan.LoaderId));
                string unloaderRes = plan.Tracker.TryReserve(
                    plan.UnloaderId.Id, PersonActivityKind.Work, dayIndex, 0, 1440,
                    $"freight unload {sale.SaleId}");
                if (unloaderRes == null) crewReservations.Add(LastReservationId(plan.Tracker, plan.UnloaderId));
                if (loaderRes != null || unloaderRes != null)
                {
                    foreach (string id in crewReservations) plan.Tracker.Release(id);
                    return "ConstructionMaterialFreightCoordinator: freight crew double-booked — " +
                        (loaderRes ?? unloaderRes);
                }
            }

            string failure = RunTrips(plan, sale, stockpile, tripSales, perTripCharge, trips, dayIndex, diag);
            if (plan.Tracker != null)
                foreach (string id in crewReservations) plan.Tracker.Release(id);
            if (failure != null) return failure;

            diag.Add($"ConstructionMaterialFreightCoordinator: sale '{sale.SaleId}' freighted to '{plan.SiteLabel}' " +
                $"({sale.TotalUnits} unit(s) conserved in {trips} trip(s), {LastFreightPaidCents}c freight) and received into the site stockpile.");
            return null;
        }

        /// <summary>
        /// DEF-016: slices one recorded sale into per-trip sub-sales,
        /// filling each trip to the wagon's per-trip capacity and putting
        /// the remainder on the last trip. Lines are walked in order and
        /// may split across trips; provenance is preserved with a trip
        /// suffix on each sliced line. A sale that cannot be sliced into
        /// exactly `trips` non-empty sub-sales is an inconsistent plan,
        /// refused — never silently re-priced. Returns the sub-sales, or
        /// an empty list with `refusal` set.
        /// </summary>
        private static List<ConstructionMaterialSale> SplitSaleIntoTrips(ConstructionMaterialSale sale,
            int trips, int maxUnitsPerTrip, out string refusal)
        {
            refusal = null;
            var result = new List<ConstructionMaterialSale>();
            if (sale == null || trips <= 0 || maxUnitsPerTrip <= 0)
            {
                refusal = "a sale, a positive trip count, and a positive per-trip capacity are required to split a load.";
                return result;
            }
            int remaining = sale.TotalUnits;
            int lineIndex = 0;
            int lineRemaining = 0;
            for (int tripIndex = 0; tripIndex < trips; tripIndex++)
            {
                int take = Math.Min(maxUnitsPerTrip, remaining);
                if (take <= 0)
                {
                    refusal = "freight plan quotes " + trips + " trip(s) for " + sale.TotalUnits
                        + " unit(s) — trip " + (tripIndex + 1) + " would carry nothing; the plan is inconsistent, refusing.";
                    return new List<ConstructionMaterialSale>();
                }
                var sub = new ConstructionMaterialSale
                {
                    SaleId = sale.SaleId + "/trip-" + (tripIndex + 1),
                    SupplierBusinessId = sale.SupplierBusinessId,
                    ProjectId = sale.ProjectId,
                    PackageId = sale.PackageId,
                    DayIndex = sale.DayIndex,
                };
                int needed = take;
                while (needed > 0 && lineIndex < sale.Lines.Count)
                {
                    ConstructionMaterialSaleLine line = sale.Lines[lineIndex];
                    if (line == null || line.Units <= 0) { lineIndex++; lineRemaining = 0; continue; }
                    if (lineRemaining <= 0) lineRemaining = line.Units;
                    int slice = Math.Min(needed, lineRemaining);
                    sub.Lines.Add(new ConstructionMaterialSaleLine
                    {
                        DisplayName = line.DisplayName,
                        Units = slice,
                        UnitLabel = line.UnitLabel,
                        UnitPriceCents = line.UnitPriceCents,
                        SupplierBusinessId = line.SupplierBusinessId,
                        Provenance = (line.Provenance ?? string.Empty) + $" (trip {tripIndex + 1} of {trips})",
                    });
                    needed -= slice;
                    lineRemaining -= slice;
                    if (lineRemaining <= 0) lineIndex++;
                }
                if (needed > 0)
                {
                    refusal = "sale '" + sale.SaleId + "' lines do not sum to its total — cannot split the load; refusing.";
                    return new List<ConstructionMaterialSale>();
                }
                result.Add(sub);
                remaining -= take;
            }
            if (remaining != 0)
            {
                refusal = "sale '" + sale.SaleId + "' did not split evenly across " + trips + " trip(s) — refusing.";
                return new List<ConstructionMaterialSale>();
            }
            return result;
        }

        /// <summary>
        /// DEF-070 (Decision 13): the shared failure exit for one trip.
        /// Freight is earned once the wagon is under way (InTransit): a haul
        /// that never departs (no shipment, Planned, Loading, or pre-load
        /// Failed) earns nothing, so its prepaid per-trip charge is refunded
        /// to the buyer through the real cash bridge and LastFreightPaidCents
        /// is decremented to the net paid. A haul that departed keeps its
        /// freight — the carrier loaded and hauled; any damages claim is a
        /// separate process. The shipment is always dispositioned terminally
        /// through the service (no-op when already Failed): a failed
        /// construction shipment has no driver to resume it, so leaving it
        /// live would hold its wagon, its team, and its cargo forever and
        /// brick the package on retry. A refund failure is named loudly —
        /// the money is then in dispute, not silently kept — and never blocks
        /// the disposition (the wagon release must not wait on the refund).
        /// </summary>
        private string FailTrip(ConstructionFreightPlan plan, ConstructionMaterialSale sale,
            LogisticsShipmentState shipment, int perTripCharge, string tripLabel,
            string failure, int deliveredUnits, int dayIndex,
            FreightShipmentHooks hooks, List<string> diag)
        {
            bool neverDeparted = shipment == null
                || shipment.Status == LogisticsShipmentStatus.Planned
                || shipment.Status == LogisticsShipmentStatus.Loading
                || shipment.Status == LogisticsShipmentStatus.Failed;
            string refundNote = string.Empty;
            if (neverDeparted && perTripCharge > 0)
            {
                string refundProblem = ConstructionCashMover.MoveCash(plan.Cash, plan.CarrierPayeeLabel,
                    plan.BuyerOwnerName, perTripCharge, dayIndex,
                    $"freight refund — sale '{sale.SaleId}' {tripLabel} never departed: {failure}", diag);
                if (refundProblem == null)
                {
                    LastFreightPaidCents -= perTripCharge;
                    refundNote = $" Freight refunded ({perTripCharge}c returned to '{plan.BuyerOwnerName}').";
                }
                else
                {
                    refundNote = $" Freight refund FAILED — {refundProblem} (carrier keeps {perTripCharge}c; in dispute).";
                }
            }
            string dispositionNote;
            if (shipment == null)
            {
                dispositionNote = " No shipment exists.";
            }
            else
            {
                plan.Freight.FailAbandonedShipment(shipment, failure, hooks);
                dispositionNote = shipment.loadApplied
                    ? $" {shipment.RemainingQuantityUnits}u quarantined on the failed shipment."
                    : " Goods stay with the supplier.";
            }
            return "ConstructionMaterialFreightCoordinator: " + tripLabel + " failed — " + failure
                + refundNote + dispositionNote
                + $" ({deliveredUnits} of {sale.TotalUnits} unit(s) already at the site).";
        }

        /// <summary>
        /// DEF-016: runs the plan's trips back to back. Each trip is
        /// priced and settled separately (its charge is paid before its
        /// wagon rolls), runs the real shipment chain, is conservation-
        /// verified against its own sub-sale, and is received into the
        /// stockpile immediately — so a later trip's failure leaves the
        /// earlier trips' goods at the site, never stranded in a
        /// completed shipment. Returns the failure, or null. The caller
        /// releases the crew reservations either way.
        /// </summary>
        private string RunTrips(ConstructionFreightPlan plan, ConstructionMaterialSale sale,
            ConstructionSiteStockpile stockpile, List<ConstructionMaterialSale> tripSales, int perTripCharge, int trips,
            int dayIndex, List<string> diag)
        {
            // QA-20: seed from the service's tracked shipments so a fresh
            // coordinator after load never re-issues a pre-save cship- id.
            SeedSequenceFromService(plan.Freight);
            int deliveredUnits = 0;
            for (int tripIndex = 0; tripIndex < tripSales.Count; tripIndex++)
            {
                ConstructionMaterialSale tripSale = tripSales[tripIndex];
                string tripLabel = "trip " + (tripIndex + 1) + " of " + trips;
                // The freight charge is real money to a real carrier, quoted
                // up front per trip — paid before that trip's wagon rolls.
                if (perTripCharge > 0)
                {
                    string payProblem = ConstructionCashMover.MoveCash(plan.Cash, plan.BuyerOwnerName,
                        plan.CarrierPayeeLabel, perTripCharge, dayIndex,
                        $"freight for sale '{sale.SaleId}' {tripLabel} to site '{plan.SiteLabel}'", diag);
                    if (payProblem != null)
                        return "ConstructionMaterialFreightCoordinator: freight charge for " + tripLabel + " unpaid — "
                            + payProblem + $" ({deliveredUnits} of {sale.TotalUnits} unit(s) already at the site).";
                    LastFreightPaidCents += perTripCharge;
                }
                string shipmentId = $"cship-{sequence++}";
                // Pass XI WS2: the site stockpile IS the receiving authority
                // for this leg. Without a ReceiveShipment callback the
                // shipment service honestly holds the goods in wagon custody
                // and the chain can never complete (the coordinator
                // previously built these hooks with no receiver, so every
                // site delivery stalled at unloading). Acceptance here is
                // site custody only; the stockpile bookkeeping follows after
                // conservation is verified, and staging consumes the
                // stockpile — no unit is counted twice. A shipment bound for
                // anywhere but this site is refused.
                var hooks = new FreightShipmentHooks
                {
                    Tasks = plan.Tasks,
                    Budgets = plan.Budgets,
                    DayIndex = dayIndex,
                    Diagnostics = diag,
                    // Slice A2 (QA-05): the coordinator already reserves
                    // whole-day crew windows for the loader and unloader
                    // before driving the chain (above), so the freight
                    // service must NOT reserve sub-windows inside them
                    // (TryReserve would refuse the overlap) and must not
                    // log the missing-calendar diagnostic. The crew's
                    // person-time is guarded by the coordinator's own
                    // windows, not by the service's reserve-first path.
                    CrewWindowsReservedByCaller = true,
                    ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string receiveReason) =>
                    {
                        receiveReason = string.Empty;
                        if (shipment == null || quantityUnits <= 0)
                        {
                            receiveReason = "no shipment or nothing to receive";
                            return 0;
                        }
                        if (!string.IsNullOrWhiteSpace(plan.SiteLabel)
                            && !string.Equals(shipment.destinationBusinessInstanceId, plan.SiteLabel, StringComparison.Ordinal))
                        {
                            receiveReason = $"shipment '{shipment.ShipmentId}' is bound for " +
                                $"'{shipment.destinationBusinessInstanceId}', not site '{plan.SiteLabel}'";
                            return 0;
                        }
                        diag?.Add($"ConstructionMaterialFreightCoordinator: site '{plan.SiteLabel}' accepted " +
                            $"{quantityUnits}u from shipment '{shipment.ShipmentId}' into site custody.");
                        return quantityUnits;
                    },
                };
                plan.Freight.RegisterWagon(plan.Wagon);
                string reason;
                LogisticsShipmentState shipment = plan.Freight.CreateShipment(
                    shipmentId, sale.SupplierBusinessId, plan.SiteLabel, tripSale.TotalUnits,
                    plan.CargoSpec ?? new FreightCargoSpec(), plan.Wagon.AssetId, hooks, out reason);
                if (shipment == null)
                    return FailTrip(plan, sale, null, perTripCharge, tripLabel,
                        "shipment not created — " + reason, deliveredUnits, dayIndex, hooks, diag);
                if (shipment.Status == LogisticsShipmentStatus.Failed)
                    return FailTrip(plan, sale, shipment, perTripCharge, tripLabel,
                        $"shipment '{shipmentId}' blocked pre-load ({shipment.BlockedReason})",
                        deliveredUnits, dayIndex, hooks, diag);

                string failure = RunShipmentChain(plan, shipment, hooks, dayIndex, diag);
                if (failure != null)
                    return FailTrip(plan, sale, shipment, perTripCharge, tripLabel,
                        failure, deliveredUnits, dayIndex, hooks, diag);

                string verifyReason;
                if (!plan.Freight.VerifyConservation(shipment, tripSale.TotalUnits, out verifyReason))
                    return $"ConstructionMaterialFreightCoordinator: conservation FAILED for '{shipmentId}' ({tripLabel}) — {verifyReason}. Goods do not reach the stockpile.";

                string receiveProblem = stockpile.Receive(tripSale, diag);
                if (receiveProblem != null)
                    return "ConstructionMaterialFreightCoordinator: " + tripLabel + " receipt failed — " + receiveProblem
                        + $" ({deliveredUnits} of {sale.TotalUnits} unit(s) already at the site).";
                deliveredUnits += tripSale.TotalUnits;
                diag.Add($"ConstructionMaterialFreightCoordinator: {tripLabel} of sale '{sale.SaleId}' " +
                    $"({tripSale.TotalUnits} unit(s)) arrived at '{plan.SiteLabel}'.");
            }
            return null;
        }

        private string RunShipmentChain(ConstructionFreightPlan plan, LogisticsShipmentState shipment,
            FreightShipmentHooks hooks, int dayIndex, List<string> diag)
        {
            string reason;
            if (!plan.Freight.BeginLoading(shipment, plan.LoaderId, hooks, out reason))
                return "ConstructionMaterialFreightCoordinator: loading refused — " + reason;
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 100)
            {
                if (!plan.Freight.WorkLoading(shipment, 60, hooks, out reason))
                    return "ConstructionMaterialFreightCoordinator: loading failed — " + reason;
            }
            if (!shipment.loadApplied)
                return $"ConstructionMaterialFreightCoordinator: loading never completed for '{shipment.ShipmentId}' — refusing.";

            if (!plan.Freight.Dispatch(shipment, plan.DriverId, plan.DraftAnimalIds, hooks, out reason))
                return "ConstructionMaterialFreightCoordinator: dispatch refused — " + reason;
            plan.Freight.AdvanceTransit(shipment, Math.Max(1f, plan.TransitGameSeconds), hooks);
            if (shipment.Status != LogisticsShipmentStatus.Arrived)
                return $"ConstructionMaterialFreightCoordinator: shipment '{shipment.ShipmentId}' is {shipment.Status} after transit — not arrived, refusing.";

            if (!plan.Freight.BeginUnloading(shipment, plan.UnloaderId, hooks, out reason))
                return "ConstructionMaterialFreightCoordinator: unloading refused — " + reason;
            guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 100)
            {
                if (!plan.Freight.WorkUnloading(shipment, 60, hooks, out reason))
                    return "ConstructionMaterialFreightCoordinator: unloading failed — " + reason;
            }
            if (shipment.Status != LogisticsShipmentStatus.Completed)
                return $"ConstructionMaterialFreightCoordinator: unloading never completed for '{shipment.ShipmentId}' — refusing.";
            return null;
        }

        private static string LastReservationId(PersonScheduleTracker tracker, EntityId person)
        {
            if (tracker == null || !person.IsValid) return string.Empty;
            string last = string.Empty;
            foreach (PersonActivityReservation reservation in tracker.Reservations)
            {
                if (reservation != null && !reservation.Released
                    && reservation.PersonId == person.Id)
                    last = reservation.ReservationId;
            }
            return last;
        }
    }

    /// <summary>
    /// Pass VIII Worker 3: the procurement leg — supplier discovery,
    /// real-lot sale, real-cash payment, real-chain freight, site staging.
    /// The commissioner (the NPC principal's decisions and books) calls
    /// this; the service owns the physical chain and refuses loudly at any
    /// broken link.
    /// </summary>
    public sealed class ConstructionProcurementService
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Maps a material kind to the supplier-directory category key used
        /// for discovery. Append-only.
        /// </summary>
        public static string CategoryKeyFor(ConstructionMaterialKind kind)
        {
            switch (kind)
            {
                case ConstructionMaterialKind.Lumber: return "construction-lumber";
                case ConstructionMaterialKind.Hardware: return "construction-hardware";
                case ConstructionMaterialKind.Masonry: return "construction-masonry";
                case ConstructionMaterialKind.Paint: return "construction-paint";
                case ConstructionMaterialKind.Glass: return "construction-glass";
                case ConstructionMaterialKind.Roofing: return "construction-roofing";
                default: return "construction-materials";
            }
        }

        /// <summary>
        /// Discovers the real supplier for one requirement: a named supplier
        /// must actually exist in the directory (no phantom suppliers); an
        /// unnamed requirement discovers one by category (no invented
        /// suppliers). Returns null + refusal when no real supplier exists.
        /// </summary>
        public string DiscoverSupplier(
            ConstructionMaterialRequirement requirement,
            ISupplierDirectory directory,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (requirement == null || !requirement.IsCoherent)
                return null;
            if (directory == null)
            {
                diag.Add("ConstructionProcurementService: no supplier directory — suppliers are discovered, never invented.");
                return null;
            }
            string category = CategoryKeyFor(requirement.MaterialKind);
            if (!string.IsNullOrWhiteSpace(requirement.SupplierBusinessId))
            {
                foreach (IGoodsSupplier supplier in directory.SuppliersFor(category))
                {
                    if (supplier != null
                        && string.Equals(supplier.SupplierBusinessId, requirement.SupplierBusinessId, StringComparison.Ordinal))
                    {
                        diag.Add($"ConstructionProcurementService: requirement '{requirement.MaterialDisplayName}' names supplier '{requirement.SupplierBusinessId}' — verified as a real, discoverable supplier.");
                        return requirement.SupplierBusinessId;
                    }
                }
                diag.Add($"ConstructionProcurementService: named supplier '{requirement.SupplierBusinessId}' is not a real, discoverable supplier for '{category}' — refusing phantom suppliers.");
                return null;
            }
            foreach (IGoodsSupplier supplier in directory.SuppliersFor(category))
            {
                if (supplier != null)
                {
                    diag.Add($"ConstructionProcurementService: discovered supplier for '{requirement.MaterialDisplayName}' ({category}).");
                    requirement.SupplierBusinessId = supplier.SupplierBusinessId;
                    return supplier.SupplierBusinessId;
                }
            }
            diag.Add($"ConstructionProcurementService: no real supplier found for '{requirement.MaterialDisplayName}' ({category}) — refusing.");
            return null;
        }

        /// <summary>
        /// Runs procurement for one package: discovers a supplier per
        /// requirement, buys real lots through the supplier sale authority,
        /// pays real cash, freights through the real shipment chain into the
        /// site stockpile, then stages from the stockpile. Any broken link
        /// refuses with the project exactly where it was.
        /// </summary>
        public string ProcureFreightAndStage(
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            WorkPackage package,
            IConstructionMaterialSalePort salePort,
            IConstructionMaterialPricePort pricePort,
            ISupplierDirectory suppliers,
            ConstructionMaterialFreightCoordinator freightCoordinator,
            ConstructionFreightPlan freightPlan,
            ConstructionSiteStockpile stockpile,
            CreditCashBridge cash,
            string buyerOwnerName,
            FinancialObligationAuthority obligations,
            EntityIdRegistry ids,
            int pricedTotalCents,
            int dayIndex,
            List<string> diag,
            out ConstructionMaterialSale sale)
        {
            diag = diag ?? diagnostics;
            sale = null;
            if (executor == null || project == null || package == null)
                return "ConstructionProcurementService: executor, project and package required.";
            if (salePort == null)
                return "ConstructionProcurementService: no supplier sale port — materials are bought from real suppliers, never conjured.";
            if (freightCoordinator == null || freightPlan == null || stockpile == null)
                return "ConstructionProcurementService: freight coordinator, plan and site stockpile required — no teleported goods.";

            // One supplier per package leg: discover for every requirement first —
            // a package with any undiscoverable material is refused whole.
            var supplierByRequirement = new Dictionary<ConstructionMaterialRequirement, string>();
            foreach (ConstructionMaterialRequirement requirement in package.Materials ?? new List<ConstructionMaterialRequirement>())
            {
                if (requirement == null || !requirement.IsCoherent)
                    return $"ConstructionProcurementService: package '{package.PackageId}' has an incoherent material requirement — refusing.";
                string supplierId = DiscoverSupplier(requirement, suppliers, diag);
                if (string.IsNullOrWhiteSpace(supplierId))
                    return $"ConstructionProcurementService: no real supplier for '{requirement.MaterialDisplayName}' — package '{package.PackageId}' refused.";
                supplierByRequirement[requirement] = supplierId;
            }
            var distinctSuppliers = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in supplierByRequirement.Values) distinctSuppliers.Add(id);
            if (distinctSuppliers.Count != 1)
                return $"ConstructionProcurementService: package '{package.PackageId}' spans {distinctSuppliers.Count} suppliers — one procurement leg per package, refusing.";
            string supplierIdOnly = null;
            foreach (string id in distinctSuppliers) supplierIdOnly = id;

            string saleProblem = salePort.TrySellToProject(supplierIdOnly, project.ProjectId,
                package.PackageId, package.Materials, pricePort, dayIndex, diag, out sale);
            if (saleProblem != null) return "ConstructionProcurementService: " + saleProblem;
            if (sale == null)
                return "ConstructionProcurementService: sale port reported success but returned no sale — refusing.";
            if (sale.TotalCents != Math.Max(0, pricedTotalCents))
                return $"ConstructionProcurementService: sale '{sale.SaleId}' totals {sale.TotalCents}c against the priced {pricedTotalCents}c — the supplier's price moved or the port mis-priced; refusing the silent change.";

            // Pay the supplier in real cash. On payment failure the debt is a
            // REAL payable — never vanished, never faked as paid.
            string payProblem = ConstructionCashMover.MoveCash(cash, buyerOwnerName,
                "business:" + supplierIdOnly, sale.TotalCents, dayIndex,
                $"procured materials for project '{project.ProjectId}' package '{package.PackageId}' (sale '{sale.SaleId}')", diag);
            if (payProblem != null)
            {
                if (obligations != null && ids != null)
                {
                    FinancialObligation payable = obligations.Create(ids, FinancialObligationKind.Payable,
                        buyerOwnerName, "business:" + supplierIdOnly, sale.TotalCents, dayIndex,
                        $"materials procured but cash payment failed: {payProblem}",
                        $"sale '{sale.SaleId}'");
                    diag.Add($"ConstructionProcurementService: {sale.TotalCents}c to 'business:{supplierIdOnly}' booked as REAL payable " +
                        $"'{(payable != null ? payable.ObligationId : "FAILED")}' — the debt stands.");
                }
                else
                {
                    diag.Add($"ConstructionProcurementService: INCONSISTENCY — {sale.TotalCents}c of materials procured but payment failed and no obligation authority to book the debt: {payProblem}");
                    return "ConstructionProcurementService: payment failed and no obligation authority to book the debt — failing loudly.";
                }
            }

            string freightProblem = freightCoordinator.FreightSaleToSite(sale, freightPlan, stockpile, dayIndex, diag);
            if (freightProblem != null) return freightProblem;

            string stageProblem = executor.StagePackageMaterialsFromStockpile(project, package, stockpile, dayIndex, diag);
            if (stageProblem != null) return stageProblem;

            diag.Add($"ConstructionProcurementService: package '{package.PackageId}' procured, freighted and staged — " +
                $"sale '{sale.SaleId}' ({sale.TotalCents}c), {sale.TotalUnits} unit(s) conserved to site.");
            return null;
        }
    }
}
