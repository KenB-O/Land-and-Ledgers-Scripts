using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    [Serializable]
    public sealed class OffMapAidCashTransaction
    {
        public string TransactionId = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public string BuyerName = string.Empty;
        public string SupplierBusinessId = string.Empty;
        public int AmountCents;
        public int DayIndex;
        public int SupplierCashAfterCents;
        public string OrderId = string.Empty;
    }

    [Serializable]
    public sealed class OffMapAidCargoLot
    {
        public string LotId = string.Empty;
        public string OrderId = string.Empty;
        public string SkuId = string.Empty;
        public string SupplierBusinessId = string.Empty;
        public string BuyerBusinessId = string.Empty;
        public string OrderUnit = string.Empty;
        public int PackageUnits;
        public int InventoryUnits;
        public string InventoryUnit = string.Empty;
        public int BakeryUnits;
        public int TotalCostCents;
        public int CreatedDayIndex;
        public string ShipmentId = string.Empty;
        public string DestinationBusinessId = string.Empty;
        public bool SourceCommitted;
        public bool Received;

        /// <summary>
        /// Priority spoilage integration: the Slice 5 perishable
        /// condition state attached to this lot (additive; old saves
        /// load with HasPerishableCondition false, which means "never
        /// attached"). Flour sacks carry no authored aging rule, so
        /// the state is Unknown and never changes with elapsed time.
        /// Created at order time. The save DTO carries the cargo lots
        /// directly, so the state round-trips with the lot.
        /// </summary>
        public PerishableLotState.SaveDto PerishableCondition;

        /// <summary>Whether <see cref="PerishableCondition"/> was attached at the order.</summary>
        public bool HasPerishableCondition;
    }

    [Serializable]
    public sealed class OffMapAidBusinessSaveDto
    {
        public string BusinessId = string.Empty;
        public int CashCents;
        public int LastReplenishedWeek;
        public List<string> InventoryItemIds = new List<string>();
        public List<int> InventoryUnits = new List<int>();
        public List<OffMapAidCashTransaction> CashTransactions = new List<OffMapAidCashTransaction>();
        public List<OffMapAidCargoLot> Lots = new List<OffMapAidCargoLot>();
    }

    /// <summary>
    /// Runtime ledger for one game-run off-map aid business. Orders debit physical
    /// inventory immediately into a uniquely identified pickup lot, and move cash
    /// from a real buyer cash port into the supplier's retained limbo balance.
    /// </summary>
    public sealed class OffMapAidBusinessRuntime
    {
        private readonly OffMapAidBusinessDefinition definition;
        private readonly Dictionary<string, int> inventory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OffMapAidInventoryRow> rows = new Dictionary<string, OffMapAidInventoryRow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<OffMapAidCashTransaction> transactions = new List<OffMapAidCashTransaction>();
        private readonly List<OffMapAidCargoLot> lots = new List<OffMapAidCargoLot>();
        private int cashCents;
        private int lastReplenishedWeek;

        public OffMapAidBusinessRuntime(OffMapAidBusinessDefinition definition)
        {
            this.definition = definition;
            if (definition == null) return;
            foreach (OffMapAidInventoryRow row in definition.Inventory ?? new List<OffMapAidInventoryRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ItemId)) continue;
                rows[row.ItemId] = row;
                inventory[row.ItemId] = Math.Max(0, row.StartingUnits);
            }
            // Week zero is the authored starting reserve; the first scheduled top-up is week one.
            lastReplenishedWeek = 0;
        }

        public string BusinessId => definition != null ? definition.BusinessId : string.Empty;
        public string DisplayName => definition != null ? definition.DisplayName : string.Empty;

        /// <summary>
        /// Priority 9 Slice F3 (P9 plan section 4 Slice F3): the custody
        /// transfer log this runtime records Millbrook orders into, when
        /// set. Null (the default) means no recording — today behaviour.
        /// The log is an audit trail only: no flow reads it, and a logging
        /// failure never fails an order.
        /// </summary>
        public CustodyTransferLog CustodyLog { get; set; }
        public float PickupRouteMilesFromTown => definition != null ? Math.Max(0f, definition.PickupRouteMilesFromTown) : 0f;
        public int CashCents => Math.Max(0, cashCents);
        public IReadOnlyList<OffMapAidCashTransaction> CashTransactions => transactions;
        public IReadOnlyList<OffMapAidCargoLot> Lots => lots;
        public int GetInventoryUnits(string itemId) => inventory.TryGetValue(itemId ?? string.Empty, out int units) ? units : 0;
        public int GetOrderPriceCents(string itemId) => rows.TryGetValue(itemId ?? string.Empty, out OffMapAidInventoryRow row)
            ? Math.Max(0, row.OrderPriceCents) : 0;
        public bool HasOrder(string requestId) => FindLot(BusinessId + ":" + (requestId ?? string.Empty).Trim()) != null;

        /// <summary>Flour path, preserved for existing callers: orders bakery_flour_sack.</summary>
        public string TryPlaceOrder(
            string requestId,
            string buyerBusinessId,
            string buyerName,
            int packageUnits,
            IBusinessCashPort buyerCash,
            int dayIndex,
            out OffMapAidCargoLot lot)
        {
            return TryPlaceOrderForItem(requestId, "bakery_flour_sack", buyerBusinessId, buyerName,
                packageUnits, buyerCash, dayIndex, out lot);
        }

        /// <summary>
        /// Part B maintenance (2026-11-15): the aid runtime is a generic
        /// authored-table authority, not a flour-only one. The old body
        /// hardcoded bakery_flour_sack, so no Timberline fuel row, feed
        /// row, or any other authored row could ever be ordered through
        /// it — a Timberline definition would be refused for having no
        /// flour row. This method orders any row the definition authors,
        /// at that row's own price and conversions; no price or
        /// conversion is invented here. BakeryUnits is the row's own
        /// figure and is 0 for rows that carry none (e.g. fuel); only
        /// the flour path treats it as bakery stock.
        /// </summary>
        public string TryPlaceOrderForItem(
            string requestId,
            string itemId,
            string buyerBusinessId,
            string buyerName,
            int packageUnits,
            IBusinessCashPort buyerCash,
            int dayIndex,
            out OffMapAidCargoLot lot)
        {
            lot = null;
            string supplierName = string.IsNullOrWhiteSpace(DisplayName) ? BusinessId : DisplayName;
            if (definition == null) return supplierName + " aid order refused: no authored supplier table is loaded.";
            string definitionProblem = definition.ValidateDefinition();
            if (definitionProblem != null) return supplierName + " aid order refused: " + definitionProblem;
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(itemId)
                || string.IsNullOrWhiteSpace(buyerBusinessId)
                || packageUnits <= 0 || buyerCash == null)
                return supplierName + " aid order refused: request id, item, buyer, positive quantity, and buyer cash authority are required.";

            string orderId = BusinessId + ":" + requestId.Trim();
            OffMapAidCargoLot existing = FindLot(orderId);
            if (existing != null)
                return supplierName + " aid order refused: request already exists as " + orderId + "; no second debit was made.";

            if (!rows.TryGetValue(itemId, out OffMapAidInventoryRow row))
                return supplierName + " aid order refused: table has no " + itemId + " package definition.";
            if (row.OrderPriceCents <= 0 || row.InventoryUnitsPerOrderUnit <= 0)
                return supplierName + " aid order refused: package price or unit conversion is not configured for " + itemId + ".";
            long poundsRequired = (long)row.InventoryUnitsPerOrderUnit * packageUnits;
            long totalPrice = (long)row.OrderPriceCents * packageUnits;
            if (poundsRequired > int.MaxValue || totalPrice > int.MaxValue)
                return supplierName + " aid order refused: requested amount exceeds supported accounting units.";
            int available = GetInventoryUnits(row.ItemId);
            if (available < poundsRequired)
                return $"{supplierName} aid order refused: {supplierName} has {available} {row.InventoryUnit}; order requires {(int)poundsRequired} {row.InventoryUnit}.";
            if ((long)cashCents + totalPrice > int.MaxValue)
                return supplierName + " aid order refused: supplier cash ledger would exceed its supported balance.";

            string debitProblem = buyerCash.TryDebit((int)totalPrice, supplierName + " " + row.ItemId + " order " + orderId);
            if (debitProblem != null) return supplierName + " aid order refused: buyer payment failed: " + debitProblem;

            inventory[row.ItemId] = available - (int)poundsRequired;
            cashCents += (int)totalPrice;
            lot = new OffMapAidCargoLot
            {
                LotId = "aidlot-" + orderId,
                OrderId = orderId,
                SkuId = row.ItemId,
                SupplierBusinessId = BusinessId,
                BuyerBusinessId = buyerBusinessId,
                OrderUnit = row.OrderUnit,
                PackageUnits = packageUnits,
                InventoryUnits = (int)poundsRequired,
                InventoryUnit = row.InventoryUnit,
                BakeryUnits = checked(row.BakeryUnitsPerOrderUnit * packageUnits),
                TotalCostCents = (int)totalPrice,
                CreatedDayIndex = Math.Max(0, dayIndex),
            };
            // Priority spoilage integration: the condition state is
            // CREATED with the lot at its creation site. The sacks
            // carry no authored aging rule, so the state is Unknown —
            // explicit, never a guessed shelf life — anchored at the
            // order day, in sacks (the order unit).
            lot.PerishableCondition = new PerishableLotState(
                new LotKey(CustodyTransferLog.MillbrookAuthority(BusinessId), lot.LotId),
                row.ItemId,
                packageUnits,
                Math.Max(0, dayIndex))
                .WithSource(orderId, BusinessId, "supplier:" + BusinessId)
                .CaptureSaveDto();
            lot.HasPerishableCondition = true;
            lots.Add(lot);
            transactions.Add(new OffMapAidCashTransaction
            {
                TransactionId = "aidcash-" + orderId,
                BuyerBusinessId = buyerBusinessId,
                BuyerName = buyerName ?? buyerBusinessId,
                SupplierBusinessId = BusinessId,
                AmountCents = (int)totalPrice,
                DayIndex = Math.Max(0, dayIndex),
                SupplierCashAfterCents = CashCents,
                OrderId = orderId,
            });
            // Priority 9 Slice F3: custody recording only. The order is a
            // creation event — no source lot — and the new cargo lot is held
            // by the supplier until freight commits it. A logging failure
            // never fails the order.
            if (CustodyLog != null)
            {
                CustodyLog.Record(new CustodyTransferRecord(
                    Math.Max(0, dayIndex), row.ItemId, packageUnits,
                    LotKey.Empty,
                    new LotKey(CustodyTransferLog.MillbrookAuthority(BusinessId), lot.LotId),
                    CustodyTransferLog.SupplierCustodian(BusinessId),
                    CustodyTransferLog.SupplierCustodian(BusinessId),
                    orderId, CustodyTransferLog.CauseMillbrookOrder), out _);
            }
            return null;
        }

        /// <summary>Weekly, deterministic playtest replenishment. No top-up exceeds the authored reserve target.</summary>
        public List<string> ApplyWeeklyReplenishment(int dayIndex)
        {
            var messages = new List<string>();
            if (definition == null || dayIndex < 7) return messages;
            int week = Math.Max(0, dayIndex / 7);
            while (lastReplenishedWeek < week)
            {
                int replenishmentWeek = ++lastReplenishedWeek;
                foreach (OffMapAidProductionRow production in definition.Production ?? new List<OffMapAidProductionRow>())
                {
                    if (production == null || !rows.TryGetValue(production.ItemId ?? string.Empty, out OffMapAidInventoryRow row)) continue;
                    int current = GetInventoryUnits(row.ItemId);
                    int amount = DeterministicRange(DeterministicSeed(production.ItemId, replenishmentWeek), production.WeeklyMinimumUnits,
                        production.WeeklyMaximumUnits);
                    int added = Math.Min(amount, Math.Max(0, production.TargetReserveUnits - current));
                    if (added <= 0) continue;
                    inventory[row.ItemId] = current + added;
                    messages.Add($"{DisplayName} production replenished {row.ItemId} by {added} {row.InventoryUnit}; inventory {current}->{current + added}/{production.TargetReserveUnits} {row.InventoryUnit} (week {replenishmentWeek}).");
                }
            }
            return messages;
        }

        /// <summary>Marks the purchased lot as leaving supplier custody; repeat same shipment is idempotent.</summary>
        public string CommitFreightPickup(string lotId, string shipmentId, int packageUnits)
        {
            string supplierName = string.IsNullOrWhiteSpace(DisplayName) ? BusinessId : DisplayName;
            OffMapAidCargoLot lot = FindLotById(lotId);
            if (lot == null) return supplierName + " freight pickup refused: unknown aid lot.";
            if (string.IsNullOrWhiteSpace(shipmentId) || packageUnits != lot.PackageUnits)
                return supplierName + " freight pickup refused: shipment id or complete lot quantity does not match.";
            if (lot.SourceCommitted)
                return string.Equals(lot.ShipmentId, shipmentId, StringComparison.Ordinal)
                    ? null : supplierName + " freight pickup refused: lot is already committed to another shipment.";
            if (lot.Received) return supplierName + " freight pickup refused: lot was already received.";
            lot.ShipmentId = shipmentId;
            lot.SourceCommitted = true;
            return null;
        }

        /// <summary>Credits the purchased lot to its named destination once, preserving its identity and bakery-unit conversion.</summary>
        public string ReceiveAtBakery(string lotId, string shipmentId, string destinationBusinessId, out int bakeryUnits)
        {
            return ReceiveAtDestination(lotId, shipmentId, destinationBusinessId, out bakeryUnits);
        }

        /// <summary>
        /// Part B maintenance (2026-11-15): the generic receipt behind
        /// ReceiveAtBakery. A fuel or feed lot credits to its destination
        /// the same way a flour lot credits to the bakery; the
        /// out value is the lot's own row figure (0 for rows that carry
        /// no bakery-unit conversion). ReceiveAtBakery stays as the
        /// flour-path wrapper so existing callers are untouched.
        /// </summary>
        public string ReceiveAtDestination(string lotId, string shipmentId, string destinationBusinessId, out int bakeryUnits)
        {
            bakeryUnits = 0;
            string supplierName = string.IsNullOrWhiteSpace(DisplayName) ? BusinessId : DisplayName;
            OffMapAidCargoLot lot = FindLotById(lotId);
            if (lot == null) return supplierName + " cargo receipt refused: unknown aid lot.";
            if (lot.Received)
            {
                if (string.Equals(lot.ShipmentId, shipmentId, StringComparison.Ordinal)
                    && string.Equals(lot.DestinationBusinessId, destinationBusinessId, StringComparison.Ordinal))
                {
                    bakeryUnits = lot.BakeryUnits;
                    return null;
                }
                return supplierName + " cargo receipt refused: lot was already received by a different shipment or destination.";
            }
            if (!lot.SourceCommitted || !string.Equals(lot.ShipmentId, shipmentId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(destinationBusinessId))
                return supplierName + " cargo receipt refused: source pickup, shipment identity, or destination does not match.";
            lot.DestinationBusinessId = destinationBusinessId;
            lot.Received = true;
            bakeryUnits = lot.BakeryUnits;
            return null;
        }

        public OffMapAidBusinessSaveDto ExportState()
        {
            var dto = new OffMapAidBusinessSaveDto { BusinessId = BusinessId, CashCents = cashCents, LastReplenishedWeek = lastReplenishedWeek };
            foreach (var pair in inventory) { dto.InventoryItemIds.Add(pair.Key); dto.InventoryUnits.Add(pair.Value); }
            foreach (OffMapAidCashTransaction transaction in transactions) dto.CashTransactions.Add(Clone(transaction));
            foreach (OffMapAidCargoLot lot in lots) dto.Lots.Add(Clone(lot));
            return dto;
        }

        public string ImportState(OffMapAidBusinessSaveDto dto)
        {
            if (dto == null)
            {
                foreach (var row in rows) inventory[row.Key] = Math.Max(0, row.Value.StartingUnits);
                cashCents = 0;
                lastReplenishedWeek = 0;
                transactions.Clear();
                lots.Clear();
                return null;
            }
            if (!string.Equals(dto.BusinessId, BusinessId, StringComparison.Ordinal))
                return $"Off-map aid save refused: saved supplier '{dto.BusinessId}' does not match '{BusinessId}'.";
            if (dto.InventoryItemIds == null || dto.InventoryUnits == null || dto.InventoryItemIds.Count != dto.InventoryUnits.Count)
                return "Off-map aid save refused: inventory item and unit arrays do not align.";
            if (dto.CashCents < 0 || dto.LastReplenishedWeek < 0)
                return "Off-map aid save refused: cash or replenishment week is negative.";
            var seenItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dto.InventoryItemIds.Count; i++)
            {
                if (!rows.ContainsKey(dto.InventoryItemIds[i]) || dto.InventoryUnits[i] < 0
                    || !seenItemIds.Add(dto.InventoryItemIds[i]))
                    return "Off-map aid save refused: saved inventory contains an unknown item or negative stock.";
            }
            var seenLotIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (OffMapAidCargoLot lot in dto.Lots ?? new List<OffMapAidCargoLot>())
            {
                // Part B maintenance (2026-11-15): a lot's BakeryUnits is
                // its row's own figure, and rows without a bakery-unit
                // conversion (e.g. fuel) legitimately carry 0. The old
                // blanket BakeryUnits > 0 check would refuse any such
                // lot on load. A lot is still invalid when its bakery
                // units are negative, or when its row carries a positive
                // conversion and the lot's figure does not match it.
                bool bakeryUnitsValid = lot != null && lot.BakeryUnits >= 0;
                if (lot != null && rows.TryGetValue(lot.SkuId ?? string.Empty, out OffMapAidInventoryRow lotRow)
                    && lotRow.BakeryUnitsPerOrderUnit > 0)
                {
                    bakeryUnitsValid = lot.BakeryUnits == checked(lotRow.BakeryUnitsPerOrderUnit * lot.PackageUnits);
                }
                if (lot == null || string.IsNullOrWhiteSpace(lot.LotId) || !seenLotIds.Add(lot.LotId)
                    || lot.PackageUnits <= 0 || lot.InventoryUnits <= 0 || !bakeryUnitsValid
                    || (lot.Received && (!lot.SourceCommitted || string.IsNullOrWhiteSpace(lot.ShipmentId)
                        || string.IsNullOrWhiteSpace(lot.DestinationBusinessId))))
                    return "Off-map aid save refused: cargo lot identity or custody state is invalid.";
            }

            foreach (var row in rows) inventory[row.Key] = Math.Max(0, row.Value.StartingUnits);
            for (int i = 0; i < dto.InventoryItemIds.Count; i++)
            {
                inventory[dto.InventoryItemIds[i]] = dto.InventoryUnits[i];
            }
            cashCents = dto.CashCents;
            lastReplenishedWeek = dto.LastReplenishedWeek;
            transactions.Clear();
            foreach (OffMapAidCashTransaction transaction in dto.CashTransactions ?? new List<OffMapAidCashTransaction>())
                if (transaction != null) transactions.Add(Clone(transaction));
            lots.Clear();
            foreach (OffMapAidCargoLot lot in dto.Lots ?? new List<OffMapAidCargoLot>())
                if (lot != null) lots.Add(Clone(lot));
            return null;
        }

        private OffMapAidCargoLot FindLot(string orderId) => lots.Find(entry => entry != null && string.Equals(entry.OrderId, orderId, StringComparison.Ordinal));
        private OffMapAidCargoLot FindLotById(string lotId) => lots.Find(entry => entry != null && string.Equals(entry.LotId, lotId, StringComparison.Ordinal));
        private static int DeterministicRange(int seed, int minimum, int maximum)
        {
            if (maximum <= minimum) return Math.Max(0, minimum);
            return new Random(seed).Next(Math.Max(0, minimum), maximum + 1);
        }
        private static int DeterministicSeed(string itemId, int week)
        {
            unchecked
            {
                int hash = 17;
                string text = (itemId ?? string.Empty) + "|" + week;
                foreach (char character in text) hash = hash * 31 + character;
                return hash;
            }
        }
        private static OffMapAidCargoLot Clone(OffMapAidCargoLot value) => new OffMapAidCargoLot
        {
            LotId = value.LotId, OrderId = value.OrderId, SkuId = value.SkuId,
            SupplierBusinessId = value.SupplierBusinessId, BuyerBusinessId = value.BuyerBusinessId,
            OrderUnit = value.OrderUnit, PackageUnits = value.PackageUnits, InventoryUnits = value.InventoryUnits,
            InventoryUnit = value.InventoryUnit, BakeryUnits = value.BakeryUnits, TotalCostCents = value.TotalCostCents,
            CreatedDayIndex = value.CreatedDayIndex, ShipmentId = value.ShipmentId,
            DestinationBusinessId = value.DestinationBusinessId, SourceCommitted = value.SourceCommitted, Received = value.Received,
            // Priority spoilage integration: the attached condition state
            // is part of the lot and clones with it (the SaveDto is a
            // value snapshot; the bool travels with it). ExportState and
            // ImportState both go through this clone, so a dropped field
            // here would lose the state on save/load.
            PerishableCondition = value.PerishableCondition, HasPerishableCondition = value.HasPerishableCondition,
        };
        private static OffMapAidCashTransaction Clone(OffMapAidCashTransaction value) => new OffMapAidCashTransaction
        {
            TransactionId = value.TransactionId, BuyerBusinessId = value.BuyerBusinessId, BuyerName = value.BuyerName,
            SupplierBusinessId = value.SupplierBusinessId, AmountCents = value.AmountCents, DayIndex = value.DayIndex,
            SupplierCashAfterCents = value.SupplierCashAfterCents, OrderId = value.OrderId,
        };
    }

}
