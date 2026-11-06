using System;

namespace LandLedgers.Economy.Core
{
    /// <summary>
    /// Plan 04 (2026-11-06) slice M5 (Obj 43, D-S6): the pure part of
    /// the loud-wiring sweep — the null-authority diagnostic strings.
    /// SharedBusinessRuntimeManager (harness-excluded) calls
    /// BuildUnwiredAuthorityWarning at each silent-null site and logs
    /// the result; the MonoBehaviour logging is source-reviewed, the
    /// strings are pinned here by SharedBusinessWiringTests.
    /// </summary>
    public static class SharedBusinessWiring
    {
        /// <summary>
        /// One warning per still-null authority, naming it and its
        /// consequence, in the plan's exact format:
        /// "SharedBusinessRuntimeManager: &lt;authority&gt; is not wired;
        /// &lt;consequence&gt;". Blank inputs fall back to generic wording
        /// so a call site can never produce an empty warning.
        /// </summary>
        public static string BuildUnwiredAuthorityWarning(string authority, string consequence)
        {
            string named = string.IsNullOrWhiteSpace(authority) ? "an authority" : authority.Trim();
            string effect = string.IsNullOrWhiteSpace(consequence) ? "the depending work was skipped" : consequence.Trim();
            return $"SharedBusinessRuntimeManager: {named} is not wired; {effect}";
        }
    }
}
