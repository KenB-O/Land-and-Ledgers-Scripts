using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Economy.Businesses.Carpentry
{
    /// <summary>Caller inputs for one carpentry supplies purchase (one item).</summary>
    public sealed class CarpentrySuppliesPurchaseInputs
    {
        public BusinessRuntimeState StoreRuntime;
        public BusinessInstanceState Workshop;
        public IBusinessIdentityCashPort WorkshopCashPort;
        public IBusinessIdentityCashPort StoreCashPort;
        public string WorkshopBusinessId = string.Empty;
        public string StoreBusinessId = string.Empty;
        public string ItemId = string.Empty;
        public int UnitPriceCents;
        public int UnitsRequested;
        public int DayIndex;
        public string TransactionId = string.Empty;
    }

    /// <summary>Outcome of one carpentry supplies purchase (one item).</summary>
    public sealed class CarpentrySuppliesPurchaseResult
    {
        public bool Succeeded;
        public bool Duplicate;
        public string Refusal = string.Empty;
        public int UnitsPurchased;
        public int TotalCostCents;
        public List<string> SourceLotIds = new List<string>();
    }

    /// <summary>
    /// G-1 (expanded campaign 2026-11-16, W-G4 Workstream 1): the
    /// carpentry workshop buys nails and cloth from the General Store's
    /// identified retail lots at the store's LIVE selling price, for its
    /// generic inventory (the furniture recipes consume them from
    /// there). This is the BakeryFlourLocalPurchase / SawmillLubrication
    /// Purchase shape — the production-proven business-buyer store
    /// purchase pattern (the bakery and the mill both buy this way in
    /// production) — NOT GenericRetailSaleAuthority, which is the
    /// seller-side authority for a business selling its own stock and
    /// does not prove a purchase from another business's retail lots.
    /// Price never hardcoded: the production caller reads
    /// GetItemSellingPriceCents(itemId) from the store manager; a zero
    /// price (store definition missing, item unknown) refuses. Supplier
    /// named: General Store. Freight is NOT built here — the store is
    /// in-town and the workshop collects, like the lumber procurement.
    ///
    /// Items (both stocked by the store, per the store definition):
    /// nails_hardware_lot (tools_hardware, unit "lot", 25c baseline,
    /// 2-6 lots opening stock) and basic_cloth (clothing, unit "yard",
    /// 12c baseline, 3-6 yards opening stock), per the development-town
    /// configuration (nails_hardware_lot minimum 2 / maximum 6,
    /// basic_cloth minimum 3 / maximum 6). The recipes count one nails
    /// lot per run and cloth by the yard; the generic inventory counts
    /// units, so one lot is one unit and one yard is one unit. No
    /// price, supplier, or good is invented here.
    ///
    /// Order: validate + prove lots, idempotency check (the transaction
    /// id is the shipment id, and the workshop's received-shipment list
    /// is the applied-transaction record), debit workshop cash, consume
    /// store lots, credit the store once via
    /// RecordRetailItemSettlement, receive into the workshop's generic
    /// inventory. A refused receipt rolls the cash back and says loudly
    /// that the consumed lots are consumed-but-unpaid.
    /// </summary>
    public static class CarpentrySuppliesPurchase
    {
        public const string NailsItemId = "nails_hardware_lot";
        public const string NailsCategoryId = "tools_hardware";
        public const string NailsUnitLabel = "lot";

        public const string ClothItemId = "basic_cloth";
        public const string ClothCategoryId = "clothing";
        public const string ClothUnitLabel = "yard";

        /// <summary>
        /// G-1: the supplies stock targets — PROPOSED DEFAULTS for the
        /// owner to correct, not measured values. One day's run needs at
        /// most 1 nails lot and 4 cloth yards (the bed); 2 lots and 8
        /// yards cover about two days of any furniture, so the workshop
        /// is never blocked on a single day's store trip while never
        /// hoarding the store's stock. The driver buys only the
        /// shortfall to these targets, once a day.
        /// </summary>
        public const int NailsTargetLots = 2;
        public const int ClothTargetYards = 8;

        public static string BuildTransactionId(string itemId, string workshopBusinessId, int dayIndex) =>
            $"carpentry-supplies-{itemId}:{workshopBusinessId}:{dayIndex}";

        /// <summary>The store's category and unit label for a supplies item, or false when the item is not a carpentry supply (never guessed).</summary>
        public static bool TryGetItemTable(string itemId, out string categoryId, out string unitLabel)
        {
            categoryId = string.Empty;
            unitLabel = string.Empty;
            if (string.Equals(itemId, NailsItemId, StringComparison.OrdinalIgnoreCase))
            {
                categoryId = NailsCategoryId;
                unitLabel = NailsUnitLabel;
                return true;
            }
            if (string.Equals(itemId, ClothItemId, StringComparison.OrdinalIgnoreCase))
            {
                categoryId = ClothCategoryId;
                unitLabel = ClothUnitLabel;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The generic product definition for a supplies item as the
        /// workshop holds it. Cloth counts by the yard (the unit exists);
        /// nails have no "lot" unit in the generic enum, so one lot is
        /// one countable unit (Each) — the recipes consume one lot per
        /// run. Lot identity is required so the nails/cloth a piece of
        /// furniture is made from keep their lot provenance into the
        /// finished EquipmentAsset.
        /// </summary>
        public static GenericProductDefinition DefinitionForItem(string itemId)
        {
            if (string.Equals(itemId, NailsItemId, StringComparison.OrdinalIgnoreCase))
                return new GenericProductDefinition(NailsItemId, "Nails / Hardware Lot", ProductQuantityUnit.Each,
                    requiresLotIdentity: true);
            if (string.Equals(itemId, ClothItemId, StringComparison.OrdinalIgnoreCase))
                return new GenericProductDefinition(ClothItemId, "Basic Cloth", ProductQuantityUnit.Yard,
                    requiresLotIdentity: true);
            return null;
        }

        public static CarpentrySuppliesPurchaseResult PurchaseUnits(
            CarpentrySuppliesPurchaseInputs inputs, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new CarpentrySuppliesPurchaseResult();

            if (inputs == null || inputs.StoreRuntime == null || inputs.Workshop == null
                || inputs.Workshop.GenericConfiguration == null
                || inputs.WorkshopCashPort == null || inputs.StoreCashPort == null)
            {
                result.Refusal = "CarpentrySuppliesPurchase: store runtime, workshop, workshop cash port, and store cash port are required.";
                diag.Add(result.Refusal);
                return result;
            }
            if (!TryGetItemTable(inputs.ItemId, out string categoryId, out string unitLabel))
            {
                result.Refusal = $"CarpentrySuppliesPurchase: '{inputs.ItemId}' is not a carpentry supply (nails_hardware_lot, basic_cloth) — the workshop buys nothing else from the store through this leg.";
                diag.Add(result.Refusal);
                return result;
            }
            if (inputs.UnitsRequested <= 0)
            {
                result.Refusal = "CarpentrySuppliesPurchase: a positive unit count is required — whole units only, never a partial unit.";
                diag.Add(result.Refusal);
                return result;
            }
            if (inputs.UnitPriceCents <= 0)
            {
                result.Refusal = $"CarpentrySuppliesPurchase: a positive unit price is required for '{inputs.ItemId}' — a zero or negative price refuses, no free supplies.";
                diag.Add(result.Refusal);
                return result;
            }
            if (string.IsNullOrWhiteSpace(inputs.TransactionId))
            {
                result.Refusal = "CarpentrySuppliesPurchase: a deterministic transaction id is required.";
                diag.Add(result.Refusal);
                return result;
            }
            string storeBusinessId = inputs.StoreRuntime.BusinessId;
            string workshopBusinessId = string.IsNullOrWhiteSpace(inputs.WorkshopBusinessId)
                ? inputs.Workshop.InstanceId : inputs.WorkshopBusinessId;
            if (string.IsNullOrWhiteSpace(storeBusinessId) || string.IsNullOrWhiteSpace(workshopBusinessId)
                || !string.Equals(inputs.WorkshopCashPort.BusinessId, workshopBusinessId, StringComparison.Ordinal)
                || !string.Equals(inputs.StoreCashPort.BusinessId, storeBusinessId, StringComparison.Ordinal)
                || string.Equals(storeBusinessId, workshopBusinessId, StringComparison.Ordinal))
            {
                result.Refusal = "CarpentrySuppliesPurchase: seller, buyer, and cash-port identities do not match distinct businesses.";
                diag.Add(result.Refusal);
                return result;
            }

            // (a) Idempotency FIRST, before the lot proof: the transaction
            // id is the shipment id, and the workshop's received-shipment
            // list is the applied-transaction record. A retry buys nothing
            // twice — the proof below would fail on a genuine retry, the
            // first purchase having consumed the lots it proved.
            if (HasReceivedShipment(inputs.Workshop, inputs.TransactionId))
            {
                result.Succeeded = true;
                result.Duplicate = true;
                diag.Add($"CarpentrySuppliesPurchase: transaction '{inputs.TransactionId}' already recorded — duplicate, 0 new units, cash moved once.");
                return result;
            }

            // (b) Prove the units from identified retail lots before anything moves.
            var sourceLotIds = new List<string>();
            var sourceProvenance = new List<string>();
            int provable = 0;
            foreach (var lot in inputs.StoreRuntime.RetailItemLots)
            {
                if (provable >= inputs.UnitsRequested) break;
                if (lot == null
                    || !string.Equals(lot.ItemId, inputs.ItemId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.UnitLabel, unitLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.OwnerBusinessId, storeBusinessId, StringComparison.Ordinal)
                    || !string.Equals(lot.CustodianBusinessId, storeBusinessId, StringComparison.Ordinal)) continue;
                int take = Math.Min(inputs.UnitsRequested - provable, lot.AvailableUnits);
                if (take <= 0) continue;
                sourceLotIds.Add(lot.LotId);
                sourceProvenance.Add(lot.Provenance);
                provable += take;
            }
            if (provable < inputs.UnitsRequested)
            {
                result.Refusal = $"CarpentrySuppliesPurchase: store retail lots prove {provable} '{unitLabel}' unit(s) of '{inputs.ItemId}', {inputs.UnitsRequested} requested — shortfall {inputs.UnitsRequested - provable} unit(s); nothing moved.";
                diag.Add(result.Refusal);
                return result;
            }

            // (c) Debit workshop cash, then consume store lots; either failure refunds / moves nothing.
            int totalCents = checked(inputs.UnitsRequested * inputs.UnitPriceCents);
            string debitRefusal = inputs.WorkshopCashPort.TryDebit(totalCents,
                $"{inputs.ItemId} purchase {inputs.UnitsRequested} unit(s) from {storeBusinessId} ({inputs.TransactionId})");
            if (debitRefusal != null)
            {
                result.Refusal = $"CarpentrySuppliesPurchase: workshop payment refused: {debitRefusal}; nothing moved.";
                diag.Add(result.Refusal);
                return result;
            }
            if (!inputs.StoreRuntime.TryConsumeRetailItemStockUnits(
                    inputs.ItemId, categoryId, inputs.UnitsRequested, out int consumed, retailSale: true)
                || consumed != inputs.UnitsRequested)
            {
                string refundRefusal = inputs.WorkshopCashPort.Credit(totalCents,
                    $"refund refused supplies purchase {inputs.TransactionId}");
                result.Refusal = $"CarpentrySuppliesPurchase: store stock changed before checkout ({consumed}/{inputs.UnitsRequested} consumed). " +
                    (refundRefusal == null ? "Workshop debit reversed." : $"Workshop refund failed: {refundRefusal}");
                diag.Add(result.Refusal);
                return result;
            }

            // Store cash, once, through the single cash writer — before the
            // receipt, so a receipt refusal can reverse it (FeedToBin shape).
            inputs.StoreRuntime.RecordRetailItemSettlement(categoryId, inputs.UnitsRequested, totalCents);

            // (d) Receive the units into the workshop's generic inventory.
            GenericProductDefinition definition = DefinitionForItem(inputs.ItemId);
            string provenance = $"carpentry supplies purchase {inputs.TransactionId} from General Store {storeBusinessId} (lots {string.Join(", ", sourceLotIds)}; {string.Join("; ", sourceProvenance)})";
            if (!inputs.Workshop.GenericConfiguration.TryReceiveInventoryShipment(
                    inputs.TransactionId, inputs.ItemId, inputs.UnitsRequested, inputs.UnitPriceCents,
                    definition, provenance, inputs.DayIndex))
            {
                // Rollback (flour shape): reverse the store credit through
                // the store cash port, credit the workshop back, and say
                // loudly that the consumed lots are consumed-but-unpaid —
                // consumption cannot be undone through the public lot API.
                // This should never fire: step (a) already proved the
                // transaction id is unrecorded.
                string storeRefundRefusal = inputs.StoreCashPort.TryDebit(totalCents,
                    $"rollback: receipt refused supplies purchase {inputs.TransactionId}");
                if (storeRefundRefusal != null)
                    diag.Add($"CarpentrySuppliesPurchase ROLLBACK WARNING: store cash could not return the supplies price ({storeRefundRefusal}); cash and stock now disagree and must be reconciled manually.");
                string workshopRefundRefusal = inputs.WorkshopCashPort.Credit(totalCents,
                    $"rollback: receipt refused supplies purchase {inputs.TransactionId}");
                if (workshopRefundRefusal != null)
                    diag.Add($"CarpentrySuppliesPurchase ROLLBACK WARNING: workshop cash could not be credited back ({workshopRefundRefusal}).");
                result.Refusal = $"CarpentrySuppliesPurchase: workshop inventory refused the {inputs.UnitsRequested} unit(s) of '{inputs.ItemId}' (receipt refused); cash reversed, but the consumed store unit(s) from lots {string.Join(", ", sourceLotIds)} are consumed-but-unpaid and must be reconciled manually.";
                diag.Add(result.Refusal);
                return result;
            }

            result.Succeeded = true;
            result.UnitsPurchased = inputs.UnitsRequested;
            result.TotalCostCents = totalCents;
            result.SourceLotIds.AddRange(sourceLotIds);
            diag.Add($"CarpentrySuppliesPurchase: bought {inputs.UnitsRequested} '{unitLabel}' unit(s) of '{inputs.ItemId}' from General Store {storeBusinessId} at {inputs.UnitPriceCents}c each ({totalCents}c total) — workshop stock now {inputs.Workshop.GenericConfiguration.GetInventoryQuantity(inputs.ItemId)} unit(s).");
            return result;
        }

        /// <summary>
        /// G-1: the driver's supplies leg. Buys the shortfall to the
        /// stock targets (nails 2 lots, cloth 8 yards — proposed
        /// defaults) from the store at the live selling price the
        /// caller reads. An item already at target is not bought. Each
        /// item is attempted even when the other refuses; the error
        /// names every refusal, and the driver logs it day-keyed. A
        /// refused day is retried tomorrow — the workshop still runs on
        /// the nails and cloth it holds.
        /// </summary>
        public static bool TryMaintainSupplies(
            BusinessRuntimeState storeRuntime,
            BusinessInstanceState workshop,
            IBusinessIdentityCashPort workshopCash,
            IBusinessIdentityCashPort storeCash,
            string storeBusinessId,
            Func<string, int> unitPriceCentsForItem,
            int dayIndex,
            List<string> diag,
            out string error)
        {
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (storeRuntime == null || workshop == null || workshop.GenericConfiguration == null
                || workshopCash == null || storeCash == null || unitPriceCentsForItem == null)
            {
                error = "Carpentry supplies maintenance requires a store runtime, a workshop, both cash ports, and a live price function.";
                return false;
            }
            string workshopBusinessId = workshop.InstanceId;
            var refusals = new List<string>();
            MaintainOneItem(storeRuntime, workshop, workshopCash, storeCash, storeBusinessId,
                workshopBusinessId, NailsItemId, NailsTargetLots, unitPriceCentsForItem, dayIndex, diag, refusals);
            MaintainOneItem(storeRuntime, workshop, workshopCash, storeCash, storeBusinessId,
                workshopBusinessId, ClothItemId, ClothTargetYards, unitPriceCentsForItem, dayIndex, diag, refusals);
            if (refusals.Count > 0)
            {
                error = string.Join(" ", refusals);
                return false;
            }
            return true;
        }

        private static void MaintainOneItem(
            BusinessRuntimeState storeRuntime,
            BusinessInstanceState workshop,
            IBusinessIdentityCashPort workshopCash,
            IBusinessIdentityCashPort storeCash,
            string storeBusinessId,
            string workshopBusinessId,
            string itemId,
            int targetUnits,
            Func<string, int> unitPriceCentsForItem,
            int dayIndex,
            List<string> diag,
            List<string> refusals)
        {
            int onHand = (int)Math.Floor(workshop.GenericConfiguration.GetInventoryQuantity(itemId));
            int shortfall = targetUnits - onHand;
            if (shortfall <= 0)
            {
                // Quiet no-op (the C-7 ShouldOrder posture): a day with
                // nothing to buy logs nothing.
                return;
            }
            // Buy what the store can actually sell today. Requiring the whole
            // target in one transaction left smaller, legitimate retail lots
            // stranded (for example 3 available cloth yards against an 8-yard
            // target). This does not book expected stock or relax the target:
            // the normal purchase authority still proves and consumes each
            // available item lot, and a later day can replenish the remainder.
            int available = CountAvailableStoreUnits(storeRuntime, storeBusinessId, itemId);
            int unitPriceCents = unitPriceCentsForItem(itemId);
            int cashAffordableUnits = unitPriceCents > 0
                ? Math.Max(0, workshopCash.BalanceCents / unitPriceCents)
                : shortfall;
            int unitsToBuy = Math.Min(shortfall, Math.Min(available, cashAffordableUnits));
            if (unitsToBuy <= 0) unitsToBuy = shortfall; // preserve the existing explicit stock-shortage refusal
            var inputs = new CarpentrySuppliesPurchaseInputs
            {
                StoreRuntime = storeRuntime,
                Workshop = workshop,
                WorkshopCashPort = workshopCash,
                StoreCashPort = storeCash,
                WorkshopBusinessId = workshopBusinessId,
                StoreBusinessId = storeBusinessId,
                ItemId = itemId,
                UnitPriceCents = unitPriceCents,
                UnitsRequested = unitsToBuy,
                DayIndex = dayIndex,
                TransactionId = BuildTransactionId(itemId, workshopBusinessId, dayIndex),
            };
            CarpentrySuppliesPurchaseResult result = PurchaseUnits(inputs, diag);
            if (!result.Succeeded)
                refusals.Add(result.Refusal);
            else if (unitsToBuy < shortfall)
                diag.Add($"CarpentrySuppliesPurchase: purchased {unitsToBuy} '{itemId}' unit(s) toward a {shortfall}-unit reorder (store available={available}, affordable at {unitPriceCents}c/unit={cashAffordableUnits}); remaining need is {shortfall - unitsToBuy} unit(s), with no incoming stock assumed.");
        }

        private static int CountAvailableStoreUnits(BusinessRuntimeState storeRuntime, string storeBusinessId, string itemId)
        {
            if (storeRuntime?.RetailItemLots == null
                || !TryGetItemTable(itemId, out string categoryId, out string unitLabel)) return 0;
            int available = 0;
            foreach (var lot in storeRuntime.RetailItemLots)
            {
                if (lot == null
                    || !string.Equals(lot.ItemId, itemId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.UnitLabel, unitLabel, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(lot.OwnerBusinessId, storeBusinessId, StringComparison.Ordinal)
                    || !string.Equals(lot.CustodianBusinessId, storeBusinessId, StringComparison.Ordinal)) continue;
                available += Math.Max(0, lot.AvailableUnits);
            }
            return available;
        }

        private static bool HasReceivedShipment(BusinessInstanceState workshop, string transactionId)
        {
            if (workshop?.GenericConfiguration?.ReceivedShipmentIds == null) return false;
            foreach (string received in workshop.GenericConfiguration.ReceivedShipmentIds)
            {
                if (string.Equals(received, transactionId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
