// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice B: retail service work.
//
// RetailServiceDesk extends GenericBusiness retail with REAL service work:
// every customer is served through real TaskAuthority WorkTasks (retrieve
// stock -> measure/wrap -> receive payment), shelves are restocked through
// real restock tasks, and deliveries are accepted through real
// accept-delivery tasks. Service capacity comes from real worker
// availability (TaskAuthority assignments + WorkTimeBudgetStore budgets).
// Understaffing produces waiting customers and missed sales — never
// fabricated customers: only real Person entities may arrive, and every
// sale settles through the ONE cash writer (HouseholdLedger) via the shared
// GenericRetailSaleAuthority seam.
//
// Any BusinessInstanceState with inventory AND an assigned premises may
// retail — this is not limited to GeneralStore.

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Population;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Economy.Generic
{
    /// <summary>Phases within one customer service episode (one serve WorkTask).</summary>
    public enum RetailServicePhase
    {
        RetrievingStock = 0,
        MeasuringWrapping = 1,
        ReceivingPayment = 2,
        Done = 3,
    }

    /// <summary>Lifecycle of one customer's visit to the service desk.</summary>
    public enum CustomerVisitStatus
    {
        Waiting = 0,
        BeingServed = 1,
        Served = 2,
        LeftImpatient = 3,
        SaleRefused = 4,
    }

    /// <summary>One customer's visit. The customer is ALWAYS a real Person entity —
    /// the desk never fabricates customers.</summary>
    [Serializable]
    public sealed class CustomerVisit
    {
        public string VisitId = string.Empty;
        public EntityId Customer = EntityId.Invalid;
        public string ProductId = string.Empty;
        public int Quantity;
        public float ArrivedGameSeconds;
        public float PatienceGameSeconds = 1800f;
        public CustomerVisitStatus Status = CustomerVisitStatus.Waiting;
        public EntityId ServiceTaskId = EntityId.Invalid;
        public EntityId ServedBy = EntityId.Invalid;
        public RetailServicePhase Phase = RetailServicePhase.RetrievingStock;
        public string RefusalReason = string.Empty;
    }

    /// <summary>A shelf-restock job: real labor moving sale-ready stock onto shelves.</summary>
    [Serializable]
    public sealed class RetailRestockJob
    {
        public string JobId = string.Empty;
        public string ProductId = string.Empty;
        public int Quantity;
        public EntityId TaskId = EntityId.Invalid;
        public bool Done;
    }

    /// <summary>Accepting a real inbound delivery (from a real shipment — never teleported goods).</summary>
    [Serializable]
    public sealed class RetailDeliveryAcceptance
    {
        public string DeliveryId = string.Empty;
        public string ShipmentId = string.Empty;
        public string ProductId = string.Empty;
        public int Quantity;
        public int UnitCostCents;
        public string SourceReference = string.Empty;
        public EntityId TaskId = EntityId.Invalid;
        public bool Done;
    }

    /// <summary>A sale that did not happen, with the honest reason.</summary>
    [Serializable]
    public sealed class MissedSaleRecord
    {
        public string VisitId = string.Empty;
        public EntityId Customer = EntityId.Invalid;
        public string ProductId = string.Empty;
        public int Quantity;
        public string Reason = string.Empty;
        public float GameSeconds;
    }

    [Serializable]
    public sealed class ShelfUnitsEntry
    {
        public string ProductId = string.Empty;
        public float Units;
    }

    /// <summary>Save DTO for the service desk. Owned by RetailServiceDesk (save-DTO
    /// methods live inside the owning runtime class).</summary>
    [Serializable]
    public sealed class RetailServiceDeskSaveDto
    {
        public string BusinessId = string.Empty;
        public List<EntityId> Clerks = new List<EntityId>();
        public List<CustomerVisit> Visits = new List<CustomerVisit>();
        public List<ShelfUnitsEntry> ShelfUnits = new List<ShelfUnitsEntry>();
        public List<RetailRestockJob> RestockJobs = new List<RetailRestockJob>();
        public List<RetailDeliveryAcceptance> Deliveries = new List<RetailDeliveryAcceptance>();
        public List<MissedSaleRecord> MissedSales = new List<MissedSaleRecord>();
        public int Sequence;
        public int TotalSalesCents;
        public int TotalUnitsSold;
    }

    /// <summary>Runtime hooks the desk needs. Supplied by the caller each tick.</summary>
    public sealed class RetailServiceHooks
    {
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public Func<float> NowGameSeconds;
        public int DayIndex;
        /// <summary>Buyer household ledgers by Person. Required for settlement.</summary>
        public Func<EntityId, HouseholdLedger> LedgerForPerson;
        /// <summary>The business household's ledger — the ONE cash writer for revenue.</summary>
        public HouseholdLedger BusinessLedger;
        public string BusinessDisplayName = string.Empty;
        public List<string> Diagnostics;
    }

    /// <summary>
    /// Retail service desk for one business. Drives real service WorkTasks through
    /// TaskAuthority, real worker availability through WorkTimeBudgetStore, and real
    /// cash settlement through HouseholdLedger (via GenericRetailSaleAuthority).
    /// </summary>
    public sealed class RetailServiceDesk
    {
        // Service episode timing (tunable per desk).
        public int RetrieveMinutes = 3;
        public int MeasureWrapMinutes = 4;
        public int ReceivePaymentMinutes = 3;
        public float DefaultPatienceGameSeconds = 1800f; // 30 minutes of waiting
        public int RestockBaseMinutes = 5;

        private readonly BusinessInstanceState business;
        private readonly List<EntityId> clerks = new List<EntityId>();
        private readonly List<CustomerVisit> visits = new List<CustomerVisit>();
        private readonly Dictionary<string, float> shelfUnits =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly List<RetailRestockJob> restockJobs = new List<RetailRestockJob>();
        private readonly List<RetailDeliveryAcceptance> deliveries = new List<RetailDeliveryAcceptance>();
        private readonly List<MissedSaleRecord> missedSales = new List<MissedSaleRecord>();
        private int sequence;
        private int totalSalesCents;
        private int totalUnitsSold;

        public BusinessInstanceState Business => business;
        public IReadOnlyList<EntityId> Clerks => clerks;
        public IReadOnlyList<CustomerVisit> Visits => visits;
        public IReadOnlyList<RetailRestockJob> RestockJobs => restockJobs;
        public IReadOnlyList<RetailDeliveryAcceptance> Deliveries => deliveries;
        public IReadOnlyList<MissedSaleRecord> MissedSales => missedSales;
        public int TotalSalesCents => totalSalesCents;
        public int TotalUnitsSold => totalUnitsSold;
        public int ServiceMinutesPerCustomer => RetrieveMinutes + MeasureWrapMinutes + ReceivePaymentMinutes;

        public RetailServiceDesk(BusinessInstanceState business)
        {
            this.business = business ?? throw new ArgumentNullException(nameof(business));
        }

        /// <summary>
        /// Any business with inventory AND an assigned premises may retail —
        /// not just GeneralStore. Refuses loudly otherwise.
        /// </summary>
        public static bool CanRetail(BusinessInstanceState candidate, out string reason)
        {
            reason = string.Empty;
            if (candidate == null) { reason = "RetailServiceDesk: no business."; return false; }
            if (candidate.AssignedBuildingId < 0)
            {
                reason = "RetailServiceDesk: '" + candidate.InstanceId + "' has no assigned premises — retail needs a shop floor.";
                return false;
            }
            if (candidate.RuntimeState == null)
            {
                reason = "RetailServiceDesk: '" + candidate.InstanceId + "' has no runtime state — the shared retail sale authority requires it.";
                return false;
            }
            bool hasGenericLines = false;
            if (candidate.GenericConfiguration != null && candidate.GenericConfiguration.Retail != null)
            {
                foreach (GenericRetailProductLine line in candidate.GenericConfiguration.Retail.ProductLines)
                {
                    if (line != null && line.EnabledForSale) { hasGenericLines = true; break; }
                }
            }
            bool hasCategoryStock = candidate.RuntimeState != null && candidate.RuntimeState.TotalCurrentStockUnits > 0;
            if (!hasGenericLines && !hasCategoryStock)
            {
                reason = "RetailServiceDesk: '" + candidate.InstanceId + "' has no saleable inventory — nothing to retail.";
                return false;
            }
            return true;
        }

        public bool AddClerk(EntityId person, out string reason)
        {
            reason = string.Empty;
            if (person.Kind != EntityKind.Person || !person.IsValid)
            {
                reason = "RetailServiceDesk: clerks must be real Person entities — refusing " + person + ".";
                return false;
            }
            if (!clerks.Contains(person)) clerks.Add(person);
            return true;
        }

        /// <summary>Places initial stock on the shelf (sale-ready). Capped at available inventory.</summary>
        public void NoteShelfStock(string productId, float units)
        {
            if (string.IsNullOrWhiteSpace(productId) || units <= 0f) return;
            float available = business.GenericConfiguration.GetAvailableInventoryQuantity(productId);
            shelfUnits[productId] = Mathf.Min(Mathf.Max(0f, units), Mathf.Max(0f, available));
        }

        public float GetShelfUnits(string productId)
        {
            return shelfUnits.TryGetValue(productId ?? string.Empty, out float u) ? Mathf.Max(0f, u) : 0f;
        }

        /// <summary>
        /// A real person walks in wanting to buy. The desk NEVER invents customers:
        /// invalid or non-Person entities are refused loudly.
        /// </summary>
        public bool CustomerArrives(EntityId customer, string productId, int quantity,
            RetailServiceHooks hooks, out string reason, float patienceGameSeconds = -1f)
        {
            reason = string.Empty;
            if (customer.Kind != EntityKind.Person || !customer.IsValid)
            {
                reason = "RetailServiceDesk: refusing fabricated customer '" + customer + "' — only real persons may arrive.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(productId) || quantity <= 0)
            {
                reason = "RetailServiceDesk: refusing arrival with no product/quantity.";
                return false;
            }
            if (!CanRetail(business, out string retailReason))
            {
                reason = retailReason;
                return false;
            }
            var visit = new CustomerVisit
            {
                VisitId = "visit-" + business.InstanceId + "-" + (sequence++),
                Customer = customer,
                ProductId = productId,
                Quantity = quantity,
                ArrivedGameSeconds = hooks != null && hooks.NowGameSeconds != null ? hooks.NowGameSeconds() : 0f,
                PatienceGameSeconds = patienceGameSeconds > 0f ? patienceGameSeconds : DefaultPatienceGameSeconds,
            };
            visits.Add(visit);
            Log(hooks, "customer " + customer + " arrived for " + quantity + "x " + productId + " (" + visit.VisitId + ").");
            return true;
        }

        /// <summary>
        /// Main driver: expire impatient waiters, start service for the waiting with a
        /// free clerk and stocked shelf, and queue restock labor when shelves run low.
        /// </summary>
        public void Tick(RetailServiceHooks hooks)
        {
            if (hooks == null) return;
            float now = hooks.NowGameSeconds != null ? hooks.NowGameSeconds() : 0f;

            // 1. Patience: waiting customers leave when their patience runs out.
            foreach (CustomerVisit visit in visits)
            {
                if (visit.Status != CustomerVisitStatus.Waiting) continue;
                if (now - visit.ArrivedGameSeconds > visit.PatienceGameSeconds)
                {
                    visit.Status = CustomerVisitStatus.LeftImpatient;
                    visit.RefusalReason = "left after " + Mathf.RoundToInt(now - visit.ArrivedGameSeconds)
                        + "s waiting — no clerk free (understaffed).";
                    missedSales.Add(new MissedSaleRecord
                    {
                        VisitId = visit.VisitId,
                        Customer = visit.Customer,
                        ProductId = visit.ProductId,
                        Quantity = visit.Quantity,
                        Reason = visit.RefusalReason,
                        GameSeconds = now,
                    });
                    Log(hooks, visit.VisitId + " " + visit.RefusalReason);
                }
            }

            // 2. Serve the waiting, FIFO, when a clerk is free and the shelf is stocked.
            foreach (CustomerVisit visit in visits)
            {
                if (visit.Status != CustomerVisitStatus.Waiting) continue;
                if (!IsProductSaleable(visit.ProductId, out _)) continue;
                if (GetShelfUnits(visit.ProductId) < visit.Quantity - 0.001f)
                {
                    EnsureRestockJob(visit.ProductId, visit.Quantity, hooks);
                    continue;
                }
                if (!TryFindFreeClerk(hooks, out EntityId clerk)) continue; // understaffed: they keep waiting
                StartService(visit, clerk, hooks);
            }

            // 3. Restock jobs get clerk labor when one is free.
            foreach (RetailRestockJob job in restockJobs)
            {
                if (job.Done || job.TaskId.IsValid) continue;
                if (!TryFindFreeClerk(hooks, out EntityId clerk)) break;
                StartRestock(job, clerk, hooks);
            }

            // 4. Deliveries get accepted when a clerk is free.
            foreach (RetailDeliveryAcceptance delivery in deliveries)
            {
                if (delivery.Done || delivery.TaskId.IsValid) continue;
                if (!TryFindFreeClerk(hooks, out EntityId clerk)) break;
                StartDeliveryAcceptance(delivery, clerk, hooks);
            }
        }

        /// <summary>
        /// Works a desk-owned task (serve / restock / accept-delivery) through
        /// TaskAuthority.RecordWorkProgress. Completing a serve task executes the
        /// sale; completing restock shelves goods; completing a delivery books it
        /// into inventory with provenance. Returns false only on hard refusal.
        /// </summary>
        public bool WorkOnTask(EntityId taskId, int minutes, RetailServiceHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (hooks == null || hooks.Tasks == null) { reason = "RetailServiceDesk: no TaskAuthority."; return false; }
            WorkTask task = hooks.Tasks.FindTask(taskId);
            if (task == null) { reason = "RetailServiceDesk: unknown task " + taskId + "."; return false; }

            if (!hooks.Tasks.RecordWorkProgress(taskId, minutes, hooks.Budgets, hooks.DayIndex,
                out bool reachedCompletion, out string rejection))
            {
                reason = rejection;
                return false;
            }

            CustomerVisit visit = visits.Find(v => v.ServiceTaskId == taskId);
            if (visit != null) UpdateServicePhase(visit, task);

            if (reachedCompletion)
            {
                hooks.Tasks.CompleteTask(taskId, TaskOutcome.Success, hooks.DayIndex, hooks.Budgets);
                return FinalizeTask(taskId, hooks, out reason);
            }
            return true;
        }

        /// <summary>Queues shelf-restock labor for a product. Real work, real worker.</summary>
        public bool RequestRestock(string productId, int quantity, RetailServiceHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(productId) || quantity <= 0)
            {
                reason = "RetailServiceDesk: restock needs a product and quantity.";
                return false;
            }
            EnsureRestockJob(productId, quantity, hooks);
            return true;
        }

        /// <summary>
        /// Notes a real inbound delivery (a shipment that actually arrived). The goods
        /// enter inventory ONLY when the accept-delivery task completes — never on note.
        /// </summary>
        public bool NoteIncomingDelivery(string deliveryId, string shipmentId, string productId,
            int quantity, int unitCostCents, string sourceReference,
            RetailServiceHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(deliveryId) || string.IsNullOrWhiteSpace(productId) || quantity <= 0)
            {
                reason = "RetailServiceDesk: delivery needs an id, product, and quantity — refusing phantom delivery.";
                return false;
            }
            if (deliveries.Exists(d => string.Equals(d.DeliveryId, deliveryId, StringComparison.Ordinal)))
            {
                reason = "RetailServiceDesk: delivery '" + deliveryId + "' already noted — refusing duplicate.";
                return false;
            }
            deliveries.Add(new RetailDeliveryAcceptance
            {
                DeliveryId = deliveryId,
                ShipmentId = shipmentId ?? string.Empty,
                ProductId = productId,
                Quantity = quantity,
                UnitCostCents = Mathf.Max(0, unitCostCents),
                SourceReference = sourceReference ?? string.Empty,
            });
            Log(hooks, "delivery '" + deliveryId + "' noted (" + quantity + "x " + productId + ") — acceptance labor queued.");
            return true;
        }

        // ---------- save / load (DTO methods inside the owning runtime class) ----------

        public RetailServiceDeskSaveDto CaptureSaveDto()
        {
            var dto = new RetailServiceDeskSaveDto
            {
                BusinessId = business.InstanceId,
                Sequence = sequence,
                TotalSalesCents = totalSalesCents,
                TotalUnitsSold = totalUnitsSold,
            };
            dto.Clerks.AddRange(clerks);
            dto.Visits.AddRange(visits);
            foreach (var kvp in shelfUnits)
                dto.ShelfUnits.Add(new ShelfUnitsEntry { ProductId = kvp.Key, Units = kvp.Value });
            dto.RestockJobs.AddRange(restockJobs);
            dto.Deliveries.AddRange(deliveries);
            dto.MissedSales.AddRange(missedSales);
            return dto;
        }

        public void RestoreSaveDto(RetailServiceDeskSaveDto dto)
        {
            if (dto == null) return;
            clerks.Clear(); clerks.AddRange(dto.Clerks);
            visits.Clear(); visits.AddRange(dto.Visits);
            shelfUnits.Clear();
            foreach (ShelfUnitsEntry e in dto.ShelfUnits)
                if (!string.IsNullOrWhiteSpace(e.ProductId)) shelfUnits[e.ProductId] = Mathf.Max(0f, e.Units);
            restockJobs.Clear(); restockJobs.AddRange(dto.RestockJobs);
            deliveries.Clear(); deliveries.AddRange(dto.Deliveries);
            missedSales.Clear(); missedSales.AddRange(dto.MissedSales);
            sequence = dto.Sequence;
            totalSalesCents = dto.TotalSalesCents;
            totalUnitsSold = dto.TotalUnitsSold;
        }

        /// <summary>
        /// Rebinds desk task ids to live TaskAuthority tasks after a load (read-only
        /// rebind — never a second task for the same definition).
        /// </summary>
        public void RebindTasks(RetailServiceHooks hooks)
        {
            if (hooks == null || hooks.Tasks == null) return;
            EntityId owner = BusinessEntityId();
            foreach (CustomerVisit visit in visits)
            {
                if (visit.Status != CustomerVisitStatus.BeingServed || !visit.ServiceTaskId.IsValid) continue;
                if (hooks.Tasks.FindTask(visit.ServiceTaskId) == null)
                    visit.ServiceTaskId = EntityId.Invalid; // task did not survive — visit returns to waiting
                if (!visit.ServiceTaskId.IsValid) visit.Status = CustomerVisitStatus.Waiting;
            }
            foreach (RetailRestockJob job in restockJobs)
            {
                if (job.Done || !job.TaskId.IsValid) continue;
                if (hooks.Tasks.FindTask(job.TaskId) == null) job.TaskId = EntityId.Invalid;
            }
            foreach (RetailDeliveryAcceptance delivery in deliveries)
            {
                if (delivery.Done || !delivery.TaskId.IsValid) continue;
                if (hooks.Tasks.FindTask(delivery.TaskId) == null) delivery.TaskId = EntityId.Invalid;
            }
        }

        // ---------- internals ----------

        private EntityId BusinessEntityId() => EntityId.For(EntityKind.Business, Math.Abs(business.InstanceId.GetHashCode()) % 1000000 + 1);

        private void Log(RetailServiceHooks hooks, string message)
        {
            if (hooks != null && hooks.Diagnostics != null) hooks.Diagnostics.Add("RetailServiceDesk: " + message);
        }

        private bool IsProductSaleable(string productId, out GenericRetailProductLine line)
        {
            line = null;
            return business.GenericConfiguration != null
                && business.GenericConfiguration.Retail.TryGetLine(productId, out line)
                && line.EnabledForSale
                && line.SellingPriceCents > 0;
        }

        /// <summary>A clerk is free when they are a real person, have work-time budget
        /// left, and hold no open desk task. This is the real service capacity.</summary>
        private bool TryFindFreeClerk(RetailServiceHooks hooks, out EntityId clerk)
        {
            clerk = EntityId.Invalid;
            if (hooks == null || hooks.Tasks == null || hooks.Budgets == null) return false;
            EntityId owner = BusinessEntityId();
            List<WorkTask> open = hooks.Tasks.GetOpenTasks(owner);
            foreach (EntityId candidate in clerks)
            {
                if (candidate.Kind != EntityKind.Person || !candidate.IsValid) continue;
                WorkTimeBudget budget;
                try { budget = hooks.Budgets.GetOrCreate(candidate); }
                catch (ArgumentException) { continue; }
                if (budget.MinutesRemaining < 1) continue; // no budget left today
                bool busy = false;
                foreach (WorkTask t in open)
                {
                    if (t.AssigneeId == candidate && t.IsOpen) { busy = true; break; }
                }
                if (busy) continue;
                clerk = candidate;
                return true;
            }
            return false;
        }

        private void EnsureRestockJob(string productId, int wantedQty, RetailServiceHooks hooks)
        {
            foreach (RetailRestockJob existing in restockJobs)
            {
                if (!existing.Done && string.Equals(existing.ProductId, productId, StringComparison.OrdinalIgnoreCase))
                    return; // one restock job per product at a time
            }
            float available = business.GenericConfiguration.GetAvailableInventoryQuantity(productId);
            float shelf = GetShelfUnits(productId);
            int deficit = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(wantedQty, available) - shelf));
            if (deficit <= 0) return;
            restockJobs.Add(new RetailRestockJob
            {
                JobId = "restock-" + business.InstanceId + "-" + (sequence++),
                ProductId = productId,
                Quantity = deficit,
            });
            Log(hooks, "shelf low on '" + productId + "' — restock job queued (" + deficit + " units).");
        }

        private TaskDefinition EnsureDefinition(TaskAuthority tasks, string definitionId, string displayName, int baseMinutes)
        {
            TaskDefinition existing = tasks.GetDefinition(definitionId);
            if (existing != null) return existing;
            var def = new TaskDefinition(definitionId, displayName, Mathf.Max(1, baseMinutes));
            tasks.RegisterDefinition(def, out _);
            return def;
        }

        private bool CreateAndAssign(TaskAuthority tasks, WorkTimeBudgetStore budgets, int dayIndex,
            string definitionId, EntityId clerk, out EntityId taskId, out string reason)
        {
            reason = string.Empty;
            taskId = EntityId.Invalid;
            WorkTask task = tasks.CreateTask(definitionId, BusinessEntityId(), dayIndex);
            if (!tasks.AssignTask(task.TaskId, clerk, budgets, out reason))
            {
                tasks.CancelTask(task.TaskId, budgets);
                return false;
            }
            if (!tasks.StartTask(task.TaskId, dayIndex, out reason))
            {
                tasks.CancelTask(task.TaskId, budgets);
                return false;
            }
            taskId = task.TaskId;
            return true;
        }

        private void StartService(CustomerVisit visit, EntityId clerk, RetailServiceHooks hooks)
        {
            string defId = "retail:serve:" + business.InstanceId + ":" + visit.VisitId;
            EnsureDefinition(hooks.Tasks, defId, "Serve customer " + visit.Customer + " (" + visit.Quantity + "x " + visit.ProductId + ")", ServiceMinutesPerCustomer);
            if (!CreateAndAssign(hooks.Tasks, hooks.Budgets, hooks.DayIndex, defId, clerk, out EntityId taskId, out string reason))
            {
                Log(hooks, visit.VisitId + ": clerk " + clerk + " could not take the service task (" + reason + ") — customer keeps waiting.");
                return;
            }
            visit.Status = CustomerVisitStatus.BeingServed;
            visit.ServiceTaskId = taskId;
            visit.ServedBy = clerk;
            visit.Phase = RetailServicePhase.RetrievingStock;
            Log(hooks, visit.VisitId + ": clerk " + clerk + " started service (retrieve -> measure/wrap -> payment).");
        }

        private void StartRestock(RetailRestockJob job, EntityId clerk, RetailServiceHooks hooks)
        {
            string defId = "retail:restock:" + business.InstanceId + ":" + job.JobId;
            EnsureDefinition(hooks.Tasks, defId, "Restock shelves (" + job.Quantity + "x " + job.ProductId + ")",
                RestockBaseMinutes + Mathf.CeilToInt(job.Quantity / 10f));
            if (!CreateAndAssign(hooks.Tasks, hooks.Budgets, hooks.DayIndex, defId, clerk, out EntityId taskId, out string reason))
            {
                Log(hooks, job.JobId + ": no clerk could take restock (" + reason + ").");
                return;
            }
            job.TaskId = taskId;
            Log(hooks, job.JobId + ": clerk " + clerk + " restocking " + job.Quantity + "x " + job.ProductId + ".");
        }

        private void StartDeliveryAcceptance(RetailDeliveryAcceptance delivery, EntityId clerk, RetailServiceHooks hooks)
        {
            string defId = "retail:accept-delivery:" + business.InstanceId + ":" + delivery.DeliveryId;
            EnsureDefinition(hooks.Tasks, defId, "Accept delivery '" + delivery.DeliveryId + "' (" + delivery.Quantity + "x " + delivery.ProductId + ")",
                10 + Mathf.CeilToInt(delivery.Quantity / 5f));
            if (!CreateAndAssign(hooks.Tasks, hooks.Budgets, hooks.DayIndex, defId, clerk, out EntityId taskId, out string reason))
            {
                Log(hooks, delivery.DeliveryId + ": no clerk could accept (" + reason + ").");
                return;
            }
            delivery.TaskId = taskId;
            Log(hooks, delivery.DeliveryId + ": clerk " + clerk + " accepting delivery.");
        }

        private void UpdateServicePhase(CustomerVisit visit, WorkTask task)
        {
            int worked = task.MinutesWorked;
            visit.Phase = worked < RetrieveMinutes ? RetailServicePhase.RetrievingStock
                : worked < RetrieveMinutes + MeasureWrapMinutes ? RetailServicePhase.MeasuringWrapping
                : worked < ServiceMinutesPerCustomer ? RetailServicePhase.ReceivingPayment
                : RetailServicePhase.Done;
        }

        private bool FinalizeTask(EntityId taskId, RetailServiceHooks hooks, out string reason)
        {
            reason = string.Empty;
            CustomerVisit visit = visits.Find(v => v.ServiceTaskId == taskId);
            if (visit != null) return FinalizeService(visit, hooks, out reason);
            RetailRestockJob job = restockJobs.Find(j => j.TaskId == taskId);
            if (job != null)
            {
                float available = business.GenericConfiguration.GetAvailableInventoryQuantity(job.ProductId);
                float add = Mathf.Min(job.Quantity, Mathf.Max(0f, available - GetShelfUnits(job.ProductId)));
                if (add > 0f) shelfUnits[job.ProductId] = GetShelfUnits(job.ProductId) + add;
                job.Done = true;
                Log(hooks, job.JobId + ": shelved " + add + "x " + job.ProductId + ".");
                return true;
            }
            RetailDeliveryAcceptance delivery = deliveries.Find(d => d.TaskId == taskId);
            if (delivery != null)
            {
                // Goods enter inventory ONLY here, through the shipment-aware authority
                // (duplicate shipment ids refused — no duplicated inventory, no teleport).
                bool received = business.GenericConfiguration.TryReceiveInventoryShipment(
                    string.IsNullOrWhiteSpace(delivery.ShipmentId) ? delivery.DeliveryId : delivery.ShipmentId,
                    delivery.ProductId, delivery.Quantity, delivery.UnitCostCents, null,
                    "delivery '" + delivery.DeliveryId + "' from " + delivery.SourceReference, hooks.DayIndex);
                if (!received)
                {
                    reason = "RetailServiceDesk: delivery '" + delivery.DeliveryId + "' refused by inventory authority (duplicate or blocked).";
                    Log(hooks, reason);
                    return false;
                }
                shelfUnits[delivery.ProductId] = GetShelfUnits(delivery.ProductId) + delivery.Quantity;
                delivery.Done = true;
                Log(hooks, delivery.DeliveryId + ": accepted " + delivery.Quantity + "x " + delivery.ProductId
                    + " into inventory and shelf (provenance: " + delivery.SourceReference + ").");
                return true;
            }
            reason = "RetailServiceDesk: completed task " + taskId + " is not a desk task.";
            return false;
        }

        private bool FinalizeService(CustomerVisit visit, RetailServiceHooks hooks, out string reason)
        {
            reason = string.Empty;
            visit.Phase = RetailServicePhase.Done;
            if (!IsProductSaleable(visit.ProductId, out GenericRetailProductLine line))
            {
                return RefuseVisit(visit, hooks, "product '" + visit.ProductId + "' is no longer saleable.", out reason);
            }
            if (hooks.LedgerForPerson == null || hooks.BusinessLedger == null)
            {
                return RefuseVisit(visit, hooks, "no ledger access — sale cannot settle.", out reason);
            }
            HouseholdLedger buyerLedger = hooks.LedgerForPerson(visit.Customer);
            if (buyerLedger == null)
            {
                return RefuseVisit(visit, hooks, "buyer " + visit.Customer + " has no household ledger — cannot pay.", out reason);
            }
            int totalCents = visit.Quantity * line.SellingPriceCents;
            var saleContext = new GenericRetailSaleContext
            {
                BuyerPrincipal = "household:" + buyerLedger.HouseholdId,
                ActingPersonId = visit.Customer.Id,
                SellerBusinessId = business.InstanceId,
                ProductId = visit.ProductId,
                Quantity = visit.Quantity,
                UnitPriceCents = line.SellingPriceCents,
                LocationId = "building:" + business.AssignedBuildingId,
            };
            // Atomic cash movement: buyer -> business household. If the household
            // cannot pay, TryTransferTo refuses and the authority rolls inventory back.
            bool settled = GenericRetailSaleAuthority.TryExecuteSale(business, saleContext,
                ctx =>
                {
                    string refusal = buyerLedger.TryTransferTo(hooks.BusinessLedger, hooks.DayIndex, totalCents,
                        "Retail purchase: " + ctx.Quantity + "x " + ctx.ProductId + " at " + BusinessDisplayName(hooks),
                        HouseholdIncomeSource.SaleProceeds, business.InstanceId,
                        "Retail sale: " + ctx.Quantity + "x " + ctx.ProductId + " to household " + buyerLedger.HouseholdId,
                        "household:" + buyerLedger.HouseholdId);
                    if (refusal != null)
                    {
                        Log(hooks, visit.VisitId + ": settlement refused — " + refusal);
                        return false;
                    }
                    return true;
                }, out string saleReason);
            if (!settled)
            {
                return RefuseVisit(visit, hooks, "sale failed: " + saleReason, out reason);
            }
            visit.Status = CustomerVisitStatus.Served;
            shelfUnits[visit.ProductId] = Mathf.Max(0f, GetShelfUnits(visit.ProductId) - visit.Quantity);
            totalSalesCents += totalCents;
            totalUnitsSold += visit.Quantity;
            Log(hooks, visit.VisitId + ": served — " + visit.Quantity + "x " + visit.ProductId
                + " for " + totalCents + "c (buyer household " + buyerLedger.HouseholdId + " -> business).");
            return true;
        }

        private bool RefuseVisit(CustomerVisit visit, RetailServiceHooks hooks, string why, out string reason)
        {
            visit.Status = CustomerVisitStatus.SaleRefused;
            visit.RefusalReason = why;
            missedSales.Add(new MissedSaleRecord
            {
                VisitId = visit.VisitId,
                Customer = visit.Customer,
                ProductId = visit.ProductId,
                Quantity = visit.Quantity,
                Reason = why,
                GameSeconds = hooks.NowGameSeconds != null ? hooks.NowGameSeconds() : 0f,
            });
            reason = "RetailServiceDesk: " + visit.VisitId + " " + why;
            Log(hooks, reason);
            return false;
        }

        private string BusinessDisplayName(RetailServiceHooks hooks) =>
            !string.IsNullOrWhiteSpace(hooks.BusinessDisplayName) ? hooks.BusinessDisplayName : business.InstanceId;
    }
}
