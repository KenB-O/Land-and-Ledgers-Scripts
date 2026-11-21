// Campaign C, package 24 — DEF-010 (domestic labour: 3 of 4 need triggers
// hard-coded off), owner Decision 6 (Nov 15, 2026).
//
// The driver was already wired end-to-end, but its three need inputs were
// hard-coded off in SimulationDrivers.DriveDomesticLaborDay, so households
// never cooked, handled fuel, or cleaned — only animal care ever ran.
// This package supplies the three missing need authorities, each computed
// from real state:
//
// - Cooking: meals the household eats at home today, read from the meal
//   authority's own served-meal records (HouseholdMealLogRegistry).
// - Fuel handling: the household burns fuel today (the heating authority's
//   own forecast) and has fuel on hand to handle.
// - Cleaning: accumulated household use (one unit per occupant per day),
//   need at 14 person-days, reset by a completed Chores task.
//
// Harness limits (stated honestly): SimulationDrivers.cs and
// DomesticLaborDriver.cs are NOT in the harness closure (their closures
// pull the whole simulation hub / TaskAuthority substrate), so the thin
// wiring there is verified by parse check + structural tests here — the
// C-21 precedent. Everything testable without Unity (the three authorities'
// behaviour, the tracker's accumulation/reset/save-load) is tested
// behaviorally against the helpers. Unity-run proof is still owed (a
// household must cook / handle fuel / clean in a live run) — no Unity
// verification is claimed by this package.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCDomesticNeedAuthoritiesTests
    {
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string DriverPath =
            "Assets/Scripts/Domains/Population/DomesticLaborDriver.cs";

        private const int HouseholdId = 7;
        private const int DayIndex = 10;

        // ---------- Cooking: meals at home today ----------

        [Test]
        public void MealsAtHomeToday_NullLog_ReturnsZero()
        {
            Assert.AreEqual(0, DomesticNeedAuthorities.MealsAtHomeToday(null, HouseholdId, DayIndex));
        }

        [Test]
        public void MealsAtHomeToday_EmptyLog_ReturnsZero()
        {
            var log = new HouseholdMealLogRegistry();
            Assert.AreEqual(0, DomesticNeedAuthorities.MealsAtHomeToday(log, HouseholdId, DayIndex));
        }

        [Test]
        public void MealsAtHomeToday_HomeInventoryMealsToday_Counted()
        {
            var log = new HouseholdMealLogRegistry();
            RecordMeal(log, 1, MealSlot.Morning, MealSourceKind.HouseholdInventory, DayIndex);
            RecordMeal(log, 2, MealSlot.Evening, MealSourceKind.HouseholdInventory, DayIndex);
            Assert.AreEqual(2, DomesticNeedAuthorities.MealsAtHomeToday(log, HouseholdId, DayIndex));
        }

        [Test]
        public void MealsAtHomeToday_HouseholdProductionMealsToday_Counted()
        {
            // Prepared in-house from raw ingredients is still eaten at home.
            var log = new HouseholdMealLogRegistry();
            RecordMeal(log, 1, MealSlot.Morning, MealSourceKind.HouseholdProduction, DayIndex);
            Assert.AreEqual(1, DomesticNeedAuthorities.MealsAtHomeToday(log, HouseholdId, DayIndex));
        }

        [Test]
        public void MealsAtHomeToday_OutOfHomeMealsToday_NotCounted()
        {
            var log = new HouseholdMealLogRegistry();
            RecordMeal(log, 1, MealSlot.Morning, MealSourceKind.PurchasedMeal, DayIndex);
            RecordMeal(log, 1, MealSlot.Midday, MealSourceKind.BoardingArrangement, DayIndex);
            RecordMeal(log, 2, MealSlot.Evening, MealSourceKind.EmployerProvided, DayIndex);
            Assert.AreEqual(0, DomesticNeedAuthorities.MealsAtHomeToday(log, HouseholdId, DayIndex));
        }

        [Test]
        public void MealsAtHomeToday_MealsOnOtherDays_NotCounted()
        {
            var log = new HouseholdMealLogRegistry();
            RecordMeal(log, 1, MealSlot.Morning, MealSourceKind.HouseholdInventory, DayIndex - 1);
            RecordMeal(log, 1, MealSlot.Evening, MealSourceKind.HouseholdInventory, DayIndex + 1);
            Assert.AreEqual(0, DomesticNeedAuthorities.MealsAtHomeToday(log, HouseholdId, DayIndex));
        }

        [Test]
        public void MealsAtHomeToday_MealsForOtherHouseholds_NotCounted()
        {
            var log = new HouseholdMealLogRegistry();
            log.RecordMeal(new HouseholdMealRecord
            {
                PersonId = 9,
                HouseholdId = HouseholdId + 1,
                DayIndex = DayIndex,
                Slot = MealSlot.Morning,
                SourceKind = MealSourceKind.HouseholdInventory,
            });
            Assert.AreEqual(0, DomesticNeedAuthorities.MealsAtHomeToday(log, HouseholdId, DayIndex));
        }

        [Test]
        public void MealsAtHomeToday_MissedMealsOnly_NoCookingNeed()
        {
            // An empty larder records missed meals, never served meals — and
            // a missed meal is a shopping need, not a cooking task.
            var log = new HouseholdMealLogRegistry();
            log.RecordMissedMeal(new MissedMealRecord
            {
                PersonId = 1,
                HouseholdId = HouseholdId,
                DayIndex = DayIndex,
                Slot = MealSlot.Morning,
                Reason = "larder empty",
            });
            log.RecordMissedMeal(new MissedMealRecord
            {
                PersonId = 2,
                HouseholdId = HouseholdId,
                DayIndex = DayIndex,
                Slot = MealSlot.Evening,
                Reason = "larder empty",
            });
            Assert.AreEqual(0, DomesticNeedAuthorities.MealsAtHomeToday(log, HouseholdId, DayIndex));
        }

        // ---------- Fuel handling: burn today + fuel on hand ----------

        [Test]
        public void FuelHandlingNeeded_NullHousehold_ReturnsFalse()
        {
            Assert.IsFalse(DomesticNeedAuthorities.FuelHandlingNeeded(null, JanuaryDay()));
        }

        [Test]
        public void FuelHandlingNeeded_July_NoBurn_ReturnsFalse()
        {
            // Heating lane (Nov 20 2026): at the July mean (21.8 C) the
            // heating authority forecasts zero burn — even with a full fuel
            // reserve there is nothing to handle. Pinned to the explicit
            // July mean, not the seeded day: a -2.5 sigma July day would
            // otherwise burn fuel and flake this test.
            HouseholdState household = HouseholdWithFuel(5);
            Assert.IsFalse(DomesticNeedAuthorities.FuelHandlingNeeded(household, JulyDay(), 21.8f));
        }

        [Test]
        public void FuelHandlingNeeded_January_WithFuelOnHand_ReturnsTrue()
        {
            HouseholdState household = HouseholdWithFuel(5);
            Assert.IsTrue(DomesticNeedAuthorities.FuelHandlingNeeded(household, JanuaryDay()));
        }

        [Test]
        public void FuelHandlingNeeded_January_EmptyReserve_ReturnsFalse()
        {
            // Fuel burns in January, but an empty reserve has nothing to
            // handle — the shortfall is a procurement need, not a handling task.
            HouseholdState household = HouseholdWithFuel(0);
            Assert.IsFalse(DomesticNeedAuthorities.FuelHandlingNeeded(household, JanuaryDay()));
        }

        [Test]
        public void FuelHandlingNeeded_January_NoFuelReserveEntry_ReturnsFalse()
        {
            var household = new HouseholdState
            {
                id = HouseholdId,
                memberIds = new List<int> { 1, 2 },
                reserves = new List<HouseholdReserveState>
                {
                    new HouseholdReserveState { categoryId = "staple_food", currentUnits = 10 },
                },
            };
            Assert.IsFalse(DomesticNeedAuthorities.FuelHandlingNeeded(household, JanuaryDay()));
        }

        [Test]
        public void FuelHandlingNeeded_January_EfficientStove_StillNeedsHandling()
        {
            // The stove tier shapes how much fuel burns, not whether handling
            // is needed: an efficient stove still burns fuel in January.
            HouseholdState household = HouseholdWithFuel(5);
            household.stoveTier = HouseholdStoveTier.Efficient;
            Assert.IsTrue(DomesticNeedAuthorities.FuelHandlingNeeded(household, JanuaryDay()));
        }

        // ---------- Cleaning: accumulated use ----------

        [Test]
        public void Cleaning_FreshTracker_NeedsNothing()
        {
            var tracker = new HouseholdCleanlinessTracker();
            Assert.IsFalse(tracker.NeedsCleaning(HouseholdId));
            Assert.AreEqual(0, tracker.AccumulatedUseUnits(HouseholdId));
        }

        [Test]
        public void Cleaning_AccumulateBelowThreshold_NotNeeded()
        {
            var tracker = new HouseholdCleanlinessTracker();
            for (int day = 0; day < 6; day++)
            {
                tracker.Accumulate(HouseholdId, 2, day);
            }

            // 2 occupants x 6 days = 12 person-days, below the 14 threshold.
            Assert.AreEqual(12, tracker.AccumulatedUseUnits(HouseholdId));
            Assert.IsFalse(tracker.NeedsCleaning(HouseholdId));
        }

        [Test]
        public void Cleaning_AccumulateAtThreshold_Needed()
        {
            var tracker = new HouseholdCleanlinessTracker();
            for (int day = 0; day < 7; day++)
            {
                tracker.Accumulate(HouseholdId, 2, day);
            }

            // 2 occupants x 7 days = 14 person-days: the threshold is met.
            Assert.AreEqual(14, tracker.AccumulatedUseUnits(HouseholdId));
            Assert.IsTrue(tracker.NeedsCleaning(HouseholdId));
        }

        [Test]
        public void Cleaning_Accumulate_IdempotentPerDay()
        {
            var tracker = new HouseholdCleanlinessTracker();
            tracker.Accumulate(HouseholdId, 2, 5);
            tracker.Accumulate(HouseholdId, 2, 5);
            Assert.AreEqual(2, tracker.AccumulatedUseUnits(HouseholdId));
            tracker.Accumulate(HouseholdId, 2, 6);
            Assert.AreEqual(4, tracker.AccumulatedUseUnits(HouseholdId));
        }

        [Test]
        public void Cleaning_ResetAfterCleaning_ClearsNeed()
        {
            var tracker = new HouseholdCleanlinessTracker();
            for (int day = 0; day < 7; day++)
            {
                tracker.Accumulate(HouseholdId, 2, day);
            }

            Assert.IsTrue(tracker.NeedsCleaning(HouseholdId));
            tracker.ResetAfterCleaning(HouseholdId, 7);
            Assert.IsFalse(tracker.NeedsCleaning(HouseholdId));
            Assert.AreEqual(0, tracker.AccumulatedUseUnits(HouseholdId));
            // The need is re-earned by further use, never by a timer alone.
            tracker.Accumulate(HouseholdId, 2, 8);
            Assert.AreEqual(2, tracker.AccumulatedUseUnits(HouseholdId));
            Assert.IsFalse(tracker.NeedsCleaning(HouseholdId));
        }

        [Test]
        public void Cleaning_HouseholdsTrackedIndependently()
        {
            var tracker = new HouseholdCleanlinessTracker();
            for (int day = 0; day < 7; day++)
            {
                tracker.Accumulate(HouseholdId, 2, day);
                tracker.Accumulate(HouseholdId + 1, 1, day);
            }

            Assert.IsTrue(tracker.NeedsCleaning(HouseholdId));
            Assert.IsFalse(tracker.NeedsCleaning(HouseholdId + 1));
            Assert.AreEqual(7, tracker.AccumulatedUseUnits(HouseholdId + 1));
        }

        [Test]
        public void Cleaning_SaveLoadRoundTrip_PreservesAccumulation()
        {
            var tracker = new HouseholdCleanlinessTracker();
            for (int day = 0; day < 7; day++)
            {
                tracker.Accumulate(HouseholdId, 2, day);
            }

            Assert.IsTrue(tracker.NeedsCleaning(HouseholdId));
            List<HouseholdCleanlinessRecord> captured = tracker.CaptureRecords();

            var restored = new HouseholdCleanlinessTracker();
            restored.LoadRecords(captured);
            Assert.IsTrue(restored.NeedsCleaning(HouseholdId));
            Assert.AreEqual(14, restored.AccumulatedUseUnits(HouseholdId));
            // Idempotency survives the round trip: re-accumulating the last
            // captured day adds nothing.
            restored.Accumulate(HouseholdId, 2, 6);
            Assert.AreEqual(14, restored.AccumulatedUseUnits(HouseholdId));
            restored.Accumulate(HouseholdId, 2, 7);
            Assert.AreEqual(16, restored.AccumulatedUseUnits(HouseholdId));
            // Captured records are copies: later accumulation does not mutate
            // the DTO the save pipeline already holds.
            tracker.Accumulate(HouseholdId, 2, 7);
            Assert.AreEqual(14, captured[0].AccumulatedUseUnits);
        }

        // ---------- Structural: the harness-excluded wiring ----------

        [Test]
        public void DriveDomesticLaborDay_CallsNeedAuthorities()
        {
            string source = ReadRepoFile(DriversPath);
            Assert.IsTrue(source.Contains("DomesticNeedAuthorities.MealsAtHomeToday("),
                "Campaign C (DEF-010): DriveDomesticLaborDay must read the cooking need from the meal authority.");
            Assert.IsTrue(source.Contains("DomesticNeedAuthorities.FuelHandlingNeeded("),
                "Campaign C (DEF-010): DriveDomesticLaborDay must read the fuel-handling need from the heating authority.");
            Assert.IsTrue(source.Contains("AccumulateCleaningUse(household.id, members.Count, absoluteDayIndex)"),
                "Campaign C (DEF-010): DriveDomesticLaborDay must accumulate cleaning use before planning.");
            Assert.IsTrue(source.Contains("CleaningNeeded(household.id)"),
                "Campaign C (DEF-010): DriveDomesticLaborDay must read the cleaning need from the driver tracker.");
        }

        [Test]
        public void DriveDomesticLaborDay_NoHardCodedOffInputs()
        {
            string source = ReadRepoFile(DriversPath);
            Assert.IsFalse(source.Contains("MealsAtHomeCount = 0"),
                "Campaign C (DEF-010): the hard-coded MealsAtHomeCount = 0 must be gone.");
            Assert.IsFalse(source.Contains("FuelHandlingNeeded = false"),
                "Campaign C (DEF-010): the hard-coded FuelHandlingNeeded = false must be gone.");
            Assert.IsFalse(source.Contains("CleaningNeeded = false"),
                "Campaign C (DEF-010): the hard-coded CleaningNeeded = false must be gone.");
        }

        [Test]
        public void Driver_PersistsCleanlinessAndResetsOnChoresCompletion()
        {
            string source = ReadRepoFile(DriverPath);
            Assert.IsTrue(source.Contains("public void AccumulateCleaningUse("),
                "Campaign C (DEF-010): the driver must expose cleaning accumulation.");
            Assert.IsTrue(source.Contains("public bool CleaningNeeded("),
                "Campaign C (DEF-010): the driver must expose the cleaning need.");
            Assert.IsTrue(source.Contains("cleanliness = cleanliness.CaptureRecords()"),
                "Campaign C (DEF-010): the driver save DTO must carry cleanliness records.");
            Assert.IsTrue(source.Contains("cleanliness.LoadRecords(dto.cleanliness)"),
                "Campaign C (DEF-010): the driver load must restore cleanliness records.");
            Assert.IsTrue(source.Contains("cleanliness.ResetAfterCleaning("),
                "Campaign C (DEF-010): a completed Chores task must reset the cleanliness accumulation.");
            Assert.IsTrue(source.Contains("public List<HouseholdCleanlinessRecord> cleanliness"),
                "Campaign C (DEF-010): the driver save DTO must hold the cleanliness list.");
        }

        // ---------- helpers ----------

        private static void RecordMeal(HouseholdMealLogRegistry log, int personId, MealSlot slot,
            MealSourceKind source, int dayIndex)
        {
            log.RecordMeal(new HouseholdMealRecord
            {
                PersonId = personId,
                HouseholdId = HouseholdId,
                DayIndex = dayIndex,
                Slot = slot,
                SourceKind = source,
            });
        }

        private static HouseholdState HouseholdWithFuel(int fuelUnits)
        {
            return new HouseholdState
            {
                id = HouseholdId,
                memberIds = new List<int> { 1, 2 },
                reserves = new List<HouseholdReserveState>
                {
                    new HouseholdReserveState
                    {
                        categoryId = HouseholdReserveCatalog.FuelWoodCategoryId,
                        currentUnits = fuelUnits,
                    },
                },
            };
        }

        private static int JanuaryDay()
        {
            // Heating lane (Nov 20 2026): mid-January, deep winter — the
            // temperature-driven burn is always positive here (the January
            // mean is -10.7 C and even a +3 sigma day stays well below the
            // 15 C heating base).
            return CalendarAnchor.Default.ToAbsoluteDay(1881, 1, 15);
        }

        private static int JulyDay()
        {
            // Heating lane (Nov 20 2026): mid-July. The seeded day's mean
            // is almost always above the 15 C heating base, but the July
            // test pins the explicit July mean instead of relying on the draw.
            return CalendarAnchor.Default.ToAbsoluteDay(1880, 7, 15);
        }

        // ---------- repo-file helpers (CampaignCProcurementStrandSummaryTests precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, DriversPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                System.IO.Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-c"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, DriversPath)))
                        {
                            return candidate;
                        }
                    }

                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-010): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string path = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(path), "Campaign C (DEF-010): expected repo file missing: " + path);
            return File.ReadAllText(path);
        }
    }
}
