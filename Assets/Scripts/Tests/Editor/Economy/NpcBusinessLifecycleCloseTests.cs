using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// B1 (2026-11-17): sustained struggle closes an NPC business
    /// orderly through the existing CloseOrderly wind-down. Harness-
    /// compiled companion to PhaseGNpcBusinessLifeTests (which is
    /// harness-excluded: its fixture uses credit types the harness
    /// does not compile). This file uses only harness-compiled types
    /// and the production shape of a null borrow context — the
    /// production lifecycle driver passes no borrow context, so
    /// these tests exercise the close path exactly as production
    /// will.
    /// </summary>
    [TestFixture]
    public sealed class NpcBusinessLifecycleCloseTests
    {
        private sealed class FakeBusinessCashPort : INpcBusinessCashPort
        {
            private readonly Dictionary<string, int> balances = new Dictionary<string, int>();

            public void Seed(string businessInstanceId, int cents)
            {
                balances[businessInstanceId] = cents;
            }

            public int ReadCashCents(string businessInstanceId)
            {
                return balances.TryGetValue(businessInstanceId, out int cents) ? cents : 0;
            }

            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                if (amountCents <= 0) return "Spend: amount must be positive.";
                int cash = ReadCashCents(businessInstanceId);
                if (amountCents > cash)
                {
                    return $"Spend refused: {businessInstanceId} holds {cash}c, cannot spend {amountCents}c ({purpose}).";
                }

                balances[businessInstanceId] = cash - amountCents;
                return null;
            }

            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                if (amountCents <= 0) return "Receive: amount must be positive.";
                balances[businessInstanceId] = ReadCashCents(businessInstanceId) + amountCents;
                return null;
            }
        }

        private sealed class FakeLiquidationPort : INpcLiquidationPort
        {
            public int RecoveryBps = 5000; // 50% of formation cost
            public List<NpcLiquidationSale> Sales = new List<NpcLiquidationSale>();

            public string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
                out NpcLiquidationSale sale)
            {
                sale = null;
                if (asset == null) return "no asset.";
                sale = new NpcLiquidationSale
                {
                    AssetItemId = asset.ItemId,
                    Units = asset.Units,
                    BuyerName = "second-hand dealer",
                    ProceedsCents = asset.CostCents * RecoveryBps / 10000,
                };
                Sales.Add(sale);
                return null;
            }
        }

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public FakeBusinessCashPort Cash = new FakeBusinessCashPort();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public NpcFormationAssetRegister Assets = new NpcFormationAssetRegister();
            public FakeLiquidationPort Liquidation = new FakeLiquidationPort();
            public NpcBusinessEventLog Events = new NpcBusinessEventLog();
            public HouseholdLedger FounderLedger;
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                FounderLedger = new HouseholdLedger(7);
                Assert.IsNull(FounderLedger.RecordInflow(0, 50000, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
            }

            public NpcBusinessLifeRecord LifeRecord(string instanceId = "biz-1")
            {
                return new NpcBusinessLifeRecord
                {
                    BusinessInstanceId = instanceId,
                    BusinessEntityKey = "business:1",
                    FounderPersonId = 42,
                    OwnerName = "Marta Kessler",
                    State = NpcBusinessLifeState.Operating,
                    CapabilityIds = new List<string> { "bake-bread" },
                };
            }

            public NpcLifecycleContext Context(int day = 20)
            {
                return new NpcLifecycleContext
                {
                    Cash = Cash,
                    Obligations = Authority,
                    Employments = Employments,
                    FounderHouseholdLedger = FounderLedger,
                    AssetRegister = Assets,
                    Liquidation = Liquidation,
                    Events = Events,
                    Ids = Ids,
                    DayIndex = day,
                };
            }
        }

        [Test]
        public void Struggle_SevenWeeks_StaysStruggling_NotClosed()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 5000); // below the 20000 reserve

            for (int week = 0; week < 7; week++)
                service.EvaluateWeek(record, 20000, 0, f.Context(20 + week * 7), null, null, f.Diag);

            Assert.AreEqual(NpcBusinessLifeState.Struggling, record.State);
            Assert.AreEqual(7, record.ConsecutiveStruggleWeeks);
            Assert.AreEqual(0, f.Events.CountKind(NpcBusinessEventKind.CloseStarted));
            Assert.AreEqual(0, f.Events.CountKind(NpcBusinessEventKind.CloseComplete));
        }

        [Test]
        public void Struggle_EightWeeks_ClosesOrderly()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            // Below the 20000 reserve (so every week struggles) but above
            // the 10000 obligation (so the close-down can settle it in full).
            f.Cash.Seed("biz-1", 15000);

            // One real obligation on the shared authority.
            FinancialObligation obligation = f.Authority.Create(f.Ids, FinancialObligationKind.Loan,
                "business:biz-1", "Abel", 10000, 0, "12% annual", "seed loan");
            Assert.IsNotNull(obligation);
            record.OpenObligationIds.Add(obligation.ObligationId);

            // One formation asset with provenance + one hire.
            Assert.IsNull(f.Assets.Record(new NpcFormationAsset
                { AssetKind = "equipment", ItemId = "oven", Units = 1, CostCents = 15000, Provenance = "purchase:smith-12", OwnerBusinessId = "biz-1" }));
            var relationship = new EmploymentRelationship
            {
                Id = "emp-biz-1-43-baker", EmployeePersonId = 43, EmployerBusinessId = "biz-1",
                RoleDisplayName = "baker",
                Compensation = CompensationTerms.FromWeeklyWage(900, "test"),
                StartDayIndex = 10, LifecycleState = EmploymentLifecycleState.Active,
            };
            Assert.IsNull(f.Employments.Register(relationship));
            record.EmploymentIds.Add(relationship.Id);

            int founderBefore = f.FounderLedger.GetBalanceCents();
            for (int week = 0; week < 8; week++)
                service.EvaluateWeek(record, 20000, 0, f.Context(20 + week * 7), null, null, f.Diag);

            Assert.AreEqual(NpcBusinessLifeState.Closed, record.State);
            Assert.IsTrue(f.Authority.Find(obligation.ObligationId).Settled, "The obligation is settled on the authority.");
            Assert.AreEqual(1, f.Liquidation.Sales.Count, "The oven liquidated to a real buyer.");
            Assert.AreEqual(7500, f.Liquidation.Sales[0].ProceedsCents, "50% recovery on the 15000c oven.");
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.CloseStarted));
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.CloseComplete));
            Assert.AreEqual(1, f.Events.CountKind(NpcBusinessEventKind.StaffReleased));
            Assert.IsTrue(f.Employments.TryGetById(relationship.Id, out EmploymentRelationship after));
            Assert.AreEqual(EmploymentLifecycleState.Ended, after.LifecycleState);

            // Residual: 15000 - 10000 settled + 7500 liquidation = 12500 to the founder.
            Assert.AreEqual(founderBefore + 12500, f.FounderLedger.GetBalanceCents());
            Assert.AreEqual(0, f.Cash.ReadCashCents("biz-1"), "Nothing left behind in the business.");
        }

        [Test]
        public void Struggle_RecoversBeforeEightWeeks_DoesNotClose()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 5000); // below the 20000 reserve

            for (int week = 0; week < 7; week++)
                service.EvaluateWeek(record, 20000, 0, f.Context(20 + week * 7), null, null, f.Diag);
            Assert.AreEqual(7, record.ConsecutiveStruggleWeeks);

            // Two healthy weeks bring it back to Operating and reset the count.
            f.Cash.Seed("biz-1", 25000);
            service.EvaluateWeek(record, 20000, 0, f.Context(69), null, null, f.Diag);
            Assert.AreEqual(1, record.ConsecutiveHealthyWeeks);
            service.EvaluateWeek(record, 20000, 0, f.Context(76), null, null, f.Diag);
            Assert.AreEqual(NpcBusinessLifeState.Operating, record.State);
            Assert.AreEqual(0, record.ConsecutiveStruggleWeeks);

            // Seven more struggle weeks still do not close.
            f.Cash.Seed("biz-1", 5000);
            for (int week = 0; week < 7; week++)
                service.EvaluateWeek(record, 20000, 0, f.Context(83 + week * 7), null, null, f.Diag);
            Assert.AreEqual(NpcBusinessLifeState.Struggling, record.State);
            Assert.AreEqual(7, record.ConsecutiveStruggleWeeks);
            Assert.AreEqual(0, f.Events.CountKind(NpcBusinessEventKind.CloseComplete));
        }

        [Test]
        public void Close_AlreadyClosed_IsIdempotent()
        {
            var f = new Fixture();
            var service = new NpcBusinessLifecycleService();
            NpcBusinessLifeRecord record = f.LifeRecord();
            f.Cash.Seed("biz-1", 5000); // below the 20000 reserve

            for (int week = 0; week < 8; week++)
                service.EvaluateWeek(record, 20000, 0, f.Context(20 + week * 7), null, null, f.Diag);
            Assert.AreEqual(NpcBusinessLifeState.Closed, record.State);
            int closeCompleteCount = f.Events.CountKind(NpcBusinessEventKind.CloseComplete);
            Assert.AreEqual(1, closeCompleteCount);

            // A further evaluation changes nothing.
            service.EvaluateWeek(record, 20000, 0, f.Context(76), null, null, f.Diag);
            Assert.AreEqual(NpcBusinessLifeState.Closed, record.State);
            Assert.AreEqual(closeCompleteCount, f.Events.CountKind(NpcBusinessEventKind.CloseComplete));
        }
    }
}
