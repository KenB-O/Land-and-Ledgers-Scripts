// Pass III Worker E: save/load idempotency across the integrated slice (§15).
// Covers: A. before subdivision; B. after; C. repeated restore; D. rent owing;
// E. partial rent paid; F. active employment; G. capability-changing task;
// H. pending shopping/financial decisions; I. old-save invents nothing.
// Uses the production CaptureSaveDto/LoadFromSaveDto on every Pass III service.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.World;
using LandLedgers.World.Property;

namespace LandLedgers.Editor.Orchestration
{
    [TestFixture]
    public sealed class Pass3SliceSaveLoadTests
    {
        private List<string> Diag() => new List<string>();
        private static EntityId P(int personId) => EntityId.For(EntityKind.Person, personId);

        private sealed class PayeeDirectory : ITenancyPayeeDirectory
        {
            public int FindPayeeHouseholdId(string payeeHolderId) =>
                string.Equals(payeeHolderId, "P4", StringComparison.Ordinal) ? 4 : -1;
            public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId) => null;
        }

        private sealed class Snapshot
        {
            public ParcelServiceSaveDto Parcels;
            public PropertyInterestStoreSaveDto Interests;
            public TitleAuthority.TitleSaveDto Titles;
            public HousingAuthority.HousingSaveDto Housing;
            public TenancyPaymentService.TenancyPaymentSaveDto Tenancy;
            public List<HouseholdLedgerState> Ledgers = new List<HouseholdLedgerState>();
            public List<HouseholdInventoryState> Inventories = new List<HouseholdInventoryState>();
            public SkillServiceSaveDto Skills;
            public EmploymentRegistrySaveDto Employments;
            public HouseholdDecisionServiceSaveDto Decisions;
            public FinancialObligationSaveDto Obligations;
        }

        private sealed class World
        {
            public ParcelService Parcels = new ParcelService();
            public PropertyInterestStore Interests = new PropertyInterestStore();
            public ParcelSubdivisionService Subdivider;
            public TitleAuthority Titles = new TitleAuthority();
            public HousingAuthority Housing = new HousingAuthority();
            public TenancyPaymentService Tenancy = new TenancyPaymentService();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public SkillService Skills = new SkillService();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public EmploymentNegotiationService Negotiation =
                new EmploymentNegotiationService((biz, person, act) => true);
            public HouseholdDecisionService Decisions = new HouseholdDecisionService();
            public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public PayeeDirectory Payees = new PayeeDirectory();
            public string FarmParcelId;
            public string AgreementId;
        }

        private Snapshot Capture(World w)
        {
            var s = new Snapshot
            {
                Parcels = w.Parcels.CaptureSaveDto(),
                Interests = w.Interests.CaptureSaveDto(),
                Titles = w.Titles.CaptureSaveDto(),
                Housing = w.Housing.CaptureSaveDto(),
                Tenancy = w.Tenancy.CaptureSaveDto(),
                Skills = w.Skills.CaptureSaveDto(),
                Employments = w.Employments.CaptureSaveDto(),
                Decisions = w.Decisions.CaptureSaveDto(),
                Obligations = w.Obligations.CaptureSaveDto(),
            };
            w.Ledgers.ExportState(s.Ledgers);
            w.Inventories.ExportState(s.Inventories);
            return s;
        }

        private World Restore(Snapshot s)
        {
            var w = new World();
            w.Subdivider = new ParcelSubdivisionService(w.Parcels, w.Interests);
            w.Parcels.LoadFromSaveDto(s.Parcels);
            w.Interests.LoadFromSaveDto(s.Interests);
            w.Titles.LoadFromSaveDto(s.Titles);
            w.Housing.LoadFromSaveDto(s.Housing);
            w.Tenancy.LoadFromSaveDto(s.Tenancy);
            w.Ledgers.ImportState(s.Ledgers);
            w.Inventories.ImportState(s.Inventories);
            w.Skills.LoadFromSaveDto(s.Skills);
            w.Employments.LoadFromSaveDto(s.Employments);
            w.Decisions.LoadFromSaveDto(s.Decisions);
            w.Obligations.LoadFromSaveDto(s.Obligations);
            return w;
        }

        private World BuildToRentPartial()
        {
            var w = new World();
            var diag = Diag();
            w.Subdivider = new ParcelSubdivisionService(w.Parcels, w.Interests);

            // Land: farm + house lot + public land.
            var farm = w.Parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(0, 0, 200, 200) },
                "40-acre farm", "agriculture", "county", LandStatus.PrivateTitled, "", "road frontage");
            var houseLot = w.Parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(300, 20, 20, 10) },
                "town lot 12", "residential", "county", LandStatus.PrivateTitled, "", "lane access");
            w.FarmParcelId = farm.Id;
            Assert.IsNotNull(w.Titles.RegisterParcel(farm.Id, "farm", 40f, "P2", TitleBasis.Purchase, 480, diag));
            Assert.IsNotNull(w.Titles.RegisterParcel(houseLot.Id, "lot 12", 0.5f, "P4", TitleBasis.Purchase, 481, diag));
            w.Interests.Record(new PropertyInterest
            {
                Id = PropertyInterestStore.NewInterestId(), ParcelId = farm.Id,
                Kind = InterestKind.Easement, HolderId = "County", StartDay = 485, Notes = "road easement",
            });

            // Housing + tenancy: H1 rents from P4, 300c/30d, due day 529.
            var house = w.Housing.RegisterBuilding("house-1", houseLot.Id, "house", 482, "test", diag);
            var space = w.Housing.DefineSpace("house-1", 5, diag);
            w.Housing.Occupy(1, 1, space.SpaceId, AccommodationArrangement.Rental, 500, diag);
            var agreement = w.Housing.RecordAgreement("rental", "P4", "H1", space.SpaceId, "rental", 500, -1, "", diag);
            w.AgreementId = agreement.AgreementId;
            w.Housing.AttachPaymentTerms(agreement.AgreementId, new TenancyPaymentTerms
            {
                AmountCents = 300, PeriodDays = 30, DueDayOfPeriod = 30,
                StartDay = 500, PayeeHolderId = "P4", TenantHouseholdId = 1,
            }, diag);

            // Cash: H1 400 -> spends to 270 before rent day.
            w.Ledgers.GetOrCreate(1).RecordInflow(500, 400, HouseholdIncomeSource.OtherDocumented, "opening", "H1", "self");
            w.Ledgers.GetOrCreate(4).RecordInflow(500, 5000, HouseholdIncomeSource.OtherDocumented, "opening", "H4", "self");
            Assert.IsNull(w.Ledgers.GetOrCreate(1).RecordOutflow(528, 130, "spending", "town"));

            // Employment: P1 hired at the store via negotiation.
            var offer = w.Negotiation.MakeOffer("store-1", 1, 3, 320, "clerk", "six days",
                530, 90, "", 0, "none", "duties", "conduct", 7, "InPerson", "witnessed", 528, diag);
            w.Negotiation.AcceptOffer(offer.OfferId, 529, w.Employments, diag);
            w.Negotiation.AdvanceDay(530, w.Employments, diag);

            // Decision history: H1 sought income.
            w.Decisions.Evaluate(new HouseholdPressureInput
            {
                HouseholdId = "1", Day = 528, ReservesCents = 270, ObligationsDueCents = 300,
                ExpectedIncomeCentsPerDay = 0, Dependents = 2, HasSearchableContacts = true,
            });

            // Skill: one genuine episode for P1.
            w.Skills.RegisterStarterSet();
            Assert.IsTrue(w.Skills.RecordTaskExperience(P(1), new ExperienceFormationInputs
            {
                TaskCapabilityId = SkillIds.Stocking, Difficulty01 = 0.5, Novelty01 = 0.8,
                Supervised = false, Outcome = "Success", MinutesSpent = 50, Day = 530,
            }));

            // Rent day: partial payment (270 of 300), 30c arrears.
            var results = w.Tenancy.SettleDueRents(w.Housing, 529, w.Ledgers, w.Payees, w.Obligations, w.Ids, diag);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("settled-partial", results[0].Outcome);
            return w;
        }

        [Test]
        public void SaveLoad_BeforeSubdivision_NoDuplication()
        {
            // A. Save/load before subdivision: parcel/interest/title state round-trips exactly.
            var w = BuildToRentPartial();
            Snapshot s = Capture(w);
            World r = Restore(s);

            Assert.AreEqual(w.Parcels.ParcelCount, r.Parcels.ParcelCount);
            Assert.AreEqual(2, r.Parcels.ParcelCount);
            ParcelRecord farm = r.Parcels.GetParcel(w.FarmParcelId);
            Assert.IsNotNull(farm);
            Assert.IsFalse(farm.IsRetired);
            Assert.AreEqual(1, r.Interests.ActiveForParcel(w.FarmParcelId).Count);
            Assert.AreEqual("P2", r.Titles.CurrentHolder(w.FarmParcelId));
            Assert.AreEqual(1, r.Titles.ChainOf(w.FarmParcelId).Count); // initial link only
        }

        [Test]
        public void SaveLoad_AfterSubdivision_LineagePreserved()
        {
            // B. Subdivide, save, restore: children + lineage + retired parent survive.
            var w = BuildToRentPartial();
            var diag = Diag();
            w.Subdivider.TitleHolderLookup = pid => w.Titles.CurrentHolder(pid);
            var result = w.Subdivider.Execute(new SubdivisionRequest
            {
                ParcelId = w.FarmParcelId, RequestedByHolder = "P2", Day = 540,
                ResultingFootprints = new List<List<GridRect>>
                {
                    new List<GridRect> { new GridRect(0, 0, 100, 200) },
                    new List<GridRect> { new GridRect(100, 0, 100, 200) },
                },
            });
            Assert.IsTrue(result.Success);

            Snapshot s = Capture(w);
            World r = Restore(s);

            Assert.AreEqual(4, r.Parcels.ParcelCount); // 2 originals + 2 children
            Assert.IsTrue(r.Parcels.GetParcel(w.FarmParcelId).IsRetired);
            foreach (string childId in result.NewParcelIds)
            {
                ParcelRecord child = r.Parcels.GetParcel(childId);
                Assert.IsNotNull(child);
                Assert.AreEqual(w.FarmParcelId, child.ParentParcelIds[0]);
                Assert.AreEqual(1, r.Interests.ActiveForParcel(childId).Count); // easement carried
            }
        }

        [Test]
        public void SaveLoad_RepeatedRestore_NoDuplicates()
        {
            // C. Restoring the same post-subdivision snapshot twice changes nothing.
            var w = BuildToRentPartial();
            var diag = Diag();
            w.Subdivider.TitleHolderLookup = pid => w.Titles.CurrentHolder(pid);
            var result = w.Subdivider.Execute(new SubdivisionRequest
            {
                ParcelId = w.FarmParcelId, RequestedByHolder = "P2", Day = 540,
                ResultingFootprints = new List<List<GridRect>>
                {
                    new List<GridRect> { new GridRect(0, 0, 100, 200) },
                    new List<GridRect> { new GridRect(100, 0, 100, 200) },
                },
            });
            Assert.IsTrue(result.Success);
            Snapshot s = Capture(w);

            World r1 = Restore(s);
            World r2 = Restore(s); // same snapshot, fresh services
            Assert.AreEqual(r1.Parcels.ParcelCount, r2.Parcels.ParcelCount);
            Assert.AreEqual(4, r2.Parcels.ParcelCount);
            Assert.AreEqual(
                r1.Interests.ActiveForParcel(result.NewParcelIds[0]).Count,
                r2.Interests.ActiveForParcel(result.NewParcelIds[0]).Count);
            Assert.AreEqual("P2", r2.Titles.CurrentHolder(w.FarmParcelId));
        }

        [Test]
        public void SaveLoad_RentOwing_PartialPayment_NoDoubleSettle()
        {
            // D/E. Rent owing + partial payment survive; re-settling never double-debits.
            var w = BuildToRentPartial();
            var diag = Diag();
            Snapshot s = Capture(w);
            World r = Restore(s);

            // Obligation state: 270 paid of 300, 30c arrears liability.
            RentObligationState state = r.Tenancy.FindObligation(w.AgreementId, 0);
            Assert.IsNotNull(state);
            Assert.AreEqual(270, state.PaidCents);
            Assert.AreEqual(RentObligationStatus.Partial, state.Status);
            Assert.IsFalse(string.IsNullOrEmpty(state.ArrearsObligationId));
            Assert.AreEqual(30, r.Obligations.Find(state.ArrearsObligationId).OutstandingPrincipalCents);

            // Re-driving the same due day settles nothing twice.
            int h1Before = r.Ledgers.GetOrCreate(1).GetBalanceCents();
            int h4Before = r.Ledgers.GetOrCreate(4).GetBalanceCents();
            var repeat = r.Tenancy.SettleDueRents(r.Housing, 529, r.Ledgers, r.Payees, r.Obligations, r.Ids, diag);
            Assert.AreEqual(1, repeat.Count);
            Assert.AreEqual("already-settled", repeat[0].Outcome);
            Assert.AreEqual(h1Before, r.Ledgers.GetOrCreate(1).GetBalanceCents());
            Assert.AreEqual(h4Before, r.Ledgers.GetOrCreate(4).GetBalanceCents());
            // No duplicate arrears obligation was created.
            Assert.AreEqual(1, CountObligations(r));
        }

        private static int CountObligations(World w)
        {
            int n = 0;
            foreach (var o in w.Obligations.Obligations) n++;
            return n;
        }

        [Test]
        public void SaveLoad_ActiveEmployment_Preserved()
        {
            // F. The employment relationship (terms, lifecycle) survives restore.
            var w = BuildToRentPartial();
            World r = Restore(Capture(w));

            int found = 0;
            foreach (EmploymentRelationship rel in r.Employments.GetByEmployee(1))
            {
                if (rel.EmployerBusinessId == "store-1")
                {
                    found++;
                    Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState);
                    Assert.AreEqual(320, rel.Compensation.AgreedWeeklyWageCents);
                }
            }
            Assert.AreEqual(1, found); // exactly one: no duplicated employment
        }

        [Test]
        public void SaveLoad_Capability_NoDoubleCredit()
        {
            // G. Capability survives; restoring the same snapshot twice does not
            // re-apply formation (entry-level idempotency via EntryId).
            var w = BuildToRentPartial();
            double before = w.Skills.GetCapability(P(1), SkillIds.Stocking);
            Assert.Greater(before, 0.10);

            Snapshot s = Capture(w);
            World r = Restore(s);
            Assert.AreEqual(before, r.Skills.GetCapability(P(1), SkillIds.Stocking), 1e-9);

            // Loading the same DTO again changes nothing: no duplicated entries,
            // no repeated formation credit.
            r.Skills.LoadFromSaveDto(s.Skills);
            Assert.AreEqual(before, r.Skills.GetCapability(P(1), SkillIds.Stocking), 1e-9);
            Assert.Greater(r.Skills.GetExperienceRelevance(P(1), SkillIds.Stocking), 0.0);
        }

        [Test]
        public void SaveLoad_DecisionHistory_Preserved()
        {
            // H. The recorded SeekIncome decision survives restore.
            var w = BuildToRentPartial();
            World r = Restore(Capture(w));
            HouseholdDecision last = r.Decisions.LastDecisionFor("1");
            Assert.IsNotNull(last);
            Assert.AreEqual(HouseholdDecisionKind.SeekIncome, last.Kind);
            Assert.AreEqual(528, last.Day);
        }

        [Test]
        public void SaveLoad_OldSave_InventsNothing()
        {
            // I. An old save (ledgers only, no tenancy/agreements) loads without
            // inventing ownership, persons, or obligations.
            var w = BuildToRentPartial();
            var oldLedgers = new List<HouseholdLedgerState>();
            var bare = new HouseholdLedgerRegistry();
            bare.GetOrCreate(1).RecordInflow(400, 400, HouseholdIncomeSource.OtherDocumented, "opening", "H1", "self");
            bare.ExportState(oldLedgers);

            var r = new World();
            r.Ledgers.ImportState(oldLedgers);
            // No housing, no tenancy, no titles loaded: nothing invented.
            Assert.AreEqual(0, r.Housing.AllBuildings().Count);
            Assert.AreEqual(0, r.Parcels.ParcelCount);
            Assert.AreEqual(400, r.Ledgers.GetOrCreate(1).GetBalanceCents());
            // Settling rents with no agreements considers nothing.
            var results = r.Tenancy.SettleDueRents(r.Housing, 529, r.Ledgers, r.Payees, r.Obligations, r.Ids, Diag());
            Assert.AreEqual(0, results.Count);
            Assert.AreEqual(400, r.Ledgers.GetOrCreate(1).GetBalanceCents());
        }
    }
}
