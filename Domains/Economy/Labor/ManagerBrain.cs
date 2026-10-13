// Worker D: Autonomous Labor, Phase G - autonomous management.
// Canon CE1-13.2 delegation model: the manager acts ONLY within delegated
// scopes and standing-instruction bounds. The manager is an ORDINARY PERSON
// (every decision notes its time cost); there is no perfect foresight
// (evidence cites observed conditions only); poor decisions are possible
// (judgment quality is a seeded, deterministic input).
//
// Authority rules enforced here:
//   - OwnerLocked instructions: refused + escalated, NEVER executed.
//   - DoOnce instructions: executed exactly once, then consumed.
//   - ManagerJudgment instructions: ongoing objectives pursued with
//     discretion, inside bounds.
//   - Reserved acts (borrowing at/above the owner's max borrowing permission,
//     borrowing at/above the major-borrowing threshold, asset sales): always
//     escalate, never execute.
//   - Cash constraints: the manager cannot spend cash the business lacks -
//     over-cash acts are delayed and surfaced, never executed.
// The brain reports INTENTS (ResultingReference); cash movement, task
// creation and agreement writes stay with the integrator.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>Well-known act kinds the ManagerBrain can consider.</summary>
    public static class ManagerActKinds
    {
        public const string OpenRecruitment = "OpenRecruitment";
        public const string Schedule = "Schedule";
        public const string Procure = "Procure";
        public const string PriceChange = "PriceChange";
        public const string OrderMaintenance = "OrderMaintenance";
        public const string Borrow = "Borrow";
        public const string SellAsset = "SellAsset";
    }

    /// <summary>Well-known delegated scope names (mirror DelegationScope names).</summary>
    public static class ManagerScopeNames
    {
        public const string Purchasing = "Purchasing";
        public const string Scheduling = "Scheduling";
        public const string Maintenance = "Maintenance";
        public const string CashHandling = "CashHandling";
        public const string Inventory = "Inventory";
        public const string Staffing = "Staffing";
    }

    /// <summary>
    /// Everything the manager legitimately observes when deciding. Demand is a
    /// caller-supplied LaborDemandReport (raw minutes, never quotas); bounds
    /// come from delegated authority + standing instructions.
    /// </summary>
    [Serializable]
    public sealed class ManagerCycleInputs
    {
        public string BusinessInstanceId = string.Empty;
        public int ManagerPersonId = -1;
        public int CashCents;
        public LaborDemandReport DemandReport;
        public List<StandingInstruction> Instructions = new List<StandingInstruction>();
        /// <summary>Delegated scope names (e.g. "Staffing"); hub-independent.</summary>
        public List<string> DelegatedScopes = new List<string>();
        public int DayIndex;
        /// <summary>
        /// Deterministic seed for judgment fallibility. The SAME seed always
        /// yields the SAME decision - poor decisions are possible but never
        /// random.
        /// </summary>
        public int JudgmentSeed;
        /// <summary>
        /// 0..1 judgment quality (caller-computed from traits + information
        /// quality, NOT from efficiency stats). Below 0.5 the manager may pick
        /// a weakly-evidenced act - a real, explicable mistake.
        /// </summary>
        public float JudgmentQuality01 = 1f;
        public int PurchaseLimitCents;
        public int MaxBorrowingCents;

        // Observed condition signals (caller-supplied, read-only).
        public bool ShortageObserved;
        public int ProposedProcurementCents;
        public bool MaintenanceNeeded;
        public bool PricingAdjustmentRequested;
        public int ProposedBorrowingCents;
        public int ProposedAssetSaleCents;
    }

    /// <summary>One cycle's decision outcome.</summary>
    [Serializable]
    public sealed class ManagerDecisionResult
    {
        public bool Acted;
        public string ActionKind = string.Empty;
        public string Decision = string.Empty;
        public string Evidence = string.Empty;
        public string AuthorityUsed = string.Empty;
        public string ResultingReference = string.Empty;
        public string Outcome = string.Empty;
        public bool Escalated;
        public string EscalationReason = string.Empty;
        public List<string> Exceptions = new List<string>();
        public int TimeCostMinutes;
        public int DayIndex = -1;
    }

    /// <summary>
    /// One significant manager action: who decided, what they saw, what they
    /// chose, under what authority, with what result. Compact read model for
    /// later Unity UI. Significant actions ONLY (acts, refusals, escalations,
    /// weekly summaries) - no per-minute chatter.
    /// </summary>
    [Serializable]
    public sealed class ManagerActivityRecord
    {
        public int ManagerPersonId = -1;
        public string BusinessInstanceId = string.Empty;
        public int DayIndex = -1;
        public string ObservedConditions = string.Empty;
        public string Decision = string.Empty;
        public string Evidence = string.Empty;
        public string AuthorityUsed = string.Empty;
        public string ResultingReference = string.Empty;
        public string Outcome = string.Empty;
        /// <summary>Exceptions raised or escalations requested, as text.</summary>
        public string ExceptionsOrEscalations = string.Empty;
        public int TimeCostMinutes;
        public bool IsSignificant = true;
    }

    /// <summary>
    /// Append-only activity log. Significant records only; reload appends,
    /// never rewrites.
    /// </summary>
    public sealed class ManagerActivityLog
    {
        private readonly List<ManagerActivityRecord> records = new List<ManagerActivityRecord>();

        public IReadOnlyList<ManagerActivityRecord> Records => records;
        public int Count => records.Count;

        public void Clear()
        {
            records.Clear();
        }

        public void Append(ManagerActivityRecord record)
        {
            if (record == null || !record.IsSignificant)
            {
                return;
            }

            records.Add(record);
        }

        public ManagerActivitySaveDto CaptureSaveDto()
        {
            var dto = new ManagerActivitySaveDto();
            dto.Records.AddRange(records);
            return dto;
        }

        public void LoadFromSaveDto(ManagerActivitySaveDto dto)
        {
            if (dto == null || dto.Records == null)
            {
                return;
            }

            // The save DTO is the authoritative snapshot. Re-appending its
            // records to an already initialized service would duplicate every
            // historical decision on repeated restore.
            records.Clear();
            records.AddRange(dto.Records);
        }
    }

    [Serializable]
    public sealed class ManagerActivitySaveDto
    {
        public List<ManagerActivityRecord> Records = new List<ManagerActivityRecord>();
    }

    [Serializable]
    public sealed class ManagerBrainSaveDto
    {
        public ManagerActivitySaveDto ActivityLog = new ManagerActivitySaveDto();
        public StandingInstructionSaveDto Instructions = new StandingInstructionSaveDto();
    }

    /// <summary>
    /// The autonomous manager loop (driven daily/weekly by the integrator).
    /// Observes LaborDemandReport + business cash + authority; acts ONLY
    /// within delegated scopes and standing-instruction bounds.
    /// </summary>
    public sealed class ManagerBrain
    {
        /// <summary>
        /// Borrowing at or above this is "major" and owner-reserved, mirroring
        /// DelegationService.MajorBorrowingThresholdCents (kept as a local
        /// constant so this file stays hub-independent).
        /// </summary>
        public const int MajorBorrowingThresholdCents = 50000;

        private readonly ManagerActivityLog activityLog = new ManagerActivityLog();
        private readonly StandingInstructionRegister instructionRegister = new StandingInstructionRegister();
        private readonly List<string> diagnostics = new List<string>();

        public ManagerActivityLog ActivityLog => activityLog;
        public StandingInstructionRegister Instructions => instructionRegister;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Daily management cycle: observe, decide within authority, record.</summary>
        public ManagerDecisionResult DailyCycle(ManagerCycleInputs inputs, List<string> diag)
        {
            return RunCycle(inputs, isWeekly: false, diag);
        }

        /// <summary>
        /// Weekly management cycle: the daily logic plus a staffing/procurement
        /// review. Persistent unmet pressure with no recruitment intent in the
        /// last 7 days escalates to the owner.
        /// </summary>
        public ManagerDecisionResult WeeklyCycle(ManagerCycleInputs inputs, List<string> diag)
        {
            diag = diag ?? diagnostics;
            ManagerDecisionResult result = RunCycle(inputs, isWeekly: true, diag);

            if (inputs != null && inputs.DemandReport != null && inputs.DemandReport.HiringPressure
                && !RecentRecruitmentIntent(inputs.BusinessInstanceId, inputs.DayIndex))
            {
                string escalation = "persistent hiring pressure with no recruitment intent in 7 days - owner review requested";
                result.Escalated = true;
                result.EscalationReason = string.IsNullOrWhiteSpace(result.EscalationReason)
                    ? escalation
                    : result.EscalationReason + "; " + escalation;
                diag.Add($"ManagerBrain: {escalation}.");
                AppendRecord(inputs, "Weekly review: hiring pressure persists",
                    "no recruitment intent in the last 7 days of the activity log",
                    "escalation (standing review duty)", string.Empty,
                    "escalated to owner", escalation, 30);
            }

            return result;
        }

        private ManagerDecisionResult RunCycle(ManagerCycleInputs inputs, bool isWeekly, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new ManagerDecisionResult();

            if (inputs == null || string.IsNullOrWhiteSpace(inputs.BusinessInstanceId))
            {
                result.Exceptions.Add("ManagerBrain: a business id is required.");
                return result;
            }

            if (inputs.ManagerPersonId < 0)
            {
                diag.Add($"ManagerBrain: no manager person for '{inputs.BusinessInstanceId}' - no autonomous acts.");
                return result;
            }

            result.DayIndex = inputs.DayIndex;
            SyncInstructions(inputs);

            List<CandidateAct> candidates = BuildCandidates(inputs);
            if (candidates.Count == 0)
            {
                diag.Add($"ManagerBrain: day {inputs.DayIndex} - nothing in scope requires action for '{inputs.BusinessInstanceId}'.");
                return result;
            }

            // Judgment quality: high quality acts on the strongest-evidenced
            // candidate; low quality may act on a weakly-evidenced one - a
            // deterministic, explicable mistake (seeded, never random).
            var escalationReasons = new List<string>();
            List<CandidateAct> passable = FilterPassable(inputs, candidates, diag, escalationReasons);
            if (passable.Count == 0)
            {
                // All candidates refused/escalated - those are already recorded.
                result.Escalated = true;
                result.EscalationReason = string.Join("; ", escalationReasons.ToArray());
                // Name the blocked act so the execution layer can route it to
                // a pending request or escalation handler. The first candidate
                // is the strongest-evidenced blocked act.
                if (candidates != null && candidates.Count > 0 && candidates[0] != null)
                {
                    result.ActionKind = candidates[0].Kind ?? string.Empty;
                    // Preserve the amount in the reference so the executor can
                    // size the pending request. Format matches ExecuteAct.
                    if (candidates[0].AmountCents > 0)
                    {
                        result.ResultingReference = $"intent:{candidates[0].Kind.ToLowerInvariant()}-request:{inputs.BusinessInstanceId}:d{inputs.DayIndex}:{candidates[0].AmountCents}c";
                    }
                }
                return result;
            }

            CandidateAct chosen;
            if (inputs.JudgmentQuality01 < 0.5f && passable.Count > 1)
            {
                var rng = new Random(inputs.JudgmentSeed);
                int pick = rng.Next(passable.Count);
                chosen = passable[pick];
                if (!string.Equals(chosen.Kind, passable[0].Kind, StringComparison.Ordinal))
                {
                    diag.Add($"ManagerBrain: low judgment quality ({inputs.JudgmentQuality01:0.00}) - " +
                             $"chose weakly-evidenced '{chosen.Kind}' over '{passable[0].Kind}' (seed {inputs.JudgmentSeed}).");
                }
            }
            else
            {
                chosen = passable[0];
            }

            return ExecuteAct(inputs, chosen, diag);
        }

        private void SyncInstructions(ManagerCycleInputs inputs)
        {
            if (inputs.Instructions == null)
            {
                return;
            }

            foreach (StandingInstruction instruction in inputs.Instructions)
            {
                if (instruction == null || string.IsNullOrWhiteSpace(instruction.InstructionId))
                {
                    continue;
                }

                if (!instructionRegister.TryGet(instruction.InstructionId, out _))
                {
                    instructionRegister.Register(instruction, diagnostics);
                }
            }
        }

        private sealed class CandidateAct
        {
            public string Kind = string.Empty;
            public string Evidence = string.Empty;
            public int EvidenceRank; // lower = stronger evidence
            public string RequiredScope = string.Empty;
            public int AmountCents;
            public int TimeCostMinutes;
        }

        private List<CandidateAct> BuildCandidates(ManagerCycleInputs inputs)
        {
            var candidates = new List<CandidateAct>();
            LaborDemandReport demand = inputs.DemandReport;

            if (demand != null && demand.HiringPressure)
            {
                candidates.Add(new CandidateAct
                {
                    Kind = ManagerActKinds.OpenRecruitment,
                    Evidence = $"hiring pressure: {demand.TotalUnmetMinutes} unmet minutes. {demand.EvidenceSummary}",
                    EvidenceRank = 0,
                    RequiredScope = ManagerScopeNames.Staffing,
                    TimeCostMinutes = 120
                });
            }

            if (inputs.ProposedAssetSaleCents > 0)
            {
                candidates.Add(new CandidateAct
                {
                    Kind = ManagerActKinds.SellAsset,
                    Evidence = $"asset sale proposed at {inputs.ProposedAssetSaleCents}c",
                    EvidenceRank = 0,
                    RequiredScope = string.Empty, // owner-reserved: no scope covers it
                    AmountCents = inputs.ProposedAssetSaleCents,
                    TimeCostMinutes = 15
                });
            }

            if (inputs.ProposedBorrowingCents > 0)
            {
                candidates.Add(new CandidateAct
                {
                    Kind = ManagerActKinds.Borrow,
                    Evidence = $"borrowing of {inputs.ProposedBorrowingCents}c proposed (owner max {inputs.MaxBorrowingCents}c)",
                    EvidenceRank = 1,
                    RequiredScope = ManagerScopeNames.CashHandling,
                    AmountCents = inputs.ProposedBorrowingCents,
                    TimeCostMinutes = 30
                });
            }

            if (inputs.ShortageObserved && inputs.ProposedProcurementCents > 0)
            {
                candidates.Add(new CandidateAct
                {
                    Kind = ManagerActKinds.Procure,
                    Evidence = $"shortage observed; proposed procurement {inputs.ProposedProcurementCents}c against {inputs.CashCents}c cash",
                    EvidenceRank = 2,
                    RequiredScope = ManagerScopeNames.Purchasing,
                    AmountCents = inputs.ProposedProcurementCents,
                    TimeCostMinutes = 60
                });
            }

            if (demand != null && demand.CoverageGap)
            {
                candidates.Add(new CandidateAct
                {
                    Kind = ManagerActKinds.Schedule,
                    Evidence = $"coverage gap: {demand.TotalUnmetMinutes} unmet minutes no available person can cover in time",
                    EvidenceRank = 3,
                    RequiredScope = ManagerScopeNames.Scheduling,
                    TimeCostMinutes = 30
                });
            }

            if (inputs.MaintenanceNeeded)
            {
                candidates.Add(new CandidateAct
                {
                    Kind = ManagerActKinds.OrderMaintenance,
                    Evidence = "maintenance needed (caller-observed)",
                    EvidenceRank = 4,
                    RequiredScope = ManagerScopeNames.Maintenance,
                    TimeCostMinutes = 45
                });
            }

            if (inputs.PricingAdjustmentRequested)
            {
                candidates.Add(new CandidateAct
                {
                    Kind = ManagerActKinds.PriceChange,
                    Evidence = "pricing adjustment requested within bounds (caller-observed)",
                    EvidenceRank = 5,
                    RequiredScope = ManagerScopeNames.Inventory,
                    TimeCostMinutes = 30
                });
            }

            candidates.Sort((a, b) => a.EvidenceRank.CompareTo(b.EvidenceRank));
            return candidates;
        }

        private bool HasScope(ManagerCycleInputs inputs, string scope)
        {
            if (string.IsNullOrWhiteSpace(scope) || inputs.DelegatedScopes == null)
            {
                return false;
            }

            foreach (string granted in inputs.DelegatedScopes)
            {
                if (string.Equals(granted, scope, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Refusals and escalations are recorded here (and logged); only acts
        /// that survive authority, bounds and cash checks are passable.
        /// </summary>
        private List<CandidateAct> FilterPassable(ManagerCycleInputs inputs, List<CandidateAct> candidates, List<string> diag, List<string> escalationReasons)
        {
            var passable = new List<CandidateAct>();
            foreach (CandidateAct candidate in candidates)
            {
                if (CheckOwnerLocked(inputs, candidate, diag, escalationReasons))
                {
                    continue;
                }

                StandingInstruction doOnce = FindDoOnce(inputs, candidate, diag);
                if (doOnce != null)
                {
                    // DoOnce is owner-specified, so the instruction itself is the
                    // authority - but its spend bound and the business cash
                    // still apply. Over-bound or over-cash DoOnce acts escalate
                    // (and are NOT consumed).
                    if (string.Equals(candidate.Kind, ManagerActKinds.Procure, StringComparison.Ordinal))
                    {
                        if (doOnce.MaxSpendCentsPerAct >= 0 && candidate.AmountCents > doOnce.MaxSpendCentsPerAct)
                        {
                            Escalate(inputs, candidate,
                                $"DoOnce '{doOnce.InstructionId}' procurement {candidate.AmountCents}c exceeds its {doOnce.MaxSpendCentsPerAct}c bound",
                                diag, escalationReasons);
                            continue;
                        }

                        if (candidate.AmountCents > inputs.CashCents)
                        {
                            string delayReason = $"insufficient cash ({inputs.CashCents}c) - owner decision on funding";
                            diag.Add($"ManagerBrain: DoOnce '{doOnce.InstructionId}' delayed - {delayReason}.");
                            escalationReasons.Add($"DoOnce '{doOnce.InstructionId}' delayed: {delayReason}");
                            AppendRecord(inputs, $"Delay DoOnce procurement ({candidate.AmountCents}c)", candidate.Evidence,
                                $"DoOnce '{doOnce.InstructionId}'", string.Empty, "delayed - insufficient cash",
                                delayReason, candidate.TimeCostMinutes);
                            continue;
                        }
                    }

                    passable.Add(candidate);
                    continue;
                }

                if (string.Equals(candidate.Kind, ManagerActKinds.SellAsset, StringComparison.Ordinal))
                {
                    Escalate(inputs, candidate, "asset sale is owner-reserved - it escalates no matter how broad the grant", diag, escalationReasons);
                    continue;
                }

                if (string.Equals(candidate.Kind, ManagerActKinds.Borrow, StringComparison.Ordinal))
                {
                    if (candidate.AmountCents > Math.Max(0, inputs.MaxBorrowingCents))
                    {
                        Escalate(inputs, candidate,
                            $"borrowing {candidate.AmountCents}c exceeds the owner's max borrowing permission ({inputs.MaxBorrowingCents}c) - the manager cannot ignore it",
                            diag, escalationReasons);
                        continue;
                    }

                    if (candidate.AmountCents >= MajorBorrowingThresholdCents)
                    {
                        Escalate(inputs, candidate,
                            $"borrowing {candidate.AmountCents}c is major borrowing (>= {MajorBorrowingThresholdCents}c) - owner-reserved",
                            diag, escalationReasons);
                        continue;
                    }

                    passable.Add(candidate);
                    continue;
                }

                if (!HasScope(inputs, candidate.RequiredScope))
                {
                    Escalate(inputs, candidate,
                        $"act '{candidate.Kind}' needs the '{candidate.RequiredScope}' scope, which P{inputs.ManagerPersonId} does not hold",
                        diag, escalationReasons);
                    continue;
                }

                if (string.Equals(candidate.Kind, ManagerActKinds.Procure, StringComparison.Ordinal))
                {
                    int cap = ResolveSpendCap(inputs, candidate);
                    if (candidate.AmountCents > cap)
                    {
                        Escalate(inputs, candidate,
                            $"procurement {candidate.AmountCents}c exceeds the delegated spend cap ({cap}c)",
                            diag, escalationReasons);
                        continue;
                    }

                    if (candidate.AmountCents > inputs.CashCents)
                    {
                        string reason = $"delayed procurement {candidate.AmountCents}c: insufficient cash ({inputs.CashCents}c) - owner decision on funding";
                        diag.Add($"ManagerBrain: {reason}.");
                        escalationReasons.Add(reason);
                        AppendRecord(inputs, $"Delay procurement ({candidate.AmountCents}c)", candidate.Evidence,
                            $"delegated scope '{candidate.RequiredScope}' (PurchaseLimitCents {inputs.PurchaseLimitCents}c)",
                            string.Empty, "delayed - insufficient cash", reason, candidate.TimeCostMinutes);
                        continue;
                    }
                }

                passable.Add(candidate);
            }

            return passable;
        }

        private int ResolveSpendCap(ManagerCycleInputs inputs, CandidateAct candidate)
        {
            int cap = Math.Max(0, inputs.PurchaseLimitCents);
            foreach (StandingInstruction instruction in instructionRegister.ForBusiness(inputs.BusinessInstanceId))
            {
                if (instruction.IsRevoked)
                {
                    continue;
                }

                bool applies = instruction.Kind == InstructionKind.ManagerJudgment
                    && (string.IsNullOrWhiteSpace(instruction.TargetActKind)
                        || string.Equals(instruction.TargetActKind, candidate.Kind, StringComparison.Ordinal));
                if (applies && instruction.MaxSpendCentsPerAct >= 0)
                {
                    cap = Math.Min(cap, instruction.MaxSpendCentsPerAct);
                }
            }

            return cap;
        }

        /// <summary>OwnerLocked: refuse + escalate, NEVER execute. Returns true when refused.</summary>
        private bool CheckOwnerLocked(ManagerCycleInputs inputs, CandidateAct candidate, List<string> diag, List<string> escalationReasons)
        {
            if (!instructionRegister.IsActProhibited(inputs.BusinessInstanceId, candidate.Kind))
            {
                return false;
            }

            string reason = $"OwnerLocked instruction prohibits '{candidate.Kind}' - not overridable without owner authorization";
            diag.Add($"ManagerBrain: REFUSED '{candidate.Kind}' for '{inputs.BusinessInstanceId}': {reason}.");
            escalationReasons.Add(reason);
            AppendRecord(inputs, $"Refuse {candidate.Kind}", candidate.Evidence,
                "none - OwnerLocked prohibition", string.Empty, "refused, escalated to owner", reason,
                candidate.TimeCostMinutes);
            return true;
        }

        /// <summary>
        /// DoOnce: a live DoOnce targeting this act claims it. Returns the
        /// claiming instruction (null when none claims it).
        /// </summary>
        private StandingInstruction FindDoOnce(ManagerCycleInputs inputs, CandidateAct candidate, List<string> diag)
        {
            foreach (StandingInstruction instruction in instructionRegister.ForBusiness(inputs.BusinessInstanceId))
            {
                if (instruction.Kind == InstructionKind.DoOnce && !instruction.IsRevoked && !instruction.Consumed
                    && string.Equals(instruction.TargetActKind, candidate.Kind, StringComparison.Ordinal))
                {
                    diag.Add($"ManagerBrain: DoOnce '{instruction.InstructionId}' claims '{candidate.Kind}' - one specified action.");
                    candidate.Evidence += $" | DoOnce '{instruction.InstructionId}': {instruction.Objective}";
                    return instruction;
                }
            }

            return null;
        }

        private void Escalate(ManagerCycleInputs inputs, CandidateAct candidate, string reason, List<string> diag, List<string> escalationReasons)
        {
            diag.Add($"ManagerBrain: ESCALATED '{candidate.Kind}' for '{inputs.BusinessInstanceId}': {reason}.");
            if (escalationReasons != null)
            {
                escalationReasons.Add(reason);
            }

            AppendRecord(inputs, $"Escalate {candidate.Kind}", candidate.Evidence,
                $"delegated scope '{candidate.RequiredScope}' checked", string.Empty,
                "escalated to owner - not executed", reason, candidate.TimeCostMinutes);
        }

        private ManagerDecisionResult ExecuteAct(ManagerCycleInputs inputs, CandidateAct candidate, List<string> diag)
        {
            var result = new ManagerDecisionResult
            {
                Acted = true,
                ActionKind = candidate.Kind,
                Evidence = candidate.Evidence,
                TimeCostMinutes = candidate.TimeCostMinutes,
                DayIndex = inputs.DayIndex
            };

            // Consume a claiming DoOnce at execution time - exactly once.
            StandingInstruction doOnce = null;
            foreach (StandingInstruction instruction in instructionRegister.ForBusiness(inputs.BusinessInstanceId))
            {
                if (instruction.Kind == InstructionKind.DoOnce && !instruction.IsRevoked && !instruction.Consumed
                    && string.Equals(instruction.TargetActKind, candidate.Kind, StringComparison.Ordinal))
                {
                    doOnce = instruction;
                    break;
                }
            }

            switch (candidate.Kind)
            {
                case ManagerActKinds.OpenRecruitment:
                    result.Decision = $"open recruitment effort ({candidate.Evidence})";
                    result.AuthorityUsed = $"delegated scope '{ManagerScopeNames.Staffing}'";
                    result.ResultingReference = $"intent:recruitment-effort:{inputs.BusinessInstanceId}:d{inputs.DayIndex}";
                    result.Outcome = "recruitment intent reported - channel costs and effort creation stay with the integrator";
                    break;

                case ManagerActKinds.Schedule:
                    result.Decision = "schedule available workers against open work";
                    result.AuthorityUsed = $"delegated scope '{ManagerScopeNames.Scheduling}'";
                    result.ResultingReference = $"intent:work-schedule:{inputs.BusinessInstanceId}:d{inputs.DayIndex}";
                    result.Outcome = "scheduling intent reported - task assignment stays with the integrator's WorkScheduler drive";
                    break;

                case ManagerActKinds.Procure:
                    result.Decision = $"procure {candidate.AmountCents}c of supplies";
                    result.AuthorityUsed = $"delegated scope '{ManagerScopeNames.Purchasing}' within spend cap {ResolveSpendCap(inputs, candidate)}c";
                    result.ResultingReference = $"intent:procurement:{inputs.BusinessInstanceId}:d{inputs.DayIndex}:{candidate.AmountCents}c";
                    result.Outcome = "procurement intent reported - cash movement stays with the integrator";
                    break;

                case ManagerActKinds.PriceChange:
                    result.Decision = "adjust pricing within bounds";
                    result.AuthorityUsed = $"delegated scope '{ManagerScopeNames.Inventory}' + ManagerJudgment bounds";
                    result.ResultingReference = $"intent:price-change:{inputs.BusinessInstanceId}:d{inputs.DayIndex}";
                    result.Outcome = "pricing intent reported - price writes stay with the integrator";
                    break;

                case ManagerActKinds.OrderMaintenance:
                    result.Decision = "order maintenance";
                    result.AuthorityUsed = $"delegated scope '{ManagerScopeNames.Maintenance}'";
                    result.ResultingReference = $"intent:maintenance:{inputs.BusinessInstanceId}:d{inputs.DayIndex}";
                    result.Outcome = "maintenance intent reported";
                    break;

                case ManagerActKinds.Borrow:
                    // Below the owner's max and below the major threshold: the
                    // manager may still only RECOMMEND - borrowing execution is
                    // owner/financing domain. Reported as intent, escalated for
                    // approval to keep the boundary explicit.
                    result.Decision = $"recommend borrowing {candidate.AmountCents}c";
                    result.AuthorityUsed = $"delegated scope '{ManagerScopeNames.CashHandling}' (owner max {inputs.MaxBorrowingCents}c)";
                    result.ResultingReference = $"intent:borrow-request:{inputs.BusinessInstanceId}:d{inputs.DayIndex}:{candidate.AmountCents}c";
                    result.Outcome = "borrowing intent escalated for owner approval - never executed by the manager";
                    result.Escalated = true;
                    result.EscalationReason = "all borrowing needs explicit owner approval";
                    break;

                default:
                    result.Decision = $"act '{candidate.Kind}'";
                    result.AuthorityUsed = $"delegated scope '{candidate.RequiredScope}'";
                    result.ResultingReference = $"intent:{candidate.Kind}:{inputs.BusinessInstanceId}:d{inputs.DayIndex}";
                    result.Outcome = "intent reported";
                    break;
            }

            if (doOnce != null)
            {
                instructionRegister.ConsumeDoOnce(doOnce.InstructionId, inputs.DayIndex, diag);
                result.Outcome += $" | DoOnce '{doOnce.InstructionId}' consumed";
            }

            diag.Add($"ManagerBrain: day {inputs.DayIndex} - {result.Decision} [{result.AuthorityUsed}].");
            AppendRecord(inputs, result.Decision, result.Evidence, result.AuthorityUsed,
                result.ResultingReference, result.Outcome,
                result.Escalated ? result.EscalationReason : string.Empty, result.TimeCostMinutes);
            return result;
        }

        private bool RecentRecruitmentIntent(string businessInstanceId, int dayIndex)
        {
            foreach (ManagerActivityRecord record in activityLog.Records)
            {
                if (string.Equals(record.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal)
                    && record.ResultingReference != null
                    && record.ResultingReference.StartsWith("intent:recruitment-effort:", StringComparison.Ordinal)
                    && record.DayIndex >= dayIndex - 7)
                {
                    return true;
                }
            }

            return false;
        }

        private void AppendRecord(
            ManagerCycleInputs inputs, string decision, string evidence, string authorityUsed,
            string resultingReference, string outcome, string exceptionsOrEscalations, int timeCostMinutes)
        {
            activityLog.Append(new ManagerActivityRecord
            {
                ManagerPersonId = inputs.ManagerPersonId,
                BusinessInstanceId = inputs.BusinessInstanceId,
                DayIndex = inputs.DayIndex,
                ObservedConditions = DescribeConditions(inputs),
                Decision = decision,
                Evidence = evidence,
                AuthorityUsed = authorityUsed,
                ResultingReference = resultingReference ?? string.Empty,
                Outcome = outcome,
                ExceptionsOrEscalations = exceptionsOrEscalations ?? string.Empty,
                TimeCostMinutes = timeCostMinutes,
                IsSignificant = true
            });
        }

        private static string DescribeConditions(ManagerCycleInputs inputs)
        {
            LaborDemandReport demand = inputs.DemandReport;
            string demandLine = demand != null
                ? $"demand: {demand.TotalUnmetMinutes} unmet minutes, coverageGap={demand.CoverageGap}, hiringPressure={demand.HiringPressure}"
                : "demand: no report";
            return $"{demandLine}; cash {inputs.CashCents}c; shortage={inputs.ShortageObserved}; " +
                   $"scopes=[{string.Join(",", inputs.DelegatedScopes ?? new List<string>())}]";
        }

        public ManagerBrainSaveDto CaptureSaveDto()
        {
            return new ManagerBrainSaveDto
            {
                ActivityLog = activityLog.CaptureSaveDto(),
                Instructions = instructionRegister.CaptureSaveDto()
            };
        }

        /// <summary>Restores the saved manager snapshot without duplicating history.</summary>
        public void LoadFromSaveDto(ManagerBrainSaveDto dto)
        {
            if (dto == null)
            {
                return;
            }

            instructionRegister.LoadFromSaveDto(dto.Instructions);
            activityLog.LoadFromSaveDto(dto.ActivityLog);
        }
    }
}
