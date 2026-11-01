using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using NUnit.Framework;

/// <summary>
/// Pass XL phase 3: family trees, immigration, items/freight,
/// through-travelers, settlement, reputation, and outmigration.
/// Plain C#, Roslyn-harness runnable. All rates, thresholds, weights,
/// distances, and prices in these tests are TEST PARAMETERS, never
/// canon. The six-person slice gate is tested, not bypassed.
/// </summary>
[TestFixture]
public sealed class PassXlPhase3FamilyImmigrationItemsTests
{
    private static PersonState NewPerson(PopulationState population, string first, string last, int age, bool isFemale)
    {
        var person = new PersonState
        {
            id = population.AllocateNextPersonId(),
            firstName = first,
            lastName = last,
            age = age,
            isFemale = isFemale,
            isOffMap = false,
            deathDayIndex = -1,
            householdId = -1,
        };
        population.people.Add(person);
        return person;
    }

    private static (PopulationState, KinshipRegistry) TwoPersonHousehold()
    {
        var population = new PopulationState();
        var kinship = new KinshipRegistry();
        // Jacob (owner, General Store in the playtest example) and a
        // second resident, both with no parents yet.
        NewPerson(population, "Jacob", "Boniecki", 34, false);
        NewPerson(population, "Grace", "Boniecki", 30, true);
        return (population, kinship);
    }

    // ---- Item 2: family trees ----

    [Test]
    public void FamilyTree_EveryoneGetsParentsGrandparentsAndSiblings()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        // Two siblings sharing parents: add a third resident, then link
        // them as children of the same (future) parents via the builder
        // is not possible, so instead test the builder's own sibling
        // output: after the tree, children of the same parent are
        // siblings. Build the tree, then check a resident's parents
        // each have parents (grandparents), and re-running adds nothing.
        var diag = new List<string>();
        FamilyTreeBuilder.Report report = FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, diag);

        Assert.IsTrue(report.ParentsCreated >= 4); // 2 per resident
        Assert.IsTrue(report.GrandparentsCreated >= 4);
        foreach (PersonState resident in population.people)
        {
            if (resident.isOffMap) continue;
            Assert.AreEqual(2, kinship.GetParents(resident.id).Count);
            foreach (int parentId in kinship.GetParents(resident.id))
                Assert.AreEqual(2, kinship.GetParents(parentId).Count); // grandparents
        }
        int peopleAfterFirst = population.people.Count;
        int edgesAfterFirst = kinship.EdgeCount;
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        Assert.AreEqual(peopleAfterFirst, population.people.Count); // idempotent
        Assert.AreEqual(edgesAfterFirst, kinship.EdgeCount);
        // No person is their own ancestor.
        foreach (PersonState person in population.people)
        {
            Assert.IsFalse(kinship.GetParents(person.id).Contains(person.id));
            Assert.IsFalse(kinship.GetRelated(person.id, KinshipRelation.Sibling).Contains(person.id));
        }
    }

    [Test]
    public void FamilyTree_SiblingsAreWritten_WhenChildrenShareAParent()
    {
        var population = new PopulationState();
        var kinship = new KinshipRegistry();
        PersonState a = NewPerson(population, "Anna", "Hale", 20, true);
        PersonState b = NewPerson(population, "Ben", "Hale", 18, false);
        PersonState father = NewPerson(population, "Carl", "Hale", 45, false);
        kinship.AddParentChild(father.id, a.id, "test");
        kinship.AddParentChild(father.id, b.id, "test");
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 3, referenceYear: 1880, new List<string>());
        Assert.IsTrue(kinship.GetRelated(a.id, KinshipRelation.Sibling).Contains(b.id));
        Assert.IsTrue(kinship.GetRelated(b.id, KinshipRelation.Sibling).Contains(a.id));
    }

    [Test]
    public void FamilyTree_OffMapRelativeMaterialises_WithoutDuplication()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        PersonState resident = population.people[0];
        int parentId = kinship.GetParents(resident.id)[0];
        PersonState parent = population.GetPerson(parentId);
        Assert.IsTrue(parent.isOffMap);
        int before = population.people.Count;
        Assert.IsTrue(FamilyTreeBuilder.MaterialiseOffMapRelative(population, parentId, householdId: 1, homeBuildingId: 5, new List<string>()));
        Assert.AreEqual(before, population.people.Count);
        Assert.IsFalse(population.GetPerson(parentId).isOffMap);
        // Second materialisation refuses, no duplicate.
        Assert.IsFalse(FamilyTreeBuilder.MaterialiseOffMapRelative(population, parentId, 1, 5, new List<string>()));
        Assert.AreEqual(before, population.people.Count);
    }

    // ---- Item 3: immigration + reputation ----

    private static TownReputationSnapshot Reputation(double housing, double work, double growth, double opinion)
    {
        return TownReputationModel.Compute(
            new TownReputationInputs
            {
                HousingVacantUnits = (int)(housing * 10), HousingTotalUnits = 10,
                OpenJobSlots = (int)(work * 10), FilledJobSlots = 10 - (int)(work * 10),
                GrowthScore01 = growth, GoodOpinionScore01 = opinion,
            },
            new TownReputationWeights { Housing = 0.3, Work = 0.5, Growth = 0.1, Opinion = 0.1 },
            dayIndex: 10);
    }

    [Test]
    public void Reputation_RisesWithHousingAndWork_AndNoWorkIsNotAttractive()
    {
        TownReputationSnapshot good = Reputation(0.8, 0.8, 0.5, 0.8);
        TownReputationSnapshot badWork = Reputation(1.0, 0.0, 0.5, 0.8);
        Assert.IsTrue(good.Score01 > badWork.Score01);
        Assert.IsTrue(badWork.Score01 < 0.5); // plentiful housing cannot rescue no work
        Assert.IsTrue(good.Score01 > 0.5);
    }

    [Test]
    public void Immigration_NoWorkOrNoHousingOrSuppression_BlocksEvenWithGoodReputation()
    {
        var thresholds = new ImmigrationThresholds { MinOpenJobs = 1, MinHousingVacancies = 1, MinReputationScore01 = 0.5 };
        TownReputationSnapshot good = Reputation(0.8, 0.8, 0.5, 0.8);
        Assert.IsFalse(ImmigrationConditions.Evaluate(good, housingVacancies: 2, openJobs: 0, allowNewArrivals: true, thresholds).Eligible);
        Assert.IsFalse(ImmigrationConditions.Evaluate(good, housingVacancies: 0, openJobs: 3, allowNewArrivals: true, thresholds).Eligible);
        Assert.IsFalse(ImmigrationConditions.Evaluate(good, housingVacancies: 2, openJobs: 3, allowNewArrivals: false, thresholds).Eligible);
        Assert.IsTrue(ImmigrationConditions.Evaluate(good, housingVacancies: 2, openJobs: 3, allowNewArrivals: true, thresholds).Eligible);
    }

    [Test]
    public void Immigration_StaleReputation_IsRefusedUntilRecomputed()
    {
        var thresholds = new ImmigrationThresholds { MinOpenJobs = 1, MinHousingVacancies = 1, MinReputationScore01 = 0.5 };
        TownReputationSnapshot old = Reputation(0.8, 0.8, 0.5, 0.8);
        Assert.IsFalse(ImmigrationConditions.EvaluateAtDay(old, currentDayIndex: 30, maxReputationAgeDays: 7,
            housingVacancies: 2, openJobs: 3, allowNewArrivals: true, thresholds).Eligible);
    }

    [Test]
    public void Recruitment_JacobCanRecommendAnOffMapRelative_ForTheGeneralStore()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        PersonState jacob = population.people[0];
        var diag = new List<string>();
        List<RecruitmentLead> leads = RecruitmentConversation.AskAboutWorkers(
            jacob.id, population, kinship, ConversationChannelKind.Household,
            businessId: "general-store", isPlayerOwnedBusiness: true, dayIndex: 5, diag);
        Assert.IsTrue(leads.Count >= 1);
        foreach (RecruitmentLead lead in leads)
        {
            Assert.AreEqual("general-store", lead.BusinessId);
            Assert.IsTrue(population.GetPerson(lead.ContactPersonId).isOffMap);
        }
        RecruitmentLetterRequest letter = RecruitmentConversation.BuildLetterRequest(leads[0], dayIndex: 5);
        Assert.AreEqual("general-store", letter.Origin);
        Assert.AreEqual(leads[0].ContactPersonId, letter.RecipientPersonId);
        Assert.AreEqual("work-at-business", letter.Purpose);
    }

    [Test]
    public void Recruitment_NoChannel_OrNonPlayerBusiness_IsRefused()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        PersonState jacob = population.people[0];
        Assert.AreEqual(0, RecruitmentConversation.AskAboutMovers(
            jacob.id, population, kinship, ConversationChannelKind.Unspecified, 5, new List<string>()).Count);
        Assert.AreEqual(0, RecruitmentConversation.AskAboutWorkers(
            jacob.id, population, kinship, ConversationChannelKind.Household,
            businessId: "bakery", isPlayerOwnedBusiness: false, dayIndex: 5, new List<string>()).Count);
    }

    // ---- Item 5B.1: settlement ----

    [Test]
    public void Settlement_WithoutHousingOrJobPlan_DoesNotSilentlySucceed()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        int contactId = kinship.GetParents(population.people[0].id)[0];
        Assert.IsFalse(ArrivalSettlement.Settle(population, new ArrivalSettlementRequest
        {
            PersonId = contactId, HousingUnitId = string.Empty, AvailableHousingUnits = 0,
            ExistingHouseholdId = -1, JobBusinessId = string.Empty, ExplicitUnemployedWithPlan = false, DayIndex = 6,
        }, new List<string>()).Settled);
        Assert.IsTrue(population.GetPerson(contactId).isOffMap); // still off map
        Assert.IsFalse(ArrivalSettlement.Settle(population, new ArrivalSettlementRequest
        {
            PersonId = contactId, HousingUnitId = "house-3", AvailableHousingUnits = 1,
            ExistingHouseholdId = -1, JobBusinessId = string.Empty, ExplicitUnemployedWithPlan = false, DayIndex = 6,
        }, new List<string>()).Settled);
    }

    [Test]
    public void Settlement_SettledArrival_HasHousingHouseholdAndJobOrExplicitPlan()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        int contactId = kinship.GetParents(population.people[0].id)[0];
        ArrivalSettlementRecord record = ArrivalSettlement.Settle(population, new ArrivalSettlementRequest
        {
            PersonId = contactId, HousingUnitId = "house-3", AvailableHousingUnits = 1,
            ExistingHouseholdId = -1, JobBusinessId = "general-store", OpenJobSlotsAtBusiness = 1, DayIndex = 6,
        }, new List<string>());
        Assert.IsTrue(record.Settled);
        Assert.AreEqual("house-3", record.HousingUnitId);
        Assert.IsTrue(record.HouseholdId >= 0);
        Assert.AreEqual("general-store", record.JobBusinessId);
        Assert.IsFalse(population.GetPerson(contactId).isOffMap);
    }

    // ---- Item 4: items + suppliers + freight ----

    [Test]
    public void Items_TiersAreDisjoint_AndEveryTierIdExistsInCatalog()
    {
        Assert.IsTrue(GeneralStoreItemTiers.TiersAreDisjoint());
        Assert.AreEqual(0, GeneralStoreItemTiers.MissingCatalogIds().Count);
        Assert.IsTrue(HouseholdItemCatalog.Get("cigarettes") != null);
        Assert.IsTrue(HouseholdItemCatalog.Get("sugar") != null);
    }

    [Test]
    public void Suppliers_TwoSuppliersSameSku_DifferentDistances_DifferentDeliveredCost()
    {
        var catalog = new OffMapSupplierCatalog();
        Assert.IsTrue(catalog.RegisterSupplier(new OffMapSupplier
        {
            SupplierId = "near", SupplierName = "Near Supplier", DistanceMiles = 100,
            OfferedSkuIds = new List<string> { "cigarettes" }, GoodsUnitPriceCents = 50,
        }));
        Assert.IsTrue(catalog.RegisterSupplier(new OffMapSupplier
        {
            SupplierId = "far", SupplierName = "Far Supplier", DistanceMiles = 300,
            OfferedSkuIds = new List<string> { "cigarettes" }, GoodsUnitPriceCents = 50,
        }));
        List<OffMapFreightQuote> quotes = catalog.QuoteAll("cigarettes", quantityUnits: 10,
            freightCentsPerMile: 1, freightBusinessCommissionCents: 20);
        // Seller-freight quotes: near = 500 + 100 = 600, far = 500 + 300 = 800.
        OffMapFreightQuote nearSeller = quotes.Find(q => q.SupplierId == "near" && q.FreightOption == FreightOptionKind.SellerFreight);
        OffMapFreightQuote farSeller = quotes.Find(q => q.SupplierId == "far" && q.FreightOption == FreightOptionKind.SellerFreight);
        Assert.AreEqual(600, nearSeller.DeliveredTotalCents);
        Assert.AreEqual(800, farSeller.DeliveredTotalCents);
        Assert.IsTrue(quotes[0].DeliveredTotalCents <= quotes[quotes.Count - 1].DeliveredTotalCents); // sorted
        OffMapFreightQuote self = quotes.Find(q => q.FreightOption == FreightOptionKind.SelfCollect);
        Assert.AreEqual(0, self.FreightCents);
        Assert.IsTrue(self.RequiresJourney);
    }

    // ---- Item 5A: through-travelers ----

    private static ImmigrationThresholds TravelerThresholds()
        => new ImmigrationThresholds { MinOpenJobs = 1, MinHousingVacancies = 1, MinReputationScore01 = 0.5 };

    [Test]
    public void Traveler_StaysOnlyWhenHousingWorkAndReputationPass_OtherwisePassesOn()
    {
        TownReputationSnapshot good = Reputation(0.8, 0.8, 0.5, 0.8);
        var visit = new ThroughTravelerVisit
        {
            TravelerId = "t-1", TravelerName = "Westbound Traveler", RouteDirection = "westbound",
            LookingForStay = true, DayIndex = 8, BoardingVacancies = 1, VisitedGeneralStore = true,
        };
        Assert.AreEqual(TravelerOutcomeKind.Stays, ThroughTravelerImmigration.EvaluateVisit(
            visit, good, housingVacancies: 2, openJobs: 3, allowNewArrivals: true, TravelerThresholds()).Outcome);
        Assert.AreEqual(TravelerOutcomeKind.PassesOn, ThroughTravelerImmigration.EvaluateVisit(
            visit, good, housingVacancies: 2, openJobs: 0, allowNewArrivals: true, TravelerThresholds()).Outcome);
        Assert.AreEqual(TravelerOutcomeKind.PassesOn, ThroughTravelerImmigration.EvaluateVisit(
            visit, good, housingVacancies: 2, openJobs: 3, allowNewArrivals: false, TravelerThresholds()).Outcome);
    }

    [Test]
    public void Traveler_PassingOn_StillSpreadsWord_ThatCanLaterProduceAnArrival()
    {
        TownReputationSnapshot good = Reputation(0.8, 0.8, 0.5, 0.8);
        var visit = new ThroughTravelerVisit
        {
            TravelerId = "t-2", LookingForStay = false, DayIndex = 8, BoardingVacancies = 1,
        };
        TravelerOutcome outcome = ThroughTravelerImmigration.EvaluateVisit(
            visit, good, housingVacancies: 2, openJobs: 3, allowNewArrivals: true, TravelerThresholds());
        Assert.AreEqual(TravelerOutcomeKind.PassesOn, outcome.Outcome);
        Assert.IsTrue(outcome.WordOfMouthSpread);
        Assert.IsTrue(ThroughTravelerImmigration.WordOfMouthProducesLaterArrival(
            outcome, housingVacancies: 2, openJobs: 3, allowNewArrivals: true, TravelerThresholds()));
        Assert.IsFalse(ThroughTravelerImmigration.WordOfMouthProducesLaterArrival(
            outcome, housingVacancies: 2, openJobs: 0, allowNewArrivals: true, TravelerThresholds()));
    }

    // ---- Item 6: outmigration ----

    [Test]
    public void Outmigration_UnpaidWorkDeparture_RecordsDestination_AndPreservesIdentity()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        FamilyTreeBuilder.EnsureCompleteTree(population, kinship, seed: 7, referenceYear: 1880, new List<string>());
        PersonState grace = population.people[1];
        int kinshipBefore = kinship.EdgeCount;
        var record = Outmigration.Depart(population, kinship, new OutmigrationRequest
        {
            PersonId = grace.id, Reason = OutmigrationReason.UnpaidWages, HasWork = false, HasUnpaidWages = true,
            Destination = new OutmigrationDestination
            {
                DestinationId = "supplier-near", DestinationName = "Near Supplier Town",
                DestinationKind = "supplier-town", ReasonForChoice = "work at the supplier town",
            },
            DayIndex = 12, AllowDepartures = true,
        }, new List<string>());
        Assert.IsTrue(record.Departed);
        Assert.AreEqual("Near Supplier Town", record.Destination.DestinationName);
        Assert.IsTrue(population.GetPerson(grace.id).isOffMap);
        Assert.AreEqual(kinshipBefore, kinship.EdgeCount); // kinship preserved, nothing deleted
        Assert.AreEqual(1, Outmigration.FindDepartedAtDestination(population, new List<OutmigrationRecord> { record }, "supplier-near").Count);
    }

    [Test]
    public void Outmigration_NoDestination_IsRejected_NeverRandomised()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        PersonState grace = population.people[1];
        var record = Outmigration.Depart(population, kinship, new OutmigrationRequest
        {
            PersonId = grace.id, Reason = OutmigrationReason.WorkDriedUp, HasWork = false,
            Destination = null, DayIndex = 12, AllowDepartures = true,
        }, new List<string>());
        Assert.IsFalse(record.Departed);
        Assert.IsFalse(population.GetPerson(grace.id).isOffMap);
    }

    [Test]
    public void Outmigration_PersonWithWorkAndPay_IsNotRolledOut()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        PersonState grace = population.people[1];
        var record = Outmigration.Depart(population, kinship, new OutmigrationRequest
        {
            PersonId = grace.id, Reason = OutmigrationReason.WorkDriedUp, HasWork = true, HasUnpaidWages = false,
            Destination = new OutmigrationDestination { DestinationId = "x", DestinationName = "Somewhere", DestinationKind = "named-new" },
            DayIndex = 12, AllowDepartures = true,
        }, new List<string>());
        Assert.IsFalse(record.Departed);
        Assert.IsFalse(population.GetPerson(grace.id).isOffMap);
    }

    [Test]
    public void Outmigration_SliceGate_BlocksDepartures()
    {
        (PopulationState population, KinshipRegistry kinship) = TwoPersonHousehold();
        PersonState grace = population.people[1];
        var record = Outmigration.Depart(population, kinship, new OutmigrationRequest
        {
            PersonId = grace.id, Reason = OutmigrationReason.UnpaidWages, HasWork = false, HasUnpaidWages = true,
            Destination = new OutmigrationDestination { DestinationId = "x", DestinationName = "Somewhere", DestinationKind = "named-new" },
            DayIndex = 12, AllowDepartures = false,
        }, new List<string>());
        Assert.IsFalse(record.Departed);
    }
}
