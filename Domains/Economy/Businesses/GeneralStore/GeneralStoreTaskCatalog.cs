using System;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;

namespace LandLedgers.MVP
{
    /// <summary>
    /// Goods classes for freight handling. Fragile and perishable goods are handled
    /// slower (more care per unit). Combined classes use the slowest applicable rate.
    /// </summary>
    public enum FreightGoodsClass
    {
        Standard = 0,
        Fragile = 1,
        Perishable = 2,
        FragileAndPerishable = 3,
    }

    /// <summary>
    /// TTS-5: the first concrete task catalog — general store work as DATA.
    ///
    /// All minute values are STARTING TUNING VALUES. Kennedy tunes the numbers
    /// (rates, bases, per-lot costs) without touching logic: every entry is a
    /// registered <see cref="TaskDefinition"/> or a documented rate constant below.
    ///
    /// Design rules honored here:
    /// - The store is NOT self-serve: a customer without an assigned tender waits.
    ///   (What happens when they wait too long is decided later — noted, not built.)
    /// - Unload time scales with the shipment itself (per-unit rate x quantity),
    ///   floored at 1 minute. Three dozen eggs ≈ 2–3 min; a full freight wagon
    ///   runs into the tens of minutes.
    /// - Every task flows through the shared TaskAuthority: skill scaling (TTS-3),
    ///   work-time budgets (TTS-1) and queue priority (TTS-2) all apply.
    /// </summary>
    public static class GeneralStoreTaskCatalog
    {
        public const string TendCustomerId = "gs.tend-customer";
        public const string UnloadFreightId = "gs.unload-freight";
        public const string StockShelvesId = "gs.stock-shelves";
        public const string CleanStoreId = "gs.clean-store";

        /// <summary>TUNING: base minutes to tend one customer (CustomerTending scales toward 1).</summary>
        public const int TendCustomerBaseMinutes = 4;

        /// <summary>TUNING: units handled per minute by goods class (slower = more care).</summary>
        public const int StandardUnitsPerMinute = 20;
        public const int FragileUnitsPerMinute = 15;
        public const int PerishableUnitsPerMinute = 12;

        /// <summary>TUNING: minutes per restocked lot.</summary>
        public const int StockShelvesMinutesPerLot = 2;

        /// <summary>TUNING: flat minutes to clean one store area.</summary>
        public const int CleanStoreMinutesPerArea = 15;

        /// <summary>Registers the four store task definitions. Safe to call once at boot.</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            string ignored;

            var tend = new TaskDefinition(TendCustomerId, "Tend customer", TendCustomerBaseMinutes);
            tend.SetRequiredSkill(SkillIds.CustomerTending, new[] { SkillIds.CustomerTending });
            tend.SetDefaultPriority(TaskPriority.High);
            tend.DomainTags.Add("retail");
            tend.DomainTags.Add("customer-service");
            authority.RegisterDefinition(tend, out ignored);

            var unload = new TaskDefinition(UnloadFreightId, "Unload freight", 1);
            unload.SetRequiredSkill(SkillIds.FreightHandling, new[] { SkillIds.FreightHandling });
            unload.SetDefaultPriority(TaskPriority.Normal);
            unload.DomainTags.Add("logistics");
            unload.DomainTags.Add("freight");
            authority.RegisterDefinition(unload, out ignored);

            var stock = new TaskDefinition(StockShelvesId, "Stock shelves", StockShelvesMinutesPerLot);
            stock.SetRequiredSkill(SkillIds.Stocking, new[] { SkillIds.Stocking });
            stock.SetDefaultPriority(TaskPriority.Normal);
            stock.DomainTags.Add("retail");
            stock.DomainTags.Add("merchandising");
            authority.RegisterDefinition(stock, out ignored);

            var clean = new TaskDefinition(CleanStoreId, "Clean store area", CleanStoreMinutesPerArea);
            clean.SetRequiredSkill(SkillIds.Cleaning, new[] { SkillIds.Cleaning });
            clean.SetDefaultPriority(TaskPriority.Low);
            clean.DomainTags.Add("upkeep");
            authority.RegisterDefinition(clean, out ignored);
        }

        /// <summary>
        /// A customer needs tending. The store is not self-serve: until a worker is
        /// assigned, the customer waits in the queue — T1C's CustomerQueue gives the
        /// queue teeth (patience expiry and overlong lines write Tech X §5.4 lost sales).
        /// </summary>
        public static WorkTask EnqueueTendCustomer(TaskAuthority authority, EntityId storeId, int currentDayIndex, string customerRef)
        {
            return authority.CreateTask(TendCustomerId, storeId, currentDayIndex, customerRef);
        }

        /// <summary>
        /// A freight shipment arrived. Unload time = ceil(quantity / unitsPerMinute
        /// for the goods class), floored at 1 minute. Fragile/perishable classes are
        /// slower; combined classes take the slowest applicable rate.
        /// </summary>
        public static WorkTask EnqueueUnloadFreight(TaskAuthority authority, EntityId storeId, int currentDayIndex, FreightGoodsClass goodsClass, int quantityUnits, string shipmentRef = null)
        {
            int minutes = ComputeUnloadMinutes(goodsClass, quantityUnits);
            WorkTask task = authority.CreateTaskWithPlannedMinutes(UnloadFreightId, storeId, currentDayIndex, minutes, shipmentRef);
            return task;
        }

        /// <summary>Restock lots onto shelves: per-lot minutes x lot count, floored at 1.</summary>
        public static WorkTask EnqueueStockShelves(TaskAuthority authority, EntityId storeId, int currentDayIndex, int lotCount)
        {
            int minutes = Math.Max(1, Math.Max(0, lotCount) * StockShelvesMinutesPerLot);
            return authority.CreateTaskWithPlannedMinutes(StockShelvesId, storeId, currentDayIndex, minutes);
        }

        /// <summary>Clean one store area (sales floor, stockroom, storefront): flat minutes.</summary>
        public static WorkTask EnqueueCleanStore(TaskAuthority authority, EntityId storeId, int currentDayIndex, string areaId)
        {
            WorkTask task = authority.CreateTask(CleanStoreId, storeId, currentDayIndex);
            task.SetCustomer(areaId);
            return task;
        }

        /// <summary>
        /// Freight unload math, exposed for tuning/tests: the slowest applicable
        /// per-unit rate wins; result floored at 1 minute.
        /// </summary>
        public static int ComputeUnloadMinutes(FreightGoodsClass goodsClass, int quantityUnits)
        {
            int unitsPerMinute;
            switch (goodsClass)
            {
                case FreightGoodsClass.Fragile:
                    unitsPerMinute = FragileUnitsPerMinute;
                    break;
                case FreightGoodsClass.Perishable:
                case FreightGoodsClass.FragileAndPerishable:
                    unitsPerMinute = PerishableUnitsPerMinute;
                    break;
                default:
                    unitsPerMinute = StandardUnitsPerMinute;
                    break;
            }

            int quantity = Math.Max(0, quantityUnits);
            int minutes = (quantity + unitsPerMinute - 1) / unitsPerMinute; // ceil
            return Math.Max(1, minutes);
        }
    }
}
