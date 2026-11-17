using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// B3 (universal business campaign 2026-11-17): the close-to-release-to-relist
    /// positive path (F-B2-1). B1 built the 8-week close; B2 built the lease
    /// authority and the close callback, but B2's tree had no B1 close, so its
    /// lifecycle test could only pin the negative guard (the hook never fires
    /// on a struggle week). With B1 merged, the full chain is pinned here
    /// through the real weekly driver: 8 struggle weeks close the business
    /// through CloseOrderly, the close hook fires, the extracted
    /// CommercialLeaseCloseHandler releases the premises, ends the lease,
    /// refunds the deposit (only when no rent Payable is unsettled), and
    /// re-lists the building with the founder as landlord, offerable.
    /// The adapter's id resolution (info.Business.AssignedBuildingId and the
    /// runtime plot lookup) lives in harness-excluded SimulationDrivers and
    /// is Codex-verified; the tests resolve the building id from a real
    /// BusinessInstanceState the same way the adapter does.
    /// All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class CommercialLeaseCloseHandlerTests
    {
        private const int FounderPersonId = 42;
        private const int FounderHouseholdId = 7;
        private const int LandlordPersonId = 9;
        private const int LandlordHouseholdId = 9;
        private const int ShopBuildingId = 5;
        private const int ShopPlotId = 3;
        private const string BusinessId = "biz-test";
        private const int DepositCents = 1500;

        private sealed class FakeCashPort : INpcBusinessCashPort
        {
            public Dictionary<string, int> Balances = new Dictionary<string, int>();

            public int ReadCashCents(string businessInstanceId)
            {
                return Balances.TryGetValue(businessInstanceId, out int balance) ? balance : 0;
            }

            public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
            {
                if (amountCents <= 0) return "spend must be positive.";
                int balance = ReadCashCents(businessInstanceId);
                if (amountCents > balance) return $"insufficient business cash ({balance}c for {amountCents}c).";
                Balances[businessInstanceId] = balance - amountCents;
                return null;
            }

            public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
            {
                if (amountCents <= 0) return "receive must be positive.";
                Balances[businessInstanceId] = ReadCashCents(businessInstanceId) + amountCents;
                return null;
            }
        }

        private sealed class FakeLiquidationPort : INpcLiquidationPort
        {
            public string Liquidate(string businessInstanceId, NpcFormationAsset asset, int dayIndex,
                out NpcLiquidationSale sale)
            {
                sale = null;
                return null;
            }
        }

        /// <summary>Fake for SharedBusinessRuntimeManager.ReleaseNpcBusinessPremises.</summary>
        private sealed class FakePremisesRelease
        {
            private readonly HashSet<int> assigned = new HashSet<int>();

            public void Assign(int buildingId) { assigned.Add(buildingId); }
            public void Unassign(int buildingId) { assigned.Remove(buildingId); }
            public bool IsAssigned(int buildingId) => assigned.Contains(buildingId);

            public string Release(int buildingId)
            {
                if (!assigned.Contains(buildingId))
                    return $"ReleaseNpcBusinessPremises: building {buildingId} was not assigned — nothing to release.";
                assigned.Remove(buildingId);
                return null;
            }
        }

        private sealed class CloseFixture
        {
            public CommercialLeaseMarket Market = new CommercialLeaseMarket();
            public CommercialLeaseAgreement Agreement;
            public FakeCashPort Cash = new FakeCashPort();
            public FakePremisesRelease Release = new FakePremisesRelease();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public HouseholdLedger FounderLedger = new HouseholdLedger(FounderHouseholdId);
            public HouseholdLedger LandlordLedger = new HouseholdLedger(LandlordHouseholdId);
            public CommercialLeaseCloseHandler Handler = new CommercialLeaseCloseHandler();
            public NpcBusinessLifecycleDriver Driver = new NpcBusinessLifecycleDriver();
            public NpcBusinessLifeRecord Record;
            public NpcBusinessLifecycleDriver.LifecycleDriverContext Context;
            public List<string> Diag = new List<string>();
            public int HookCalls;
            public bool LandlordLedgerMissing;

            public CloseFixture()
            {
                Record = new NpcBusinessLifeRecord
                {
                    BusinessInstanceId = BusinessId,
                    BusinessEntityKey = "business:" + BusinessId,
                    FounderPersonId = FounderPersonId,
                    OwnerName = "Test Founder",
                    State = NpcBusinessLifeState.Operating,
                    ConsecutiveStruggleWeeks = 0,
                };
            }

            /// <summary>
            /// Leases shop building 5 to the founder's business (deposit 1500c,
            /// first month prepaid, from day 0). The deposit already sits in
            /// the landlord's ledger — it was paid at formation, pinned by
            /// B2's Driver_FormsBusinessThroughLease test; seeded here with
            /// the lease's own receipt source.
            /// </summary>
            public void LeaseShop(int startDay, int businessCash)
            {
                CommercialLeaseListing listing = Market.ListBuilding(ShopBuildingId, ShopPlotId,
                    LandlordPersonId, "Landlord Name", startDay, new List<string>());
                Agreement = Market.ActivateLease(listing, FounderPersonId, FounderHouseholdId,
                    BusinessId, DepositCents, firstRentPeriodPrepaid: true, startDay, new List<string>());
                Assert.IsNotNull(Agreement);
                Assert.IsNull(LandlordLedger.RecordInflow(startDay, DepositCents,
                    HouseholdIncomeSource.CommercialRentReceived, Agreement.AgreementId,
                    "lease deposit held for " + BusinessId, "person:" + FounderPersonId));
                Assert.IsNull(FounderLedger.RecordInflow(startDay, 10000,
                    HouseholdIncomeSource.OwnerContribution, "test-seed", "test seed capital", "test"));
                Cash.Balances[BusinessId] = businessCash;
                Release.Assign(ShopBuildingId);
            }

            /// <summary>Owner-held shop: no lease, the founder holds the building outright.</summary>
            public void OwnShop(int businessCash)
            {
                Assert.IsNull(FounderLedger.RecordInflow(0, 10000,
                    HouseholdIncomeSource.OwnerContribution, "test-seed", "test seed capital", "test"));
                Cash.Balances[BusinessId] = businessCash;
                Release.Assign(ShopBuildingId);
            }

            public BusinessInstanceState ShopBusiness()
            {
                var business = new BusinessInstanceState();
                Assert.IsTrue(business.TryAssignPremises(ShopBuildingId));
                return business;
            }

            public void BindHook(BusinessInstanceState business)
            {
                var info = new NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo
                {
                    BusinessInstanceId = BusinessId,
                    BusinessType = BusinessType.GeneralStore,
                    OwnerPersonId = FounderPersonId,
                    OwnerName = "Test Founder",
                    Business = business,
                };
                Context = new NpcBusinessLifecycleDriver.LifecycleDriverContext
                {
                    LifeRecords = new List<NpcBusinessLifeRecord> { Record },
                    NpcBusinesses = new List<NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo> { info },
                    Cash = Cash,
                    Obligations = Obligations,
                    Employments = new EmploymentRelationshipRegistry(),
                    FounderLedgerByPersonId = personId => personId == FounderPersonId ? FounderLedger : null,
                    AssetRegister = new NpcFormationAssetRegister(),
                    Liquidation = new FakeLiquidationPort(),
                    Events = new NpcBusinessEventLog(),
                    Ids = new EntityIdRegistry(),
                    DayIndex = 0,
                    OnBusinessClosed = (closedInfo, closedRecord) =>
                    {
                        HookCalls++;
                        // Resolves the building id exactly like the
                        // SimulationDrivers adapter: from the live instance.
                        int buildingId = closedInfo.Business != null
                            ? closedInfo.Business.AssignedBuildingId : -1;
                        Handler.HandleClosedBusiness(closedInfo.BusinessInstanceId, buildingId, ShopPlotId,
                            closedRecord.FounderPersonId, closedRecord.OwnerName,
                            Ports(), closedRecord.LastEvaluatedDayIndex, Diag);
                    },
                };
            }

            public CommercialLeaseCloseHandler.ClosePorts Ports()
            {
                return new CommercialLeaseCloseHandler.ClosePorts
                {
                    Market = Market,
                    ReleasePremises = Release.Release,
                    LedgerByPersonId = personId =>
                        LandlordLedgerMissing || personId != LandlordPersonId ? null : LandlordLedger,
                    LedgerByHouseholdId = householdId =>
                        householdId == FounderHouseholdId ? FounderLedger
                        : householdId == LandlordHouseholdId ? LandlordLedger : null,
                    Obligations = Obligations,
                };
            }

            public void DriveWeeks(int weeks, int firstDay)
            {
                for (int week = 0; week < weeks; week++)
                {
                    Context.DayIndex = firstDay + week * 7;
                    Driver.EvaluateWeek(Context, new List<string>());
                }
            }

            public bool DiagContains(string fragment)
            {
                foreach (string line in Diag)
                    if (line.Contains(fragment)) return true;
                return false;
            }
        }

        [Test]
        public void Close_LeasedBusiness_ReleasesEndsLease_RefundsDeposit_RelistsOfferable()
        {
            var f = new CloseFixture();
            f.LeaseShop(startDay: 0, businessCash: 5000);
            f.BindHook(f.ShopBusiness());

            f.DriveWeeks(8, firstDay: 20);

            Assert.AreEqual(1, f.HookCalls, "The close hook fires exactly once — on the 8th struggle week.");
            Assert.AreEqual(NpcBusinessLifeState.Closed, f.Record.State);
            Assert.AreEqual(69, f.Record.LastEvaluatedDayIndex, "Close day is week 8: day 20 + 7*7.");
            Assert.IsFalse(f.Release.IsAssigned(ShopBuildingId), "The premises is released back to the leasable pool.");
            Assert.AreEqual(CommercialLeaseAgreementStatus.Ended, f.Agreement.Status);
            Assert.AreEqual(69, f.Agreement.EndDayIndex, "The lease ends on the close day.");
            Assert.AreEqual(0, f.LandlordLedger.GetBalanceCents(), "The deposit leaves the landlord's household.");
            Assert.AreEqual(11500, f.FounderLedger.GetBalanceCents(),
                "Seed 10000c + the 1500c deposit refund; the close residual was 0c (the till was drained by customer-seeking).");
            Assert.AreEqual(0, f.Cash.ReadCashCents(BusinessId));
            CommercialLeaseListing listing = f.Market.Listings[0];
            Assert.AreEqual(CommercialLeaseListingStatus.Active, listing.Status);
            Assert.AreEqual(LandlordPersonId, listing.LandlordPersonId, "The same landlord holds the re-listing.");
            Assert.IsTrue(listing.IsOfferable, "The re-listed shop can be offered again.");
            Assert.AreEqual(3, listing.IncludedFixtures.Count, "The fitted fixtures stay with the building.");
            Assert.IsTrue(f.DiagContains("refunded to H7"), "Deposit refund logged: " + string.Join(" | ", f.Diag));
        }

        [Test]
        public void Close_OwnerHeldShop_RelistsWithFounderAsLandlord()
        {
            var f = new CloseFixture();
            f.OwnShop(businessCash: 5000);
            f.BindHook(f.ShopBusiness());

            f.DriveWeeks(8, firstDay: 20);

            Assert.AreEqual(1, f.HookCalls);
            Assert.AreEqual(NpcBusinessLifeState.Closed, f.Record.State);
            Assert.AreEqual(0, f.Market.Agreements.Count, "No lease existed, none is ended.");
            Assert.AreEqual(1, f.Market.Listings.Count);
            CommercialLeaseListing listing = f.Market.Listings[0];
            Assert.AreEqual(CommercialLeaseListingStatus.Active, listing.Status);
            Assert.AreEqual(FounderPersonId, listing.LandlordPersonId,
                "The closing founder becomes the landlord of the re-listed shop.");
            Assert.AreEqual("person:" + FounderPersonId, listing.LandlordHolderId);
            Assert.IsTrue(listing.IsOfferable, "The founder's re-listed shop can be offered.");
            Assert.AreEqual(3, listing.IncludedFixtures.Count);
            Assert.AreEqual(10000, f.FounderLedger.GetBalanceCents(), "No refund moves — there was no lease and no deposit.");
            Assert.IsFalse(f.Release.IsAssigned(ShopBuildingId));
        }

        [Test]
        public void Close_UnsettledRentPayable_HoldsDeposit_LeaseStillEnds()
        {
            var f = new CloseFixture();
            f.LeaseShop(startDay: 0, businessCash: 0);
            f.BindHook(f.ShopBusiness());

            // Book the arrears through the real settlement: period 1 falls due
            // on day 30 and the till is empty, so the full 1500c becomes a
            // real Payable naming the lease.
            CommercialLeaseRentSettlementResult settled = new CommercialLeaseRentSettlement().SettleDue(
                f.Market, f.Cash, personId => f.LandlordLedger, f.Obligations, new EntityIdRegistry(), 30, new List<string>());
            Assert.AreEqual(1, settled.PeriodsInArrears);
            Assert.AreEqual(1, f.Agreement.RentPayableIds.Count);

            f.DriveWeeks(8, firstDay: 31);

            Assert.AreEqual(1, f.HookCalls);
            Assert.AreEqual(NpcBusinessLifeState.Closed, f.Record.State);
            Assert.AreEqual(CommercialLeaseAgreementStatus.Ended, f.Agreement.Status,
                "The lease ends even though the deposit is held.");
            Assert.AreEqual(80, f.Agreement.EndDayIndex, "Close day is week 8: day 31 + 7*7.");
            Assert.AreEqual(1500, f.LandlordLedger.GetBalanceCents(),
                "The deposit is HELD — it does not move while arrears stand.");
            Assert.AreEqual(10000, f.FounderLedger.GetBalanceCents(), "No refund reaches the founder.");
            CommercialLeaseListing listing = f.Market.Listings[0];
            Assert.AreEqual(CommercialLeaseListingStatus.Active, listing.Status,
                "The shop re-lists even though the deposit is held.");
            Assert.IsTrue(listing.IsOfferable);
            Assert.IsTrue(f.DiagContains("HELD"), "Deposit held, logged loudly: " + string.Join(" | ", f.Diag));
        }

        [Test]
        public void Close_ReleaseRefused_LeaseNotEnded_LoggedLoudly()
        {
            var f = new CloseFixture();
            f.LeaseShop(startDay: 0, businessCash: 5000);
            // The premises was never assigned in the release port: the release refuses.
            f.Release.Unassign(ShopBuildingId);
            f.BindHook(f.ShopBusiness());

            f.DriveWeeks(8, firstDay: 20);

            Assert.AreEqual(1, f.HookCalls);
            Assert.AreEqual(NpcBusinessLifeState.Closed, f.Record.State,
                "The business still closes — the release failure is downstream of the close.");
            Assert.AreEqual(CommercialLeaseAgreementStatus.Active, f.Agreement.Status,
                "A refused release never ends the lease silently.");
            Assert.AreEqual(CommercialLeaseListingStatus.Leased, f.Market.Listings[0].Status);
            Assert.AreEqual(1500, f.LandlordLedger.GetBalanceCents(), "No refund moves when the release fails.");
            Assert.AreEqual(10000, f.FounderLedger.GetBalanceCents());
            Assert.IsTrue(f.DiagContains("was not assigned"), "Refusal logged loudly: " + string.Join(" | ", f.Diag));
        }

        [Test]
        public void Close_NoAssignedBuilding_NothingReleased_LeaseUntouched()
        {
            var f = new CloseFixture();
            f.LeaseShop(startDay: 0, businessCash: 5000);
            // No live instance: the adapter resolves building id -1.
            f.BindHook(null);

            f.DriveWeeks(8, firstDay: 20);

            Assert.AreEqual(1, f.HookCalls);
            Assert.AreEqual(NpcBusinessLifeState.Closed, f.Record.State);
            Assert.AreEqual(CommercialLeaseAgreementStatus.Active, f.Agreement.Status,
                "With no building to release, the lease is untouched.");
            Assert.AreEqual(CommercialLeaseListingStatus.Leased, f.Market.Listings[0].Status);
            Assert.AreEqual(1500, f.LandlordLedger.GetBalanceCents());
            Assert.AreEqual(10000, f.FounderLedger.GetBalanceCents());
            Assert.IsTrue(f.DiagContains("no assigned building"), "Logged loudly: " + string.Join(" | ", f.Diag));
        }

        [Test]
        public void Close_MissingLandlordLedger_DepositNotRefunded_LeaseStillEnds()
        {
            var f = new CloseFixture();
            f.LeaseShop(startDay: 0, businessCash: 5000);
            f.BindHook(f.ShopBusiness());
            // The landlord's household ledger cannot be resolved (e.g. the
            // membership is missing): the refund cannot be delivered.
            f.LandlordLedgerMissing = true;

            f.DriveWeeks(8, firstDay: 20);

            Assert.AreEqual(1, f.HookCalls);
            Assert.AreEqual(CommercialLeaseAgreementStatus.Ended, f.Agreement.Status,
                "The lease ends even when the refund cannot be delivered.");
            Assert.AreEqual(CommercialLeaseListingStatus.Active, f.Market.Listings[0].Status);
            Assert.AreEqual(1500, f.LandlordLedger.GetBalanceCents(), "Nothing moves out of the landlord's ledger.");
            Assert.AreEqual(10000, f.FounderLedger.GetBalanceCents(), "No refund reaches the founder.");
            Assert.IsTrue(f.DiagContains("NOT refunded"), "Logged loudly: " + string.Join(" | ", f.Diag));
        }

        [Test]
        public void HandleClosedBusiness_MissingMarket_NeverReleasesSilently()
        {
            var diag = new List<string>();
            new CommercialLeaseCloseHandler().HandleClosedBusiness(
                BusinessId, ShopBuildingId, ShopPlotId, FounderPersonId, "Test Founder",
                null, 0, diag);

            Assert.AreEqual(1, diag.Count);
            Assert.IsTrue(diag[0].Contains("lease market is missing"), "Logged loudly: " + string.Join(" | ", diag));
            Assert.IsTrue(diag[0].Contains("NOT released"));
        }
    }
}
