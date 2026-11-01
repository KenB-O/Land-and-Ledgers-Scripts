using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Reporting;
using LandLedgers.UI;
using NUnit.Framework;

namespace LandLedgers.Editor.Orchestration
{
    /// <summary>
    /// Pass XII Worker D: acquisition, panel-control, civic, resources,
    /// startup-notice, causal, and person-inspection read models.
    /// Reproduction-first: before this pass there was no source model
    /// exposing a single next acquisition action, no provenance flag
    /// for opportunities, no modal-ownership rule, no non-blocker
    /// civic/resources model, no startup-notice classifier, no
    /// event-tied causal explanation, and no single inspection model
    /// with per-field access rules. Each test pins the new contract.
    /// </summary>
    [TestFixture]
    public sealed class Pass12PlayerExperienceReadModelTests
    {
        // ---------- Acquisition: single next action, states preserved ----------

        [Test]
        public void Acquisition_NextActionMirrorsRealDealSequence()
        {
            Assert.AreEqual("Open Inquiry", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.None, false, false, false));
            Assert.AreEqual("Present Proof of Funds", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.Inquiry, true, false, false));
            Assert.AreEqual("Commit Earnest", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.Inquiry, false, false, false));
            Assert.AreEqual("Continue Diligence", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.EarnestCommitted, false, false, false));
            Assert.AreEqual("Agree Terms", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.EarnestCommitted, false, true, false));
            Assert.AreEqual("Agree Terms", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.DiligenceComplete, false, false, false));
            Assert.AreEqual("Close Acquisition", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.TentativeAgreement, false, false, false));
            Assert.AreEqual("Closed", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.Closed, false, false, false));
            Assert.AreEqual("Review Failure", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.Failed, false, false, false));
            Assert.AreEqual("Reopen Inquiry", AcquisitionDealReadModel.NextActionForStage(
                AcquisitionDealReadModel.DealStageKey.Failed, false, false, true));
        }

        [Test]
        public void Acquisition_StageRowsKeepEveryRealStateVisible()
        {
            AcquisitionDealReadModel.Model model = AcquisitionDealReadModel.Build(
                "L1", "Doctor's Business", "Business",
                AcquisitionDealReadModel.DealStageKey.EarnestCommitted,
                AcquisitionDealReadModel.DealStageKey.None,
                false, false, false,
                AcquisitionDealReadModel.LeadKindKey.PublicListing,
                string.Empty, "initial market",
                new List<string> { "Asking price: $1,000" }, string.Empty);
            Assert.AreEqual(5, model.Stages.Count);
            Assert.AreEqual("Complete", model.Stages[0].State);
            Assert.AreEqual("Current", model.Stages[1].State);
            Assert.AreEqual("Upcoming", model.Stages[2].State);
            Assert.AreEqual("Upcoming", model.Stages[4].State);
            Assert.AreEqual("Continue Diligence", model.NextLegitimateAction);
            Assert.AreEqual(1, model.DealDetails.Count);
        }

        [Test]
        public void Acquisition_ProvenanceUnknownIsFlaggedNeverInvented()
        {
            AcquisitionDealReadModel.Model quiet = AcquisitionDealReadModel.Build(
                "L2", "Quiet shop", "Business",
                AcquisitionDealReadModel.DealStageKey.None,
                AcquisitionDealReadModel.DealStageKey.None,
                false, false, false,
                AcquisitionDealReadModel.LeadKindKey.QuietOpportunity,
                string.Empty, "owner turnover", null, string.Empty);
            Assert.IsFalse(quiet.IsChannelKnown);
            Assert.AreEqual(AcquisitionDealReadModel.InformationChannel.Unknown, quiet.Channel);
            Assert.IsTrue(quiet.ChannelDisplay.Contains("not established"), quiet.ChannelDisplay);
            Assert.IsTrue(quiet.MarketContextLabel.Contains("owner turnover"), quiet.MarketContextLabel);

            AcquisitionDealReadModel.Model distress = AcquisitionDealReadModel.Build(
                "L3", "Distress shop", "Business",
                AcquisitionDealReadModel.DealStageKey.None,
                AcquisitionDealReadModel.DealStageKey.None,
                false, false, false,
                AcquisitionDealReadModel.LeadKindKey.DistressSale,
                string.Empty, "pressure release", null, string.Empty);
            Assert.IsFalse(distress.IsChannelKnown);

            AcquisitionDealReadModel.Model pub = AcquisitionDealReadModel.Build(
                "L4", "Listed shop", "Business",
                AcquisitionDealReadModel.DealStageKey.None,
                AcquisitionDealReadModel.DealStageKey.None,
                false, false, false,
                AcquisitionDealReadModel.LeadKindKey.PublicListing,
                string.Empty, "initial market", null, string.Empty);
            Assert.IsTrue(pub.IsChannelKnown);
            Assert.AreEqual(AcquisitionDealReadModel.InformationChannel.PublicListing, pub.Channel);

            AcquisitionDealReadModel.Model rumor = AcquisitionDealReadModel.Build(
                "L5", "Rumor shop", "Business",
                AcquisitionDealReadModel.DealStageKey.None,
                AcquisitionDealReadModel.DealStageKey.None,
                false, false, false,
                AcquisitionDealReadModel.LeadKindKey.RumorLead,
                string.Empty, "service shortage", null, string.Empty);
            Assert.IsTrue(rumor.IsChannelKnown);
            Assert.AreEqual(AcquisitionDealReadModel.InformationChannel.Conversation, rumor.Channel);

            AcquisitionDealReadModel.Model ad = AcquisitionDealReadModel.Build(
                "L6", "Advertised shop", "Business",
                AcquisitionDealReadModel.DealStageKey.None,
                AcquisitionDealReadModel.DealStageKey.None,
                false, false, false,
                AcquisitionDealReadModel.LeadKindKey.QuietOpportunity,
                "newspaper advertisement", "owner turnover", null, string.Empty);
            Assert.IsTrue(ad.IsChannelKnown);
            Assert.AreEqual(AcquisitionDealReadModel.InformationChannel.Newspaper, ad.Channel);
        }

        // ---------- Panel modal contract ----------

        [Test]
        public void PanelModal_OneOwnerFocusReturnAndRefusals()
        {
            var coordinator = new PanelModalCoordinator();
            Assert.IsTrue(coordinator.RequestModal("seller-meeting", "management-panel"));
            Assert.AreEqual("seller-meeting", coordinator.ActiveOwner);
            Assert.IsFalse(coordinator.RequestModal("other-modal", "management-panel"));
            Assert.AreEqual("seller-meeting", coordinator.ActiveOwner);
            Assert.IsTrue(coordinator.RequestModal("seller-meeting", "management-panel"));
            Assert.IsNull(coordinator.CloseModal("other-modal"));
            Assert.AreEqual("seller-meeting", coordinator.ActiveOwner);
            Assert.AreEqual("management-panel", coordinator.CloseModal("seller-meeting"));
            Assert.IsFalse(coordinator.IsModalActive);
            Assert.IsNull(coordinator.CloseModal("seller-meeting"));
            Assert.IsFalse(coordinator.RequestModal(string.Empty, "management-panel"));
        }

        // ---------- Startup notice gate ----------

        [Test]
        public void StartupGate_PrecedenceAndLegitimateConditions()
        {
            string reason;
            Assert.AreEqual(StartupNoticeGate.Verdict.Show, StartupNoticeGate.Classify(
                true, false, false, false, false, false, out reason));
            Assert.AreEqual(StartupNoticeGate.Verdict.Suppress, StartupNoticeGate.Classify(
                true, false, false, true, false, false, out reason));
            Assert.IsTrue(reason.Contains("Ghost"), reason);
            Assert.AreEqual(StartupNoticeGate.Verdict.Suppress, StartupNoticeGate.Classify(
                false, false, false, false, false, false, out reason));
            Assert.IsTrue(reason.Contains("Premature"), reason);
            Assert.AreEqual(StartupNoticeGate.Verdict.Suppress, StartupNoticeGate.Classify(
                true, true, false, false, true, false, out reason));
            Assert.AreEqual(StartupNoticeGate.Verdict.Show, StartupNoticeGate.Classify(
                true, true, false, false, false, false, out reason));
            Assert.AreEqual(StartupNoticeGate.Verdict.Suppress, StartupNoticeGate.Classify(
                true, false, true, false, false, false, out reason));
        }

        // ---------- Civic / Resources ----------

        [Test]
        public void Civic_NotBuiltIsInformativeNotABlocker()
        {
            SettlementCivicReadModel model = SettlementCivicReadModel.Build(
                true, false, "Town Hall", "Town Council", "Civic",
                0, 0, 0f, 0f, 0f, 0f, string.Empty);
            Assert.AreEqual("Civic: Town Hall", model.TitleText);
            Assert.IsFalse(model.TitleText.Contains("Not Built"), model.TitleText);
            Assert.IsTrue(model.SummaryText.Contains("no town hall yet"), model.SummaryText);
            Assert.IsFalse(model.FocusAvailable);
            Assert.IsTrue(model.EffectsText.Contains("+0%"), model.EffectsText);
        }

        [Test]
        public void Civic_NoAuthorityInventsNothing()
        {
            SettlementCivicReadModel model = SettlementCivicReadModel.Build(
                false, false, string.Empty, string.Empty, string.Empty,
                0, 0, 0f, 0f, 0f, 0f, string.Empty);
            Assert.AreEqual("Civic", model.TitleText);
            Assert.IsFalse(model.FocusAvailable);
            Assert.IsTrue(model.SummaryText.Contains("not been established"), model.SummaryText);
        }

        [Test]
        public void Resources_PercentagesAreProspectivityNeverReserves()
        {
            ResourceObservationReadModel model = ResourceObservationReadModel.Build(
                true,
                new List<ResourceObservationReadModel.DistrictInput>
                {
                    new ResourceObservationReadModel.DistrictInput
                    {
                        KindLabel = "Coal", DistrictCount = 2, HasDistrict = true,
                        StrongestStrength01 = 0.72f, FreightPressure01 = 0.5f, SettlementPressure01 = 0.4f,
                    },
                    new ResourceObservationReadModel.DistrictInput
                    {
                        KindLabel = "Iron", DistrictCount = 0, HasDistrict = false,
                    },
                },
                new List<ResourceObservationReadModel.ProtoSiteInput>
                {
                    new ResourceObservationReadModel.ProtoSiteInput { Label = "North site", SiteStrength01 = 0.6f, CampRelevance01 = 0.3f, BoomtownRelevance01 = 0.2f },
                });
            Assert.AreEqual(2, model.Districts.Count);
            Assert.IsTrue(model.Districts[0].StrengthText.Contains("72%"), model.Districts[0].StrengthText);
            Assert.IsTrue(model.Districts[0].StrengthText.Contains("prospectivity"), model.Districts[0].StrengthText);
            Assert.IsFalse(model.Districts[0].StrengthText.Contains("reserve"), model.Districts[0].StrengthText);
            Assert.IsTrue(model.Districts[0].OwnershipNote.Contains("not established"), model.Districts[0].OwnershipNote);
            Assert.AreEqual("no district generated", model.Districts[1].StrengthText);
            Assert.IsTrue(model.ProtoSites[0].Label.Contains("not a proven reserve"), model.ProtoSites[0].Label);
            Assert.IsTrue(model.UncertaintyBanner.Contains("not a surveyed reserve"), model.UncertaintyBanner);
        }

        [Test]
        public void Resources_NoOpportunityStatesItPlainly()
        {
            ResourceObservationReadModel model = ResourceObservationReadModel.Build(false, null, null);
            Assert.IsFalse(model.HasMeaningfulOpportunity);
            Assert.AreEqual(0, model.Districts.Count);
            Assert.IsTrue(model.SummaryText.Contains("unavailable"), model.SummaryText);
        }

        // ---------- Causal explanation ----------

        [Test]
        public void Causal_EveryLineTiesToAnEventAndEmptyInventsNothing()
        {
            SettlementCausalExplanation empty = SettlementCausalExplanation.Build("Test Town", null);
            Assert.AreEqual("No recorded events", empty.Verdict);
            Assert.AreEqual(0, empty.Lines.Count);

            var events = new List<SettlementCausalExplanation.CausalEvent>
            {
                new SettlementCausalExplanation.CausalEvent { DayIndex = 3, Kind = SettlementCausalExplanation.EventKind.WageMissed, Detail = "Mine payroll missed" },
                new SettlementCausalExplanation.CausalEvent { DayIndex = 5, Kind = SettlementCausalExplanation.EventKind.RentArrears, Detail = "Bakery rent arrears" },
                new SettlementCausalExplanation.CausalEvent { DayIndex = 9, Kind = SettlementCausalExplanation.EventKind.FreightDelay, Detail = "Lumber shipment delayed" },
                new SettlementCausalExplanation.CausalEvent { DayIndex = 2, Kind = SettlementCausalExplanation.EventKind.Purchase, Detail = "Household bought bread" },
            };
            SettlementCausalExplanation model = SettlementCausalExplanation.Build("Test Town", events);
            Assert.AreEqual(4, model.Lines.Count);
            Assert.AreEqual(2, model.Lines[0].DayIndex);
            Assert.IsTrue(model.Lines[1].Text.Contains("Mine payroll missed"), model.Lines[1].Text);
            Assert.AreEqual("Mixed", model.Verdict);
            Assert.IsTrue(model.VerdictBasis.Contains("1 positive"), model.VerdictBasis);
            Assert.IsTrue(model.VerdictBasis.Contains("2 distress"), model.VerdictBasis);
        }

        [Test]
        public void Causal_AllKindsProduceLines()
        {
            var events = new List<SettlementCausalExplanation.CausalEvent>();
            SettlementCausalExplanation.EventKind[] kinds =
            {
                SettlementCausalExplanation.EventKind.EmploymentGained,
                SettlementCausalExplanation.EventKind.EmploymentLost,
                SettlementCausalExplanation.EventKind.WagePaid,
                SettlementCausalExplanation.EventKind.WageMissed,
                SettlementCausalExplanation.EventKind.Purchase,
                SettlementCausalExplanation.EventKind.SupplierFailure,
                SettlementCausalExplanation.EventKind.FreightDelay,
                SettlementCausalExplanation.EventKind.InventoryShortage,
                SettlementCausalExplanation.EventKind.BusinessOpened,
                SettlementCausalExplanation.EventKind.BusinessClosed,
                SettlementCausalExplanation.EventKind.RentArrears,
                SettlementCausalExplanation.EventKind.CreditDistress,
                SettlementCausalExplanation.EventKind.PropertyChanged,
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                events.Add(new SettlementCausalExplanation.CausalEvent { DayIndex = i, Kind = kinds[i], Detail = "event " + i });
            }
            SettlementCausalExplanation model = SettlementCausalExplanation.Build("Town", events);
            Assert.AreEqual(kinds.Length, model.Lines.Count);
            for (int i = 0; i < kinds.Length; i++)
            {
                Assert.IsTrue(model.Lines[i].Text.Contains("event " + i), model.Lines[i].Text);
            }
        }

        // ---------- Person inspection access rules ----------

        private static ObserverContext Observer(int personId, int householdId, string locationId)
        {
            return new ObserverContext
            {
                ObserverPersonId = personId,
                ObserverHouseholdId = householdId,
                ObserverLocationId = locationId,
                IsPublicLocation = (loc) => loc == "market-square",
            };
        }

        private static PersonInspectionReadModel Inspect(ObserverContext observer)
        {
            return PersonInspectionReadModel.Build(
                7, "Ada", false, 34, 3, "Household Three",
                "General Store clerk", "Accepted, active",
                "Stocking shelves", "Stocking", "store-1", "store-1",
                540, 30, "Lives at Plot 012", "Household cash $100", "Needs flour",
                new List<PersonInspectionReadModel.InspectionEvent>
                {
                    new PersonInspectionReadModel.InspectionEvent { DayIndex = 4, Kind = "employment-start", Detail = "Clerk at store-1" },
                },
                observer);
        }

        [Test]
        public void Inspection_SelfSeesEverything()
        {
            PersonInspectionReadModel model = Inspect(Observer(7, 3, "store-1"));
            Assert.IsTrue(model.HouseholdVisible);
            Assert.AreEqual("Household Three", model.HouseholdName);
            Assert.IsTrue(model.ActivityVisible);
            Assert.IsTrue(model.PurposeVisible);
            Assert.AreEqual("Stocking", model.CurrentTaskLabel);
            Assert.IsTrue(model.FinancesVisible);
            Assert.IsTrue(model.EventsVisible);
            Assert.AreEqual(1, model.RecentEvents.Count);
            Assert.AreEqual("Needs flour", model.NeedsSummary);
        }

        [Test]
        public void Inspection_HouseholdMemberSeesPurposeButStrangerAtPublicPlaceSeesActivityOnly()
        {
            PersonInspectionReadModel member = Inspect(Observer(8, 3, "elsewhere"));
            Assert.IsTrue(member.PurposeVisible);
            Assert.IsTrue(member.FinancesVisible);

            // Stranger physically present at the subject's location sees
            // activity but not purpose, employer, or task.
            PersonInspectionReadModel stranger = Inspect(Observer(9, 99, "store-1"));
            Assert.IsFalse(stranger.HouseholdVisible);
            Assert.AreEqual(-1, stranger.HouseholdId);
            Assert.IsTrue(stranger.ActivityVisible);
            Assert.IsFalse(stranger.PurposeVisible);
            Assert.AreEqual(string.Empty, stranger.CurrentTaskLabel);
            Assert.IsFalse(stranger.FinancesVisible);
            Assert.AreEqual(0, stranger.RecentEvents.Count);
            Assert.IsTrue(stranger.AccessNotes.Count > 0);
        }

        [Test]
        public void Inspection_StrangerElsewhereAndNullObserverSeeIdentityOnly()
        {
            PersonInspectionReadModel stranger = Inspect(Observer(9, 99, "farmhouse-kitchen"));
            Assert.AreEqual("unknown", stranger.CurrentActivity);
            Assert.AreEqual(string.Empty, stranger.LocationId);
            Assert.IsFalse(stranger.ActivityVisible);

            PersonInspectionReadModel none = Inspect(null);
            Assert.AreEqual("Ada", none.DisplayName);
            Assert.AreEqual(34, none.DerivedAgeYears);
            Assert.IsFalse(none.HouseholdVisible);
            Assert.IsFalse(none.FinancesVisible);
            Assert.AreEqual("Needs: not recorded in this view.",
                PersonInspectionReadModel.Build(
                    7, "Ada", false, 34, 3, "Household Three", "Clerk", "Active",
                    "Stocking", "Stocking", "store-1", "store-1", 540, 30,
                    "Lives at Plot 012", "Cash", null, null, Observer(7, 3, "store-1")).NeedsSummary);
        }
    }
}
