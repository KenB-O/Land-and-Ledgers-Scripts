using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Delegation
{
    /// <summary>
    /// T2D: what a delegate may be authorized to do. Routine acts are
    /// delegable; the reserved three ALWAYS escalate to the owner no matter
    /// how broad the grant (Tech X §12.3 Autonomous Crown).
    /// </summary>
    public enum DelegatedAct
    {
        Unspecified = 0,
        // Routine — delegable within scope and limits.
        PurchaseSupplies = 1,
        ScheduleWork = 2,
        OrderMaintenance = 3,
        HandleCash = 4,
        ManageInventory = 5,
        HireStaff = 6,
        AssignTasks = 7,
        // Owner-reserved — escalate, always.
        SellBusiness = 101,
        MortgageProperty = 102,
        MajorBorrowing = 103,
    }

    [Flags]
    public enum DelegationScope
    {
        None = 0,
        Purchasing = 1,
        Scheduling = 2,
        Maintenance = 4,
        CashHandling = 8,
        Inventory = 16,
        Staffing = 32,
        AllRoutine = Purchasing | Scheduling | Maintenance | CashHandling | Inventory | Staffing,
    }

    public enum AuthorityVerdict
    {
        Unspecified = 0,
        Permitted = 1,  // the delegate may act — no owner click needed
        Escalated = 2,  // owner-reserved or over limits — the owner decides
        Denied = 3,     // no authority at all
    }

    /// <summary>T2D: one standing grant of authority to a real person.</summary>
    [Serializable]
    public sealed class DelegatedAuthority
    {
        public string AuthorityId = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public int DelegatePersonId = -1;
        public DelegationScope Scopes;
        public int PurchaseLimitCents;   // routine purchases above this escalate
        public string RequiredSkillId = string.Empty; // qualification gate
        public int GrantedDayIndex;
        public int RevokedDayIndex = -1;

        public bool IsActive(int dayIndex) =>
            RevokedDayIndex < 0 || dayIndex < RevokedDayIndex;

        public DelegatedAuthority() { }
    }

    /// <summary>T2D: one auditable delegated decision (the Autonomous Crown
    /// absence simulation needs a trail, not just a predicate).</summary>
    [Serializable]
    public sealed class DelegateDecision
    {
        public int DayIndex;
        public string BusinessInstanceId = string.Empty;
        public int DelegatePersonId = -1;
        public DelegatedAct Act;
        public int AmountCents;
        public AuthorityVerdict Verdict;
        public string Reason = string.Empty;

        public DelegateDecision() { }
    }

    /// <summary>
    /// T2D: standing delegated authority (GHOST-DEF-008, Tech X §12.3).
    /// Authority is a PREDICATE, not a bot: the simulation asks whether an
    /// act by a delegate is permitted; routine acts proceed without owner
    /// clicks, reserved acts escalate. The fixture's demand is judged sound:
    /// "Routine operation must not require hidden owner clicks" is exactly
    /// portfolio play, and the reserved list (sale/mortgage/major borrowing)
    /// is the explicit boundary. No design fork — built as specified.
    /// </summary>
    public sealed class DelegationService
    {
        /// <summary>Borrowing at or above this is "major" and owner-reserved (calibration).</summary>
        public const int MajorBorrowingThresholdCents = 50000;

        private readonly Dictionary<string, DelegatedAuthority> authorities =
            new Dictionary<string, DelegatedAuthority>(StringComparer.Ordinal);
        private readonly List<DelegateDecision> decisions = new List<DelegateDecision>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<DelegateDecision> Decisions => decisions;

        /// <param name="hasSkill">Qualification gate: (personId, skillId) → true when the person is qualified.</param>
        public DelegatedAuthority GrantAuthority(
            EntityIdRegistry ids, string businessInstanceId, int delegatePersonId,
            DelegationScope scopes, int purchaseLimitCents, string requiredSkillId,
            Func<int, string, bool> hasSkill, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null) { diag.Add("DelegationService.GrantAuthority: id registry required."); return null; }
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                diag.Add("DelegationService.GrantAuthority: a business is required — authority needs a domain.");
                return null;
            }
            if (delegatePersonId < 0)
            {
                diag.Add("DelegationService.GrantAuthority: the delegate must be a real person. Authority is never granted to nobody.");
                return null;
            }
            if (scopes == DelegationScope.None)
            {
                diag.Add("DelegationService.GrantAuthority: no scopes granted — an empty grant authorizes nothing.");
                return null;
            }
            if (!string.IsNullOrWhiteSpace(requiredSkillId) && hasSkill != null && !hasSkill(delegatePersonId, requiredSkillId))
            {
                diag.Add($"DelegationService.GrantAuthority: P{delegatePersonId} lacks '{requiredSkillId}' — the Autonomous Crown requires a QUALIFIED manager (Tech X §12.3).");
                return null;
            }

            var authority = new DelegatedAuthority
            {
                AuthorityId = ids.Allocate(EntityKind.Contract).ToString(),
                BusinessInstanceId = businessInstanceId,
                DelegatePersonId = delegatePersonId,
                Scopes = scopes,
                PurchaseLimitCents = Math.Max(0, purchaseLimitCents),
                RequiredSkillId = requiredSkillId ?? string.Empty,
                GrantedDayIndex = dayIndex,
            };
            authorities[authority.AuthorityId] = authority;
            diag.Add($"DelegationService: authority {authority.AuthorityId} — P{delegatePersonId} may act for '{businessInstanceId}' within {scopes} (purchases to {purchaseLimitCents}c).");
            return authority;
        }

        public string Revoke(string authorityId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!authorities.TryGetValue(authorityId, out DelegatedAuthority authority))
                return $"DelegationService.Revoke: unknown authority '{authorityId}'.";
            authority.RevokedDayIndex = dayIndex;
            diag.Add($"DelegationService: authority {authorityId} revoked on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// T2D: the authority predicate. Every call is audited.
        /// </summary>
        public AuthorityVerdict CheckAct(
            string businessInstanceId, int personId, DelegatedAct act,
            int amountCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            AuthorityVerdict verdict;
            string reason;

            if (IsReserved(act))
            {
                verdict = AuthorityVerdict.Escalated;
                reason = $"'{act}' is owner-reserved — it escalates no matter how broad the grant (Tech X §12.3).";
            }
            else
            {
                DelegatedAuthority authority = FindActive(businessInstanceId, personId, dayIndex);
                if (authority == null)
                {
                    verdict = AuthorityVerdict.Denied;
                    reason = $"P{personId} holds no active authority for '{businessInstanceId}'.";
                }
                else if (!ScopeCovers(authority.Scopes, act))
                {
                    verdict = AuthorityVerdict.Denied;
                    reason = $"Authority {authority.AuthorityId} does not cover '{act}'.";
                }
                else if (act == DelegatedAct.PurchaseSupplies && amountCents > authority.PurchaseLimitCents)
                {
                    verdict = AuthorityVerdict.Escalated;
                    reason = $"Purchase of {amountCents}c exceeds the {authority.PurchaseLimitCents}c delegated limit — escalates.";
                }
                else
                {
                    verdict = AuthorityVerdict.Permitted;
                    reason = $"Permitted under authority {authority.AuthorityId} ({authority.Scopes}). No owner click required.";
                }
            }

            decisions.Add(new DelegateDecision
            {
                DayIndex = dayIndex,
                BusinessInstanceId = businessInstanceId,
                DelegatePersonId = personId,
                Act = act,
                AmountCents = amountCents,
                Verdict = verdict,
                Reason = reason,
            });
            return verdict;
        }

        private static bool IsReserved(DelegatedAct act) =>
            act == DelegatedAct.SellBusiness ||
            act == DelegatedAct.MortgageProperty ||
            act == DelegatedAct.MajorBorrowing;

        private static bool ScopeCovers(DelegationScope scopes, DelegatedAct act)
        {
            return act switch
            {
                DelegatedAct.PurchaseSupplies => scopes.HasFlag(DelegationScope.Purchasing),
                DelegatedAct.ScheduleWork or DelegatedAct.AssignTasks => scopes.HasFlag(DelegationScope.Scheduling),
                DelegatedAct.OrderMaintenance => scopes.HasFlag(DelegationScope.Maintenance),
                DelegatedAct.HandleCash => scopes.HasFlag(DelegationScope.CashHandling),
                DelegatedAct.ManageInventory => scopes.HasFlag(DelegationScope.Inventory),
                DelegatedAct.HireStaff => scopes.HasFlag(DelegationScope.Staffing),
                _ => false,
            };
        }

        private DelegatedAuthority FindActive(string businessInstanceId, int personId, int dayIndex)
        {
            foreach (DelegatedAuthority authority in authorities.Values)
            {
                if (authority.IsActive(dayIndex) &&
                    string.Equals(authority.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal) &&
                    authority.DelegatePersonId == personId)
                    return authority;
            }
            return null;
        }

        #region Save / Load
        [Serializable]
        public sealed class DelegationSaveDto
        {
            public List<DelegatedAuthority> Authorities = new List<DelegatedAuthority>();
            public List<DelegateDecision> Decisions = new List<DelegateDecision>();
        }

        public DelegationSaveDto CaptureSaveDto()
        {
            return new DelegationSaveDto
            {
                Authorities = new List<DelegatedAuthority>(authorities.Values),
                Decisions = new List<DelegateDecision>(decisions),
            };
        }

        public void LoadFromSaveDto(DelegationSaveDto dto)
        {
            authorities.Clear();
            decisions.Clear();
            if (dto == null) return;
            foreach (var a in dto.Authorities) authorities[a.AuthorityId] = a;
            decisions.AddRange(dto.Decisions);
        }
        #endregion
    }
}
