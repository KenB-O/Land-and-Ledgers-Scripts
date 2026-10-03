using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Saloon;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// T2G: the saloon runtime. Staffing scales from observed workload
    /// (GHOST-DES-050); free poker moves zero gaming revenue (Tech X §7.1);
    /// faro P&L never touches the house except through the concession payment
    /// (Tech X §7.2/7.3, §12.4).
    /// </summary>
    public sealed class SaloonRuntimeTests
    {
        [Test]
        public void StaffingScalesFromObservedWorkloadNotARecipe()
        {
            var saloon = new SaloonRuntime("biz-saloon");
            saloon.SetCurrentStaff(bartenders: 1, servers: 0);

            // A quiet week: ~10 patrons/day.
            for (int day = 0; day < 7; day++)
            {
                for (int i = 0; i < 10; i++) saloon.ServeDrink(day, null);
                saloon.ObserveLingering(4f);
                saloon.CloseDay(day, null);
            }
            var (b1, s1) = saloon.RecommendedStaff();
            Assert.AreEqual(1, b1, "Quiet week needs one bartender.");

            // A busy week: ~120 patrons/day.
            for (int day = 7; day < 14; day++)
            {
                for (int i = 0; i < 120; i++) saloon.ServeDrink(day, null);
                saloon.ObserveLingering(30f);
                saloon.CloseDay(day, null);
            }
            var (b2, s2) = saloon.RecommendedStaff(trailingDays: 7);
            Assert.Greater(b2, b1, "Observed workload — not a fixed recipe — drives staffing.");
            Assert.Greater(s2, s1);

            var (needB, needS) = saloon.StaffingShortfall();
            Assert.AreEqual(b2 - 1, needB, "Shortfall is against CURRENT staff — hire through T2B.");
        }

        [Test]
        public void NewSaloonOpensWithABartender()
        {
            var saloon = new SaloonRuntime("biz-saloon");
            var (b, s) = saloon.RecommendedStaff();
            Assert.AreEqual(1, b);
            Assert.AreEqual(0, s);
        }

        [Test]
        public void FreePokerMovesZeroGamingRevenue()
        {
            var table = new FreePokerTable();
            var diag = new List<string>();
            table.HostGame(playersSeated: 6, ordersPlaced: 9, diag);

            Assert.AreEqual(1, table.GamesHosted);
            Assert.AreEqual(9, table.PlayerOrders, "Players order food/drink — ordinary house transactions.");
            // There is no field, method, or path on FreePokerTable that can move
            // gaming money to the house: the separation is structural (Tech X §7.1).
        }

        [Test]
        public void FaroBankrollIsNotTheHouse()
        {
            var agreement = new FaroConcessionAgreement
            {
                AgreementId = "faro-1",
                SaloonBusinessInstanceId = "biz-saloon",
                ConcessionaireName = "Hank Mercer",
                ConcessionPaymentCents = 500,
                PaymentCadence = "weekly",
            };
            var bankroll = new FaroBankroll { BankrollId = "br-1", OwnerName = "Hank Mercer", BankrollCents = 10000 };
            var concession = new FaroConcession(agreement, bankroll);
            var diag = new List<string>();

            // Dealer wins big.
            int houseCut = concession.SettleSession(netToDealerCents: 3000, isCadenceDay: false, diag);
            Assert.AreEqual(0, houseCut, "Off-cadence: the house books nothing from the game.");
            Assert.AreEqual(13000, bankroll.BankrollCents, "The P&L lives on the dealer's bankroll.");

            // Cadence day: ONLY the concession payment flows to the house.
            houseCut = concession.SettleSession(netToDealerCents: -2000, isCadenceDay: true, diag);
            Assert.AreEqual(500, houseCut, "The concession payment is the ONLY house money from faro (Tech X §12.4).");
            Assert.AreEqual(11000, bankroll.BankrollCents);
        }

        [Test]
        public void TerminatedConcessionHostsNoGames()
        {
            var agreement = new FaroConcessionAgreement { AgreementId = "faro-1", ConcessionaireName = "Hank" };
            var bankroll = new FaroBankroll { BankrollId = "br-1", OwnerName = "Hank", BankrollCents = 10000 };
            var concession = new FaroConcession(agreement, bankroll);
            concession.Terminate("breach of cadence", new List<string>());

            int houseCut = concession.SettleSession(1000, true, new List<string>());
            Assert.AreEqual(0, houseCut);
            Assert.AreEqual(10000, bankroll.BankrollCents, "The bankroll remains the dealer's after termination.");
        }

        [Test]
        public void SaloonSaveLoadRoundTripsWorkload()
        {
            var saloon = new SaloonRuntime("biz-saloon");
            saloon.SetCurrentStaff(2, 1);
            for (int i = 0; i < 50; i++) saloon.ServeDrink(0, null);
            saloon.CloseDay(0, null);

            var dto = saloon.CaptureSaveDto();
            var restored = new SaloonRuntime("biz-saloon");
            restored.LoadFromSaveDto(dto);

            var (b, s) = restored.RecommendedStaff();
            Assert.AreEqual(2, b, "50 patrons/day → 2 bartenders (40 each).");
            var (needB, needS) = restored.StaffingShortfall();
            Assert.AreEqual(0, needB, "Current staff of 2 covers it.");
        }
    }
}
