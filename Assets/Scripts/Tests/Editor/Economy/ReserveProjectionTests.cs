// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice 6 — adaptive reserve projection tests (directive P4,
// P9 plan Slice 6, Priority F). Pins: the projection adapts with recent
// demand (rises when demand rises, falls when it falls — no fixed 12-loaf
// or 24-loaf reserve anywhere), forecast spoilage discounts aging loaves
// from usable stock (a lot past its useful life or in an unsafe state
// counts as zero), the projection names every input that moved it and says
// Unknown where an input is Unknown, usable stock / incoming deliveries /
// production capacity reduce the reserve, the storage shortfall is named
// rather than silently applied, the reserve never goes negative, and the
// observation-to-inputs bridge derives recent demand and uncertainty from
// recorded history only. The projection is a recommendation: nothing
// orders, nothing reserves stock in any live authority, and no input is
// hardcoded — every input is passed in.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class ReserveProjectionTests
    {
        private const string StoreId = "general-store-1";

        private static ReserveInputs BaseInputs(int demandPerDay = 10, double leadTimeDays = 2.0)
        {
            return new ReserveInputs
            {
                RecentDemandPerDay = ReserveQuantity.Known(demandPerDay),
                DemandUncertaintyUnits = ReserveQuantity.Unknown,
                ActualUsableStockUnits = ReserveQuantity.Known(0),
                ForecastSpoilageUnits = ReserveQuantity.Known(0),
                ExistingCommitmentsUnits = ReserveQuantity.Known(0),
                ConfirmedIncomingDeliveriesUnits = ReserveQuantity.Known(0),
                ProductionCapacityPerDay = ReserveQuantity.Unknown,
                ReplenishmentLeadTimeDays = LeadTimeDays.Known(leadTimeDays),
                AvailableStorageUnits = ReserveQuantity.Unknown,
            };
        }

        private static PerishableLotState Lot(string lotId, string itemId, int units,
            PerishablePhysicalCondition condition = PerishablePhysicalCondition.Fresh,
            PerishableSafetyState safety = PerishableSafetyState.Safe,
            UsefulLifeEstimate remainingLife = default(UsefulLifeEstimate))
        {
            var state = new PerishableLotState(
                new LotKey("bakery:bakery_001", lotId), itemId, units, 0);
            state = state.WithConditions(condition, condition);
            state = state.WithSafety(safety,
                safety == PerishableSafetyState.Unsafe ? "test evidence: unsafe" : string.Empty);
            if (remainingLife.IsKnown)
            {
                // Remaining life is set at construction only through the
                // DTO round-trip: capture, edit the DTO field, restore.
                PerishableLotState.SaveDto dto = state.CaptureSaveDto();
                dto.RemainingLifeKnown = true;
                dto.RemainingLifeDays = remainingLife.Days;
                state = PerishableLotState.FromSaveDto(dto);
            }
            return state;
        }

        private static string Reasoning(ReserveProjection projection) =>
            string.Join(" | ", projection.Reasoning);

        [Test]
        public void RisesWhenRecentDemandRises()
        {
            ReserveProjection low = ReserveProjection.Compute(BaseInputs(demandPerDay: 10));
            ReserveProjection high = ReserveProjection.Compute(BaseInputs(demandPerDay: 30));
            Assert.IsTrue(low.RecommendedReserveUnits.IsKnown);
            Assert.IsTrue(high.RecommendedReserveUnits.IsKnown);
            Assert.AreEqual(20, low.RecommendedReserveUnits.Units, "10/day over a 2-day lead time covers 20");
            Assert.AreEqual(60, high.RecommendedReserveUnits.Units, "30/day over a 2-day lead time covers 60");
            Assert.IsTrue(high.RecommendedReserveUnits.Units > low.RecommendedReserveUnits.Units,
                "the reserve target adapts with demand — it is recomputed, never a stored constant");
            Assert.IsTrue(Reasoning(high).Contains("demand"), "the reasoning names the demand cover: " + Reasoning(high));
        }

        [Test]
        public void FallsWhenRecentDemandFalls_NoFixedReserve()
        {
            ReserveProjection high = ReserveProjection.Compute(BaseInputs(demandPerDay: 30));
            ReserveProjection low = ReserveProjection.Compute(BaseInputs(demandPerDay: 5));
            Assert.AreEqual(60, high.RecommendedReserveUnits.Units);
            Assert.AreEqual(10, low.RecommendedReserveUnits.Units, "5/day over a 2-day lead time covers 10");
            Assert.IsTrue(low.RecommendedReserveUnits.Units < high.RecommendedReserveUnits.Units,
                "the reserve falls when demand falls");
            Assert.IsTrue(low.RecommendedReserveUnits.Units != 12
                && low.RecommendedReserveUnits.Units != 24
                && high.RecommendedReserveUnits.Units != 12
                && high.RecommendedReserveUnits.Units != 24,
                "no fixed 12-loaf or 24-loaf reserve: the number follows demand, and neither demand gives 12 or 24 here");
            ReserveProjection mid = ReserveProjection.Compute(BaseInputs(demandPerDay: 12));
            Assert.AreEqual(24, mid.RecommendedReserveUnits.Units,
                "24 here is 12/day over 2 days — arithmetic, not a constant: change the demand and it moves");
            ReserveProjection moved = ReserveProjection.Compute(BaseInputs(demandPerDay: 13));
            Assert.AreEqual(26, moved.RecommendedReserveUnits.Units, "13/day over 2 days covers 26, not 24");
        }

        [Test]
        public void SpoilageDiscountsAgingLoaves_UsableStock()
        {
            var lots = new List<PerishableLotState>
            {
                // 24 fresh loaves with no authored remaining life: counted.
                Lot("lot-fresh", "bake.bread-loaf", 24),
                // 24 loaves past their useful life: zero usable stock.
                Lot("lot-aged", "bake.bread-loaf", 24,
                    PerishablePhysicalCondition.Stale, PerishableSafetyState.Safe,
                    UsefulLifeEstimate.Known(0.0)),
                // 10 loaves in an unsafe state: zero usable stock.
                Lot("lot-unsafe", "bake.bread-loaf", 10,
                    PerishablePhysicalCondition.Fresh, PerishableSafetyState.Unsafe),
                // 6 suspect loaves: counted, with the caution named.
                Lot("lot-suspect", "bake.bread-loaf", 6,
                    PerishablePhysicalCondition.DayOld, PerishableSafetyState.Suspect),
            };
            UsableStockAssessment assessment = ReserveProjection.UsableStockFromLots(lots);
            Assert.AreEqual(30, assessment.UsableUnits,
                "24 fresh + 6 suspect count; the 24 past-life and 10 unsafe loaves count as zero");
            string reasons = string.Join(" | ", assessment.PerLotReasons);
            Assert.IsTrue(reasons.Contains("past its useful life"), "the aged lot names its discount: " + reasons);
            Assert.IsTrue(reasons.Contains("unsafe"), "the unsafe lot names its discount: " + reasons);
            Assert.IsTrue(reasons.Contains("suspect"), "the suspect lot names its caution: " + reasons);

            var agedOnly = new List<PerishableLotState>
            {
                Lot("lot-aged", "bake.bread-loaf", 24,
                    PerishablePhysicalCondition.Stale, PerishableSafetyState.Safe,
                    UsefulLifeEstimate.Known(0.0)),
            };
            Assert.AreEqual(0, ReserveProjection.UsableStockFromLots(agedOnly).UsableUnits,
                "24 aging loaves past their useful life do not count as 24 future sellable loaves");
        }

        [Test]
        public void UnknownLeadTime_SaysUnknown()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.ReplenishmentLeadTimeDays = LeadTimeDays.Unknown;
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.IsTrue(!projection.RecommendedReserveUnits.IsKnown,
                "with no authored lead time the projection says Unknown instead of inventing one");
            Assert.AreEqual(ReserveConfidence.Unknown, projection.Confidence);
            Assert.IsTrue(Reasoning(projection).Contains("lead time"),
                "the reasoning names the missing lead time: " + Reasoning(projection));
        }

        [Test]
        public void UnknownDemand_SaysUnknown()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.RecentDemandPerDay = ReserveQuantity.Unknown;
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.IsTrue(!projection.RecommendedReserveUnits.IsKnown,
                "with no observed demand the projection says Unknown instead of inventing a demand");
            Assert.AreEqual(ReserveConfidence.Unknown, projection.Confidence);
            Assert.IsTrue(Reasoning(projection).Contains("demand"),
                "the reasoning names the missing demand: " + Reasoning(projection));
        }

        [Test]
        public void UnknownUncertainty_NamedNotInvented()
        {
            ReserveProjection projection = ReserveProjection.Compute(BaseInputs(demandPerDay: 10));
            Assert.IsTrue(projection.RecommendedReserveUnits.IsKnown);
            Assert.AreEqual(20, projection.RecommendedReserveUnits.Units,
                "with no measured uncertainty the buffer is zero and the cover stands alone");
            Assert.AreEqual(ReserveConfidence.Reduced, projection.Confidence,
                "an unknown input that could move the number drops confidence");
            Assert.IsTrue(Reasoning(projection).Contains("uncertainty"),
                "the reasoning names the unknown uncertainty instead of silently adding a buffer: "
                + Reasoning(projection));
            Assert.IsTrue(!projection.UncertaintyBandUnits.IsKnown,
                "the uncertainty band is Unknown when uncertainty was never measured");
        }

        [Test]
        public void Reasoning_NamesEveryInputThatMovedIt()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.DemandUncertaintyUnits = ReserveQuantity.Known(5);
            inputs.ActualUsableStockUnits = ReserveQuantity.Known(4);
            inputs.ForecastSpoilageUnits = ReserveQuantity.Known(3);
            inputs.ExistingCommitmentsUnits = ReserveQuantity.Known(2);
            inputs.ConfirmedIncomingDeliveriesUnits = ReserveQuantity.Known(6);
            inputs.ProductionCapacityPerDay = ReserveQuantity.Known(1);
            inputs.AvailableStorageUnits = ReserveQuantity.Known(100);
            // Cover 20 + buffer 5 + commitments 2 + spoilage 3 = 30 gross;
            // minus usable 4, incoming 6, producible 2 (1/day x 2 days) = 18.
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.IsTrue(projection.RecommendedReserveUnits.IsKnown);
            Assert.AreEqual(18, projection.RecommendedReserveUnits.Units);
            Assert.AreEqual(ReserveConfidence.High, projection.Confidence,
                "every input Known means full confidence");
            Assert.AreEqual(5, projection.UncertaintyBandUnits.Units, "the band is the measured buffer");
            string reasoning = Reasoning(projection);
            Assert.IsTrue(reasoning.Contains("demand"), "demand cover named: " + reasoning);
            Assert.IsTrue(reasoning.Contains("uncertainty"), "uncertainty buffer named: " + reasoning);
            Assert.IsTrue(reasoning.Contains("commitment"), "commitments named: " + reasoning);
            Assert.IsTrue(reasoning.Contains("spoilage"), "forecast spoilage named: " + reasoning);
            Assert.IsTrue(reasoning.Contains("usable stock"), "usable stock named: " + reasoning);
            Assert.IsTrue(reasoning.Contains("incoming"), "incoming deliveries named: " + reasoning);
            Assert.IsTrue(reasoning.Contains("production"), "production capacity named: " + reasoning);
            Assert.IsTrue(reasoning.Contains("storage"), "available storage named: " + reasoning);
            Assert.IsTrue(projection.FitsAvailableStorage, "18 fits in 100 of storage");
        }

        [Test]
        public void UsableStockAndIncoming_ReduceReserve()
        {
            ReserveProjection bare = ReserveProjection.Compute(BaseInputs(demandPerDay: 10));
            Assert.AreEqual(20, bare.RecommendedReserveUnits.Units);

            ReserveInputs stocked = BaseInputs(demandPerDay: 10);
            stocked.ActualUsableStockUnits = ReserveQuantity.Known(15);
            Assert.AreEqual(5, ReserveProjection.Compute(stocked).RecommendedReserveUnits.Units,
                "15 usable loaves on hand cover 15 of the 20");

            ReserveInputs incoming = BaseInputs(demandPerDay: 10);
            incoming.ConfirmedIncomingDeliveriesUnits = ReserveQuantity.Known(8);
            Assert.AreEqual(12, ReserveProjection.Compute(incoming).RecommendedReserveUnits.Units,
                "8 confirmed incoming loaves cover 8 of the 20");

            ReserveInputs both = BaseInputs(demandPerDay: 10);
            both.ActualUsableStockUnits = ReserveQuantity.Known(15);
            both.ConfirmedIncomingDeliveriesUnits = ReserveQuantity.Known(8);
            Assert.AreEqual(0, ReserveProjection.Compute(both).RecommendedReserveUnits.Units,
                "15 on hand plus 8 incoming cover the whole 20, floored at zero");
        }

        [Test]
        public void ProductionCapacity_ReducesReserve()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.ProductionCapacityPerDay = ReserveQuantity.Known(5);
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.AreEqual(10, projection.RecommendedReserveUnits.Units,
                "5 loaves/day producible over the 2-day lead time cover 10 of the 20");
            Assert.IsTrue(Reasoning(projection).Contains("production"),
                "the reasoning names the production credit: " + Reasoning(projection));
        }

        [Test]
        public void StorageShortfall_Named()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.AvailableStorageUnits = ReserveQuantity.Known(12);
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.AreEqual(20, projection.RecommendedReserveUnits.Units,
                "the demand-derived need stays 20 — the storage limit is reported, not silently applied");
            Assert.IsTrue(!projection.FitsAvailableStorage, "20 does not fit in 12 of storage");
            Assert.IsTrue(projection.StorageShortfallUnits.IsKnown);
            Assert.AreEqual(8, projection.StorageShortfallUnits.Units, "20 needed, 12 fit: shortfall 8");
            Assert.IsTrue(Reasoning(projection).Contains("storage"),
                "the reasoning names the storage shortfall: " + Reasoning(projection));
            Assert.AreEqual(ReserveConfidence.Reduced, projection.Confidence,
                "a storage shortfall drops confidence");
        }

        [Test]
        public void ZeroFloor_NeverNegative()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 1);
            inputs.ActualUsableStockUnits = ReserveQuantity.Known(100);
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.AreEqual(0, projection.RecommendedReserveUnits.Units,
                "a reserve is never negative: overstocked means zero to hold, not a negative target");
        }

        [Test]
        public void ForecastSpoilage_RaisesReserve()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.ForecastSpoilageUnits = ReserveQuantity.Known(4);
            Assert.AreEqual(24, ReserveProjection.Compute(inputs).RecommendedReserveUnits.Units,
                "4 loaves expected to spoil must be covered on top of the 20");
        }

        [Test]
        public void ExistingCommitments_RaiseReserve()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.ExistingCommitmentsUnits = ReserveQuantity.Known(3);
            Assert.AreEqual(23, ReserveProjection.Compute(inputs).RecommendedReserveUnits.Units,
                "3 committed loaves must be covered on top of the 20");
        }

        [Test]
        public void Deterministic_SameInputsSameOutputs()
        {
            ReserveInputs inputs = BaseInputs(demandPerDay: 10);
            inputs.DemandUncertaintyUnits = ReserveQuantity.Known(5);
            inputs.ActualUsableStockUnits = ReserveQuantity.Known(4);
            ReserveProjection first = ReserveProjection.Compute(inputs);
            ReserveProjection second = ReserveProjection.Compute(inputs);
            Assert.AreEqual(first.RecommendedReserveUnits.Units, second.RecommendedReserveUnits.Units);
            Assert.AreEqual(first.Confidence, second.Confidence);
            Assert.AreEqual(first.Reasoning.Count, second.Reasoning.Count);
            for (int i = 0; i < first.Reasoning.Count; i++)
            {
                Assert.AreEqual(first.Reasoning[i], second.Reasoning[i], "reasoning is deterministic");
            }
            Assert.AreEqual(10, inputs.RecentDemandPerDay.Units,
                "computing a projection never mutates its inputs");
            Assert.AreEqual(4, inputs.ActualUsableStockUnits.Units);
        }

        [Test]
        public void FromObservation_Bridge()
        {
            var book = new DemandObservation();
            // Seven days of steady demand: 10 loaves a day.
            for (int day = 0; day < 7; day++)
            {
                Assert.IsTrue(book.Record(new DemandAttemptRecord(day, StoreId,
                    HouseholdItemCatalog.BreadId, 7, "household:7/need:" + day, string.Empty,
                    10, 10, false, string.Empty), out string diag), "day " + day + ": " + diag);
            }
            ReserveInputs inputs = ReserveInputs.FromObservation(book, StoreId,
                HouseholdItemCatalog.BreadId, 7, 6, out string diagnostic);
            Assert.IsTrue(inputs.RecentDemandPerDay.IsKnown, "seven days of history bridge: " + diagnostic);
            Assert.AreEqual(10, inputs.RecentDemandPerDay.Units, "average daily demand over the window");
            Assert.IsTrue(inputs.DemandUncertaintyUnits.IsKnown, "steady demand has a measured (zero) spread");
            Assert.AreEqual(0, inputs.DemandUncertaintyUnits.Units);
            Assert.IsTrue(!inputs.ReplenishmentLeadTimeDays.IsKnown,
                "lead time has no authored value anywhere — the bridge does not invent one");
            Assert.IsTrue(!inputs.ActualUsableStockUnits.IsKnown,
                "usable stock is not derivable from the observation book alone");

            var empty = new DemandObservation();
            ReserveInputs noHistory = ReserveInputs.FromObservation(empty, StoreId,
                HouseholdItemCatalog.BreadId, 7, 6, out string noDiag);
            Assert.IsTrue(!noHistory.RecentDemandPerDay.IsKnown,
                "no history means no demand rate: " + noDiag);
            Assert.IsTrue(!noHistory.DemandUncertaintyUnits.IsKnown, "no history means no measured spread");
            Assert.IsTrue(noDiag.Contains("history"), "the bridge names the gap: " + noDiag);

            // The bridged inputs compute: 10/day, but no lead time, so the
            // projection still says Unknown rather than guessing a lead time.
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.IsTrue(!projection.RecommendedReserveUnits.IsKnown);
            inputs.ReplenishmentLeadTimeDays = LeadTimeDays.Known(2.0);
            Assert.AreEqual(20, ReserveProjection.Compute(inputs).RecommendedReserveUnits.Units,
                "with a supplied 2-day lead time the bridged demand covers 20");
        }
    }
}
