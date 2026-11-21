// Land & Ledgers — Household Survival and Food Security Monitor tests (Lane 7, Tool 08).
//
// Known cases reproduced from the current tree's source-reviewed
// evidence (no run exists on this HEAD): the Agent L §4 7-point audit,
// the G-11 full shopping chain (the 5-day run's "42 meals, zero
// missed" is NOT a shopping success), and the poverty-vs-mechanics
// separation with Lane 9a reasons and Lane 9b real affordability.

using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class HouseholdSurvivalMonitorTests
    {
        private static DailyLifecycleSnapshot HealthyDay(int day)
        {
            return new DailyLifecycleSnapshot
            {
                DayIndex = day,
                HouseholdCount = 8,
                HouseholdsWithReserveResolutionToday = 8,
                TownPulseLastResolvedDayIndex = day,
                HouseholdsWithFreshDemandSnapshot = 8,
                IsWeekBoundary = false,
                WeeklyUpgradeEffectsRanThisWeek = true,
                NpcWeeklySettlementRanThisWeek = true,
            };
        }

        private static HouseholdFoodSnapshot Household(int id)
        {
            return new HouseholdFoodSnapshot
            {
                HouseholdId = id,
                HouseholdLabel = "Household " + id,
                CashCents = 40000,
                FoodDaysOfSupply = 14f,
                HeatingFuelUnits = 20,
                HeatingFuelDaysOfSupply = 10f,
                CanAffordFoodTopUp = true,
                FoodTopUpCostCents = 500,
                KnownSellerCount = 1,
                SellerStockKnownPresent = true,
            };
        }

        private static MissedMealObservation Missed(int day, string slot, MissedMealReasonKind kind)
        {
            return new MissedMealObservation { DayIndex = day, Slot = slot, ReasonKind = kind, Reason = kind.ToString() };
        }

        // --- Agent L §4 lifecycle audit -------------------------------------------

        [Test]
        public void EvaluateLifecycle_HealthyDay_HasNoFindings()
        {
            List<LifecycleFinding> findings = HouseholdSurvivalMonitor.EvaluateLifecycle(HealthyDay(12));
            Assert.AreEqual(0, findings.Count);
        }

        [Test]
        public void EvaluateLifecycle_ReserveResolutionMissed_IsCritical_NamingResolveDailyUse()
        {
            DailyLifecycleSnapshot snapshot = HealthyDay(12);
            snapshot.HouseholdsWithReserveResolutionToday = 0;
            List<LifecycleFinding> findings = HouseholdSurvivalMonitor.EvaluateLifecycle(snapshot);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(LifecycleCheckKind.HeatingReserveResolutionRan, findings[0].Check);
            Assert.AreEqual(IncidentSeverity.Critical, findings[0].Severity);
            Assert.IsTrue(findings[0].Authority.Contains("ResolveDailyUse"));
            Assert.IsTrue(findings[0].Cause.Contains("L-01"));
        }

        [Test]
        public void EvaluateLifecycle_TownPulseUnresolved_IsWarning_NamingBeginDailyResolution()
        {
            DailyLifecycleSnapshot snapshot = HealthyDay(12);
            snapshot.TownPulseLastResolvedDayIndex = -1;
            List<LifecycleFinding> findings = HouseholdSurvivalMonitor.EvaluateLifecycle(snapshot);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(LifecycleCheckKind.TownPulseResolved, findings[0].Check);
            Assert.AreEqual(IncidentSeverity.Warning, findings[0].Severity);
            Assert.IsTrue(findings[0].Authority.Contains("BeginDailyResolution"));
        }

        [Test]
        public void EvaluateLifecycle_WeeklyBoundaryChecks_BothFire()
        {
            DailyLifecycleSnapshot snapshot = HealthyDay(14);
            snapshot.IsWeekBoundary = true;
            snapshot.WeeklyUpgradeEffectsRanThisWeek = false;
            snapshot.NpcWeeklySettlementRanThisWeek = false;
            List<LifecycleFinding> findings = HouseholdSurvivalMonitor.EvaluateLifecycle(snapshot);
            Assert.AreEqual(2, findings.Count);
            Assert.AreEqual(IncidentSeverity.Warning, findings[0].Severity);
            Assert.AreEqual(LifecycleCheckKind.WeeklyUpgradeEffectsRan, findings[0].Check);
            Assert.AreEqual(IncidentSeverity.Critical, findings[1].Severity);
            Assert.AreEqual(LifecycleCheckKind.NpcWeeklySettlementRan, findings[1].Check);
        }

        [Test]
        public void EvaluateLifecycle_StaleSignalRead_NamesTheField()
        {
            DailyLifecycleSnapshot snapshot = HealthyDay(12);
            snapshot.StaleSignalReads = new List<string> { "LastDailyReserveOffMapUnits" };
            List<LifecycleFinding> findings = HouseholdSurvivalMonitor.EvaluateLifecycle(snapshot);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(LifecycleCheckKind.NoStaleZeroSignalConsumed, findings[0].Check);
            Assert.IsTrue(findings[0].Title.Contains("LastDailyReserveOffMapUnits"));
        }

        [Test]
        public void EvaluateLifecycle_DoublePurchase_IsCritical_NamingHousehold()
        {
            DailyLifecycleSnapshot snapshot = HealthyDay(12);
            snapshot.DoublePurchaseHouseholdIds = new List<int> { 5 };
            List<LifecycleFinding> findings = HouseholdSurvivalMonitor.EvaluateLifecycle(snapshot);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(LifecycleCheckKind.NoDoublePurchase, findings[0].Check);
            Assert.AreEqual(IncidentSeverity.Critical, findings[0].Severity);
            Assert.IsTrue(findings[0].Title.Contains("5"));
        }

        [Test]
        public void EvaluateLifecycle_NullSnapshot_IsUnknown_NeverAPass()
        {
            List<LifecycleFinding> findings = HouseholdSurvivalMonitor.EvaluateLifecycle(null);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(IncidentSeverity.Unknown, findings[0].Severity);
        }

        // --- G-11 full shopping chain ----------------------------------------------

        [Test]
        public void FiveDayRun_MealsWithoutChain_IsNotAShoppingSuccess()
        {
            // The 5-day run's "42 meals, zero missed" proves none of the chain:
            // meals eaten with no observed shopping stages is NotAttempted, never Success.
            HouseholdFoodSnapshot snapshot = Household(1);
            string missing;
            Assert.AreEqual(ShoppingChainVerdict.NotAttempted,
                HouseholdSurvivalMonitor.EvaluateShoppingChain(snapshot, out missing));
            Assert.AreEqual(string.Empty, missing);
        }

        [Test]
        public void ShoppingChain_FiveOfSixStages_IsIncomplete_NamingMissingStage()
        {
            HouseholdFoodSnapshot snapshot = Household(1);
            snapshot.Departed = true;
            snapshot.Purchased = true;
            snapshot.Paid = true;
            snapshot.Returned = true;
            snapshot.PantryReceived = true;
            string missing;
            Assert.AreEqual(ShoppingChainVerdict.IncompleteChain,
                HouseholdSurvivalMonitor.EvaluateShoppingChain(snapshot, out missing));
            Assert.AreEqual("later consumption", missing);
        }

        [Test]
        public void ShoppingChain_AllSixStages_IsSuccess()
        {
            HouseholdFoodSnapshot snapshot = Household(1);
            snapshot.Departed = true;
            snapshot.Purchased = true;
            snapshot.Paid = true;
            snapshot.Returned = true;
            snapshot.PantryReceived = true;
            snapshot.LaterConsumed = true;
            string missing;
            Assert.AreEqual(ShoppingChainVerdict.Success,
                HouseholdSurvivalMonitor.EvaluateShoppingChain(snapshot, out missing));
            Assert.AreEqual(string.Empty, missing);
        }

        // --- Poverty vs broken mechanics ---------------------------------------------

        [Test]
        public void ClassifyHousehold_CannotAfford_KnownSeller_StockPresent_IsLegitimatePoverty()
        {
            HouseholdFoodSnapshot snapshot = Household(2);
            snapshot.CashCents = 100;
            snapshot.FoodTopUpCostCents = 500;
            snapshot.CanAffordFoodTopUp = false;
            snapshot.FoodDaysOfSupply = 0f;
            snapshot.MissedMeals = new List<MissedMealObservation> { Missed(12, "Dinner", MissedMealReasonKind.CannotAfford) };
            HouseholdSurvivalRow row = HouseholdSurvivalMonitor.ClassifyHousehold(snapshot);
            Assert.AreEqual(ShortageVerdictKind.LegitimatePoverty, row.Verdict);
            Assert.IsTrue(row.Reason.Contains("Poverty"));
        }

        [Test]
        public void ClassifyHousehold_NoKnownSeller_IsBrokenMechanics_NotPoverty()
        {
            HouseholdFoodSnapshot snapshot = Household(3);
            snapshot.FoodDaysOfSupply = 0f;
            snapshot.MissedMeals = new List<MissedMealObservation> { Missed(12, "Dinner", MissedMealReasonKind.NoKnownSeller) };
            HouseholdSurvivalRow row = HouseholdSurvivalMonitor.ClassifyHousehold(snapshot);
            Assert.AreEqual(ShortageVerdictKind.BrokenMechanics, row.Verdict);
            Assert.IsTrue(row.Reason.Contains("no known seller"));
        }

        [Test]
        public void ClassifyHousehold_NoCounterService_IsBrokenMechanics()
        {
            HouseholdFoodSnapshot snapshot = Household(3);
            snapshot.MissedMeals = new List<MissedMealObservation> { Missed(12, "Lunch", MissedMealReasonKind.NoCounterService) };
            HouseholdSurvivalRow row = HouseholdSurvivalMonitor.ClassifyHousehold(snapshot);
            Assert.AreEqual(ShortageVerdictKind.BrokenMechanics, row.Verdict);
            Assert.IsTrue(row.Reason.Contains("STORE_UNSERVED"));
        }

        [Test]
        public void ClassifyHousehold_UnknownReason_StaysUnknown_NeverInvented()
        {
            HouseholdFoodSnapshot snapshot = Household(4);
            snapshot.MissedMeals = new List<MissedMealObservation> { Missed(12, "Breakfast", MissedMealReasonKind.Unknown) };
            HouseholdSurvivalRow row = HouseholdSurvivalMonitor.ClassifyHousehold(snapshot);
            Assert.AreEqual(ShortageVerdictKind.Unknown, row.Verdict);
            Assert.IsTrue(row.Reason.Contains("not invented"));
        }

        [Test]
        public void ClassifyHousehold_SecureHousehold_IsSecure()
        {
            HouseholdSurvivalRow row = HouseholdSurvivalMonitor.ClassifyHousehold(Household(5));
            Assert.AreEqual(ShortageVerdictKind.Secure, row.Verdict);
        }

        [Test]
        public void BuildReport_CountsVerdicts()
        {
            HouseholdFoodSnapshot poor = Household(2);
            poor.CashCents = 100;
            poor.FoodTopUpCostCents = 500;
            poor.CanAffordFoodTopUp = false;
            poor.FoodDaysOfSupply = 0f;
            poor.MissedMeals = new List<MissedMealObservation> { Missed(12, "Dinner", MissedMealReasonKind.CannotAfford) };
            HouseholdFoodSnapshot broken = Household(3);
            broken.MissedMeals = new List<MissedMealObservation> { Missed(12, "Dinner", MissedMealReasonKind.NoKnownSeller) };
            HouseholdSurvivalReport report = HouseholdSurvivalMonitor.BuildReport(
                HealthyDay(12),
                new List<HouseholdFoodSnapshot> { Household(1), poor, broken });
            Assert.AreEqual(1, report.SecureCount);
            Assert.AreEqual(1, report.LegitimatePovertyCount);
            Assert.AreEqual(1, report.BrokenMechanicsCount);
            Assert.AreEqual(0, report.LifecycleFindings.Count);
        }
    }
}
