using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3A: one batch of dirty linen — sheets/towels used in a room-night
    /// turnover, waiting for the wash. The source lot of each set is kept
    /// so the laundry return's provenance names real lots, never a vague
    /// "washed" flag.
    /// </summary>
    [Serializable]
    public sealed class HotelDirtyLinenBatch
    {
        public EntityId SourceLotId = EntityId.Invalid;
        public string SourceProvenanceChain = string.Empty;
        public int Sets;

        public HotelDirtyLinenBatch() { }
    }

    /// <summary>
    /// W3A: the hotel's housekeeping — bed turnover linen with provenance
    /// and the in-house laundry. Canon §8.1E: "Cooking, cleaning, laundry,
    /// bed turnover ... all consume work"; the canon equipment table gives
    /// the hotel keeper "linens" as a real input.
    ///
    /// The linen loop is honest under the upstream-provenance doctrine:
    /// - each guest-night turnover dispenses one clean linen set from the
    ///   shelf (a real purchase lot or the bootstrap endowment), and the
    ///   used set goes into the dirty queue with its source lot recorded;
    /// - laundry consumes real soap portions and real housekeeping labor
    ///   minutes (supplied by the caller, like the boarding-house
    ///   kitchen's labor), and returns clean sets to the shelf as a
    ///   laundry-return lot whose provenance names the source lots and
    ///   the soap lots — recycled physical stock, never conjured;
    /// - only worn-out or lost sets leave the loop, replaced by real
    ///   purchases through import orders. No synthetic stock, ever.
    /// </summary>
    public sealed class HotelHousekeeping
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly HotelConsumableStock linenStock = new HotelConsumableStock();
        private readonly List<HotelDirtyLinenBatch> dirtyLinen = new List<HotelDirtyLinenBatch>();
        private readonly EntityIdRegistry idRegistry;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public HotelConsumableStock LinenStock => linenStock;
        public IReadOnlyList<HotelDirtyLinenBatch> DirtyLinen => dirtyLinen;

        /// <summary>TUNING: soap portions consumed to wash one linen set (calibration, Canon Part XV).</summary>
        public const int SoapPortionsPerLinenSet = 1;

        /// <summary>TUNING: housekeeping labor minutes to wash one linen set (calibration).</summary>
        public const int LaborMinutesPerLinenSet = 30;

        /// <summary>TUNING: clean linen sets turned over per guest-night (one fresh set per occupied bed-night).</summary>
        public const int LinenSetsPerGuestNight = 1;

        public HotelHousekeeping(EntityIdRegistry idRegistry)
        {
            this.idRegistry = idRegistry;
        }

        /// <summary>One-time opening linen/soap endowment — explicit, flagged, never auto-replenished.</summary>
        public void ApplyOpeningLinenEndowment(int dayIndex, List<string> diag)
        {
            HotelConsumableBootstrap.ApplyBootstrapEndowment(linenStock, idRegistry, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// Turns over the bed for one guest-night: dispenses one clean
        /// linen set and queues the used set as dirty. Returns the
        /// dispense lines (provenance for the room-night sale record), or
        /// null on shortfall — the night is then recorded linenless,
        /// loudly, never faked.
        /// </summary>
        public List<HotelConsumableDispenseLine> TurnOverForGuestNight(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            List<HotelConsumableDispenseLine> lines =
                linenStock.TryDispenseUnits(HotelConsumableSupply.LinenMaterialId, LinenSetsPerGuestNight, dayIndex, diag);
            if (lines == null)
            {
                diag.Add($"HotelHousekeeping: no clean linen for the turnover (day {dayIndex}) — the night is recorded without a linen set.");
                return null;
            }

            foreach (HotelConsumableDispenseLine line in lines)
            {
                dirtyLinen.Add(new HotelDirtyLinenBatch
                {
                    SourceLotId = line.LotId,
                    SourceProvenanceChain = line.ProvenanceChain ?? string.Empty,
                    Sets = line.UnitsTaken,
                });
            }
            return lines;
        }

        public int DirtySetsCount
        {
            get
            {
                int total = 0;
                foreach (HotelDirtyLinenBatch batch in dirtyLinen)
                    if (batch != null) total += Math.Max(0, batch.Sets);
                return total;
            }
        }

        /// <summary>
        /// Washes dirty linen: limited by dirty sets on hand, soap portions
        /// on the shelf, and housekeeping labor minutes supplied. Returns
        /// the sets washed (and the labor minutes consumed, via
        /// laborMinutesConsumed). Clean sets return to the shelf as a
        /// laundry-return lot with full source provenance.
        /// </summary>
        public int Launder(int dayIndex, int laborMinutesAvailable, out int laborMinutesConsumed, List<string> diag)
        {
            diag = diag ?? diagnostics;
            laborMinutesConsumed = 0;
            if (idRegistry == null)
            {
                diag.Add("HotelHousekeeping.Launder: no id registry — the wash cannot be recorded.");
                return 0;
            }
            if (dayIndex < 0)
            {
                diag.Add("HotelHousekeeping.Launder: refused — laundry needs a real day index.");
                return 0;
            }

            int dirty = DirtySetsCount;
            int soapOnHand = linenStock.UnitsOnHand(HotelConsumableSupply.SoapMaterialId);
            int bySoap = SoapPortionsPerLinenSet > 0 ? soapOnHand / SoapPortionsPerLinenSet : 0;
            int byLabor = LaborMinutesPerLinenSet > 0 ? Math.Max(0, laborMinutesAvailable) / LaborMinutesPerLinenSet : 0;
            int toWash = Math.Min(dirty, Math.Min(bySoap, byLabor));
            if (toWash <= 0)
            {
                diag.Add($"HotelHousekeeping: no laundry — dirty {dirty} set(s), soap {soapOnHand} portion(s), {laborMinutesAvailable} labor minute(s) available.");
                return 0;
            }

            List<HotelConsumableDispenseLine> soapLines = linenStock.TryDispenseUnits(
                HotelConsumableSupply.SoapMaterialId, toWash * SoapPortionsPerLinenSet, dayIndex, diag);
            if (soapLines == null)
            {
                diag.Add("HotelHousekeeping.Launder: soap dispense failed — the wash is cancelled, nothing half-done.");
                return 0;
            }

            // FIFO from the dirty queue, recording source provenance.
            var sourceChains = new List<string>();
            int remaining = toWash;
            while (remaining > 0 && dirtyLinen.Count > 0)
            {
                HotelDirtyLinenBatch batch = dirtyLinen[0];
                if (batch == null || batch.Sets <= 0) { dirtyLinen.RemoveAt(0); continue; }
                int take = Math.Min(remaining, batch.Sets);
                batch.Sets -= take;
                remaining -= take;
                string chain = string.IsNullOrWhiteSpace(batch.SourceProvenanceChain) ? $"lot {batch.SourceLotId}" : batch.SourceProvenanceChain;
                sourceChains.Add($"{take} set(s) from {chain}");
                if (batch.Sets <= 0) dirtyLinen.RemoveAt(0);
            }

            var soapChains = new StringBuilder();
            foreach (HotelConsumableDispenseLine line in soapLines)
            {
                if (soapChains.Length > 0) soapChains.Append("; ");
                soapChains.Append($"{line.UnitsTaken} portion(s) from {line.ProvenanceChain}");
            }
            string returnSources = string.Join(" | ", sourceChains) + " || soap: " + soapChains;

            string rejection = linenStock.ReceiveLot(new HotelConsumableLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ConsumableName = HotelConsumableSupply.LinenMaterialId,
                Units = toWash,
                AcquiredDayIndex = dayIndex,
                IsLaundryReturn = true,
                LaundryReturnSourceChains = returnSources,
            }, diag);
            if (rejection != null)
            {
                diag.Add($"HotelHousekeeping.Launder: {rejection} — the wash is cancelled, nothing half-done.");
                return 0;
            }

            laborMinutesConsumed = toWash * LaborMinutesPerLinenSet;
            diag.Add($"HotelHousekeeping: laundered {toWash} linen set(s) (day {dayIndex}) — {toWash * SoapPortionsPerLinenSet} soap portion(s), {laborMinutesConsumed} labor minute(s). Clean sets returned as a laundry lot.");
            return toWash;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelHousekeepingSaveDto
        {
            public HotelConsumableStock.HotelConsumableStockSaveDto LinenStock = new HotelConsumableStock.HotelConsumableStockSaveDto();
            public List<HotelDirtyLinenBatch> DirtyLinen = new List<HotelDirtyLinenBatch>();
        }

        public HotelHousekeepingSaveDto CaptureSaveDto()
        {
            var dto = new HotelHousekeepingSaveDto { LinenStock = linenStock.CaptureSaveDto() };
            foreach (HotelDirtyLinenBatch batch in dirtyLinen)
            {
                if (batch == null) continue;
                dto.DirtyLinen.Add(new HotelDirtyLinenBatch
                {
                    SourceLotId = batch.SourceLotId,
                    SourceProvenanceChain = batch.SourceProvenanceChain ?? string.Empty,
                    Sets = Math.Max(0, batch.Sets),
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(HotelHousekeepingSaveDto dto)
        {
            dirtyLinen.Clear();
            linenStock.LoadFromSaveDto(dto?.LinenStock);
            if (dto?.DirtyLinen == null) return;
            foreach (HotelDirtyLinenBatch batch in dto.DirtyLinen)
            {
                if (batch == null || batch.Sets <= 0) continue;
                dirtyLinen.Add(new HotelDirtyLinenBatch
                {
                    SourceLotId = batch.SourceLotId,
                    SourceProvenanceChain = batch.SourceProvenanceChain ?? string.Empty,
                    Sets = batch.Sets,
                });
            }
        }
        #endregion
    }
}
