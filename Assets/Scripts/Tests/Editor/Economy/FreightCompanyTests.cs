using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Financing;
using LandLedgers.FirstLedger;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// BIZ-3: the freight company as a multi-unit enterprise (Canon §3.6) —
    /// one physical pool, MR-P001 shipment conservation, internal work never
    /// fabricates revenue to itself.
    /// </summary>
    [TestFixture]
    public sealed class FreightCompanyTests
    {
        private static FreightCompanyRuntime BuildCompany()
        {
            var company = new FreightCompanyRuntime("biz_freight_001");
            company.ResourcePool.AddWagon(new FreightWagon("wagon-1", "Wagon 1", 600));
            company.ResourcePool.AddWagon(new FreightWagon("wagon-2", "Wagon 2", 600));
            company.AddRoute(new FreightRoute("r1", "Town Run", "Depot", "Town", 120));
            return company;
        }

        private static List<EntityId> Animals(int count)
        {
            var registry = new EntityIdRegistry();
            var list = new List<EntityId>();
            for (int i = 0; i < count; i++)
            {
                list.Add(registry.Allocate(EntityKind.Animal));
            }

            return list;
        }

        private static List<EntityId> Drivers(int count)
        {
            var registry = new EntityIdRegistry();
            var list = new List<EntityId>();
            for (int i = 0; i < count; i++)
            {
                list.Add(registry.Allocate(EntityKind.Person));
            }

            return list;
        }

        private static LogisticsShipmentState BuildShipment(string id, int qty)
        {
            return new LogisticsShipmentState
            {
                shipmentId = id,
                plannedQuantityUnits = qty,
                remainingQuantityUnits = qty,
                freightChargeCents = 500,
                freightPayerBusinessInstanceId = "biz_store_001",
                haulingMode = ShipmentHaulingMode.HiredFreight,
                cargoClass = LogisticsCargoClass.PackagedGoods,
                state = LogisticsShipmentStatus.Planned,
            };
        }

        [Test]
        public void TryReserveTeam_ReservesWagonAnimalsAndDriver_AsOneUnit()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();

            bool ok = company.ResourcePool.TryReserveTeam(
                500, Animals(4), Drivers(2), "test haul",
                out DraftTeamReservation reservation, diagnostics);

            Assert.IsTrue(ok, string.Join("; ", diagnostics));
            Assert.IsNotNull(reservation);
            Assert.AreEqual(2, reservation.DraftAnimalIds.Count);
            Assert.AreEqual(1, company.ResourcePool.AvailableWagonCount);
        }

        [Test]
        public void TryReserveTeam_CannotDoubleBookWagon()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();

            company.ResourcePool.TryReserveTeam(500, Animals(4), Drivers(2), "first",
                out DraftTeamReservation first, diagnostics);
            bool second = company.ResourcePool.TryReserveTeam(500, Animals(4), Drivers(2), "second",
                out DraftTeamReservation secondReservation, diagnostics);

            Assert.IsTrue(second); // second wagon available
            Assert.AreNotEqual(first.WagonId, secondReservation.WagonId);

            // Third reservation: no wagons left.
            bool third = company.ResourcePool.TryReserveTeam(100, Animals(4), Drivers(2), "third",
                out _, diagnostics);
            Assert.IsFalse(third);
        }

        [Test]
        public void WagonInRepair_CannotHaul()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();

            company.ResourcePool.TrySendToRepair("wagon-1", diagnostics);
            bool ok = company.ResourcePool.TryReserveTeam(700, Animals(4), Drivers(2), "heavy",
                out _, diagnostics);

            // Only wagon-2 (600) is available; 700 exceeds it. wagon-1 is in repair.
            Assert.IsFalse(ok);
        }

        [Test]
        public void ReleaseTeam_ReturnsWagonToAvailable()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();

            company.ResourcePool.TryReserveTeam(500, Animals(4), Drivers(2), "haul",
                out DraftTeamReservation reservation, diagnostics);
            Assert.AreEqual(1, company.ResourcePool.AvailableWagonCount);

            company.ResourcePool.ReleaseTeam(reservation);

            Assert.AreEqual(2, company.ResourcePool.AvailableWagonCount);
        }

        [Test]
        public void TryAcceptShipment_RefusesPreLoadBlockedPhantomCargo()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();
            var blocked = new LogisticsShipmentState
            {
                shipmentId = "phantom-1",
                plannedQuantityUnits = 100,
                remainingQuantityUnits = 0,
                blockedReason = "no source inventory",
                state = LogisticsShipmentStatus.Failed,
                loadApplied = false,
                sourceCommittedAtSchedule = false,
            };

            Assert.IsTrue(blocked.IsPreLoadBlocked);
            bool ok = company.TryAcceptShipment(blocked, diagnostics);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, company.ActiveShipments.Count);
        }

        [Test]
        public void TryAcceptShipment_RefusesSelfHaulAndOtherCarrierCustody()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();
            LogisticsShipmentState shipment = BuildShipment("self-haul", 10);
            shipment.haulingMode = ShipmentHaulingMode.SourceDelivers;
            Assert.IsFalse(company.TryAcceptShipment(shipment, diagnostics));
            Assert.AreEqual(string.Empty, shipment.CarrierBusinessInstanceId);
            Assert.IsEmpty(company.ActiveShipments);

            shipment.haulingMode = ShipmentHaulingMode.HiredFreight;
            shipment.carrierBusinessInstanceId = "biz-other-carrier";
            Assert.IsFalse(company.TryAcceptShipment(shipment, diagnostics));
            Assert.AreEqual("biz-other-carrier", shipment.CarrierBusinessInstanceId);
            Assert.IsEmpty(company.ActiveShipments);
        }

        [Test]
        public void TryAcceptShipment_RejectsDuplicateShipmentIdentity()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();
            LogisticsShipmentState shipment = BuildShipment("duplicate", 10);
            Assert.IsTrue(company.TryAcceptShipment(shipment, diagnostics));

            LogisticsShipmentState duplicate = BuildShipment("duplicate", 10);
            Assert.IsFalse(company.TryAcceptShipment(duplicate, diagnostics));
            Assert.AreEqual(1, company.ActiveShipments.Count);
            Assert.AreEqual(string.Empty, duplicate.CarrierBusinessInstanceId);
        }

        [Test]
        public void VerifiedCollection_InsufficientFundsCreatesOnePayable_ThenPaysCarrierOnce()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();
            LogisticsShipmentState shipment = BuildShipment("payable-shipment", 10);
            shipment.state = LogisticsShipmentStatus.Completed;
            shipment.deliveryApplied = true;
            Assert.IsTrue(company.TryAcceptShipment(shipment, diagnostics));

            var payer = new BusinessRuntimeState();
            payer.BindBusinessIdentity("biz_store_001");
            payer.SetCurrentCashCents(0);
            payer.ObligationAuthority = new FinancialObligationAuthority();
            payer.ObligationIdRegistry = new EntityIdRegistry();
            var carrier = new BusinessRuntimeState();
            carrier.BindBusinessIdentity("biz_freight_001");
            carrier.SetCurrentCashCents(0);
            BusinessRuntimeState Resolve(string id) => id == payer.BusinessId ? payer : null;

            Assert.AreEqual(0, company.CollectAndPostUncollectedRevenue(carrier, Resolve, diagnostics));
            Assert.AreEqual(0, company.CollectAndPostUncollectedRevenue(carrier, Resolve, diagnostics));
            Assert.AreEqual(0, payer.CurrentCashCents);
            Assert.AreEqual(0, carrier.CurrentCashCents);
            Assert.AreEqual(1, payer.NamedPayables.Count, "repeated retries must not duplicate the shipment debt");
            Assert.AreEqual("biz_freight_001", payer.NamedPayables[0].Creditor);
            Assert.AreEqual(500, payer.NamedPayables[0].AmountCents);
            Assert.IsFalse(shipment.FreightRevenueCollected,
                "the freight charge remains unsettled until real cash pays the recorded payable");

            payer.SetCurrentCashCents(500);
            Assert.AreEqual(500, company.CollectAndPostUncollectedRevenue(carrier, Resolve, diagnostics));
            Assert.AreEqual(0, payer.CurrentCashCents);
            Assert.AreEqual(500, carrier.CurrentCashCents);
            Assert.IsEmpty(payer.NamedPayables);
            Assert.IsTrue(shipment.FreightRevenueCollected);

            Assert.AreEqual(0, company.CollectAndPostUncollectedRevenue(carrier, Resolve, diagnostics));
            Assert.AreEqual(0, payer.CurrentCashCents);
            Assert.AreEqual(500, carrier.CurrentCashCents);
        }

        [Test]
        public void CollectFreightCharge_OnlyFromExternalPayer_OnCompletion()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();
            LogisticsShipmentState shipment = BuildShipment("s1", 100);
            company.TryAcceptShipment(shipment, diagnostics);

            // Not completed yet: no charge.
            Assert.AreEqual(0, company.CollectFreightCharge("s1", diagnostics));

            shipment.state = LogisticsShipmentStatus.Completed;
            shipment.deliveryApplied = true;
            Assert.AreEqual(0, company.CollectFreightCharge("s1", diagnostics),
                "A named payer without a verified till cannot be charged or marked settled.");
            var payer = new BusinessRuntimeState();
            payer.BindBusinessIdentity("biz_store_001");
            payer.SetCurrentCashCents(1000);
            Assert.AreEqual(500, company.CollectFreightCharge("s1", payer, diagnostics));
            Assert.AreEqual(500, payer.CurrentCashCents);
            Assert.IsTrue(shipment.FreightRevenueCollected);
        }

        [Test]
        public void CollectFreightCharge_SelfPayer_RecordsNoRevenue()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();
            LogisticsShipmentState shipment = BuildShipment("s2", 100);
            shipment.freightPayerBusinessInstanceId = "biz_freight_001"; // self
            shipment.state = LogisticsShipmentStatus.Completed;
            shipment.deliveryApplied = true;
            company.TryAcceptShipment(shipment, diagnostics);

            // Canon §3.6: internal work never fabricates revenue to the same enterprise.
            Assert.AreEqual(0, company.CollectFreightCharge("s2", diagnostics));
        }

        [Test]
        public void RepairJob_RecordsCost_NotRevenue()
        {
            FreightCompanyRuntime company = BuildCompany();
            var diagnostics = new List<string>();

            company.ResourcePool.TrySendToRepair("wagon-1", diagnostics);
            bool ok = company.TryCompleteRepairJob("wagon-1", 1200, 90, diagnostics);

            Assert.IsTrue(ok);
            Assert.AreEqual(1200, company.InternalRepairCostCents);
            Assert.AreEqual(FreightWagonStatus.Available,
                company.ResourcePool.GetWagon("wagon-1").Status);
        }

        [Test]
        public void FreightMinutes_ScalesWithQuantity_TTS5Math()
        {
            // 3 dozen eggs ≈ 2-3 min; full 600-unit wagon = 30 min at 20/min.
            Assert.AreEqual(2, FreightCompanyRuntime.FreightMinutes(36, FreightGoodsClass.Standard));
            Assert.AreEqual(30, FreightCompanyRuntime.FreightMinutes(600, FreightGoodsClass.Standard));
            Assert.AreEqual(3, FreightCompanyRuntime.FreightMinutes(36, FreightGoodsClass.Perishable));
            Assert.AreEqual(1, FreightCompanyRuntime.FreightMinutes(0, FreightGoodsClass.Standard));
        }

        [Test]
        public void ToGoodsClass_MapsPerishablesAndLiveAnimals()
        {
            Assert.AreEqual(FreightGoodsClass.Perishable,
                FreightCompanyRuntime.ToGoodsClass(LogisticsCargoClass.Perishables));
            Assert.AreEqual(FreightGoodsClass.Perishable,
                FreightCompanyRuntime.ToGoodsClass(LogisticsCargoClass.LiveAnimals));
            Assert.AreEqual(FreightGoodsClass.Standard,
                FreightCompanyRuntime.ToGoodsClass(LogisticsCargoClass.BulkMaterials));
        }
    }
}
