using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: practice capability and referral — Canon §13.3D (shortages reduce
    /// what the practice can do) and §13.3E (refer to a larger off-map center
    /// when local capability is inadequate and travel is feasible).
    /// </summary>
    [TestFixture]
    public sealed class DoctorCapabilityReferralTests
    {
        private static DoctorPracticeCapabilityInput FullInputs()
        {
            return new DoctorPracticeCapabilityInput
            {
                PractitionerAvailable = true,
                PractitionerSkill = DoctorPractitionerSkill.Competent,
                HasDoctorBag = true,
                HasSurgicalSet = true,
                DressingsOnHand = 10,
                MedicinesOnHand = 20,
                TransportReady = true,
            };
        }

        private static JourneyModel NewJourneyWithOffMap()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("doctor-office", JourneyLocationKind.TownBuilding, "Doctor's Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("offmap-center", JourneyLocationKind.OffMapGateway, "Off-map medical center", 40f, 0f));
            journeys.AddEdge("doctor-office", "offmap-center", 40.0f, "railhead road");
            return journeys;
        }

        [Test]
        public void Evaluate_FullInputs_AllCapabilities()
        {
            var capability = DoctorPracticeCapability.Evaluate(FullInputs(), new List<string>());

            Assert.IsTrue(capability.CanDoOfficeVisits);
            Assert.IsTrue(capability.CanDoMinorProcedures);
            Assert.IsTrue(capability.CanDoHouseCalls);
            Assert.IsTrue(capability.CanHandleSerious(isInjury: true));
            Assert.IsTrue(capability.CanHandleSerious(isInjury: false));
        }

        [Test]
        public void Evaluate_NoPractitioner_NoCapacity_WithReasons()
        {
            var input = FullInputs();
            input.PractitionerAvailable = false;

            var capability = DoctorPracticeCapability.Evaluate(input, new List<string>());

            Assert.IsFalse(capability.CanDoOfficeVisits);
            Assert.IsFalse(capability.CanDoMinorProcedures);
            Assert.IsFalse(capability.CanDoHouseCalls);
            Assert.IsTrue(capability.LimitingFactors.Count > 0, "Capability loss must name its reasons.");
        }

        [Test]
        public void Evaluate_Shortages_ReduceWhatThePracticeCanDo()
        {
            var diag = new List<string>();

            var noTransport = FullInputs();
            noTransport.TransportReady = false;
            var held = DoctorPracticeCapability.Evaluate(noTransport, diag);
            Assert.IsTrue(held.CanDoOfficeVisits);
            Assert.IsFalse(held.CanDoHouseCalls, "Transport shortage holds house calls (Canon §13.3D).");

            var noDressings = FullInputs();
            noDressings.DressingsOnHand = 0;
            var noProc = DoctorPracticeCapability.Evaluate(noDressings, diag);
            Assert.IsFalse(noProc.CanDoMinorProcedures, "Dressing shortage holds procedures (Canon §13.3D).");
            Assert.IsFalse(noProc.CanHandleSerious(isInjury: true));

            var noSet = FullInputs();
            noSet.HasSurgicalSet = false;
            var noSetCap = DoctorPracticeCapability.Evaluate(noSet, diag);
            Assert.IsFalse(noSetCap.CanDoMinorProcedures);
            Assert.IsFalse(noSetCap.CanHandleSerious(isInjury: true));
            Assert.IsTrue(noSetCap.CanHandleSerious(isInjury: false),
                "Serious illness needs skill and medicines, not a surgical set.");
        }

        [Test]
        public void Evaluate_BasicPractitioner_CannotHandleSerious()
        {
            var input = FullInputs();
            input.PractitionerSkill = DoctorPractitionerSkill.Basic;

            var capability = DoctorPracticeCapability.Evaluate(input, new List<string>());

            Assert.IsFalse(capability.CanHandleSerious(isInjury: true));
            Assert.IsFalse(capability.CanHandleSerious(isInjury: false));
        }

        [Test]
        public void EvaluateReferral_MildCase_NeverReferred()
        {
            var capability = DoctorPracticeCapability.Evaluate(FullInputs(), new List<string>());
            var registry = new EntityIdRegistry();

            var referral = DoctorReferralPolicy.EvaluateReferral(
                EntityId.For(EntityKind.Person, 701), "mild fever", HouseCallSeverity.Moderate, false,
                capability, "Off-map medical center", "offmap-center", "doctor-office",
                NewJourneyWithOffMap(), registry, 300, new List<string>());

            Assert.Null(referral, "Mild cases stay local.");
        }

        [Test]
        public void EvaluateReferral_SeriousInjuryBeyondCapability_ReferredWithFeasibleTravel()
        {
            var input = FullInputs();
            input.HasSurgicalSet = false; // beyond local capability
            var capability = DoctorPracticeCapability.Evaluate(input, new List<string>());
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var referral = DoctorReferralPolicy.EvaluateReferral(
                EntityId.For(EntityKind.Person, 701), "compound fracture", HouseCallSeverity.Severe, true,
                capability, "Off-map medical center", "offmap-center", "doctor-office",
                NewJourneyWithOffMap(), registry, 300, diag);

            Assert.NotNull(referral, "Beyond local capability → refer (Canon §13.3E).");
            Assert.IsTrue(referral.ReferralId.IsValid);
            Assert.IsTrue(referral.TravelFeasible, "A real route exists to the off-map center.");
            Assert.AreEqual(40f, referral.TravelMiles);
            StringAssert.Contains("surgical set", referral.Reason.ToLowerInvariant());
            Assert.IsTrue(diag.Count > 0);
        }

        [Test]
        public void EvaluateReferral_WithinCapability_NotReferred()
        {
            var capability = DoctorPracticeCapability.Evaluate(FullInputs(), new List<string>());
            var registry = new EntityIdRegistry();

            var referral = DoctorReferralPolicy.EvaluateReferral(
                EntityId.For(EntityKind.Person, 701), "compound fracture", HouseCallSeverity.Severe, true,
                capability, "Off-map medical center", "offmap-center", "doctor-office",
                NewJourneyWithOffMap(), registry, 300, new List<string>());

            Assert.Null(referral, "Within local capability → treat locally, no referral.");
        }

        [Test]
        public void EvaluateReferral_NoRouteToOffMap_TravelNotFeasible_Noted()
        {
            var input = FullInputs();
            input.HasSurgicalSet = false;
            var capability = DoctorPracticeCapability.Evaluate(input, new List<string>());
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            // No edge to the off-map center: unreachable.
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("doctor-office", JourneyLocationKind.TownBuilding, "Doctor's Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("offmap-center", JourneyLocationKind.OffMapGateway, "Off-map medical center", 40f, 0f));

            var referral = DoctorReferralPolicy.EvaluateReferral(
                EntityId.For(EntityKind.Person, 701), "compound fracture", HouseCallSeverity.Severe, true,
                capability, "Off-map medical center", "offmap-center", "doctor-office",
                journeys, registry, 300, diag);

            Assert.NotNull(referral);
            Assert.IsFalse(referral.TravelFeasible, "Travel feasibility is a real route check, never assumed.");
            StringAssert.Contains("not feasible", referral.Reason);
        }
    }
}
