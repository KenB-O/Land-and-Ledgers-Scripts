using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Tailor
{
    /// <summary>
    /// W1C: the production stages of a garment order. Garments flow
    /// Ordered → Measured → ClothReserved → Cut → BasteFitted → Sewn →
    /// FinalFitted → Finished → Delivered. Mending flows
    /// Ordered → Assessed → Mended → Delivered (no cloth, no fittings).
    /// </summary>
    public enum TailorOrderStage
    {
        Ordered = 0,
        Measured = 1,
        ClothReserved = 2,
        Cut = 3,
        BasteFitted = 4,
        Sewn = 5,
        FinalFitted = 6,
        Finished = 7,
        Assessed = 8,
        Mended = 9,
        Delivered = 10,
        Cancelled = 11,
    }

    /// <summary>
    /// W1C: one garment order. The order holds its cloth in custody from the
    /// reservation stage (dispensed from named lots with provenance lines), so
    /// cutting never re-dispenses and a cancelled order returns real cloth to
    /// the shelf. Fittings are genuine steps: the order cannot pass a fitting
    /// stage until <see cref="TailorShopRuntime.RecordFitting"/> records that
    /// the customer came in.
    /// </summary>
    [Serializable]
    public sealed class TailorGarmentOrder
    {
        public string OrderId = string.Empty;
        public EntityId CustomerPersonId = EntityId.Invalid; // EntityKind.Person
        public string GarmentId = string.Empty; // tail.shirt / tail.trousers / tail.coat / tail.dress / tail.mend
        public int OrderDayIndex;
        public TailorOrderStage Stage = TailorOrderStage.Ordered;

        /// <summary>Cloth yards dispensed into this order's custody at reservation, with provenance.</summary>
        public List<TailorClothDispenseLine> ClothInCustody = new List<TailorClothDispenseLine>();

        /// <summary>Notions dispensed at sew/mend, with provenance.</summary>
        public List<TailorClothDispenseLine> NotionsUsed = new List<TailorClothDispenseLine>();

        public bool BasteFittingRecorded;
        public string BasteFittingNotes = string.Empty;
        public bool FinalFittingRecorded;
        public string FinalFittingNotes = string.Empty;

        public List<string> StageLog = new List<string>();

        public TailorGarmentOrder() { }

        public bool IsActive => Stage != TailorOrderStage.Delivered && Stage != TailorOrderStage.Cancelled;
    }

    /// <summary>
    /// W1C: one delivered garment — the piece-rate fee record. The caller
    /// settles these records as ordinary ledger outflows; money moves only
    /// through ledger authorities.
    /// </summary>
    [Serializable]
    public sealed class TailorDeliveryRecord
    {
        public string OrderId = string.Empty;
        public EntityId CustomerPersonId = EntityId.Invalid;
        public string GarmentId = string.Empty;
        public int DayIndex;
        public int PieceRateCents;
        public int FittingFeesCents;
        public List<TailorClothDispenseLine> ClothProvenance = new List<TailorClothDispenseLine>();
        public List<TailorClothDispenseLine> NotionsProvenance = new List<TailorClothDispenseLine>();

        public TailorDeliveryRecord() { }

        public int TotalFeeCents => Math.Max(0, PieceRateCents) + Math.Max(0, FittingFeesCents);
    }

    /// <summary>
    /// W1C: one cutting table. A cutting table IS a workstation instance
    /// (WorkstationId "tailor-cutting-table", Tech X §3.5: Ironing/CuttingTable):
    /// readiness derives from its component assets, never from a flag. Bench
    /// stages (cut, sew, press, mend) each occupy one ready table for the day —
    /// a one-table shop works one garment at the bench per day.
    /// </summary>
    [Serializable]
    public sealed class TailorCuttingTable
    {
        public int TableIndex;
        public WorkstationInstance Station = new WorkstationInstance();

        public TailorCuttingTable() { }
    }

    /// <summary>
    /// W1C: the per-instance tailor shop runtime. Holds the cutting-table pool
    /// (tables as workstations gating concurrent bench work), the cloth shelf
    /// (lots with provenance), the piece-rate schedule, the order queue, and the
    /// day-resolution scheduler that makes the made-to-order problem visible:
    /// garments flow across days through measure → reserve → cut → fit → sew →
    /// fit → press → deliver while fittings wait on the customer's actual visit.
    ///
    /// Boundary: this runtime owns the table/cloth/order layer only. The
    /// existing generic clothing-outlet resolution for BusinessType.Tailor in
    /// SharedBusinessRuntimeManager is untouched — W1C routes around it the way
    /// W1B routes around the barber's daily in-office resolution.
    /// </summary>
    public sealed class TailorShopRuntime
    {
        /// <summary>TUNING: the tailor's hands-on minutes per day (calibration, Canon Part XV; mirrors the barber's professional day).</summary>
        public const int ProfessionalMinutesPerDay = 600;

        private readonly string businessInstanceId;
        private readonly TailorClothStock clothStock = new TailorClothStock();
        private readonly List<TailorCuttingTable> tables = new List<TailorCuttingTable>();
        private readonly List<TailorGarmentOrder> orders = new List<TailorGarmentOrder>();
        private readonly List<TailorDeliveryRecord> deliveries = new List<TailorDeliveryRecord>();
        private TailorPieceRateSchedule pieceRates = new TailorPieceRateSchedule();
        private int orderSeq;

        /// <summary>Per-day table claims: order id → table index, released on failed advances.</summary>
        private readonly Dictionary<string, int> pendingTableClaims = new Dictionary<string, int>();

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public TailorClothStock ClothStock => clothStock;
        public IReadOnlyList<TailorCuttingTable> Tables => tables;
        public IReadOnlyList<TailorGarmentOrder> Orders => orders;
        public IReadOnlyList<TailorDeliveryRecord> Deliveries => deliveries;
        public TailorPieceRateSchedule PieceRates => pieceRates;

        public TailorShopRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void SetPieceRateSchedule(TailorPieceRateSchedule schedule)
        {
            pieceRates = schedule ?? new TailorPieceRateSchedule();
        }

        /// <summary>
        /// Installs a cutting table: a workstation instance of
        /// "tailor-cutting-table" with its component assets named. Returns a
        /// rejection string, or null on success (the table index is
        /// tables.Count - 1 afterwards).
        /// </summary>
        public string AddCuttingTable(string spaceId, List<string> componentAssetIds, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(spaceId))
                return "TailorShopRuntime: a cutting table needs a functional space — a room alone never grants the workstation.";

            var table = new TailorCuttingTable
            {
                TableIndex = tables.Count,
                Station = new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-tailor-cutting-table-{tables.Count}",
                    WorkstationId = TailorGarmentCatalog.TailorCuttingTableStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = spaceId,
                },
            };
            if (componentAssetIds != null)
            {
                foreach (string assetId in componentAssetIds)
                {
                    table.Station.InstallComponent(assetId);
                }
            }

            tables.Add(table);
            diag.Add($"TailorShopRuntime: cutting table {table.TableIndex} installed in '{spaceId}' (workstation {table.Station.InstanceId}).");
            return null;
        }

        /// <summary>
        /// Places a garment order for a customer. Returns the order id, or a
        /// rejection string starting with "TailorShopRuntime:" — callers tell
        /// them apart by the prefix (ids never contain a colon).
        /// </summary>
        public string PlaceOrder(EntityId customerPersonId, string garmentId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!customerPersonId.IsValid || customerPersonId.Kind != EntityKind.Person)
                return "TailorShopRuntime: the customer must be a valid person — no anonymous or batch orders.";
            if (!TailorGarmentCatalog.IsKnownGarment(garmentId))
                return $"TailorShopRuntime: unknown garment '{garmentId}' — only shirt, trousers, coat, dress, and mending are offered.";

            var order = new TailorGarmentOrder
            {
                OrderId = $"{businessInstanceId}-order-{orderSeq++}",
                CustomerPersonId = customerPersonId,
                GarmentId = garmentId,
                OrderDayIndex = dayIndex,
                Stage = TailorOrderStage.Ordered,
            };
            order.StageLog.Add($"day {dayIndex}: order placed ({garmentId})");
            orders.Add(order);
            diag.Add($"TailorShopRuntime: order {order.OrderId} placed — {garmentId} for {customerPersonId} (day {dayIndex}).");
            return order.OrderId;
        }

        /// <summary>
        /// Records that the customer came in for a fitting. Fittings are genuine
        /// steps: the order cannot pass its fitting stage until this is called.
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string RecordFitting(string orderId, bool isBasteFitting, string alterationNotes, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            TailorGarmentOrder order = FindOrder(orderId);
            if (order == null) return $"TailorShopRuntime: no order '{orderId}' — fitting not recorded.";
            if (!order.IsActive) return $"TailorShopRuntime: order {orderId} is {order.Stage} — no fitting on a closed order.";
            if (TailorGarmentCatalog.GetSpec(order.GarmentId).IsMending)
                return $"TailorShopRuntime: order {orderId} is mending — mending has no fittings.";

            if (isBasteFitting)
            {
                if (order.Stage != TailorOrderStage.Cut)
                    return $"TailorShopRuntime: order {orderId} is at {order.Stage} — the basting fitting happens after cutting.";
                if (order.BasteFittingRecorded)
                    return $"TailorShopRuntime: order {orderId} already had its basting fitting.";
                order.BasteFittingRecorded = true;
                order.BasteFittingNotes = alterationNotes ?? string.Empty;
                order.StageLog.Add($"day {dayIndex}: basting fitting held" +
                    (string.IsNullOrWhiteSpace(alterationNotes) ? "" : $" — alterations: {alterationNotes}"));
                diag.Add($"TailorShopRuntime: basting fitting recorded for order {orderId} (day {dayIndex}).");
                return null;
            }

            if (order.Stage != TailorOrderStage.Sewn)
                return $"TailorShopRuntime: order {orderId} is at {order.Stage} — the final fitting happens after sewing.";
            if (order.FinalFittingRecorded)
                return $"TailorShopRuntime: order {orderId} already had its final fitting.";
            order.FinalFittingRecorded = true;
            order.FinalFittingNotes = alterationNotes ?? string.Empty;
            order.StageLog.Add($"day {dayIndex}: final fitting held" +
                (string.IsNullOrWhiteSpace(alterationNotes) ? "" : $" — alterations: {alterationNotes}"));
            diag.Add($"TailorShopRuntime: final fitting recorded for order {orderId} (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Cancels an open order. Cloth held in the order's custody returns to
        /// the shelf as a fresh lot whose provenance names the cancellation and
        /// the original chains — never silently absorbed. Notions already sewn
        /// are consumed and do not return. Returns a rejection string, or null
        /// on success.
        /// </summary>
        public string CancelOrder(string orderId, int dayIndex, EntityIdRegistry idRegistry, List<string> diag)
        {
            diag = diag ?? diagnostics;
            TailorGarmentOrder order = FindOrder(orderId);
            if (order == null) return $"TailorShopRuntime: no order '{orderId}' — nothing cancelled.";
            if (!order.IsActive) return $"TailorShopRuntime: order {orderId} is already {order.Stage}.";

            if (order.ClothInCustody.Count > 0)
            {
                if (idRegistry == null)
                    return $"TailorShopRuntime: order {orderId} holds cloth in custody and there is no id registry — cancel refused rather than orphan the cloth.";

                var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var chains = new List<string>();
                foreach (var line in order.ClothInCustody)
                {
                    if (line == null) continue;
                    if (!byName.TryGetValue(line.ClothName, out int soFar)) soFar = 0;
                    byName[line.ClothName] = soFar + Math.Max(0, line.UnitsTaken);
                    if (!string.IsNullOrWhiteSpace(line.ProvenanceChain)) chains.Add(line.ProvenanceChain);
                }

                foreach (var kvp in byName)
                {
                    string rejection = clothStock.ReceiveLot(new TailorClothLot
                    {
                        LotId = idRegistry.Allocate(EntityKind.Lot),
                        ClothName = kvp.Key,
                        Units = kvp.Value,
                        AcquiredDayIndex = dayIndex,
                        ImportOrderId = $"return-{orderId}",
                        OriginName = "Returned to shelf from cancelled garment order",
                        SupplierNote = "Originally: " + string.Join(" | ", chains),
                        IsBootstrapEndowment = false,
                    }, diag);
                    if (rejection != null)
                    {
                        diag.Add($"TailorShopRuntime: {rejection}");
                        return $"TailorShopRuntime: order {orderId} cancel refused — returned cloth could not be re-lotted.";
                    }
                }

                order.ClothInCustody.Clear();
            }

            order.Stage = TailorOrderStage.Cancelled;
            order.StageLog.Add($"day {dayIndex}: order cancelled");
            diag.Add($"TailorShopRuntime: order {orderId} cancelled (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Counts cutting tables whose workstation instance evaluates ready
        /// against the catalog definition. Loud per-table diagnostics — an
        /// unready table is named, never silently skipped.
        /// </summary>
        public int CountReadyTables(
            WorkstationDefinition tableDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int ready = 0;
            foreach (var table in tables)
            {
                var reasons = new List<string>();
                string notReady = table.Station.EvaluateReady(tableDefinition, findComponent, reasons);
                if (notReady == null)
                {
                    ready++;
                }
                else
                {
                    diag.Add($"TailorShopRuntime: cutting table {table.TableIndex} not ready — {notReady}");
                }
            }

            return ready;
        }

        /// <summary>
        /// Runs one shop day: advances open orders through their production
        /// stages FIFO while the tailor's labor minutes last. Bench stages (cut,
        /// sew, press, mend) each occupy one ready cutting table for the day.
        /// Cloth and notions shortfalls refuse LOUDLY and the order stays parked
        /// at its stage — never worked on conjured stock, never silently
        /// dropped. Fitting stages wait on <see cref="RecordFitting"/>; the
        /// customer not coming in is a parked order, not a skipped step.
        /// Returns the number of stage advances.
        /// </summary>
        public int WorkDay(
            int dayIndex,
            bool tailorKitUsable,
            WorkstationDefinition tableDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int advances = 0;

            if (!tailorKitUsable)
            {
                diag.Add($"TailorShopRuntime: no usable tailor's hand kit — the shop cannot work today (NX-1 teeth gate). {CountActiveOrders()} order(s) still open.");
                return 0;
            }

            // Per-table busy flags within the shop day; only ready tables enter the pool.
            var tableFree = new Dictionary<int, bool>();
            foreach (var table in tables)
            {
                var reasons = new List<string>();
                if (table.Station.EvaluateReady(tableDefinition, findComponent, reasons) == null)
                {
                    tableFree[table.TableIndex] = true;
                }
            }

            int laborRemaining = ProfessionalMinutesPerDay;
            var snapshot = new List<TailorGarmentOrder>(orders);

            foreach (var order in snapshot)
            {
                if (!order.IsActive) continue;

                // Cascade through stages while this order can still advance today.
                while (TryAdvanceOneStage(order, dayIndex, ref laborRemaining, tableFree, diag))
                {
                    advances++;
                }
            }

            return advances;
        }

        /// <summary>
        /// Advances one order one stage if its preconditions hold. Returns true
        /// when a stage advanced (the caller cascades); false when the order is
        /// parked, done, or out of budget.
        /// </summary>
        private bool TryAdvanceOneStage(
            TailorGarmentOrder order, int dayIndex, ref int laborRemaining,
            Dictionary<int, bool> tableFree, List<string> diag)
        {
            TailorGarmentSpec spec = TailorGarmentCatalog.GetSpec(order.GarmentId);
            if (string.IsNullOrEmpty(spec.GarmentId))
            {
                diag.Add($"TailorShopRuntime: order {order.OrderId} names unknown garment '{order.GarmentId}' — stays parked, never guessed.");
                return false;
            }

            if (spec.IsMending)
            {
                return TryAdvanceMendStage(order, spec, dayIndex, ref laborRemaining, tableFree, diag);
            }

            switch (order.Stage)
            {
                case TailorOrderStage.Ordered:
                    if (!SpendLabor(ref laborRemaining, spec.MeasureMinutes, diag, order, "measuring")) return false;
                    SetStage(order, TailorOrderStage.Measured, dayIndex, $"measured ({spec.MeasureMinutes}m)");
                    return true;

                case TailorOrderStage.Measured:
                    // Cloth reservation: dispense into the order's custody with provenance.
                    var clothLines = clothStock.TryDispenseUnits(
                        TailorGarmentCatalog.ClothItemId, spec.ClothYards, dayIndex, diag);
                    if (clothLines == null)
                    {
                        diag.Add($"TailorShopRuntime: order {order.OrderId} ({spec.DisplayName}) refused — no cloth on hand. Stays parked.");
                        return false;
                    }

                    order.ClothInCustody.AddRange(clothLines);
                    SetStage(order, TailorOrderStage.ClothReserved, dayIndex,
                        $"cloth reserved ({spec.ClothYards} yd from {clothLines.Count} lot(s))");
                    return true;

                case TailorOrderStage.ClothReserved:
                    if (!ClaimTable(tableFree, diag, order)) return false;
                    if (!SpendLabor(ref laborRemaining, spec.CutMinutes, diag, order, "cutting"))
                    {
                        ReleaseTableClaim(tableFree, order);
                        return false;
                    }

                    SetStage(order, TailorOrderStage.Cut, dayIndex, $"cut ({spec.CutMinutes}m)");
                    return true;

                case TailorOrderStage.Cut:
                    if (!order.BasteFittingRecorded)
                    {
                        diag.Add($"TailorShopRuntime: order {order.OrderId} waits on its basting fitting — the customer has not come in. Stays parked.");
                        return false;
                    }

                    if (!SpendLabor(ref laborRemaining, spec.FitBasteMinutes, diag, order, "baste fitting")) return false;
                    SetStage(order, TailorOrderStage.BasteFitted, dayIndex, $"baste-fitted ({spec.FitBasteMinutes}m)");
                    return true;

                case TailorOrderStage.BasteFitted:
                    if (!ClaimTable(tableFree, diag, order)) return false;
                    // Notions already dispensed on a previous attempt stay with the
                    // order — dispense only the shortfall, never double-charge.
                    int notionsHeld = 0;
                    foreach (var held in order.NotionsUsed)
                    {
                        if (held != null) notionsHeld += Math.Max(0, held.UnitsTaken);
                    }

                    List<TailorClothDispenseLine> notionLines = new List<TailorClothDispenseLine>();
                    if (notionsHeld < spec.NotionsUnits)
                    {
                        notionLines = clothStock.TryDispenseUnits(
                            TailorGarmentCatalog.NotionsItemId, spec.NotionsUnits - notionsHeld, dayIndex, diag);
                        if (notionLines == null)
                        {
                            ReleaseTableClaim(tableFree, order);
                            diag.Add($"TailorShopRuntime: order {order.OrderId} ({spec.DisplayName}) refused — no notions on hand. Stays parked.");
                            return false;
                        }
                    }

                    if (!SpendLabor(ref laborRemaining, spec.SewMinutes, diag, order, "sewing"))
                    {
                        ReleaseTableClaim(tableFree, order);
                        // Notions were already dispensed — they stay with the order, never silently returned.
                        order.NotionsUsed.AddRange(notionLines);
                        return false;
                    }

                    order.NotionsUsed.AddRange(notionLines);
                    SetStage(order, TailorOrderStage.Sewn, dayIndex, $"sewn ({spec.SewMinutes}m)");
                    return true;

                case TailorOrderStage.Sewn:
                    if (!order.FinalFittingRecorded)
                    {
                        diag.Add($"TailorShopRuntime: order {order.OrderId} waits on its final fitting — the customer has not come in. Stays parked.");
                        return false;
                    }

                    if (!SpendLabor(ref laborRemaining, spec.FitFinalMinutes, diag, order, "final fitting")) return false;
                    SetStage(order, TailorOrderStage.FinalFitted, dayIndex, $"final-fitted ({spec.FitFinalMinutes}m)");
                    return true;

                case TailorOrderStage.FinalFitted:
                    if (!ClaimTable(tableFree, diag, order)) return false;
                    if (!SpendLabor(ref laborRemaining, spec.PressMinutes, diag, order, "pressing"))
                    {
                        ReleaseTableClaim(tableFree, order);
                        return false;
                    }

                    SetStage(order, TailorOrderStage.Finished, dayIndex, $"pressed ({spec.PressMinutes}m)");
                    return true;

                case TailorOrderStage.Finished:
                    Deliver(order, spec, dayIndex, diag);
                    return true;

                default:
                    return false;
            }
        }

        private bool TryAdvanceMendStage(
            TailorGarmentOrder order, TailorGarmentSpec spec, int dayIndex,
            ref int laborRemaining, Dictionary<int, bool> tableFree, List<string> diag)
        {
            switch (order.Stage)
            {
                case TailorOrderStage.Ordered:
                    if (!SpendLabor(ref laborRemaining, spec.AssessMinutes, diag, order, "assessing")) return false;
                    SetStage(order, TailorOrderStage.Assessed, dayIndex, $"assessed ({spec.AssessMinutes}m)");
                    return true;

                case TailorOrderStage.Assessed:
                    if (!ClaimTable(tableFree, diag, order)) return false;
                    int mendNotionsHeld = 0;
                    foreach (var held in order.NotionsUsed)
                    {
                        if (held != null) mendNotionsHeld += Math.Max(0, held.UnitsTaken);
                    }

                    List<TailorClothDispenseLine> notionLines = new List<TailorClothDispenseLine>();
                    if (mendNotionsHeld < spec.NotionsUnits)
                    {
                        notionLines = clothStock.TryDispenseUnits(
                            TailorGarmentCatalog.NotionsItemId, spec.NotionsUnits - mendNotionsHeld, dayIndex, diag);
                        if (notionLines == null)
                        {
                            ReleaseTableClaim(tableFree, order);
                            diag.Add($"TailorShopRuntime: order {order.OrderId} (mending) refused — no notions on hand. Stays parked.");
                            return false;
                        }
                    }

                    if (!SpendLabor(ref laborRemaining, spec.MendMinutes, diag, order, "mending"))
                    {
                        ReleaseTableClaim(tableFree, order);
                        order.NotionsUsed.AddRange(notionLines);
                        return false;
                    }

                    order.NotionsUsed.AddRange(notionLines);
                    SetStage(order, TailorOrderStage.Mended, dayIndex, $"mended ({spec.MendMinutes}m)");
                    return true;

                case TailorOrderStage.Mended:
                    Deliver(order, spec, dayIndex, diag);
                    return true;

                default:
                    return false;
            }
        }

        private void Deliver(TailorGarmentOrder order, TailorGarmentSpec spec, int dayIndex, List<string> diag)
        {
            int pieceRate = TailorGarmentCatalog.GetPieceRateCents(order.GarmentId, pieceRates);
            int fittingFees = spec.IsMending ? 0 : pieceRates.FittingFeeCents * 2;

            var record = new TailorDeliveryRecord
            {
                OrderId = order.OrderId,
                CustomerPersonId = order.CustomerPersonId,
                GarmentId = order.GarmentId,
                DayIndex = dayIndex,
                PieceRateCents = pieceRate,
                FittingFeesCents = fittingFees,
            };
            record.ClothProvenance.AddRange(order.ClothInCustody);
            record.NotionsProvenance.AddRange(order.NotionsUsed);
            // The cloth is now IN the garment: custody lines move to the delivery
            // record, so the order never double-counts and the shelf never sees it again.
            order.ClothInCustody.Clear();
            order.NotionsUsed.Clear();

            deliveries.Add(record);
            SetStage(order, TailorOrderStage.Delivered, dayIndex,
                $"delivered — {record.TotalFeeCents}c (piece {pieceRate}c + fittings {fittingFees}c)");
            diag.Add($"TailorShopRuntime: order {order.OrderId} delivered — {spec.DisplayName} for {order.CustomerPersonId}, {record.TotalFeeCents}c (day {dayIndex}).");
        }

        private bool SpendLabor(ref int laborRemaining, int minutes, List<string> diag, TailorGarmentOrder order, string stageName)
        {
            if (minutes <= 0) return true;
            if (laborRemaining < minutes)
            {
                diag.Add($"TailorShopRuntime: order {order.OrderId} {stageName} needs {minutes}m, only {laborRemaining}m left today — stays parked for tomorrow.");
                return false;
            }

            laborRemaining -= minutes;
            return true;
        }

        /// <summary>
        /// Claims the lowest-index free table for a bench stage. Deterministic;
        /// returns false (with a diagnostic) when no table is free today.
        /// </summary>
        private bool ClaimTable(Dictionary<int, bool> tableFree, List<string> diag, TailorGarmentOrder order)
        {
            int best = int.MaxValue;
            foreach (var kvp in tableFree)
            {
                if (kvp.Value && kvp.Key < best) best = kvp.Key;
            }

            if (best == int.MaxValue)
            {
                diag.Add($"TailorShopRuntime: order {order.OrderId} waits on a free cutting table — all tables busy today. Stays parked.");
                return false;
            }

            tableFree[best] = false;
            pendingTableClaims[order.OrderId] = best;
            return true;
        }

        /// <summary>
        /// Releases the table this order claimed when its stage advance fails
        /// after the claim (labor shortfall or notions shortfall). The claim is
        /// recorded per order, so the release frees exactly that table.
        /// </summary>
        private void ReleaseTableClaim(Dictionary<int, bool> tableFree, TailorGarmentOrder order)
        {
            if (pendingTableClaims.TryGetValue(order.OrderId, out int claimed))
            {
                tableFree[claimed] = true;
                pendingTableClaims.Remove(order.OrderId);
            }
        }

        private void SetStage(TailorGarmentOrder order, TailorOrderStage stage, int dayIndex, string note)
        {
            order.Stage = stage;
            pendingTableClaims.Remove(order.OrderId);
            order.StageLog.Add($"day {dayIndex}: → {stage} ({note})");
        }

        private int CountActiveOrders()
        {
            int count = 0;
            foreach (var order in orders)
            {
                if (order.IsActive) count++;
            }

            return count;
        }

        private TailorGarmentOrder FindOrder(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return null;
            foreach (var order in orders)
            {
                if (string.Equals(order.OrderId, orderId, StringComparison.Ordinal)) return order;
            }

            return null;
        }

        /// <summary>
        /// Exposes the shop's cutting tables to the NX-1 equipment gate: each
        /// table is registered under "tailor-cutting-table-{n}", and the declared
        /// "tailor-cutting-table" id aliases the first READY table at call time
        /// (point-in-time resolution — the runtime stays the authority for
        /// per-stage table assignment). With no ready table the alias is left
        /// unregistered so the gate refuses honestly.
        /// </summary>
        public void PopulateWorkstations(
            BusinessWorkstations registry,
            WorkstationDefinition tableDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (registry == null)
            {
                diag.Add("TailorShopRuntime: no workstation registry — cutting tables not exposed to the equipment gate.");
                return;
            }

            TailorCuttingTable firstReady = null;
            foreach (var table in tables)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-tailor-cutting-table-{table.TableIndex}",
                    WorkstationId = $"{TailorGarmentCatalog.TailorCuttingTableStationId}-{table.TableIndex}",
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = table.Station.SpaceId,
                    ComponentAssetIds = new List<string>(table.Station.ComponentAssetIds),
                });

                if (firstReady == null)
                {
                    var reasons = new List<string>();
                    if (table.Station.EvaluateReady(tableDefinition, findComponent, reasons) == null)
                    {
                        firstReady = table;
                    }
                }
            }

            if (firstReady != null)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = firstReady.Station.InstanceId,
                    WorkstationId = TailorGarmentCatalog.TailorCuttingTableStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = firstReady.Station.SpaceId,
                    ComponentAssetIds = new List<string>(firstReady.Station.ComponentAssetIds),
                });
                diag.Add($"TailorShopRuntime: '{TailorGarmentCatalog.TailorCuttingTableStationId}' resolves to table {firstReady.TableIndex} (first ready, point-in-time).");
            }
            else
            {
                diag.Add("TailorShopRuntime: no ready table — the workstation alias is left unregistered; the gate refuses honestly.");
            }
        }

        /// <summary>Throughput readout: the table-and-labor problem, plainly stated.</summary>
        public string CoverageSummary()
        {
            return $"Tailor shop {businessInstanceId}: {tables.Count} cutting table(s), " +
                $"{CountActiveOrders()} open order(s), {deliveries.Count} delivered, " +
                $"{clothStock.UnitsOnHand(TailorGarmentCatalog.ClothItemId)} yd cloth on hand.";
        }

        public void CloseDay(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            diag.Add($"Tailor shop {businessInstanceId}: day {dayIndex} — {CoverageSummary()}");
        }

        #region Save / Load
        [Serializable]
        public sealed class TailorShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public TailorPieceRateSchedule PieceRates = new TailorPieceRateSchedule();
            public TailorClothStock.TailorClothStockSaveDto ClothStock = new TailorClothStock.TailorClothStockSaveDto();
            public List<TailorCuttingTable> Tables = new List<TailorCuttingTable>();
            public List<TailorGarmentOrder> Orders = new List<TailorGarmentOrder>();
            public List<TailorDeliveryRecord> Deliveries = new List<TailorDeliveryRecord>();
            public int OrderSeq;
        }

        public TailorShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new TailorShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                PieceRates = pieceRates,
                ClothStock = clothStock.CaptureSaveDto(),
                OrderSeq = orderSeq,
            };
            dto.Tables.AddRange(tables);
            // Only live orders rehydrate; delivered/cancelled orders are history
            // and their delivery records (below) are the persisted audit trail.
            foreach (var order in orders)
            {
                if (order != null && order.IsActive) dto.Orders.Add(order);
            }

            dto.Deliveries.AddRange(deliveries);
            return dto;
        }

        public void LoadFromSaveDto(TailorShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            clothStock.LoadFromSaveDto(dto.ClothStock);
            if (dto.PieceRates != null) pieceRates = dto.PieceRates;
            orderSeq = Math.Max(0, dto.OrderSeq);
            tables.Clear();
            if (dto.Tables != null)
            {
                foreach (var table in dto.Tables)
                {
                    if (table == null) continue;
                    if (table.Station == null) table.Station = new WorkstationInstance();
                    tables.Add(table);
                }
            }

            orders.Clear();
            if (dto.Orders != null)
            {
                foreach (var order in dto.Orders)
                {
                    if (order != null) orders.Add(order);
                }
            }

            deliveries.Clear();
            if (dto.Deliveries != null)
            {
                foreach (var delivery in dto.Deliveries)
                {
                    if (delivery != null) deliveries.Add(delivery);
                }
            }
        }
        #endregion
    }
}
