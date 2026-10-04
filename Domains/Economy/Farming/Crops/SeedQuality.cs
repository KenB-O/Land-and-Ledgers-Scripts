using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// D2G: seed quality grades as DATA. The canon names seed as a
    /// biological-potential input (Canon Rev XXVII §7.4/7.4C: "soil, water,
    /// seed, weather and crop condition determine what the land could
    /// produce") and routes seed through merchants ("agricultural supply"),
    /// but it defines no grading ladder — and 19th-century grading was
    /// informal, so none is invented here. A grade is what the seller
    /// DECLARED at the sale, recorded on the lot; it is a claim, not a
    /// canon standard.
    /// </summary>
    public enum SeedQualityGrade
    {
        /// <summary>No grade was declared — the honest default.</summary>
        Unstated = 0,

        /// <summary>Seller declared the seed cleaned/winnowed (chaff and weed seed removed).</summary>
        Cleaned = 1,

        /// <summary>Seller declared the seed straight from threshing, uncleaned.</summary>
        AsThreshed = 2,

        /// <summary>Seller declared tailings/screenings — historically sown in hard times, never first quality.</summary>
        Screenings = 3,
    }

    /// <summary>
    /// D2G: one bad-seed report's lifecycle. Reports are records, not verdicts:
    /// a filed report is a farmer's claim; corroboration is a second witness or
    /// the merchant's own admission; dismissal and resolution close the record.
    /// </summary>
    public enum SeedQualityIncidentStatus
    {
        Filed = 0,
        Corroborated = 1,
        Dismissed = 2,
        Resolved = 3,
    }

    /// <summary>
    /// D2G: seed-quality helpers. Germination rates are RECORDED data, never
    /// calibration: the declared rate is the merchant's claim at sale, the
    /// reported rate is the buyer's observation after sowing. Neither is read
    /// by yield calibration (CropCalibration is untouched by design).
    /// </summary>
    public static class SeedQuality
    {
        /// <summary>Sentinel for "no germination rate recorded" — honesty, not a guess.</summary>
        public const int UndeclaredGerminationRate = -1;

        /// <summary>Keeps the undeclared sentinel; clamps real rates to 0–100.</summary>
        public static int NormalizeGerminationRate(int ratePct)
        {
            if (ratePct == UndeclaredGerminationRate) return UndeclaredGerminationRate;
            return Mathf.Clamp(ratePct, 0, 100);
        }

        public static string GradeDisplayName(SeedQualityGrade grade)
        {
            switch (grade)
            {
                case SeedQualityGrade.Cleaned: return "cleaned seed";
                case SeedQualityGrade.AsThreshed: return "as threshed";
                case SeedQualityGrade.Screenings: return "screenings";
                default: return "grade unstated";
            }
        }
    }

    /// <summary>
    /// D2G: one bad-seed incident — a farmer's report that a merchant's seed
    /// failed to come up as declared. The report names the merchant, the lot,
    /// the declared quality, and the observed germination. Whether the report
    /// is true is NOT decided here: status moves Filed → Corroborated (a
    /// second report or the merchant's admission) or Filed/Corroborated →
    /// Dismissed/Resolved. Terminal states reject further transitions loudly.
    /// </summary>
    [Serializable]
    public sealed class SeedQualityIncident
    {
        public EntityId IncidentId = EntityId.Invalid; // EntityKind.Lot (HF-1), record id
        public string MerchantBusinessId = string.Empty;
        public string MerchantName = string.Empty;
        public EntityId SeedLotId = EntityId.Invalid; // the merchant lot sold, when known
        public CropKind Crop;
        public string VarietyId = string.Empty;
        public SeedQualityGrade DeclaredGrade = SeedQualityGrade.Unstated;
        public int DeclaredGerminationRatePct = SeedQuality.UndeclaredGerminationRate;
        public int ReportedGerminationRatePct = SeedQuality.UndeclaredGerminationRate;
        public string ReporterFarmId = string.Empty;
        public int FiledDayIndex = -1;
        public string Note = string.Empty;
        public SeedQualityIncidentStatus Status = SeedQualityIncidentStatus.Filed;
        public int ClosedDayIndex = -1;
        public string ResolutionNote = string.Empty;

        public SeedQualityIncident() { }

        /// <summary>
        /// The declared-vs-observed germination gap, 0–1. When the merchant
        /// declared nothing, severity is unknowable: 0.5 (a mid, honest
        /// placeholder) rather than a fabricated gap.
        /// </summary>
        public float GerminationGap01()
        {
            if (DeclaredGerminationRatePct == SeedQuality.UndeclaredGerminationRate
                || ReportedGerminationRatePct == SeedQuality.UndeclaredGerminationRate)
            {
                return 0.5f;
            }
            return Mathf.Clamp01((DeclaredGerminationRatePct - ReportedGerminationRatePct) / 100f);
        }

        public bool IsOpen => Status == SeedQualityIncidentStatus.Filed
            || Status == SeedQualityIncidentStatus.Corroborated;
    }

    /// <summary>
    /// D2G: the merchant-facing summary of a seed merchant's quality record —
    /// the seam to the reputation owner. This is DATA: incident counts and a
    /// deterministic pressure value. How incidents translate into
    /// BusinessReputationState changes is a recorded design fork (canon 9.2A
    /// says supply outcomes should affect Business Reputation but gives no
    /// deltas), so this signal is handed to the reputation owner, never
    /// applied here.
    /// </summary>
    [Serializable]
    public struct SeedMerchantQualitySignal
    {
        public string MerchantBusinessId;
        public string MerchantName;
        public int TotalIncidents;
        public int FiledCount;
        public int CorroboratedCount;
        public int DismissedCount;
        public int ResolvedCount;
        public float BadSeedPressure01;

        public SeedMerchantQualitySignal(string merchantBusinessId, string merchantName)
        {
            MerchantBusinessId = merchantBusinessId ?? string.Empty;
            MerchantName = merchantName ?? string.Empty;
            TotalIncidents = 0;
            FiledCount = 0;
            CorroboratedCount = 0;
            DismissedCount = 0;
            ResolvedCount = 0;
            BadSeedPressure01 = 0f;
        }
    }

    /// <summary>
    /// D2G: the bad-seed incident ledger, keyed by merchant. The canon
    /// (9.2A) expects supply failures to affect "trust, Business Reputation,
    /// and future renewal odds"; this book is the seed-chain side of that
    /// record. It stores reports as data and exposes a deterministic
    /// <see cref="BadSeedPressure01"/> and <see cref="BuildQualitySignal"/>
    /// for the reputation owner. It never applies reputation changes itself.
    ///
    /// Upstream-provenance doctrine applies to the book too: an incident must
    /// name a real merchant (business id or name) and a real lot; anonymous
    /// reports are refused, not filed vaguely.
    /// </summary>
    [Serializable]
    public sealed class SeedQualityIncidentBook
    {
        private readonly List<SeedQualityIncident> incidents = new List<SeedQualityIncident>();

        public SeedQualityIncidentBook() { }

        public IReadOnlyList<SeedQualityIncident> Incidents => incidents;

        public SeedQualityIncident FindIncident(EntityId incidentId)
        {
            foreach (var incident in incidents)
            {
                if (incident != null && incident.IncidentId.Equals(incidentId)) return incident;
            }
            return null;
        }

        public List<SeedQualityIncident> IncidentsForMerchant(string merchantBusinessId, string merchantName)
        {
            var result = new List<SeedQualityIncident>();
            foreach (var incident in incidents)
            {
                if (incident == null) continue;
                bool byId = !string.IsNullOrWhiteSpace(merchantBusinessId)
                    && string.Equals(incident.MerchantBusinessId, merchantBusinessId, StringComparison.OrdinalIgnoreCase);
                bool byName = !string.IsNullOrWhiteSpace(merchantName)
                    && string.Equals(incident.MerchantName, merchantName, StringComparison.OrdinalIgnoreCase);
                if (byId || byName) result.Add(incident);
            }
            return result;
        }

        /// <summary>
        /// Files a bad-seed report. Refuses loudly when the report names no
        /// merchant, names no lot, or carries no observed germination rate — a
        /// report without a claim is a rumor, not a record.
        /// </summary>
        public SeedQualityIncident FileIncident(
            EntityIdRegistry idRegistry,
            string merchantBusinessId,
            string merchantName,
            EntityId seedLotId,
            CropKind crop,
            string varietyId,
            SeedQualityGrade declaredGrade,
            int declaredGerminationRatePct,
            int reportedGerminationRatePct,
            string reporterFarmId,
            int dayIndex,
            string note,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null)
            {
                diagnostics.Add("SeedQualityIncidentBook: filing needs an id registry.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(merchantBusinessId) && string.IsNullOrWhiteSpace(merchantName))
            {
                diagnostics.Add("SeedQualityIncidentBook: a bad-seed report must name the merchant (business id or name) — no anonymous reports.");
                return null;
            }
            if (!seedLotId.IsValid)
            {
                diagnostics.Add("SeedQualityIncidentBook: a bad-seed report must name the seed lot — reports attach to lots, not vibes.");
                return null;
            }
            if (reportedGerminationRatePct < 0 || reportedGerminationRatePct > 100)
            {
                diagnostics.Add($"SeedQualityIncidentBook: the report must state an observed germination rate 0–100 (got {reportedGerminationRatePct}) — a claim needs a number.");
                return null;
            }

            var incident = new SeedQualityIncident
            {
                IncidentId = idRegistry.Allocate(EntityKind.Lot),
                MerchantBusinessId = merchantBusinessId ?? string.Empty,
                MerchantName = merchantName ?? string.Empty,
                SeedLotId = seedLotId,
                Crop = crop,
                VarietyId = string.IsNullOrWhiteSpace(varietyId) ? CropVarietyCatalog.UnknownVarietyId : varietyId,
                DeclaredGrade = declaredGrade,
                DeclaredGerminationRatePct = SeedQuality.NormalizeGerminationRate(declaredGerminationRatePct),
                ReportedGerminationRatePct = reportedGerminationRatePct,
                ReporterFarmId = reporterFarmId ?? string.Empty,
                FiledDayIndex = dayIndex,
                Note = note ?? string.Empty,
                Status = SeedQualityIncidentStatus.Filed,
            };
            incidents.Add(incident);
            string merchant = !string.IsNullOrWhiteSpace(incident.MerchantName)
                ? incident.MerchantName : incident.MerchantBusinessId;
            diagnostics.Add(
                $"SeedQualityIncidentBook: filed bad-seed report {incident.IncidentId} against {merchant} " +
                $"(lot {seedLotId}, observed {reportedGerminationRatePct}% germination).");
            return incident;
        }

        /// <summary>A second report (or the merchant's admission) corroborates a filed incident.</summary>
        public string CorroborateIncident(EntityId incidentId, int dayIndex, string note, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            SeedQualityIncident incident = FindIncident(incidentId);
            if (incident == null) return $"SeedQualityIncidentBook: unknown incident {incidentId}.";
            if (incident.Status != SeedQualityIncidentStatus.Filed)
            {
                return $"SeedQualityIncidentBook: incident {incidentId} is {incident.Status} — only a filed report can be corroborated.";
            }
            incident.Status = SeedQualityIncidentStatus.Corroborated;
            if (!string.IsNullOrWhiteSpace(note))
            {
                incident.Note = string.IsNullOrWhiteSpace(incident.Note)
                    ? note
                    : incident.Note + " | corroborated: " + note;
            }
            diagnostics.Add($"SeedQualityIncidentBook: incident {incidentId} corroborated (day {dayIndex}).");
            return null;
        }

        /// <summary>Dismisses a filed or corroborated incident (unfounded or settled privately).</summary>
        public string DismissIncident(EntityId incidentId, int dayIndex, string reason, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            SeedQualityIncident incident = FindIncident(incidentId);
            if (incident == null) return $"SeedQualityIncidentBook: unknown incident {incidentId}.";
            if (!incident.IsOpen)
            {
                return $"SeedQualityIncidentBook: incident {incidentId} is already {incident.Status} — terminal states do not move.";
            }
            incident.Status = SeedQualityIncidentStatus.Dismissed;
            incident.ClosedDayIndex = dayIndex;
            incident.ResolutionNote = reason ?? string.Empty;
            diagnostics.Add($"SeedQualityIncidentBook: incident {incidentId} dismissed (day {dayIndex}).");
            return null;
        }

        /// <summary>Resolves a corroborated incident (merchant made it right).</summary>
        public string ResolveIncident(EntityId incidentId, int dayIndex, string resolution, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            SeedQualityIncident incident = FindIncident(incidentId);
            if (incident == null) return $"SeedQualityIncidentBook: unknown incident {incidentId}.";
            if (incident.Status != SeedQualityIncidentStatus.Corroborated)
            {
                return $"SeedQualityIncidentBook: incident {incidentId} is {incident.Status} — only a corroborated incident can be resolved.";
            }
            incident.Status = SeedQualityIncidentStatus.Resolved;
            incident.ClosedDayIndex = dayIndex;
            incident.ResolutionNote = resolution ?? string.Empty;
            diagnostics.Add($"SeedQualityIncidentBook: incident {incidentId} resolved (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Deterministic bad-seed pressure for a merchant, 0–1. Corroborated
        /// incidents weigh most, open filed reports weigh lightly, and the
        /// worst declared-vs-observed germination gap adds severity. Dismissed
        /// and resolved incidents weigh nothing — the record forgives. This is
        /// a reporting value, not a reputation delta (recorded fork).
        /// </summary>
        public float BadSeedPressure01(string merchantBusinessId, string merchantName)
        {
            int corroborated = 0;
            int filed = 0;
            float worstGap = 0f;
            foreach (var incident in IncidentsForMerchant(merchantBusinessId, merchantName))
            {
                switch (incident.Status)
                {
                    case SeedQualityIncidentStatus.Filed:
                        filed++;
                        break;
                    case SeedQualityIncidentStatus.Corroborated:
                        corroborated++;
                        worstGap = Mathf.Max(worstGap, incident.GerminationGap01());
                        break;
                }
            }
            return Mathf.Clamp01(corroborated * 0.3f + filed * 0.1f + worstGap * 0.2f);
        }

        /// <summary>Builds the reputation-owner signal for a merchant.</summary>
        public SeedMerchantQualitySignal BuildQualitySignal(string merchantBusinessId, string merchantName)
        {
            var signal = new SeedMerchantQualitySignal(merchantBusinessId, merchantName);
            foreach (var incident in IncidentsForMerchant(merchantBusinessId, merchantName))
            {
                signal.TotalIncidents++;
                switch (incident.Status)
                {
                    case SeedQualityIncidentStatus.Filed: signal.FiledCount++; break;
                    case SeedQualityIncidentStatus.Corroborated: signal.CorroboratedCount++; break;
                    case SeedQualityIncidentStatus.Dismissed: signal.DismissedCount++; break;
                    case SeedQualityIncidentStatus.Resolved: signal.ResolvedCount++; break;
                }
            }
            signal.BadSeedPressure01 = BadSeedPressure01(merchantBusinessId, merchantName);
            return signal;
        }

        /// <summary>Save support (CLN-1 pattern): DTO lives inside the owning book class.</summary>
        public SeedQualityIncidentBookSaveDto CaptureSaveDto()
        {
            return new SeedQualityIncidentBookSaveDto
            {
                incidents = new List<SeedQualityIncident>(incidents),
            };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(SeedQualityIncidentBookSaveDto dto)
        {
            incidents.Clear();
            if (dto == null) return;
            if (dto.incidents != null)
            {
                foreach (var incident in dto.incidents)
                {
                    if (incident != null) incidents.Add(incident);
                }
            }
        }

        /// <summary>Save DTO for the incident book (CLN-1 pattern).</summary>
        [Serializable]
        public sealed class SeedQualityIncidentBookSaveDto
        {
            public List<SeedQualityIncident> incidents = new List<SeedQualityIncident>();
        }
    }
}
