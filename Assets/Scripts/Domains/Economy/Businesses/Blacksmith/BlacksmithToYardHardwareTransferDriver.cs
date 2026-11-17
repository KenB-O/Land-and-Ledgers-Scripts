using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Blacksmith
{
    /// <summary>
    /// D3 (F-D2-1): hardware delivery from the smithy to the lumber yard.
    /// A tested adapter, not a second smithy or yard runtime. It moves
    /// forged construction-hardware pieces (door hinges, iron latches,
    /// door handles) from the smith's shop stock into the yard's hardware
    /// lots through the existing yard intake
    /// (LumberYardShopRuntime.ReceiveHardwareFromBlacksmith), with the
    /// piece breakdown recorded in the lot's SupplierNote, the material lot
    /// ids the pieces were forged from named beside it, and the yard paying the smith the wholesale
    /// unit cost per delivered piece.
    ///
    /// The driver never sets lot state directly. It calls only
    /// smithy.TakeHardware / ReturnHardwareToStock plus the yard intake
    /// and the read-only stock properties. It never touches a GameObject,
    /// grid coordinate, work position, or the forge itself; forging stays
    /// the smith's job, and the delivery starts from pieces already in
    /// shop stock.
    ///
    /// Exactly-once, two authorities: the driver's job record first, and
    /// the yard lot's provenance second. A successful delivery takes each
    /// piece out of smith stock exactly once, and the yard lot it creates
    /// carries the smith, the forged day, the piece breakdown, and the
    /// material lots, so the same piece can never deliver again.
    ///
    /// C-4 (OD-C5) wholesale-settlement pattern, as in the sawmill
    /// transfer: the delivery is a wholesale PURCHASE, not a gift of
    /// custody. The driver moves custody AND cash in one transaction:
    /// the yard pays the smith the job's wholesale unit cost per
    /// delivered piece, through caller-supplied business cash ports (the
    /// existing cash bridge). Affordability is checked before any custody
    /// moves, from the actual piece count and the job's unit cost; the
    /// payment settles on the actual delivered delta, never on an
    /// estimate; a yard that cannot pay keeps the hardware at the smithy.
    /// The unit cost the yard lot carries is the same wholesale price,
    /// recorded on the lot as its cost basis for later pricing. A zero
    /// unit cost is refused at begin: a free lot quotes 1c at retail and
    /// the smith earns nothing (the F6 defect).
    ///
    /// The iron import order id is NOT carried on a forged piece — the
    /// smith's material ledger records lot ids, not order ids — so the
    /// yard lot's IronImportOrderId stays empty ("when known") and the
    /// material lot ids travel in the SupplierNote instead. No order id is
    /// ever invented from a lot id.
    ///
    /// The driver is bound by the bootstrapper
    /// (FirstLedgerSliceBootstrapper.ConfigureBlacksmithYardHardwareWiring)
    /// and driven daily by SimulationDrivers.
    /// </summary>
    [Serializable]
    public sealed class BlacksmithToYardHardwareTransferJob
    {
        public string JobId = string.Empty;
        public List<string> PieceAssetIds = new List<string>();
        public string SmithBusinessId = string.Empty;
        public string YardBusinessId = string.Empty;
        public int HardwareUnits;
        public string PieceBreakdown = string.Empty;
        public string SupplierNote = string.Empty;
        public List<string> MaterialLotIds = new List<string>();
        public int ForgedDayIndex = -1;
        public int UnitCostCents;
        /// <summary>
        /// C-4: the settled wholesale price in cents, or -1 while the job
        /// is unsettled. Set only when the yard debit AND the smith credit
        /// both succeed; the job is the settlement record. A job whose
        /// custody moved but whose smith credit was refused keeps -1 and
        /// names the unreconciled debit in diagnostics.
        /// </summary>
        public int SettledPriceCents = -1;
        public int SettledDayIndex = -1;
        public int AcquiredDayIndex = -1;
        public EntityId YardLotId = EntityId.Invalid;
        public BlacksmithToYardHardwareTransferJobState State = BlacksmithToYardHardwareTransferJobState.Planned;
        public int CreatedDayIndex = -1;
        public int TransferredDayIndex = -1;
        public BlacksmithToYardHardwareTransferJob() { }
    }

    public enum BlacksmithToYardHardwareTransferJobState
    {
        Planned = 0,
        Transferred = 1,
        Failed = 2,
    }

    public sealed class BlacksmithToYardHardwareTransferDriver
    {
        /// <summary>
        /// D3: the smith-to-yard wholesale unit price in cents. Coherence
        /// default 45c — D2's established yard cost basis for one forged
        /// hardware piece, quoting at the yard's price-port cost. Agent D
        /// calibration, provisional; the owner corrects the figure. The
        /// production caller passes this constant at BeginDelivery; the
        /// driver never defaults a cost itself and refuses a zero or
        /// negative one.
        /// </summary>
        public const int WholesaleHardwareUnitCostCents = 45;

        private readonly BlacksmithRuntime smithy;
        private readonly LumberYardShopRuntime yard;
        private readonly List<string> diagnostics;
        private readonly Dictionary<string, BlacksmithToYardHardwareTransferJob> jobs =
            new Dictionary<string, BlacksmithToYardHardwareTransferJob>(StringComparer.Ordinal);

        /// <summary>
        /// D3: the yard's business cash port, bound by the production
        /// caller fresh each drive from the yard business's runtime
        /// state (the byproduct-transfer pattern). Null means no
        /// delivery can settle: RunDelivery refuses loudly rather than
        /// moving hardware unpaid.
        /// </summary>
        public IBusinessIdentityCashPort YardCash { get; set; }

        /// <summary>
        /// D3: the smith's business cash port, bound by the production
        /// caller fresh each drive from the smith business's runtime
        /// state. Null means no delivery can settle.
        /// </summary>
        public IBusinessIdentityCashPort SmithCash { get; set; }

        public BlacksmithToYardHardwareTransferDriver(
            BlacksmithRuntime smithy,
            LumberYardShopRuntime yard,
            List<string> diagnostics)
        {
            this.smithy = smithy;
            this.yard = yard;
            this.diagnostics = diagnostics ?? new List<string>();
            if (smithy == null)
            {
                this.diagnostics.Add("BlacksmithToYardHardwareTransferDriver: no blacksmith runtime — deliveries refused.");
            }
            if (yard == null)
            {
                this.diagnostics.Add("BlacksmithToYardHardwareTransferDriver: no lumber yard runtime — deliveries refused.");
            }
        }

        public BlacksmithRuntime Smithy => smithy;
        public LumberYardShopRuntime Yard => yard;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int TransferJobCount => jobs.Count;
        public IReadOnlyDictionary<string, BlacksmithToYardHardwareTransferJob> TransferJobs => jobs;

        /// <summary>
        /// Begins a delivery job for pieces already in smith shop stock.
        /// Never calls the intake, never moves a piece, never allocates a
        /// yard lot id. A refused begin leaves both stocks untouched.
        /// All pieces in one job share one forged day — one yard lot
        /// carries one forged day — so a mixed-day batch must be split
        /// by the caller.
        /// </summary>
        public BlacksmithToYardHardwareTransferJob BeginDelivery(List<string> pieceAssetIds, int unitCostCents, int dayIndex, out string refusal)
        {
            refusal = null;
            if (smithy == null)
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.BeginDelivery: no blacksmith runtime — delivery refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (yard == null)
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.BeginDelivery: no lumber yard runtime — delivery refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (pieceAssetIds == null || pieceAssetIds.Count == 0)
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.BeginDelivery: no hardware pieces named — delivery refused.";
                diagnostics.Add(refusal);
                return null;
            }
            if (unitCostCents <= 0)
            {
                refusal = $"BlacksmithToYardHardwareTransferDriver.BeginDelivery: delivery names a non-positive unit cost ({unitCostCents}c) — a delivery is a wholesale purchase at a real price; a zero-cost lot quotes 1c at retail and the smith earns nothing (the F6 defect, C-4). Refused.";
                diagnostics.Add(refusal);
                return null;
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string assetId in pieceAssetIds)
            {
                if (string.IsNullOrWhiteSpace(assetId))
                {
                    refusal = "BlacksmithToYardHardwareTransferDriver.BeginDelivery: a piece id is blank — delivery refused.";
                    diagnostics.Add(refusal);
                    return null;
                }
                if (!seen.Add(assetId))
                {
                    refusal = $"BlacksmithToYardHardwareTransferDriver.BeginDelivery: piece '{assetId}' is named twice — a delivery never moves the same piece twice.";
                    diagnostics.Add(refusal);
                    return null;
                }
            }

            int forgedDayIndex = -1;
            foreach (string assetId in pieceAssetIds)
            {
                if (!smithy.Assets.TryGetValue(assetId, out EquipmentAsset asset) || asset == null)
                {
                    refusal = $"BlacksmithToYardHardwareTransferDriver.BeginDelivery: piece '{assetId}' is absent from smith shop stock — delivery must start from forged pieces on hand.";
                    diagnostics.Add(refusal);
                    return null;
                }
                if (!BlacksmithRuntime.IsHardwareKind(asset.Kind))
                {
                    refusal = $"BlacksmithToYardHardwareTransferDriver.BeginDelivery: piece '{assetId}' is a {asset.Kind}, not forged construction hardware — delivery refused.";
                    diagnostics.Add(refusal);
                    return null;
                }
                if (asset.IsReserved)
                {
                    refusal = $"BlacksmithToYardHardwareTransferDriver.BeginDelivery: piece '{assetId}' is reserved by {asset.ReservedBy} — reserved pieces are not delivered.";
                    diagnostics.Add(refusal);
                    return null;
                }
                if (forgedDayIndex < 0)
                {
                    forgedDayIndex = asset.MadeDayIndex;
                }
                else if (asset.MadeDayIndex != forgedDayIndex)
                {
                    refusal = $"BlacksmithToYardHardwareTransferDriver.BeginDelivery: piece '{assetId}' was forged on day {asset.MadeDayIndex}, not day {forgedDayIndex} — one yard lot carries one forged day, so split the delivery by forged day.";
                    diagnostics.Add(refusal);
                    return null;
                }
            }

            var sortedIds = new List<string>(pieceAssetIds);
            sortedIds.Sort(StringComparer.Ordinal);
            string jobId = "hardware-delivery:" + sortedIds[0];
            foreach (string assetId in sortedIds)
            {
                if (IsPieceInOpenJob(assetId))
                {
                    refusal = $"BlacksmithToYardHardwareTransferDriver.BeginDelivery: piece '{assetId}' already has an open delivery job — a retried begin never creates a second job.";
                    diagnostics.Add(refusal);
                    return null;
                }
            }

            string breakdown = BuildPieceBreakdown(sortedIds);
            var materialLotIds = new List<string>();
            var seenLots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string assetId in sortedIds)
            {
                EquipmentAsset asset = smithy.Assets[assetId];
                if (asset.MaterialLotIds == null) continue;
                foreach (string lotId in asset.MaterialLotIds)
                {
                    if (string.IsNullOrWhiteSpace(lotId)) continue;
                    if (seenLots.Add(lotId)) materialLotIds.Add(lotId);
                }
            }
            materialLotIds.Sort(StringComparer.Ordinal);
            string supplierNote = BuildSupplierNote(breakdown, materialLotIds);

            var job = new BlacksmithToYardHardwareTransferJob
            {
                JobId = jobId,
                PieceAssetIds = sortedIds,
                SmithBusinessId = smithy.BusinessInstanceId,
                YardBusinessId = yard.BusinessInstanceId,
                HardwareUnits = sortedIds.Count,
                PieceBreakdown = breakdown,
                SupplierNote = supplierNote,
                MaterialLotIds = materialLotIds,
                ForgedDayIndex = forgedDayIndex,
                UnitCostCents = unitCostCents,
                AcquiredDayIndex = -1,
                State = BlacksmithToYardHardwareTransferJobState.Planned,
                CreatedDayIndex = dayIndex,
            };
            jobs[jobId] = job;
            diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.BeginDelivery: job '{jobId}' began for {sortedIds.Count} pieces ({breakdown}), forged day {forgedDayIndex}, cost {unitCostCents}c, smith '{job.SmithBusinessId}', yard '{job.YardBusinessId}'.");
            return job;
        }

        /// <summary>
        /// The one place smithy.TakeHardware and the yard hardware intake
        /// are called for a job, and the one place a yard lot id is
        /// recorded on a job. Custody is taken first (pieces leave smith
        /// stock into the driver's hands), then the yard debit, then the
        /// intake; any failure after the take restores the pieces to smith
        /// stock and refunds the debit, so the job stays Planned with
        /// nothing moved.
        /// </summary>
        public bool RunDelivery(string jobId, int dayIndex, out string refusal)
        {
            refusal = null;
            if (smithy == null)
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.RunDelivery: no blacksmith runtime — delivery refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (yard == null)
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.RunDelivery: no lumber yard runtime — delivery refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (string.IsNullOrWhiteSpace(jobId))
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.RunDelivery: no job id supplied — delivery refused.";
                diagnostics.Add(refusal);
                return false;
            }
            if (!jobs.TryGetValue(jobId, out BlacksmithToYardHardwareTransferJob job))
            {
                refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: unknown delivery job '{jobId}'.";
                diagnostics.Add(refusal);
                return false;
            }
            if (job.State == BlacksmithToYardHardwareTransferJobState.Transferred || job.State == BlacksmithToYardHardwareTransferJobState.Failed)
            {
                refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' is {job.State} — a terminal job never delivers again.";
                diagnostics.Add(refusal);
                return false;
            }

            // Re-validate the pieces: a piece taken by another path since
            // begin fails the job (the stock changed under it); a reserved
            // piece stays Planned — the reservation is transient and the
            // next day's drive re-attempts it.
            foreach (string assetId in job.PieceAssetIds)
            {
                if (!smithy.Assets.TryGetValue(assetId, out EquipmentAsset asset) || asset == null)
                {
                    job.State = BlacksmithToYardHardwareTransferJobState.Failed;
                    refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — piece '{assetId}' is no longer in smith shop stock; the job is Failed and a fresh begin is the recovery path.";
                    diagnostics.Add(refusal);
                    return false;
                }
                if (!BlacksmithRuntime.IsHardwareKind(asset.Kind))
                {
                    job.State = BlacksmithToYardHardwareTransferJobState.Failed;
                    refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — piece '{assetId}' is now a {asset.Kind}, not forged construction hardware; the job is Failed.";
                    diagnostics.Add(refusal);
                    return false;
                }
                if (asset.IsReserved)
                {
                    refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — piece '{assetId}' is reserved by {asset.ReservedBy}; the job stays Planned and the next day's drive re-attempts it.";
                    diagnostics.Add(refusal);
                    return false;
                }
            }

            int units = job.PieceAssetIds.Count;
            int yardTotalBefore = yard.HardwareStock != null ? yard.HardwareStock.TotalHardwareUnits : 0;

            // C-4 (OD-C5): the delivery is a wholesale PURCHASE. The
            // settlement gate runs BEFORE any custody moves: the ports
            // must be bound to the right Businesses, and the yard must
            // hold the price for the actual pieces. A refusal here leaves
            // the job Planned and both stocks untouched, so the next
            // day's drive re-attempts it.
            string settlementRefusal = SettlementRefusal(job, units, out int priceCents);
            if (settlementRefusal != null)
            {
                refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — {settlementRefusal}";
                diagnostics.Add(refusal);
                return false;
            }

            // Custody first: every piece leaves smith stock into the
            // driver's hands before any cash moves. A take failure after
            // validation cannot happen synchronously, but the unwind is
            // written anyway — taken pieces go back, nothing is lost.
            var taken = new List<EquipmentAsset>();
            foreach (string assetId in job.PieceAssetIds)
            {
                EquipmentAsset asset = smithy.TakeHardware(assetId, diagnostics);
                if (asset == null)
                {
                    RestoreTakenPieces(taken);
                    refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — piece '{assetId}' could not be taken after validation passed; taken pieces were returned to smith stock and the job stays Planned.";
                    diagnostics.Add(refusal);
                    return false;
                }
                taken.Add(asset);
            }

            if (!string.IsNullOrEmpty(YardCash.TryDebit(priceCents, $"smith-to-yard wholesale settlement for job '{jobId}'")))
            {
                RestoreTakenPieces(taken);
                refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — the yard debit of {priceCents}c for {units} pieces was refused after the affordability check passed; pieces returned to smith stock, job stays Planned.";
                diagnostics.Add(refusal);
                return false;
            }

            // The iron import order id is not recoverable from a forged
            // piece — the smith's material ledger records lot ids, not
            // order ids — so the yard lot's IronImportOrderId stays empty
            // ("when known") and the material lots travel in the SupplierNote.
            // No order id is ever invented from a lot id.
            string intakeRefusal = yard.ReceiveHardwareFromBlacksmith(
                LumberYardHardwareKind.SimpleHardware, units, job.SmithBusinessId, job.ForgedDayIndex,
                string.Empty, dayIndex, job.UnitCostCents, diagnostics, job.SupplierNote);
            if (intakeRefusal != null)
            {
                // The debit is refunded and the pieces go back, so nothing
                // moved at all: no custody, no cash, job stays Planned. A
                // refund refusal is a port defect: the job FAILS rather
                // than staying Planned, so the next day's drive cannot
                // debit the yard a second time for hardware it never
                // received — but the pieces are still restored, because
                // they are in the driver's hands, not the yard's.
                RestoreTakenPieces(taken);
                string refundRefusal = YardCash.Credit(priceCents, $"smith-to-yard wholesale settlement refund for job '{jobId}'");
                if (!string.IsNullOrEmpty(refundRefusal))
                {
                    job.State = BlacksmithToYardHardwareTransferJobState.Failed;
                    refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — {intakeRefusal} The yard debit of {priceCents}c could NOT be refunded ({refundRefusal}); the debit STANDS and the job is Failed. The pieces were returned to smith stock. Reconcile the books and make the yard whole before a fresh begin.";
                    diagnostics.Add(refusal);
                    return false;
                }
                refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — {intakeRefusal} The yard debit of {priceCents}c was refunded and the pieces returned to smith stock; no hardware or cash moved.";
                diagnostics.Add(refusal);
                return false;
            }

            // Verification step: the intake's own contract plus the
            // stock-level test make a mismatch unreachable in the current
            // code, but the driver must not record a delivery it cannot
            // prove. The pieces are already moved; the driver never
            // re-invents them and never re-runs the intake for this job —
            // the production caller must reconcile the stocks.
            string mismatch = VerifyDelivery(job, yardTotalBefore, units, out LumberYardHardwareLot yardLot);
            if (mismatch != null)
            {
                // The smith is NOT credited and the yard debit is NOT
                // refunded: the books no longer agree, and silently
                // un-winding either side would hide the break. The job
                // fails loudly for reconciliation. The pieces cannot go
                // back — they are already yard stock.
                job.State = BlacksmithToYardHardwareTransferJobState.Failed;
                job.SettledPriceCents = -1;
                job.SettledDayIndex = -1;
                refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — the delivery cannot be verified ({mismatch}); the job is Failed and the production caller must reconcile the stocks. The yard debit of {priceCents}c STANDS and the smith was NOT credited.";
                diagnostics.Add(refusal);
                return false;
            }

            string creditRefusal = SmithCash.Credit(priceCents, $"smith-to-yard wholesale settlement for job '{jobId}'");
            if (!string.IsNullOrEmpty(creditRefusal))
            {
                // The byproduct-settlement precedent: the debit stands,
                // recorded loudly, never re-debited or silently refunded.
                // Custody moved; the job still marks Transferred, but
                // SettledPriceCents stays -1 so the unsettled credit is
                // visible in the job record.
                job.HardwareUnits = units;
                job.YardLotId = yardLot.LotId;
                job.AcquiredDayIndex = dayIndex;
                job.TransferredDayIndex = dayIndex;
                job.State = BlacksmithToYardHardwareTransferJobState.Transferred;
                job.SettledPriceCents = -1;
                job.SettledDayIndex = -1;
                refusal = $"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' — custody moved ({units} pieces) and the yard paid {priceCents}c, but the smith credit of {priceCents}c was refused ({creditRefusal}). The yard debit STANDS; the smith has not been paid. Reconcile the books before the next drive.";
                diagnostics.Add(refusal);
                return true;
            }

            job.HardwareUnits = units;
            job.YardLotId = yardLot.LotId;
            job.AcquiredDayIndex = dayIndex;
            job.TransferredDayIndex = dayIndex;
            job.State = BlacksmithToYardHardwareTransferJobState.Transferred;
            job.SettledPriceCents = priceCents;
            job.SettledDayIndex = dayIndex;
            diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.RunDelivery: job '{jobId}' settled {units} pieces ({job.PieceBreakdown}) for {priceCents}c "
                + $"(smith '{job.SmithBusinessId}' to yard lot {yardLot.LotId}, forged day {job.ForgedDayIndex}, cost {job.UnitCostCents}c).");
            return true;
        }

        /// <summary>
        /// C-4: the settlement gate. Null means the delivery may settle
        /// for <paramref name="priceCents"/>; a non-null string is the
        /// refusal, and the job stays Planned with nothing moved. Ports
        /// must be bound to the right Businesses (the wholesale
        /// settlement pattern) and the yard must hold the price for the
        /// actual pieces. The price is computed here, from the actual
        /// piece count, so the payment always settles on the delivered
        /// delta, never on an estimate.
        /// </summary>
        private string SettlementRefusal(BlacksmithToYardHardwareTransferJob job, int units, out int priceCents)
        {
            priceCents = 0;
            if (YardCash == null)
            {
                return $"job '{job.JobId}': no yard cash port bound — the yard cannot pay for {units} pieces; no hardware moved unpaid.";
            }
            if (SmithCash == null)
            {
                return $"job '{job.JobId}': no smith cash port bound — the smith cannot be credited for {units} pieces; no hardware moved unpaid.";
            }
            if (!string.Equals(YardCash.BusinessId, yard.BusinessInstanceId, StringComparison.Ordinal))
            {
                return $"job '{job.JobId}': the yard cash port belongs to '{YardCash.BusinessId}', not the yard '{yard.BusinessInstanceId}' — a miswired port never settles a sale.";
            }
            if (!string.Equals(SmithCash.BusinessId, smithy.BusinessInstanceId, StringComparison.Ordinal))
            {
                return $"job '{job.JobId}': the smith cash port belongs to '{SmithCash.BusinessId}', not the smith '{smithy.BusinessInstanceId}' — a miswired port never settles a sale.";
            }
            priceCents = units * job.UnitCostCents;
            if (priceCents <= 0)
            {
                return $"job '{job.JobId}': the wholesale price for {units} pieces at {job.UnitCostCents}c is not positive — a delivery is a purchase at a real price.";
            }
            if (YardCash.BalanceCents < priceCents)
            {
                return $"job '{job.JobId}': the yard holds {YardCash.BalanceCents}c but owes {priceCents}c for {units} pieces at {job.UnitCostCents}c — the hardware stays at the smithy until the yard can pay.";
            }
            return null;
        }

        /// <summary>
        /// Abandons a job. The pieces stay in smith shop stock untouched,
        /// no yard lot is created. A fresh BeginDelivery for the same
        /// pieces is allowed because no open job remains.
        /// </summary>
        public bool FailDelivery(string jobId, int dayIndex, out string refusal)
        {
            refusal = null;
            if (smithy == null)
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.FailDelivery: no blacksmith runtime — nothing failed.";
                diagnostics.Add(refusal);
                return false;
            }
            if (string.IsNullOrWhiteSpace(jobId))
            {
                refusal = "BlacksmithToYardHardwareTransferDriver.FailDelivery: no job id supplied — nothing failed.";
                diagnostics.Add(refusal);
                return false;
            }
            if (!jobs.TryGetValue(jobId, out BlacksmithToYardHardwareTransferJob job))
            {
                refusal = $"BlacksmithToYardHardwareTransferDriver.FailDelivery: unknown delivery job '{jobId}'.";
                diagnostics.Add(refusal);
                return false;
            }
            if (job.State == BlacksmithToYardHardwareTransferJobState.Transferred || job.State == BlacksmithToYardHardwareTransferJobState.Failed)
            {
                refusal = $"BlacksmithToYardHardwareTransferDriver.FailDelivery: job '{jobId}' is {job.State} — a terminal job cannot fail again.";
                diagnostics.Add(refusal);
                return false;
            }
            job.State = BlacksmithToYardHardwareTransferJobState.Failed;
            diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.FailDelivery: job '{jobId}' failed on day {dayIndex}; the pieces stay in the smithy.");
            return true;
        }

        public BlacksmithToYardHardwareTransferJob GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return null;
            return jobs.TryGetValue(jobId, out BlacksmithToYardHardwareTransferJob job) ? job : null;
        }

        public List<BlacksmithToYardHardwareTransferJob> JobsInState(BlacksmithToYardHardwareTransferJobState state)
        {
            var found = new List<BlacksmithToYardHardwareTransferJob>();
            foreach (BlacksmithToYardHardwareTransferJob job in jobs.Values)
            {
                if (job != null && job.State == state)
                {
                    found.Add(job);
                }
            }
            return found;
        }

        /// <summary>
        /// True when a piece is named by an open (Planned) delivery job.
        /// The production begin pass skips such pieces; a Failed job's
        /// pieces are eligible for a fresh begin.
        /// </summary>
        public bool IsPieceInOpenJob(string assetId)
        {
            if (string.IsNullOrWhiteSpace(assetId)) return false;
            foreach (BlacksmithToYardHardwareTransferJob job in jobs.Values)
            {
                if (job == null || job.State != BlacksmithToYardHardwareTransferJobState.Planned) continue;
                if (job.PieceAssetIds == null) continue;
                foreach (string id in job.PieceAssetIds)
                {
                    if (string.Equals(id, assetId, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        // ---- Save contract (nested DTO, F6 driver pattern) ----

        [Serializable]
        public sealed class BlacksmithToYardHardwareTransferDriverSaveDto
        {
            public List<BlacksmithToYardHardwareTransferJobLink> TransferJobLinks = new List<BlacksmithToYardHardwareTransferJobLink>();

            [Serializable]
            public sealed class BlacksmithToYardHardwareTransferJobLink
            {
                public string JobId = string.Empty;
                public List<string> PieceAssetIds = new List<string>();
                public string SmithBusinessId = string.Empty;
                public string YardBusinessId = string.Empty;
                public string PieceBreakdown = string.Empty;
                public string SupplierNote = string.Empty;
                public List<string> MaterialLotIds = new List<string>();
                public int ForgedDayIndex = -1;
                public int UnitCostCents;
                public int State;
                public int CreatedDayIndex = -1;
            }
        }

        /// <summary>
        /// Deep copies every open job, Planned only. Transferred and
        /// Failed jobs are terminal and are not persisted: a Transferred
        /// job's yard lot already persists in the yard stock (Pass 9
        /// LumberYard slot) and its smith pieces are gone from shop
        /// stock; a Failed job's pieces stay in the smithy and a fresh
        /// begin is the recovery path.
        /// </summary>
        public BlacksmithToYardHardwareTransferDriverSaveDto ToSaveDto()
        {
            var dto = new BlacksmithToYardHardwareTransferDriverSaveDto();
            foreach (BlacksmithToYardHardwareTransferJob job in jobs.Values)
            {
                if (job == null) continue;
                if (job.State != BlacksmithToYardHardwareTransferJobState.Planned) continue;
                dto.TransferJobLinks.Add(new BlacksmithToYardHardwareTransferDriverSaveDto.BlacksmithToYardHardwareTransferJobLink
                {
                    JobId = job.JobId,
                    PieceAssetIds = new List<string>(job.PieceAssetIds ?? new List<string>()),
                    SmithBusinessId = job.SmithBusinessId,
                    YardBusinessId = job.YardBusinessId,
                    PieceBreakdown = job.PieceBreakdown,
                    SupplierNote = job.SupplierNote,
                    MaterialLotIds = new List<string>(job.MaterialLotIds ?? new List<string>()),
                    ForgedDayIndex = job.ForgedDayIndex,
                    UnitCostCents = job.UnitCostCents,
                    State = (int)job.State,
                    CreatedDayIndex = job.CreatedDayIndex,
                });
            }
            return dto;
        }

        /// <summary>
        /// Clear-then-load. A null DTO clears to empty. Links re-attach
        /// against smith shop stock (every piece must still be present,
        /// hardware, and unreserved) and against yard hardware stock
        /// (the yard must not already hold a lot for this delivery — if
        /// it does, the delivery already happened by another path and the
        /// link is dropped). Re-attachment never re-runs the intake and
        /// never re-credits anything.
        /// </summary>
        public void LoadFromSaveDto(BlacksmithToYardHardwareTransferDriverSaveDto dto)
        {
            jobs.Clear();
            if (dto == null || dto.TransferJobLinks == null) return;
            var seenJobIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BlacksmithToYardHardwareTransferDriverSaveDto.BlacksmithToYardHardwareTransferJobLink link in dto.TransferJobLinks)
            {
                if (link == null)
                {
                    diagnostics.Add("BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped a null link.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.JobId))
                {
                    diagnostics.Add("BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped a link with no job id.");
                    continue;
                }
                if (!seenJobIds.Add(link.JobId))
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped duplicate link for job '{link.JobId}' — the first link is kept.");
                    continue;
                }
                if (link.PieceAssetIds == null || link.PieceAssetIds.Count == 0)
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — no pieces named.");
                    continue;
                }
                if (link.State < 0 || link.State > 2)
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — state value {link.State} is outside 0 to 2.");
                    continue;
                }
                if (link.UnitCostCents <= 0)
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — unit cost {link.UnitCostCents}c is not positive. A delivery is a purchase at a real price (C-4); a zero-cost link is dropped and the pieces stay at the smithy, where a fresh begin at the wholesale price is the recovery path.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.SmithBusinessId) || string.IsNullOrWhiteSpace(link.YardBusinessId))
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — a business id is blank; a delivery always names its smith and its yard.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(link.PieceBreakdown) || string.IsNullOrWhiteSpace(link.SupplierNote))
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — the piece breakdown or supplier note is blank; a delivery always records what it moves.");
                    continue;
                }
                if (link.State != (int)BlacksmithToYardHardwareTransferJobState.Planned)
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: skipped link for job '{link.JobId}' — state {(BlacksmithToYardHardwareTransferJobState)link.State} is terminal; only Planned jobs are persisted.");
                    continue;
                }
                if (smithy == null || yard == null)
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: dropped link for job '{link.JobId}' — no smith or yard runtime to re-attach against.");
                    continue;
                }
                bool piecesOk = true;
                foreach (string assetId in link.PieceAssetIds)
                {
                    if (string.IsNullOrWhiteSpace(assetId)
                        || !smithy.Assets.TryGetValue(assetId, out EquipmentAsset asset) || asset == null
                        || !BlacksmithRuntime.IsHardwareKind(asset.Kind) || asset.IsReserved)
                    {
                        diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: dropped link for job '{link.JobId}' — piece '{assetId}' is missing from smith shop stock, not hardware, or reserved.");
                        piecesOk = false;
                        break;
                    }
                }
                if (!piecesOk) continue;
                if (FindYardLotByDelivery(link.SmithBusinessId, link.ForgedDayIndex, link.PieceAssetIds.Count, link.SupplierNote) != null)
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver.LoadFromSaveDto: dropped link for job '{link.JobId}' — the yard already holds a lot for this delivery; the delivery already happened by another path.");
                    continue;
                }
                jobs[link.JobId] = new BlacksmithToYardHardwareTransferJob
                {
                    JobId = link.JobId,
                    PieceAssetIds = new List<string>(link.PieceAssetIds),
                    SmithBusinessId = link.SmithBusinessId,
                    YardBusinessId = link.YardBusinessId,
                    HardwareUnits = link.PieceAssetIds.Count,
                    PieceBreakdown = link.PieceBreakdown,
                    SupplierNote = link.SupplierNote,
                    MaterialLotIds = new List<string>(link.MaterialLotIds ?? new List<string>()),
                    ForgedDayIndex = link.ForgedDayIndex,
                    UnitCostCents = link.UnitCostCents,
                    AcquiredDayIndex = -1,
                    State = BlacksmithToYardHardwareTransferJobState.Planned,
                    CreatedDayIndex = link.CreatedDayIndex,
                };
            }
        }

        /// <summary>
        /// Builds the piece breakdown for a job: counts per hardware kind,
        /// kinds sorted by name for determinism ("4 door-hinge, 2
        /// iron-latch, 1 door-handle").
        /// </summary>
        private string BuildPieceBreakdown(List<string> sortedPieceAssetIds)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string assetId in sortedPieceAssetIds)
            {
                EquipmentAsset asset = smithy.Assets[assetId];
                if (asset == null) continue;
                string kind = asset.Kind ?? string.Empty;
                counts.TryGetValue(kind, out int count);
                counts[kind] = count + 1;
            }
            var kinds = new List<string>(counts.Keys);
            kinds.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string kind in kinds)
            {
                parts.Add($"{counts[kind]} {kind}");
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// Builds the yard lot's SupplierNote: the piece breakdown, plus
        /// the material lot ids the pieces were forged from (the order id is
        /// not recoverable from a piece, so the lots are named, never an
        /// invented order id).
        /// </summary>
        private static string BuildSupplierNote(string pieceBreakdown, List<string> materialLotIds)
        {
            if (materialLotIds == null || materialLotIds.Count == 0) return pieceBreakdown;
            return $"{pieceBreakdown} | material lots {string.Join(", ", materialLotIds.ToArray())}";
        }

        /// <summary>
        /// Returns taken pieces to smith shop stock (the unwind path).
        /// A return refusal is recorded loudly — a piece the driver holds
        /// but cannot return is a custody break, never silently dropped.
        /// </summary>
        private void RestoreTakenPieces(List<EquipmentAsset> taken)
        {
            if (taken == null) return;
            foreach (EquipmentAsset asset in taken)
            {
                if (asset == null) continue;
                string refusal = smithy.ReturnHardwareToStock(asset, diagnostics);
                if (refusal != null)
                {
                    diagnostics.Add($"BlacksmithToYardHardwareTransferDriver: UNWIND FAILED — piece '{asset.AssetId}' could not be returned to smith stock ({refusal}); reconcile the stocks.");
                }
            }
        }

        /// <summary>
        /// The yard lot id is never handed back by the intake — it is
        /// allocated internally — so the driver finds the new yard lot by
        /// scanning for the lot whose smith, forged day, unit count, and
        /// supplier note match the job. The note carries the job's piece
        /// breakdown and material lots, so the scan is unambiguous for one
        /// delivery; a second identical delivery the same day would match
        /// the same lot, which is why the job record (not this scan) is
        /// the exactly-once authority.
        /// </summary>
        private LumberYardHardwareLot FindYardLotByDelivery(string smithBusinessId, int forgedDayIndex, int units, string supplierNote)
        {
            if (yard == null || yard.HardwareStock == null) return null;
            LumberYardHardwareLot found = null;
            int matches = 0;
            foreach (LumberYardHardwareLot lot in yard.HardwareStock.Lots)
            {
                if (lot == null) continue;
                if (!string.Equals(lot.SourceBlacksmithBusinessId, smithBusinessId, StringComparison.Ordinal)) continue;
                if (lot.ForgedDayIndex != forgedDayIndex) continue;
                if (lot.HardwareUnits != units) continue;
                if (!string.Equals(lot.SupplierNote, supplierNote, StringComparison.Ordinal)) continue;
                matches++;
                found = lot;
            }
            return matches == 1 ? found : null;
        }

        private string VerifyDelivery(BlacksmithToYardHardwareTransferJob job, int yardTotalBefore, int units, out LumberYardHardwareLot yardLot)
        {
            yardLot = null;
            if (yard.HardwareStock == null)
            {
                return "the yard has no hardware stock to verify against";
            }
            if (yard.HardwareStock.TotalHardwareUnits != yardTotalBefore + units)
            {
                return $"yard hardware total is {yard.HardwareStock.TotalHardwareUnits}, expected {yardTotalBefore + units}";
            }
            yardLot = FindYardLotByDelivery(job.SmithBusinessId, job.ForgedDayIndex, units, job.SupplierNote);
            if (yardLot == null)
            {
                return "no single yard lot matches this delivery (smith, forged day, units, supplier note)";
            }
            return null;
        }
    }
}
