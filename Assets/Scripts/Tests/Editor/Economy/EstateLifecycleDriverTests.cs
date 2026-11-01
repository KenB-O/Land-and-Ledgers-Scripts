using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Estates;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass VII Workstream C: the runtime estate lifecycle driver, tested
    /// against the production authorities (EstateService, MortalityDriver,
    /// KinshipRegistry, TitleAuthority, FinancialObligationAuthority,
    /// HouseholdLedger). Slice E: provider death and estate. Slice F:
    /// insolvent estate.
    /// </summary>
    [TestFixture]
    public sealed class EstateLifecycleDriverTests
    {
        private sealed class World
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
            public PersonState AdultChild;
            public PersonState MinorChild;
            public Estate Estate;
        }

        private World BuildWorld(bool withFamily = true)
        {
            var w = new World();
            w.Day = CalendarAnchor.Default.ToAbsoluteDay(1892, 3, 1);
            w.Lifecycle = new HouseholdLifecycleManager(w.Population, w.Memberships, w.Kinship, w.Ids);
            w.Credit.AttachFinancialAuthority(w.Obligations);

            w.Decedent = AddPerson(w, "John", "Doe", 1847, 3, 1);   // 45
            if (withFamily)
            {
                w.Spouse = AddPerson(w, "Mary", "Doe", 1849, 6, 1);      // 42
                w.AdultChild = AddPerson(w, "Tom", "Doe", 1872, 1, 1);    // 20
                w.MinorChild = AddPerson(w, "Ann", "Doe", 1882, 5, 1);    // 9
                w.Kinship.AddSpouses(w.Decedent.id, w.Spouse.id, "test");
                w.Kinship.AddParentChild(w.Decedent.id, w.AdultChild.id, "test");
                w.Kinship.AddParentChild(w.Decedent.id, w.MinorChild.id, "test");
                w.Kinship.AddParentChild(w.Spouse.id, w.AdultChild.id, "test");
                w.Kinship.AddParentChild(w.Spouse.id, w.MinorChild.id, "test");
            }

            int householdId = 1;
            int membershipId = 1;
            foreach (PersonState person in w.Population.people)
            {
                Assert.IsNull(w.Memberships.Register(new HouseholdMembership
                {
                    MembershipId = membershipId++,
                    PersonId = person.id,
                    HouseholdId = householdId,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = w.Day - 365,
                }));
            }
            w.Ledgers.GetOrCreate(householdId);

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

        private PersonState AddPerson(World w, string first, string last, int birthYear, int birthMonth, int birthDay)
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

        private void OpenEstate(World w)
        {
            MortalityDriver.RecordDeath(w.DemoCtx, w.Decedent, "test cause");
            Assert.GreaterOrEqual(w.Decedent.deathDayIndex, 0, "death must be recorded exactly once through RecordDeath");
            var all = w.Estates.AllEstates();
            Assert.AreEqual(1, all.Count, "estate opens exactly once per death");
            w.Estate = all[0];
        }

        private void RegisterParcels(World w, params string[] parcelIds)
        {
            foreach (string parcelId in parcelIds)
            {
                w.Titles.RegisterParcel(parcelId, "test parcel", 40f, w.Decedent.DisplayName,
                    TitleBasis.Purchase, w.Day - 100, w.Diag);
            }
        }

        private string TitleHolder(World w, string parcelId)
        {
            return w.Titles.CurrentHolder(parcelId);
        }

        // ------------------------------------------------------------------
        // Slice E: provider death and estate
        // ------------------------------------------------------------------

        [Test]
        public void SliceE_DeathEndsEmploymentExactlyOnce()
        {
            World w = BuildWorld();
            var employment = new EmploymentRelationship
            {
                Id = "emp-1",
                EmployeePersonId = w.Decedent.id,
                EmployerBusinessId = "biz-1",
                LifecycleState = EmploymentLifecycleState.Active,
            };
            employment.Compensation = CompensationTerms.FromWeeklyWage(500, "test");
            Assert.IsNull(w.Employments.Register(employment));

            MortalityDriver.RecordDeath(w.DemoCtx, w.Decedent, "test cause");
            Assert.AreEqual(0, w.Employments.GetActiveByEmployee(w.Decedent.id).Count);
            Assert.AreEqual(EmploymentEndReason.Death, employment.EndReason);

            // A second RecordDeath is a no-op: the employment stays ended, no estate duplicated.
            MortalityDriver.RecordDeath(w.DemoCtx, w.Decedent, "test cause");
            Assert.AreEqual(EmploymentEndReason.Death, employment.EndReason);
            Assert.AreEqual(1, w.Estates.AllEstates().Count, "estate opens exactly once");
        }

        [Test]
        public void SliceE_EarnedWagesRemainAccountedFor()
        {
            World w = BuildWorld();
            HouseholdLedger ledger = w.Ledgers.GetOrCreate(1);
            Assert.IsNull(ledger.RecordWagePayment(w.Day - 7, "emp-1", w.Decedent.id, 500, "week 9"));
            Assert.IsNull(ledger.RecordWagePayment(w.Day, "emp-1", w.Decedent.id, 500, "week 10"));
            int balanceAtDeath = ledger.GetBalanceCents();

            MortalityDriver.RecordDeath(w.DemoCtx, w.Decedent, "test cause");

            Assert.AreEqual(balanceAtDeath, ledger.GetBalanceCents(),
                "earned wages already paid stay in the household ledger — death claws nothing back");
            Assert.AreEqual(0, w.Employments.GetActiveByEmployee(w.Decedent.id).Count,
                "and no further wages can accrue: the employment is ended");
        }

        [Test]
        public void SliceE_SurvivingHouseholdFinancesReflectActualIncomeLoss()
        {
            World w = BuildWorld();
            HouseholdLedger ledger = w.Ledgers.GetOrCreate(1);
            // The provider earns; the spouse earns a smaller wage.
            Assert.IsNull(ledger.RecordWagePayment(w.Day, "emp-provider", w.Decedent.id, 800, "week 10"));
            Assert.IsNull(ledger.RecordWagePayment(w.Day, "emp-spouse", w.Spouse.id, 300, "week 10"));
            int incomeBefore = 1100;

            var providerJob = new EmploymentRelationship
            {
                Id = "emp-provider",
                EmployeePersonId = w.Decedent.id,
                EmployerBusinessId = "biz-1",
                LifecycleState = EmploymentLifecycleState.Active,
            };
            providerJob.Compensation = CompensationTerms.FromWeeklyWage(800, "test");
            var spouseJob = new EmploymentRelationship
            {
                Id = "emp-spouse",
                EmployeePersonId = w.Spouse.id,
                EmployerBusinessId = "biz-1",
                LifecycleState = EmploymentLifecycleState.Active,
            };
            spouseJob.Compensation = CompensationTerms.FromWeeklyWage(300, "test");
            Assert.IsNull(w.Employments.Register(providerJob));
            Assert.IsNull(w.Employments.Register(spouseJob));

            MortalityDriver.RecordDeath(w.DemoCtx, w.Decedent, "test cause");

            // No widow-income grant, no replacement mechanic: the household keeps
            // exactly what was earned, and the provider's future income is gone.
            Assert.AreEqual(incomeBefore, ledger.GetBalanceCents());
            Assert.AreEqual(0, w.Employments.GetActiveByEmployee(w.Decedent.id).Count);
            Assert.AreEqual(EmploymentEndReason.Death, providerJob.EndReason);
            Assert.AreEqual(1, w.Employments.GetActiveByEmployee(w.Spouse.id).Count,
                "the surviving spouse's employment is untouched — only real earnings remain possible");
        }

        [Test]
        public void SliceE_FullLifecycle_ParcelToSoleHeir()
        {
            World w = BuildWorld(withFamily: false);
            w.Spouse = AddPerson(w, "Mary", "Doe", 1849, 6, 1);
            w.Kinship.AddSpouses(w.Decedent.id, w.Spouse.id, "test");
            Assert.IsNull(w.Memberships.Register(new HouseholdMembership
            {
                MembershipId = 99, PersonId = w.Spouse.id, HouseholdId = 1,
                Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = w.Day - 365,
            }));
            RegisterParcels(w, "p1");
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.AreEqual(EstateLifecycleStage.Opened, w.Driver.StageOf(estateId));
            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Inventoried, w.Driver.StageOf(estateId));
            Assert.IsTrue(w.Estate.ParcelIds.Contains("p1"), "the decedent's parcel is inventoried from the title authority");

            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.ClaimsAdjudicated, w.Driver.StageOf(estateId));

            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Distributed, w.Driver.StageOf(estateId));
            Assert.AreEqual(w.Spouse.DisplayName, TitleHolder(w, "p1"),
                "title moves only through the title authority, with Inheritance provenance");

            Assert.IsNull(w.Driver.CloseEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Closed, w.Driver.StageOf(estateId));
            Assert.AreEqual(EstateStatus.Closed, w.Estate.Status);
        }

        [Test]
        public void SliceE_MultipleHeirs_NoDuplicateAssets()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1", "p2");
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));

            // Each parcel has exactly one holder; no parcel is conjured or doubled.
            string holder1 = TitleHolder(w, "p1");
            string holder2 = TitleHolder(w, "p2");
            Assert.IsFalse(string.IsNullOrWhiteSpace(holder1));
            Assert.IsFalse(string.IsNullOrWhiteSpace(holder2));
            var heirs = new HashSet<string>
            {
                w.Spouse.DisplayName, w.AdultChild.DisplayName, w.MinorChild.DisplayName,
            };
            Assert.IsTrue(heirs.Contains(holder1), $"p1 holder '{holder1}' must be a real heir");
            Assert.IsTrue(heirs.Contains(holder2), $"p2 holder '{holder2}' must be a real heir");
            Assert.AreEqual(0, w.Estate.ParcelIds.Count, "every inventoried parcel distributed exactly once");
        }

        [Test]
        public void SliceE_MinorHeir_InterestPreservedInCustody()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1", "p2", "p3");
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            // The minor child is a real heir: spouse 1/3, each child 1/3 of the remainder's half.
            bool minorIsHeir = false;
            foreach (HeirShare heir in w.Estate.Heirs)
            {
                if (heir.PersonId == w.MinorChild.id && !heir.IsLifeInterest) minorIsHeir = true;
            }
            Assert.IsTrue(minorIsHeir, "the minor child is identified as an heir from the kinship registry");

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));

            EstateLifecycleProgress prog = w.Driver.ProgressFor(estateId);
            Assert.IsNotNull(prog);
            bool minorParcelInCustody = false;
            foreach (EstateMinorCustody custody in prog.MinorCustodies)
            {
                Assert.AreNotEqual(w.Decedent.id, custody.CustodianPersonId);
                Assert.GreaterOrEqual(custody.CustodianPersonId, 0, "a real custodian administers the minor's interest");
                if (custody.MinorPersonId == w.MinorChild.id && custody.AssetKind == "parcel")
                {
                    minorParcelInCustody = true;
                    Assert.AreEqual(w.Spouse.id, custody.CustodianPersonId,
                        "the surviving parent holds the custodial arrangement");
                    Assert.AreEqual(w.MinorChild.DisplayName, TitleHolder(w, custody.AssetRef),
                        "the parcel vests in the minor — the interest is the minor's, not the custodian's");
                }
            }
            Assert.IsTrue(minorParcelInCustody, "the minor's parcel interest is preserved in a custodial arrangement");
            // No child was ever given fiduciary authority.
            Assert.AreNotEqual(w.MinorChild.id, prog.FiduciaryPersonId);
            Assert.AreNotEqual(w.AdultChild.id, prog.FiduciaryPersonId,
                "with a living adult spouse, the spouse takes administration");
            Assert.AreEqual(w.Spouse.id, prog.FiduciaryPersonId);
        }

        [Test]
        public void SliceE_BusinessContinuity_OwnershipFollowsThePerson()
        {
            World w = BuildWorld(withFamily: false);
            w.Spouse = AddPerson(w, "Mary", "Doe", 1849, 6, 1);
            w.Kinship.AddSpouses(w.Decedent.id, w.Spouse.id, "test");
            Assert.IsNull(w.Memberships.Register(new HouseholdMembership
            {
                MembershipId = 99, PersonId = w.Spouse.id, HouseholdId = 1,
                Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = w.Day - 365,
            }));

            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(BusinessType.GeneralStore, "General Store");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "biz-1", profile, -1, BusinessOwnerIdentity.Npc(w.Decedent.id, w.Decedent.DisplayName, "Doe"));
            business.RuntimeState.AddCashCents(25000);
            w.Businesses["biz-1"] = business;
            int cashBefore = business.RuntimeState.CurrentCashCents;

            OpenEstate(w);
            string estateId = w.Estate.EstateId;
            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsTrue(w.Estate.BusinessInstanceIds.Contains("biz-1"),
                "the decedent's person-keyed business interest is inventoried");

            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));

            Assert.AreEqual(BusinessOwnerKind.Npc, business.Owner.OwnerKind,
                "ownership stays person-keyed (never a bare OwnerKind)");
            Assert.AreEqual(w.Spouse.id, business.Owner.PersonId, "the business follows the person to the heir");
            Assert.AreEqual(cashBefore, business.RuntimeState.CurrentCashCents,
                "the business's cash is preserved — death never erases the business's assets");
            Assert.IsNull(w.Driver.CloseEstate(estateId, w.Diag));
        }

        [Test]
        public void SliceE_IdentityAndKinshipHistoryPreserved()
        {
            World w = BuildWorld();
            OpenEstate(w);
            string estateId = w.Estate.EstateId;
            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.CloseEstate(estateId, w.Diag));

            Assert.IsNotNull(w.Population.GetPerson(w.Decedent.id), "the decedent's person record is retained");
            Assert.GreaterOrEqual(w.Decedent.deathDayIndex, 0);
            Assert.IsTrue(w.Kinship.GetRelated(w.Decedent.id, KinshipRelation.Spouse).Contains(w.Spouse.id),
                "kinship history is not deleted by death or by estate administration");
            Assert.IsTrue(w.Kinship.GetChildren(w.Decedent.id).Contains(w.AdultChild.id),
                "the decedent's children remain their children in the kinship record");
        }

        [Test]
        public void SliceE_SaveLoad_CannotDuplicateDistribution()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1", "p2");
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 60000, w.Day - 50, 0, 0, w.Diag);
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));
            HouseholdLedger ledger = w.Ledgers.GetOrCreate(1);
            int ledgerEntriesAfter = ledger.State.NextEntrySequence;
            string holder1Before = TitleHolder(w, "p1");

            // Save the driver's progress; load it into a fresh driver over the
            // same authorities; re-run every stage. Everything must be a no-op.
            EstateLifecycleDriver.EstateLifecycleDriverSaveDto dto = w.Driver.CaptureSaveDto();
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

            Assert.AreEqual(EstateLifecycleStage.Distributed, freshDriver.StageOf(estateId));
            Assert.IsNull(freshDriver.InventoryEstate(estateId, w.Diag), "re-inventory is a no-op");
            Assert.IsNull(freshDriver.AdjudicateClaims(estateId, w.Diag), "re-adjudication is a no-op");
            Assert.IsNull(freshDriver.DistributeEstate(estateId, w.Diag), "re-distribution is a no-op");

            Assert.AreEqual(holder1Before, TitleHolder(w, "p1"), "no duplicate title transfer");
            Assert.AreEqual(ledgerEntriesAfter, ledger.State.NextEntrySequence, "no duplicate cash placement");
            Assert.AreEqual(0, w.Estate.ParcelIds.Count);

            Assert.IsNull(freshDriver.CloseEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Closed, freshDriver.StageOf(estateId));
            Assert.IsNull(freshDriver.CloseEstate(estateId, w.Diag), "re-closure is a no-op");
        }

        [Test]
        public void SliceE_ValidClaimsAffectDistribution()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1");
            // A real T2A debt: the decedent made a note to a creditor.
            PromissoryNote note = w.Credit.IssuePromissoryNote(
                w.Ids, w.Decedent.DisplayName, "Creditor Name", 40000, "test terms", w.Day - 200,
                new List<string>(), w.Diag);
            Assert.IsNotNull(note);
            // The estate holds some cash, but not enough to matter for the guard itself.
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 10000, w.Day - 50, 0, 0, w.Diag);
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            string held = w.Driver.AdjudicateClaims(estateId, w.Diag);
            Assert.IsNotNull(held, "the claim cannot be paid in full — the estate is held");
            Assert.AreEqual(w.Decedent.DisplayName, TitleHolder(w, "p1"),
                "while debts are unsettled the parcel does not move — creditors before heirs");

            string refused = w.Driver.DistributeEstate(estateId, w.Diag);
            Assert.IsNotNull(refused, "distribution is refused while the valid claim is unsettled");
            Assert.AreEqual(w.Decedent.DisplayName, TitleHolder(w, "p1"));
        }

        [Test]
        public void SliceE_ClaimsPaidInFull_ThenDistributionProceeds()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1");
            PromissoryNote note = w.Credit.IssuePromissoryNote(
                w.Ids, w.Decedent.DisplayName, "Creditor Name", 40000, "test terms", w.Day - 200,
                new List<string>(), w.Diag);
            Assert.IsNotNull(note);
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 100000, w.Day - 50, 0, 0, w.Diag);
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag), "solvent estate settles its debts");
            Assert.IsTrue(w.Obligations.Find(note.ObligationId).Settled,
                "the shared obligation is settled through the financial authority — the liability stays recognized, then satisfied");

            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));
            Assert.AreNotEqual(w.Decedent.DisplayName, TitleHolder(w, "p1"));
            Assert.IsNull(w.Driver.CloseEstate(estateId, w.Diag));
            Assert.AreEqual(EstateLifecycleStage.Closed, w.Driver.StageOf(estateId));
        }

        // ------------------------------------------------------------------
        // Slice F: insolvent estate
        // ------------------------------------------------------------------

        [Test]
        public void SliceF_InsolventEstate_InventsNoFunds()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1");
            // Debt of 100000c against deposits of 20000c: insolvent.
            PromissoryNote note = w.Credit.IssuePromissoryNote(
                w.Ids, w.Decedent.DisplayName, "Creditor Name", 100000, "test terms", w.Day - 200,
                new List<string>(), w.Diag);
            Assert.IsNotNull(note);
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 20000, w.Day - 50, 0, 0, w.Diag);
            HouseholdLedger household = w.Ledgers.GetOrCreate(1);
            int householdBefore = household.GetBalanceCents();
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            string held = w.Driver.AdjudicateClaims(estateId, w.Diag);
            Assert.IsNotNull(held, "the insolvent estate is held — the claim cannot be paid in full");

            EstateAccount account = w.Admin.AccountFor(estateId);
            Assert.IsNotNull(account);
            Assert.AreEqual(20000, account.BalanceCents,
                "the estate holds exactly what the bank released — not a cent invented");
            Assert.IsFalse(w.Obligations.Find(note.ObligationId).Settled,
                "the debt is not erased by the shortfall");
            Assert.AreEqual(householdBefore, household.GetBalanceCents(),
                "the family's own cash is untouched — no automatic debt transfer to the family");

            string refused = w.Driver.DistributeEstate(estateId, w.Diag);
            Assert.IsNotNull(refused, "no distribution while creditors wait");
            Assert.AreEqual(w.Decedent.DisplayName, TitleHolder(w, "p1"),
                "the parcel stays with the estate — it is not conjured into anyone's hands");

            string closeRefused = w.Driver.CloseEstate(estateId, w.Diag);
            Assert.IsNotNull(closeRefused, "an insolvent estate never closes as if resolved");
            EstateLifecycleProgress prog = w.Driver.ProgressFor(estateId);
            Assert.IsTrue(prog.LegalHolds.Count > 0, "the shortfall is an explicit hold");
        }

        [Test]
        public void SliceF_BeneficiariesNotPersonallyLiable()
        {
            World w = BuildWorld();
            PromissoryNote note = w.Credit.IssuePromissoryNote(
                w.Ids, w.Decedent.DisplayName, "Creditor Name", 50000, "test terms", w.Day - 200,
                new List<string>(), w.Diag);
            Assert.IsNotNull(note);
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNotNull(w.Driver.AdjudicateClaims(estateId, w.Diag));

            FinancialObligation obligation = w.Obligations.Find(note.ObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(w.Decedent.DisplayName, obligation.Debtor,
                "the debt stays the decedent's — it is never rewritten onto a beneficiary");
            Assert.AreEqual(0, w.Obligations.TotalOutstandingFor(w.Spouse.DisplayName),
                "no heir carries the estate's debt on their own name");
        }

        [Test]
        public void SliceF_SecuredCreditor_RankedBeforeUnsecured()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1");
            // A mortgage (secured) and a note (unsecured), both owed by the decedent.
            MortgageDeed mortgage = w.Credit.IssueMortgage(
                w.Ids, w.Decedent.DisplayName, "Bank Lender", "p1", "the farm",
                60000, "test terms", w.Day - 300, w.Diag);
            Assert.IsNotNull(mortgage);
            PromissoryNote note = w.Credit.IssuePromissoryNote(
                w.Ids, w.Decedent.DisplayName, "Creditor Name", 20000, "test terms", w.Day - 200,
                new List<string>(), w.Diag);
            Assert.IsNotNull(note);
            // Enough cash for the mortgage but not both: the secured creditor is paid first.
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 70000, w.Day - 50, 0, 0, w.Diag);
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            string held = w.Driver.AdjudicateClaims(estateId, w.Diag);
            Assert.IsNotNull(held, "the unsecured claim cannot be paid in full — held");

            Assert.IsTrue(w.Obligations.Find(mortgage.ObligationId).Settled,
                "the secured creditor is paid first from the estate's real funds");
            Assert.IsFalse(w.Obligations.Find(note.ObligationId).Settled,
                "the unsecured creditor waits — and the estate invents nothing to pay them");
            EstateAccount account = w.Admin.AccountFor(estateId);
            Assert.AreEqual(10000, account.BalanceCents, "only real remainder cash sits in the account");
        }

        // ------------------------------------------------------------------
        // Remaining mandatory behaviors
        // ------------------------------------------------------------------

        [Test]
        public void EstateAssets_RetainValidProvenance()
        {
            World w = BuildWorld(withFamily: false);
            w.Spouse = AddPerson(w, "Mary", "Doe", 1849, 6, 1);
            w.Kinship.AddSpouses(w.Decedent.id, w.Spouse.id, "test");
            Assert.IsNull(w.Memberships.Register(new HouseholdMembership
            {
                MembershipId = 99, PersonId = w.Spouse.id, HouseholdId = 1,
                Lifecycle = HouseholdMembershipLifecycle.Active, StartDayIndex = w.Day - 365,
            }));
            RegisterParcels(w, "p1");
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));

            var chain = w.Titles.ChainOf("p1");
            Assert.IsNotNull(chain);
            Assert.GreaterOrEqual(chain.Count, 2, "the chain keeps its purchase link and gains the inheritance link");
            TitleRecord last = chain[chain.Count - 1];
            Assert.AreEqual(TitleBasis.Inheritance, last.Basis, "title changes only with a legitimate basis");
            Assert.AreEqual(w.Spouse.DisplayName, last.HolderName);
            Assert.AreEqual(estateId, last.InstrumentId, "the inheritance link cites the estate as its provenance");
        }

        [Test]
        public void Deposits_CollectedIntoEstateAccount_WithProvenance()
        {
            World w = BuildWorld();
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 45000, w.Day - 50, 0, 0, w.Diag);
            w.Bank.OpenAccount("dep-2", "Some Other Person", DepositKind.Demand, 99999, w.Day - 50, 0, 0, w.Diag);
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            List<EstateInventoryLine> lines = w.Admin.InventoryFor(estateId);
            int depositLines = 0;
            foreach (EstateInventoryLine line in lines)
            {
                if (line.Kind == EstateInventoryLineKind.DepositAccount) depositLines++;
                Assert.IsFalse(line.ReferenceId == "dep-2", "only the decedent's accounts marshal — nothing invented");
            }
            Assert.AreEqual(1, depositLines);

            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            EstateAccount account = w.Admin.AccountFor(estateId);
            Assert.AreEqual(45000, account.BalanceCents, "the deposit moved through a real bank withdrawal");
            Assert.AreEqual(0, BankBalance(w, "dep-1"), "the bank's own book shows the withdrawal");

            // Re-running collection is a no-op: no double withdrawal.
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.AreEqual(45000, account.BalanceCents);
        }

        private int BankBalance(World w, string accountId)
        {
            foreach (DepositAccount account in w.Bank.Accounts)
            {
                if (account.AccountId == accountId) return account.BalanceCents;
            }
            return -1;
        }

        [Test]
        public void Livestock_PersonOwned_AnimalsMarshalAsPersonalProperty()
        {
            World w = BuildWorld();
            var animals = new AnimalRegistry(w.Ids);
            AnimalState cow = animals.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Female,
                AnimalOwnerKind.Person, "P" + w.Decedent.id, w.Day - 100, "test");
            Assert.IsNotNull(cow);
            var ctx2 = RebuildContextWithAnimals(w, animals);
            w.Driver = new EstateLifecycleDriver(ctx2);

            OpenEstate(w);
            string estateId = w.Estate.EstateId;
            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));

            bool found = false;
            foreach (EstateInventoryLine line in w.Admin.InventoryFor(estateId))
            {
                if (line.Kind == EstateInventoryLineKind.PersonalProperty && line.Description.Contains("Cattle"))
                    found = true;
            }
            Assert.IsTrue(found, "the decedent's person-owned animal marshals from the real registry");
        }

        private EstateLifecycleDriverContext RebuildContextWithAnimals(World w, AnimalRegistry animals)
        {
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
                Animals = animals,
                Memberships = w.Memberships,
                Ledgers = w.Ledgers,
                Contests = w.Contests,
                Probate = w.Probate,
                CurrentDayIndex = w.Day,
            };
            ctx.DepositBooks.Add(w.Bank);
            foreach (var kv in w.Businesses) ctx.Businesses[kv.Key] = kv.Value;
            return ctx;
        }

        [Test]
        public void MultipleHeirs_BusinessInterest_UndividedSharesRecorded_NoDuplication()
        {
            World w = BuildWorld();
            BusinessProfileDefinition profile = BusinessProfileDefinition.CreateFallback(BusinessType.GeneralStore, "General Store");
            BusinessInstanceState business = BusinessInstanceState.Create(
                "biz-1", profile, -1, BusinessOwnerIdentity.Npc(w.Decedent.id, w.Decedent.DisplayName, "Doe"));
            w.Businesses["biz-1"] = business;
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));

            EstateLifecycleProgress prog = w.Driver.ProgressFor(estateId);
            Assert.AreEqual(3, prog.UndividedBusinessShares.Count,
                "each ownership heir holds one recorded undivided share — the asset is never duplicated");
            int managers = 0;
            foreach (EstateUndividedBusinessShare share in prog.UndividedBusinessShares)
            {
                Assert.AreEqual("biz-1", share.BusinessInstanceId);
                if (share.IsInterimManager) managers++;
            }
            Assert.AreEqual(1, managers, "exactly one interim manager operates the business");
            Assert.AreEqual(w.Spouse.id, business.Owner.PersonId, "the spouse operates as interim manager");
            Assert.IsTrue(prog.LegalHolds.Count > 0, "the undivided interest is an explicit legal hold for partition");
        }

        [Test]
        public void Inventory_PartialRun_RerunAddsNoDuplicateLines()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1");
            w.Bank.OpenAccount("dep-1", w.Decedent.DisplayName, DepositKind.Demand, 10000, w.Day - 50, 0, 0, w.Diag);
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            int linesAfterFull = w.Admin.InventoryFor(estateId).Count;
            Assert.Greater(linesAfterFull, 0);

            // Simulate a partial run: rewind the driver's stage and inventory again.
            EstateLifecycleProgress prog = w.Driver.ProgressFor(estateId);
            prog.Stage = EstateLifecycleStage.Opened;
            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag));
            Assert.AreEqual(linesAfterFull, w.Admin.InventoryFor(estateId).Count,
                "re-inventory after a partial run adds no duplicate lines");
            Assert.AreEqual(1, w.Estate.ParcelIds.Count, "the parcel is still inventoried exactly once");
        }

        [Test]
        public void TestateEstate_ParcelBequest_DistributesByProbatedWill()
        {
            World w = BuildWorld();
            RegisterParcels(w, "p1");
            OpenEstate(w);
            string estateId = w.Estate.EstateId;

            // The decedent left a will devising the parcel to the adult child;
            // it is probated through the real pipeline before distribution.
            Will will = w.Probate.RecordWill(w.Ids, w.Decedent.id, w.Decedent.DisplayName, w.Spouse.id,
                new List<int> { 9001, 9002 }, w.Day - 400, null, w.Diag);
            Assert.IsNotNull(will);
            Assert.IsNull(w.Probate.AddBequest(will, new Bequest
            {
                Kind = BequestKind.Parcel,
                ParcelId = "p1",
                BeneficiaryPersonId = w.AdultChild.id,
                BeneficiaryName = w.AdultChild.DisplayName,
            }, w.Population, w.Diag));
            Assert.IsNull(w.Probate.ProbateWill(will, w.Estate, w.Titles, w.Day - 10, w.Diag),
                "the will probates: real testator, devised parcel held by the testator");
            Assert.IsTrue(will.Probated);
            w.Estate.TestateWillId = will.WillId;

            Assert.IsNull(w.Driver.InventoryEstate(estateId, w.Diag),
                "the will-named executor takes administration from probate");
            EstateLifecycleProgress prog = w.Driver.ProgressFor(estateId);
            Assert.AreEqual(w.Spouse.id, prog.FiduciaryPersonId);
            Assert.IsNull(w.Driver.AdjudicateClaims(estateId, w.Diag));
            Assert.IsNull(w.Driver.DistributeEstate(estateId, w.Diag));
            Assert.AreEqual(w.AdultChild.DisplayName, TitleHolder(w, "p1"),
                "a probated will's parcel devise conveys through the title chain");
        }
    }
}
