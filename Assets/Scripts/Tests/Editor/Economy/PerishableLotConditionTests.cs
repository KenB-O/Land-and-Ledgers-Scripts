// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority E Slice 5 — perishable lot condition foundation (directive P2).
// The condition model is Canon-locked, not a plan preference: Canon XXXI
// 4.5 (perishable degradation and food-safety risk are separate causal
// states, neither only an expiry timer; mixing cannot rejuvenate) and 4.6
// (preservation transforms and does not reset), Technical XIV 4.1 (lot and
// batch provenance; the suggested state fields are this type's shape) and
// 4.2 (output inherits source history). No authored per-item mass, sack
// weight, person carry limit, or shelf-life number exists anywhere in
// v3.1, so this slice invents none: the ONLY authored aging rule in the
// tree is BakeryGoodsCondition Fresh/DayOld/Stale plus AgeToDay, and
// deterioration applies only where such an authored parameter exists.
// Where no authored parameter exists the condition does NOT change and
// remaining useful life stays Unknown.
//
// No single freshness percentage exists anywhere in this file or in the
// production type: physical condition, food-safety state, remaining
// useful life, handling history, and perceived vs known condition are
// separate dimensions, never collapsed into one number.
//
// The model is unwired by design (no driver activation): these tests
// construct states directly and drive the real Millbrook-to-household
// chain only to prove the state survives transfers and purchase and
// that the model changes nothing in any live authority (acceptance Q)
// with the chain figures unchanged (acceptance R).

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Economy.Storage;
using LandLedgers.Economy.Transport;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class PerishableLotConditionTests
    {
        private const int Day = 12;
        private const int EthanId = 7;
        private const string CarrierId = "livery_freight_003";
        private const string BakeryId = "bakery_001";
        private const string StoreInstanceId = "general-store-1";
        private const string WagonId = "opening-freight-wagon";
        private const string ShipmentId = "freight-shipment:request-1";
        private const string AgreementId = "freight-agreement:request-1";
        private const string LotId = "aidlot-millbrook_grain_co:chain-flour-16";
        private const string OrderRequestId = "chain-flour-16";
        private const int Sacks = 16;
        private const int FreightChargeCents = 125;
        private const int HouseholdId = 7;
        private const int ShopperPersonId = 1;

        private static LotKey MillbrookKey() =>
            new LotKey(CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId);

        private static PerishableLotState FlourSackState(int units) =>
            new PerishableLotState(MillbrookKey(), "bakery_flour_sack", units, 0);

        private static PerishableLotState BreadState(LotKey key, int units, int bakedDay) =>
            new PerishableLotState(key, BakeryBreadCatalog.BreadLoafId, units, bakedDay);

        [Test]
        public void ConditionDimensionsStaySeparate_PhysicalChangeLeavesSafetyAlone()
        {
            // Canon XXXI 4.5: degradation and food-safety risk are
            // separate causal states. Changing one dimension must never
            // move the other.
            PerishableLotState state = BreadState(MillbrookKey(), 24, Day)
                .WithSafety(PerishableSafetyState.Safe, "no contamination observed");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition);
            Assert.AreEqual(PerishableSafetyState.Safe, state.SafetyState);

            PerishableLotState aged = state.WithConditions(PerishablePhysicalCondition.DayOld, state.PerceivedCondition);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, aged.PhysicalCondition,
                "the physical condition moved");
            Assert.AreEqual(PerishableSafetyState.Safe, aged.SafetyState,
                "moving the physical condition must not move the safety state");
            Assert.AreEqual("no contamination observed", aged.SafetyEvidence,
                "the safety evidence stays with the safety state");

            PerishableLotState suspect = aged.WithSafety(PerishableSafetyState.Suspect, "odd smell reported");
            Assert.AreEqual(PerishableSafetyState.Suspect, suspect.SafetyState,
                "the safety state moved");
            Assert.AreEqual("odd smell reported", suspect.SafetyEvidence);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, suspect.PhysicalCondition,
                "moving the safety state must not move the physical condition");
            Assert.AreEqual(24, suspect.Units, "neither change touches the quantity");
            Assert.AreEqual(24, state.Units, "the original state is untouched — the type is immutable");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition,
                "the original state's physical condition is untouched");
        }

        [Test]
        public void PerceivedAndKnownConditionCanDiffer_ReportedSeparately()
        {
            // Perceived condition (what an observer reports) and known
            // condition (what the lot's history establishes) are separate
            // fields: a lot can look stale while its record says fresh,
            // and the model reports both instead of reconciling them.
            PerishableLotState state = BreadState(MillbrookKey(), 24, Day)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Stale);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, state.PhysicalCondition,
                "the known condition is Fresh");
            Assert.AreEqual(PerishablePhysicalCondition.Stale, state.PerceivedCondition,
                "the perceived condition is Stale — reported separately, never merged");
            Assert.IsTrue(!state.PhysicalCondition.Equals(state.PerceivedCondition),
                "the two fields genuinely differ here");

            PerishableLotState reobserved = state.WithConditions(state.PhysicalCondition, PerishablePhysicalCondition.Fresh);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, reobserved.PhysicalCondition,
                "re-observation moves only the perceived field");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, reobserved.PerceivedCondition);
        }

        [Test]
        public void NoAuthoredParameter_NoChange_UnknownRemainingLife()
        {
            // No authored aging parameter exists for flour (the F1 spec
            // carries no aging parameter at all — Perishable is a data
            // flag only), so thirty simulated days change nothing and
            // remaining useful life stays Unknown. The model never
            // invents a decay rate.
            PerishableLotState state = FlourSackState(Sacks);
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition);
            Assert.IsTrue(!state.RemainingUsefulLife.IsKnown, "remaining life starts Unknown");
            Assert.AreEqual(PerishableSafetyState.Unknown, state.SafetyState);

            PerishableLotState aged = PerishableLotState.ApplyElapsedTime(state, 30.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, aged.PhysicalCondition,
                "no authored parameter, so the condition does not change");
            Assert.IsTrue(!aged.RemainingUsefulLife.IsKnown,
                "remaining useful life stays Unknown — no decay rate is invented");
            Assert.AreEqual(PerishableSafetyState.Unknown, aged.SafetyState,
                "elapsed time alone never creates a safety state");
            Assert.AreEqual(Sacks, aged.Units, "deterioration changes condition, never quantity");
            Assert.AreEqual(30.0, aged.AsOfDay - state.AsOfDay, 0.0,
                "the clock still advances — only the condition is unmoved");
            Assert.AreEqual(0, aged.HandlingHistory.Count,
                "time passing is not a handling event");
        }

        [Test]
        public void BakeryBreadAgesFreshToDayOldToStale_ByAgeToDay()
        {
            // The only authored aging rule in the tree: BakeryBreadLot
            // AgeToDay (baked day Fresh, next day DayOld, after that
            // Stale). The state must agree with it exactly.
            var breadLot = new BakeryBreadLot
            {
                LotId = EntityId.For(EntityKind.Lot, 99),
                ProductId = BakeryBreadCatalog.BreadLoafId,
                BakedDayIndex = Day,
                UnitCount = 24,
            };
            var key = new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), "99");
            PerishableLotState state = BreadState(key, 24, Day);
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition,
                "a freshly constructed state carries no condition until deterioration is applied");

            PerishableLotState day0 = PerishableLotState.ApplyElapsedTime(state, 0.0, "ambient");
            breadLot.AgeToDay(Day);
            Assert.AreEqual(PerishableLotState.FromBakeryCondition(breadLot.Condition), day0.PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, day0.PhysicalCondition, "baked today: Fresh");

            PerishableLotState day1 = PerishableLotState.ApplyElapsedTime(state, 1.0, "ambient");
            breadLot.AgeToDay(Day + 1);
            Assert.AreEqual(PerishableLotState.FromBakeryCondition(breadLot.Condition), day1.PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, day1.PhysicalCondition, "baked yesterday: DayOld");

            PerishableLotState day2 = PerishableLotState.ApplyElapsedTime(state, 2.0, "ambient");
            breadLot.AgeToDay(Day + 2);
            Assert.AreEqual(PerishableLotState.FromBakeryCondition(breadLot.Condition), day2.PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.Stale, day2.PhysicalCondition, "older: Stale waste");

            PerishableLotState day30 = PerishableLotState.ApplyElapsedTime(state, 30.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.Stale, day30.PhysicalCondition,
                "stale is terminal: further time never improves it");
        }

        [Test]
        public void Deterioration_IsTickSubdivisionInvariant_AcceptanceP()
        {
            // Acceptance P: equivalent simulated elapsed time produces
            // equivalent deterioration regardless of tick subdivision.
            PerishableLotState start = BreadState(MillbrookKey(), 24, Day);

            PerishableLotState oneStep = PerishableLotState.ApplyElapsedTime(start, 6.0, "ambient");
            PerishableLotState sixSteps = start;
            for (int i = 0; i < 6; i++)
                sixSteps = PerishableLotState.ApplyElapsedTime(sixSteps, 1.0, "ambient");
            PerishableLotState twelveSteps = start;
            for (int i = 0; i < 12; i++)
                twelveSteps = PerishableLotState.ApplyElapsedTime(twelveSteps, 0.5, "ambient");

            Assert.IsTrue(oneStep.EqualsState(sixSteps),
                "one 6-day step must equal six 1-day steps");
            Assert.IsTrue(oneStep.EqualsState(twelveSteps),
                "one 6-day step must equal twelve half-day steps");
            Assert.AreEqual(PerishablePhysicalCondition.Stale, oneStep.PhysicalCondition,
                "six days past baking is Stale on every subdivision");
            Assert.AreEqual(Day + 6.0, oneStep.AsOfDay, 0.0);
            Assert.AreEqual(Day + 6.0, sixSteps.AsOfDay, 0.0);
            Assert.AreEqual(Day + 6.0, twelveSteps.AsOfDay, 0.0);

            // The same invariance holds where nothing is authored: the
            // flour states agree too, still Unknown.
            PerishableLotState flourOne = PerishableLotState.ApplyElapsedTime(FlourSackState(Sacks), 6.0, "ambient");
            PerishableLotState flourTwelve = FlourSackState(Sacks);
            for (int i = 0; i < 12; i++)
                flourTwelve = PerishableLotState.ApplyElapsedTime(flourTwelve, 0.5, "ambient");
            Assert.IsTrue(flourOne.EqualsState(flourTwelve), "flour subdivision invariance");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, flourOne.PhysicalCondition);
        }

        [Test]
        public void Deterioration_IsDeterministic_AndStorageConditionsInventNoRule()
        {
            // Pure function of simulation time: same input, same output,
            // no wall clock, no randomness. And no authored storage rule
            // exists, so the storage-conditions argument cannot change
            // the outcome today — it is a stable signature for a future
            // authored rule, not a live dial.
            PerishableLotState start = BreadState(MillbrookKey(), 24, Day);
            PerishableLotState first = PerishableLotState.ApplyElapsedTime(start, 3.0, "ambient");
            PerishableLotState second = PerishableLotState.ApplyElapsedTime(start, 3.0, "ambient");
            Assert.IsTrue(first.EqualsState(second), "identical inputs must give identical states");
            Assert.AreEqual(PerishablePhysicalCondition.Stale, first.PhysicalCondition);

            PerishableLotState cool = PerishableLotState.ApplyElapsedTime(start, 3.0, "cool cellar");
            PerishableLotState warm = PerishableLotState.ApplyElapsedTime(start, 3.0, "warm kitchen");
            PerishableLotState unheated = PerishableLotState.ApplyElapsedTime(start, 3.0, "unheated barn");
            Assert.IsTrue(first.EqualsState(cool),
                "no authored storage rule: 'cool cellar' must not change the outcome");
            Assert.IsTrue(first.EqualsState(warm), "no authored storage rule: 'warm kitchen' must not change it");
            Assert.IsTrue(first.EqualsState(unheated), "no authored storage rule: 'unheated barn' must not change it");
            Assert.IsTrue(first.EqualsState(PerishableLotState.ApplyElapsedTime(start, 3.0, null)),
                "a missing storage description also changes nothing");
        }

        [Test]
        public void Deterioration_NegativeElapsed_IsRefused()
        {
            PerishableLotState state = BreadState(MillbrookKey(), 24, Day);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PerishableLotState.ApplyElapsedTime(state, -1.0, "ambient"),
                "time cannot run backwards through deterioration");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition,
                "the refused call leaves the state untouched");
        }

        [Test]
        public void PreservationTransformsWithoutResetting_Canon46()
        {
            // Canon XXXI 4.6: preservation transforms and does not reset.
            // A DayOld loaf stays DayOld (never Fresh again); a Stale
            // loaf stays Stale; the preservation is recorded as a
            // handling event and the age is untouched.
            PerishableLotState dayOld = PerishableLotState.ApplyElapsedTime(
                BreadState(MillbrookKey(), 24, Day), 1.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, dayOld.PhysicalCondition);

            PerishableLotState preserved = dayOld.ApplyPreservation("cool dark storage", Day + 1);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, preserved.PhysicalCondition,
                "preservation never rejuvenates: DayOld stays DayOld");
            Assert.AreEqual(Day + 1.0, preserved.AsOfDay, 0.0, "the age is untouched by preservation");
            Assert.IsTrue(preserved.IsPreserved, "the preservation is recorded");
            Assert.AreEqual("cool dark storage", preserved.PreservationMethod);
            Assert.AreEqual(1, preserved.HandlingHistory.Count, "one handling event recorded");
            Assert.AreEqual("preservation", preserved.HandlingHistory[0].Kind);
            Assert.IsTrue(preserved.HandlingHistory[0].Detail.Contains("cool dark storage"),
                preserved.HandlingHistory[0].Detail);

            PerishableLotState stale = PerishableLotState.ApplyElapsedTime(
                BreadState(MillbrookKey(), 24, Day), 5.0, "ambient");
            PerishableLotState stalePreserved = stale.ApplyPreservation("cool dark storage", Day + 5);
            Assert.AreEqual(PerishablePhysicalCondition.Stale, stalePreserved.PhysicalCondition,
                "preservation never rescues Stale either");
            Assert.IsTrue(!dayOld.IsPreserved, "the unpreserved state records no preservation");
            Assert.AreEqual(string.Empty, dayOld.PreservationMethod,
                "no preservation method before one is applied");
        }

        [Test]
        public void SplitAndTransferPreserveCondition_NoRejuvenationByMixing()
        {
            // Canon XXXI 4.5: mixing new and old stock cannot rejuvenate
            // old product. The type has no operation that blends or
            // averages conditions: splitting a DayOld lot yields two
            // DayOld lots, and transferring one never improves it.
            PerishableLotState dayOld = PerishableLotState.ApplyElapsedTime(
                BreadState(MillbrookKey(), 10, Day), 1.0, "ambient");
            (PerishableLotState remaining, PerishableLotState part) = dayOld.Split(4, Day + 1);
            Assert.AreEqual(6, remaining.Units, "10 - 4 stays behind");
            Assert.AreEqual(4, part.Units, "4 moves");
            Assert.AreEqual(6 + 4, 10, "the split conserves exactly");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, remaining.PhysicalCondition,
                "the remainder is not improved by the split");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, part.PhysicalCondition,
                "the split-off part inherits the source condition exactly — no averaging, no fresh start");
            Assert.AreEqual(dayOld.SafetyState, part.SafetyState, "safety is inherited too");

            PerishableLotState moved = part.TransferredTo("household:7", Day + 1, "carried home");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, moved.PhysicalCondition,
                "transfer never improves the condition");
            Assert.AreEqual(4, moved.Units, "transfer moves the quantity unchanged");
            Assert.AreEqual("household:7", moved.LocationOrContainer);
            Assert.AreEqual(2, moved.HandlingHistory.Count,
                "split event plus transfer event, in order");

            // A Fresh lot split beside a Stale lot stays Fresh on its own
            // record and Stale stays Stale: no operation blends them.
            PerishableLotState fresh = BreadState(MillbrookKey(), 5, Day);
            PerishableLotState stale = PerishableLotState.ApplyElapsedTime(
                BreadState(MillbrookKey(), 5, Day), 4.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, fresh.PhysicalCondition,
                "freshness is only assigned by deterioration, not by construction");
            PerishableLotState freshAged0 = PerishableLotState.ApplyElapsedTime(fresh, 0.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, freshAged0.PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.Stale, stale.PhysicalCondition);
        }

        [Test]
        public void SpoilageRecording_AccountsTheLossExactly()
        {
            // The record is the accounting: units lost plus units
            // remaining equal units before, no creation. Recording a
            // loss does not remove units from any live authority — that
            // application is downstream; this test pins the accounting.
            PerishableLotState state = BreadState(MillbrookKey(), 24, Day);
            PerishableLotState updated;
            SpoilageRecord record = state.RecordSpoilage(5, "mould", Day + 2, "visible mould on 5 loaves", out updated);
            Assert.AreEqual(MillbrookKey(), record.LotKey, "the record names its lot");
            Assert.AreEqual(5, record.UnitsLost);
            Assert.AreEqual("mould", record.Cause);
            Assert.AreEqual(Day + 2, record.DayIndex);
            Assert.AreEqual("visible mould on 5 loaves", record.Evidence);
            Assert.AreEqual(19, updated.Units, "24 - 5 remain");
            Assert.AreEqual(record.UnitsLost + updated.Units, 24, "lost + remaining = before: exact accounting");
            Assert.AreEqual(24, state.Units, "the original state is untouched — the call returns the updated state");
            Assert.AreEqual(1, updated.HandlingHistory.Count, "the spoilage is a recorded handling event");
            Assert.AreEqual("spoilage", updated.HandlingHistory[0].Kind);

            PerishableLotState emptied;
            SpoilageRecord all = updated.RecordSpoilage(19, "mould", Day + 2, "rest condemned", out emptied);
            Assert.AreEqual(19, all.UnitsLost);
            Assert.AreEqual(0, emptied.Units, "the lot is empty");
            Assert.AreEqual(PerishableDisposition.Spoiled, emptied.Disposition,
                "a fully spoiled lot is disposed Spoiled");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, emptied.PhysicalCondition,
                "spoilage accounting alone does not invent a physical condition");
        }

        [Test]
        public void SpoilageRecording_RefusesLossBeyondTheLot()
        {
            PerishableLotState state = BreadState(MillbrookKey(), 24, Day);
            PerishableLotState updated;
            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.RecordSpoilage(25, "mould", Day, "more than the lot holds", out updated),
                "cannot spoil more units than the lot holds");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.RecordSpoilage(0, "mould", Day, "no loss", out updated),
                "a spoilage record must record a positive loss");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => state.RecordSpoilage(-3, "mould", Day, "negative loss", out updated),
                "a negative loss is not a loss");
            Assert.AreEqual(24, state.Units, "the refused calls leave the state untouched");

            PerishableLotState splitState = BreadState(MillbrookKey(), 10, Day);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => splitState.Split(11, Day),
                "cannot split off more than the lot holds");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => splitState.Split(0, Day),
                "cannot split off nothing");
        }

        [Test]
        public void Chain_ConditionSurvivesTransfersAndPurchase_AcceptanceN_O()
        {
            // Acceptance N and O: a lot's condition survives transfer
            // and is not reset by purchase. Drive the real Millbrook to
            // household chain; the state is carried alongside each leg
            // (TransferredTo mirrors the custody legs) and its condition
            // dimensions are identical after every leg.
            var log = new CustodyTransferLog();
            Chain s = ChainAtBake(log);

            // Flour leg: received into the bakery bin on day 12. No
            // authored parameter, so the condition is Unknown at every
            // leg and stays Unknown.
            BakeryFlourLot flourLot = FlourLot(s);
            var flourKey = new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), flourLot.LotId.ToString());
            PerishableLotState flour = new PerishableLotState(flourKey, BakeryBreadCatalog.FlourItemId, 160, Day);
            flour = PerishableLotState.ApplyElapsedTime(flour, 0.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, flour.PhysicalCondition,
                "flour carries no authored condition at receipt");
            flour = flour.TransferredTo("bakery:" + BakeryId, Day, "freight receive into bin");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, flour.PhysicalCondition,
                "receipt does not invent a condition");
            (PerishableLotState flourRemaining, PerishableLotState flourDispensed) = flour.Split(12, Day);
            Assert.AreEqual(148, flourRemaining.Units, "160 - 12 dispensed stays behind");
            Assert.AreEqual(12, flourDispensed.Units, "12 lb dispensed into the batch");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, flourRemaining.PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, flourDispensed.PhysicalCondition,
                "the dispense splits quantity, never condition");

            // Bread leg: baked day 12, Fresh. Carry it through the
            // wholesale transfer and the household purchase — the
            // condition must not move except by elapsed simulation
            // time. (Day 13 is a non-trading day — day-of-week index 6
            // — so the purchase runs on day 12 like the F5 chain, and
            // the one-day age step is applied to the state after the
            // purchase instead: the purchase must neither reset the
            // condition nor disturb the age anchor.) The wholesale
            // delivery drains the bread lot itself to 0 — the 24 loaves
            // live in the store pool (ChainAtBake already pinned
            // UnitsStocked 24) — so the state carries the 24 delivered
            // loaves, keyed to the bread lot that baked them.
            BakeryBreadLot breadLot = s.Bakery.BreadShelf[0];
            Assert.AreEqual(0, breadLot.UnitCount,
                "the wholesale delivery drains the bread lot to 0 into the store pool");
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the 24 baked loaves sit in the store pool");
            var breadKey = new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), breadLot.LotId.ToString());
            PerishableLotState bread = BreadState(breadKey, 24, breadLot.BakedDayIndex);
            bread = PerishableLotState.ApplyElapsedTime(bread, 0.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, bread.PhysicalCondition,
                "baked today: Fresh by the authored rule");
            bread = bread.TransferredTo("business:" + StoreInstanceId, Day, "wholesale delivery to store");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, bread.PhysicalCondition,
                "the wholesale transfer does not touch the condition");

            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, trip.UnitsAcquired, "the 24 loaves sell");
            bread = bread.TransferredTo("household:" + HouseholdId, Day, "household receipt");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, bread.PhysicalCondition,
                "purchase does not reset the condition — Fresh stays Fresh (acceptance O)");
            Assert.AreEqual(24, bread.Units, "purchase moves the quantity unchanged through the state");
            HouseholdItemLot homeLot = HouseholdBreadLot(s);
            Assert.AreEqual(24, homeLot.QuantityUnits, "the household lot holds the 24 loaves");
            Assert.AreEqual(0, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the store pool is empty after selling its 24 loaves");

            // Aging after the purchase still follows the authored rule
            // from the same anchor: one simulated day later the lot is
            // DayOld — the purchase neither froze the clock nor reset
            // the age.
            bread = PerishableLotState.ApplyElapsedTime(bread, 1.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, bread.PhysicalCondition,
                "one day past baking: DayOld, even after the purchase");
            Assert.AreEqual(Day, bread.ProducedOrAcquiredDay,
                "the age anchor is still the bake day");

            // The custody log's chain agrees the transfers happened;
            // the condition state agrees nothing about the condition
            // changed while they did. (The household-receipt record's
            // CauseId is the carried batch id, not the receipt id, so
            // the check scans for the kind.)
            Assert.IsNotNull(log.LatestForCause(CustodyTransferLog.CauseFreightReceive, ShipmentId),
                "the freight receive is in the custody log");
            bool receiptRecorded = false;
            foreach (CustodyTransferRecord record in log.Records)
            {
                if (record != null && record.CauseKind == CustodyTransferLog.CauseHouseholdReceipt)
                    receiptRecorded = true;
            }
            Assert.IsTrue(receiptRecorded, "the household receipt is in the custody log");
        }

        [Test]
        public void Chain_QuantitiesAndSettlementsUnchanged_AcceptanceR()
        {
            // Acceptance R: the chain quantities and settlements match
            // the existing figures — 16-sack haul, 125c settlement,
            // 12 lb to 24 loaves — all asserted as unchanged by this
            // slice (the model only observes; nothing in the chain
            // consults it).
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);

            PlaceFreightOrder(s);
            Assert.AreEqual(Sacks, s.Lot.PackageUnits, "16-sack haul");
            Assert.AreEqual(160, s.Lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(768, s.Lot.TotalCostCents, "16 sacks x 48c");

            BuildFreightShipment(s);
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "freight charge debited from the bakery exactly once (125c)");
            Assert.AreEqual(carrierBefore + FreightChargeCents, s.CarrierRuntime.CurrentCashCents,
                "freight charge credited to the carrier exactly once (125c)");
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds the lot's 160 lb");

            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "12 lb flour -> 24 loaves");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour 160 -> 148 lb after the 12 lb dispense");
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the 24 baked loaves sit in the store pool");

            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            Assert.AreEqual(24, trip.UnitsAcquired, "the partial fill sells the 24 loaves on hand");
            Assert.AreEqual(120, trip.BuyerOutflowCents, "24 loaves x 5c");
            Assert.AreEqual(880, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 120c");
            Assert.AreEqual(24, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "24 bread units in household inventory");
        }

        [Test]
        public void ModelAndQueries_ChangeNothingInAnyLiveAuthority_AcceptanceQ()
        {
            // Acceptance Q: the model and its queries change nothing in
            // any live authority. Drive the chain to the bake, snapshot
            // every quantity and settlement, exercise the model hard on
            // standalone states, and snapshot again — identical.
            Chain s = ChainAtBake(null);

            int supplierShelfBefore = s.Supplier.GetInventoryUnits("bakery_flour_sack");
            int supplierCashBefore = s.Supplier.CashCents;
            int binFlourBefore = s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int notionsBefore = NotionsLot(s).Units;
            int storePoolBefore = s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits;
            int storeCashBefore = s.StoreRuntime.CurrentCashCents;
            int bakeryCashBefore = s.BakeryRuntime.CurrentCashCents;
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            int bakeryPortBefore = s.BakeryCash.BalanceCents;
            int breadShelfBefore = s.Bakery.BreadShelf.Count;
            int breadUnitsBefore = s.Bakery.BreadShelf[0].UnitCount;
            int flourLotsBefore = s.Bakery.FlourStock.Lots.Count;
            int shipmentsBefore = s.Freight.Service.TrackedShipments.Count;

            // Exercise the model: deterioration, preservation, spoilage
            // accounting, splits, transfers, queries, DTO round-trips.
            PerishableLotState bread = BreadState(
                new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), s.Bakery.BreadShelf[0].LotId.ToString()),
                24, Day);
            bread = PerishableLotState.ApplyElapsedTime(bread, 30.0, "ambient");
            bread = bread.ApplyPreservation("cool dark storage", Day + 30);
            PerishableLotState spoiled;
            SpoilageRecord record = bread.RecordSpoilage(24, "mould", Day + 30, "all condemned", out spoiled);
            Assert.AreEqual(24, record.UnitsLost);
            Assert.AreEqual(0, spoiled.Units);
            PerishableLotState flour = new PerishableLotState(
                new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), FlourLot(s).LotId.ToString()),
                BakeryBreadCatalog.FlourItemId, 148, Day);
            flour = PerishableLotState.ApplyElapsedTime(flour, 30.0, "ambient");
            (PerishableLotState kept, PerishableLotState moved) = flour.Split(12, Day);
            moved = moved.TransferredTo("batch:1", Day, "dispense");
            PerishableLotState.SaveDto dto = bread.CaptureSaveDto();
            PerishableLotState restored = PerishableLotState.FromSaveDto(dto);
            Assert.IsTrue(bread.EqualsState(restored), "the round-trip inside the Q leg agrees");

            Assert.AreEqual(supplierShelfBefore, s.Supplier.GetInventoryUnits("bakery_flour_sack"),
                "supplier shelf unchanged by the model");
            Assert.AreEqual(supplierCashBefore, s.Supplier.CashCents, "supplier cash unchanged");
            Assert.AreEqual(binFlourBefore, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bakery bin unchanged — spoilage accounting removes nothing from the authority");
            Assert.AreEqual(notionsBefore, NotionsLot(s).Units, "notions unchanged");
            Assert.AreEqual(storePoolBefore, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "store pool unchanged");
            Assert.AreEqual(storeCashBefore, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(bakeryCashBefore, s.BakeryRuntime.CurrentCashCents, "bakery runtime cash unchanged");
            Assert.AreEqual(payerBefore, s.PayerRuntime.CurrentCashCents, "payer cash unchanged");
            Assert.AreEqual(carrierBefore, s.CarrierRuntime.CurrentCashCents, "carrier cash unchanged");
            Assert.AreEqual(bakeryPortBefore, s.BakeryCash.BalanceCents, "bakery cash port unchanged");
            Assert.AreEqual(breadShelfBefore, s.Bakery.BreadShelf.Count, "no bread lot created or lost");
            Assert.AreEqual(breadUnitsBefore, s.Bakery.BreadShelf[0].UnitCount, "no loaf created or lost");
            Assert.AreEqual(flourLotsBefore, s.Bakery.FlourStock.Lots.Count, "no flour lot created or lost");
            Assert.AreEqual(shipmentsBefore, s.Freight.Service.TrackedShipments.Count, "no shipment created or lost");
            Assert.AreEqual(148, kept.Units + moved.Units,
                "the model's own split still conserves — but only inside the model");
        }

        [Test]
        public void SaveLoad_RoundTrip_ReproducesStateExactly_IncludingHistory()
        {
            // The state round-trips deterministically through its own
            // plain-data form: every field, including the append-only
            // handling history, comes back identical. No authority
            // carries the state yet (attachment is downstream), so the
            // round-trip is the state's own DTO pair.
            PerishableLotState state = BreadState(MillbrookKey(), 24, Day)
                .WithConditions(PerishablePhysicalCondition.DayOld, PerishablePhysicalCondition.Fresh)
                .WithSafety(PerishableSafetyState.Suspect, "odd smell reported");
            state = PerishableLotState.ApplyElapsedTime(state, 1.0, "ambient");
            state = state.ApplyPreservation("cool dark storage", Day + 1);
            state = state.TransferredTo("household:7", Day + 1, "carried home");
            PerishableLotState afterSpoilage;
            state.RecordSpoilage(2, "mould", Day + 1, "two loaves mouldy", out afterSpoilage);
            state = afterSpoilage;
            Assert.AreEqual(22, state.Units);
            Assert.AreEqual(3, state.HandlingHistory.Count,
                "preservation + transfer + spoilage events, in order");

            PerishableLotState.SaveDto dto = state.CaptureSaveDto();
            PerishableLotState restored = PerishableLotState.FromSaveDto(dto);
            Assert.IsTrue(state.EqualsState(restored),
                "the round-trip reproduces the state exactly, handling history included");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, restored.PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, restored.PerceivedCondition,
                "perceived and known stay separate across the round-trip");
            Assert.AreEqual(PerishableSafetyState.Suspect, restored.SafetyState);
            Assert.AreEqual("odd smell reported", restored.SafetyEvidence);
            Assert.AreEqual("cool dark storage", restored.PreservationMethod);
            Assert.IsTrue(restored.IsPreserved);
            Assert.AreEqual(3, restored.HandlingHistory.Count);
            Assert.AreEqual("preservation", restored.HandlingHistory[0].Kind);
            Assert.AreEqual("transfer", restored.HandlingHistory[1].Kind);
            Assert.AreEqual("spoilage", restored.HandlingHistory[2].Kind);
            Assert.AreEqual(22, restored.Units);

            // A second round-trip is stable, and the default DTO
            // restores to an empty-key state rather than a fabrication.
            PerishableLotState twice = PerishableLotState.FromSaveDto(restored.CaptureSaveDto());
            Assert.IsTrue(state.EqualsState(twice), "the round-trip is idempotent");
            PerishableLotState empty = PerishableLotState.FromSaveDto(new PerishableLotState.SaveDto());
            Assert.IsTrue(empty.Key.IsEmpty, "the default DTO carries no lot");
            Assert.AreEqual(0, empty.Units);
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, empty.PhysicalCondition);
            Assert.IsTrue(!empty.RemainingUsefulLife.IsKnown, "remaining life stays Unknown");
        }

        [Test]
        public void FromBakeryCondition_MapsOneToOne_UnspecifiedIsUnknown()
        {
            // The same 1:1 translation Slice 3 pinned for the read
            // model: Fresh/DayOld/Stale map to themselves; Unspecified
            // — the lot carries no condition — maps to Unknown. It
            // invents no condition for any lot.
            Assert.AreEqual(PerishablePhysicalCondition.Fresh,
                PerishableLotState.FromBakeryCondition(BakeryGoodsCondition.Fresh));
            Assert.AreEqual(PerishablePhysicalCondition.DayOld,
                PerishableLotState.FromBakeryCondition(BakeryGoodsCondition.DayOld));
            Assert.AreEqual(PerishablePhysicalCondition.Stale,
                PerishableLotState.FromBakeryCondition(BakeryGoodsCondition.Stale));
            Assert.AreEqual(PerishablePhysicalCondition.Unknown,
                PerishableLotState.FromBakeryCondition(BakeryGoodsCondition.Unspecified));
        }

        // ---- shared chain fixture: the real authorities, trimmed from
        // PhysicalInventorySaveLoadTests (F5) to the legs this slice
        // drives — Millbrook order, freight haul, Bakery bake, household
        // purchase and receipt. No meal leg, no feed leg, no save/load
        // bundle: this slice pins condition survival, not persistence. ----

        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }

            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents) return "test cash port refused debit";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (amountCents <= 0) return "test cash port refused credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        // Production port over a real BusinessRuntimeState, copied from
        // BakeryFoodChainEndToEndTests: the production GeneralStoreTradingPort
        // wraps a MonoBehaviour manager and cannot be constructed in Roslyn.
        private sealed class RuntimeStorePort : IGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int Cap = int.MaxValue;
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => Runtime != null ? Runtime.CurrentCashCents : 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }
            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }
            public int CategoryPricePerUnitCents(string categoryId)
            {
                return Prices.TryGetValue(categoryId, out int p) ? p : 0;
            }
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                if (Runtime != null) Runtime.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "no runtime";
                if (Runtime.CurrentCashCents < amountCents) return "cannot afford";
                Runtime.SpendCents(amountCents);
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "no runtime";
                if (units > Cap) return $"capped port refuses {units}u (cap {Cap})";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                if (Cap < int.MaxValue) return Math.Min(Cap, stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : Cap);
                return stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class Chain
        {
            public List<string> Diag = new List<string>();
            public CustodyTransferLog Log;
            public EntityIdRegistry Ids = new EntityIdRegistry();

            // Millbrook + freight.
            public OffMapAidBusinessRuntime Supplier;
            public FakeBusinessCashPort BakeryCash;
            public OffMapAidCargoLot Lot;
            public BakeryShopRuntime Bakery = new BakeryShopRuntime(BakeryId);
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public MillbrookFlourFreightCustodyAdapter Adapter;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentReservationService Reservations;
            public FreightProductionDriver Freight;
            public FreightShipmentHooks Hooks;
            public AnimalState Horse;
            public EquipmentAsset Wagon;
            public LogisticsShipmentState Shipment;
            public FreightServiceAgreement Agreement;
            public BusinessRuntimeState CarrierRuntime;
            public BusinessRuntimeState PayerRuntime;
            public JourneyModel JourneyModel;
            public OffMapJourneyAuthority Journeys;

            // Bakery production + store.
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public RuntimeStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
            public Series2BakeryProductionDriver ProductionDriver;

            // Shopping.
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel ShoppingJourneys;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public HouseholdShoppingLoop Loop;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            // Row values copied verbatim from the authored Millbrook asset
            // (10 lb / 18 bakery units / 48c / 230 starting), as in
            // MillbrookFlourFreightCustodyTests.
            var definition = ScriptableObject.CreateInstance<OffMapAidBusinessDefinition>();
            definition.BusinessId = "millbrook_grain_co";
            definition.DisplayName = "Millbrook Grain Co.";
            definition.PickupRouteMilesFromTown = 50f;
            definition.Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack", DisplayName = "Flour",
                    InventoryUnit = "lb", StartingUnits = 230,
                    OrderUnit = "10 lb sack", InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18, OrderPriceCents = 48,
                },
            };
            return definition;
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static Chain NewChain(CustodyTransferLog log)
        {
            var s = new Chain { Log = log };
            s.Supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            s.Supplier.CustodyLog = log;
            s.BakeryCash = new FakeBusinessCashPort(20000);
            s.Bakery.CustodyLog = log;
            s.Adapter = new MillbrookFlourFreightCustodyAdapter(s.Supplier, s.Bakery, s.Ids, log);

            // Freight (mirrors FreightDeliveryPaymentAuditTests.SetUp).
            s.Tasks = new TaskAuthority();
            s.Budgets = new WorkTimeBudgetStore();
            s.Budgets.EnsureDay(Day);
            s.Budgets.EnsureDay(Day + 1);
            s.Budgets.EnsureDay(Day + 2);
            s.Reservations = new EquipmentReservationService();
            s.Horse = new AnimalRegistry(s.Ids).RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Save/load fixture");
            Assert.IsNotNull(s.Horse);
            Assert.IsNull(HorseAuthority.Configure(s.Horse, HorseUseCapabilities.GeneralPurpose,
                "ethan-staging", "freight-company-stable", Day));
            s.Horse.TransportState = HorseTransportState.AttachedToWagon;
            s.Horse.AttachedWagonAssetId = WagonId;
            s.Wagon = new EquipmentAsset
            {
                AssetId = WagonId, Kind = "freight-wagon", DisplayName = "Opening Freight Wagon",
                Condition01 = 1f, OwnerKind = "business", OwnerId = CarrierId,
                LocationId = "ethan-staging", HitchPreparationShipmentId = ShipmentId,
                HitchingAnimalIds = new List<string> { s.Horse.AnimalId.ToString() }
            };
            Assert.IsNull(WagonAuthority.Configure(s.Wagon, WagonSubtype.GeneralFarmFreight.ToString(),
                450f, 4f, "ethan-staging"));
            s.CarrierRuntime = Runtime(CarrierId, 0);
            s.PayerRuntime = Runtime(BakeryId, 20000);
            s.Freight = new FreightProductionDriver();
            s.Freight.EnsureCompany(CarrierId, s.CarrierRuntime);
            s.Freight.RegisterBusinessRuntime(BakeryId, s.PayerRuntime);
            s.Freight.Service.RegisterWagon(s.Wagon);
            s.Hooks = new FreightShipmentHooks
            {
                Tasks = s.Tasks,
                Budgets = s.Budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                Reservations = s.Reservations,
                NowGameSeconds = Day * 86400f + 8 * 3600f,
                CommitSourceLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.CommitSourceLot(shipment, qty, out reason),
                ReceiveCargoLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.ReceiveCargoLot(shipment, qty, Day, out reason),
            };
            s.JourneyModel = new JourneyModel();
            Assert.IsNull(s.JourneyModel.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            RegionalTownRegistry.ApplyAll(s.JourneyModel, new List<string>());
            s.Journeys = new OffMapJourneyAuthority(s.JourneyModel);

            // Bakery production + store (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(5000);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.StoreRuntime.EnsureCategoryStock("animal_feed", 0, 0);
            s.BakeryRuntime.BindBusinessIdentity(BakeryId);
            s.BakeryRuntime.AddCashCents(2000);
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
            });
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));

            // Shopping (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.Population = new PopulationState();
            s.Population.people.Add(new PersonState
            {
                id = ShopperPersonId, firstName = "Glen", lastName = "Test", age = 34,
                ageBand = AgeBand.Adult18Plus, householdId = HouseholdId,
            });
            s.Population.households.Add(new HouseholdState { id = HouseholdId });
            s.Membership = new HouseholdMembershipRegistry();
            Assert.IsNull(s.Membership.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = ShopperPersonId, HouseholdId = HouseholdId, StartDayIndex = 0,
            }));
            s.Ledgers = new HouseholdLedgerRegistry();
            s.Inventories = new HouseholdInventoryRegistry();
            s.Needs = new HouseholdNeedRegistry();
            s.ShoppingJourneys = new JourneyModel();
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            Assert.IsNull(s.ShoppingJourneys.AddEdge("home", "general-store", 2.0f, "river road"));
            // One shared TaskAuthority serves the freight service and the
            // shopping loop, as in production.
            HouseholdShoppingLoop.RegisterTaskDefinitions(s.Tasks);
            s.Tracker = new PersonScheduleTracker();
            s.Knowledge = new ShopperSellerKnowledge();
            s.Directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(s.Directory, s.StorePort);
            s.Knowledge.LearnSupplier(ShopperPersonId, StoreInstanceId, "lives in the same town", 0);
            s.Loop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.ShoppingJourneys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            s.Loop.CustodyLog = log;
            s.PersonLocations[ShopperPersonId] = "home";
            return s;
        }

        private static void PlaceFreightOrder(Chain s)
        {
            Assert.IsNull(s.Supplier.TryPlaceOrder(
                OrderRequestId, BakeryId, "Glen Pierce", Sacks, s.BakeryCash, 0, out OffMapAidCargoLot lot),
                "fixture order must place");
            s.Lot = lot;
            Assert.AreEqual(LotId, lot.LotId);
            Assert.AreEqual(Sacks, lot.PackageUnits);
            Assert.AreEqual(Sacks * 10, lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(Sacks * 48, lot.TotalCostCents, "16 sacks x 48c");
        }

        private static void BuildFreightShipment(Chain s)
        {
            FreightServiceAgreementBook book = s.Freight.ServiceAgreements;
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = LotId, Quantity = Sacks, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }));
            Assert.IsNull(book.Accept(request.RequestId, EthanId, Day, 40000f, out s.Agreement));
            s.Shipment = s.Freight.CreateShipment(CarrierId, ShipmentId, "millbrook_grain_co", BakeryId, Sacks,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                s.Hooks, EntityId.For(EntityKind.Person, EthanId), new List<EntityId> { s.Horse.AnimalId },
                FreightChargeCents, BakeryId, s.Diag, out string shipmentRefusal,
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(s.Shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(s.Agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(s.Agreement.AgreementId, ShipmentId, 50000f));
            s.Freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { s.Horse.AnimalId });
        }

        private static void BindAndAuthorize(Chain s)
        {
            string bind = WagonAuthority.TryAssignDraftTeam(s.Wagon,
                new List<AnimalState> { s.Horse },
                EntityId.For(EntityKind.Person, EthanId).ToString(), ShipmentId,
                new AnimalAvailabilityAuthority(s.Reservations), Day * 86400f + 8 * 3600f, float.MaxValue);
            Assert.IsNull(bind, "fixture bind must succeed");
            Assert.IsTrue(s.Freight.AuthorizeShipmentDispatch(ShipmentId, out _), "fixture authorization must succeed");
        }

        private static List<LogisticsShipmentStatus> TickToCompleted(Chain s)
        {
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && s.Shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                s.Freight.AdvanceTick(86400f, s.Hooks, EntityId.For(EntityKind.Person, EthanId), s.Diag);
                trace.Add(s.Shipment.Status);
            }
            return trace;
        }

        private static void DriveFreightToReturnDay(Chain s)
        {
            OffMapJourneyRecord record = s.Journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { AgreementId },
                10, null, null, s.Diag);
            Assert.IsNotNull(record, "Millbrook departure should succeed: " + string.Join("; ", s.Diag));
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = Sacks;

            // The D+1 pickup's first half, as the drive performs it: commit
            // the lot through the adapter (day 11), then apply the loading
            // writes — exactly WorkLoading's post-commit writes, no task.
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, s.Shipment.PlannedQuantityUnits, out string reason, 11),
                reason);
            s.Shipment.sourceLotDebitApplied = true;
            s.Shipment.loadApplied = true;
            s.Shipment.sourceCommittedAtSchedule = true;
            s.Shipment.remainingQuantityUnits = s.Shipment.PlannedQuantityUnits;
            s.Shipment.physicalLocationId = "wagon:" + WagonId;

            BindAndAuthorize(s);
            List<OffMapJourneyRecord> returned = s.Journeys.CompleteDueJourneys(Day, null, s.Diag);
            Assert.AreEqual(1, returned.Count, "the journey must complete exactly once on its due day");
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);
            s.Hooks.IsVehicleOffMap = _ => false;
        }

        private static Chain ChainAtReceive(CustodyTransferLog log)
        {
            Chain s = NewChain(log);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds the lot's 160 lb");
            return s;
        }

        private static Chain ChainAtBake(CustodyTransferLog log)
        {
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "24 loaves stocked to the live staple_food pool");
            return s;
        }

        private static void InstallOven(Chain s)
        {
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
        }

        private static void SeedNotionsAndFuel(Chain s)
        {
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
        }

        private static void BookAccount(Chain s, int dueDay)
        {
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                PricePerUnitCents = 2,
                Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                NextDeliveryDayIndex = dueDay,
            };
            Assert.IsNull(s.Bakery.AddWholesaleAccount(account, s.Diag), "account booking should succeed");
        }

        private static BakeryWholesaleDailyInputs InputsFor(Chain s, int dayIndex) =>
            new BakeryWholesaleDailyInputs
            {
                Bakery = s.Bakery,
                ProductionDriver = s.ProductionDriver,
                DayIndex = dayIndex,
                BakerKitUsable = true,
                OvenDefinition = WorkstationCatalog.BakeOven,
                FindOvenComponent = OvenFinder(),
                IdRegistry = s.Ids,
                StorePort = s.StorePort,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryId,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreRuntimeState = s.StoreRuntime,
                BakeryRuntimeState = s.BakeryRuntime,
                StoreFlourUnitPriceCents = 29,
                MillbrookQuote = new MillbrookFlourQuote
                {
                    SacksAvailable = 0,
                    GoodsPricePerSackCents = null,
                    OneWayMiles = 50f,
                    FreightQuoteCents = 125,
                    FreightAvailable = false,
                    FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed",
                },
                MillbrookSupplier = null,
                ImportFlourPricePerLbCents = 3,
                ImportTransitDays = 14,
                ImportOrderPathExists = false,
            };

        private static ShoppingLoopOptions ShoppingOptions(Chain s) =>
            new ShoppingLoopOptions
            {
                PersonLocationId = pid => s.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                SetPersonLocation = (pid, loc) => s.PersonLocations[pid] = loc,
                DepartureMinuteOfDay = 600,
                InStoreMinutes = 15,
                Diagnostics = s.Diag,
            };

        private static void FundHousehold(Chain s, int cents)
        {
            HouseholdLedger ledger = s.Ledgers.GetOrCreate(HouseholdId);
            Assert.IsNull(ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));
        }

        private static ShoppingTripResult BuyBread(Chain s, int units, int dayIndex)
        {
            HouseholdPurchasingNeed need = s.Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, dayIndex);
            need.UnitsNeeded = units;
            need.Urgency01 = 0.9f;
            need.ReasonSummary = "test need: bread for meals";
            return s.Loop.ExecuteNeed(need, dayIndex, ShoppingOptions(s));
        }

        // The flour bin holds flour AND notions lots: find a lot by name
        // instead of indexing Lots[0].
        private static BakeryFlourLot FlourLot(Chain s) => FlourLot(s.Bakery);

        private static BakeryFlourLot FlourLot(BakeryShopRuntime bakery)
        {
            BakeryFlourLot found = null;
            int flourCount = 0;
            foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                {
                    flourCount++;
                    found = lot;
                }
            }
            Assert.AreEqual(1, flourCount, "exactly one flour lot is expected in the bin");
            Assert.IsNotNull(found);
            return found;
        }

        private static BakeryFlourLot NotionsLot(Chain s) => NotionsLot(s.Bakery);

        private static BakeryFlourLot NotionsLot(BakeryShopRuntime bakery)
        {
            BakeryFlourLot found = null;
            foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.NotionsItemId, StringComparison.Ordinal))
                    found = lot;
            }
            Assert.IsNotNull(found, "exactly one notions lot is expected in the bin");
            return found;
        }

        private static HouseholdItemLot HouseholdBreadLot(Chain s) =>
            HouseholdBreadLot(s.Inventories);

        private static HouseholdItemLot HouseholdBreadLot(HouseholdInventoryRegistry registries)
        {
            HouseholdItemLot found = null;
            foreach (HouseholdItemLot lot in registries.GetOrCreate(HouseholdId).Lots)
            {
                if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    found = lot;
            }
            Assert.IsNotNull(found, "exactly one household bread lot is expected");
            return found;
        }
    }
}
