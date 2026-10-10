using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Phase B (Real People): OQ-8 deterministic repair for the retired
    /// HouseholdState.spendingMoneyCents wallet. The ledger is the single
    /// household-cash truth; any balance still sitting in the legacy field is
    /// booked exactly once as an explicit "legacy balance migration" inflow
    /// with provenance, so money is neither duplicated nor lost and never
    /// silently dropped. Ambiguous cases (negative or unreadable balances) are
    /// quarantined to diagnostics, never booked.
    /// </summary>
    public static class HouseholdLegacyCashMigrator
    {
        public const string MigrationSourceReference = "legacy-spending-money-migration";
        public const string MigrationReason =
            "Phase B unification: legacy spendingMoneyCents balance migrated into the household ledger (single cash truth).";

        /// <summary>
        /// Migrates every household's legacy wallet balance into its ledger.
        /// Idempotent: only households with a positive legacy balance are
        /// touched, and the field is zeroed after booking so a second pass (or
        /// a reloaded migrated save) books nothing.
        /// </summary>
        public static void MigrateAll(
            IEnumerable<HouseholdState> households,
            HouseholdLedgerRegistry ledgers,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (households == null || ledgers == null)
            {
                diagnostics.Add("HouseholdLegacyCashMigrator: households or ledger registry missing — migration skipped.");
                return;
            }

            int day = Math.Max(0, dayIndex);
            int migrated = 0;
            foreach (HouseholdState household in households)
            {
                if (household == null)
                {
                    continue;
                }

                int legacyBalance = household.spendingMoneyCents;
                if (legacyBalance <= 0)
                {
                    if (legacyBalance < 0)
                    {
                        diagnostics.Add(
                            $"H{household.id}: negative legacy balance {legacyBalance}c quarantined (not booked); field reset to 0.");
                        household.spendingMoneyCents = 0;
                    }

                    continue;
                }

                HouseholdLedger ledger = ledgers.GetOrCreate(household.id);
                string rejection = ledger.RecordInflow(
                    day,
                    legacyBalance,
                    HouseholdIncomeSource.OtherDocumented,
                    MigrationSourceReference,
                    MigrationReason,
                    "legacy save");
                if (rejection != null)
                {
                    diagnostics.Add($"H{household.id}: legacy balance migration rejected — {rejection}");
                    continue;
                }

                household.spendingMoneyCents = 0;
                migrated++;
            }

            diagnostics.Add($"HouseholdLegacyCashMigrator: migrated {migrated} legacy balances.");
        }
    }
}
