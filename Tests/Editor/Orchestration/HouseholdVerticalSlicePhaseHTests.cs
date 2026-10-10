using System.Collections.Generic;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Financing;
using LandLedgers.Orchestration.HouseholdSlice;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Phase H: the unified household economy vertical slice — the full
    /// end-to-end consumption trace, the three failure/recovery experiments,
    /// the eight secondary proofs, numeric reconciliation, and the save/load
    /// round-trip. Every beat runs through the production authorities from
    /// Phases B-G; the numbers below are the deterministic fixture
    /// configuration, not universal simulation rules.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdVerticalSlicePhaseHTests
    {
        private HouseholdVerticalSlice.UnifiedWorld Build()
        {
            var slice = new HouseholdVerticalSlice();
            return slice.BuildUnifiedWorld();
        }

        // ---------- H1: the fixture ----------

        [Test]
        public void UnifiedWorld_Builds_FiveHouseholds_EightPersons_RealDwellings()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();

            Assert.AreEqual(8, w.Population.people.Count);
            Assert.AreEqual(5, w.Population.households.Count);
            foreach (int pid in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                Assert.IsNotNull(w.Population.GetPerson(pid), $"P{pid} missing");
            }

            // Varied household cash, every inflow provenanced.
            Assert.AreEqual(40000, w.Ledgers.Get(HouseholdVerticalSlice.H_Morrow).GetBalanceCents());
            Assert.AreEqual(40000, w.Ledgers.Get(HouseholdVerticalSlice.H_Kovac).GetBalanceCents());
            Assert.AreEqual(40000, w.Ledgers.Get(HouseholdVerticalSlice.H_Renner).GetBalanceCents());
            Assert.AreEqual(200, w.Ledgers.Get(HouseholdVerticalSlice.H_Vane).GetBalanceCents());
            foreach (int hid in new[] { 100, 101, 102, 103, 104 })
            {
                foreach (HouseholdLedgerEntry entry in w.Ledgers.Get(hid).Entries)
                {
                    if (entry.IsInflow)
                    {
                        Assert.AreNotEqual(HouseholdIncomeSource.Unspecified, entry.Source);
                        Assert.IsNotEmpty(entry.Reason);
                    }
                }
            }

            // Real occupancies; Renner (P5) has none — the housing problem.
            Assert.AreEqual(0, w.Housing.CurrentOccupants(w.BoardingRoomASpaceId).Count); // room A empty at build
            Assert.AreEqual(0, w.Housing.CurrentOccupanciesForPerson(HouseholdVerticalSlice.P_Emil).Count);
            Assert.AreEqual(1, w.Housing.CurrentOccupanciesForPerson(HouseholdVerticalSlice.P_Tomas).Count);
            Assert.AreEqual(AccommodationArrangement.OwnerOccupied,
                w.Housing.CurrentOccupanciesForPerson(HouseholdVerticalSlice.P_Tomas)[0].Arrangement);

            // The store is on the real retail path with real finite stock.
            Assert.AreEqual(60, w.StorePort.CategoryStockUnits("staple_food"));
            Assert.AreEqual(120, w.StorePort.CategoryPricePerUnitCents("staple_food"));
            Assert.AreEqual(50000, w.StorePort.CurrentCashCents);
            Assert.AreEqual(500, w.Upstream.AvailableUnits("staple_food"));

            // The supply problem: Kovac's pantry is thin.
            Assert.AreEqual(30, w.Inventories.GetOrCreate(HouseholdVerticalSlice.H_Kovac)
                .GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        // ---------- H2: the mandatory end-to-end trace ----------

        [Test]
        public void EndToEndTrace_FullChain_ConsumptionToRepurchase_WithConservation()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.UnifiedTraceResult t = slice.RunUnifiedConsumptionTrace(w);

            // A household owns food -> members eat -> reserves decline.
            Assert.AreEqual(30, t.FlourDay0);
            Assert.AreEqual(6, t.FlourAfterDay3);
            Assert.AreEqual(12, t.MealsServedDays1To3);
            Assert.AreEqual(0, t.MealsMissedDays1To3);

            // A member recognizes the shortage -> a real need (need is not a sale; sequence 0 = first need).
            Assert.AreEqual(0, t.NeedSequence);
            Assert.AreEqual(50, t.NeedUnits);
            Assert.AreEqual(50, t.NeedUnits);

            // The member schedules a shopping trip -> visits the open seller ->
            // transaction -> correct money changes hands -> stock changes custody.
            Assert.IsTrue(t.TripSuccess);
            Assert.AreEqual(HouseholdVerticalSlice.P_Petr, t.ShopperPersonId);
            Assert.AreEqual(50, t.UnitsAcquired);
            Assert.AreEqual(6000, t.BuyerOutflowCents);
            Assert.AreEqual(6000, t.StoreCashInCents);
            Assert.AreEqual(0, t.StoreReceivableCents);
            Assert.AreEqual("Hart General Store", t.Counterparty);
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.ReceiptId));

            // Buyer out == store in, every cent; real stock moved.
            Assert.AreEqual(34000, w.Ledgers.Get(HouseholdVerticalSlice.H_Kovac).GetBalanceCents());
            Assert.AreEqual(56000, t.StoreCashAfterSale);
            Assert.AreEqual(10, t.StoreStockAfterSale);

            // The member returns home -> the household receives the goods.
            Assert.AreEqual(56, t.HouseholdFlourAfterReceipt);
            Assert.AreEqual("kovac-cottage", w.PersonLocations[HouseholdVerticalSlice.P_Petr]);

            // The household later consumes the purchased goods.
            // (With a full pantry the meal policy serves the midday slot too:
            // 16 meals over days 5-7, 32u flour consumed.)
            Assert.AreEqual(16, t.MealsServedDays5To7);
            Assert.AreEqual(24, t.HouseholdFlourAfterDay7);

            // The event trail covers the loop with ordered source event IDs.
            Assert.Greater(t.EventCount, 0);
            Assert.Greater(t.LastEventSequence, 0);

            // The narrative tells the story with real identities and numbers.
            Assert.Greater(t.Narrative.Count, 0);
            StringAssert.Contains("P3", t.Narrative[3]);
        }

        [Test]
        public void EndToEndTrace_ClosedStore_FailsThenRecovers()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.UnifiedTraceResult t = slice.RunUnifiedConsumptionTrace(w);
            HouseholdVerticalSlice.FailureExperimentResult f = t.ClosedStore;

            Assert.IsTrue(f.FailedAsExpected);
            StringAssert.Contains("closed", f.FailureReason);
            Assert.IsTrue(f.NeedStayedOpen);
            Assert.AreEqual(40000, f.BuyerBalanceAfterFailure);
            Assert.AreEqual(56000, f.StoreCashAfterFailure);
            Assert.AreEqual(10, f.StoreStockAfterFailure);

            Assert.IsTrue(f.Recovered);
            Assert.AreEqual(1200, f.BuyerOutflowOnRecovery);
            Assert.AreEqual(10, f.UnitsAcquiredOnRecovery);
            Assert.IsFalse(string.IsNullOrWhiteSpace(f.RecoveryReceiptId));
            Assert.AreEqual(38800, w.Ledgers.Get(HouseholdVerticalSlice.H_Morrow).GetBalanceCents());
        }

        [Test]
        public void EndToEndTrace_Stockout_FailsThenRestocksAndRecovers()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.UnifiedTraceResult t = slice.RunUnifiedConsumptionTrace(w);
            HouseholdVerticalSlice.FailureExperimentResult f = t.Stockout;

            Assert.IsTrue(f.FailedAsExpected);
            Assert.IsTrue(f.NeedStayedOpen);
            Assert.AreEqual(40000, f.BuyerBalanceAfterFailure);
            Assert.AreEqual(0, f.StoreStockAfterFailure);

            Assert.IsTrue(f.Recovered);
            Assert.AreEqual(1200, f.BuyerOutflowOnRecovery);
            Assert.AreEqual(10, f.UnitsAcquiredOnRecovery);
            // The procurement chain really refilled the shelf (50u right after the recovery sale).
            Assert.AreEqual(50, f.StoreStockAfterRecovery);
            Assert.AreEqual(440, w.Upstream.AvailableUnits("staple_food"));
        }

        [Test]
        public void EndToEndTrace_InsufficientFunds_FailsThenRecovers()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.UnifiedTraceResult t = slice.RunUnifiedConsumptionTrace(w);
            HouseholdVerticalSlice.FailureExperimentResult f = t.InsufficientFunds;

            Assert.IsTrue(f.FailedAsExpected);
            StringAssert.Contains("unaffordable", f.FailureReason);
            Assert.IsTrue(f.NeedStayedOpen);
            Assert.AreEqual(200, f.BuyerBalanceAfterFailure);
            Assert.AreEqual(0, f.HouseholdFlourAfterFailure);

            Assert.IsTrue(f.Recovered);
            Assert.AreEqual(1200, f.BuyerOutflowOnRecovery);
            Assert.AreEqual(10, f.UnitsAcquiredOnRecovery);
            Assert.AreEqual(1000, w.Ledgers.Get(HouseholdVerticalSlice.H_Vane).GetBalanceCents());
            Assert.AreEqual(10, w.Inventories.GetOrCreate(HouseholdVerticalSlice.H_Vane)
                .GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        // ---------- H3: the eight secondary proofs ----------

        [Test]
        public void Secondary_Hardship_MissedMeals_NoInvisibleReplenishment()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.HardshipProofResult p = proofs.Hardship;

            Assert.AreEqual(0, p.MealsServed);
            Assert.AreEqual(4, p.MealsMissed);
            Assert.AreEqual(4, p.MissedMealRecords);
            Assert.AreEqual(200, p.BalanceCents);
            Assert.AreEqual(0, p.FlourUnits);
            Assert.AreEqual(1, p.InflowEntryCount);
            Assert.IsTrue(p.NeedOpenAfterFailedTrip);
            StringAssert.Contains("unaffordable", p.TripFailureReason);
        }

        [Test]
        public void Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.RestockProofResult p = proofs.Restock;

            Assert.AreEqual(60, p.StockBefore);
            Assert.IsTrue(p.TripFailedOnEmptyShelf);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.OrderId));
            Assert.AreEqual(60, p.OrderUnits);
            Assert.AreEqual(80, p.OrderUnitCostCents);
            Assert.AreEqual(500, p.UpstreamStockBefore);
            Assert.AreEqual(440, p.UpstreamStockAfter);
            Assert.AreEqual(p.StoreCashBeforeRestock - 4800, p.StoreCashAfterRestock);
            Assert.AreEqual(60, p.StockAfterReceipt);
            Assert.IsTrue(p.TripSucceededAfterRestock);
            Assert.AreEqual(1200, p.BuyerOutflowCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.ReceiptId));
        }

        [Test]
        public void Secondary_HousingSearch_Agreement_ThenOccupancy()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.HousingSearchProofResult p = proofs.HousingSearch;

            Assert.IsTrue(p.DecisionResolved);
            Assert.AreEqual(HousingSearchPathKind.Rental, p.ChosenPath);
            Assert.IsTrue(p.Executed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.AgreementId));
            Assert.AreEqual(1, p.OccupancyCount);
            Assert.AreEqual(AccommodationArrangement.Rental, p.Arrangement);
        }

        [Test]
        public void Secondary_Rental_Dues_Collected_OccupancyMaintained()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.RentalProofResult p = proofs.Rental;

            Assert.AreEqual(1, p.DuesCount);
            Assert.AreEqual(800, p.CentsDue);
            Assert.AreEqual(39200, p.TenantBalanceAfter);
            Assert.AreEqual(10800, p.BoardingCashAfter);
            Assert.AreEqual(0, p.OpenArrearsCount);
            Assert.IsTrue(p.OccupancyMaintained);
        }

        [Test]
        public void Secondary_SellerNote_Negotiation_Closing_Title_Obligation()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.SellerNoteProofResult p = proofs.SellerNote;

            Assert.IsTrue(p.Closed);
            Assert.AreEqual(20000, p.CashToSellerCents);
            Assert.AreEqual(40000, p.FinancedCents);
            Assert.AreEqual(60000, p.CashToSellerCents + p.FinancedCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.ObligationId));
            Assert.IsTrue(p.TitleTransferred);
            Assert.AreEqual("household:100", p.TitleHolder);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.SecurityInterestId));
            // Morrow spent 1200c in the restock proof (runs before this), so 38800 - 20000 = 18800.
            Assert.AreEqual(18800, p.BuyerBalanceAfter);
            Assert.AreEqual(170000, p.SellerPurseAfter);

            FinancialObligation obligation = w.Obligations.Find(p.ObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(FinancialObligationKind.SellerFinance, obligation.Kind);
            Assert.AreEqual("household:100", obligation.Debtor);
            Assert.AreEqual("Abel Hart", obligation.Creditor);
            Assert.AreEqual(40000, obligation.OutstandingPrincipalCents);
        }

        [Test]
        public void Secondary_NpcLoan_Request_Evaluation_Advance_Repayment()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.NpcLoanProofResult p = proofs.NpcLoan;

            Assert.IsTrue(p.RequestFiled);
            Assert.IsTrue(p.OfferMade);
            Assert.IsTrue(p.Closed);
            Assert.IsTrue(p.CashConserved);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.ObligationId));
            Assert.Greater(p.PaymentsCollected, 0);
            Assert.AreEqual(0, p.PaymentsMissed);
            Assert.AreEqual(FinancialObligationStatus.Satisfied, p.FinalStatus);
            Assert.AreEqual(0, p.FinalOutstandingCents);
            Assert.AreEqual(150000, p.LenderFundsAvailableAfter);
        }

        [Test]
        public void Secondary_Construction_Need_Project_Materials_Labor_Home_Occupancy()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.ConstructionProofResult p = proofs.Construction;

            Assert.IsTrue(p.Commissioned);
            Assert.AreEqual(26000, p.TotalEstimatedCostCents);
            Assert.AreEqual(26000, p.MaterialCostPaidCents);
            // Renner paid 800c rent in the rental proof (runs before this): 40000 - 800 - 26000 = 13200.
            Assert.AreEqual(13200, p.HouseholdBalanceAfter);
            Assert.AreEqual(31000, p.YardCashAfter);
            Assert.AreEqual(960, p.FieldstoneLeft);
            Assert.AreEqual(9400, p.LumberLeft);
            Assert.IsTrue(p.ProjectComplete);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.BuildingId));
            Assert.IsTrue(p.OccupancyRecorded);
            Assert.AreEqual(AccommodationArrangement.OwnerOccupied, p.OccupancyArrangement);
        }

        [Test]
        public void Secondary_BusinessOpportunity_Observation_Investigation_Formation_FirstSale()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            HouseholdVerticalSlice.SecondaryProofsResult proofs = slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.BusinessOpportunityProofResult p = proofs.BusinessOpportunity;

            Assert.AreEqual(3, p.ObservationsRecorded);
            Assert.AreEqual(1, p.OpportunitiesRecognized);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.OpportunityId));
            Assert.IsTrue(p.InvestigationStarted);
            Assert.AreEqual(NpcInvestigationVerdict.Proceed, p.InvestigationVerdict);
            Assert.IsTrue(p.FormationSucceeded);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.BusinessInstanceId));
            Assert.AreEqual(30000, p.HouseholdCashOutCents);
            Assert.IsTrue(p.FirstSaleSucceeded);
            Assert.AreEqual(10, p.FirstSaleUnits);
            Assert.AreEqual(1500, p.FirstSaleRevenueCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(p.FirstSaleReceiptId));
            Assert.AreEqual(1500, p.BakeryPurseAfter);
            Assert.AreEqual(90, p.BreadStockAfter);
            Assert.AreEqual(10, p.BuyerBreadUnits);
        }

        // ---------- H4: numeric reconciliation ----------

        [Test]
        public void Reconciliation_EveryCent_EveryLot_EveryObligation()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            slice.RunUnifiedConsumptionTrace(w);
            slice.RunSecondaryProofs(w);
            HouseholdVerticalSlice.ReconciliationResult r = slice.RunUnifiedReconciliation(w);

            Assert.Greater(r.CheckCount, 15);
            foreach (string check in r.Checks)
            {
                StringAssert.StartsWith("PASS: ", check);
            }
        }

        // ---------- save/load ----------

        [Test]
        public void SaveLoad_RoundTrip_NoDuplication()
        {
            var slice = new HouseholdVerticalSlice();
            HouseholdVerticalSlice.UnifiedWorld w = slice.BuildUnifiedWorld();
            slice.RunUnifiedConsumptionTrace(w);
            slice.RunSecondaryProofs(w);

            List<string> report = slice.RunUnifiedSaveLoad(w);
            string joined = string.Join(" | ", report);
            Assert.IsTrue(joined.Contains("people stable: True"), joined);
            Assert.IsTrue(joined.Contains("ledgers stable: True"), joined);
            Assert.IsTrue(joined.Contains("inventories stable: True"), joined);
            Assert.IsTrue(joined.Contains("meals stable: True"), joined);
            Assert.IsTrue(joined.Contains("needs stable: True"), joined);
            Assert.IsTrue(joined.Contains("obligations stable: True"), joined);
            Assert.IsTrue(joined.Contains("titles stable: True"), joined);
            Assert.IsTrue(joined.Contains("housing stable: True"), joined);
            Assert.IsTrue(joined.Contains("observations stable: True"), joined);
            Assert.IsTrue(joined.Contains("formation assets stable: True"), joined);
            Assert.IsTrue(joined.Contains("stock stable: True"), joined);
            Assert.IsTrue(joined.Contains("purses stable: True"), joined);
            Assert.IsTrue(joined.Contains("no duplication on re-import: True"), joined);
            Assert.IsTrue(joined.Contains("sequences continue: True"), joined);
        }
    }
}
