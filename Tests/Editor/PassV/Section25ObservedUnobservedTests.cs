using System;
using System.Collections.Generic;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.PassV
{
    /// <summary>
    /// Pass V (Worker 4, brief section 25): observed/unobserved regression tests.
    /// There is no streaming/culling/active-region concept in the simulation:
    /// everything is always simulated, so "one simulation regardless of camera"
    /// holds at the source level. These tests lock it in:
    ///
    ///  A. same worker, camera-focused vs camera-absent: identical outcomes.
    ///  B. repeated presentation activation/deactivation: no duplicates, no drift.
    ///  C. streamed region save/load: clear-then-load is idempotent mid-run.
    ///  D. wagon leaving and returning from an external route: covered by
    ///     OffMapJourneyAuthorityTests (same entity, no duplicated inventory,
    ///     no teleporting goods).
    ///  E. high-speed observation (one giant tick): identical outcomes.
    ///  F. different frame-rate/headless scheduling: covered by
    ///     AutonomousSocietySimTests (tick-size consistency + headless).
    ///  G. birthday while the region is unloaded: aging is never paused.
    ///  H. birth/death/household split with no mesh: identity continuity.
    ///
    /// Actual Unity camera tests stay Codex-side; they are recorded in
    /// Docs/Pass5_Worker4_CodexHandoff.md.
    /// </summary>
    [TestFixture]
    public sealed class Section25ObservedUnobservedTests
    {
        private sealed class CountingSink : IObservationSink
        {
            public int Events;
            public void Notify(ObservationMode mode, string eventName, string detail) { Events++; }
        }

        [TearDown]
        public void TearDown()
        {
            SimulationPresentationBridge.ClearSinks();
        }

        [Test]
        public void A_CameraFocusedVsAbsent_IdenticalOutcomes()
        {
            // Camera-focused: a presentation sink is attached for the whole run.
            SimulationPresentationBridge.ClearSinks();
            var focusedSink = new CountingSink();
            SimulationPresentationBridge.AttachSink(focusedSink);
            AutonomousSocietyDriver focused;
            try
            {
                focused = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
                focused.AdvanceDays(14);
            }
            finally
            {
                SimulationPresentationBridge.DetachSink(focusedSink);
            }

            // Camera-absent: no sink at any point.
            SimulationPresentationBridge.ClearSinks();
            var absent = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            absent.AdvanceDays(14);

            Assert.AreEqual(focused.Fingerprint(), absent.Fingerprint(),
                "camera-focused vs camera-absent runs must be identical");
            Assert.Greater(focusedSink.Events, 0, "the focused run should have notified presentation");
            SocietyReconciliation report = absent.Reconcile();
            Assert.IsTrue(report.Clean, string.Join("\n", report.Divergences.ToArray()));
        }

        [Test]
        public void B_RepeatedPresentationToggling_NoDuplicatesNoDrift()
        {
            var toggled = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            var sink = new CountingSink();
            // Toggle presentation every day: on, off, on, off...
            for (int day = 0; day < 14; day++)
            {
                if (day % 2 == 0)
                {
                    SimulationPresentationBridge.AttachSink(sink);
                }
                else
                {
                    SimulationPresentationBridge.DetachSink(sink);
                }
                toggled.AdvanceDays(1);
            }
            SimulationPresentationBridge.ClearSinks();

            var steady = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            steady.AdvanceDays(14);

            Assert.AreEqual(steady.Fingerprint(), toggled.Fingerprint(),
                "repeated presentation activation/deactivation must not change outcomes");
            Assert.AreEqual(steady.PersonCount, toggled.PersonCount, "no duplicated persons");
            SocietyReconciliation report = toggled.Reconcile();
            Assert.IsTrue(report.Clean, string.Join("\n", report.Divergences.ToArray()));
        }

        [Test]
        public void C_StreamedRegionSaveLoad_IdempotentMidRun()
        {
            // A region "streams out" (observation stops mattering), the game
            // saves, and the region "streams back in" on load.
            var straight = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            straight.AdvanceDays(20);

            var streamed = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            streamed.AdvanceDays(10);
            AutonomousSocietySaveDto saved = streamed.CaptureSaveDto();
            var reloaded = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            reloaded.LoadFromSaveDto(saved);
            reloaded.AdvanceDays(10);

            Assert.AreEqual(straight.Fingerprint(), reloaded.Fingerprint(),
                "streamed-out save/load must not change outcomes");
        }

        [Test]
        public void E_HighSpeedObservation_SingleGiantTick_Identical()
        {
            var fast = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            fast.AdvanceMinutes(30 * 1440); // the whole month in one tick: 100x-style

            var daily = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            daily.AdvanceDays(30);

            Assert.AreEqual(daily.Fingerprint(), fast.Fingerprint(),
                "high-speed observation (one giant tick) must match daily ticks");
        }

        [Test]
        public void G_BirthdayWhileRegionUnloaded_AgingNeverPauses()
        {
            // Two persons with birthdays inside the window; both are "unloaded"
            // (StreamedOut) the whole time. Birthdays are due-event driven and
            // must fire regardless of observation state.
            var scheduler = new DueEventScheduler();
            var birth = new BirthDate(1850, 6, 15, BirthDateProvenance.GeneratedExact, "test");
            var birth2 = new BirthDate(1879, 6, 20, BirthDateProvenance.GeneratedExact, "test");
            var diag = new List<string>();

            string refusal = BirthdayScheduling.ScheduleNextBirthday(
                scheduler, 1, birth, 0,
                AutonomousSocietyDriver.AnchorYear, AutonomousSocietyDriver.AnchorMonth, AutonomousSocietyDriver.AnchorDay,
                diag);
            Assert.IsNull(refusal, refusal);
            refusal = BirthdayScheduling.ScheduleNextBirthday(
                scheduler, 2, birth2, 0,
                AutonomousSocietyDriver.AnchorYear, AutonomousSocietyDriver.AnchorMonth, AutonomousSocietyDriver.AnchorDay,
                diag);
            Assert.IsNull(refusal, refusal);

            // Rescheduling is idempotent: still exactly 2 events, stable ids.
            BirthdayScheduling.ScheduleNextBirthday(scheduler, 1, birth, 0,
                AutonomousSocietyDriver.AnchorYear, AutonomousSocietyDriver.AnchorMonth, AutonomousSocietyDriver.AnchorDay,
                diag);
            Assert.AreEqual(2, scheduler.ScheduledCount, "rescheduling must replace, never duplicate");
            Assert.AreEqual("due-Birthday-birthday:1", BirthdayScheduling.SchedulerEventId(1));

            // Advance 400 days with everyone "unloaded". Pop and process.
            int birthdaysSeenP1 = 0;
            int birthdaysSeenP2 = 0;
            for (int day = 1; day <= 400; day++)
            {
                List<DueEvent> due = scheduler.PopDue(day * BirthdayScheduling.SecondsPerDay);
                foreach (DueEvent evt in due)
                {
                    Assert.AreEqual(DueEventKind.Birthday, evt.Kind);
                    int personId = BirthdayScheduling.PersonIdFromPayload(evt.PayloadRef);
                    if (personId == 1) birthdaysSeenP1++;
                    if (personId == 2) birthdaysSeenP2++;
                    // Chain the next birthday from the day AFTER the one that just
                    // fired: "on or after" from today would reschedule today.
                    BirthDate b = personId == 1 ? birth : birth2;
                    BirthdayScheduling.ScheduleNextBirthday(scheduler, personId, b, day + 1,
                        AutonomousSocietyDriver.AnchorYear, AutonomousSocietyDriver.AnchorMonth, AutonomousSocietyDriver.AnchorDay,
                        diag);
                }
            }
            Assert.AreEqual(1, birthdaysSeenP1, "P1 (June 15) has exactly one birthday in 400 days from March 1880");
            Assert.AreEqual(1, birthdaysSeenP2, "P2 (June 20) has exactly one birthday in 400 days from March 1880");

            // Age derives from the birth date: no paused aging, no double-aging.
            GregorianDateMath.FromAbsoluteDay(400,
                AutonomousSocietyDriver.AnchorYear, AutonomousSocietyDriver.AnchorMonth, AutonomousSocietyDriver.AnchorDay,
                out int y, out int m, out int d);
            int age1 = AgeAuthority.CompletedYears(birth, y, m, d);
            int age2 = AgeAuthority.CompletedYears(birth2, y, m, d);
            Assert.AreEqual(30, age1, "P1 born 1850-06-15 is 30 in spring 1881");
            Assert.AreEqual(1, age2, "P2 born 1879-06-20 is 1 in spring 1881");

            // Save/load keeps exactly one pending birthday per living person.
            DueEventSchedulerSaveDto dto = scheduler.CaptureSaveDto();
            var reloaded = new DueEventScheduler();
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(2, reloaded.ScheduledCount, "birthdays survive save/load without duplication");
        }

        [Test]
        public void H_BirthDeathHouseholdSplit_NoMesh_IdentityContinuity()
        {
            // No presentation, no mesh, no scene: the lifecycle authorities
            // alone must keep identity continuous.
            var population = new PopulationState();
            var memberships = new HouseholdMembershipRegistry();
            var kinship = new KinshipRegistry();
            var ids = new EntityIdRegistry();
            ids.SeedKind(EntityKind.Membership, 100); // manual memberships 1..2 below must not collide
            population.RestorePersonIdAllocator(3); // manual persons 1..2 below must not collide
            var lifecycle = new HouseholdLifecycleManager(population, memberships, kinship, ids);

            var founder = new PersonState { id = 1, firstName = "Tomas", lastName = "Miller", age = 34 };
            var spouse = new PersonState { id = 2, firstName = "Anna", lastName = "Miller", age = 31 };
            population.people.Add(founder);
            population.people.Add(spouse);
            var household = new HouseholdState { id = 1, householdName = "Miller", surname = "Miller" };
            population.households.Add(household);
            ids.SeedKind(EntityKind.Person, 3);
            memberships.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = 1, HouseholdId = 1,
                Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = 0,
            });
            memberships.Register(new HouseholdMembership
            {
                MembershipId = 2, PersonId = 2, HouseholdId = 1,
                Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = 0,
            });

            // Birth: the child is a NEW person; parents keep their ids.
            PersonState child = lifecycle.RecordBirth(1, 2, 1, "Ruth", "Miller", 100);
            Assert.IsNotNull(child);
            int childId = child.id;
            Assert.AreNotEqual(1, childId);
            Assert.AreNotEqual(2, childId);
            Assert.AreEqual(1, founder.id, "the father's identity is untouched");
            Assert.AreEqual(1, memberships.GetActiveHouseholdId(childId), "the child joins the household");

            // Household split: the adult child leaves home. Same person, new household.
            HouseholdState newHousehold = lifecycle.FormHousehold("Ruth's household", "Miller", childId, 200,
                HouseholdMembershipSource.Manual);
            Assert.IsNotNull(newHousehold);
            string transferRejection = lifecycle.TransferMember(childId, newHousehold.id, 200);
            Assert.IsNull(transferRejection, transferRejection);
            Assert.AreEqual(childId, population.GetPerson(childId).id, "the same Person record after the split");
            Assert.AreEqual(newHousehold.id, memberships.GetActiveHouseholdId(childId));

            // Death: the record is retained, the id is never rewritten or reused.
            Assert.IsTrue(lifecycle.RecordDeath(1, 300, "test cause"));
            Assert.AreEqual(300, population.GetPerson(1).deathDayIndex);
            Assert.AreEqual(-1, memberships.GetActiveHouseholdId(1), "membership ended by death");
            PersonState childAfter = lifecycle.RecordBirth(newHousehold.id, -1, -1, "Anna", "Miller", 400);
            Assert.AreNotEqual(1, childAfter.id, "the decedent's id is never reused");

            // No duplicates anywhere.
            var seen = new HashSet<int>();
            foreach (PersonState person in population.people)
            {
                Assert.IsTrue(seen.Add(person.id), "duplicate person id " + person.id);
            }
        }
    }
}
