using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// D2C: a project stockpile earmark — physical yard inventory reserved for
    /// a named construction project (Canon §6.4: "A Project Stockpile is
    /// physical inventory earmarked for a named project. It is distinct from
    /// the producing business's ordinary operating reserve, target stock or
    /// sale inventory."). The earmark names the yard lot and the units taken
    /// out of the ordinary sale inventory; the lot itself carries the live
    /// reservation (LumberYardLumberLot.EarmarkedForProjectId /
    /// EarmarkedUnits), so the reservation survives save/load with the stock.
    /// This ledger is the AUDIT TRAIL of earmark and release events.
    /// </summary>
    [Serializable]
    public sealed class LumberYardEarmarkEvent
    {
        public EntityId LotId = EntityId.Invalid;
        public string ProjectId = string.Empty;
        public int Units;
        public bool IsRelease; // false = earmarked, true = released back to sale inventory
        public int DayIndex;
        public string Note = string.Empty;

        public LumberYardEarmarkEvent() { }
    }

    /// <summary>
    /// D2C: the project-stockpile audit ledger. Canon §6.4: "Allocated project
    /// material is not simultaneously available for normal sale unless the
    /// allocation is deliberately released." Earmarks are delegated to the
    /// stock (lot-level reservations); this ledger records every earmark and
    /// release loudly so the reservation history is inspectable. Canon §6.4
    /// also warns that pre-buying ties up capital and risks theft, fire,
    /// warping and deterioration — the yard records the reservation, it does
    /// not price or insure the hedge; that stays with the ledger authorities
    /// and the project system.
    ///
    /// Genuine design fork (recorded, not guessed): Canon §6.4 says
    /// "Pre-buying materials can protect schedule but ties up capital and
    /// storage and can create theft, fire, deterioration, weather, warping,
    /// handling and other material-specific risks." No canon rate or schedule
    /// is given for those risks on stockpiled lumber, and the historical
    /// research annex carries no stockpile-loss calibration — so this pass
    /// implements NO automatic deterioration of earmarked stock. If a later
    /// pass wants stockpile risk, it belongs on the earmark records here as
    /// data, not as an invented decay rate.
    /// </summary>
    public sealed class LumberYardProjectStockpile
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LumberYardEarmarkEvent> history = new List<LumberYardEarmarkEvent>();
        private readonly string yardBusinessId;

        public LumberYardProjectStockpile(string yardBusinessId)
        {
            this.yardBusinessId = yardBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LumberYardEarmarkEvent> History => history;
        public string YardBusinessId => yardBusinessId;

        /// <summary>
        /// Reserves units on a yard lot for a named project. The lot keeps
        /// physical custody; its earmarked portion leaves the ordinary sale
        /// inventory. Refuses loudly: unknown lot, unnamed project,
        /// non-positive units, units beyond the lot's unreserved balance, or a
        /// lot already earmarked for a DIFFERENT project (one lot serves one
        /// project at a time — mixing reservations would lie about what is
        /// promised to whom). Returns the refusal, or null.
        /// </summary>
        public string Earmark(
            LumberYardLumberStock stock,
            EntityId lotId,
            string projectId,
            int units,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (stock == null) return "LumberYardProjectStockpile: no stock to earmark from.";
            if (string.IsNullOrWhiteSpace(projectId))
                return "LumberYardProjectStockpile: the project must be named — anonymous reservations refused.";
            string refusal = stock.EarmarkUnits(lotId, projectId.Trim(), units, diag);
            if (refusal != null) return refusal;

            history.Add(new LumberYardEarmarkEvent
            {
                LotId = lotId,
                ProjectId = projectId.Trim(),
                Units = units,
                IsRelease = false,
                DayIndex = dayIndex,
            });
            diag.Add($"LumberYardProjectStockpile ({yardBusinessId}): earmarked {units} lumber units on yard lot "
                + $"{lotId} for project '{projectId.Trim()}' (day {dayIndex}). "
                + "Reserved stock leaves ordinary sale inventory until deliberately released.");
            return null;
        }

        /// <summary>
        /// Releases earmarked units back to ordinary sale inventory. Partial
        /// releases are allowed. Refuses loudly when the lot carries no such
        /// reservation or the units exceed it. Returns the refusal, or null.
        /// </summary>
        public string Release(
            LumberYardLumberStock stock,
            EntityId lotId,
            int units,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (stock == null) return "LumberYardProjectStockpile: no stock to release into.";
            string projectId;
            string refusal = stock.ReleaseEarmark(lotId, units, out projectId, diag);
            if (refusal != null) return refusal;

            history.Add(new LumberYardEarmarkEvent
            {
                LotId = lotId,
                ProjectId = projectId,
                Units = units,
                IsRelease = true,
                DayIndex = dayIndex,
            });
            diag.Add($"LumberYardProjectStockpile ({yardBusinessId}): released {units} earmarked units on yard lot "
                + $"{lotId} (was reserved for project '{projectId}') back to sale inventory (day {dayIndex}).");
            return null;
        }

        /// <summary>D2C save contract: lives inside the owning ledger class.</summary>
        [Serializable]
        public sealed class LumberYardProjectStockpileSaveDto
        {
            public List<LumberYardEarmarkEvent> History = new List<LumberYardEarmarkEvent>();
        }

        public LumberYardProjectStockpileSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardProjectStockpileSaveDto();
            foreach (var e in history)
            {
                if (e == null) continue;
                dto.History.Add(new LumberYardEarmarkEvent
                {
                    LotId = e.LotId,
                    ProjectId = e.ProjectId,
                    Units = e.Units,
                    IsRelease = e.IsRelease,
                    DayIndex = e.DayIndex,
                    Note = e.Note,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardProjectStockpileSaveDto dto)
        {
            history.Clear();
            if (dto == null || dto.History == null) return;
            foreach (var e in dto.History)
            {
                if (e == null) continue;
                history.Add(e);
            }
        }
    }
}
