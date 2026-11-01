using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// D4L: inspection records and misgrade disputes for the used-equipment
    /// market. Canon Part VI §50 KEEP: uncertain used-asset condition and
    /// inspection; §51: "Diligence combines records with inspection." An
    /// inspection VERIFIES the listing's recorded grade against the asset's
    /// actual condition. A mismatch opens a dispute that is RECORDED ONLY —
    /// the register never auto-resolves, never reprices, never relists. Those
    /// are explicit operator steps (see UsedEquipmentMarketBook).
    /// </summary>

    /// <summary>D4L: what an inspection concluded about the listing's grade.</summary>
    public enum EquipmentInspectionVerdict
    {
        Unspecified = 0,
        /// <summary>Observed condition grades the same as the listing claims.</summary>
        MatchesRecordedGrade = 1,
        /// <summary>Observed condition grades differently — a dispute opens.</summary>
        Misgraded = 2,
    }

    /// <summary>
    /// D4L: one pre-purchase inspection of a listed asset. Signed by a named
    /// inspector, dated, and graded with the SAME taxonomy as the listing so
    /// the comparison is apples-to-apples. The record is evidence (Canon §51),
    /// not a verdict on the seller's honesty — a plow can wear between
    /// listing and inspection.
    /// </summary>
    [Serializable]
    public sealed class EquipmentInspectionRecord
    {
        public EntityId InspectionId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public EntityId ListingId = EntityId.Invalid;
        public string AssetId = string.Empty;

        public string InspectorKind = string.Empty; // "person", "business"
        public string InspectorId = string.Empty;
        public string InspectorName = string.Empty;
        public int DayIndex;

        public float ObservedCondition01;
        public UsedEquipmentConditionGrade ObservedGrade = UsedEquipmentConditionGrade.Unspecified;
        public UsedEquipmentConditionGrade RecordedGrade = UsedEquipmentConditionGrade.Unspecified;
        public EquipmentInspectionVerdict Verdict = EquipmentInspectionVerdict.Unspecified;
        public string Notes = string.Empty;

        public EquipmentInspectionRecord() { }
    }

    /// <summary>D4L: lifecycle of a misgrade dispute — record only.</summary>
    public enum EquipmentDisputeStatus
    {
        Unspecified = 0,
        /// <summary>Flagged by an inspection; stands open. Never auto-resolves.</summary>
        Recorded = 1,
        /// <summary>Operator confirmed the misgrade (explicit step).</summary>
        Upheld = 2,
        /// <summary>Operator found the listing was fair (explicit step).</summary>
        Dismissed = 3,
    }

    /// <summary>
    /// D4L: one misgrade dispute. Opened by an inspection that found the
    /// asset's observed grade differing from the listing's recorded grade.
    /// The dispute carries the evidence; it does NOT change the listing, the
    /// price, or the asset. Resolution is an explicit operator step recorded
    /// on the dispute (UsedEquipmentMarketBook.RecordDisputeResolution).
    /// </summary>
    [Serializable]
    public sealed class EquipmentMisgradeDispute
    {
        public EntityId DisputeId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public EntityId ListingId = EntityId.Invalid;
        public EntityId InspectionId = EntityId.Invalid;
        public string AssetId = string.Empty;

        public UsedEquipmentConditionGrade RecordedGrade = UsedEquipmentConditionGrade.Unspecified;
        public float ObservedCondition01;
        public UsedEquipmentConditionGrade ObservedGrade = UsedEquipmentConditionGrade.Unspecified;

        public string ReporterKind = string.Empty;
        public string ReporterId = string.Empty;
        public string ReporterName = string.Empty;
        public int OpenedDayIndex;

        public EquipmentDisputeStatus Status = EquipmentDisputeStatus.Recorded;
        public int ResolvedDayIndex = -1;
        public string ResolvedBy = string.Empty;
        public string ResolutionNote = string.Empty;

        public EquipmentMisgradeDispute() { }

        public string DisputeKey() => DisputeId.Kind + ":" + DisputeId.Id;
    }

    /// <summary>
    /// D4L: the misgrade dispute register — open, find, and explicit-resolve.
    /// Owned by the UsedEquipmentMarketBook; the save DTO lives here with its
    /// owning register.
    /// </summary>
    public sealed class EquipmentMisgradeDisputeRegister
    {
        private readonly Dictionary<string, EquipmentMisgradeDispute> disputes =
            new Dictionary<string, EquipmentMisgradeDispute>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, EquipmentMisgradeDispute> Disputes => disputes;
        public int Count => disputes.Count;

        public EquipmentMisgradeDisputeRegister() { }

        public EquipmentMisgradeDispute Find(string disputeKey)
        {
            if (string.IsNullOrWhiteSpace(disputeKey)) return null;
            disputes.TryGetValue(disputeKey, out EquipmentMisgradeDispute dispute);
            return dispute;
        }

        public int OpenCount()
        {
            int count = 0;
            foreach (var kv in disputes)
                if (kv.Value != null && kv.Value.Status == EquipmentDisputeStatus.Recorded)
                    count++;
            return count;
        }

        /// <summary>
        /// Opens a dispute from a misgraded inspection. Record-only: no
        /// listing is touched, no price is changed, nothing is resolved.
        /// </summary>
        public EquipmentMisgradeDispute OpenDispute(
            EntityId listingId,
            string assetId,
            UsedEquipmentConditionGrade recordedGrade,
            float observedCondition01,
            EntityId inspectionId,
            string reporterKind,
            string reporterId,
            string reporterName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var dispute = new EquipmentMisgradeDispute
            {
                DisputeId = idRegistry != null ? idRegistry.Allocate(EntityKind.Contract) : EntityId.Invalid,
                ListingId = listingId,
                InspectionId = inspectionId,
                AssetId = assetId ?? string.Empty,
                RecordedGrade = recordedGrade,
                ObservedCondition01 = observedCondition01,
                ObservedGrade = UsedEquipmentConditionGrades.GradeFor(observedCondition01),
                ReporterKind = reporterKind ?? string.Empty,
                ReporterId = reporterId ?? string.Empty,
                ReporterName = reporterName ?? string.Empty,
                OpenedDayIndex = dayIndex,
                Status = EquipmentDisputeStatus.Recorded,
            };
            disputes[dispute.DisputeKey()] = dispute;
            diagnostics.Add(
                $"EquipmentMisgradeDisputeRegister: dispute {dispute.DisputeKey()} opened on listing " +
                $"{listingId.Kind}:{listingId.Id} — recorded {recordedGrade}, observed {dispute.ObservedGrade}. " +
                "Recorded only; awaiting an explicit resolution.");
            return dispute;
        }

        // ---------- save DTO (inside the owning register class) ----------

        [Serializable]
        public sealed class EquipmentMisgradeDisputeRegisterDto
        {
            public List<EquipmentMisgradeDispute> Disputes = new List<EquipmentMisgradeDispute>();
        }

        public EquipmentMisgradeDisputeRegisterDto ToSaveDto()
        {
            var dto = new EquipmentMisgradeDisputeRegisterDto();
            foreach (var kv in disputes) dto.Disputes.Add(kv.Value);
            return dto;
        }

        public void LoadFromSaveDto(EquipmentMisgradeDisputeRegisterDto dto)
        {
            disputes.Clear();
            if (dto == null || dto.Disputes == null) return;
            foreach (EquipmentMisgradeDispute d in dto.Disputes)
            {
                if (d == null || d.DisputeId.Equals(EntityId.Invalid)) continue;
                disputes[d.DisputeKey()] = d;
            }
        }
    }
}
