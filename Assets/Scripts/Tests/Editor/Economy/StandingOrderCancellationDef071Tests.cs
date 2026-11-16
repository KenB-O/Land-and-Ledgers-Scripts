// DEF-071 (Decision 14): cancellation — a standing-order principal can
// stop their own order. Future weeks resolve Cancelled with the recorded
// reason; shipments already dispatched stand (the relationship holds no
// shipment reference — stopping the order cannot reach them). The harness
// shim's Resources.LoadAll returns an empty array, so the profiles here are
// built in code (BusinessProfileDefinition.CreateFallback) and the
// livestock_inputs stock slot is ensured on the runtime state with
// EnsureCategoryStock. Copying that file's LoadProfile helper verbatim would
// fail every test in the harness, and injecting the category into the
// profile would not help either — BusinessRuntimeState.CreateFrom skips any
// category the business definition does not own.
// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.

using LandLedgers.Economy;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class StandingOrderCancellationDef071Tests
    {
        private static LocalRecurringOrderTemplate RanchToButcherTemplate()
        {
            return new LocalRecurringOrderTemplate(
                "test_ranch_to_butcher",
                BusinessType.Ranch,
                "livestock_inputs",
                BusinessType.Butcher,
                "livestock_inputs",
                4);
        }

        private static BusinessInstanceState CreateBusiness(BusinessType businessType, string instanceId)
        {
            BusinessInstanceState state = BusinessInstanceState.Create(instanceId,
                BusinessProfileDefinition.CreateFallback(businessType, "Test " + businessType),
                0, BusinessOwnerIdentity.Npc(1, "Test Owner", "Test"));
            // The fallback profile owns no categories, so the runtime starts
            // with no stock slots; the standing-order category is ensured
            // here on the runtime state rather than invented on the profile.
            // (BusinessRuntimeState.CreateFrom skips any category the
            // business definition does not own, so injecting the category
            // into the profile would not create the slot.)
            state.RuntimeState.EnsureCategoryStock("livestock_inputs", 0, 0);
            return state;
        }

        private static void SetStock(BusinessInstanceState business, string categoryId, int units)
        {
            CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
            Assert.NotNull(stock, $"{business.BusinessType} should have {categoryId} stock.");
            stock.SetCurrentStockForTests(units);
        }

        private static void SetCash(BusinessInstanceState business, int cashCents)
        {
            int target = Mathf.Max(0, cashCents);
            int current = business.RuntimeState.CurrentCashCents;
            if (current > target)
            {
                business.RuntimeState.SpendCents(current - target);
                return;
            }

            business.RuntimeState.AddCashCents(target - current);
        }

        private static LocalRecurringOrderFulfillmentRequest CreateRequest(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer,
            int weekKey)
        {
            return new LocalRecurringOrderFulfillmentRequest
            {
                Template = template,
                Seller = seller,
                Buyer = buyer,
                WeekKey = weekKey,
                UnitPriceCents = 100,
                BuyerCashReserveCents = 2500,
                SellerCanFulfill = true,
                BuyerCanReceive = true,
            };
        }

        private static (LocalRecurringOrderManager Manager, LocalRecurringOrderTemplate Template,
            BusinessInstanceState Ranch, BusinessInstanceState Butcher) LiveOrder()
        {
            var manager = new LocalRecurringOrderManager();
            LocalRecurringOrderTemplate template = RanchToButcherTemplate();
            BusinessInstanceState ranch = CreateBusiness(BusinessType.Ranch, "test_ranch_stop");
            BusinessInstanceState butcher = CreateBusiness(BusinessType.Butcher, "test_butcher_stop");
            SetStock(ranch, "livestock_inputs", 8);
            SetStock(butcher, "livestock_inputs", 0);
            SetCash(ranch, 10000);
            SetCash(butcher, 20000);
            return (manager, template, ranch, butcher);
        }

        [Test]
        public void StoppedRelationship_ResolvesCancelled_WithRecordedReason()
        {
            var (manager, template, ranch, butcher) = LiveOrder();

            LocalRecurringOrderFulfillmentResult first = manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1));
            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, first.Status);
            Assert.AreEqual(1, manager.Relationships.Count);

            Assert.IsNull(manager.StopRelationship(template, ranch, butcher,
                "the butcher found a cheaper ranch", 2));
            LocalRecurringOrderRelationshipState relationship = manager.Relationships[0];
            Assert.IsTrue(!relationship.Active);
            Assert.AreEqual("the butcher found a cheaper ranch", relationship.CancellationReason);

            LocalRecurringOrderFulfillmentResult next = manager.ResolveOrder(CreateRequest(template, ranch, butcher, 2));
            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Cancelled, next.Status);
            Assert.AreEqual("the butcher found a cheaper ranch", next.Reason);
            Assert.AreEqual(0, next.FulfilledUnits);
            Assert.AreEqual(4, ranch.RuntimeState.GetCategoryStock("livestock_inputs").CurrentStockUnits,
                "the stop moves no stock — the ranch keeps what the first order left");
        }

        [Test]
        public void StopRelationship_RefusesBlankReason_UnknownOrder_AndSecondStop()
        {
            var (manager, template, ranch, butcher) = LiveOrder();

            Assert.IsNotNull(manager.StopRelationship(template, ranch, butcher, "no such order", 1),
                "stopping an order that was never created must be refused, not created just to cancel it");
            Assert.AreEqual(0, manager.Relationships.Count);

            Assert.IsNotNull(manager.StopRelationship(template, ranch, butcher, "   ", 1),
                "a blank reason is no reason");
            manager.ResolveOrder(CreateRequest(template, ranch, butcher, 1));
            Assert.IsNotNull(manager.StopRelationship(template, ranch, butcher, "", 2),
                "a blank reason is refused even on a live order");
            Assert.IsTrue(manager.Relationships[0].Active);

            Assert.IsNull(manager.StopRelationship(template, ranch, butcher, "ending the arrangement", 2));
            Assert.IsNotNull(manager.StopRelationship(template, ranch, butcher, "ending it again", 3),
                "a second stop is refused — the cancellation is already on the record");
        }

        [Test]
        public void DispatchedShipment_Stands_AfterStop_NoNewShipmentIsScheduled()
        {
            var (manager, template, ranch, butcher) = LiveOrder();
            int shipmentsScheduled = 0;
            LocalRecurringOrderFulfillmentRequest request = CreateRequest(template, ranch, butcher, 1);
            request.ScheduleShipment = (req, units, sellerPrice, buyerCost) =>
            {
                shipmentsScheduled++;
                return units;
            };

            LocalRecurringOrderFulfillmentResult first = manager.ResolveOrder(request);
            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Clean, first.Status);
            Assert.AreEqual(4, first.FulfilledUnits);
            Assert.AreEqual(1, shipmentsScheduled, "the first week's order dispatched one shipment");

            Assert.IsNull(manager.StopRelationship(template, ranch, butcher, "ending the arrangement", 2));

            LocalRecurringOrderFulfillmentResult next = manager.ResolveOrder(CreateRequest(template, ranch, butcher, 2));
            Assert.AreEqual(LocalRecurringOrderFulfillmentStatus.Cancelled, next.Status);
            Assert.AreEqual(1, shipmentsScheduled,
                "no new shipment is scheduled after the stop — and the one already dispatched stands, untouched by it");
        }
    }
}
