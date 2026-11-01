using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Estates;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Orchestration
{
    /// <summary>
    /// Pass VII slice L: save/load and repeated execution. Each test
    /// interrupts a REAL operation mid-flight, restores it into fresh
    /// services, continues it to completion, and asserts exactly-once
    /// outcomes plus conservation:
    /// (1) purchases: an in-transit external purchase arrives exactly once;
    /// (2) animal reservations: a mid-term horse rental returns exactly once;
    /// (3) estate administration: a mid-inventory estate continues to
    /// closure with exactly-once distribution;
    /// (4) manager operations: covered by
    /// ManagerDecisionExecutionTests.SaveLoad_PreservesLedger_NoDuplicateExecutionAfterRestore
    /// (procurement executed -> save -> restore -> the same decision is
    /// refused as a duplicate -> the in-transit shipment still delivers);
    /// (5) information transmission: a mid-chain rumor continues to the
    /// next recipient exactly once, provenance intact.
    /// Existing per-area save/load tests prove round-trip and re-import
    /// idempotency; the gap closed here is interrupt -> restore -> continue
    /// -> exactly-once completion.
    /// </summary>
    [TestFixture]
    public sealed class Pass7SliceLSaveLoadContinuityTests
    {
        // ---------- (1) purchases: interrupt an in-transit external purchase ----------

        [Test]
        public void L_Purchases_InterruptInTransit_Restore_ArriveExactlyOnce()
        {
            var authority = new ExternalMarketAuthority();
            authority.SetDayIndex(100);
            var diag = new List<string>();
            Assert.IsNull(authority.RegisterCounterparty(new ExternalCounterparty
            {
                CounterpartyId = "st-louis-wholesaler",
                DisplayName = "St. Louis Wholesaler",
                InitialBudgetCents = 10000,
                TransitDays = 6,
            }));
            authority.GetCounterparty("st-louis-wholesaler").SupplyUnits["tools_hardware"] = 60;

            // The purchase is real: finite supply decremented, outflow
            // recorded, fulfillment modeled with a 6-day transit delay.
            string receipt = authority.RecordExternalPurchase(
                "st-louis-wholesaler", "tools_hardware", 20, 10000, diag);
            Assert.IsFalse(string.IsNullOrEmpty(receipt), "purchase must record: " + string.Join(" | ", diag.ToArray()));
            Assert.AreEqual(40, authority.GetCounterparty("st-louis-wholesaler").SupplyUnits["tools_hardware"]);
            Assert.AreEqual(1, authority.Trades.Count);
            Assert.IsFalse(authority.Trades[0].Fulfilled, "the goods are in transit, not yet arrived");

            // INTERRUPT: save mid-operation, into a fresh authority.
            ExternalMarketAuthoritySaveDto dto = authority.ExportState();
            var restored = new ExternalMarketAuthority();
            restored.ImportState(dto);

            Assert.AreEqual(1, restored.Trades.Count, "no duplicated trade records across save/load");
            ExternalTradeRecord trade = restored.Trades[0];
            Assert.AreEqual(receipt, trade.ReceiptId);
            Assert.IsFalse(trade.Fulfilled, "the purchase is still in transit after restore");
            Assert.AreEqual(40, restored.GetCounterparty("st-louis-wholesaler").SupplyUnits["tools_hardware"],
                "supply was decremented exactly once — no double-deduction across save/load");
            Assert.IsTrue(trade.Provenance.Contains("st-louis-wholesaler"));

            // CONTINUE to completion: the goods arrive on day 106.
            List<ExternalTradeRecord> arrived = restored.ResolveArrivals(106);
            Assert.AreEqual(1, arrived.Count, "the in-transit purchase must complete after restore");
            Assert.IsTrue(trade.Fulfilled);

            // EXACTLY-ONCE: a second resolution finds nothing to fulfill.
            Assert.AreEqual(0, restored.ResolveArrivals(106).Count, "arrival is not a duplicable event");
            Assert.AreEqual(1, restored.Trades.Count);

            // CONSERVATION: boundary totals recomputed from records, never doubled.
            (int inflow, int outflow, int net) = restored.GetBoundaryReconciliation();
            Assert.AreEqual(0, inflow);
            Assert.AreEqual(10000, outflow);
            Assert.AreEqual(-10000, net);
        }

        // ---------- (2) animal reservations: interrupt a mid-term rental ----------

        private sealed class RentalFixture
        {
            public readonly EntityIdRegistry Ids = new EntityIdRegistry();
            public readonly AnimalRegistry Registry;
            public readonly EquipmentReservationService Reservations = new EquipmentReservationService();
            public readonly AnimalAvailabilityAuthority Availability;
            public readonly HorseRentalService Rentals = new HorseRentalService();
            public readonly FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public readonly List<string> Diag = new List<string>();

            public RentalFixture()
            {
                Registry = new AnimalRegistry(Ids);
                Availability = new AnimalAvailabilityAuthority(Reservations);
            }

            public AnimalState AddHorse()
            {
                AnimalState horse = Registry.RegisterAnimal(
                    AnimalSpecies.Horse, "draft", AnimalSex.Female, AnimalOwnerKind.Household, "H-1", 0, "test herd");
                Assert.IsNull(HorseAuthority.Configure(
                    horse, HorseUseCapabilities.GeneralPurpose, "farm-1", "stable-1", 0));
                horse.Health01 = 0.9f;
                horse.Fatigue01 = 0.1f;
                return horse;
            }
        }

        [Test]
        public void L_AnimalRental_InterruptMidTerm_Restore_ReturnExactlyOnce()
        {
            var f = new RentalFixture();
            AnimalState horse = f.AddHorse();
            float rentalSeconds = 100 * 86400f + 1000f;

            // The rental is real: authorized by the owner, the horse is held
            // in the availability authority, custody moves to the borrower,
            // and the 500c fee becomes one Payable obligation (credit route).
            var request = new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "household",
                BorrowerId = "H-2",
                BorrowerName = "Neighbor Household",
                FromDayIndex = 100,
                ToDayIndex = 106,
                FeeCents = 500,
                TermsNote = "return in working condition",
                Availability = f.Availability,
                GameSecondsPerDay = 86400f,
                DayIndex = 100,
                Obligations = f.Obligations,
                Ids = f.Ids,
            };
            HorseRentalRecord record = f.Rentals.RentOut(request, f.Diag);
            Assert.IsNotNull(record, "rental must start: " + string.Join(" | ", f.Diag.ToArray()));
            Assert.AreEqual(HorseRentalStatus.Active, record.Status);
            Assert.IsFalse(f.Availability.IsAvailable(horse.AnimalId.ToString(), rentalSeconds),
                "the rented horse is held — no double-booking");
            Assert.AreEqual("household", horse.CustodianKind);
            Assert.AreEqual("H-2", horse.CustodianId);
            Assert.AreEqual(1, f.Obligations.Obligations.Count, "the fee became exactly one obligation");

            // INTERRUPT: save the reservation authority, the rental service,
            // and the registry mid-term.
            var authorityDto = f.Reservations.CaptureSaveDto();
            HorseRentalServiceSaveDto rentalDto = f.Rentals.CaptureSaveDto();
            var animals = new List<AnimalState>();
            f.Registry.ExportState(animals,
                new List<HistoricalAnimalRecord>(), new List<LivestockCohort>(), new List<EggBatchState>());

            // RESTORE into fresh services.
            var restoredReservations = new EquipmentReservationService();
            restoredReservations.LoadFromSaveDto(authorityDto);
            var restoredAvailability = new AnimalAvailabilityAuthority(restoredReservations);
            var restoredRentals = new HorseRentalService();
            restoredRentals.LoadFromSaveDto(rentalDto);
            var restoredRegistry = new AnimalRegistry(new EntityIdRegistry());
            restoredRegistry.ImportState(animals, null, null, null);

            HorseRentalRecord restoredRecord = restoredRentals.ActiveRentalFor(horse.AnimalId);
            Assert.IsNotNull(restoredRecord, "the active rental must survive the restore");
            Assert.AreEqual(HorseRentalStatus.Active, restoredRecord.Status);
            Assert.AreEqual(500, restoredRecord.FeeCents, "the fee survives the restore");
            Assert.IsFalse(restoredAvailability.IsAvailable(horse.AnimalId.ToString(), rentalSeconds),
                "the live hold survives the restore — capacity is not conjured");
            AnimalState restoredHorse = restoredRegistry.GetAnimal(horse.AnimalId);
            Assert.IsNotNull(restoredHorse);
            Assert.AreEqual("H-2", restoredHorse.CustodianId, "borrower custody survives the restore");
            Assert.AreEqual(0.9f, restoredHorse.Health01, "condition survives the restore");
            Assert.AreEqual(0.1f, restoredHorse.Fatigue01, "condition survives the restore");

            // CONTINUE to completion: the horse is returned on day 107.
            Assert.IsNull(restoredRentals.ReturnHorse(
                restoredRegistry, horse.AnimalId, 107, restoredAvailability, f.Diag),
                "return must succeed: " + string.Join(" | ", f.Diag.ToArray()));
            Assert.AreEqual(HorseRentalStatus.Returned, restoredRecord.Status);
            Assert.AreEqual(107, restoredRecord.ReturnedDayIndex);
            Assert.AreEqual(0.9f, restoredRecord.ReturnHealth01, "the return condition is snapshotted once");
            Assert.IsTrue(restoredAvailability.IsAvailable(horse.AnimalId.ToString(), rentalSeconds),
                "the hold is released exactly once — capacity restored, never doubled");
            Assert.AreEqual("H-1", restoredHorse.CustodianId, "custody restored to the owner");

            // EXACTLY-ONCE: returning again is refused; nothing re-settles.
            Assert.IsNotNull(restoredRentals.ReturnHorse(
                restoredRegistry, horse.AnimalId, 108, restoredAvailability, f.Diag),
                "a second return must be refused — no active rental remains");
            Assert.AreEqual(1, restoredRentals.Rentals.Count, "no duplicated rental records");
            Assert.AreEqual(HorseRentalStatus.Returned, restoredRecord.Status);
            Assert.AreEqual(107, restoredRecord.ReturnedDayIndex, "the return day is not overwritten");
            Assert.AreEqual(1, f.Obligations.Obligations.Count,
                "the fee obligation was created exactly once — no duplicate fee");
            FinancialObligation fee = null;
            foreach (FinancialObligation o in f.Obligations.Obligations) fee = o;
            Assert.IsNotNull(fee);
            Assert.AreEqual(500, fee.OriginalPrincipalCents, "the fee amount is conserved");
        }

        // ---------- (3) estate administration: interrupt mid-inventory, continue to closure ----------

        private sealed class EstateWorld
        {
            public int Day;
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public PopulationState Population = new PopulationState();
            public KinshipRegistry Kinship = new KinshipRegistry();
            public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
            public HouseholdLifecycleManager Lifecycle;
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public TitleAuthority Titles = new TitleAuthority();
            public EstateService Estates = new EstateService();
            public EstateAdministrationService Admin = new EstateAdministrationService();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public CreditRegistry Credit = new CreditRegistry();
            public BankDepositLedger Bank = new BankDepositLedger("Test Bank");
            public Dictionary<string, BusinessInstanceState> Businesses = new Dictionary<string, BusinessInstanceState>();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public ProbateService Probate = new ProbateService();
            public WillContestService Contests = new WillContestService();
            public EstateLifecycleDriver Driver;
            public List<string> Diag = new List<string>();
            public DemographicDriverContext DemoCtx;

            public PersonState Decedent;
            public PersonState Spouse;
            public Estate Estate;
        }

        private EstateWorld BuildEstateWorld()
        {
            var w = new EstateWorld();
            w.Day = CalendarAnchor.Default.ToAbsoluteDay(1892, 3, 1);
            w.Lifecycle = new HouseholdLifecycleManager(w.Population, w.Memberships, w.Kinship, w.Ids);
            w.Credit.AttachFinancialAuthority(w.Obligations);

            w.Decedent = AddEstatePerson(w, "John", "Doe", 1847, 3, 1);
            w.Spouse = AddEstatePerson(w, "Mary", "Doe", 1849, 6, 1);
            w.Kinship.AddSpouses(w.Decedent.id, w.Spouse.id, "test");

            int membershipId = 1;
            foreach (PersonState person in w.Population.people)
            {
                Assert.IsNull(w.Memberships.Register(new HouseholdMembership
                {
                    MembershipId = membershipId++,
                    PersonId = person.id,
                    HouseholdId = 1,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = w.Day - 365,
                }));
            }
            w.Ledgers.GetOrCreate(1);

            var ctx = new EstateLifecycleDriverContext
            {
                Estates = w.Estates,
                Administration = w.Admin,
                Ids = w.Ids,
                Kinship = w.Kinship,
                Population = w.Population,
                Anchor = CalendarAnchor.Default,
                Titles = w.Titles,
                Obligations = w.Obligations,
                Credit = w.Credit,
                Memberships = w.Memberships,
                Ledgers = w.Ledgers,
                Contests = w.Contests,
                Probate = w.Probate,
                CurrentDayIndex = w.Day,
                Businesses = w.Businesses,
            };
            ctx.DepositBooks.Add(w.Bank);
            w.Driver = new EstateLifecycleDriver(ctx);

            w.DemoCtx = new DemographicDriverContext
            {
                Population = w.Population,
                Kinship = w.Kinship,
                Lifecycle = w.Lifecycle,
                Memberships = w.Memberships,
                Estates = w.Estates,
                Ids = w.Ids,
                Ledgers = w.Ledgers,
                Employments = w.Employments,
                Anchor = CalendarAnchor.Default,
                CurrentDayIndex = w.Day,
                WorldSeed = 7,
            };
            return w;
        }

        private PersonState AddEstatePerson(EstateWorld w, string first, string last, int birthYear, int birthMonth, int birthDay)
        {
            var person = new PersonState
            {
                id = w.Population.AllocateNextPersonId(),
                firstName = first,
                lastName = last,
                birthDate = new BirthDate(birthYear, birthMonth, birthDay, BirthDateProvenance.Exact),
            };
            CalendarAnchor.Default.FromAbsoluteDay(w.Day, out int y, out int m, out int d);
            person.RefreshDerivedAge(y, m, d);
            w.Population.people.Add(person);
            return person;
        }

        [Test]
        public void L_EstateAdministration_InterruptMidInventory_Restore_ContinueToClosureExactlyOnce()
        {
            EstateWorld w = BuildEstateWorld();
            foreach (string parcelId in new[] { "p1", "p2" })
            {
                w.Titles.RegisterParcel(parcelId, "test parcel", 40f, w.Decedent.DisplayName,
                    TitleBasis.Purchase, w.Day - 100, w.Diag);
            }
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 60000, w.Day - 50, 0, 0, w.Diag);

            MortalityDriver.RecordDeath(w.DemoCtx, w.Decedent, "test cause");
            var all = w.Estates.AllEstates();
            Assert.AreEqual(1, all.Count, "estate opens exactly once per death");
            w.Estate = all[0];
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Inventoried, w.Driver.StageOf(estateId));
            Assert.IsTrue(w.Estate.ParcelIds.Contains("p1") && w.Estate.ParcelIds.Contains("p2"),
                "both parcels are inventoried from the title authority");

            // INTERRUPT: save the driver's progress mid-administration.
            EstateLifecycleDriver.EstateLifecycleDriverSaveDto dto = w.Driver.CaptureSaveDto();

            // RESTORE a fresh driver over the same authorities.
            var freshCtx = new EstateLifecycleDriverContext
            {
                Estates = w.Estates,
                Administration = w.Admin,
                Ids = w.Ids,
                Population = w.Population,
                Anchor = CalendarAnchor.Default,
                Titles = w.Titles,
                Obligations = w.Obligations,
                Credit = w.Credit,
                Memberships = w.Memberships,
                Ledgers = w.Ledgers,
                Contests = w.Contests,
                Probate = w.Probate,
                CurrentDayIndex = w.Day,
                Businesses = w.Businesses,
            };
            freshCtx.DepositBooks.Add(w.Bank);
            var freshDriver = new EstateLifecycleDriver(freshCtx);
            freshDriver.LoadFromSaveDto(dto);

            Assert.AreEqual(EstateLifecycleStage.Inventoried, freshDriver.StageOf(estateId),
                "the restored driver resumes mid-administration, not from the start");

            // CONTINUE to completion.
            Assert.IsNull(freshDriver.AdjudicateClaims(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.ClaimsAdjudicated, freshDriver.StageOf(estateId));

            HouseholdLedger heirLedger = w.Ledgers.GetOrCreate(1);
            int balanceBefore = heirLedger.GetBalanceCents();
            int entriesBefore = heirLedger.State.NextEntrySequence;
            Assert.IsNull(freshDriver.DistributeEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Distributed, freshDriver.StageOf(estateId));

            // EXACTLY-ONCE: each parcel vests in the sole heir once; the
            // 60000c bank deposit moves bank -> estate account -> heir ledger
            // once (the deposit was really withdrawn from the bank book).
            Assert.AreEqual(w.Spouse.DisplayName, w.Titles.CurrentHolder("p1"),
                "title moves only through the title authority, with Inheritance provenance");
            Assert.AreEqual(w.Spouse.DisplayName, w.Titles.CurrentHolder("p2"));
            Assert.AreEqual(60000, heirLedger.GetBalanceCents() - balanceBefore,
                "the full bank deposit is placed exactly once — no partial, no double");
            Assert.AreEqual(0, w.Estate.ParcelIds.Count, "every inventoried parcel distributed exactly once");

            // Re-running the completed stages is a no-op: nothing duplicates.
            int entriesAfter = heirLedger.State.NextEntrySequence;
            Assert.Greater(entriesAfter, entriesBefore, "distribution really wrote ledger entries");
            Assert.IsNull(freshDriver.InventoryEstate(estateId, w.Diag), "re-inventory is a no-op");
            Assert.IsNull(freshDriver.AdjudicateClaims(estateId, w.Diag), "re-adjudication is a no-op");
            Assert.IsNull(freshDriver.DistributeEstate(estateId, w.Diag), "re-distribution is a no-op");
            Assert.AreEqual(entriesAfter, heirLedger.State.NextEntrySequence, "no duplicate cash placement");
            Assert.AreEqual(w.Spouse.DisplayName, w.Titles.CurrentHolder("p1"), "no duplicate title transfer");
            Assert.AreEqual(w.Spouse.DisplayName, w.Titles.CurrentHolder("p2"), "no duplicate title transfer");

            Assert.IsNull(freshDriver.CloseEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Closed, freshDriver.StageOf(estateId));
            Assert.AreEqual(EstateStatus.Closed, w.Estate.Status);
            Assert.IsNull(freshDriver.CloseEstate(estateId, w.Diag), "re-closure is a no-op");
        }

        // ---------- (5) information transmission: interrupt mid-chain, continue the rumor ----------

        private sealed class InfoFixture
        {
            public const int Day = 410;
            public readonly Dictionary<int, PersonState> Persons = new Dictionary<int, PersonState>();
            public readonly Dictionary<string, BusinessInstanceState> Businesses =
                new Dictionary<string, BusinessInstanceState>(System.StringComparer.Ordinal);
            public readonly PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public readonly EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public readonly KnowledgeTransmissionStore Knowledge = new KnowledgeTransmissionStore();
            public readonly List<string> Diagnostics = new List<string>();

            public ConversationOpportunityService Opportunities()
            {
                return new ConversationOpportunityService(
                    Schedules,
                    id => Persons.TryGetValue(id, out PersonState person) ? person : null,
                    Employments,
                    id =>
                    {
                        Businesses.TryGetValue(id, out BusinessInstanceState business);
                        return business;
                    });
            }
        }

        private static PersonState MakeInfoPerson(int id, string firstName)
        {
            return new PersonState { id = id, firstName = firstName, lastName = "Test" };
        }

        private static BusinessInstanceState MakeInfoBusiness(string id)
        {
            var business = new BusinessInstanceState();
            System.Reflection.FieldInfo field = business.GetType().GetField("instanceId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "instanceId field");
            field.SetValue(business, id);
            Assert.IsTrue(business.TryAssignPremises(7), "premises assigned");
            return business;
        }

        private static void VisitInfo(InfoFixture fixture, int personId, string venue, int startMinute, int duration, SocialVisitMotivation motivation)
        {
            string refusal = SocialVisitPlanner.PlanSocialVisit(
                personId, venue, InfoFixture.Day, startMinute, duration, motivation,
                fixture.Schedules, fixture.Diagnostics);
            Assert.IsNull(refusal, "visit planned: " + refusal);
        }

        [Test]
        public void L_InformationTransmission_InterruptMidChain_Restore_ContinueExactlyOnce()
        {
            var fixture = new InfoFixture();
            fixture.Persons[101] = MakeInfoPerson(101, "Foreman");
            fixture.Persons[102] = MakeInfoPerson(102, "Bartender");
            fixture.Persons[103] = MakeInfoPerson(103, "JobSeeker");
            fixture.Businesses["saloon-1"] = MakeInfoBusiness("saloon-1");
            VisitInfo(fixture, 101, "saloon-1", 19 * 60, 90, SocialVisitMotivation.Recreation);
            VisitInfo(fixture, 102, "saloon-1", 19 * 60, 120, SocialVisitMotivation.Friendship);
            VisitInfo(fixture, 103, "saloon-1", 19 * 60, 120, SocialVisitMotivation.JobSeeking);

            // P101 observes the mine trouble firsthand and tells P102.
            InformationPacket observed = InformationPacketFactory.Observe(
                "mine-trouble", "the north shaft floods", 101, InfoFixture.Day, 0.8f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), fixture.Knowledge, 101, 102, InfoFixture.Day, observed, fixture.Diagnostics),
                "first hop must succeed: " + string.Join(" | ", fixture.Diagnostics.ToArray()));
            Assert.AreEqual(1, fixture.Knowledge.PacketsFor(102, "mine-trouble").Count);

            // INTERRUPT: save the knowledge store mid-chain.
            KnowledgeTransmissionSaveDto dto = fixture.Knowledge.CaptureSaveDto();
            var restored = new KnowledgeTransmissionStore();
            restored.LoadFromSaveDto(dto);

            // The first hop survives the restore, provenance intact.
            List<InformationPacket> heldBy102 = restored.PacketsFor(102, "mine-trouble");
            Assert.AreEqual(1, heldBy102.Count, "no duplicated packets across save/load");
            Assert.AreEqual("101", heldBy102[0].TransmissionChain);
            Assert.AreEqual("direct-observation", heldBy102[0].SourceKind);
            Assert.AreEqual(0.8f, heldBy102[0].Confidence01);

            // CONTINUE to completion: P102 relays to P103.
            InformationPacket relay = InformationPacketFactory.Relay(
                heldBy102[0], 102, 103, InfoFixture.Day, 0.6f);
            Assert.IsNull(ConversationRunner.Converse(
                fixture.Opportunities(), restored, 102, 103, InfoFixture.Day, relay, fixture.Diagnostics),
                "the chain must continue after restore: " + string.Join(" | ", fixture.Diagnostics.ToArray()));

            List<InformationPacket> heldBy103 = restored.PacketsFor(103, "mine-trouble");
            Assert.AreEqual(1, heldBy103.Count, "P103 holds exactly one telling");
            Assert.AreEqual("101->102", heldBy103[0].TransmissionChain,
                "the chain extends by exactly one real link — no fabricated independent source");
            Assert.AreEqual("conversation", heldBy103[0].SourceKind, "the relay is honestly secondhand");
            Assert.LessOrEqual(heldBy103[0].Confidence01, 0.8f,
                "confidence degrades with retelling — it never improves by relaying");

            // EXACTLY-ONCE: retelling the same telling to P103 is refused.
            InformationPacket retell = InformationPacketFactory.Relay(
                heldBy102[0], 102, 103, InfoFixture.Day, 0.6f);
            Assert.IsNotNull(ConversationRunner.Converse(
                fixture.Opportunities(), restored, 102, 103, InfoFixture.Day, retell, fixture.Diagnostics),
                "hearing the same rumor twice must not create a second record");
            Assert.AreEqual(1, restored.PacketsFor(103, "mine-trouble").Count);
            Assert.AreEqual(1, restored.PacketsFor(102, "mine-trouble").Count);
        }
    }
}
