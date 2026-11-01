using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Barber;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1B: chair rental to an independent barber operator (Canon §8.5 — the
    /// landlord leases a station/chair to an operator who keeps customer
    /// revenue and pays rent; §8.6 — the tenant remains an independent
    /// business). The runtime models the landlord side only: the leased chair
    /// leaves the shop's pool, and rent accrues as obligations the caller
    /// settles through ledger authorities.
    /// </summary>
    [TestFixture]
    public sealed class BarberChairLeaseTests
    {
        private static Func<string, WorkstationComponentView?> ChairFinder(params string[] goodAssetIds)
        {
            var good = new HashSet<string>(goodAssetIds);
            return assetId => good.Contains(assetId)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = "barber-chair",
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BarberShopRuntime TwoChairShop(List<string> diag)
        {
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            BarberConsumableBootstrap.ApplyBootstrapEndowment(runtime.ConsumableStock, registry, 290, diag);
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-2" }, diag));
            return runtime;
        }

        private static BarberChairLease Lease(int chairIndex, int operatorSeq, int startDay, int endDayExclusive)
        {
            return new BarberChairLease
            {
                ChairIndex = chairIndex,
                OperatorPersonId = EntityId.For(EntityKind.Person, operatorSeq),
                RentCentsPerDay = 50,
                StartDayIndex = startDay,
                EndDayIndexExclusive = endDayExclusive,
            };
        }

        [Test]
        public void LeaseChair_ValidatesInputs()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);

            Assert.NotNull(runtime.LeaseChair(null, diag), "Null lease refused.");
            Assert.NotNull(runtime.LeaseChair(Lease(5, 501, 300, 310), diag), "Nonexistent chair refused.");

            var noOperator = Lease(0, 501, 300, 310);
            noOperator.OperatorPersonId = EntityId.Invalid;
            Assert.NotNull(runtime.LeaseChair(noOperator, diag), "Anonymous tenancy refused.");

            var negativeRent = Lease(0, 501, 300, 310);
            negativeRent.RentCentsPerDay = -1;
            Assert.NotNull(runtime.LeaseChair(negativeRent, diag), "Negative rent refused.");

            Assert.NotNull(runtime.LeaseChair(Lease(0, 501, 310, 310), diag), "End day must be after start day.");
            Assert.NotNull(runtime.LeaseChair(Lease(0, 501, 312, 310), diag), "End day must be after start day.");

            Assert.AreEqual(0, runtime.Leases.Count, "Nothing invalid was recorded.");
        }

        [Test]
        public void LeaseChair_OverlappingLeaseRefused_AdjacentLeaseAccepted()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);

            Assert.Null(runtime.LeaseChair(Lease(0, 501, 300, 310), diag));
            Assert.NotNull(runtime.LeaseChair(Lease(0, 502, 305, 315), diag),
                "Overlapping leases on one chair are refused.");
            Assert.Null(runtime.LeaseChair(Lease(0, 502, 310, 320), diag),
                "An adjacent lease starting the day the old one ends is fine.");
            Assert.Null(runtime.LeaseChair(Lease(1, 503, 305, 315), diag),
                "A different chair is unaffected.");

            Assert.AreEqual(3, runtime.Leases.Count);
        }

        [Test]
        public void ActiveLease_CoversExactly_ItsDayRange()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.LeaseChair(Lease(0, 501, 300, 310), diag));

            Assert.Null(runtime.ActiveLease(0, 299), "Before the start day — the chair is the shop's.");
            Assert.NotNull(runtime.ActiveLease(0, 300), "Start day is inclusive.");
            Assert.NotNull(runtime.ActiveLease(0, 309), "Last covered day.");
            Assert.Null(runtime.ActiveLease(0, 310), "End day is exclusive.");
            Assert.Null(runtime.ActiveLease(1, 305), "The other chair is unaffected.");
        }

        [Test]
        public void CountReadyChairs_DayAware_ExcludesLeasedChairs()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.LeaseChair(Lease(0, 501, 300, 310), diag));
            var finder = ChairFinder("chair-asset-1", "chair-asset-2");

            Assert.AreEqual(1, runtime.CountReadyChairs(WorkstationCatalog.BarberChairStation, finder, 300, diag),
                "At day 300 the leased chair is the tenant's station, not the shop's.");
            Assert.AreEqual(2, runtime.CountReadyChairs(WorkstationCatalog.BarberChairStation, finder, 310, diag),
                "At day 310 the lease has ended — the chair is back.");
            Assert.AreEqual(2, runtime.CountReadyChairs(WorkstationCatalog.BarberChairStation, finder, diag),
                "The physical-readiness count ignores the lease calendar.");
        }

        [Test]
        public void ServeDay_LeasedChairNotScheduled_RentStillAccrues()
        {
            var diag = new List<string>();
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            BarberConsumableBootstrap.ApplyBootstrapEndowment(runtime.ConsumableStock, registry, 290, diag);
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));
            Assert.Null(runtime.LeaseChair(Lease(0, 501, 300, 310), diag));

            Assert.Null(runtime.QueueCustomer(new BarberCustomer
            {
                CustomerPersonId = EntityId.For(EntityKind.Person, 101),
                ServiceId = BarberServiceCatalog.ShaveId,
                RequestDayIndex = 300,
            }, diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1"), diag);

            Assert.AreEqual(0, completed, "The leased chair is not the shop's pool — the shop serves nobody.");
            Assert.AreEqual(1, runtime.WaitingQueue.Count, "The customer stays queued, never silently dropped.");

            var obligations = runtime.AccrueDayRent(300, diag);
            Assert.AreEqual(1, obligations.Count, "Rent still accrues while the tenant holds the chair.");
            Assert.AreEqual(50, obligations[0].RentCents);
            Assert.AreEqual("barb-biz-1", obligations[0].LandlordBusinessInstanceId);
            Assert.AreEqual(0, obligations[0].ChairIndex);
            Assert.AreEqual(300, obligations[0].DayIndex);
        }

        [Test]
        public void AccrueDayRent_AccruesPerCoveredDay_Only()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.LeaseChair(Lease(1, 502, 300, 303), diag));

            Assert.AreEqual(0, runtime.AccrueDayRent(299, diag).Count, "Before the lease — no rent.");
            Assert.AreEqual(50, runtime.AccrueDayRent(300, diag)[0].RentCents);
            Assert.AreEqual(50, runtime.AccrueDayRent(301, diag)[0].RentCents);
            Assert.AreEqual(50, runtime.AccrueDayRent(302, diag)[0].RentCents);
            Assert.AreEqual(0, runtime.AccrueDayRent(303, diag).Count, "End day is exclusive — no rent.");

            BarberChairRentObligation obligation = runtime.AccrueDayRent(301, diag)[0];
            Assert.AreEqual(EntityId.For(EntityKind.Person, 502), obligation.OperatorPersonId,
                "The obligation names the operator who owes it.");
            StringAssert.Contains("lease", obligation.LeaseId.ToLowerInvariant(),
                "The obligation carries the lease id for ledger posting.");
        }

        [Test]
        public void EndLease_ReturnsChairToPool()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.LeaseChair(Lease(0, 501, 300, 310), diag));
            var finder = ChairFinder("chair-asset-1", "chair-asset-2");

            Assert.NotNull(runtime.EndLease(1, 305, diag), "No active lease on chair 1 — nothing to end.");
            Assert.Null(runtime.EndLease(0, 305, diag));

            Assert.Null(runtime.ActiveLease(0, 305), "From the end day the chair is the shop's again.");
            Assert.NotNull(runtime.ActiveLease(0, 304), "Days before the early end are still covered.");
            Assert.AreEqual(2, runtime.CountReadyChairs(WorkstationCatalog.BarberChairStation, finder, 305, diag),
                "Both chairs are back in the pool at day 305.");
        }

        [Test]
        public void ServeDay_ShopServesOwnChairWhileOtherIsLeased()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.LeaseChair(Lease(0, 501, 300, 310), diag));

            Assert.Null(runtime.QueueCustomer(new BarberCustomer
            {
                CustomerPersonId = EntityId.For(EntityKind.Person, 101),
                ServiceId = BarberServiceCatalog.ShaveId,
                RequestDayIndex = 300,
            }, diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1", "chair-asset-2"), diag);

            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, runtime.CompletedServices[0].ChairIndex,
                "The shop serves from its own chair 1 — the tenant's chair 0 is untouched. " +
                "The shop never schedules the tenant's customers (Canon §8.6: the tenant is an independent business).");
        }
    }
}
