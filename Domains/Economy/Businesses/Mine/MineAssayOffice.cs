using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>Lifecycle of one assay sample at the office.</summary>
    public enum MineAssaySampleStatus
    {
        Unspecified = 0,
        Received = 1,   // registered, waiting for bench time
        InAnalysis = 2, // on the bench
        Reported = 3,  // analysis complete, report issued
    }

    /// <summary>
    /// D3C: one sample received by the assay office (Canon 21.7F). The sample
    /// names its source lot and HOW it was obtained — the Canon's key
    /// distinction is sampling quality versus analytical quality: a competent
    /// assayer can accurately test an unrepresentative hand-picked sample, so
    /// buyers care both about who assayed the material and how the sample
    /// was obtained. Both are recorded here, never inferred later.
    /// </summary>
    [Serializable]
    public sealed class MineAssaySample
    {
        [SerializeField]
        private string sampleId = string.Empty;

        [SerializeField]
        private EntityId lotId = EntityId.Invalid;

        [SerializeField]
        private EntityId samplerPersonId = EntityId.Invalid;

        [SerializeField, TextArea(1, 2)]
        private string samplingNote = string.Empty;

        [SerializeField, Min(0)]
        private int receivedDayIndex;

        [SerializeField]
        private bool rush;

        [SerializeField]
        private MineAssaySampleStatus status = MineAssaySampleStatus.Received;

        [SerializeField, Min(-1)]
        private int analysisBegunDayIndex = -1;

        [SerializeField]
        private string reportId = string.Empty;

        public string SampleId => sampleId ?? string.Empty;
        public EntityId LotId => lotId;
        public EntityId SamplerPersonId => samplerPersonId;
        public string SamplingNote => samplingNote ?? string.Empty;
        public int ReceivedDayIndex => Math.Max(0, receivedDayIndex);
        public bool Rush => rush;
        public MineAssaySampleStatus Status => status;
        public int AnalysisBegunDayIndex => analysisBegunDayIndex;
        public string ReportId => reportId ?? string.Empty;

        public MineAssaySample() { }

        public MineAssaySample(string sampleId, EntityId lotId, EntityId samplerPersonId,
            string samplingNote, int receivedDayIndex, bool rush)
        {
            this.sampleId = sampleId ?? string.Empty;
            this.lotId = lotId;
            this.samplerPersonId = samplerPersonId;
            this.samplingNote = samplingNote ?? string.Empty;
            this.receivedDayIndex = Math.Max(0, receivedDayIndex);
            this.rush = rush;
        }

        public void MarkAnalysisBegun(int dayIndex)
        {
            status = MineAssaySampleStatus.InAnalysis;
            analysisBegunDayIndex = Math.Max(0, dayIndex);
        }

        public void MarkReported(string reportId)
        {
            status = MineAssaySampleStatus.Reported;
            this.reportId = reportId ?? string.Empty;
        }

        public MineAssaySampleSaveDto CaptureSaveDto()
        {
            return new MineAssaySampleSaveDto
            {
                sampleId = SampleId,
                lotKind = (int)lotId.Kind,
                lotSeq = lotId.Id,
                samplerKind = (int)samplerPersonId.Kind,
                samplerSeq = samplerPersonId.Id,
                samplingNote = SamplingNote,
                receivedDayIndex = ReceivedDayIndex,
                rush = rush,
                status = status,
                analysisBegunDayIndex = analysisBegunDayIndex,
                reportId = ReportId,
            };
        }

        public static MineAssaySample FromSaveDto(MineAssaySampleSaveDto dto)
        {
            if (dto == null)
                return null;
            var sample = new MineAssaySample(
                dto.sampleId ?? string.Empty,
                new EntityId { Kind = (EntityKind)dto.lotKind, Id = dto.lotSeq },
                new EntityId { Kind = (EntityKind)dto.samplerKind, Id = dto.samplerSeq },
                dto.samplingNote ?? string.Empty, dto.receivedDayIndex, dto.rush)
            {
                status = dto.status,
                analysisBegunDayIndex = dto.analysisBegunDayIndex,
                reportId = dto.reportId ?? string.Empty,
            };
            return sample;
        }
    }

    /// <summary>
    /// D3C: one issued assay report — the office's product (Canon 21.7F:
    /// professional reputation affects how lenders, investors and buyers
    /// treat reports because credibility is the service being sold). The
    /// report carries the assay id, the office name, and the sampling note
    /// reference so a reader can judge both analytical and sampling quality.
    /// Reputation effects on pricing/terms are NOT invented here — the office
    /// records counts (issued, disputed, rush on-time) as honest data.
    /// </summary>
    [Serializable]
    public sealed class MineAssayReport
    {
        [SerializeField]
        private string reportId = string.Empty;

        [SerializeField]
        private string assayId = string.Empty;

        [SerializeField]
        private string sampleId = string.Empty;

        [SerializeField]
        private string officeName = string.Empty;

        [SerializeField, Min(0)]
        private int issuedDayIndex;

        [SerializeField]
        private bool rush;

        [SerializeField, Min(0)]
        private int feeCents;

        [SerializeField]
        private bool feePaid;

        [SerializeField]
        private bool disputed;

        [SerializeField, TextArea(1, 2)]
        private string disputeNote = string.Empty;

        public string ReportId => reportId ?? string.Empty;
        public string AssayId => assayId ?? string.Empty;
        public string SampleId => sampleId ?? string.Empty;
        public string OfficeName => officeName ?? string.Empty;
        public int IssuedDayIndex => Math.Max(0, issuedDayIndex);
        public bool Rush => rush;
        public int FeeCents => Math.Max(0, feeCents);
        public bool FeePaid => feePaid;
        public bool Disputed => disputed;
        public string DisputeNote => disputeNote ?? string.Empty;

        public MineAssayReport() { }

        public MineAssayReport(string reportId, string assayId, string sampleId, string officeName,
            int issuedDayIndex, bool rush, int feeCents, bool feePaid)
        {
            this.reportId = reportId ?? string.Empty;
            this.assayId = assayId ?? string.Empty;
            this.sampleId = sampleId ?? string.Empty;
            this.officeName = officeName ?? string.Empty;
            this.issuedDayIndex = Math.Max(0, issuedDayIndex);
            this.rush = rush;
            this.feeCents = Math.Max(0, feeCents);
            this.feePaid = feePaid;
        }

        public void MarkDisputed(string note)
        {
            disputed = true;
            disputeNote = note ?? string.Empty;
        }

        public MineAssayReportSaveDto CaptureSaveDto()
        {
            return new MineAssayReportSaveDto
            {
                reportId = ReportId,
                assayId = AssayId,
                sampleId = SampleId,
                officeName = OfficeName,
                issuedDayIndex = IssuedDayIndex,
                rush = rush,
                feeCents = FeeCents,
                feePaid = feePaid,
                disputed = disputed,
                disputeNote = DisputeNote,
            };
        }

        public static MineAssayReport FromSaveDto(MineAssayReportSaveDto dto)
        {
            if (dto == null)
                return null;
            var report = new MineAssayReport(
                dto.reportId ?? string.Empty, dto.assayId ?? string.Empty, dto.sampleId ?? string.Empty,
                dto.officeName ?? string.Empty, dto.issuedDayIndex, dto.rush, dto.feeCents, dto.feePaid);
            if (dto.disputed)
                report.MarkDisputed(dto.disputeNote ?? string.Empty);
            return report;
        }
    }

    /// <summary>
    /// D3C: the assay office desk (Canon 21.7F) — samples are received,
    /// registered, queued, prepared/analyzed and reported. Rush turnaround
    /// jumps the queue and is priced separately. Capacity is a caller-set
    /// TUNING parameter (analyses per day); the office never invents bench
    /// time. A large mine can run this desk internally (its own bench and
    /// assayer); small operators purchase it externally — the same desk
    /// serves both.
    /// </summary>
    [Serializable]
    public sealed class MineAssayOffice
    {
        [SerializeField]
        private string officeName = string.Empty;

        /// <summary>TUNING (calibration): analyses the bench can begin per day.</summary>
        [SerializeField, Min(1)]
        private int samplesPerDayCapacity = 1;

        /// <summary>TUNING (calibration): standard fee per reported assay, cents.</summary>
        [SerializeField, Min(0)]
        private int standardFeeCents;

        /// <summary>TUNING (calibration): rush fee per reported assay, cents.</summary>
        [SerializeField, Min(0)]
        private int rushFeeCents;

        [SerializeField]
        private List<MineAssaySample> samples = new List<MineAssaySample>();

        [SerializeField]
        private List<MineAssayReport> reports = new List<MineAssayReport>();

        [SerializeField, Min(0)]
        private int nextSampleNumber = 1;

        [SerializeField, Min(0)]
        private int nextReportNumber = 1;

        [SerializeField, Min(-1)]
        private int lastAnalysisDayIndex = -1;

        [SerializeField, Min(0)]
        private int analysesBegunOnDay;

        [SerializeField, Min(0)]
        private int rushCompletedOnTime;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<MineAssaySample> Samples => samples;
        public IReadOnlyList<MineAssayReport> Reports => reports;
        public string OfficeName => officeName ?? string.Empty;
        public int SamplesPerDayCapacity => Math.Max(1, samplesPerDayCapacity);
        public int StandardFeeCents => Math.Max(0, standardFeeCents);
        public int RushFeeCents => Math.Max(0, rushFeeCents);

        public MineAssayOffice() { }

        public MineAssayOffice(string officeName, int samplesPerDayCapacity, int standardFeeCents, int rushFeeCents)
        {
            this.officeName = officeName ?? string.Empty;
            this.samplesPerDayCapacity = Math.Max(1, samplesPerDayCapacity);
            this.standardFeeCents = Math.Max(0, standardFeeCents);
            this.rushFeeCents = Math.Max(0, rushFeeCents);
        }

        /// <summary>Receives and registers a sample. The lot and the sampler must be real.</summary>
        public MineAssaySample RegisterSample(EntityId lotId, EntityId samplerPersonId, string samplingNote,
            int dayIndex, bool rush, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (lotId.Equals(EntityId.Invalid))
            {
                callerDiagnostics.Add("MineAssayOffice.RegisterSample: a real ore-lot id is required — no anonymous samples.");
                return null;
            }
            if (samplerPersonId.Equals(EntityId.Invalid) || samplerPersonId.Kind != EntityKind.Person)
            {
                callerDiagnostics.Add("MineAssayOffice.RegisterSample: a real sampler person is required — sampling quality must be attributable.");
                return null;
            }
            var sample = new MineAssaySample($"sample-{nextSampleNumber++}", lotId, samplerPersonId,
                samplingNote, dayIndex, rush);
            samples.Add(sample);
            return sample;
        }

        public MineAssaySample FindSample(string sampleId)
        {
            foreach (MineAssaySample sample in samples)
            {
                if (string.Equals(sample.SampleId, sampleId, StringComparison.Ordinal))
                    return sample;
            }
            return null;
        }

        /// <summary>
        /// Next sample due on the bench: rush samples first (Canon 21.7F:
        /// rush turnaround can be sold), then oldest received.
        /// </summary>
        public MineAssaySample NextSampleForAnalysis()
        {
            MineAssaySample best = null;
            foreach (MineAssaySample sample in samples)
            {
                if (sample.Status != MineAssaySampleStatus.Received)
                    continue;
                if (best == null)
                {
                    best = sample;
                    continue;
                }
                if (sample.Rush != best.Rush)
                {
                    if (sample.Rush)
                        best = sample;
                    continue;
                }
                if (sample.ReceivedDayIndex < best.ReceivedDayIndex)
                    best = sample;
            }
            return best;
        }

        /// <summary>Begins analysis on a queued sample, gated by daily bench capacity.</summary>
        public string BeginAnalysis(string sampleId, int dayIndex)
        {
            MineAssaySample sample = FindSample(sampleId);
            if (sample == null)
                return $"MineAssayOffice.BeginAnalysis: unknown sample '{sampleId}'.";
            if (sample.Status != MineAssaySampleStatus.Received)
                return $"MineAssayOffice.BeginAnalysis: sample '{sampleId}' is {sample.Status}, not queued.";
            if (dayIndex != lastAnalysisDayIndex)
            {
                lastAnalysisDayIndex = Math.Max(0, dayIndex);
                analysesBegunOnDay = 0;
            }
            if (analysesBegunOnDay >= SamplesPerDayCapacity)
                return $"MineAssayOffice.BeginAnalysis: bench capacity reached for day {dayIndex} ({SamplesPerDayCapacity}/day) — sample waits.";
            sample.MarkAnalysisBegun(dayIndex);
            analysesBegunOnDay++;
            return null;
        }

        /// <summary>
        /// Completes the analysis: runs the assay through the shared assay
        /// service (W8B authority — lot must be on the stockpile, assayer a
        /// real person), issues the report, and collects the fee from the
        /// payer's cash ledger when one is provided. An unpaid fee is
        /// recorded on the report as an open account item, never forgiven.
        /// </summary>
        public MineAssayReport CompleteAnalysis(string sampleId, MineOreStock stock,
            MineAssayService assayService, EntityId assayerPersonId, float assayedGradeValue,
            string methodNote, int dayIndex, HouseholdLedger payerCash, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            MineAssaySample sample = FindSample(sampleId);
            if (sample == null)
            {
                callerDiagnostics.Add($"MineAssayOffice.CompleteAnalysis: unknown sample '{sampleId}'.");
                return null;
            }
            if (sample.Status != MineAssaySampleStatus.InAnalysis)
            {
                callerDiagnostics.Add($"MineAssayOffice.CompleteAnalysis: sample '{sampleId}' has not begun analysis.");
                return null;
            }
            if (assayService == null)
            {
                callerDiagnostics.Add("MineAssayOffice.CompleteAnalysis: the assay service is required.");
                return null;
            }

            MineAssayResult result = assayService.AssayLot(stock, sample.LotId, assayerPersonId,
                dayIndex, assayedGradeValue, methodNote);
            if (result == null)
            {
                callerDiagnostics.Add($"MineAssayOffice.CompleteAnalysis: assay failed — {string.Join("; ", assayService.Diagnostics.ToArray())}");
                return null;
            }

            int fee = sample.Rush ? RushFeeCents : StandardFeeCents;
            bool feePaid = false;
            if (payerCash != null && fee > 0)
            {
                string rejection = payerCash.RecordOutflow(dayIndex, fee,
                    $"assay fee {(sample.Rush ? "rush" : "standard")}: sample {sample.SampleId} lot {sample.LotId.Id}",
                    OfficeName);
                if (rejection != null)
                    callerDiagnostics.Add($"MineAssayOffice.CompleteAnalysis: assay fee {fee}c unpaid — {rejection} (open account item).");
                else
                    feePaid = true;
            }
            else if (payerCash == null)
            {
                callerDiagnostics.Add($"MineAssayOffice.CompleteAnalysis: no payer cash ledger — assay fee {fee}c recorded as open account item.");
            }

            var report = new MineAssayReport($"report-{nextReportNumber++}", result.AssayId,
                sample.SampleId, OfficeName, dayIndex, sample.Rush, fee, feePaid);
            reports.Add(report);
            sample.MarkReported(report.ReportId);
            if (sample.Rush)
                rushCompletedOnTime++;
            return report;
        }

        /// <summary>Records a buyer/lender/investor dispute against a report — the reputation signal.</summary>
        public string DisputeReport(string reportId, string note)
        {
            foreach (MineAssayReport report in reports)
            {
                if (string.Equals(report.ReportId, reportId, StringComparison.Ordinal))
                {
                    report.MarkDisputed(note);
                    return null;
                }
            }
            return $"MineAssayOffice.DisputeReport: unknown report '{reportId}'.";
        }

        public int ReportsIssued => reports.Count;

        public int ReportsDisputed
        {
            get
            {
                int count = 0;
                foreach (MineAssayReport report in reports)
                {
                    if (report.Disputed)
                        count++;
                }
                return count;
            }
        }

        /// <summary>One-line reputation standing — data for future diligence, no invented effects.</summary>
        public string ReputationLine()
        {
            return $"{OfficeName}: {ReportsIssued} reports issued, {ReportsDisputed} disputed, {rushCompletedOnTime} rush completed.";
        }

        public MineAssayOfficeSaveDto CaptureSaveDto()
        {
            var dto = new MineAssayOfficeSaveDto
            {
                officeName = OfficeName,
                samplesPerDayCapacity = SamplesPerDayCapacity,
                standardFeeCents = StandardFeeCents,
                rushFeeCents = RushFeeCents,
                nextSampleNumber = nextSampleNumber,
                nextReportNumber = nextReportNumber,
                lastAnalysisDayIndex = lastAnalysisDayIndex,
                analysesBegunOnDay = analysesBegunOnDay,
                rushCompletedOnTime = rushCompletedOnTime,
            };
            foreach (MineAssaySample sample in samples)
                dto.samples.Add(sample.CaptureSaveDto());
            foreach (MineAssayReport report in reports)
                dto.reports.Add(report.CaptureSaveDto());
            return dto;
        }

        public static MineAssayOffice FromSaveDto(MineAssayOfficeSaveDto dto)
        {
            var office = new MineAssayOffice();
            if (dto == null)
                return office;
            office.officeName = dto.officeName ?? string.Empty;
            office.samplesPerDayCapacity = Math.Max(1, dto.samplesPerDayCapacity);
            office.standardFeeCents = Math.Max(0, dto.standardFeeCents);
            office.rushFeeCents = Math.Max(0, dto.rushFeeCents);
            office.nextSampleNumber = Math.Max(1, dto.nextSampleNumber);
            office.nextReportNumber = Math.Max(1, dto.nextReportNumber);
            office.lastAnalysisDayIndex = dto.lastAnalysisDayIndex;
            office.analysesBegunOnDay = Math.Max(0, dto.analysesBegunOnDay);
            office.rushCompletedOnTime = Math.Max(0, dto.rushCompletedOnTime);
            foreach (MineAssaySampleSaveDto sampleDto in dto.samples)
            {
                MineAssaySample sample = MineAssaySample.FromSaveDto(sampleDto);
                if (sample != null)
                    office.samples.Add(sample);
            }
            foreach (MineAssayReportSaveDto reportDto in dto.reports)
            {
                MineAssayReport report = MineAssayReport.FromSaveDto(reportDto);
                if (report != null)
                    office.reports.Add(report);
            }
            return office;
        }
    }

    /// <summary>D3C: save DTOs for the assay office. Owned by the mine runtime (standing rule).</summary>
    [Serializable]
    public sealed class MineAssaySampleSaveDto
    {
        public string sampleId = string.Empty;
        public int lotKind;
        public int lotSeq;
        public int samplerKind;
        public int samplerSeq;
        public string samplingNote = string.Empty;
        public int receivedDayIndex;
        public bool rush;
        public MineAssaySampleStatus status = MineAssaySampleStatus.Received;
        public int analysisBegunDayIndex = -1;
        public string reportId = string.Empty;
    }

    [Serializable]
    public sealed class MineAssayReportSaveDto
    {
        public string reportId = string.Empty;
        public string assayId = string.Empty;
        public string sampleId = string.Empty;
        public string officeName = string.Empty;
        public int issuedDayIndex;
        public bool rush;
        public int feeCents;
        public bool feePaid;
        public bool disputed;
        public string disputeNote = string.Empty;
    }

    [Serializable]
    public sealed class MineAssayOfficeSaveDto
    {
        public string officeName = string.Empty;
        public int samplesPerDayCapacity = 1;
        public int standardFeeCents;
        public int rushFeeCents;
        public int nextSampleNumber = 1;
        public int nextReportNumber = 1;
        public int lastAnalysisDayIndex = -1;
        public int analysesBegunOnDay;
        public int rushCompletedOnTime;
        public List<MineAssaySampleSaveDto> samples = new List<MineAssaySampleSaveDto>();
        public List<MineAssayReportSaveDto> reports = new List<MineAssayReportSaveDto>();
    }
}
