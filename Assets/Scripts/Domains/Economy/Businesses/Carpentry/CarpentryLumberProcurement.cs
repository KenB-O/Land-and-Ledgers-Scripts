using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Carpentry
{
    /// <summary>
    /// The receipt for one carpentry lumber purchase from the lumber
    /// yard: the yard sale it settles, the units and price, and the
    /// yard lot ids the lumber came from (provenance for the furniture
    /// the lumber becomes).
    /// </summary>
    [Serializable]
    public sealed class CarpentryLumberReceipt
    {
        public EntityId SaleId = EntityId.Invalid;
        public int Units;
        public int UnitPriceCents;
        public int TotalCents;
        public List<string> LotIds = new List<string>();
    }

    /// <summary>
    /// F1 part 2 (expanded campaign 2026-11-16, O-F3): the carpentry
    /// workshop's lumber procurement from the lumber yard, through the
    /// yard's real retail path. The workshop is a retail buyer, not a
    /// construction project — it buys through TrySellLumberRetail at
    /// the yard's quoted retail price (FIFO landed cost with the yard's
    /// grade-price markup), never through the construction sale
    /// adapter, which requires a project identity the carpenter does
    /// not have. Money moves through the caller's cash ports
    /// (IBusinessCashPort): the workshop is debited, the yard is
    /// credited, and the lumber is received into the workshop's
    /// generic inventory with the yard sale id as the shipment id, so
    /// a re-applied receipt can never double-add. Order: quote, debit
    /// the buyer, sell, credit the seller, receive. A sale refusal
    /// after the debit refunds the buyer in full; a seller-credit or
    /// inventory-receipt failure after the sale is a loud dispute
    /// naming the sale id — the lots have left the yard and must be
    /// reconciled, never retried blindly. The yard quotes -1 when it
    /// holds no saleable lumber, and this service refuses loudly
    /// rather than inventing a price. Buyer-collected at the yard for
    /// this slice: no delivery is scheduled (the yard delivery
    /// register exists for a later slice, or Ethan's freight).
    /// </summary>
    public static class CarpentryLumberProcurement
    {
        public static bool TryBuyLumber(
            LumberYardShopRuntime yard,
            BusinessInstanceState workshop,
            IBusinessCashPort workshopCash,
            IBusinessCashPort yardCash,
            int units,
            int dayIndex,
            List<string> diag,
            out CarpentryLumberReceipt receipt,
            out string error,
            LumberYardGradePricePolicy pricePolicy = null)
        {
            receipt = null;
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (yard == null || workshop == null || workshop.RuntimeState == null
                || workshopCash == null || yardCash == null)
            {
                error = "Carpentry lumber procurement requires a lumber yard, a workshop with runtime state, and cash ports for both parties.";
                return false;
            }
            if (units <= 0)
            {
                error = "Carpentry lumber procurement buys at least one unit.";
                return false;
            }
            string buyerLabel = workshop.RuntimeDisplayName;
            if (string.IsNullOrWhiteSpace(buyerLabel))
            {
                error = "Carpentry lumber procurement requires a named workshop — the yard refuses anonymous sales.";
                return false;
            }

            int unitPriceCents = yard.QuoteRetailLumberPrice(
                null, null, pricePolicy ?? new LumberYardGradePricePolicy(), diag);
            if (unitPriceCents < 0)
            {
                error = $"Carpentry lumber procurement for '{buyerLabel}' refused: the lumber yard holds no saleable lumber to quote — no stock, no price.";
                return false;
            }
            int totalCents = units * unitPriceCents;
            if (workshopCash.BalanceCents < totalCents)
            {
                error = $"Carpentry lumber procurement for '{buyerLabel}' refused: the workshop holds {workshopCash.BalanceCents}c and {units} lumber units cost {totalCents}c at {unitPriceCents}c each — nothing was bought.";
                return false;
            }

            string debitRefusal = workshopCash.TryDebit(totalCents,
                $"Carpentry lumber purchase: {units} units from the lumber yard at {unitPriceCents}c each");
            if (!string.IsNullOrWhiteSpace(debitRefusal))
            {
                error = $"Carpentry lumber procurement for '{buyerLabel}' refused: the workshop debit failed — {debitRefusal}";
                return false;
            }

            LumberYardSaleRecord sale = yard.TrySellLumberRetail(
                buyerLabel, units, null, null, unitPriceCents, dayIndex, diag);
            if (sale == null)
            {
                string refundRefusal = workshopCash.Credit(totalCents,
                    $"Carpentry lumber purchase refund: the yard sale for {units} units was refused");
                if (!string.IsNullOrWhiteSpace(refundRefusal))
                {
                    error = $"Carpentry lumber procurement for '{buyerLabel}' DISPUTED: the yard refused the sale after the workshop was debited {totalCents}c, and the refund failed — {refundRefusal}. Reconcile before retrying.";
                    return false;
                }
                error = $"Carpentry lumber procurement for '{buyerLabel}' refused: the yard refused the sale — the workshop was refunded {totalCents}c in full.";
                return false;
            }

            string creditRefusal = yardCash.Credit(sale.TotalCents,
                $"Carpentry lumber sale {sale.SaleId}: {units} units to '{buyerLabel}' at {unitPriceCents}c each");
            if (!string.IsNullOrWhiteSpace(creditRefusal))
            {
                error = $"Carpentry lumber procurement for '{buyerLabel}' DISPUTED: yard sale {sale.SaleId} moved {units} lumber units but the yard credit of {sale.TotalCents}c failed — {creditRefusal}. Reconcile before retrying.";
                return false;
            }

            var lotIds = new List<string>();
            foreach (LumberYardMaterialSaleLine line in sale.Lines ?? new List<LumberYardMaterialSaleLine>())
            {
                if (line == null) continue;
                foreach (LumberYardLumberDispenseLine dispense in line.LumberLines ?? new List<LumberYardLumberDispenseLine>())
                {
                    if (dispense == null) continue;
                    lotIds.Add(dispense.LotId.ToString());
                }
            }
            string provenance = $"lumber-yard-retail sale {sale.SaleId} to '{buyerLabel}' ({string.Join(", ", sale.AllProvenanceLines())})";
            if (!workshop.GenericConfiguration.TryReceiveInventoryShipment(
                    sale.SaleId.ToString(), "lumber", units, unitPriceCents,
                    GenericVerticalCatalog.LumberDefinition(), provenance, dayIndex))
            {
                error = $"Carpentry lumber procurement for '{buyerLabel}' DISPUTED: yard sale {sale.SaleId} moved {units} lumber units and the yard was paid {sale.TotalCents}c, but the workshop could not receive the lumber into inventory. Reconcile before retrying.";
                return false;
            }

            receipt = new CarpentryLumberReceipt
            {
                SaleId = sale.SaleId,
                Units = units,
                UnitPriceCents = unitPriceCents,
                TotalCents = sale.TotalCents,
                LotIds = lotIds,
            };
            diag.Add($"CarpentryLumberProcurement: '{buyerLabel}' bought {units} lumber units from the lumber yard at {unitPriceCents}c each (sale {sale.SaleId}, {sale.TotalCents}c) and received them into workshop inventory.");
            return true;
        }
    }
}
