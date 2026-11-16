// DEF-071 (Decision 14): cancellation — a failed hired-freight shipment
// settles its agreement on the next tick (agreement-cancel + hold release
// + cargo record; no refund, because hired freight is paid on delivery
// only), and a shipment delayed three consecutive times without a forward
// move is abandoned instead of retried. A settlement that already ran is
// never repeated, and a failed shipment with no agreement settles nothing.
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FreightCancellationDef071Tests
    {
        private const int Day = 42;
        private const string CarrierId = "carrier-1";
        private const string BakeryId = "bakery-1";
        private const string SourceId = "mill-1";
        private const int FreightChargeCents = 125;
        private const int EthanId = 2;

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightProductionDriver freight;
        private FreightShipmentHooks hooks;
        private List<string> diag;
        private EntityId loader;
        private EntityId driverA;
        private List<EntityId> teamA;
        private BusinessRuntimeState payerRuntime;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            budgets.EnsureDay(Day + 1);
            freight = new FreightProductionDriver();
            freight.EnsureCompany(CarrierId, Runtime(CarrierId, 0));
            payerRuntime = Runtime(BakeryId, 20000);
            freight.RegisterBusinessRuntime(BakeryId, payerRuntime);
            freight.Service.RegisterWagon(Wagon("wagon-1"));
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    return quantityUnits;
                },
            };
            diag = new List<string>();
            loader = EntityId.For(EntityKind.Person, 701);
            driverA = EntityId.For(EntityKind.Person, 702);
            teamA = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
        }

        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static EquipmentAsset Wagon(string id) => new EquipmentAsset
        {
            AssetId = id,
            Kind = "farm-wagon",
            DisplayName = id,
            Condition01 = 0.9f,
            PayloadMassCapacityKg = 1000f,
            CargoVolumeCapacityM3 = 4f,
        };

        private static FreightCargoSpec GrainSpec() => new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };

        private EntityId Handler() => EntityId.For(EntityKind.Person, EthanId);

        private int Tick() => freight.AdvanceTick(86400f, hooks, Handler(), diag);

        private FreightServiceAgreement AcceptAgreement(string requestId, string lotId)
        {
            FreightServiceAgreementBook book = freight.ServiceAgreements;
            var request = new FreightServiceRequest
            {
                RequestId = requestId, RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = SourceId, DestinationBusinessId = BakeryId,
                CargoLotId = lotId, Quantity = 10, QuantityUnit = "lb", RequestingPersonId = EthanId,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "mill-door",
                DestinationLocationId = "bakery-door",
                CargoSkuId = "flour-sack"
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-" + requestId, OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }));
            Assert.IsNull(book.Accept(request.RequestId, EthanId, Day, 40000f, out FreightServiceAgreement agreement));
            Assert.IsNotNull(agreement);
            return agreement;
        }

        private LogisticsShipmentState CreateDriverShipment(string shipmentId, FreightServiceAgreement agreement)
        {
            LogisticsShipmentState shipment = freight.CreateShipment(CarrierId, shipmentId, SourceId, BakeryId, 10,
                GrainSpec(), "wagon-1", hooks, driverA, teamA, FreightChargeCents, BakeryId, diag,
                out string refusal, agreement.AgreementId);
            Assert.IsNotNull(shipment, refusal);
            Assert.IsNull(freight.ServiceAgreements.LinkShipment(agreement.AgreementId, shipmentId));
            return shipment;
        }

        private void LoadShipment(LogisticsShipmentState shipment)
        {
            Assert.IsTrue(freight.Service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 100)
                Assert.IsTrue(freight.Service.WorkLoading(shipment, 60, hooks, out string workReason), workReason);
            Assert.IsTrue(shipment.loadApplied, "loading must apply");
        }

        // ---------------- D1: failed shipments settle their agreements ----------------

        [Test]
        public void LotBindingRefusal_DispositionsThroughFailAbandonedShipment()
        {
            // A lot whose previous shipment completed cannot be bound again:
            // the lot is spent (delivered). The refusal must disposition the
            // new shipment terminally through FailAbandonedShipment — zero
            // cargo, transport unbound — with the agreement id recorded for
            // the settlement pass.
            LogisticsShipmentState holder = freight.Service.CreateShipment("ship-holder", SourceId, BakeryId,
                10, GrainSpec(), "wagon-1", hooks, out string holderReason);
            Assert.IsNotNull(holder, holderReason);
            holder.cargoLotId = "lot-1";
            holder.state = LogisticsShipmentStatus.Completed; // previous haul delivered; the lot is spent

            FreightServiceAgreement agreement = AcceptAgreement("request-1", "lot-1");
            LogisticsShipmentState shipment = freight.CreateShipment(CarrierId, "ship-b", SourceId, BakeryId, 10,
                GrainSpec(), "wagon-1", hooks, driverA, teamA, FreightChargeCents, BakeryId, diag,
                out string refusal, agreement.AgreementId, "lot-1", "flour-sack");
            Assert.IsNotNull(shipment, "the refused shipment must still be returned as a terminal record: " + refusal);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits,
                "a never-loaded shipment owns zero cargo (MR-P001)");
            Assert.IsTrue(!shipment.loadApplied, "nothing was ever loaded");
            Assert.IsTrue(!shipment.physicalTransportBound, "nothing was ever bound");
            Assert.AreEqual(agreement.AgreementId, shipment.FreightServiceAgreementId,
                "the settlement pass finds the agreement through this reference");
            StringAssert.Contains("already assigned", shipment.BlockedReason);
        }

        [Test]
        public void FailedShipment_SettlesAcceptedAgreement_OnNextTick()
        {
            FreightServiceAgreement agreement = AcceptAgreement("request-2", "lot-2");
            LogisticsShipmentState shipment = CreateDriverShipment("ship-2", agreement);
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement.Status);

            // A failure recorded outside FailAbandonedShipment: the shipment
            // failed after binding, so its transport is still bound.
            shipment.state = LogisticsShipmentStatus.Failed;
            shipment.physicalTransportBound = true;
            shipment.blockedReason = "wagon broke an axle on Millbrook Road";
            int payerCashBefore = payerRuntime.CurrentCashCents;

            Tick();

            Assert.AreEqual(FreightServiceAgreementStatus.Cancelled, agreement.Status);
            StringAssert.Contains("axle", agreement.FulfilmentStatus);
            Assert.IsTrue(!shipment.physicalTransportBound,
                "the still-bound transport is released by the settlement pass");
            Assert.AreEqual(payerCashBefore, payerRuntime.CurrentCashCents,
                "a failed haul collects no freight charge (payment is on delivery only)");
        }

        [Test]
        public void SettlementPass_IsIdempotent()
        {
            FreightServiceAgreement agreement = AcceptAgreement("request-3", "lot-3");
            LogisticsShipmentState shipment = CreateDriverShipment("ship-3", agreement);
            shipment.state = LogisticsShipmentStatus.Failed;
            shipment.blockedReason = "driver gave up";

            Tick();
            Assert.AreEqual(FreightServiceAgreementStatus.Cancelled, agreement.Status);
            Tick();

            Assert.AreEqual(FreightServiceAgreementStatus.Cancelled, agreement.Status);
            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            int cancelLines = 0;
            foreach (string line in diag)
            {
                if (line.Contains("cancelled") && line.Contains(agreement.AgreementId)) cancelLines++;
            }
            Assert.AreEqual(1, cancelLines, "the settlement pass must act exactly once per agreement");
        }

        [Test]
        public void FailedShipment_WithNoAgreement_SettlesNothing()
        {
            LogisticsShipmentState shipment = freight.CreateShipment(CarrierId, "ship-4", SourceId, BakeryId, 10,
                GrainSpec(), "wagon-1", hooks, driverA, teamA, FreightChargeCents, BakeryId, diag,
                out string refusal);
            Assert.IsNotNull(shipment, refusal);
            shipment.state = LogisticsShipmentStatus.Failed;
            shipment.blockedReason = "driver gave up";

            Tick();

            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.AreEqual("driver gave up", shipment.BlockedReason);
            foreach (string line in diag)
                Assert.IsTrue(!line.Contains("cancelled"), "no agreement exists, so nothing may be cancelled: " + line);
        }

        // ---------------- D2: three consecutive delays abandon the shipment ----------------

        [Test]
        public void DelayCycles_IncrementPerDelay_AndResetOnForwardMove()
        {
            LogisticsShipmentState shipment = freight.Service.CreateShipment("ship-5", SourceId, BakeryId,
                10, GrainSpec(), "wagon-1", hooks, out string reason);
            Assert.IsNotNull(shipment, reason);
            Assert.AreEqual(0, shipment.DelayCycles);

            shipment.MarkDelayed("receiver refused", LogisticsShipmentStatus.Arrived);
            Assert.AreEqual(1, shipment.DelayCycles);
            shipment.MarkDelayed("receiver refused again", LogisticsShipmentStatus.Arrived);
            Assert.AreEqual(2, shipment.DelayCycles);

            // A forward stage move clears the count: the legacy clock
            // advances the Delayed shipment to its resume state. A resume
            // alone would not reset it (a resume is a retry, not progress).
            shipment.AdvanceGameSeconds(0.01f);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);
            Assert.AreEqual(0, shipment.DelayCycles);
        }

        [Test]
        public void DelayedThreeConsecutiveCycles_IsAbandoned_AndAgreementCancelled()
        {
            FreightServiceAgreement agreement = AcceptAgreement("request-6", "lot-6");
            LogisticsShipmentState shipment = CreateDriverShipment("ship-6", agreement);

            // Three consecutive failures, no forward move between them.
            shipment.MarkDelayed("receiver refused", LogisticsShipmentStatus.Arrived);
            shipment.MarkDelayed("receiver refused again", LogisticsShipmentStatus.Arrived);
            shipment.MarkDelayed("receiver refused a third time", LogisticsShipmentStatus.Arrived);
            Assert.AreEqual(3, shipment.DelayCycles);

            Tick();

            Assert.AreEqual(LogisticsShipmentStatus.Failed, shipment.Status);
            Assert.AreEqual(0, shipment.RemainingQuantityUnits, "a never-loaded shipment owns zero cargo");
            StringAssert.Contains("3 consecutive", shipment.BlockedReason);
            Assert.AreEqual(FreightServiceAgreementStatus.Cancelled, agreement.Status);
        }

        [Test]
        public void DelayedTwiceThenProgressing_ResetsAndIsNotAbandoned()
        {
            FreightServiceAgreement agreement = AcceptAgreement("request-7", "lot-7");
            LogisticsShipmentState shipment = CreateDriverShipment("ship-7", agreement);

            // Two delays, then forward progress: load the wagon (which clears
            // the count) and dispatch on the tick (which also clears it).
            Assert.IsTrue(freight.Service.BeginLoading(shipment, loader, hooks, out string beginReason), beginReason);
            shipment.MarkDelayed("receiver refused", LogisticsShipmentStatus.Loading);
            shipment.MarkDelayed("receiver refused again", LogisticsShipmentStatus.Loading);
            Assert.AreEqual(2, shipment.DelayCycles);
            freight.Service.ResumeShipment(shipment, hooks);
            Assert.AreEqual(LogisticsShipmentStatus.Loading, shipment.Status);
            Assert.AreEqual(2, shipment.DelayCycles, "a resume is a retry, not progress — the count stands");

            Tick();
            Assert.IsTrue(shipment.loadApplied, "the tick must work the load");
            Assert.AreEqual(0, shipment.DelayCycles, "loading is forward progress — the count resets");
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, "the tick dispatches the loaded wagon");

            // Delayed twice more after the progress: still under the bound,
            // so the tick retries instead of abandoning.
            shipment.MarkDelayed("receiver refused", LogisticsShipmentStatus.InTransit);
            shipment.MarkDelayed("receiver refused again", LogisticsShipmentStatus.InTransit);
            Assert.AreEqual(2, shipment.DelayCycles);
            Tick();
            Assert.AreNotEqual(LogisticsShipmentStatus.Failed, shipment.Status,
                "two delays after progress must not abandon");
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement.Status);
        }

        [Test]
        public void DelayCycles_SurviveSaveLoadRoundTrip_OldSaveDefaultsToZero()
        {
            LogisticsShipmentState shipment = freight.Service.CreateShipment("ship-8", SourceId, BakeryId,
                10, GrainSpec(), "wagon-1", hooks, out string reason);
            Assert.IsNotNull(shipment, reason);
            LoadShipment(shipment);
            shipment.MarkDelayed("receiver refused", LogisticsShipmentStatus.Arrived);
            shipment.MarkDelayed("receiver refused again", LogisticsShipmentStatus.Arrived);
            Assert.AreEqual(2, shipment.DelayCycles);

            LogisticsShipmentSaveDto dto = shipment.CaptureSaveDto();
            Assert.AreEqual(2, dto.delayCycles);
            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(dto);
            Assert.AreEqual(2, restored.DelayCycles);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, restored.Status);
            Assert.IsTrue(restored.loadApplied, "the loaded state survives with the count");

            // An old save without the field restores 0 (clear-then-load stays valid).
            var legacyDto = new LogisticsShipmentSaveDto
            {
                shipmentId = "ship-legacy",
                state = LogisticsShipmentStatus.Delayed,
            };
            Assert.AreEqual(0, LogisticsShipmentState.FromSaveDto(legacyDto).DelayCycles);
        }
    }
}
