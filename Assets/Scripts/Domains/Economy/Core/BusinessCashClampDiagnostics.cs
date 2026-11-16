using System;
using UnityEngine;

namespace LandLedgers.Economy
{
    /// <summary>
    /// CAMPAIGN-B (DEF-055), package 13: no single cash writer; primitives
    /// clamp silently. DIAGNOSTIC LAYER ONLY — no clamp-to-refuse change, no
    /// ledger, no cash policy.
    ///
    /// The cash primitives on BusinessRuntimeState (SpendCents,
    /// SetCurrentCashCents, AddCashCents) clamp silently: SpendCents spends
    /// Min(balance, Max(0, requested)) and SetCurrentCashCents / AddCashCents
    /// clamp to Max(0, ...), all without any diagnostic. Every live caller
    /// pre-checks the balance or bounds the spend (GeneralStoreTradingPort
    /// refuses when CurrentCashCents &lt; spend; SharedBusinessWeeklyProcurement
    /// spends only affordableUnits; PlayerPortfolioManager spends only the
    /// surplus above the protected reserve; AssetSaleAppraisal verifies the
    /// balance first; CalculatePostAcquisitionOpeningCashCents can never
    /// return negative), so a bite is exceptional by construction — which is
    /// exactly why it must be loud when it happens instead of silent.
    ///
    /// This class owns the clamp decision and the warning text so the
    /// diagnostic is behaviorally testable: the REAL BusinessRuntimeState
    /// cannot compile in the Roslyn harness (its closure runs through
    /// BusinessInstanceState.cs, which pulls ScriptableObject and ~130
    /// types), so the primitives call these Warn* methods and the harness
    /// pins (a) the helper's behaviour directly and (b) the wiring by
    /// source inspection (CampaignBCashClampDiagnosticsTests). Per-occurrence,
    /// not day-keyed: the primitives carry no day index (PayrollDayIndex is
    /// payroll-specific and -1 when unknown), and day-keying would mean
    /// threading a day through six call sites and changing public
    /// signatures for a diagnostic. Callers are daily / weekly /
    /// transactional drivers, not per-frame, so per-occurrence volume stays
    /// proportional to anomalies. The warning text below is identical to
    /// the inline Debug.LogWarning text the primitives carried before this
    /// extraction, so the observable diagnostic is unchanged.
    ///
    /// TrySpendCents is NOT a clamp — it refuses (returns false) — and
    /// emits no clamp diagnostic. AdjustCashCents needs no separate hunk:
    /// it routes through SetCurrentCashCents, so a negative delta past zero
    /// logs there.
    /// </summary>
    public static class BusinessCashClampDiagnostics
    {
        /// <summary>True when SpendCents would clamp: a negative request, or
        /// a request larger than the balance (partial spend).</summary>
        public static bool SpendClamps(int requested, int balance)
        {
            if (requested < 0)
            {
                return true;
            }

            int spend = Math.Min(balance, Math.Max(0, requested));
            return spend < requested;
        }

        public static string SpendClampMessage(string businessId, int requested, int balance, int spent)
        {
            if (requested < 0)
            {
                return "[BusinessCashClamp] Business '" + businessId + "' SpendCents requested "
                    + requested + "c (negative); clamped to 0c, no cash moved (balance "
                    + balance + "c).";
            }

            return "[BusinessCashClamp] Business '" + businessId + "' SpendCents requested "
                + requested + "c with balance " + balance + "c; spent " + spent
                + "c (shortfall " + (requested - spent) + "c).";
        }

        /// <summary>Logs the clamp warning exactly when the spend clamps;
        /// silent on an in-balance or exact-balance spend.</summary>
        public static void WarnSpendClamp(string businessId, int requested, int balance, int spent)
        {
            if (SpendClamps(requested, balance))
            {
                Debug.LogWarning(SpendClampMessage(businessId, requested, balance, spent));
            }
        }

        /// <summary>True when SetCurrentCashCents would clamp: a negative
        /// target. Setting to exactly zero is a legitimate set, not a clamp.</summary>
        public static bool SetClamps(int requested)
        {
            return requested < 0;
        }

        public static string SetClampMessage(string businessId, int requested, int priorBalance)
        {
            return "[BusinessCashClamp] Business '" + businessId + "' SetCurrentCashCents requested "
                + requested + "c (negative); clamped to 0c (balance was " + priorBalance + "c).";
        }

        /// <summary>Logs the clamp warning exactly when the set clamps;
        /// silent on an ordinary or zero set.</summary>
        public static void WarnSetClamp(string businessId, int requested, int priorBalance)
        {
            if (SetClamps(requested))
            {
                Debug.LogWarning(SetClampMessage(businessId, requested, priorBalance));
            }
        }

        /// <summary>True when AddCashCents would clamp: a negative add.</summary>
        public static bool AddClamps(int requested)
        {
            return requested < 0;
        }

        public static string AddClampMessage(string businessId, int requested, int balance)
        {
            return "[BusinessCashClamp] Business '" + businessId + "' AddCashCents requested "
                + requested + "c (negative); clamped to 0c, no cash added (balance "
                + balance + "c).";
        }

        /// <summary>Logs the clamp warning exactly when the add clamps;
        /// silent on an ordinary positive add.</summary>
        public static void WarnAddClamp(string businessId, int requested, int balance)
        {
            if (AddClamps(requested))
            {
                Debug.LogWarning(AddClampMessage(businessId, requested, balance));
            }
        }
    }
}
