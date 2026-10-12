using System;
using System.Collections.Generic;
using LandLedgers.Reporting;
using NUnit.Framework;

namespace LandLedgers.Editor.Reporting
{
    /// <summary>
    /// Pass IV (Worker 4, Phase Q): read models are production-backed, bounded,
    /// and obey the observer-visibility rule — no hidden NPC knowledge reaches
    /// the player.
    /// </summary>
    [TestFixture]
    public sealed class ReadModelTests
    {
        private static ObserverContext Observer(int personId, int householdId, string locationId)
        {
            return new ObserverContext
            {
                ObserverPersonId = personId,
                ObserverHouseholdId = householdId,
                ObserverLocationId = locationId,
                IsPublicLocation = loc => loc == "market-square",
                OwnerPersonIdOfBusiness = biz => biz == "bakery-1" ? 8 : -1,
                IsDelegatedManagerOfBusiness = (biz, pid) => biz == "bakery-1" && pid == 7,
            };
        }

        private static PersonActivityReadModelBuilder ActivityBuilder()
        {
            return new PersonActivityReadModelBuilder
            {
                DisplayNameOf = pid => "P" + pid,
                HouseholdOf = pid => pid == 1 ? 7 : 9,
                CurrentActivityOf = (pid, day) => new PersonActivitySnapshot
                {
                    ActivityLabel = "Working",
                    Purpose = "bake the morning batch",
                    LocationId = "bakery",
                    TaskId = "task-1",
                    TaskLabel = "bake bread",
                    EmployerBusinessId = "bakery-1",
                    StartMinuteOfDay = 540,
                    RemainingMinutes = 120,
                    Blocker = "oven preheating",
                    Outcome = string.Empty,
                },
                HistoryOf = pid =>
                {
                    var list = new List<ActivityTransition>();
                    for (int i = 0; i < 40; i++)
                    {
                        list.Add(new ActivityTransition { DayIndex = 100, MinuteOfDay = i * 10, FromActivity = "a", ToActivity = "b" });
                    }

                    return list;
                },
            };
        }

        [Test]
        public void PersonActivity_OwnHousehold_SeesEverything()
        {
            PersonActivityReadModel model = ActivityBuilder().Build(1, 100, 600);
            PersonActivityReadModel view = model.RedactForObserver(Observer(2, 7, "home"));

            Assert.AreEqual("Working", view.CurrentActivity);
            Assert.AreEqual("bake the morning batch", view.Purpose);
            Assert.AreEqual("bakery-1", view.EmployerBusinessId);
            Assert.AreEqual("task-1", view.TaskId);
            Assert.IsFalse(view.Redacted);
        }

        [Test]
        public void PersonActivity_StrangerElsewhere_SeesNothing()
        {
            PersonActivityReadModel model = ActivityBuilder().Build(1, 100, 600);
            // Observer at home; subject at the bakery (not public).
            PersonActivityReadModel view = model.RedactForObserver(Observer(5, 9, "home"));

            Assert.AreEqual("unknown", view.CurrentActivity);
            Assert.IsTrue(string.IsNullOrEmpty(view.Purpose));
            Assert.IsTrue(string.IsNullOrEmpty(view.EmployerBusinessId));
            Assert.AreEqual(0, view.RecentHistory.Count);
            Assert.IsTrue(view.Redacted);
        }

        [Test]
        public void PersonActivity_StrangerSameLocation_SeesActivityNotPurpose()
        {
            PersonActivityReadModel model = ActivityBuilder().Build(1, 100, 600);
            PersonActivityReadModel view = model.RedactForObserver(Observer(5, 9, "bakery"));

            Assert.AreEqual("Working", view.CurrentActivity, "can see them working");
            Assert.IsTrue(string.IsNullOrEmpty(view.Purpose), "but not why");
            Assert.IsTrue(string.IsNullOrEmpty(view.EmployerBusinessId), "nor for whom");
            Assert.IsTrue(view.Redacted);
        }

        [Test]
        public void PersonActivity_History_IsBounded()
        {
            PersonActivityReadModel model = ActivityBuilder().Build(1, 100, 600);
            Assert.AreEqual(PersonActivityReadModel.MaxHistory, model.RecentHistory.Count);
        }

        private static BusinessWorkReadModelBuilder BusinessBuilder()
        {
            return new BusinessWorkReadModelBuilder
            {
                DisplayNameOf = _ => "Bakery",
                LocationOf = _ => "bakery",
                TasksOf = (biz, day) => new List<TaskRow>
                {
                    new TaskRow { TaskId = "t1", Status = "Active", AssigneePersonId = 1, PlannedMinutes = 120 },
                },
                WorkersOf = (biz, day) => new List<WorkerRow>
                {
                    new WorkerRow { PersonId = 1, MinutesWorkedToday = 120 },
                },
                DemandOf = (biz, day) => new List<LaborDemandRow>(),
                EquipmentOccupancyOf = (biz, day) => new Dictionary<string, int> { { "oven-1", 180 } },
                ShortagesOf = (biz, day) => new List<string> { "low on flour" },
                OutputsOf = (biz, day) => new Dictionary<string, int> { { "bread", 30 } },
                FailuresOf = (biz, day) => new List<string>(),
                CashCentsOf = _ => 50000,
                WagesPaidTodayOf = (biz, day) => 2000,
                HistoryOf = biz =>
                {
                    var list = new List<WorkEvent>();
                    for (int i = 0; i < 50; i++)
                    {
                        list.Add(new WorkEvent { DayIndex = 100, Kind = "completed", Summary = "x", IsFinancial = i % 2 == 0 });
                    }

                    return list;
                },
            };
        }

        [Test]
        public void BusinessWork_Owner_SeesFinancials()
        {
            BusinessWorkReadModel model = BusinessBuilder().Build("bakery-1", 100, 600);
            BusinessWorkReadModel view = model.RedactForObserver(Observer(8, 7, "home"));

            Assert.AreEqual(50000, view.CashCents);
            Assert.AreEqual(2000, view.WagesPaidCentsToday);
            Assert.AreEqual(1, view.ActiveTasks.Count);
            Assert.IsFalse(view.Redacted);
        }

        [Test]
        public void BusinessWork_StrangerOnPremises_SeesFloorNotFinancials()
        {
            BusinessWorkReadModel model = BusinessBuilder().Build("bakery-1", 100, 600);
            BusinessWorkReadModel view = model.RedactForObserver(Observer(5, 9, "bakery"));

            Assert.AreEqual(1, view.ActiveTasks.Count, "can see the work floor");
            Assert.AreEqual(-1, view.CashCents, "but not the cash");
            Assert.AreEqual(-1, view.WagesPaidCentsToday);
            Assert.IsTrue(view.Redacted);
        }

        [Test]
        public void BusinessWork_StrangerElsewhere_SeesNothing()
        {
            BusinessWorkReadModel model = BusinessBuilder().Build("bakery-1", 100, 600);
            BusinessWorkReadModel view = model.RedactForObserver(Observer(5, 9, "home"));

            Assert.AreEqual(0, view.ActiveTasks.Count);
            Assert.AreEqual(-1, view.CashCents);
            Assert.IsTrue(view.Redacted);
        }

        [Test]
        public void BusinessWork_History_IsBounded_AndRedactsFinancials()
        {
            BusinessWorkReadModel model = BusinessBuilder().Build("bakery-1", 100, 600);
            Assert.AreEqual(BusinessWorkReadModel.MaxHistory, model.RecentHistory.Count);

            BusinessWorkReadModel view = model.RedactForObserver(Observer(5, 9, "bakery"));
            foreach (WorkEvent e in view.RecentHistory)
            {
                Assert.IsFalse(e.IsFinancial, "financial events never leak to non-owners");
            }
        }

        private static EquipmentReadModelBuilder EquipmentBuilder()
        {
            return new EquipmentReadModelBuilder
            {
                DisplayNameOf = _ => "Thresher",
                OwnerBusinessOf = _ => "farm-1",
                LocationOf = _ => "barn",
                ConditionOf = _ => 0.8f,
                StatusOf = _ => "Occupied",
                OccupiedMinutesTodayOf = (eid, day) => 240,
                AvailableCapacityNowOf = _ => 0f,
                UpcomingReservationsOf = _ => new List<ReservationRow>
                {
                    new ReservationRow { ResourceKey = "thresher-1", StartMinuteOfDay = 600, EndMinuteOfDay = 720, HolderLabel = "P4 harvest" },
                },
                PendingMaintenanceOf = _ => new List<string> { "belt wearing" },
                HistoryOf = _ => new List<EquipmentEvent>(),
                OwnerPersonIdOf = biz => biz == "farm-1" ? 8 : -1,
                IsDelegatedManagerOf = (biz, pid) => false,
            };
        }

        [Test]
        public void Equipment_Owner_SeesHolderLabels()
        {
            EquipmentReadModel model = EquipmentBuilder().Build("thresher-1", 100);
            var observer = Observer(8, 7, "home");

            // Owner is person 8 of farm-1: holder visible.
            EquipmentReadModel view = model.RedactForObserver(observer);
            Assert.AreEqual(1, view.UpcomingReservations.Count);
            Assert.AreEqual("P4 harvest", view.UpcomingReservations[0].HolderLabel);
            Assert.IsFalse(view.Redacted);
        }

        [Test]
        public void Equipment_BystanderAtLocation_SeesOccupiedAnonymized()
        {
            EquipmentReadModel model = EquipmentBuilder().Build("thresher-1", 100);
            EquipmentReadModel view = model.RedactForObserver(Observer(5, 9, "barn"));

            Assert.AreEqual("Occupied", view.Status);
            Assert.AreEqual(1, view.UpcomingReservations.Count);
            Assert.AreEqual("occupied", view.UpcomingReservations[0].HolderLabel, "holder anonymized for non-owners");
        }

        [Test]
        public void Equipment_StrangerElsewhere_SeesAlmostNothing()
        {
            EquipmentReadModel model = EquipmentBuilder().Build("thresher-1", 100);
            EquipmentReadModel view = model.RedactForObserver(Observer(5, 9, "home"));

            Assert.AreEqual(-1f, view.Condition01, "condition undisclosed");
            Assert.AreEqual(0, view.UpcomingReservations.Count);
            Assert.IsTrue(view.Redacted);
        }
    }
}
