using System;
using System.Collections.Generic;
using LandLedgers.Economy.Retail;

namespace LandLedgers.Economy.GeneralStore
{
    /// <summary>T1C: one customer waiting to be tended.</summary>
    [Serializable]
    public sealed class WaitingCustomer
    {
        public string CustomerRef = string.Empty;
        public int ActingPersonId = -1;
        public string CategoryId = string.Empty;
        public int UnitsWanted;
        public int PriceCentsPerUnit;
        public int PatienceMinutesRemaining;
        public int QueuedDayIndex;

        public WaitingCustomer() { }
    }

    /// <summary>
    /// T1C: the store's waiting-customer queue — queues get teeth. Customers join the
    /// line when no clerk is free (the TTS-5 TendCustomer task serves them FIFO);
    /// each carries a patience budget. Patience exhausted -> the customer BALKS and a
    /// Tech X §5.4 lost-sale event is written. Overlong lines balk on arrival.
    ///
    /// Integration (Unity-side / store runtime): call Tick with elapsed game minutes
    /// (or decrement by TendCustomer task minutes), and ServeNext when a TendCustomer
    /// task completes. The queue never serves anyone by itself — service is the task's job.
    /// </summary>
    public sealed class CustomerQueue
    {
        /// <summary>TUNING: how long a customer waits before balking (calibration, Canon Part XV).</summary>
        public const int DefaultPatienceMinutes = 20;

        /// <summary>TUNING: line length at which arrivals refuse to join (calibration).</summary>
        public const int MaxToleratedQueueLength = 5;

        public string MerchantBusinessId = string.Empty;
        public string MerchantName = string.Empty;

        private readonly List<WaitingCustomer> waiting = new List<WaitingCustomer>();
        private readonly LostSaleLog lostSales;

        public CustomerQueue() : this(null) { }

        public CustomerQueue(LostSaleLog lostSales)
        {
            this.lostSales = lostSales ?? new LostSaleLog();
        }

        public LostSaleLog LostSales => lostSales;
        public int WaitingCount => waiting.Count;

        /// <summary>
        /// A customer arrives. Returns false when the line is too long: the customer
        /// balks immediately and a QueueBalk lost sale is recorded.
        /// </summary>
        public bool Enqueue(
            string customerRef,
            int actingPersonId,
            string categoryId,
            int unitsWanted,
            int priceCentsPerUnit,
            int dayIndex,
            int patienceMinutes = DefaultPatienceMinutes)
        {
            if (waiting.Count >= MaxToleratedQueueLength)
            {
                lostSales.Record(
                    dayIndex, MerchantBusinessId, MerchantName, actingPersonId,
                    categoryId, unitsWanted, priceCentsPerUnit,
                    LostSaleReason.QueueBalk,
                    $"Balked on arrival: {waiting.Count} already waiting (tolerance {MaxToleratedQueueLength}).");
                return false;
            }

            waiting.Add(new WaitingCustomer
            {
                CustomerRef = customerRef ?? string.Empty,
                ActingPersonId = actingPersonId,
                CategoryId = categoryId ?? string.Empty,
                UnitsWanted = Math.Max(0, unitsWanted),
                PriceCentsPerUnit = Math.Max(0, priceCentsPerUnit),
                PatienceMinutesRemaining = Math.Max(1, patienceMinutes),
                QueuedDayIndex = dayIndex,
            });
            return true;
        }

        /// <summary>
        /// Advances the queue clock. Customers whose patience runs out balk: each
        /// writes a QueueBalk lost-sale event with the demand they took with them.
        /// Returns the number of customers who balked this tick.
        /// </summary>
        public int Tick(int minutesElapsed, int dayIndex)
        {
            int elapsed = Math.Max(0, minutesElapsed);
            int balked = 0;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                WaitingCustomer customer = waiting[i];
                customer.PatienceMinutesRemaining -= elapsed;
                if (customer.PatienceMinutesRemaining > 0)
                {
                    continue;
                }

                waiting.RemoveAt(i);
                balked++;
                lostSales.Record(
                    dayIndex, MerchantBusinessId, MerchantName, customer.ActingPersonId,
                    customer.CategoryId, customer.UnitsWanted, customer.PriceCentsPerUnit,
                    LostSaleReason.QueueBalk,
                    $"Patience exhausted after waiting (wanted {customer.UnitsWanted}u {customer.CategoryId}).");
            }

            return balked;
        }

        /// <summary>
        /// Called when a TendCustomer task completes: the next customer steps up.
        /// Returns their customer ref, or null when the line is empty.
        /// </summary>
        public string ServeNext()
        {
            if (waiting.Count == 0)
            {
                return null;
            }

            WaitingCustomer next = waiting[0];
            waiting.RemoveAt(0);
            return next.CustomerRef;
        }

        /// <summary>Peeks at the waiting line without serving (diagnostics/UI).</summary>
        public IReadOnlyList<WaitingCustomer> Waiting => waiting;

        /// <summary>
        /// W4C save contract: lives inside the owning queue class. Persists
        /// the waiting line and the merchant identity. The lost-sale log is
        /// intentionally NOT persisted: it is diagnostics history, not
        /// gameplay state (LostSaleEvent: "diagnostics, not entities"), so
        /// after a load the log restarts empty and sequences restart.
        /// </summary>
        [Serializable]
        public sealed class CustomerQueueSaveDto
        {
            public string MerchantBusinessId = string.Empty;
            public string MerchantName = string.Empty;
            public List<WaitingCustomer> Waiting = new List<WaitingCustomer>();
        }

        public CustomerQueueSaveDto CaptureSaveDto()
        {
            var dto = new CustomerQueueSaveDto
            {
                MerchantBusinessId = MerchantBusinessId ?? string.Empty,
                MerchantName = MerchantName ?? string.Empty,
            };
            foreach (var customer in waiting)
            {
                if (customer == null) continue;
                dto.Waiting.Add(new WaitingCustomer
                {
                    CustomerRef = customer.CustomerRef ?? string.Empty,
                    ActingPersonId = customer.ActingPersonId,
                    CategoryId = customer.CategoryId ?? string.Empty,
                    UnitsWanted = customer.UnitsWanted,
                    PriceCentsPerUnit = customer.PriceCentsPerUnit,
                    PatienceMinutesRemaining = customer.PatienceMinutesRemaining,
                    QueuedDayIndex = customer.QueuedDayIndex,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(CustomerQueueSaveDto dto)
        {
            waiting.Clear();
            if (dto == null) return;
            MerchantBusinessId = dto.MerchantBusinessId ?? string.Empty;
            MerchantName = dto.MerchantName ?? string.Empty;
            if (dto.Waiting == null) return;
            foreach (var customer in dto.Waiting)
            {
                if (customer == null) continue;
                waiting.Add(new WaitingCustomer
                {
                    CustomerRef = customer.CustomerRef ?? string.Empty,
                    ActingPersonId = customer.ActingPersonId,
                    CategoryId = customer.CategoryId ?? string.Empty,
                    UnitsWanted = customer.UnitsWanted,
                    PriceCentsPerUnit = customer.PriceCentsPerUnit,
                    PatienceMinutesRemaining = customer.PatienceMinutesRemaining,
                    QueuedDayIndex = customer.QueuedDayIndex,
                });
            }
        }
    }
}
