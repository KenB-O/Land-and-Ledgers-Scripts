using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Orchestration.HouseholdSlice;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// HF-5: the household vertical slice — founding, work-taxonomy roles, provenanced
    /// economy, embodied consumption, mid-slice membership change, and save/load ID
    /// stability. Small enough to actually run.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdVerticalSliceTests
    {
        [Test]
        public void Slice_Founding_CreatesRealPlayerHousehold()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdState household = slice.RunFounding(0);

            Assert.IsNotNull(household);
            Assert.IsTrue(household.isPlayerHousehold);
            Assert.AreEqual(EntityKind.Household, household.entityId.Kind);
            Assert.AreEqual(household.id, household.entityId.Id);
            Assert.IsNotNull(slice.World.Population.GetHousehold(household.id));
            CollectionAssert.AreEquivalent(
                new[] { slice.FounderId, slice.SpouseId },
                slice.World.Lifecycle.GetMembers(household.id));
            Assert.AreEqual(2, slice.World.Animals.ActiveCount); // founding flock
        }

        [Test]
        public void Slice_WorkRoles_CoverOperator_FamilyLabor_And_WageEmployment()
        {
            var slice = new HouseholdVerticalSlice();
            slice.RunFounding(0);
            slice.RunWorkRoles(1);

            Assert.AreEqual(3, slice.World.WorkRelationships.Count);
            Assert.AreEqual(WorkRelationshipKind.FarmOrBusinessOperator, slice.World.WorkRelationships[0].Kind);
            Assert.AreEqual(WorkRelationshipKind.FamilyEnterpriseLabor, slice.World.WorkRelationships[1].Kind);
            Assert.AreEqual(WorkRelationshipKind.SeasonalOrCasualEmployment, slice.World.WorkRelationships[2].Kind);

            // Family labor tracked as time/output, never payroll.
            slice.World.FamilyLabor.GetTotals(slice.SpouseId, "B-farm-1", out int minutes, out int output);
            Assert.AreEqual(300, minutes);
            Assert.AreEqual(22, output);

            // The wage hand is employed through the EmploymentRelationship authority.
            Assert.IsTrue(slice.World.Employments.TryGetById(slice.EmploymentId, out EmploymentRelationship employment));
            Assert.AreEqual(slice.HiredHandId, employment.EmployeePersonId);
            Assert.IsTrue(slice.World.Employments.GetAgreedWeeklyWageCents(slice.EmploymentId, out int wage));
            Assert.AreEqual(900, wage);
            // The hired hand boards with the household: membership, not just employment.
            Assert.AreEqual(slice.PlayerHouseholdId, slice.World.Memberships.GetActiveHouseholdId(slice.HiredHandId));
        }

        [Test]
        public void Slice_Economy_EveryInflowProvenanced_ConsumptionEmbodied()
        {
            var slice = new HouseholdVerticalSlice();
            slice.RunFounding(0);
            slice.RunWorkRoles(1);
            slice.RunEconomy(7);

            HouseholdLedger ledger = slice.World.Ledgers.Get(slice.PlayerHouseholdId);
            Assert.IsNotNull(ledger);
            // 900 wage (via E-id) + 4500 owner draw - 1200 embodied staple purchase (10u @ 120c).
            Assert.AreEqual(4200, ledger.GetBalanceCents());
            foreach (HouseholdLedgerEntry entry in ledger.Entries)
            {
                if (entry.IsInflow)
                {
                    Assert.AreNotEqual(HouseholdIncomeSource.Unspecified, entry.Source);
                    Assert.IsNotEmpty(entry.Reason);
                }
            }
        }

        [Test]
        public void Slice_Birth_ChangesMembership_WithSeparateKinship()
        {
            var slice = new HouseholdVerticalSlice();
            slice.RunFounding(0);
            slice.RunWorkRoles(1);

            PersonState child = slice.RunBirth(60);

            Assert.IsNotNull(child);
            Assert.AreEqual(slice.PlayerHouseholdId, slice.World.Memberships.GetActiveHouseholdId(child.id));
            CollectionAssert.AreEquivalent(
                new[] { slice.SpouseId, slice.FounderId }, slice.World.Kinship.GetParents(child.id));
            Assert.AreEqual(4, slice.World.Lifecycle.GetMembers(slice.PlayerHouseholdId).Count);
        }

        [Test]
        public void Slice_SaveLoadRoundTrip_KeepsEveryIdStable()
        {
            var slice = new HouseholdVerticalSlice();
            slice.RunFounding(0);
            slice.RunWorkRoles(1);
            slice.RunEconomy(7);
            slice.RunBirth(60);

            List<string> report = slice.RunSaveLoadRoundTrip();

            string joined = string.Join(" | ", report);
            Assert.IsTrue(joined.Contains("people stable: True"), joined);
            Assert.IsTrue(joined.Contains("ledger stable: True"), joined);
            Assert.IsTrue(joined.Contains("animals stable: True"), joined);
            Assert.IsTrue(joined.Contains("sequences continue: True"), joined);
        }

        [Test]
        public void Slice_FullRun_ProducesBeatsReport()
        {
            var slice = new HouseholdVerticalSlice();
            string report = slice.RunAll();

            Assert.IsTrue(report.Contains("HOUSEHOLD VERTICAL SLICE"));
            Assert.GreaterOrEqual(slice.Beats.Count, 5);
        }

        [Test]
        public void ScriptedExecutor_NeverFakesASale_WithoutSupplier()
        {
            var slice = new HouseholdVerticalSlice();
            slice.RunFounding(0);

            var executor = new ScriptedPurchaseExecutor(
                slice.World.Ledgers, new Dictionary<string, int>());
            var need = new ProcurementNeed
            {
                NeedSequence = 0,
                HouseholdId = slice.PlayerHouseholdId,
                CategoryId = "unobtainium",
                UnitsNeeded = 5,
            };

            PurchaseExecutionResult result = executor.Execute(need, slice.FounderId, 1);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.UnitsAcquired);
        }
    }
}
