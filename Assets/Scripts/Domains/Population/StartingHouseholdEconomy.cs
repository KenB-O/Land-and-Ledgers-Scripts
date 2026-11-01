using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P1-5 (Worker 3 side): the household-economy half of starting
    /// world coherence. World generation (Worker 1) decides WHO lives where
    /// and works where; this seeder turns those decisions into coherent
    /// starting economic state through the production authorities:
    ///
    ///   employment -> real EmploymentRelationships with agreed wages;
    ///   cash       -> real HouseholdLedger inflows with documented sources;
    ///   debts      -> real FinancialObligations with named creditors/terms;
    ///   pantry     -> real inventory lots with provenance;
    ///   lenders    -> wealthy NPCs registered in the HouseholdLenderDirectory
    ///                 with documented capital (the P2-2 credit source).
    ///
    /// Every inflow names its source (Canon 13.2). Anything that fails
    /// validation is refused loudly and seeded nothing — a half-built
    /// starting economy is worse than a loud refusal.
    /// </summary>
    public static class StartingHouseholdEconomy
    {
        [Serializable]
        public sealed class StartingEmploymentSpec
        {
            public int PersonId = -1;
            public string BusinessId = string.Empty;
            public string RoleDisplayName = string.Empty;
            public int WeeklyWageCents;
        }

        [Serializable]
        public sealed class StartingDebtSpec
        {
            public string Creditor = string.Empty;
            public int PrincipalCents;
            public string Terms = string.Empty;
            public string Purpose = string.Empty;
        }

        [Serializable]
        public sealed class StartingPantryLotSpec
        {
            public string ItemId = string.Empty;
            public int Units;
            public string Source = string.Empty;
        }

        [Serializable]
        public sealed class StartingLenderSpec
        {
            public int PersonId = -1;
            public string LenderName = string.Empty;
            public int CapitalCents;
            public string CapitalSource = string.Empty;
        }

        [Serializable]
        public sealed class StartingHouseholdSpec
        {
            public int HouseholdId = -1;
            public int OpeningCashCents;
            public string OpeningCashSource = string.Empty;
            public List<StartingEmploymentSpec> Employments = new List<StartingEmploymentSpec>();
            public List<StartingDebtSpec> Debts = new List<StartingDebtSpec>();
            public List<StartingPantryLotSpec> PantryLots = new List<StartingPantryLotSpec>();
            public StartingLenderSpec Lender;
        }

        public sealed class SeederContext
        {
            public HouseholdLedgerRegistry Ledgers;
            public EmploymentRelationshipRegistry Employments;
            public FinancialObligationAuthority Obligations;
            public EntityIdRegistry Ids;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdLenderDirectory Lenders;
            public Func<int, bool> HouseholdExists;
            public Func<int, bool> PersonExists;
            public Func<string, bool> BusinessExists;
        }

        /// <summary>
        /// Seeds every household spec. Returns the number of households
        /// seeded; refusals are recorded in diagnostics and that household is
        /// skipped (nothing partially seeded for it).
        /// </summary>
        public static int Seed(IEnumerable<StartingHouseholdSpec> specs, SeederContext ctx,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (specs == null || ctx == null)
            {
                diag.Add("StartingHouseholdEconomy: no specs or context — nothing seeded.");
                return 0;
            }

            int seeded = 0;
            foreach (StartingHouseholdSpec spec in specs)
            {
                if (spec == null) continue;
                string refusal = Validate(spec, ctx);
                if (refusal != null)
                {
                    diag.Add($"StartingHouseholdEconomy: household {spec.HouseholdId} refused: {refusal}");
                    continue;
                }

                SeedOne(spec, ctx, dayIndex, diag);
                seeded++;
            }

            diag.Add($"StartingHouseholdEconomy: seeded {seeded} household(s) with coherent starting economics.");
            return seeded;
        }

        private static string Validate(StartingHouseholdSpec spec, SeederContext ctx)
        {
            if (spec.HouseholdId < 0) return "household id is negative.";
            if (ctx.HouseholdExists != null && !ctx.HouseholdExists(spec.HouseholdId))
                return $"household {spec.HouseholdId} does not exist in the generated population.";
            if (spec.OpeningCashCents < 0) return "opening cash cannot be negative.";
            if (spec.OpeningCashCents > 0 && string.IsNullOrWhiteSpace(spec.OpeningCashSource))
                return "opening cash needs a documented source (Canon 13.2).";

            if (spec.Employments != null)
            {
                foreach (StartingEmploymentSpec emp in spec.Employments)
                {
                    if (emp == null) continue;
                    if (ctx.PersonExists != null && !ctx.PersonExists(emp.PersonId))
                        return $"employment references unknown person P{emp.PersonId}.";
                    if (ctx.BusinessExists != null && !ctx.BusinessExists(emp.BusinessId))
                        return $"employment references unknown business '{emp.BusinessId}'.";
                    if (emp.WeeklyWageCents < 0) return "wage cannot be negative.";
                }
            }

            if (spec.Debts != null)
            {
                foreach (StartingDebtSpec debt in spec.Debts)
                {
                    if (debt == null) continue;
                    if (string.IsNullOrWhiteSpace(debt.Creditor)) return "a debt must name its creditor.";
                    if (debt.PrincipalCents <= 0) return "debt principal must be positive.";
                    if (string.IsNullOrWhiteSpace(debt.Terms)) return "a debt must carry terms.";
                }
            }

            if (spec.PantryLots != null)
            {
                foreach (StartingPantryLotSpec lot in spec.PantryLots)
                {
                    if (lot == null) continue;
                    if (HouseholdItemCatalog.Get(lot.ItemId) == null)
                        return $"unknown pantry item '{lot.ItemId}'.";
                    if (lot.Units <= 0) return "pantry lot units must be positive.";
                    if (string.IsNullOrWhiteSpace(lot.Source))
                        return "pantry lots need provenance (no synthetic stock).";
                }
            }

            if (spec.Lender != null)
            {
                if (string.IsNullOrWhiteSpace(spec.Lender.LenderName)) return "lender must be named.";
                if (spec.Lender.CapitalCents <= 0) return "lender capital must be positive.";
                if (string.IsNullOrWhiteSpace(spec.Lender.CapitalSource))
                    return "lender capital needs a documented source.";
            }

            return null;
        }

        private static void SeedOne(StartingHouseholdSpec spec, SeederContext ctx,
            int dayIndex, List<string> diag)
        {
            // Cash first: the ledger is the single cash truth.
            HouseholdLedger ledger = ctx.Ledgers.GetOrCreate(spec.HouseholdId);
            if (spec.OpeningCashCents > 0)
            {
                string refusal = ledger.RecordInflow(dayIndex, spec.OpeningCashCents,
                    HouseholdIncomeSource.OtherDocumented, "world-gen",
                    $"starting cash: {spec.OpeningCashSource}", "world-gen");
                if (refusal != null) diag.Add($"StartingHouseholdEconomy: household {spec.HouseholdId} cash: {refusal}");
            }

            // Employment with agreed (never derived) wages.
            if (spec.Employments != null)
            {
                foreach (StartingEmploymentSpec emp in spec.Employments)
                {
                    if (emp == null) continue;
                    var rel = new EmploymentRelationship
                    {
                        Id = $"gen-emp-{spec.HouseholdId}-{emp.PersonId}",
                        EmployeePersonId = emp.PersonId,
                        EmployerBusinessId = emp.BusinessId,
                        RoleDisplayName = emp.RoleDisplayName ?? string.Empty,
                        LifecycleState = EmploymentLifecycleState.Active,
                        Compensation = CompensationTerms.FromWeeklyWage(
                            Math.Max(0, emp.WeeklyWageCents), "starting wage (world-gen)"),
                    };
                    string refusal = ctx.Employments.Register(rel);
                    if (refusal != null) diag.Add($"StartingHouseholdEconomy: {refusal}");
                }
            }

            // Legitimate debts through the single debt authority.
            if (spec.Debts != null)
            {
                foreach (StartingDebtSpec debt in spec.Debts)
                {
                    if (debt == null) continue;
                    FinancialObligation obligation = ctx.Obligations.Create(
                        ctx.Ids, FinancialObligationKind.Loan,
                        "household:" + spec.HouseholdId, debt.Creditor, debt.PrincipalCents,
                        dayIndex, debt.Terms, debt.Purpose ?? "starting debt (world-gen)",
                        $"gen-debt-{spec.HouseholdId}-{debt.Creditor}");
                    if (obligation == null)
                        diag.Add($"StartingHouseholdEconomy: household {spec.HouseholdId} debt to {debt.Creditor} refused by the debt authority.");
                }
            }

            // Pantry with provenance.
            if (spec.PantryLots != null)
            {
                HouseholdInventory inventory = ctx.Inventories.GetOrCreate(spec.HouseholdId);
                foreach (StartingPantryLotSpec lot in spec.PantryLots)
                {
                    if (lot == null) continue;
                    string refusal = inventory.AddLot(lot.ItemId, lot.Units, dayIndex, lot.Source);
                    if (refusal != null) diag.Add($"StartingHouseholdEconomy: {refusal}");
                }
            }

            // Wealthy NPCs become the world's private lenders (P2-2).
            if (spec.Lender != null && ctx.Lenders != null)
            {
                string refusal = ctx.Lenders.RegisterPrivateLender(
                    spec.Lender.LenderName, spec.Lender.PersonId,
                    spec.Lender.CapitalCents, spec.Lender.CapitalSource);
                if (refusal != null) diag.Add($"StartingHouseholdEconomy: lender: {refusal}");
            }
        }

        /// <summary>
        /// Coherence check over seeded households: every household has a
        /// ledger, no negative balances, every lot has provenance, every debt
        /// names a creditor with terms. Returns diagnostics (empty = coherent).
        /// </summary>
        public static List<string> ValidateCoherence(IEnumerable<int> householdIds, SeederContext ctx)
        {
            var problems = new List<string>();
            if (householdIds == null || ctx == null) return problems;
            foreach (int householdId in householdIds)
            {
                HouseholdLedger ledger = ctx.Ledgers != null ? ctx.Ledgers.Get(householdId) : null;
                if (ledger == null)
                {
                    problems.Add($"household {householdId}: no ledger.");
                    continue;
                }

                if (ledger.GetBalanceCents() < 0)
                    problems.Add($"household {householdId}: negative balance {ledger.GetBalanceCents()}c.");

                HouseholdInventory inventory = ctx.Inventories != null ? ctx.Inventories.Get(householdId) : null;
                if (inventory != null)
                {
                    foreach (HouseholdItemLot lot in inventory.Lots)
                    {
                        if (lot == null) continue;
                        if (string.IsNullOrWhiteSpace(lot.SourceLabel))
                            problems.Add($"household {householdId}: lot {lot.LotId} has no provenance.");
                        if (lot.QuantityUnits < 0)
                            problems.Add($"household {householdId}: lot {lot.LotId} has negative units.");
                    }
                }
            }

            if (ctx.Obligations != null)
            {
                foreach (FinancialObligation obligation in ctx.Obligations.Obligations)
                {
                    if (obligation == null || obligation.Settled) continue;
                    if (string.IsNullOrWhiteSpace(obligation.Creditor))
                        problems.Add($"obligation {obligation.ObligationId}: no creditor.");
                    if (obligation.OutstandingPrincipalCents <= 0)
                        problems.Add($"obligation {obligation.ObligationId}: non-positive principal.");
                }
            }

            return problems;
        }
    }
}
