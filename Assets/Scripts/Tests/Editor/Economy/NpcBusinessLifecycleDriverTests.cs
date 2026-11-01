using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// G-01: the weekly source-side driver for NPC business lifecycles.
    /// Sources a life record per live NPC-founded business, builds the
    /// lifecycle context, and evaluates with a per-business-type survival
    /// reserve — without modifying the lifecycle internals.
    /// </summary>
    [TestFixture]
    public sealed class NpcBusinessLifecycleDriverTests
    {
        private sealed class FakeBusinessCashPort : INpcBusinessCashPort
        {
            private readonly Dictionary<string, int> balances = new Dictionary<string, int>();

            public void Seed(string businessInstanceId, int cents) => balances[businessInstanceId] = cents;

            public int ReadCashCents(string businessInstanceId) =>
                balances.TryGetValue(businessInstanceId, out int cents) ? cents : 0;

            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                int cash = ReadCashCents(businessInstanceId);
                if (amountCents > cash) return "refused";
                balances[businessInstanceId] = cash - amountCents;
                return null;
            }

            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                balances[businessInstanceId] = ReadCashCents(businessInstanceId) + amountCents;
                return null;
            }
        }

        private sealed class Fixture
        {
            public readonly FakeBusinessCashPort Cash = new FakeBusinessCashPort();
            public readonly List<NpcBusinessLifeRecord> Records = new List<NpcBusinessLifeRecord>();
            public readonly List<NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo> Businesses =
                new List<NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo>();
            public readonly List<string> Diag = new List<string>();

            public void AddBusiness(string instanceId, BusinessType type, int ownerPersonId = 42)
            {
                Businesses.Add(new NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo
                {
                    BusinessInstanceId = instanceId,
                    BusinessType = type,
                    OwnerPersonId = ownerPersonId,
                    OwnerName = "Owner " + ownerPersonId,
                });
            }

            public NpcBusinessLifecycleDriver.LifecycleDriverContext Context(int day = 20)
            {
                return new NpcBusinessLifecycleDriver.LifecycleDriverContext
                {
                    LifeRecords = Records,
                    NpcBusinesses = Businesses,
                    Cash = Cash,
                    Events = new NpcBusinessEventLog(),
                    DayIndex = day,
                };
            }
        }

        [Test]
        public void EvaluateWeek_SourcesRecordForNewNpcBusiness()
        {
            var f = new Fixture();
            f.AddBusiness("biz-1", BusinessType.Bakery);
            f.Cash.Seed("biz-1", 20000);
            var driver = new NpcBusinessLifecycleDriver();

            driver.EvaluateWeek(f.Context(), f.Diag);

            Assert.AreEqual(1, f.Records.Count, "The driver sources a life record for the NPC business.");
            NpcBusinessLifeRecord record = f.Records[0];
            Assert.AreEqual("biz-1", record.BusinessInstanceId);
            Assert.AreEqual(42, record.FounderPersonId);
            Assert.AreEqual(NpcBusinessLifeState.Operating, record.State);
            Assert.AreEqual(20, record.LastEvaluatedDayIndex, "The record was evaluated this week.");
        }

        [Test]
        public void EvaluateWeek_HealthyBusiness_StaysOperating()
        {
            var f = new Fixture();
            f.AddBusiness("biz-1", BusinessType.Bakery); // default reserve 15000
            f.Cash.Seed("biz-1", 20000);
            var driver = new NpcBusinessLifecycleDriver();

            driver.EvaluateWeek(f.Context(), f.Diag);

            Assert.AreEqual(NpcBusinessLifeState.Operating, f.Records[0].State);
            Assert.AreEqual(1, f.Records[0].ConsecutiveHealthyWeeks);
        }

        [Test]
        public void EvaluateWeek_CashBelowReserve_MarksStruggling()
        {
            var f = new Fixture();
            f.AddBusiness("biz-1", BusinessType.Bakery); // default reserve 15000
            f.Cash.Seed("biz-1", 100);
            var driver = new NpcBusinessLifecycleDriver();

            driver.EvaluateWeek(f.Context(), f.Diag);

            Assert.AreEqual(NpcBusinessLifeState.Struggling, f.Records[0].State);
            Assert.AreEqual(1, f.Records[0].ConsecutiveStruggleWeeks);
        }

        [Test]
        public void EvaluateWeek_UsesCalibratedReserveResolver()
        {
            var f = new Fixture();
            f.AddBusiness("biz-1", BusinessType.Bakery);
            f.Cash.Seed("biz-1", 10000);
            var driver = new NpcBusinessLifecycleDriver();
            var ctx = f.Context();
            ctx.SurvivalReserveCents = info => 5000; // calibrated: healthy at 10000c

            driver.EvaluateWeek(ctx, f.Diag);

            Assert.AreEqual(NpcBusinessLifeState.Operating, f.Records[0].State,
                "The resolver's reserve (not the fallback table) decides.");

            var f2 = new Fixture();
            f2.AddBusiness("biz-1", BusinessType.Bakery);
            f2.Cash.Seed("biz-1", 10000);
            var ctx2 = f2.Context();
            ctx2.SurvivalReserveCents = info => 15000; // calibrated: struggling at 10000c
            driver.EvaluateWeek(ctx2, f2.Diag);

            Assert.AreEqual(NpcBusinessLifeState.Struggling, f2.Records[0].State);
        }

        [Test]
        public void EvaluateWeek_TerminalRecords_AreSkipped()
        {
            var f = new Fixture();
            f.AddBusiness("biz-1", BusinessType.Bakery);
            f.Records.Add(new NpcBusinessLifeRecord
            {
                BusinessInstanceId = "biz-1",
                State = NpcBusinessLifeState.Closed,
                LastEvaluatedDayIndex = 13,
            });
            f.Cash.Seed("biz-1", 20000);
            var driver = new NpcBusinessLifecycleDriver();

            driver.EvaluateWeek(f.Context(), f.Diag);

            Assert.AreEqual(1, f.Records.Count, "No duplicate record sourced.");
            Assert.AreEqual(13, f.Records[0].LastEvaluatedDayIndex, "Terminal records are not re-evaluated.");
        }

        [Test]
        public void EvaluateWeek_NullContext_DiagnosesWithoutThrowing()
        {
            var driver = new NpcBusinessLifecycleDriver();
            var diag = new List<string>();

            driver.EvaluateWeek(null, diag);

            Assert.IsNotEmpty(diag);
        }

        [Test]
        public void DefaultSurvivalReserveCents_IsSanePerType()
        {
            Assert.Greater(NpcBusinessLifecycleDriver.DefaultSurvivalReserveCents(BusinessType.Mine),
                NpcBusinessLifecycleDriver.DefaultSurvivalReserveCents(BusinessType.Barber),
                "Capital-intensive trades carry larger reserves than solo trades.");
            Assert.Greater(NpcBusinessLifecycleDriver.DefaultSurvivalReserveCents(BusinessType.LiveryFreight),
                NpcBusinessLifecycleDriver.DefaultSurvivalReserveCents(BusinessType.Doctor));
            foreach (BusinessType type in new[]
            {
                BusinessType.GeneralStore, BusinessType.Blacksmith, BusinessType.Ranch,
                BusinessType.Hotel, BusinessType.Bank, BusinessType.Generic,
            })
            {
                Assert.Greater(NpcBusinessLifecycleDriver.DefaultSurvivalReserveCents(type), 0);
            }

            Assert.AreEqual(750, NpcBusinessLifecycleDriver.CustomerSeekingSpendCents(15000),
                "Customer-seeking is a bounded 5% of the reserve.");
        }
    }
}
