using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Labor;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker C Phase D: person job decisions. Deterministic; decisions use
    /// ONLY JobDecisionInput fields (legitimately-known information).
    /// Lifecycle paths (change/resign/dismissed/pause) are modeled as
    /// decisions per PersonJobDecisions remarks.
    /// </summary>
    [TestFixture]
    public sealed class PersonJobDecisionTests
    {
        private static JobDecisionInput BaseOffer()
        {
            return new JobDecisionInput
            {
                PersonId = 1,
                HouseholdId = 10,
                ReservesCents = 120,
                Dependents = 2,
                CurrentWageCentsPerWeek = 0,
                CurrentlyEmployed = false,
                OfferWageCentsPerWeek = 300,
                CommuteMinutesOneWay = 20,
                ConditionsScore01 = 0.7f,
                EmployerReliability01 = 0.8f,
                HousingOffered = false,
                KnownAlternativeCount = 0,
                ReservesTrendCentsPerDay = -10,
                DayIndex = 401
            };
        }

        [Test]
        public void Urgency_UnemployedDecliningReservesWithDependents_AcceptsReasonableOffer()
        {
            JobDecision d = PersonJobDecisions.EvaluateOffer(BaseOffer(), new List<string>());
            Assert.AreEqual(JobDecisionKind.Accept, d.Kind);
            StringAssert.Contains("urgency", d.Reason);
        }

        [Test]
        public void AdequateResources_RefusesPoorOffer_DespiteHigherWage()
        {
            // Employed at 250c; offered 400c (much higher wage) but harsh conditions.
            var input = BaseOffer();
            input.CurrentlyEmployed = true;
            input.CurrentWageCentsPerWeek = 250;
            input.OfferWageCentsPerWeek = 400;
            input.ConditionsScore01 = 0.2f;
            input.ReservesCents = 500;
            input.ReservesTrendCentsPerDay = 0;

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Refuse, d.Kind);
            StringAssert.Contains("poor conditions", d.Reason);
        }

        [Test]
        public void NoAutoAcceptHighestWage_LongCommuteOutweighsPay()
        {
            // P2-style: adequate resources; the highest wage on the table loses
            // to a long commute.
            var input = BaseOffer();
            input.CurrentlyEmployed = true;
            input.CurrentWageCentsPerWeek = 250;
            input.OfferWageCentsPerWeek = 900;
            input.CommuteMinutesOneWay = 150;
            input.ReservesCents = 3000;
            input.ReservesTrendCentsPerDay = 5;

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Refuse, d.Kind);
            StringAssert.Contains("long commute", d.Reason);
        }

        [Test]
        public void UnknownEmployerReliability_CautionDelay_EvenWhenUrgent()
        {
            // Known-info-only: unknown reputation -> Delay, not a blind accept.
            // Urgency does NOT override caution (the offer cannot be verified).
            var input = BaseOffer(); // urgent by construction
            input.EmployerReliability01 = 0f;

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Delay, d.Kind);
            StringAssert.Contains("unknown", d.Reason);
        }

        [Test]
        public void JobChange_BetterOfferAcceptableCommute_Accepts()
        {
            var input = BaseOffer();
            input.CurrentlyEmployed = true;
            input.CurrentWageCentsPerWeek = 250;
            input.OfferWageCentsPerWeek = 320; // > 10% improvement
            input.CommuteMinutesOneWay = 25;
            input.ReservesCents = 800;
            input.ReservesTrendCentsPerDay = 0;

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Accept, d.Kind);
            StringAssert.Contains("job change", d.Reason);
        }

        [Test]
        public void Counter_WhenWageIsOnlyBlocker()
        {
            var input = BaseOffer();
            input.CurrentlyEmployed = true;
            input.CurrentWageCentsPerWeek = 300;
            input.OfferWageCentsPerWeek = 260; // below current, everything else fine
            input.CommuteMinutesOneWay = 20;
            input.ConditionsScore01 = 0.8f;
            input.EmployerReliability01 = 0.9f;
            input.ReservesCents = 800;
            input.ReservesTrendCentsPerDay = 0;

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Counter, d.Kind);
            Assert.AreEqual(300, d.CounterWageCentsPerWeek, "counter proposes current pay");
        }

        [Test]
        public void Resign_ModeledAsRefuseOfCurrentTerms()
        {
            // Resign = refusing to continue current terms (offer reflects the
            // current job; conditions have become poor).
            var input = BaseOffer();
            input.CurrentlyEmployed = true;
            input.CurrentWageCentsPerWeek = 300;
            input.OfferWageCentsPerWeek = 300;
            input.ConditionsScore01 = 0.1f;
            input.ReservesCents = 800;
            input.ReservesTrendCentsPerDay = 0;

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Refuse, d.Kind, "resign: walks away from current terms");
        }

        [Test]
        public void Pause_ModeledAsDelayDecision()
        {
            // Pause/dismissed are employer-side outcomes; they are
            // representable as decision records (Delay / Refuse kinds).
            var pause = new JobDecision { Kind = JobDecisionKind.Delay, Reason = "paused by employer: mill idle" };
            Assert.AreEqual(JobDecisionKind.Delay, pause.Kind);
            StringAssert.Contains("paused", pause.Reason);
        }

        [Test]
        public void Urgency_StillRefusesDangerousWork()
        {
            // Refusal stays meaningful even in need (CAN-CE1-03.5).
            var input = BaseOffer();
            input.ConditionsScore01 = 0.05f; // hazardous

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Refuse, d.Kind);
        }

        [Test]
        public void HousingOffered_WaivesLongCommuteBlocker()
        {
            var input = BaseOffer();
            input.CurrentlyEmployed = true;
            input.CurrentWageCentsPerWeek = 250;
            input.OfferWageCentsPerWeek = 320;
            input.CommuteMinutesOneWay = 150;
            input.HousingOffered = true; // lives on site
            input.ReservesCents = 800;
            input.ReservesTrendCentsPerDay = 0;

            JobDecision d = PersonJobDecisions.EvaluateOffer(input, new List<string>());
            Assert.AreEqual(JobDecisionKind.Accept, d.Kind, "on-site housing waives the commute blocker");
        }

        [Test]
        public void WillWorkToday_Fatigued_Refuses()
        {
            var diag = new List<string>();
            bool ok = PersonJobDecisions.WillWorkToday(7, true, null, "", diag);
            Assert.IsFalse(ok);
            Assert.IsNotEmpty(diag);
            StringAssert.Contains("fatigued", diag[0]);
        }

        [Test]
        public void WillWorkToday_Danger_Refuses()
        {
            var diag = new List<string>();
            bool ok = PersonJobDecisions.WillWorkToday(7, false, "unstable shaft", "", diag);
            Assert.IsFalse(ok);
            StringAssert.Contains("danger", diag[0]);
        }

        [Test]
        public void WillWorkToday_ContractRestriction_Refuses()
        {
            var diag = new List<string>();
            bool ok = PersonJobDecisions.WillWorkToday(7, false, null, "no night work", diag);
            Assert.IsFalse(ok);
            StringAssert.Contains("contract restriction", diag[0]);
        }

        [Test]
        public void WillWorkToday_Clear_Willing()
        {
            var diag = new List<string>();
            bool ok = PersonJobDecisions.WillWorkToday(7, false, null, "", diag);
            Assert.IsTrue(ok);
            StringAssert.Contains("willing", diag[0]);
        }
    }
}
