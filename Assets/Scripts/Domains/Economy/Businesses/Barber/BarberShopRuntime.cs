using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Barber
{
    /// <summary>
    /// W1B: one queued customer. Per-customer work, never a batch — each
    /// customer is served and billed individually.
    /// </summary>
    [Serializable]
    public sealed class BarberCustomer
    {
        public EntityId CustomerPersonId = EntityId.Invalid; // EntityKind.Person
        public string ServiceId = string.Empty; // barb.shave / barb.haircut / barb.bath
        public int RequestDayIndex;

        public BarberCustomer() { }
    }

    /// <summary>
    /// W1B: one completed service — chair time, fee, and the consumable lots
    /// consumed with full provenance. The caller settles these records as
    /// ordinary ledger outflows; money moves only through ledger authorities.
    /// </summary>
    [Serializable]
    public sealed class BarberServiceRecord
    {
        public EntityId CustomerPersonId = EntityId.Invalid;
        public string ServiceId = string.Empty;
        public int DayIndex;
        public int StartMinute; // minutes after the shop opened
        public int ChairMinutes;
        public int LaborMinutes;
        public int ChairIndex;
        /// <summary>
        /// D1B: tub index when the service ran on a bath tub (-1 when the
        /// service ran on a chair). Baths run on tubs once the shop has bath
        /// facilities installed; otherwise they use the W1B chair slot.
        /// </summary>
        public int TubIndex = -1;
        public int FeeCents;
        public List<BarberConsumableDispenseLine> ConsumablesUsed = new List<BarberConsumableDispenseLine>();
        public string Notes = string.Empty;

        public BarberServiceRecord() { }

        public int EndMinute => Math.Max(0, StartMinute) + Math.Max(0, ChairMinutes);

        /// <summary>D1B: true when this service ran on a bath tub rather than a chair.</summary>
        public bool ServedAtTub => TubIndex >= 0;
    }

    /// <summary>
    /// W1B: one barber chair. A chair IS a workstation instance
    /// (WorkstationId "barber-chair-station", Tech X §3.5): readiness derives
    /// from its component assets, never from a flag. The shop's concurrent
    /// customers can never exceed its ready chairs.
    /// </summary>
    [Serializable]
    public sealed class BarberChair
    {
        public int ChairIndex;
        public WorkstationInstance Station = new WorkstationInstance();

        public BarberChair() { }
    }

    /// <summary>
    /// W1B: the per-instance barbershop runtime. Holds the chair pool (chairs as
    /// workstations gating concurrent service), the consumable shelf (lots with
    /// provenance), the fee schedule, the waiting queue, and the chair-round
    /// scheduler that makes the throughput problem visible: each service
    /// occupies one ready chair for its duration while the barber's hands-on
    /// minutes run sequentially — a shave runs in one chair while another
    /// customer waits on the next.
    ///
    /// D1B: adds the bath-tub pool (Canon Part V scale column: bath tubs /
    /// hot-water capability) — a shop with tubs installed runs baths on
    /// dedicated tubs, freeing chairs; a shop with no tubs keeps the W1B
    /// chair-slot behavior. Also the chair-lease register (Canon §8.5/§8.6):
    /// leased chairs leave the shop's pool while the tenant holds them.
    ///
    /// Boundary: this runtime owns the chair/tub/consumable/queue/lease layer only. The
    /// existing generic service-visit resolution for BusinessType.Barber in
    /// SharedBusinessRuntimeManager is untouched — W1B routes around it the way
    /// W1A routes around the doctor's daily in-office resolution.
    /// </summary>
    public sealed class BarberShopRuntime
    {
        /// <summary>TUNING: the barber's hands-on minutes per day (calibration, Canon Part XV; mirrors the doctor's professional day).</summary>
        public const int ProfessionalMinutesPerDay = 600;

        /// <summary>TUNING: minutes per day each chair is available (12-hour shop day; calibration, Canon Part XV).</summary>
        public const int ShopOpenMinutesPerDay = 720;

        private readonly string businessInstanceId;
        private readonly BarberConsumableStock consumableStock = new BarberConsumableStock();
        private readonly List<BarberChair> chairs = new List<BarberChair>();
        private readonly List<BarberCustomer> waitingQueue = new List<BarberCustomer>();
        private readonly List<BarberServiceRecord> completedServices = new List<BarberServiceRecord>();
        private BarberFeeSchedule feeSchedule = new BarberFeeSchedule();

        /// <summary>
        /// D1B: the bath-tub pool. Empty = the shop has no bath facilities and
        /// baths run on the W1B chair slot. Installing tubs moves baths onto
        /// dedicated tubs (Canon Part V scale column: bath tubs/hot-water
        /// capability).
        /// </summary>
        private readonly List<BarberTub> tubs = new List<BarberTub>();

        /// <summary>
        /// D1B: the shop's hot-water capability — a named source on the
        /// premises (period: laundry stove / boiler / kettle). Null or empty =
        /// no hot water; tubs are not ready without it. Satisfies the bath
        /// station's "infrastructure: hot-water" support requirement.
        /// </summary>
        private string hotWaterSource;

        /// <summary>
        /// D1B: chair leases to independent operators (Canon §8.5/§8.6). A
        /// leased chair leaves the shop's service pool while the lease covers
        /// the day; the tenant runs their own services and keeps that revenue.
        /// </summary>
        private readonly List<BarberChairLease> leases = new List<BarberChairLease>();

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public BarberConsumableStock ConsumableStock => consumableStock;
        public IReadOnlyList<BarberChair> Chairs => chairs;
        public IReadOnlyList<BarberCustomer> WaitingQueue => waitingQueue;
        public IReadOnlyList<BarberServiceRecord> CompletedServices => completedServices;
        public BarberFeeSchedule FeeSchedule => feeSchedule;
        /// <summary>D1B: installed bath tubs (empty = no bath facilities).</summary>
        public IReadOnlyList<BarberTub> Tubs => tubs;
        /// <summary>D1B: recorded chair leases to independent operators.</summary>
        public IReadOnlyList<BarberChairLease> Leases => leases;
        /// <summary>D1B: the named hot-water source, or null/empty when the shop has none.</summary>
        public string HotWaterSource => hotWaterSource;
        /// <summary>D1B: true when the shop has declared a hot-water source.</summary>
        public bool HasHotWater => !string.IsNullOrWhiteSpace(hotWaterSource);

        public BarberShopRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void SetFeeSchedule(BarberFeeSchedule schedule)
        {
            feeSchedule = schedule ?? new BarberFeeSchedule();
        }

        /// <summary>
        /// Installs a chair: a workstation instance of "barber-chair-station"
        /// with its component assets named. Returns a rejection string, or null
        /// on success (the chair index is chairs.Count - 1 afterwards).
        /// </summary>
        public string AddChair(string spaceId, List<string> componentAssetIds, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(spaceId))
                return "BarberShopRuntime: a chair needs a functional space — a room alone never grants the workstation.";

            var chair = new BarberChair
            {
                ChairIndex = chairs.Count,
                Station = new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-barber-chair-{chairs.Count}",
                    WorkstationId = BarberServiceCatalog.BarberChairStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = spaceId,
                },
            };
            if (componentAssetIds != null)
            {
                foreach (string assetId in componentAssetIds)
                {
                    chair.Station.InstallComponent(assetId);
                }
            }

            chairs.Add(chair);
            diag.Add($"BarberShopRuntime: chair {chair.ChairIndex} installed in '{spaceId}' (workstation {chair.Station.InstanceId}).");
            return null;
        }

        /// <summary>
        /// D1B: installs a bath tub — a workstation instance of
        /// "barber-bath-station" with its component assets named. Canon Part V:
        /// bath tubs are the barber's scale column. Returns a rejection string,
        /// or null on success (the tub index is tubs.Count - 1 afterwards).
        /// </summary>
        public string AddTub(string spaceId, List<string> componentAssetIds, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(spaceId))
                return "BarberShopRuntime: a tub needs a functional space — a room alone never grants the workstation.";

            var tub = new BarberTub
            {
                TubIndex = tubs.Count,
                Station = new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-barber-tub-{tubs.Count}",
                    WorkstationId = BarberServiceCatalog.BarberBathStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = spaceId,
                },
            };
            if (componentAssetIds != null)
            {
                foreach (string assetId in componentAssetIds)
                {
                    tub.Station.InstallComponent(assetId);
                }
            }

            tubs.Add(tub);
            diag.Add($"BarberShopRuntime: tub {tub.TubIndex} installed in '{spaceId}' (workstation {tub.Station.InstanceId}).");
            return null;
        }

        /// <summary>
        /// D1B: declares the shop's hot-water source — the named asset or setup
        /// on the premises that gives the tubs hot water (period: a laundry
        /// stove / boiler / kettle). Satisfies the bath station's
        /// "infrastructure: hot-water" support requirement. Returns a rejection
        /// string, or null on success.
        /// </summary>
        public string DeclareHotWaterSource(string sourceDescription, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(sourceDescription))
                return "BarberShopRuntime: the hot-water source must be named — an unnamed capability heats nothing.";

            hotWaterSource = sourceDescription.Trim();
            diag.Add($"BarberShopRuntime: hot-water capability declared — '{hotWaterSource}'.");
            return null;
        }

        /// <summary>D1B: removes the hot-water capability (the source is gone or cold).</summary>
        public void ClearHotWaterSource(List<string> diag)
        {
            diag = diag ?? diagnostics;
            hotWaterSource = null;
            diag.Add("BarberShopRuntime: hot-water capability cleared — tubs are not ready without hot water.");
        }

        /// <summary>
        /// D1B: true when the tub's station evaluates ready AND the shop has
        /// hot water. A tub with a sound basin but no hot water is not a bath.
        /// </summary>
        private bool TubIsReady(
            BarberTub tub,
            WorkstationDefinition bathDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> reasons)
        {
            reasons = reasons ?? new List<string>();
            if (tub == null || tub.Station == null)
            {
                reasons.Add("tub record missing — never assumed ready.");
                return false;
            }

            string notReady = tub.Station.EvaluateReady(bathDefinition, findComponent, reasons);
            if (notReady != null) return false;

            if (!HasHotWater)
            {
                reasons.Add($"tub {tub.TubIndex}: basin is sound but the shop has no hot-water source — " +
                    "baths need hot-water capability (Canon Part V scale column).");
                return false;
            }

            return true;
        }

        /// <summary>
        /// D1B: counts tubs whose station evaluates ready with hot water.
        /// Loud per-tub diagnostics — an unready tub is named, never silently
        /// skipped.
        /// </summary>
        public int CountReadyTubs(
            WorkstationDefinition bathDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int ready = 0;
            foreach (var tub in tubs)
            {
                var reasons = new List<string>();
                if (TubIsReady(tub, bathDefinition, findComponent, reasons))
                {
                    ready++;
                }
                else
                {
                    diag.Add($"BarberShopRuntime: tub {tub.TubIndex} not ready — {string.Join("; ", reasons)}");
                }
            }

            return ready;
        }

        /// <summary>D1B: throughput readout for the bath facilities.</summary>
        public string BathFacilitiesSummary()
        {
            return $"Barber shop {businessInstanceId}: {tubs.Count} tub(s) installed, " +
                (HasHotWater ? $"hot water: '{hotWaterSource}'." : "no hot-water source declared.");
        }

        /// <summary>
        /// D1B: leases a chair to an independent barber operator (Canon §8.5).
        /// The operator keeps their customer revenue and pays rent; the chair
        /// leaves the shop's service pool while the lease covers the day
        /// (Canon §8.6: the tenant remains an independent business — the shop
        /// never schedules the tenant's customers). Returns a rejection string,
        /// or null on success.
        /// </summary>
        public string LeaseChair(BarberChairLease lease, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lease == null) return "BarberShopRuntime: null lease — no chair leased.";
            if (lease.ChairIndex < 0 || lease.ChairIndex >= chairs.Count)
                return $"BarberShopRuntime: lease refused — chair {lease.ChairIndex} does not exist.";
            if (!lease.OperatorPersonId.IsValid || lease.OperatorPersonId.Kind != EntityKind.Person)
                return "BarberShopRuntime: lease refused — the operator must be a valid person; no anonymous tenancy.";
            if (lease.RentCentsPerDay < 0)
                return "BarberShopRuntime: lease refused — rent cannot be negative.";
            if (lease.EndDayIndexExclusive <= lease.StartDayIndex)
                return "BarberShopRuntime: lease refused — the end day must be after the start day.";

            foreach (var existing in leases)
            {
                if (existing.ChairIndex == lease.ChairIndex && existing.Overlaps(lease))
                    return $"BarberShopRuntime: lease refused — chair {lease.ChairIndex} is already leased " +
                        $"[{existing.StartDayIndex}, {existing.EndDayIndexExclusive}) under '{existing.LeaseId}'.";
            }

            if (string.IsNullOrWhiteSpace(lease.LeaseId))
            {
                lease.LeaseId = $"{businessInstanceId}-lease-{leases.Count}";
            }

            leases.Add(lease);
            diag.Add($"BarberShopRuntime: chair {lease.ChairIndex} leased to operator {lease.OperatorPersonId} " +
                $"[{lease.StartDayIndex}, {lease.EndDayIndexExclusive}) at {lease.RentCentsPerDay}c/day (lease '{lease.LeaseId}'). " +
                "The operator keeps their customer revenue and pays rent (Canon §8.5).");
            return null;
        }

        /// <summary>
        /// D1B: ends a chair's lease early. The lease stops covering days from
        /// <paramref name="dayIndex"/> on; the chair returns to the shop's pool
        /// the same day. Returns a rejection string, or null on success.
        /// </summary>
        public string EndLease(int chairIndex, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            BarberChairLease active = ActiveLease(chairIndex, dayIndex);
            if (active == null)
                return $"BarberShopRuntime: no active lease on chair {chairIndex} at day {dayIndex} — nothing to end.";

            if (dayIndex <= active.StartDayIndex)
            {
                leases.Remove(active);
                diag.Add($"BarberShopRuntime: lease '{active.LeaseId}' on chair {chairIndex} ended before it began — removed.");
            }
            else
            {
                active.EndDayIndexExclusive = dayIndex;
                diag.Add($"BarberShopRuntime: lease '{active.LeaseId}' on chair {chairIndex} ended at day {dayIndex} — the chair is back in the shop's pool.");
            }

            return null;
        }

        /// <summary>
        /// D1B: the lease covering a chair on a given day, or null when the
        /// chair is the shop's own that day.
        /// </summary>
        public BarberChairLease ActiveLease(int chairIndex, int dayIndex)
        {
            foreach (var lease in leases)
            {
                if (lease.ChairIndex == chairIndex && lease.CoversDay(dayIndex))
                    return lease;
            }

            return null;
        }

        /// <summary>
        /// D1B: accrues one day's rent for every lease covering
        /// <paramref name="dayIndex"/>. Returns the obligations as DATA — the
        /// caller settles them as ordinary ledger transfers; money moves only
        /// through ledger authorities.
        /// </summary>
        public List<BarberChairRentObligation> AccrueDayRent(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var obligations = new List<BarberChairRentObligation>();
            foreach (var lease in leases)
            {
                if (!lease.CoversDay(dayIndex)) continue;
                obligations.Add(new BarberChairRentObligation
                {
                    LeaseId = lease.LeaseId,
                    LandlordBusinessInstanceId = businessInstanceId,
                    ChairIndex = lease.ChairIndex,
                    OperatorPersonId = lease.OperatorPersonId,
                    DayIndex = dayIndex,
                    RentCents = lease.RentCentsPerDay,
                });
                diag.Add($"BarberShopRuntime: rent accrued — lease '{lease.LeaseId}', chair {lease.ChairIndex}, " +
                    $"operator {lease.OperatorPersonId}, day {dayIndex}: {lease.RentCentsPerDay}c.");
            }

            if (obligations.Count == 0)
            {
                diag.Add($"BarberShopRuntime: no active chair leases at day {dayIndex} — no rent accrued.");
            }

            return obligations;
        }

        /// <summary>
        /// Queues a customer for a service. Returns a rejection string, or null on success.
        /// </summary>
        public string QueueCustomer(BarberCustomer customer, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (customer == null) return "BarberShopRuntime: null customer — nobody queued.";
            if (!customer.CustomerPersonId.IsValid || customer.CustomerPersonId.Kind != EntityKind.Person)
                return "BarberShopRuntime: the customer must be a valid person — no anonymous or batch service.";
            if (!BarberServiceCatalog.IsKnownService(customer.ServiceId))
                return $"BarberShopRuntime: unknown service '{customer.ServiceId}' — only shave, haircut, and bath are offered.";

            waitingQueue.Add(customer);
            diag.Add($"BarberShopRuntime: customer {customer.CustomerPersonId} queued for {customer.ServiceId} (day {customer.RequestDayIndex}).");
            return null;
        }

        /// <summary>
        /// Counts chairs whose workstation instance evaluates ready against the
        /// catalog definition. Loud per-chair diagnostics — an unready chair is
        /// named, never silently skipped.
        ///
        /// D1B note: this overload counts physical readiness and does not
        /// consult the lease calendar — a leased chair is still a sound chair.
        /// Use the day-aware overload for the operational count.
        /// </summary>
        public int CountReadyChairs(
            WorkstationDefinition chairDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int ready = 0;
            foreach (var chair in chairs)
            {
                var reasons = new List<string>();
                string notReady = chair.Station.EvaluateReady(chairDefinition, findComponent, reasons);
                if (notReady == null)
                {
                    ready++;
                }
                else
                {
                    diag.Add($"BarberShopRuntime: chair {chair.ChairIndex} not ready — {notReady}");
                }
            }

            return ready;
        }

        /// <summary>
        /// D1B: the operational ready-chair count for a given day — chairs
        /// under an active independent-operator lease are the tenant's station,
        /// not the shop's, and are excluded (Canon §8.5/§8.6). Loud
        /// per-chair diagnostics.
        /// </summary>
        public int CountReadyChairs(
            WorkstationDefinition chairDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int ready = 0;
            foreach (var chair in chairs)
            {
                BarberChairLease lease = ActiveLease(chair.ChairIndex, dayIndex);
                if (lease != null)
                {
                    diag.Add($"BarberShopRuntime: chair {chair.ChairIndex} leased to operator {lease.OperatorPersonId} " +
                        $"(lease '{lease.LeaseId}') — not in the shop's pool at day {dayIndex}.");
                    continue;
                }

                var reasons = new List<string>();
                string notReady = chair.Station.EvaluateReady(chairDefinition, findComponent, reasons);
                if (notReady == null)
                {
                    ready++;
                }
                else
                {
                    diag.Add($"BarberShopRuntime: chair {chair.ChairIndex} not ready — {notReady}");
                }
            }

            return ready;
        }

        /// <summary>
        /// Runs one shop day: schedules queued customers onto ready chairs FIFO.
        /// Each service needs a free ready chair for its full duration, the
        /// barber's remaining hands-on minutes, and its soap/linen on the shelf.
        /// Consumable shortfalls refuse LOUDLY and the customer stays queued for
        /// tomorrow — never served on conjured stock, never silently dropped.
        /// Returns the number of completed services.
        ///
        /// D1B: chairs under an active independent-operator lease are excluded
        /// from the shop's pool (Canon §8.5/§8.6). When the shop has bath tubs
        /// installed, baths run on ready tubs with hot water instead of chairs
        /// (Canon Part V scale column); a shop with no tubs serves baths the
        /// W1B way, on a chair slot.
        /// </summary>
        public int ServeDay(
            int dayIndex,
            bool barberKitUsable,
            WorkstationDefinition chairDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            return ServeDay(dayIndex, barberKitUsable, chairDefinition,
                WorkstationCatalog.BarberBathStation, findComponent, diag);
        }

        /// <summary>
        /// D1B: ServeDay with an injectable bath-station definition (test seam).
        /// Behavior is identical to the 5-argument overload; the definition is
        /// only used to evaluate tub readiness when the shop has tubs.
        /// </summary>
        public int ServeDay(
            int dayIndex,
            bool barberKitUsable,
            WorkstationDefinition chairDefinition,
            WorkstationDefinition bathDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int completed = 0;

            if (!barberKitUsable)
            {
                diag.Add($"BarberShopRuntime: no usable barber's kit — the shop cannot work today (NX-1 teeth gate). {waitingQueue.Count} customer(s) still waiting.");
                return 0;
            }

            // Per-chair free-at minutes within the shop day. Only ready chairs
            // enter the pool — a chair under an active independent-operator
            // lease is the tenant's station, not the shop's (Canon §8.5/§8.6).
            var chairFreeAt = new Dictionary<int, int>();
            foreach (var chair in chairs)
            {
                BarberChairLease lease = ActiveLease(chair.ChairIndex, dayIndex);
                if (lease != null)
                {
                    diag.Add($"BarberShopRuntime: chair {chair.ChairIndex} is leased to operator {lease.OperatorPersonId} " +
                        $"(lease '{lease.LeaseId}') — not in the shop's pool today.");
                    continue;
                }

                var reasons = new List<string>();
                if (chair.Station.EvaluateReady(chairDefinition, findComponent, reasons) == null)
                {
                    chairFreeAt[chair.ChairIndex] = 0;
                }
            }

            // Per-tub free-at minutes. Only when the shop has installed bath
            // facilities: tubs are the Canon Part V scale column, so a tub
            // shop runs baths on tubs; a shop with no tubs keeps the W1B
            // chair-slot behavior.
            bool bathsUseTubs = tubs.Count > 0;
            var tubFreeAt = new Dictionary<int, int>();
            if (bathsUseTubs)
            {
                bathDefinition = bathDefinition ?? WorkstationCatalog.BarberBathStation;
                foreach (var tub in tubs)
                {
                    var reasons = new List<string>();
                    if (TubIsReady(tub, bathDefinition, findComponent, reasons))
                    {
                        tubFreeAt[tub.TubIndex] = 0;
                    }
                    else
                    {
                        diag.Add($"BarberShopRuntime: tub {tub.TubIndex} not ready — {string.Join("; ", reasons)}");
                    }
                }
            }

            if (chairFreeAt.Count == 0 && tubFreeAt.Count == 0)
            {
                diag.Add($"BarberShopRuntime: no ready chair and no ready tub — {waitingQueue.Count} customer(s) still waiting.");
                return 0;
            }

            int barberFreeAt = 0;
            var snapshot = new List<BarberCustomer>(waitingQueue);

            foreach (var customer in snapshot)
            {
                BarberServiceSpec spec = BarberServiceCatalog.GetSpec(customer.ServiceId);
                if (string.IsNullOrEmpty(spec.ServiceId))
                {
                    diag.Add($"BarberShopRuntime: customer {customer.CustomerPersonId} requests unknown service '{customer.ServiceId}' — stays queued, never guessed.");
                    continue;
                }

                // Consumables first: no service on conjured stock.
                if (spec.SoapUnits > 0 && consumableStock.UnitsOnHand(BarberServiceCatalog.SoapItemId) < spec.SoapUnits)
                {
                    diag.Add($"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} refused — no soap on hand. Stays queued.");
                    continue;
                }

                if (spec.LinenUnits > 0 && consumableStock.UnitsOnHand(BarberServiceCatalog.LinenItemId) < spec.LinenUnits)
                {
                    diag.Add($"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} refused — no linen on hand. Stays queued.");
                    continue;
                }

                bool wantsBath = string.Equals(customer.ServiceId, BarberServiceCatalog.BathId, StringComparison.Ordinal);
                bool useTub = wantsBath && bathsUseTubs;

                // Earliest-free ready slot wins; ties go to the longest-idle slot
                // (index order as the final tiebreak — fully deterministic).
                // The barber's hands run sequentially across chairs and tubs:
                // a bath soaks in a tub while the barber shaves in a chair.
                int bestSlot = -1;
                int bestStart = int.MaxValue;
                int bestFreeAt = int.MaxValue;
                if (useTub)
                {
                    foreach (var tub in tubs)
                    {
                        if (!tubFreeAt.TryGetValue(tub.TubIndex, out int freeAt)) continue; // not ready
                        int start = Math.Max(freeAt, barberFreeAt);
                        if (start < bestStart || (start == bestStart && freeAt < bestFreeAt))
                        {
                            bestStart = start;
                            bestFreeAt = freeAt;
                            bestSlot = tub.TubIndex;
                        }
                    }
                }
                else
                {
                    foreach (var chair in chairs)
                    {
                        if (!chairFreeAt.TryGetValue(chair.ChairIndex, out int freeAt)) continue; // not ready or leased
                        int start = Math.Max(freeAt, barberFreeAt);
                        if (start < bestStart || (start == bestStart && freeAt < bestFreeAt))
                        {
                            bestStart = start;
                            bestFreeAt = freeAt;
                            bestSlot = chair.ChairIndex;
                        }
                    }
                }

                if (bestSlot < 0)
                {
                    diag.Add(useTub
                        ? $"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} refused — no ready tub with hot water. Stays queued."
                        : $"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} refused — no ready chair in the shop's pool. Stays queued.");
                    continue;
                }

                int end = bestStart + spec.ChairMinutes;
                int barberEnd = bestStart + spec.LaborMinutes;
                if (end > ShopOpenMinutesPerDay || barberEnd > ProfessionalMinutesPerDay)
                {
                    diag.Add($"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} does not fit today " +
                        $"({(useTub ? "tub" : "chair")} to {end}m, barber to {barberEnd}m) — stays queued.");
                    continue;
                }

                // Dispense AFTER the fit check so failed fits never burn stock.
                var record = new BarberServiceRecord
                {
                    CustomerPersonId = customer.CustomerPersonId,
                    ServiceId = spec.ServiceId,
                    DayIndex = dayIndex,
                    StartMinute = bestStart,
                    ChairMinutes = spec.ChairMinutes,
                    LaborMinutes = spec.LaborMinutes,
                    ChairIndex = useTub ? -1 : bestSlot,
                    TubIndex = useTub ? bestSlot : -1,
                    FeeCents = BarberServiceCatalog.GetFeeCents(spec.ServiceId, feeSchedule),
                };

                bool dispensed = true;
                if (spec.SoapUnits > 0)
                {
                    var lines = consumableStock.TryDispenseUnits(BarberServiceCatalog.SoapItemId, spec.SoapUnits, dayIndex, diag);
                    if (lines == null) dispensed = false;
                    else record.ConsumablesUsed.AddRange(lines);
                }

                if (dispensed && spec.LinenUnits > 0)
                {
                    var lines = consumableStock.TryDispenseUnits(BarberServiceCatalog.LinenItemId, spec.LinenUnits, dayIndex, diag);
                    if (lines == null) dispensed = false;
                    else record.ConsumablesUsed.AddRange(lines);
                }

                if (!dispensed)
                {
                    diag.Add($"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} refused at dispense — stock shortfall is loud, never papered over. Stays queued.");
                    continue;
                }

                if (useTub) tubFreeAt[bestSlot] = end;
                else chairFreeAt[bestSlot] = end;
                barberFreeAt = barberEnd;
                waitingQueue.Remove(customer);
                completedServices.Add(record);
                completed++;
                diag.Add($"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} done — " +
                    $"{(useTub ? "tub" : "chair")} {bestSlot}, {bestStart}-{end}m, {record.FeeCents}c (day {dayIndex}).");
            }

            return completed;
        }

        /// <summary>
        /// Exposes the shop's chairs to the NX-1 equipment gate: each chair is
        /// registered under "barber-chair-station-{n}", and the declared
        /// "barber-chair-station" id aliases the first READY chair at call time
        /// (point-in-time resolution — the runtime stays the authority for
        /// per-service chair assignment). With no ready chair the alias is left
        /// unregistered so the gate refuses honestly.
        /// </summary>
        public void PopulateWorkstations(
            BusinessWorkstations registry,
            WorkstationDefinition chairDefinition,
            Func<string, WorkstationComponentView?> findComponent,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (registry == null)
            {
                diag.Add("BarberShopRuntime: no workstation registry — chairs not exposed to the equipment gate.");
                return;
            }

            BarberChair firstReady = null;
            foreach (var chair in chairs)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-barber-chair-station-{chair.ChairIndex}",
                    WorkstationId = $"{BarberServiceCatalog.BarberChairStationId}-{chair.ChairIndex}",
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = chair.Station.SpaceId,
                    ComponentAssetIds = new List<string>(chair.Station.ComponentAssetIds),
                });

                if (firstReady == null)
                {
                    var reasons = new List<string>();
                    if (chair.Station.EvaluateReady(chairDefinition, findComponent, reasons) == null)
                    {
                        firstReady = chair;
                    }
                }
            }

            if (firstReady != null)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = firstReady.Station.InstanceId,
                    WorkstationId = BarberServiceCatalog.BarberChairStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = firstReady.Station.SpaceId,
                    ComponentAssetIds = new List<string>(firstReady.Station.ComponentAssetIds),
                });
                diag.Add($"BarberShopRuntime: '{BarberServiceCatalog.BarberChairStationId}' resolves to chair {firstReady.ChairIndex} (first ready, point-in-time).");
            }
            else
            {
                diag.Add("BarberShopRuntime: no ready chair — the workstation alias is left unregistered; the gate refuses honestly.");
            }

            // D1B: tubs are exposed the same way — each tub under
            // "barber-bath-station-{n}", and the declared "barber-bath-station"
            // id aliases the first tub that is ready with hot water at call
            // time. With no ready tub the alias is left unregistered so the
            // gate refuses honestly.
            BarberTub firstReadyTub = null;
            foreach (var tub in tubs)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = $"{businessInstanceId}-barber-bath-station-{tub.TubIndex}",
                    WorkstationId = $"{BarberServiceCatalog.BarberBathStationId}-{tub.TubIndex}",
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = tub.Station.SpaceId,
                    ComponentAssetIds = new List<string>(tub.Station.ComponentAssetIds),
                });

                if (firstReadyTub == null)
                {
                    var reasons = new List<string>();
                    if (TubIsReady(tub, WorkstationCatalog.BarberBathStation, findComponent, reasons))
                    {
                        firstReadyTub = tub;
                    }
                }
            }

            if (firstReadyTub != null)
            {
                registry.RegisterInstance(new WorkstationInstance
                {
                    InstanceId = firstReadyTub.Station.InstanceId,
                    WorkstationId = BarberServiceCatalog.BarberBathStationId,
                    BusinessInstanceId = businessInstanceId,
                    SpaceId = firstReadyTub.Station.SpaceId,
                    ComponentAssetIds = new List<string>(firstReadyTub.Station.ComponentAssetIds),
                });
                diag.Add($"BarberShopRuntime: '{BarberServiceCatalog.BarberBathStationId}' resolves to tub {firstReadyTub.TubIndex} (first ready with hot water, point-in-time).");
            }
            else if (tubs.Count > 0)
            {
                diag.Add("BarberShopRuntime: no ready tub — the bath-station alias is left unregistered; the gate refuses honestly.");
            }
        }

        /// <summary>Throughput readout: the chair-and-coverage problem, plainly stated.</summary>
        public string CoverageSummary()
        {
            return $"Barber shop {businessInstanceId}: {chairs.Count} chair(s), {tubs.Count} tub(s), " +
                $"{leases.Count} chair lease(s), {completedServices.Count} service(s) completed, " +
                $"{waitingQueue.Count} customer(s) still waiting.";
        }

        public void CloseDay(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            diag.Add($"Barber shop {businessInstanceId}: day {dayIndex} — {CoverageSummary()}");
        }

        #region Save / Load
        [Serializable]
        public sealed class BarberShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public BarberFeeSchedule FeeSchedule = new BarberFeeSchedule();
            public BarberConsumableStock.BarberConsumableStockSaveDto ConsumableStock = new BarberConsumableStock.BarberConsumableStockSaveDto();
            public List<BarberChair> Chairs = new List<BarberChair>();
            public List<BarberCustomer> WaitingQueue = new List<BarberCustomer>();
            /// <summary>D1B: installed bath tubs.</summary>
            public List<BarberTub> Tubs = new List<BarberTub>();
            /// <summary>D1B: the named hot-water source (null/empty = none).</summary>
            public string HotWaterSource;
            /// <summary>D1B: chair leases to independent operators.</summary>
            public List<BarberChairLease> Leases = new List<BarberChairLease>();
        }

        public BarberShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new BarberShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                FeeSchedule = feeSchedule,
                ConsumableStock = consumableStock.CaptureSaveDto(),
                HotWaterSource = hotWaterSource,
            };
            dto.Chairs.AddRange(chairs);
            dto.WaitingQueue.AddRange(waitingQueue);
            dto.Tubs.AddRange(tubs);
            dto.Leases.AddRange(leases);
            return dto;
        }

        public void LoadFromSaveDto(BarberShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            consumableStock.LoadFromSaveDto(dto.ConsumableStock);
            if (dto.FeeSchedule != null) feeSchedule = dto.FeeSchedule;
            hotWaterSource = dto.HotWaterSource;
            chairs.Clear();
            if (dto.Chairs != null)
            {
                foreach (var chair in dto.Chairs)
                {
                    if (chair == null) continue;
                    if (chair.Station == null) chair.Station = new WorkstationInstance();
                    chairs.Add(chair);
                }
            }

            waitingQueue.Clear();
            if (dto.WaitingQueue != null)
            {
                foreach (var customer in dto.WaitingQueue)
                {
                    if (customer != null) waitingQueue.Add(customer);
                }
            }

            tubs.Clear();
            if (dto.Tubs != null)
            {
                foreach (var tub in dto.Tubs)
                {
                    if (tub == null) continue;
                    if (tub.Station == null) tub.Station = new WorkstationInstance();
                    tubs.Add(tub);
                }
            }

            leases.Clear();
            if (dto.Leases != null)
            {
                foreach (var lease in dto.Leases)
                {
                    if (lease != null) leases.Add(lease);
                }
            }
        }
        #endregion
    }
}
