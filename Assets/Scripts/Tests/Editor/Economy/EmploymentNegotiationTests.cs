using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker B: real employment agreements (Autonomous Labor, Phase C).
    /// Offer lifecycle Draft→Offered→Countered→Accepted/Refused/Withdrawn/Expired;
    /// accept registers a PendingStart EmploymentRelationship that goes Active on
    /// the start day; employment never grants ownership or residential rights;
    /// the offering person must hold offer authority (owner or HireStaff scope).
    /// Fixed day indices throughout; fully deterministic.
    /// </summary>
    [TestFixture]
    public sealed class EmploymentNegotiationTests
    {
        private const int OwnerId = 1;
        private const int CandidateId = 2;

        // Builds the service with a caller-supplied authority predicate:
        // (businessInstanceId, offeringPersonId, actName) → may offer.
        private static EmploymentNegotiationService ServiceWithAuthority(
            System.Func<string, int, string, bool> predicate)
        {
            return new EmploymentNegotiationService(predicate);
        }

        private static EmploymentOffer MakeStandardOffer(
            EmploymentNegotiationService service, int dayIndex, List<string> diag,
            string howHeard = "referred-by:P5", string housingNote = "", int startDayIndex = 404)
        {
            return service.MakeOffer(
                "store-1", CandidateId, OwnerId,
                wageCentsPerWeek: 300, duties: "Clerk", scheduleText: "dawn to dusk, six days",
                startDayIndex: startDayIndex, trialDays: 14, workLocation: "store-1 counter",
                durationDays: 0, housingNote: housingNote, authorityScope: "serve customers, keep the till",
                conditions: " sober on duty", terminationNoticeDays: 7,
                deliveryChannel: "InPerson", howHeard: howHeard, dayIndex: dayIndex, diag: diag);
        }

        [Test]
        public void MakeOffer_ValidOffer_ExtendedWithTerms()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => person == OwnerId);

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);

            Assert.IsNotNull(offer);
            Assert.AreEqual(OfferState.Offered, offer.State);
            Assert.AreEqual(300, offer.WageCentsPerWeek);
            Assert.AreEqual(404, offer.StartDayIndex);
            Assert.AreEqual(414, offer.ExpiresDayIndex, "14-day validity");
            Assert.AreEqual("referred-by:P5", offer.HowHeard, "provenance preserved verbatim");
            Assert.AreEqual(OwnerId, offer.OfferingPersonId);
            Assert.IsFalse(string.IsNullOrEmpty(offer.OfferId));
        }

        [Test]
        public void MakeOffer_AuthorityPredicateReceivesHireStaffAct()
        {
            var diag = new List<string>();
            string seenAct = null;
            var service = ServiceWithAuthority((biz, person, act) => { seenAct = act; return person == OwnerId; });

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);

            Assert.IsNotNull(offer);
            Assert.AreEqual(EmploymentNegotiationService.HireStaffAct, seenAct,
                "hub wires owner-or-HireStaff-scope through this act name");
        }

        [Test]
        public void MakeOffer_NoAuthority_Refused()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => false);

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);

            Assert.IsNull(offer, "employer without authority cannot offer");
            Assert.IsTrue(string.Join(" | ", diag).Contains("no offer authority"),
                "diagnostic explains the refusal: " + string.Join(" | ", diag));
        }

        [Test]
        public void MakeOffer_ForbiddenScope_Refused_NoRightsGuard()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);

            EmploymentOffer offer = service.MakeOffer(
                "store-1", CandidateId, OwnerId, 300, "Clerk", "days", 404, 0,
                "store-1", 0, "", "includes ownership of the store", "", 7,
                "InPerson", "", 400, diag);

            Assert.IsNull(offer, "GUARD: employment can never grant ownership");
            Assert.IsTrue(string.Join(" | ", diag).Contains("NEVER grants ownership"),
                "guard diagnostic: " + string.Join(" | ", diag));
        }

        [Test]
        public void MakeOffer_InvalidInputs_Refused()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);

            Assert.IsNull(service.MakeOffer("store-1", CandidateId, OwnerId, 0, "Clerk", "", 404, 0, "", 0, "", "", "", 0, "InPerson", "", 400, diag), "zero wage refused");
            Assert.IsNull(service.MakeOffer("store-1", CandidateId, OwnerId, 300, "Clerk", "", 399, 0, "", 0, "", "", "", 0, "InPerson", "", 400, diag), "start in the past refused");
            Assert.IsNull(service.MakeOffer("store-1", CandidateId, OwnerId, 300, "", "", 404, 0, "", 0, "", "", "", 0, "InPerson", "", 400, diag), "unstated duties refused");
            Assert.IsNull(service.MakeOffer("store-1", CandidateId, OwnerId, 300, "Clerk", "", 404, 0, "", 0, "", "", "", 0, "", "", 400, diag), "no delivery channel refused");
        }

        [Test]
        public void FullLifecycle_CounterThenAccept_RegistersPendingStart()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => person == OwnerId);
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNull(service.RecordCounter(offer.OfferId, 340, 401, diag));
            Assert.AreEqual(OfferState.Countered, offer.State);
            Assert.AreEqual(340, offer.CounterWageCentsPerWeek);

            Assert.IsNull(service.AcceptCounterOffer(offer.OfferId, OwnerId, 402, diag));

            Assert.IsNull(service.AcceptOffer(offer.OfferId, 402, registry, diag));
            Assert.AreEqual(OfferState.Accepted, offer.State);

            Assert.IsTrue(registry.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel));
            Assert.AreEqual(CandidateId, rel.EmployeePersonId);
            Assert.AreEqual("store-1", rel.EmployerBusinessId);
            Assert.AreEqual(EmploymentLifecycleState.PendingStart, rel.LifecycleState, "PendingStart until the start day");
            Assert.AreEqual(340, rel.Compensation.AgreedWeeklyWageCents, "countered wage is the agreed wage");
            Assert.AreEqual(404, rel.StartDayIndex);
            Assert.AreEqual(EmploymentSource.Manual, rel.Source);
            Assert.IsTrue(rel.LegacyNotes.Contains("Clerk"), "duties recorded: " + rel.LegacyNotes);
            Assert.IsTrue(rel.LegacyNotes.Contains("no ownership or residential rights"), "guard restated on the record");
        }

        [Test]
        public void Accept_OfferedWithoutCounter_UsesOfferedWage()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNull(service.AcceptOffer(offer.OfferId, 400, registry, diag));

            registry.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel);
            Assert.AreEqual(300, rel.Compensation.AgreedWeeklyWageCents);
        }

        [Test]
        public void PendingStart_BecomesActive_OnStartDay()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            service.AcceptOffer(offer.OfferId, 400, registry, diag);
            registry.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel);

            service.AdvanceDay(403, registry, diag);
            Assert.AreEqual(EmploymentLifecycleState.PendingStart, rel.LifecycleState, "still PendingStart the day before");

            service.AdvanceDay(404, registry, diag);
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState, "Active on the start day");
        }

        [Test]
        public void Refusal_IsTerminal_NoHire()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNull(service.RecordRefusal(offer.OfferId, "commute too long for the wage", 401, diag));
            Assert.AreEqual(OfferState.Refused, offer.State);
            Assert.IsTrue(offer.LastNote.Contains("commute too long"), "reason retained");

            Assert.IsNotNull(service.AcceptOffer(offer.OfferId, 401, registry, diag), "refused offer cannot be accepted");
            Assert.AreEqual(0, registry.Count, "no relationship created");
        }

        [Test]
        public void Withdraw_IsTerminal()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNull(service.RecordWithdraw(offer.OfferId, "role filled internally", 401, diag));
            Assert.AreEqual(OfferState.Withdrawn, offer.State);
            Assert.IsNotNull(service.RecordCounter(offer.OfferId, 350, 401, diag), "withdrawn offer cannot be countered");
        }

        [Test]
        public void Expiry_LapsesOpenOffer()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag); // expires day 414
            service.AdvanceDay(414, null, diag);
            Assert.AreEqual(OfferState.Offered, offer.State, "still open on the expiry day");

            service.AdvanceDay(415, null, diag);
            Assert.AreEqual(OfferState.Expired, offer.State, "lapsed past expiry");
            Assert.IsNotNull(service.RecordCounter(offer.OfferId, 350, 415, diag), "expired offer cannot be countered");
        }

        [Test]
        public void Accept_AfterExpiry_Fails()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNotNull(service.AcceptOffer(offer.OfferId, 415, registry, diag));
            Assert.AreEqual(OfferState.Expired, offer.State);
            Assert.AreEqual(0, registry.Count, "no late hire");
        }

        [Test]
        public void HousingNote_RecordedAsLinkedNoteOnly()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag,
                housingNote: "room above the store while employed");
            Assert.IsNull(service.AcceptOffer(offer.OfferId, 400, registry, diag));

            registry.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel);
            Assert.IsTrue(rel.LegacyNotes.Contains("room above the store while employed"),
                "housing/board term preserved as a note");
            Assert.IsTrue(rel.LegacyNotes.Contains("LINKED NOTE ONLY"),
                "GUARD: recorded as a linked note, never a property transfer");
            Assert.IsTrue(rel.LegacyNotes.Contains("no property transfer"),
                "GUARD: explicit boundary on the record");
        }

        [Test]
        public void UnknownOffer_ReturnsErrors()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);
            var registry = new EmploymentRelationshipRegistry();

            Assert.IsNotNull(service.RecordCounter("offer-nope", 300, 400, diag));
            Assert.IsNotNull(service.RecordRefusal("offer-nope", "x", 400, diag));
            Assert.IsNotNull(service.RecordWithdraw("offer-nope", "x", 400, diag));
            Assert.IsNotNull(service.AcceptOffer("offer-nope", 400, registry, diag));
        }

        [Test]
        public void AcceptOffer_NullRegistry_Refused()
        {
            var diag = new List<string>();
            var service = ServiceWithAuthority((biz, person, act) => true);

            EmploymentOffer offer = MakeStandardOffer(service, 400, diag);
            Assert.IsNotNull(service.AcceptOffer(offer.OfferId, 400, null, diag),
                "employment is a real registered record, never implied");
            Assert.AreEqual(OfferState.Offered, offer.State, "offer left open for a retry with a registry");
        }

        [Test]
        public void SaveLoad_RoundTrip_NoDuplicateHire()
        {
            var diag = new List<string>();
            var serviceA = ServiceWithAuthority((biz, person, act) => true);
            var registry = new EmploymentRelationshipRegistry();

            EmploymentOffer offer = MakeStandardOffer(serviceA, 400, diag);
            serviceA.RecordCounter(offer.OfferId, 320, 401, diag);
            Assert.IsNull(serviceA.AcceptCounterOffer(offer.OfferId, OwnerId, 402, diag));
            serviceA.AcceptOffer(offer.OfferId, 402, registry, diag);
            Assert.AreEqual(1, registry.Count);

            EmploymentNegotiationSaveDto dto = serviceA.CaptureSaveDto();
            var serviceB = ServiceWithAuthority((biz, person, act) => true);
            serviceB.LoadFromSaveDto(dto);

            // The accepted offer cannot be accepted again: reload never re-hires.
            Assert.IsNotNull(serviceB.AcceptOffer(offer.OfferId, 403, registry, diag), "no duplicate hire on reload");
            Assert.AreEqual(1, registry.Count, "registry first-wins holds");

            // The accepted-pair mapping round-tripped: the PendingStart→Active flip still works.
            serviceB.AdvanceDay(404, registry, diag);
            registry.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel);
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState);

            // Sequence round-tripped: new offers get fresh ids, terminal states stay terminal.
            EmploymentOffer second = MakeStandardOffer(serviceB, 405, diag, startDayIndex: 410);
            Assert.IsNotNull(second, "new offer after reload");
            Assert.AreNotEqual(offer.OfferId, second.OfferId, "no id reuse after reload");
            Assert.IsNotNull(serviceB.RecordCounter(offer.OfferId, 999, 405, diag), "accepted offer stays terminal");
        }

        [Test]
        public void Reload_TerminalOffersStayTerminal()
        {
            var diag = new List<string>();
            var serviceA = ServiceWithAuthority((biz, person, act) => true);

            EmploymentOffer refused = MakeStandardOffer(serviceA, 400, diag);
            serviceA.RecordRefusal(refused.OfferId, "found other work", 401, diag);

            var serviceB = ServiceWithAuthority((biz, person, act) => true);
            serviceB.LoadFromSaveDto(serviceA.CaptureSaveDto());

            Assert.IsTrue(serviceB.TryGetOffer(refused.OfferId, out EmploymentOffer restored));
            Assert.AreEqual(OfferState.Refused, restored.State, "terminal state survives reload");
            var registry = new EmploymentRelationshipRegistry();
            Assert.IsNotNull(serviceB.AcceptOffer(refused.OfferId, 402, registry, diag), "reload never re-hires");
            Assert.AreEqual(0, registry.Count);
        }
    }
}
