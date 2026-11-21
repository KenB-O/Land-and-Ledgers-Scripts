// Land & Ledgers — A-to-Z automated audit finding statuses (Lane 8, §20/§22).
//
// The §22 status vocabulary, verbatim, as the single status scale for every
// automated A-to-Z check. A check's status is a statement about the EVIDENCE
// behind it, never a judgement call:
//   - VERIFIED FIXED: the fix is evidenced AND the behaviour is confirmed
//     (harness test or run evidence). Source-only confirmation is NOT this.
//   - REGRESSION VERIFIED: a previously fixed behaviour is confirmed broken
//     again by a run or a harness test.
//   - IMPLEMENTED UNVERIFIED: the fix is present in source, but no run or
//     harness test has confirmed the behaviour yet.
//   - SUSPECTED FIX: a mechanism exists that should prevent the failure, but
//     the original failure was observed in a retained run and no new run has
//     re-observed the failure path since.
//   - UNRESOLVED: the defect is still present in source or in a run.
//   - BLOCKED: the check cannot run — a live run or a prior repair is needed
//     first, and the blocker is named.
//   - NOT REPRODUCED: the reported behaviour could not be reproduced from
//     the current source or a run.
//   - EXPLAINED LEGITIMATE: the behaviour follows from a legitimate rule
//     (hardship, Canon, an owner decision) and is not a defect.
//   - NEW REGRESSION: a behaviour that worked before now fails, first seen
//     in a comparison (Tool 11); it is a candidate until investigated.
//
// No run exists on this HEAD: every status that needs run evidence says so
// in the finding's detail instead of guessing.

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// Lane 8 (§22) — the status vocabulary for automated A-to-Z findings.
    /// </summary>
    public enum AtoZFindingStatus
    {
        /// <summary>Fix evidenced AND behaviour confirmed (harness or run).</summary>
        VerifiedFixed = 0,

        /// <summary>A previously fixed behaviour is confirmed broken again.</summary>
        RegressionVerified = 1,

        /// <summary>Fix present in source; no run or harness confirmation yet.</summary>
        ImplementedUnverified = 2,

        /// <summary>Preventing mechanism exists in source; no new run has re-observed the failure path.</summary>
        SuspectedFix = 3,

        /// <summary>Defect still present in source or in a run.</summary>
        Unresolved = 4,

        /// <summary>Cannot run — a live run or a prior repair is needed first (named in the detail).</summary>
        Blocked = 5,

        /// <summary>Reported behaviour could not be reproduced from current source or a run.</summary>
        NotReproduced = 6,

        /// <summary>Behaviour follows from a legitimate rule; not a defect.</summary>
        ExplainedLegitimate = 7,

        /// <summary>Behaviour that worked before now fails, first seen in a comparison.</summary>
        NewRegression = 8,
    }

    /// <summary>Stable §22 text labels for <see cref="AtoZFindingStatus"/>.</summary>
    public static class AtoZFindingStatusText
    {
        /// <summary>The canonical §22 label for a status.</summary>
        public static string Label(AtoZFindingStatus status)
        {
            switch (status)
            {
                case AtoZFindingStatus.VerifiedFixed: return "VERIFIED FIXED";
                case AtoZFindingStatus.RegressionVerified: return "REGRESSION VERIFIED";
                case AtoZFindingStatus.ImplementedUnverified: return "IMPLEMENTED UNVERIFIED";
                case AtoZFindingStatus.SuspectedFix: return "SUSPECTED FIX";
                case AtoZFindingStatus.Unresolved: return "UNRESOLVED";
                case AtoZFindingStatus.Blocked: return "BLOCKED";
                case AtoZFindingStatus.NotReproduced: return "NOT REPRODUCED";
                case AtoZFindingStatus.ExplainedLegitimate: return "EXPLAINED LEGITIMATE";
                case AtoZFindingStatus.NewRegression: return "NEW REGRESSION";
                default: return "UNRESOLVED";
            }
        }
    }
}
