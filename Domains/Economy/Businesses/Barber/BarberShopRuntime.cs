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
        public int FeeCents;
        public List<BarberConsumableDispenseLine> ConsumablesUsed = new List<BarberConsumableDispenseLine>();
        public string Notes = string.Empty;

        public BarberServiceRecord() { }

        public int EndMinute => Math.Max(0, StartMinute) + Math.Max(0, ChairMinutes);
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
    /// minutes run sequentially — a bath soaks in one chair while the barber
    /// shaves in another.
    ///
    /// Boundary: this runtime owns the chair/consumable/queue layer only. The
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

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public BarberConsumableStock ConsumableStock => consumableStock;
        public IReadOnlyList<BarberChair> Chairs => chairs;
        public IReadOnlyList<BarberCustomer> WaitingQueue => waitingQueue;
        public IReadOnlyList<BarberServiceRecord> CompletedServices => completedServices;
        public BarberFeeSchedule FeeSchedule => feeSchedule;

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
        /// Runs one shop day: schedules queued customers onto ready chairs FIFO.
        /// Each service needs a free ready chair for its full duration, the
        /// barber's remaining hands-on minutes, and its soap/linen on the shelf.
        /// Consumable shortfalls refuse LOUDLY and the customer stays queued for
        /// tomorrow — never served on conjured stock, never silently dropped.
        /// Returns the number of completed services.
        /// </summary>
        public int ServeDay(
            int dayIndex,
            bool barberKitUsable,
            WorkstationDefinition chairDefinition,
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

            // Per-chair free-at minutes within the shop day; only ready chairs enter the pool.
            var chairFreeAt = new Dictionary<int, int>();
            foreach (var chair in chairs)
            {
                var reasons = new List<string>();
                if (chair.Station.EvaluateReady(chairDefinition, findComponent, reasons) == null)
                {
                    chairFreeAt[chair.ChairIndex] = 0;
                }
            }

            if (chairFreeAt.Count == 0)
            {
                diag.Add($"BarberShopRuntime: no ready chair — {waitingQueue.Count} customer(s) still waiting.");
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

                // Earliest-free ready chair wins; ties go to the longest-idle chair
                // (index order as the final tiebreak — fully deterministic).
                // The barber's hands run sequentially across chairs.
                BarberChair bestChair = null;
                int bestStart = int.MaxValue;
                int bestFreeAt = int.MaxValue;
                foreach (var chair in chairs)
                {
                    if (!chairFreeAt.TryGetValue(chair.ChairIndex, out int freeAt)) continue; // not ready
                    int start = Math.Max(freeAt, barberFreeAt);
                    if (start < bestStart || (start == bestStart && freeAt < bestFreeAt))
                    {
                        bestStart = start;
                        bestFreeAt = freeAt;
                        bestChair = chair;
                    }
                }

                if (bestChair == null) continue; // no ready chair — cannot happen here, but never assume

                int end = bestStart + spec.ChairMinutes;
                int barberEnd = bestStart + spec.LaborMinutes;
                if (end > ShopOpenMinutesPerDay || barberEnd > ProfessionalMinutesPerDay)
                {
                    diag.Add($"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} does not fit today (chair to {end}m, barber to {barberEnd}m) — stays queued.");
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
                    ChairIndex = bestChair.ChairIndex,
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

                chairFreeAt[bestChair.ChairIndex] = end;
                barberFreeAt = barberEnd;
                waitingQueue.Remove(customer);
                completedServices.Add(record);
                completed++;
                diag.Add($"BarberShopRuntime: {spec.DisplayName} for {customer.CustomerPersonId} done — chair {bestChair.ChairIndex}, {bestStart}-{end}m, {record.FeeCents}c (day {dayIndex}).");
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
        }

        /// <summary>Throughput readout: the chair-and-coverage problem, plainly stated.</summary>
        public string CoverageSummary()
        {
            return $"Barber shop {businessInstanceId}: {chairs.Count} chair(s), {completedServices.Count} service(s) completed, " +
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
        }

        public BarberShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new BarberShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                FeeSchedule = feeSchedule,
                ConsumableStock = consumableStock.CaptureSaveDto(),
            };
            dto.Chairs.AddRange(chairs);
            dto.WaitingQueue.AddRange(waitingQueue);
            return dto;
        }

        public void LoadFromSaveDto(BarberShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            consumableStock.LoadFromSaveDto(dto.ConsumableStock);
            if (dto.FeeSchedule != null) feeSchedule = dto.FeeSchedule;
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
        }
        #endregion
    }
}
