using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// F7 Tier 1 (2026-11-13): lumber inventory handover to yard. A tested
    /// adapter, not a second yard or mill runtime. It moves one whole
    /// SawmillLumberLot from the shared mill LumberStock into the yard
    /// LumberStock through the existing yard intake
    /// (LumberYardShopRuntime.ReceiveLumberFromSawmill), with the grade
    /// assigned at intake, the full sawmill provenance copied, and the mill
    /// lot emptied exactly once.
    ///
    /// The driver never sets a lot state directly. It calls only
    /// yard.ReceiveLumberFromSawmill plus the read-only stock properties.
    /// It never touches a GameObject, grid coordinate, work position,
    /// money, charge, price, or settlement; it moves custody only. The
    /// unit cost the yard lot carries is caller-supplied data recorded on
    /// the lot (a cost basis for later pricing, not a payment). The driver
    /// never calls the scalar weekly transfer, never touches
    /// BusinessRuntimeState category stock, never calls TimberChain
    /// SawLogs, and never calls RunSawing or RunTollSawing. Production
    /// stays the F6 driver's job; the transfer starts from lots already
    /// in mill LumberStock.
    ///
    /// Exactly-once, two authorities: the driver's job record first, and
    /// the yard's duplicate-source guard second. A successful intake zeroes
    /// the mill lot in place, so the same lot can never transfer again.
    ///
    /// C-4 (OD-C5, expanded campaign 2026-11-16): the transfer is a
    /// wholesale PURCHASE, not a gift of custody. The driver moves custody
    /// AND cash in one transaction: the yard pays the mill the job's
    /// wholesale unit cost per transferred unit, through caller-supplied
    /// business cash ports (the existing cash bridge). Affordability is
    /// checked before any custody moves, from the actual lot units and
    /// the job's unit cost; the payment settles on the actual transferred
    /// delta, never on an estimate; a yard that cannot pay keeps the
    /// lumber at the mill. The unit cost the yard lot carries is the same
    /// wholesale price, recorded on the lot as its cost basis for later
    /// pricing. A zero unit cost is refused at begin: a free lot quotes
    /// 1c at retail and the mill earns nothing (the F6 defect).
    ///
    /// The driver is unbound in Tier 1: no hub list entry, no save
    /// section, no production call site. Wiring is instructions only
    /// (plan 21-plan-f7 §6). The call-site grade is the yard's
    /// DefaultGradeId (merchantable) until a sort policy is authored
    /// (Agent 3 note 93, correction C5: no "declared sort grade" property
    /// exists in source).
    /// </summary>
    [Serializable]
    public sealed class SawmillToYardTransferJob
    {
        public string JobId = string.Empty;
        public string MillLotId = string.Empty;
        public EntityId YardLotId = EntityId.Invalid;
        public string MillBusinessId = string.Empty;
        public string YardBusinessId = string.Empty;
        public int LumberUnits;
        public string YardGradeId = string.Empty;
        public int UnitCostCents;
        /// <summary>
        /// C-4: the settled wholesale price in cents, or -1 while the job
        /// is unsettled. Set only when the yard debit AND the mill credit
        /// both succeed; the job is the settlement record. A job whose
        /// custody moved but whose mill credit was refused keeps -1 and
        /// names the unreconciled debit in diagnostics.
        /// </summary>
        public int SettledPriceCents = -1;
        public int SettledDayIndex = -1;
        public int AcquiredDayIndex = -1;
        public SawmillToYardTransferJobState State = SawmillToYardTransferJobState.Planned;
        public int CreatedDayIndex = -1;
        public int TransferredDayIndex = -1;
        public SawmillToYardTransferJob() { }
    }

    public enum SawmillToYardTransferJobState
    {
        Planned = 0,
        Transferred = 1,
        Failed = 2,
    }

    public sealed class SawmillToYardTransferDriver
    {
        /// <summary>
        /// C-4 (OD-C5): the mill-to-yard wholesale unit price in cents.
        /// Coherence default 32c wholesale, quoting 40c at retail under
        /// the yard's 25% markup (DEF-016 coherence: ~$25 per thousand
        /// board feet, 1870 pine). Agent F calibration, HOLD-011 open;
        /// the owner corrects the figure. The production caller passes
        /// this constant at BeginTransfer; the driver never defaults a
        /// cost itself and refuses a zero or negative one.
        /// </summary>
        public const int WholesaleLumberUnitCostCents = 32;

        private readonly SawmillShopRuntime mill;
        private readonly LumberYardShopRuntime yard;
        private readonly List<string> diagnostics;
        private readonly Dictionary<string, SawmillToYardTransferJob> jobs =
            new Dictionary<string, SawmillToYardTransferJob>(StringComparer.Ordinal);

        /// <summary>
        /// C-4: the yard's business cash port, bound by the production
        /// caller fresh each drive from the yard business's runtime
        /// state (the byproduct-transfer pattern). Null means no
        /// transfer can settle: RunTransfer refuses loudly rather than
        /// moving lumber unpaid.
        /// </summary>
        public IBusinessIdentityCashPort YardCash { get; set; }

        /// <summary>
        /// C-4: the mill's business cash port, bound by the production
        /// caller fresh each drive from the mill business's runtime
        /// state. Null means no transfer can settle.
        /// </summary>
        public IBusinessIdentityCashPort MillCash { get; set; }

        public SawmillToYardTransferDriver(
            SawmillShopRuntime mill,
            LumberYardShopRuntime yard,
            List<string> diagnostics)
        {
            this.mill = mill;
            this.yard = yard;
            this.diagnostics = diagnostics ?? new List<string>();
            if (mill == null)
            {
                this.diagnostics.Add("SawmillToYardTransferDriver: no sawmill runtime — transfers refused.");
            }
            if (yard == null)
            {
                this.diagnostics.Add("SawmillToYardTransferDriver: no lumber yard runtime — transfers refused.");
            }
        }

        public SawmillShopRuntime Mill => mill;
        public LumberYardShopRuntime Yard => yard;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int TransferJobCount => jobs.Count;
        public IReadOnlyDictionary<string, SawmillToYardTransferJob> TransferJobs => jobs;

        /// <summary>
        /// Begins a transfer job for a lot already in mill LumberStock.
        /// Never calls the intake, never moves a unit, never allocates a
        /// yard lot id. A refused begin leaves both stocks untouched.
        /// </summary>
        public SawmillToYardTransferJob BeginTransfer(string millLotId, string yardGradeId, int unitCostCents, int dayIndex, out string refusal)
        {
            refusal = null;
            if (mill == null)
            {
                refusal = "SawmillToYardTransferDriver.BeginTransfer: no sawmill runtime — transfer refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (yard == null)
            {
                refusal = "SawmillToYardTransferDriver.BeginTransfer: no lumber yard runtime — transfer refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (string.IsNullOrWhiteSpace(millLotId))
            {
                refusal = "SawmillToYardTransferDriver.BeginTransfer: no mill lot id supplied — transfer refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (unitCostCents <= 0)
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: job for lot '{millLotId}' names a non-positive unit cost ({unitCostCents}c) — a transfer is a wholesale purchase at a real price; a zero-cost lot quotes 1c at retail and the mill earns nothing (the F6 defect, C-4). Refused.";
                diagnostics.Add(refusal);
                return null;
            }
            string resolvedGrade = yardGradeId;
            if (string.IsNullOrWhiteSpace(resolvedGrade))
            {
                resolvedGrade = LumberYardGradeCatalog.DefaultGradeId;
                diagnostics.Add($"SawmillToYardTransferDriver.BeginTransfer: job for lot '{millLotId}' declares no grade — "
                    + $"sorted as '{resolvedGrade}' (the honest default yard sort), recorded loudly.");
            }
            else if (!LumberYardGradeCatalog.IsKnownGrade(resolvedGrade))
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: job for lot '{millLotId}' names unknown grade '{yardGradeId}' — grades are data, fix the id.";
                diagnostics.Add(refusal);
                return null;
            }
            else
            {
                resolvedGrade = resolvedGrade.Trim();
            }
            SawmillLumberLot lot = FindMillLot(millLotId);
            if (lot == null)
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: lot '{millLotId}' is absent from the mill yard — intake must happen first.";
                diagnostics.Add(refusal);
                return null;
            }
            if (lot.LumberUnits <= 0)
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: lot '{millLotId}' is an empty husk (already transferred or never stocked) — transfer refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (YardCash == null || !string.Equals(YardCash.BusinessId, yard.BusinessInstanceId, StringComparison.Ordinal))
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: no correctly bound cash authority for yard '{yard.BusinessInstanceId}' — a purchase job cannot be created without its buyer's settlement authority.";
                diagnostics.Add(refusal);
                return null;
            }
            if (MillCash == null || !string.Equals(MillCash.BusinessId, mill.BusinessInstanceId, StringComparison.Ordinal))
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: no correctly bound cash authority for mill '{mill.BusinessInstanceId}' — a purchase job cannot be created without its seller's settlement authority.";
                diagnostics.Add(refusal);
                return null;
            }
            // A planned transfer is a purchase attempt, not a credit
            // reservation. Do not leave a new open job for a buyer that
            // cannot pay its full lot price; the source lot remains owned
            // by the mill and can be offered again after the yard's cash
            // position changes.
            long quotedPrice = (long)lot.LumberUnits * unitCostCents;
            if (quotedPrice > int.MaxValue)
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: lot '{millLotId}' price {quotedPrice}c exceeds supported settlement limits; no transfer job was created.";
                diagnostics.Add(refusal);
                return null;
            }
            if (YardCash != null
                && string.Equals(YardCash.BusinessId, yard.BusinessInstanceId, StringComparison.Ordinal)
                && YardCash.BalanceCents < quotedPrice)
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: yard '{yard.BusinessInstanceId}' has {YardCash.BalanceCents}c but needs {quotedPrice}c for lot '{millLotId}'; no job or payment obligation was created and the lot remains at the mill.";
                diagnostics.Add(refusal);
                return null;
            }
            string jobId = "yard-transfer:" + millLotId;
            if (jobs.TryGetValue(jobId, out SawmillToYardTransferJob open)
                && open.State == SawmillToYardTransferJobState.Planned)
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: job '{jobId}' is already open ({open.State}) — a retried begin never creates a second job.";
                diagnostics.Add(refusal);
                return null;
            }
            if (FindYardLotBySource(millLotId) != null)
            {
                refusal = $"SawmillToYardTransferDriver.BeginTransfer: the yard already holds a lot from source mill lot '{millLotId}' — duplicate source refused.";
                diagnostics.Add(refusal);
                return null;
            }
            var job = new SawmillToYardTransferJob
            {
                JobId = jobId,
                MillLotId = millLotId,
                YardLotId = EntityId.Invalid,
                MillBusinessId = mill.BusinessInstanceId,
                YardBusinessId = yard.BusinessInstanceId,
                LumberUnits = 0,
                YardGradeId = resolvedGrade,
                UnitCostCents = unitCostCents,
                AcquiredDayIndex = -1,
                State = SawmillToYardTransferJobState.Planned,
                CreatedDayIndex = dayIndex,
            };
            jobs[jobId] = job;
            diagnostics.Add($"SawmillToYardTransferDriver.BeginTransfer: job '{jobId}' began for lot '{millLotId}' "
                + $"at grade '{resolvedGrade}', cost {unitCostCents}c, mill '{job.MillBusinessId}', yard '{job.YardBusinessId}'.");
            return job;
        }

        /// <summary>
        /// The one place yard.ReceiveLumberFromSawmill is called, and the
        /// one place a yard lot id is recorded on a job.
        /// </summary>
        public bool RunTransfer(string jobId, int dayIndex, out string refusal)
        {
            refusal = null;
            if (mill == null)
            {
                refusal = "SawmillToYardTransferDriver.RunTransfer: no sawmill runtime — transfer refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (yard == null)
            {
                refusal = "SawmillToYardTransferDriver.RunTransfer: no lumber yard runtime — transfer refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (string.IsNullOrWhiteSpace(jobId))
            {
                refusal = "SawmillToYardTransferDriver.RunTransfer: no job id supplied — transfer refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (!jobs.TryGetValue(jobId, out SawmillToYardTransferJob job))
            {
                refusal = $"SawmillToYardTransferDriver.RunTransfer: unknown transfer job '{jobId}'.";
                diagnostics.Add(refusal);
                return false;
            }
            if (job.State == SawmillToYardTransferJobState.Transferred || job.State == SawmillToYardTransferJobState.Failed)
            {
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' is {job.State} — a terminal job never transfers again.";
                diagnostics.Add(refusal);
                return false;
            }
            SawmillLumberLot millLot = FindMillLot(job.MillLotId);
            if (millLot == null)
            {
                job.State = SawmillToYardTransferJobState.Failed;
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — lot '{job.MillLotId}' is absent from the mill yard; the job is Failed and a fresh begin is the recovery path.";
                diagnostics.Add(refusal);
                return false;
            }
            if (millLot.LumberUnits <= 0)
            {
                job.State = SawmillToYardTransferJobState.Failed;
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — lot '{job.MillLotId}' holds no units; something else already moved it, the job is Failed.";
                diagnostics.Add(refusal);
                return false;
            }

            int millTotalBefore = mill.LumberStock.TotalLumberUnits;
            int yardTotalBefore = yard.LumberStock.TotalLumberUnits;
            int units = millLot.LumberUnits;

            // C-4 (OD-C5): the transfer is a wholesale PURCHASE. The
            // settlement gate runs BEFORE any custody moves: the ports
            // must remain bound to the right Businesses, and the yard
            // must hold the price for the actual lot units. A transient
            // intake/debit refusal leaves the job Planned and both stocks
            // untouched, so the next day's drive may retry it. A missing
            // authority or insufficient cash closes this purchase attempt.
            // The yard debit is taken
            // now (atomic TryDebit) so an intake refusal can refund it
            // and leave nothing moved; the mill credit follows only
            // after the intake is verified.
            string settlementRefusal = SettlementRefusal(job, units, out int priceCents, out bool purchaseAttemptMustClose);
            if (settlementRefusal != null)
            {
                if (purchaseAttemptMustClose)
                {
                    // No agreement or custody transfer was accepted: this
                    // was only a pending purchase attempt. A missing or
                    // misbound settlement authority, invalid price or cash
                    // rejection is terminal for this attempt, otherwise the
                    // daily driver keeps an impossible Planned job forever.
                    // The mill lot and both cash balances remain untouched;
                    // a new purchase attempt is allowed after affordability
                    // changes.
                    job.State = SawmillToYardTransferJobState.Failed;
                    refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — {settlementRefusal} Purchase attempt closed as Failed; lumber remains mill-owned and no money moved.";
                    diagnostics.Add(refusal);
                    return false;
                }
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — {settlementRefusal}";
                diagnostics.Add(refusal);
                return false;
            }

            if (!string.IsNullOrEmpty(YardCash.TryDebit(priceCents, $"sawmill-to-yard wholesale settlement for job '{jobId}'")))
            {
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — the yard debit of {priceCents}c for {units}u was refused after the affordability check passed; no lumber moved, job stays Planned.";
                diagnostics.Add(refusal);
                return false;
            }

            string intakeRefusal = yard.ReceiveLumberFromSawmill(millLot, job.YardGradeId, dayIndex, job.UnitCostCents, diagnostics);
            if (intakeRefusal != null)
            {
                // The debit is refunded, so nothing moved at all: no
                // custody, no cash, job stays Planned (the wholesale
                // settlement pattern). The production port cannot refuse
                // a non-negative credit on a non-null runtime, so a
                // refund refusal is a port defect: the job FAILS rather
                // than staying Planned, so the next day's drive cannot
                // debit the yard a second time for lumber it never
                // received.
                string refundRefusal = YardCash.Credit(priceCents, $"sawmill-to-yard wholesale settlement refund for job '{jobId}'");
                if (!string.IsNullOrEmpty(refundRefusal))
                {
                    job.State = SawmillToYardTransferJobState.Failed;
                    refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — {intakeRefusal} The yard debit of {priceCents}c could NOT be refunded ({refundRefusal}); the debit STANDS and the job is Failed. Reconcile the books and make the yard whole before a fresh begin.";
                    diagnostics.Add(refusal);
                    return false;
                }
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — {intakeRefusal} The yard debit of {priceCents}c was refunded; no lumber or cash moved.";
                diagnostics.Add(refusal);
                return false;
            }

            // Verification step: the intake's own contract plus the
            // stock-level test make a mismatch unreachable in the current
            // code, but the driver must not record a transfer it cannot
            // prove. The units are already moved; the driver never
            // re-invents them and never re-runs the intake for this job —
            // the production caller must reconcile the stocks.
            string mismatch = VerifyTransfer(job, millLot, millTotalBefore, yardTotalBefore, units, out LumberYardLumberLot yardLot);
            if (mismatch != null)
            {
                // The mill is NOT credited and the yard debit is NOT
                // refunded: the books no longer agree, and silently
                // un-winding either side would hide the break. The job
                // fails loudly for reconciliation.
                job.State = SawmillToYardTransferJobState.Failed;
                job.SettledPriceCents = -1;
                job.SettledDayIndex = -1;
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — the transfer cannot be verified ({mismatch}); the job is Failed and the production caller must reconcile the stocks. The yard debit of {priceCents}c STANDS and the mill was NOT credited.";
                diagnostics.Add(refusal);
                return false;
            }

            string creditRefusal = MillCash.Credit(priceCents, $"sawmill-to-yard wholesale settlement for job '{jobId}'");
            if (!string.IsNullOrEmpty(creditRefusal))
            {
                // The byproduct-settlement precedent: the debit stands,
                // recorded loudly, never re-debited or silently refunded.
                // Custody moved; the job still marks Transferred, but
                // SettledPriceCents stays -1 so the unsettled credit is
                // visible in the job record.
                job.LumberUnits = units;
                job.YardLotId = yardLot.LotId;
                job.AcquiredDayIndex = dayIndex;
                job.TransferredDayIndex = dayIndex;
                job.State = SawmillToYardTransferJobState.Transferred;
                job.SettledPriceCents = -1;
                job.SettledDayIndex = -1;
                refusal = $"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' — custody moved ({units}u) and the yard paid {priceCents}c, but the mill credit of {priceCents}c was refused ({creditRefusal}). The yard debit STANDS; the mill has not been paid. Reconcile the books before the next drive.";
                diagnostics.Add(refusal);
                return true;
            }

            job.LumberUnits = units;
            job.YardLotId = yardLot.LotId;
            job.AcquiredDayIndex = dayIndex;
            job.TransferredDayIndex = dayIndex;
            job.State = SawmillToYardTransferJobState.Transferred;
            job.SettledPriceCents = priceCents;
            job.SettledDayIndex = dayIndex;
            diagnostics.Add($"SawmillToYardTransferDriver.RunTransfer: job '{jobId}' settled {units} lumber units for {priceCents}c "
                + $"(mill lot {job.MillLotId} to yard lot {yardLot.LotId}, grade '{job.YardGradeId}', cost {job.UnitCostCents}c).");
            return true;
        }

        /// <summary>
        /// C-4: the settlement gate. Null means the transfer may settle
        /// for <paramref name="priceCents"/>; a non-null string is the
        /// refusal. Terminal authority, price and affordability failures
        /// close the purchase attempt; transient atomic settlement failures
        /// remain retryable. Ports must be bound to the right Businesses
        /// and the yard must hold the price for the actual lot units. The
        /// price is computed from actual units, never an estimate.
        /// </summary>
        private string SettlementRefusal(SawmillToYardTransferJob job, int units, out int priceCents, out bool purchaseAttemptMustClose)
        {
            priceCents = 0;
            purchaseAttemptMustClose = false;
            if (YardCash == null)
            {
                purchaseAttemptMustClose = true;
                return $"job '{job.JobId}': no yard cash port bound — the yard cannot pay for {units}u; no lumber moved unpaid.";
            }
            if (MillCash == null)
            {
                purchaseAttemptMustClose = true;
                return $"job '{job.JobId}': no mill cash port bound — the mill cannot be credited for {units}u; no lumber moved unpaid.";
            }
            if (!string.Equals(YardCash.BusinessId, yard.BusinessInstanceId, StringComparison.Ordinal))
            {
                purchaseAttemptMustClose = true;
                return $"job '{job.JobId}': the yard cash port belongs to '{YardCash.BusinessId}', not the yard '{yard.BusinessInstanceId}' — a miswired port never settles a sale.";
            }
            if (!string.Equals(MillCash.BusinessId, mill.BusinessInstanceId, StringComparison.Ordinal))
            {
                purchaseAttemptMustClose = true;
                return $"job '{job.JobId}': the mill cash port belongs to '{MillCash.BusinessId}', not the mill '{mill.BusinessInstanceId}' — a miswired port never settles a sale.";
            }
            long totalPrice = (long)units * job.UnitCostCents;
            if (totalPrice <= 0 || totalPrice > int.MaxValue)
            {
                purchaseAttemptMustClose = true;
                return $"job '{job.JobId}': the wholesale price for {units}u at {job.UnitCostCents}c is outside the supported positive settlement range — a transfer is a purchase at a real price.";
            }
            priceCents = (int)totalPrice;
            if (YardCash.BalanceCents < priceCents)
            {
                purchaseAttemptMustClose = true;
                return $"job '{job.JobId}': the yard holds {YardCash.BalanceCents}c but owes {priceCents}c for {units}u at {job.UnitCostCents}c — the lumber stays at the mill until the yard can pay.";
            }
            return null;
        }

        /// <summary>
        /// Abandons a job. The mill lot stays in mill LumberStock
        /// untouched, no yard lot is created. A fresh BeginTransfer for
        /// the same lot is allowed because no open job remains.
        /// </summary>
        public bool FailTransfer(string jobId, int dayIndex, out string refusal)
        {
            refusal = null;
            if (mill == null)
            {
                refusal = "SawmillToYardTransferDriver.FailTransfer: no sawmill runtime — nothing failed.";
                diagnostics.Add(refusal);
                return false;
            }
            if (string.IsNullOrWhiteSpace(jobId))
            {
                refusal = "SawmillToYardTransferDriver.FailTransfer: no job id supplied — nothing failed.";
                diagnostics.Add(refusal);
                return false;
            }
            if (!jobs.TryGetValue(jobId, out SawmillToYardTransferJob job))
            {
                refusal = $"SawmillToYardTransferDriver.FailTransfer: unknown transfer job '{jobId}'.";
                diagnostics.Add(refusal);
                return false;
            }
            if (job.State == SawmillToYardTransferJobState.Transferred || job.State == SawmillToYardTransferJobState.Failed)
            {
                refusal = $"SawmillToYardTransferDriver.FailTransfer: job '{jobId}' is {job.State} — a terminal job cannot fail again.";
                diagnostics.Add(refusal);
                return false;
            }
            job.State = SawmillToYardTransferJobState.Failed;
            diagnostics.Add($"SawmillToYardTransferDriver.FailTransfer: job '{jobId}' failed on day {dayIndex}; the lot stays in the mill yard.");
            return true;
        }

        public SawmillToYardTransferJob GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return null;
            return jobs.TryGetValue(jobId, out SawmillToYardTransferJob job) ? job : null;
        }

        public List<SawmillToYardTransferJob> JobsInState(SawmillToYardTransferJobState state)
        {
            var found = new List<SawmillToYardTransferJob>();
            foreach (SawmillToYardTransferJob job in jobs.Values)
            {
                if (job != null && job.State == state)
                {
                    found.Add(job);
                }
            }
            return found;
        }

        // ---- Save contract (nested DTO, F6 driver pattern) ----

        [Serializable]
        public sealed class SawmillToYardTransferDriverSaveDto
        {
            public List<SawmillToYardTransferJobLink> TransferJobLinks = new List<SawmillToYardTransferJobLink>();

            [Serializable]
            public sealed class SawmillToYardTransferJobLink
            {
                public string JobId = string.Empty;
                public string MillLotId = string.Empty;
                public string MillBusinessId = string.Empty;
                public string YardBusinessId = string.Empty;
                public string YardGradeId = string.Empty;
                public int UnitCostCents;
                public int State;
                public int CreatedDayIndex = -1;
            }
        }

        /// <summary>
        /// Deep copies every open job, Planned only. Transferred and
        /// Failed jobs are terminal and are not persisted: a Transferred
        /// job's yard lot already persists in the yard stock (Pass 9
        /// LumberYard slot) and its mill husk in the mill stock (Pass 9
        /// Sawmill slot); a Failed job's lot stays in the mill yard and a
        /// fresh begin is the recovery path.
        /// </summary>
        public SawmillToYardTransferDriverSaveDto ToSaveDto()
        {
            var dto = new SawmillToYardTransferDriverSaveDto();
            foreach (SawmillToYardTransferJob job in jobs.Values)
            {
                if (job == null) continue;
                if (job.State != SawmillToYardTransferJobState.Planned) continue;
                dto.TransferJobLinks.Add(new SawmillToYardTransferDriverSaveDto.SawmillToYardTransferJobLink
                {
                    JobId = job.JobId,
                    MillLotId = job.MillLotId,
                    MillBusinessId = job.MillBusinessId,
                    YardBusinessId = job.YardBusinessId,
                    YardGradeId = job.YardGradeId,
                    UnitCostCents = job.UnitCostCents,
                    State = (int)job.State,
                    CreatedDayIndex = job.CreatedDayIndex,
                });
            }
            return dto;
        }

        /// <summary>
        /// Clear-then-load. A null DTO clears to empty. Links re-attach
        /// against mill LumberStock Lots (the lot must still be present
        /// with positive units) and against yard LumberStock Lots (the
        /// yard must not already hold a lot with that source — if it
        /// does, the transfer already happened by another path and the
        /// link is dropped). Re-attachment never re-runs the intake and
        /// never re-credits anything.
        /// </summary>
        public void LoadFromSaveDto(SawmillToYardTransferDriverSaveDto dto)
        {
            jobs.Clear();
            if (dto == null || dto.TransferJobLinks == null) return;
            var seenJobIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SawmillToYardTransferDriverSaveDto.SawmillToYardTransferJobLink link in dto.TransferJobLinks)
            {
                if (link == null)
                {
                    diagnostics.Add("SawmillToYardTransferDriver.LoadFromSaveDto: skipped a null link.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.JobId))
                {
                    diagnostics.Add("SawmillToYardTransferDriver.LoadFromSaveDto: skipped a link with no job id.");
                    continue;
                }
                if (!seenJobIds.Add(link.JobId))
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: skipped duplicate link for job '{link.JobId}' — the first link is kept.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.MillLotId))
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — no mill lot id.");
                    continue;
                }
                if (link.State < 0 || link.State > 2)
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — state value {link.State} is outside 0 to 2.");
                    continue;
                }
                if (link.UnitCostCents <= 0)
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — unit cost {link.UnitCostCents}c is not positive. A transfer is a purchase at a real price (C-4); a pre-C-4 zero-cost link is dropped and the lot stays at the mill, where a fresh begin at the wholesale price is the recovery path.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.MillBusinessId) || string.IsNullOrWhiteSpace(link.YardBusinessId))
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — a business id is blank; a transfer always names its mill and its yard.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.YardGradeId) || !LumberYardGradeCatalog.IsKnownGrade(link.YardGradeId))
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — grade '{link.YardGradeId}' is blank or unknown; the loud merchantable default is a begin-time act, never a load-time invention.");
                    continue;
                }
                if (link.State != (int)SawmillToYardTransferJobState.Planned)
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — state {(SawmillToYardTransferJobState)link.State} is terminal; only Planned jobs are persisted.");
                    continue;
                }
                if (mill == null || yard == null)
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: dropped link for job '{link.JobId}' — no mill or yard runtime to re-attach against.");
                    continue;
                }
                SawmillLumberLot lot = FindMillLot(link.MillLotId);
                if (lot == null || lot.LumberUnits <= 0)
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: dropped link for job '{link.JobId}' — lot '{link.MillLotId}' is missing from the mill yard or holds no units.");
                    continue;
                }
                if (FindYardLotBySource(link.MillLotId) != null)
                {
                    diagnostics.Add($"SawmillToYardTransferDriver.LoadFromSaveDto: dropped link for job '{link.JobId}' — the yard already holds a lot from source mill lot '{link.MillLotId}'; the transfer already happened by another path.");
                    continue;
                }
                jobs[link.JobId] = new SawmillToYardTransferJob
                {
                    JobId = link.JobId,
                    MillLotId = link.MillLotId,
                    YardLotId = EntityId.Invalid,
                    MillBusinessId = link.MillBusinessId,
                    YardBusinessId = link.YardBusinessId,
                    LumberUnits = 0,
                    YardGradeId = link.YardGradeId.Trim(),
                    UnitCostCents = link.UnitCostCents,
                    AcquiredDayIndex = -1,
                    State = SawmillToYardTransferJobState.Planned,
                    CreatedDayIndex = link.CreatedDayIndex,
                };
            }
        }

        /// <summary>
        /// First-match lookup by lot id string. The step order in
        /// BeginTransfer implies first-match: in the B5 shape the mill
        /// stock holds a 0-unit husk plus a fresh descriptor sharing one
        /// LotId, and the husk refusal must fire before the
        /// duplicate-source check.
        /// </summary>
        private SawmillLumberLot FindMillLot(string millLotId)
        {
            if (mill == null || mill.LumberStock == null || string.IsNullOrWhiteSpace(millLotId)) return null;
            foreach (SawmillLumberLot lot in mill.LumberStock.Lots)
            {
                if (lot != null && string.Equals(lot.LotId.ToString(), millLotId, StringComparison.Ordinal))
                {
                    return lot;
                }
            }
            return null;
        }

        /// <summary>
        /// The yard lot id is never handed back by the intake — it is
        /// allocated internally — so the driver finds the new yard lot by
        /// scanning for the lot whose SourceSawmillLotId equals the mill
        /// lot id string. The duplicate-source guard guarantees at most
        /// one such lot exists, so the scan is unambiguous.
        /// </summary>
        private LumberYardLumberLot FindYardLotBySource(string millLotId)
        {
            if (yard == null || yard.LumberStock == null || string.IsNullOrWhiteSpace(millLotId)) return null;
            foreach (LumberYardLumberLot lot in yard.LumberStock.Lots)
            {
                if (lot != null && string.Equals(lot.SourceSawmillLotId, millLotId, StringComparison.Ordinal))
                {
                    return lot;
                }
            }
            return null;
        }

        private string VerifyTransfer(SawmillToYardTransferJob job, SawmillLumberLot millLot,
            int millTotalBefore, int yardTotalBefore, int units, out LumberYardLumberLot yardLot)
        {
            yardLot = null;
            if (millLot.LumberUnits != 0)
            {
                return $"the mill lot still holds {millLot.LumberUnits} units after acceptance";
            }
            if (mill.LumberStock.TotalLumberUnits != millTotalBefore - units)
            {
                return $"mill total is {mill.LumberStock.TotalLumberUnits}, expected {millTotalBefore - units}";
            }
            if (yard.LumberStock.TotalLumberUnits != yardTotalBefore + units)
            {
                return $"yard total is {yard.LumberStock.TotalLumberUnits}, expected {yardTotalBefore + units}";
            }
            int matches = 0;
            foreach (LumberYardLumberLot lot in yard.LumberStock.Lots)
            {
                if (lot != null
                    && string.Equals(lot.SourceSawmillLotId, job.MillLotId, StringComparison.Ordinal)
                    && lot.LumberUnits == units)
                {
                    matches++;
                    yardLot = lot;
                }
            }
            if (matches != 1)
            {
                yardLot = null;
                return $"found {matches} yard lots for source '{job.MillLotId}' with {units} units, expected exactly 1";
            }
            return null;
        }
    }
}
