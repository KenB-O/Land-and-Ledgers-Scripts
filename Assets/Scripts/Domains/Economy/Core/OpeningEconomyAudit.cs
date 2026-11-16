using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// DEF-013 (Decision 9): opening-economy audit. After a New Game has
    /// applied every endowment and seeded the player stake, the authored
    /// opening figures (business cash targets, household cash targets, owner
    /// stake, liabilities) must equal the actual balances. Diagnostic only:
    /// it reports mismatches, never corrects or fabricates anything. The
    /// dev scenario authors no debts, no pantry lots, and no lenders at
    /// opening, so the authored liability is zero; any liability at opening
    /// is reported.
    /// </summary>
    public static class OpeningEconomyAudit
    {
        /// <summary>Authored opening figures, all in cents.</summary>
        public sealed class AuthoredOpening
        {
            public Dictionary<string, int> BusinessCashTargets =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, int> HouseholdCashTargets =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public int OwnerStakeCents;
            public int AuthoredLiabilityCents;
        }

        /// <summary>Actual opening balances, all in cents.</summary>
        public sealed class ActualOpening
        {
            public Dictionary<string, int> BusinessCash =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, int> HouseholdCash =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public int OwnerCashCents;
            public int LiabilityCents;
        }

        /// <summary>
        /// Returns one diagnostic per problem found. An empty list means the
        /// opening matches the authored figures exactly.
        /// </summary>
        public static List<string> Check(AuthoredOpening authored, ActualOpening actual)
        {
            var diagnostics = new List<string>();
            if (authored == null)
            {
                diagnostics.Add("Opening audit refused: no authored opening figures supplied.");
                return diagnostics;
            }

            if (actual == null)
            {
                diagnostics.Add("Opening audit refused: no actual opening balances supplied.");
                return diagnostics;
            }

            long authoredTotalCents = 0;
            long actualTotalCents = 0;

            foreach (KeyValuePair<string, int> target in authored.BusinessCashTargets)
            {
                authoredTotalCents += target.Value;
                if (!actual.BusinessCash.TryGetValue(target.Key, out int balance))
                {
                    diagnostics.Add($"Business '{target.Key}' is missing from the opening state (authored {target.Value}c).");
                    continue;
                }

                actualTotalCents += balance;
                CheckBalance(diagnostics, "Business", target.Key, target.Value, balance);
            }

            foreach (KeyValuePair<string, int> target in authored.HouseholdCashTargets)
            {
                authoredTotalCents += target.Value;
                if (!actual.HouseholdCash.TryGetValue(target.Key, out int balance))
                {
                    diagnostics.Add($"Household '{target.Key}' is missing from the opening state (authored {target.Value}c).");
                    continue;
                }

                actualTotalCents += balance;
                CheckBalance(diagnostics, "Household", target.Key, target.Value, balance);
            }

            authoredTotalCents += authored.OwnerStakeCents;
            actualTotalCents += actual.OwnerCashCents;
            CheckBalance(diagnostics, "Owner cash", "player", authored.OwnerStakeCents, actual.OwnerCashCents);

            if (actual.LiabilityCents != authored.AuthoredLiabilityCents)
            {
                diagnostics.Add($"Opening liabilities are {actual.LiabilityCents}c, authored {authored.AuthoredLiabilityCents}c.");
            }

            if (actualTotalCents != authoredTotalCents)
            {
                diagnostics.Add($"Total opening cash is {actualTotalCents}c, authored {authoredTotalCents}c.");
            }

            return diagnostics;
        }

        private static void CheckBalance(
            List<string> diagnostics,
            string pool,
            string key,
            int authoredCents,
            int actualCents)
        {
            if (actualCents < 0)
            {
                diagnostics.Add($"{pool} '{key}' has a negative opening balance of {actualCents}c (authored {authoredCents}c).");
                return;
            }

            if (actualCents != authoredCents)
            {
                diagnostics.Add($"{pool} '{key}' holds {actualCents}c at opening, authored {authoredCents}c.");
            }
        }
    }
}
