using System.Collections.Generic;
using LandLedgers.Economy.GeneralStore;

namespace LandLedgers.Economy.Retail
{
    /// <summary>
    /// NX-1B: merchant-side scarcity allocation. When stock is short, queued
    /// customers are served in queue order (first-come, first-served); the
    /// unserved remainder becomes honest lost-sale evidence (T1C,
    /// LostSaleReason.Stockout) rather than silently vanishing demand.
    /// Regulars-on-credit is a defined extension: the merchant may serve a
    /// known customer on credit (T2A instruments) when cash would turn them
    /// away — not implemented here, flagged for the credit pass.
    /// </summary>
    public static class MerchantScarcityAllocator
    {
        public sealed class ServiceResult
        {
            public WaitingCustomer Customer;
            public int UnitsServed;
            public int UnitsUnserved;
        }

        /// <summary>
        /// Serves the queue head-first against availableUnits. Returns one
        /// result per queued customer, in queue order.
        /// </summary>
        public static List<ServiceResult> Allocate(
            IReadOnlyList<WaitingCustomer> queueInOrder, int availableUnits)
        {
            var results = new List<ServiceResult>();
            int remaining = System.Math.Max(0, availableUnits);
            if (queueInOrder == null) return results;

            foreach (WaitingCustomer customer in queueInOrder)
            {
                if (customer == null) continue;
                int wanted = System.Math.Max(0, customer.UnitsWanted);
                int served = System.Math.Min(wanted, remaining);
                remaining -= served;
                results.Add(new ServiceResult
                {
                    Customer = customer,
                    UnitsServed = served,
                    UnitsUnserved = wanted - served,
                });
            }
            return results;
        }

        /// <summary>
        /// Records lost sales for the unserved remainder (T1C evidence).
        /// </summary>
        public static int RecordLostSales(
            List<ServiceResult> results,
            LostSaleLog lostSales,
            int dayIndex,
            string merchantBusinessId,
            string merchantName)
        {
            if (results == null || lostSales == null) return 0;
            int recorded = 0;
            foreach (ServiceResult r in results)
            {
                if (r.UnitsUnserved <= 0) continue;
                lostSales.Record(
                    dayIndex, merchantBusinessId, merchantName,
                    r.Customer.ActingPersonId, r.Customer.CategoryId,
                    r.UnitsUnserved, r.Customer.PriceCentsPerUnit,
                    LostSaleReason.Stockout,
                    $"NX-1B scarcity allocation: served {r.UnitsServed} of {r.Customer.UnitsWanted} queued units; stock exhausted.");
                recorded++;
            }
            return recorded;
        }
    }
}
