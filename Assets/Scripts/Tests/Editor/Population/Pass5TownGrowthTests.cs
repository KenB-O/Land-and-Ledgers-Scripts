using System.Collections.Generic;
using LandLedgers.Population.TownGrowth;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass 5 (town growth, last content pass): Roslyn tests for the
    /// new town-growth surface only. Existing systems are not
    /// re-tested here; their own suites cover them. All thresholds,
    /// weights, frequencies, prices, and rates in these tests are
    /// caller-supplied test parameters, never canon. No town name
    /// in these tests is canon: "Fredericton" appears only as a
    /// caller-supplied example location, exactly as Kennedy used it.
    /// </summary>
    [TestFixture]
    public sealed class Pass5TownGrowthTests
    {
        [Test]
        public void LeadEnrichment_NeverInvents_MissingDataStaysUnknown()
        {
            var diag = new List<string>();
            var lead = Pass5LeadEnrichment.Enrich(
                5, "Brother", 3, "Jacob", "freight-co", "driver", "driver",
                null, null, null,
                Pass5ContactDisposition.Unknown, false, 0, 10, diag);
            Assert.AreEqual(Pass5ContactDisposition.Unknown, lead.Disposition);
            Assert.IsTrue(string.IsNullOrWhiteSpace(lead.ProfessionId));
            Assert.IsFalse(lead.Location.IsNamedTown);
            Assert.AreEqual("offmap", lead.Location.EffectivePostalDestination());
            Assert.IsTrue(diag.Count >= 3);
        }

        [Test]
        public void LeadEnrichment_NamedLocation_RoutesToThatDestination()
        {
            var diag = new List<string>();
            var lead = Pass5LeadEnrichment.Enrich(
                5, "Brother", 3, "Jacob", "freight-co", "driver", "driver",
                new Pass5OffMapLocation { TownName = "Fredericton", PostalDestinationId = "fredericton-office" },
                "driver", "Freight Driver",
                Pass5ContactDisposition.SeekingChange, true, 900, 10, diag);
            Assert.IsTrue(lead.Location.IsNamedTown);
            Assert.AreEqual("fredericton-office", lead.Location.EffectivePostalDestination());
            Assert.AreEqual("driver", lead.ProfessionId);
            Assert.AreEqual(Pass5ContactDisposition.SeekingChange, lead.Disposition);
        }

        [Test]
        public void LeadSaveRecord_RoundTrips_WithoutLosingRoleOrLocation()
        {
            var diag = new List<string>();
            var lead = Pass5LeadEnrichment.Enrich(
                5, "Brother", 3, "Jacob", "freight-co", "driver", "driver",
                new Pass5OffMapLocation { TownName = "Fredericton", PostalDestinationId = "fredericton-office" },
                "stable-hand", "Stable Hand",
                Pass5ContactDisposition.RecentlyLostJob, false, 0, 12, diag);
            var record = Pass5LeadSaveRecord.FromLead(lead);
            var back = record.ToLead();
            Assert.AreEqual("driver", back.RoleId);
            Assert.AreEqual("Fredericton", back.Location.TownName);
            Assert.AreEqual("stable-hand", back.ProfessionId);
            Assert.AreEqual(Pass5ContactDisposition.RecentlyLostJob, back.Disposition);
        }

        [Test]
        public void SettlementReadiness_Refuses_NoHousing_NoJob_NoPlan_ButNotForDefaults()
        {
            var refused = Pass5SettlementReadinessModel.Check(new Pass5SettlementInputs
            {
                ContactPersonId = 5, IsAlreadyOnMap = false,
                HousingUnitId = string.Empty, HousingVacancies = 0,
                HasCompensationTerms = false, ExplicitUnemployedWithPlan = false,
            });
            Assert.IsFalse(refused.Ready);
            Assert.IsTrue(refused.HousingAssignmentPending); // always true: Codex HousingResidence step

            var readyWithDefaults = Pass5SettlementReadinessModel.Check(new Pass5SettlementInputs
            {
                ContactPersonId = 5, IsAlreadyOnMap = false,
                HousingUnitId = "house-1", HousingVacancies = 1,
                HasCompensationTerms = true, ExplicitUnemployedWithPlan = false,
                StoveDataAuthored = false, HeatRetentionDataAuthored = false, PreferredFuelDataAuthored = false,
            });
            Assert.IsTrue(readyWithDefaults.Ready);
            Assert.IsTrue(readyWithDefaults.UsesDefaultStoveData);
            Assert.IsTrue(readyWithDefaults.UsesDefaultFuelData);

            var pending = Pass5SettlementReadinessModel.Check(new Pass5SettlementInputs
            {
                ContactPersonId = 5, IsAlreadyOnMap = false,
                HousingUnitId = "house-1", HousingVacancies = 1,
                HasCompensationTerms = false, ExplicitUnemployedWithPlan = true,
            });
            Assert.IsTrue(pending.Ready);
            Assert.IsTrue(pending.EmploymentPending);
        }

        [Test]
        public void EmploymentId_Uses_R05_Format_Exactly()
        {
            Assert.AreEqual("arrival-5-freight-co-12", Pass5EmploymentId.Build(5, "freight-co", 12));
        }

        [Test]
        public void TownDriver_WorkFirst_HousingSecond_ReputationThird_AndGateUndecided()
        {
            var eval = Pass5TownDriverModel.Evaluate(new Pass5TownInputs
            {
                HousingVacantUnits = 2, HousingTotalUnits = 10,
                OpenJobSlots = 3, FilledJobSlots = 7,
                GrowthScore01 = 0.5, OpinionScore01 = 0.5, GrowthSupplied = true, OpinionSupplied = true,
                SnapshotDayIndex = 10, CurrentDayIndex = 10,
                SliceSuppressArrivals = false,
                DepartureGateDecision = Pass5SliceDepartureGateDecision.Undecided,
            });
            // Work 0.3, housing 0.2, reputation 0.5*0.3 + 0.3*0.2 + 0.1*0.5 + 0.1*0.5 = 0.31 < 0.5, so reputation fails.
            Assert.IsTrue(eval.WorkPasses);
            Assert.IsTrue(eval.HousingPasses);
            Assert.IsFalse(eval.ReputationPasses);
            Assert.IsFalse(eval.ArrivalsAllowed);
            Assert.IsTrue(eval.LegacyRollStillLive); // never retired by the model
            Assert.IsTrue(eval.DepartureGateState.Contains("UNDECIDED"));
        }

        [Test]
        public void TownDriver_StaleSnapshot_BlocksArrivals_AndSliceSuppression_BlocksArrivals()
        {
            var stale = Pass5TownDriverModel.Evaluate(new Pass5TownInputs
            {
                HousingVacantUnits = 5, HousingTotalUnits = 10,
                OpenJobSlots = 5, FilledJobSlots = 5,
                GrowthScore01 = 1.0, OpinionScore01 = 1.0, GrowthSupplied = true, OpinionSupplied = true,
                SnapshotDayIndex = 1, CurrentDayIndex = 20, MaxSnapshotAgeDays = 7,
            });
            Assert.IsTrue(stale.SnapshotStale);
            Assert.IsFalse(stale.ArrivalsAllowed);

            var suppressed = Pass5TownDriverModel.Evaluate(new Pass5TownInputs
            {
                HousingVacantUnits = 5, HousingTotalUnits = 10,
                OpenJobSlots = 5, FilledJobSlots = 5,
                GrowthScore01 = 1.0, OpinionScore01 = 1.0, GrowthSupplied = true, OpinionSupplied = true,
                SnapshotDayIndex = 10, CurrentDayIndex = 10,
                SliceSuppressArrivals = true,
            });
            Assert.IsFalse(suppressed.ArrivalsAllowed);
        }

        [Test]
        public void Traveler_StayRequires_AllConditions_AndNeverAutoSettles()
        {
            var town = Pass5TownDriverModel.Evaluate(new Pass5TownInputs
            {
                HousingVacantUnits = 5, HousingTotalUnits = 10,
                OpenJobSlots = 8, FilledJobSlots = 2,
                GrowthScore01 = 1.0, OpinionScore01 = 1.0, GrowthSupplied = true, OpinionSupplied = true,
                SnapshotDayIndex = 10, CurrentDayIndex = 10,
            });
            Assert.IsTrue(town.ArrivalsAllowed);
            var visits = Pass5TravelerModel.GenerateVisits(1, 1, town, 10);
            Assert.AreEqual(1, visits.Count);
            Assert.AreEqual("visitor-only", visits[0].NextStep); // no PersonState created, no auto-settle
            visits[0].LookingForStay = true;
            var stay = Pass5TravelerModel.EvaluateStay(visits[0], town, 8, 5);
            Assert.IsTrue(stay.StayEligible);
            Assert.AreEqual("run-ArrivalSettlementDriver.SettleAndApply", stay.NextStep);
        }

        [Test]
        public void Departure_NewChannel_RequiresDestination_LegacyStaysUntracked()
        {
            var refused = Pass5DepartureModel.DepartNewChannel(7, Pass5DepartureDestinationKind.LegacyUntracked, null, false);
            Assert.IsFalse(refused.MembershipClosed);
            var ok = Pass5DepartureModel.DepartNewChannel(7, Pass5DepartureDestinationKind.FamilyContact, null, true);
            Assert.IsTrue(ok.MembershipClosed);
            Assert.IsTrue(ok.EmploymentClosed);
            Assert.IsTrue(ok.HouseholdPropertyDebtStillRequired); // F08 still open
            Assert.AreEqual(0.2, ok.ReputationPenalty01);
            var legacy = Pass5DepartureModel.PreserveLegacyDeparture(7);
            Assert.AreEqual(Pass5DepartureDestinationKind.LegacyUntracked, legacy.DestinationKind);
            Assert.AreEqual(string.Empty, legacy.DestinationName); // never invented
        }

        [Test]
        public void NeedReader_NeverOutputsHeadcount_PlayerOpens_TaskEvidenceRanks()
        {
            var diag = new List<string>();
            var ranks = Pass5EconomicNeedReader.Rank(
                new List<Pass5TaskFamilyEvidence>
                {
                    new Pass5TaskFamilyEvidence { Family = "freight", UnmetMinutes = 300, BalkedDemandCount = 3, SustainedDays = 5 },
                    new Pass5TaskFamilyEvidence { Family = "unknown-family", UnmetMinutes = 100, BalkedDemandCount = 0, SustainedDays = 5 },
                },
                new Dictionary<string, string> { { "freight", "driver" } },
                new Dictionary<string, string> { { "freight", "driver" } },
                new List<Pass5PlayerStatedNeed>
                {
                    new Pass5PlayerStatedNeed { BusinessId = "freight-co", RoleId = "driver", RoleDisplayName = "driver" },
                },
                3, diag);
            Pass5RoleNeedRank driver = null;
            Pass5RoleNeedRank unmapped = null;
            foreach (var r in ranks)
            {
                if (r.RoleId == "driver") driver = r;
                if (r.IsUnmappedFamily) unmapped = r;
            }
            Assert.IsNotNull(driver);
            Assert.AreEqual(Pass5NeedSource.Both, driver.Source);
            Assert.IsTrue(driver.MeetsSustainedThreshold);
            Assert.AreEqual(300, driver.UnmetMinutes);
            Assert.IsNotNull(unmapped);
            Assert.IsTrue(unmapped.IsUnmappedFamily);
            // No headcount field exists on the rank type by design;
            // this test asserts the evidence shape, not a staffing order.
            Assert.IsTrue(diag.Count >= 1);
        }

        [Test]
        public void ConversationChain_CannotSkipSteps_AndRefusesFriendEdgesWithoutHistory()
        {
            var diag = new List<string>();
            Assert.IsTrue(Pass5ConversationChain.ValidateQuestion(
                new Pass5DialogueQuestion
                {
                    SpeakerPersonId = 3, BusinessId = "freight-co", RoleId = "driver", RoleDisplayName = "driver",
                    IsPlayerOwnedBusiness = true, HasLegitimateChannel = true,
                },
                new List<string> { "driver", "teamster" }, diag));
            Assert.IsFalse(Pass5ConversationChain.ValidateQuestion(
                new Pass5DialogueQuestion
                {
                    SpeakerPersonId = 3, BusinessId = "freight-co", RoleId = "invented-role", RoleDisplayName = "invented-role",
                    IsPlayerOwnedBusiness = true, HasLegitimateChannel = true,
                },
                new List<string> { "driver", "teamster" }, diag));

            var edges = Pass5ConversationChain.ValidateFriendEdges(
                new List<Pass5FriendEdge>
                {
                    new Pass5FriendEdge { PersonId = 3, FriendPersonId = 9, SharedHistoryEvidence = string.Empty },
                    new Pass5FriendEdge { PersonId = 3, FriendPersonId = 10, SharedHistoryEvidence = "shared workplace" },
                }, diag);
            Assert.AreEqual(1, edges.Count); // only the evidenced edge survives; friends are never generated

            var record = new Pass5ChainRecord { LeadContactPersonId = 5, Step = Pass5ConversationStep.AskWorkers, DayIndex = 10 };
            var skipped = Pass5ConversationChain.Advance(record, Pass5ConversationStep.ArrivalSettlement, diag);
            Assert.AreEqual(Pass5ConversationStep.AskWorkers, skipped.Step); // skip refused
            var toLetter = Pass5ConversationChain.Advance(record, Pass5ConversationStep.BuildLetterRequest, diag);
            Assert.AreEqual(Pass5ConversationStep.BuildLetterRequest, toLetter.Step);
            var unpaid = Pass5ConversationChain.Advance(toLetter, Pass5ConversationStep.PostLetter, diag);
            Assert.AreEqual(Pass5ConversationStep.BuildLetterRequest, unpaid.Step); // postage unpaid, posting refused
            toLetter.PostagePaid = true;
            toLetter.LetterOrigin = "general-store";
            var posted = Pass5ConversationChain.Advance(toLetter, Pass5ConversationStep.PostLetter, diag);
            Assert.AreEqual(Pass5ConversationStep.PostLetter, posted.Step);
        }
    }
}
