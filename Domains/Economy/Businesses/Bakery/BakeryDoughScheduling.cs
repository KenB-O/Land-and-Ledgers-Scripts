using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// D1D: the proofing-rack scheduler. Canon Part III §3.1 names proof as a
    /// meaningful bakery task and the canon equipment profile lists the
    /// proofing rack ("expanded racks" are researched expansion equipment).
    ///
    /// The model: between prep and bake, every dough batch must proof. Proof
    /// consumes RACK TIME, not baker labor — each WorkDay the rack provides
    /// (slots × <see cref="BakeryBreadCatalog.ProofMinutesPerSlotPerDay"/>)
    /// proof-minutes, allocated FIFO by proof-entry order. A batch bakes only
    /// when its proof completes AND it wins an oven firing, fuel, and baker
    /// labor. Three gates interact: proofing-rack capacity, oven firings, and
    /// the baker's hands-on minutes — that interaction IS the dough-scheduling
    /// problem. Batches that cannot proof today wait in
    /// <see cref="BakeryBatchStage.Proofing"/> loudly, never silently dropped.
    ///
    /// Fork note (canon-silent): proof credit accrues the same day a batch is
    /// prepped, so a small day's dough still proofs and bakes the same day
    /// (the W2A day rhythm is preserved). All rates are calibration
    /// (Canon Part XV).
    /// </summary>
    public static class BakeryProofScheduler
    {
        /// <summary>
        /// Allocates today's proof budget across proofing batches, FIFO by
        /// proof-entry order. Returns the number of batches whose proof
        /// completed (remaining ≤ 0). Batches that get no budget keep their
        /// remaining minutes and wait — loudly, via the returned waiters.
        /// </summary>
        public static int AllocateProofCredit(
            List<BakeryDoughBatch> proofingBatches,
            int proofingRackSlots,
            int dayIndex,
            List<string> diag,
            out List<BakeryDoughBatch> stillWaiting)
        {
            stillWaiting = new List<BakeryDoughBatch>();
            diag = diag ?? new List<string>();
            int completed = 0;
            if (proofingBatches == null) return completed;

            var ordered = new List<BakeryDoughBatch>(proofingBatches);
            ordered.Sort((a, b) => ProofOrderOf(a).CompareTo(ProofOrderOf(b)));

            int budget = Math.Max(0, proofingRackSlots) * BakeryBreadCatalog.ProofMinutesPerSlotPerDay;
            bool capacityNoted = false;
            foreach (var batch in ordered)
            {
                if (batch == null) continue;
                if (batch.ProofMinutesRemaining > 0 && budget > 0)
                {
                    int credit = Math.Min(budget, batch.ProofMinutesRemaining);
                    batch.ProofMinutesRemaining -= credit;
                    budget -= credit;
                    batch.StageLog.Add($"day {dayIndex}: proofed {credit}m on the rack ({Math.Max(0, batch.ProofMinutesRemaining)}m remaining)");
                }

                if (batch.ProofMinutesRemaining > 0)
                {
                    stillWaiting.Add(batch);
                    if (!capacityNoted)
                    {
                        capacityNoted = true;
                        diag.Add($"BakeryProofScheduler: proofing rack at capacity ({proofingRackSlots} slot(s)) — " +
                            $"batch {batch.BatchId} waits its turn (day {dayIndex}). Plan proof slots ahead of delivery days.");
                    }
                }
                else
                {
                    completed++;
                }
            }

            return completed;
        }

        private static int ProofOrderOf(BakeryDoughBatch batch)
        {
            return batch != null ? batch.ProofOrder : int.MaxValue;
        }
    }
}
