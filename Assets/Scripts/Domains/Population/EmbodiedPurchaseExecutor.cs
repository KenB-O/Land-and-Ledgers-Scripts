using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// T1A: a real supplier of trade goods for embodied purchasing. Finite stock —
    /// a supplier cannot sell what it does not hold (Canon §9.2). LocationId is a
    /// JRN-1 journey location so the executor can route a real trip to the seller.
    /// </summary>
    public interface IGoodsSupplier
    {
        string SupplierBusinessId { get; }
        string SupplierName { get; }
        string LocationId { get; }
        bool HasCategory(string categoryId);
        int StockUnits(string categoryId);
        int PricePerUnitCents(string categoryId);
        /// <summary>Sells up to requestedUnits; returns units actually sold (0 = none available).</summary>
        int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// T1A: directory of goods suppliers, keyed by trade-good category. The general
    /// store "can buy/sell whatever" — it registers whatever categories it stocks.
    /// </summary>
    public interface ISupplierDirectory
    {
        IEnumerable<IGoodsSupplier> SuppliersFor(string categoryId);
    }

    /// <summary>T1A: simple in-memory supplier directory (scenario/scene wiring registers suppliers).</summary>
    public sealed class SupplierDirectory : ISupplierDirectory
    {
        private readonly List<IGoodsSupplier> suppliers = new List<IGoodsSupplier>();

        public void Register(IGoodsSupplier supplier)
        {
            if (supplier != null) suppliers.Add(supplier);
        }

        /// <summary>Phase C: all registered suppliers (for legitimate-knowledge filtering).</summary>
        public IReadOnlyList<IGoodsSupplier> All => suppliers;

        public IEnumerable<IGoodsSupplier> SuppliersFor(string categoryId)
        {
            foreach (var supplier in suppliers)
            {
                if (supplier != null && supplier.HasCategory(categoryId))
                    yield return supplier;
            }
        }
    }

    /// <summary>
    /// T1A: the REAL embodied-purchase executor replacing the HF-5
    /// ScriptedPurchaseExecutor test double. The full chain per Canon 13.4:
    /// ProcurementNeed (HF-4 planner) -> acting Person (must exist) -> Journey
    /// (JRN-1 route, real miles, TTS-1 minute quantum) -> supplier availability
    /// (finite stock, named counterparty) -> Transaction (household ledger
    /// outflow with provenance) -> possession (household reserves credited).
    ///
    /// No anonymous buyers (Rev XXIII doctrine): a purchase without a real
    /// acting Person is rejected. No sale is faked: no supplier with stock, no
    /// route to the supplier, insufficient household funds, or insufficient
    /// travel time all fail WITHOUT posting revenue.
    ///
    /// Supplier choice is nearest-by-travel-minutes among stocked suppliers.
    /// Price-aware choice belongs to the T1C Customer Choice Resolver; this
    /// executor documents the selection rule it uses.
    /// </summary>
    public sealed class EmbodiedPurchaseExecutor : IEmbodiedPurchaseExecutor
    {
        private readonly PopulationState population;
        private readonly HouseholdLedgerRegistry ledgers;
        private readonly ISupplierDirectory suppliers;
        private readonly JourneyModel journeys;
        private readonly Func<int, string> personLocationId;
        private readonly WorkTimeBudgetStore budgets;
        private readonly HouseholdInventoryRegistry inventories;
        private readonly Func<int, EntityId> personEntityId;
        private readonly List<string> log = new List<string>();

        /// <param name="personLocationId">Resolves a person's journey location id; defaults to town center.</param>
        /// <param name="budgets">Optional: travel minutes are committed against the person's TTS-1 budget.</param>
        /// <param name="personEntityId">Optional: maps legacy person int id to HF-1 EntityId for budget lookup.</param>
        /// <param name="inventories">Optional (Phase B): purchased goods also book real lots via the reserve bridge.</param>
        public EmbodiedPurchaseExecutor(
            PopulationState population,
            HouseholdLedgerRegistry ledgers,
            ISupplierDirectory suppliers,
            JourneyModel journeys,
            Func<int, string> personLocationId = null,
            WorkTimeBudgetStore budgets = null,
            Func<int, EntityId> personEntityId = null,
            HouseholdInventoryRegistry inventories = null)
        {
            this.population = population ?? throw new ArgumentNullException(nameof(population));
            this.ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
            this.suppliers = suppliers ?? throw new ArgumentNullException(nameof(suppliers));
            this.journeys = journeys ?? throw new ArgumentNullException(nameof(journeys));
            this.personLocationId = personLocationId ?? (pid => "town-center");
            this.budgets = budgets;
            this.personEntityId = personEntityId;
            this.inventories = inventories;
        }

        public IReadOnlyList<string> Log => log;

        public PurchaseExecutionResult Execute(ProcurementNeed need, int actingPersonId, int dayIndex)
        {
            var result = new PurchaseExecutionResult
            {
                NeedSequence = need != null ? need.NeedSequence : -1,
                ActingPersonId = actingPersonId,
            };

            if (need == null || actingPersonId < 0)
            {
                result.Notes = "Rejected: need or acting person missing. Only an acting Person transacts (Canon 13.4).";
                log.Add(result.Notes);
                return result;
            }

            // The acting Person must be real — no anonymous buyers (Rev XXIII doctrine).
            PersonState person = population.GetPerson(actingPersonId);
            if (person == null)
            {
                result.Notes = $"Rejected: P{actingPersonId} is not a known person. Only a real acting Person transacts (Canon 13.4).";
                log.Add(result.Notes);
                return result;
            }

            if (need.UnitsNeeded <= 0)
            {
                result.Notes = $"Need {need.NeedSequence}: no units needed — nothing to buy.";
                log.Add(result.Notes);
                return result;
            }

            // Supplier availability: finite stock only.
            IGoodsSupplier chosen = null;
            JourneyRoute chosenRoute = null;
            string originId = personLocationId(actingPersonId);
            foreach (IGoodsSupplier supplier in suppliers.SuppliersFor(need.CategoryId))
            {
                if (supplier.StockUnits(need.CategoryId) <= 0) continue;
                JourneyRoute route = journeys.FindRoute(originId, supplier.LocationId, TravelMode.Foot);
                if (!route.Found)
                {
                    log.Add($"Day {dayIndex}: no road from '{originId}' to '{supplier.SupplierName}' — {route.Diagnostic}");
                    continue;
                }
                if (chosenRoute == null || route.TotalMinutes < chosenRoute.TotalMinutes)
                {
                    chosen = supplier;
                    chosenRoute = route;
                }
            }

            if (chosen == null)
            {
                result.Notes = $"Day {dayIndex}: P{actingPersonId} could not source '{need.CategoryId}' — no stocked supplier reachable. No sale faked.";
                log.Add(result.Notes);
                return result;
            }

            int units = Math.Min(need.UnitsNeeded, chosen.StockUnits(need.CategoryId));
            int unitPrice = Math.Max(0, chosen.PricePerUnitCents(need.CategoryId));
            int cost = units * unitPrice;
            // A shopping trip is out-and-back: the person walks both ways.
            int roundTripMinutes = Math.Max(1, chosenRoute.TotalMinutes * 2);

            if (cost <= 0)
            {
                result.Notes = $"Rejected: '{need.CategoryId}' at '{chosen.SupplierName}' has no legitimate price — free goods are not commerce.";
                log.Add(result.Notes);
                return result;
            }

            // Travel time comes out of the person's day when budgets are wired.
            if (budgets != null && personEntityId != null)
            {
                EntityId personEid = personEntityId(actingPersonId);
                if (personEid.IsValid && personEid.Kind == EntityKind.Person)
                {
                    if (!budgets.TryCommitTask(personEid, roundTripMinutes, out string timeRejection))
                    {
                        result.Notes = $"Day {dayIndex}: P{actingPersonId} has no time left for the {roundTripMinutes}-minute trip to '{chosen.SupplierName}': {timeRejection}. Need unmet, no sale faked.";
                        log.Add(result.Notes);
                        return result;
                    }
                }
            }

            // Ledger first: if the household cannot pay, nothing moves (atomic, FRM-1 pattern).
            HouseholdLedger ledger = ledgers.GetOrCreate(need.HouseholdId);
            string rejection = ledger.RecordOutflow(
                dayIndex, cost,
                $"embodied purchase: {units}u {need.CategoryId} ({roundTripMinutes} min round trip)",
                chosen.SupplierName);
            if (rejection != null)
            {
                result.Notes = $"Purchase failed ledger validation: {rejection}";
                log.Add(result.Notes);
                return result;
            }

            int sold = chosen.Sell(need.CategoryId, units, dayIndex, log);
            if (sold <= 0)
            {
                // Stock moved between selection and sale — loud, never silent. (Single-threaded
                // simulation makes this unreachable in practice; the guard is structural.)
                result.Notes = $"INCONSISTENCY: ledger paid {cost}c but '{chosen.SupplierName}' sold 0 units — manual reconciliation required.";
                log.Add(result.Notes);
                return result;
            }

            // Possession: the goods enter the household's reserves.
            HouseholdState household = population.GetHousehold(need.HouseholdId);
            if (household != null)
            {
                bool credited = false;
                if (household.reserves != null)
                {
                    foreach (var reserve in household.reserves)
                    {
                        if (reserve != null && string.Equals(reserve.categoryId, need.CategoryId, StringComparison.OrdinalIgnoreCase))
                        {
                            reserve.currentUnits += sold;
                            credited = true;
                            break;
                        }
                    }
                }
                if (!credited)
                {
                    log.Add($"Day {dayIndex}: no reserve tracked for '{need.CategoryId}' — {sold} units held untracked by H{need.HouseholdId}.");
                }

                // Phase B: purchased goods also book real lots (the lot
                // inventory is the consumption truth).
                // Phase C: this legacy HF-4 path still uses the transitional
                // bridge until DailyNeedsService migrates to HouseholdShoppingLoop.
#pragma warning disable 618
                if (inventories != null && sold > 0)
                {
                    HouseholdInventoryReserveBridge.MirrorReserveCreditToLots(
                        inventories.GetOrCreate(household.id),
                        need.CategoryId,
                        sold,
                        dayIndex,
                        $"embodied purchase from {chosen.SupplierName}",
                        log);
                }
#pragma warning restore 618
            }

            result.Success = true;
            result.UnitsAcquired = sold;
            result.AmountPaidCents = sold * unitPrice;
            result.Counterparty = chosen.SupplierName;
            result.Notes = $"Day {dayIndex}: P{actingPersonId} walked {chosenRoute.TotalMiles:F1} mi ({roundTripMinutes} min round trip) to '{chosen.SupplierName}' and bought {sold}u {need.CategoryId} for {result.AmountPaidCents}c.";
            log.Add(result.Notes);
            return result;
        }
    }
}
