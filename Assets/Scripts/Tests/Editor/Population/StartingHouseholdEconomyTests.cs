using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass V P1-5 (Worker 3 side): starting household economics are seeded
    /// coherently through the production authorities — employment with agreed
    /// wages, cash with documented sources, debts with named creditors and
    /// terms, pantry lots with provenance, lenders with documented capital.
    /// Bad specs are refused loudly; nothing is partially seeded.
    /// </summary>
    [TestFixture]
    public sealed class StartingHouseholdEconomyTests
    {
        private sealed class Fixture
        {
            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public readonly FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public readonly EntityIdRegistry Ids = new EntityIdRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public readonly HouseholdLenderDirectory Lenders = new HouseholdLenderDirectory();
            public readonly List<string> Diag = new List<string>();

            public StartingHouseholdEconomy.SeederContext Context()
            {
                return new StartingHouseholdEconomy.SeederContext
                {
                    Ledgers = Ledgers,
                    Employments = Employments,
                    Obligations = Obligations,
                    Ids = Ids,
                    Inventories = Inventories,
                    Lenders = Lenders,
                    HouseholdExists = hid => hid == 7 || hid == 8,
                    PersonExists = pid => pid == 71 || pid == 91,
                    BusinessExists = bid => bid == "mill-1",
                };
            }

            public StartingHouseholdEconomy.StartingHouseholdSpec GoodSpec()
            {
                return new StartingHouseholdEconomy.StartingHouseholdSpec
                {
                    HouseholdId = 7,
                    OpeningCashCents = 5000,
                    OpeningCashSource = "wages saved before the move west",
                    Employments = new List<StartingHouseholdEconomy.StartingEmploymentSpec>
                    {
                        new StartingHouseholdEconomy.StartingEmploymentSpec
                        {
                            PersonId = 71, BusinessId = "mill-1",
                            RoleDisplayName = "hand", WeeklyWageCents = 2100,
                        },
                    },
                    Debts = new List<StartingHouseholdEconomy.StartingDebtSpec>
                    {
                        new StartingHouseholdEconomy.StartingDebtSpec
                        {
                            Creditor = "land office", PrincipalCents = 12000,
                            Terms = "12 monthly installments", Purpose = "land claim purchase",
                        },
                    },
                    PantryLots = new List<StartingHouseholdEconomy.StartingPantryLotSpec>
                    {
                        new StartingHouseholdEconomy.StartingPantryLotSpec
                        {
                            ItemId = HouseholdItemCatalog.FlourId, Units = 20,
                            Source = "starting pantry (world-gen)",
                        },
                    },
                    Lender = new StartingHouseholdEconomy.StartingLenderSpec
                    {
                        PersonId = 91, LenderName = "Abel Hart",
                        CapitalCents = 20000, CapitalSource = "inheritance documented in the county will register",
                    },
                };
            }
        }

        [Test]
        public void Seed_GoodSpec_SeedsEverythingCoherently()
        {
            var f = new Fixture();
            int seeded = StartingHouseholdEconomy.Seed(
                new[] { f.GoodSpec() }, f.Context(), 0, f.Diag);

            Assert.AreEqual(1, seeded);

            // Cash with provenance.
            HouseholdLedger ledger = f.Ledgers.Get(7);
            Assert.IsNotNull(ledger);
            Assert.AreEqual(5000, ledger.GetBalanceCents());

            // Employment with agreed wages.
            List<EmploymentRelationship> active = f.Employments.GetActiveByEmployee(71);
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("mill-1", active[0].EmployerBusinessId);
            Assert.AreEqual(2100, active[0].Compensation.AgreedWeeklyWageCents);

            // Debt through the single debt authority.
            Assert.AreEqual(1, f.Obligations.Obligations.Count);
            foreach (FinancialObligation o in f.Obligations.Obligations)
            {
                Assert.AreEqual("household:7", o.Debtor);
                Assert.AreEqual("land office", o.Creditor);
                Assert.AreEqual(12000, o.OutstandingPrincipalCents);
                Assert.AreEqual(FinancialObligationKind.Loan, o.Kind);
            }

            // Pantry with provenance.
            HouseholdInventory inventory = f.Inventories.Get(7);
            Assert.IsNotNull(inventory);
            Assert.AreEqual(20, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId));

            // Lender registered with documented capital.
            Assert.IsTrue(f.Lenders.HasLender("Abel Hart"));

            // Coherence check passes.
            List<string> problems = StartingHouseholdEconomy.ValidateCoherence(new[] { 7 }, f.Context());
            Assert.AreEqual(0, problems.Count, string.Join(" | ", problems));
        }

        [Test]
        public void Seed_BadSpecs_AreRefusedLoudly_NothingPartial()
        {
            var f = new Fixture();

            // Sourceless cash.
            var noSource = f.GoodSpec();
            noSource.OpeningCashSource = string.Empty;
            // Unknown person employed.
            var unknownPerson = f.GoodSpec();
            unknownPerson.HouseholdId = 8;
            unknownPerson.Employments[0].PersonId = 999;
            // Debt without creditor.
            var noCreditor = f.GoodSpec();
            noCreditor.HouseholdId = 8;
            noCreditor.Debts[0].Creditor = string.Empty;
            // Pantry without provenance.
            var noProvenance = f.GoodSpec();
            noProvenance.HouseholdId = 8;
            noProvenance.PantryLots[0].Source = string.Empty;

            int seeded = StartingHouseholdEconomy.Seed(
                new[] { noSource, unknownPerson, noCreditor, noProvenance }, f.Context(), 0, f.Diag);

            Assert.AreEqual(0, seeded);
            Assert.AreEqual(4, f.Diag.Count - 1, "each refusal must be recorded: " + string.Join(" | ", f.Diag));
            // Nothing partially seeded for any refused household.
            Assert.IsNull(f.Ledgers.Get(7));
            Assert.IsNull(f.Ledgers.Get(8));
            Assert.AreEqual(0, f.Obligations.Obligations.Count);
            Assert.IsFalse(f.Lenders.HasLender("Abel Hart"));
        }

        [Test]
        public void Seed_ZeroCashHousehold_IsValid()
        {
            var f = new Fixture();
            var spec = f.GoodSpec();
            spec.OpeningCashCents = 0;
            spec.OpeningCashSource = string.Empty;

            int seeded = StartingHouseholdEconomy.Seed(new[] { spec }, f.Context(), 0, f.Diag);

            Assert.AreEqual(1, seeded);
            Assert.AreEqual(0, f.Ledgers.Get(7).GetBalanceCents());
        }

        [Test]
        public void ValidateCoherence_CatchesMissingLedger()
        {
            var f = new Fixture();
            List<string> problems = StartingHouseholdEconomy.ValidateCoherence(new[] { 7 }, f.Context());
            Assert.AreEqual(1, problems.Count);
            Assert.IsTrue(problems[0].Contains("no ledger"));
        }
    }
}
