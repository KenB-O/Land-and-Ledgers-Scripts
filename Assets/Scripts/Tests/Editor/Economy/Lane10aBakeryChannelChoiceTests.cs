using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEditor;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Lane 10a (bakery sales-channel choice, H-2/H-3/H-5/H-6): Glenn's
    /// initial policy resolves once from config and is recorded; every
    /// channel decision is recorded and survives a save/load round-trip;
    /// the deterministic morning re-evaluation changes policy only through
    /// a recorded decision; no wholesale account ever appears implicitly
    /// under direct retail. Unity compilation, Test Runner, PlayMode, and
    /// live behaviour are NOT claimed; live behaviour is unverified until
    /// Codex runs the 5–10 day 10x run.
    /// </summary>
    [TestFixture]
    public sealed class Lane10aBakeryChannelChoiceTests
    {
        private const string BakeryInstanceId = "test-bakery-10a";

        // ---- H-2: the config-authored initial policy, resolved once ----

        [Test]
        public void ResolveInitialPolicy_AuthoredValueWins_AndIsRecordedOnce()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime(BakeryInstanceId);

            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.ProtectWholesaleContracts, 0, diag));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectWholesaleContracts, runtime.SupplyProtection,
                "the authored initial policy wins over the retail-first default");
            Assert.AreEqual(1, runtime.ChannelDecisions.Count, "the resolution is recorded as a decision");
            BakeryChannelDecision decision = runtime.ChannelDecisions[0];
            Assert.AreEqual(BakeryChannelDecision.InitialPolicyKind, decision.Kind);
            Assert.AreEqual(BakeryChannelDecision.ScenarioDecider, decision.Decider);
            Assert.AreEqual(0, decision.DayIndex);
            Assert.AreEqual("ProtectRetailCounter", decision.OldValue, "the runtime started on the code default");
            Assert.AreEqual("ProtectWholesaleContracts", decision.NewValue);
            StringAssert.Contains("scenario-authored", decision.Reason, "reason: " + decision.Reason);

            // Idempotent: a second resolution is not re-resolved and
            // records nothing, even with a different authored value.
            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.ProtectRetailCounter, 5, diag));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectWholesaleContracts, runtime.SupplyProtection,
                "a recorded initial policy is never re-resolved");
            Assert.AreEqual(1, runtime.ChannelDecisions.Count, "no second decision is recorded");
        }

        [Test]
        public void ResolveInitialPolicy_AbsentMeansProtectRetailCounter_RecordedAsScenarioDefault()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime(BakeryInstanceId);

            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.Unspecified, 0, diag));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, runtime.SupplyProtection,
                "absent means ProtectRetailCounter (retail-first per §19)");
            Assert.AreEqual(1, runtime.ChannelDecisions.Count);
            BakeryChannelDecision decision = runtime.ChannelDecisions[0];
            Assert.AreEqual(BakeryChannelDecision.InitialPolicyKind, decision.Kind);
            Assert.AreEqual("ProtectRetailCounter", decision.NewValue);
            StringAssert.Contains("retail-first", decision.Reason, "reason: " + decision.Reason);
        }

        // ---- H-3: the persisted decision record ----

        [Test]
        public void ChannelDecisions_RoundTripWithSaveDto_PreservingKindsInputsAndOrder()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.ProtectWholesaleContracts, 0, diag));
            Assert.IsNull(runtime.AddWholesaleAccount(
                BoardingHouseAccount(units: 24, dueDay: 1, priceCents: 4), 0, "scenario", diag));
            Assert.IsNull(runtime.SetSupplyProtection(
                BakerySupplyProtectionPolicy.ProtectRetailCounter, 1, "P12", "test reason", diag,
                recordedRetailAverageUnitsPerDay: 5, openHouseholdBreadNeedUnits: 10,
                dueWholesaleUnits: 24, retailPriceCentsPerUnit: 8, wholesalePriceCentsPerUnit: 4));

            BakeryShopRuntime.BakeryShopRuntimeSaveDto saved = runtime.CaptureSaveDto();
            var restored = new BakeryShopRuntime(BakeryInstanceId);
            restored.LoadFromSaveDto(saved);

            Assert.AreEqual(3, restored.ChannelDecisions.Count, "the history round-trips with the runtime");
            Assert.AreEqual(BakeryChannelDecision.InitialPolicyKind, restored.ChannelDecisions[0].Kind);
            Assert.AreEqual(BakeryChannelDecision.AccountAddedKind, restored.ChannelDecisions[1].Kind);
            Assert.AreEqual(BakeryChannelDecision.PolicyChangeKind, restored.ChannelDecisions[2].Kind);
            BakeryChannelDecision change = restored.ChannelDecisions[2];
            Assert.AreEqual("P12", change.Decider);
            Assert.AreEqual("ProtectWholesaleContracts", change.OldValue);
            Assert.AreEqual("ProtectRetailCounter", change.NewValue);
            Assert.AreEqual("test reason", change.Reason);
            Assert.AreEqual(5, change.RecordedRetailAverageUnitsPerDay);
            Assert.AreEqual(10, change.OpenHouseholdBreadNeedUnits);
            Assert.AreEqual(24, change.DueWholesaleUnits);
            Assert.AreEqual(8, change.RetailPriceCentsPerUnit);
            Assert.AreEqual(4, change.WholesalePriceCentsPerUnit);
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, restored.SupplyProtection,
                "the policy itself round-trips alongside its history");
            Assert.IsNotNull(restored.LatestChannelDecision);
            Assert.AreEqual(BakeryChannelDecision.PolicyChangeKind, restored.LatestChannelDecision.Kind);
            StringAssert.Contains("PolicyChange", restored.LatestChannelDecision.Summary(),
                "the one-line summary names the decision: " + restored.LatestChannelDecision.Summary());
            StringAssert.Contains("P12", restored.LatestChannelDecision.Summary());
        }

        [Test]
        public void ChannelDecisions_OldSaveLoadsEmpty_WithoutThrowing()
        {
            // An old save carries no decision history (the DTO field is
            // additive, like TransferredStagedLotIds): it loads empty,
            // never null, never throwing.
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            BakeryShopRuntime.BakeryShopRuntimeSaveDto saved = runtime.CaptureSaveDto();
            saved.ChannelDecisions = null;

            var restored = new BakeryShopRuntime(BakeryInstanceId);
            restored.LoadFromSaveDto(saved); // would throw on a null history; the test fails if it does
            Assert.AreEqual(0, restored.ChannelDecisions.Count, "an old save loads an empty history");
            Assert.IsNull(restored.LatestChannelDecision);
            Assert.IsNotNull(new BakeryShopRuntime.BakeryShopRuntimeSaveDto().ChannelDecisions,
                "the DTO default is an empty list, never null");
        }

        [Test]
        public void SetSupplyProtection_RecordsOnlyRealChanges_AndRefusesUnspecified()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime(BakeryInstanceId);

            Assert.IsNotNull(runtime.SetSupplyProtection(
                BakerySupplyProtectionPolicy.Unspecified, 0, "P12", "nope", diag),
                "Unspecified is not a settable policy");
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, runtime.SupplyProtection,
                "a refused set changes nothing");
            Assert.AreEqual(0, runtime.ChannelDecisions.Count);

            Assert.IsNull(runtime.SetSupplyProtection(
                BakerySupplyProtectionPolicy.ProtectRetailCounter, 0, "P12", "already retail", diag),
                "setting the value already in force is a no-op, not a decision");
            Assert.AreEqual(0, runtime.ChannelDecisions.Count, "a no-op set records nothing");

            Assert.IsNull(runtime.SetSupplyProtection(
                BakerySupplyProtectionPolicy.ProportionalShare, 2, "P12", "splitting the shelf", diag,
                recordedRetailAverageUnitsPerDay: 3, openHouseholdBreadNeedUnits: 4,
                dueWholesaleUnits: 24, retailPriceCentsPerUnit: 8, wholesalePriceCentsPerUnit: 6));
            Assert.AreEqual(1, runtime.ChannelDecisions.Count);
            Assert.AreEqual(BakeryChannelDecision.PolicyChangeKind, runtime.ChannelDecisions[0].Kind);
            Assert.AreEqual(3, runtime.ChannelDecisions[0].RecordedRetailAverageUnitsPerDay);
            Assert.AreEqual(6, runtime.ChannelDecisions[0].WholesalePriceCentsPerUnit);
        }

        [Test]
        public void AddWholesaleAccount_WithoutContext_RecordsHonestUnspecifiedEntry()
        {
            // The 2-arg booker still records the add (H-3: every account
            // add writes one entry) — with decider "unspecified" and day
            // -1, never a fabricated day 0.
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 24, dueDay: 300), diag));
            Assert.AreEqual(1, runtime.ChannelDecisions.Count);
            BakeryChannelDecision decision = runtime.ChannelDecisions[0];
            Assert.AreEqual(BakeryChannelDecision.AccountAddedKind, decision.Kind);
            Assert.AreEqual(BakeryChannelDecision.UnspecifiedDecider, decision.Decider);
            Assert.AreEqual(-1, decision.DayIndex, "the day is unknown, not fabricated");
            Assert.AreEqual("absent", decision.OldValue);
            Assert.AreEqual(4, decision.WholesalePriceCentsPerUnit, "the account's own price is on the record");
        }

        [Test]
        public void SetWholesaleAccountActive_PauseAndResumeAreRecordedDecisions()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            Assert.IsNull(runtime.AddWholesaleAccount(
                BoardingHouseAccount(units: 24, dueDay: 300), 300, "scenario", diag));
            string accountId = runtime.WholesaleAccounts[0].AccountId;

            Assert.IsNotNull(runtime.SetWholesaleAccountActive("no-such-account", false, 301, "P12", "why", diag),
                "an unknown account id is refused, loudly");
            Assert.AreEqual(1, runtime.ChannelDecisions.Count, "a refused pause records nothing");

            Assert.IsNull(runtime.SetWholesaleAccountActive(accountId, false, 301, "P12", "counter starved", diag));
            Assert.IsFalse(runtime.WholesaleAccounts[0].IsActive, "the account is paused, not wiped");
            Assert.AreEqual(BakeryChannelDecision.AccountPausedKind, runtime.ChannelDecisions[1].Kind);
            Assert.AreEqual("active", runtime.ChannelDecisions[1].OldValue);
            Assert.AreEqual("paused", runtime.ChannelDecisions[1].NewValue);
            Assert.AreEqual("counter starved", runtime.ChannelDecisions[1].Reason);

            Assert.IsNull(runtime.SetWholesaleAccountActive(accountId, false, 302, "P12", "again", diag),
                "pausing an already-paused account is a no-op");
            Assert.AreEqual(2, runtime.ChannelDecisions.Count, "a no-op pause records nothing");

            Assert.IsNull(runtime.SetWholesaleAccountActive(accountId, true, 303, "P12", "retail covered", diag));
            Assert.IsTrue(runtime.WholesaleAccounts[0].IsActive);
            Assert.AreEqual(BakeryChannelDecision.AccountResumedKind, runtime.ChannelDecisions[2].Kind);
        }

        // ---- H-5: the deterministic morning re-evaluation ----

        [Test]
        public void Reevaluate_StarvedCounterFlipsPolicy_ThroughARecordedDecision()
        {
            // R1: open need 30u, retail-available 0u (24 loaves on the
            // shelf, all committed to the due 24-loaf account under
            // ProtectWholesaleContracts), due 24u.
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            BindGlenn(runtime, diag);
            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.ProtectWholesaleContracts, 0, diag));
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.IsNull(runtime.AddWholesaleAccount(
                BoardingHouseAccount(units: 24, dueDay: 301, priceCents: 4), 301, "scenario", diag));
            Assert.AreEqual(0, runtime.RetailUnitsAvailableForSale(BakeryBreadCatalog.BreadLoafId, 301, diag),
                "under ProtectWholesaleContracts the due account takes the whole shelf");

            Assert.IsNull(runtime.ReevaluateChannelChoice(301, openHouseholdBreadNeedUnits: 30, deliveryCostCents: 0, diag));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, runtime.SupplyProtection,
                "the starved counter flips the policy to retail-first");
            BakeryChannelDecision change = runtime.ChannelDecisions[runtime.ChannelDecisions.Count - 1];
            Assert.AreEqual(BakeryChannelDecision.PolicyChangeKind, change.Kind);
            Assert.AreEqual("P12", change.Decider, "Glenn (the bound owner) is the decider");
            Assert.AreEqual(301, change.DayIndex);
            Assert.AreEqual("ProtectWholesaleContracts", change.OldValue);
            Assert.AreEqual("ProtectRetailCounter", change.NewValue);
            Assert.AreEqual(30, change.OpenHouseholdBreadNeedUnits);
            Assert.AreEqual(24, change.DueWholesaleUnits);
            Assert.AreEqual(8, change.RetailPriceCentsPerUnit, "the bakery counter fresh loaf is 8c");
            Assert.AreEqual(4, change.WholesalePriceCentsPerUnit);
            StringAssert.Contains("starved", change.Reason, "reason: " + change.Reason);
        }

        [Test]
        public void Reevaluate_NoSupportingSignal_ChangesNothingAndRecordsNothing()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            BindGlenn(runtime, diag);
            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.ProtectWholesaleContracts, 0, diag));
            Assert.IsNull(runtime.AddWholesaleAccount(
                BoardingHouseAccount(units: 24, dueDay: 301, priceCents: 4), 301, "scenario", diag));
            int decisionsBefore = runtime.ChannelDecisions.Count;

            // No recorded sales, no open need: the wholesale value cannot
            // be compared against observed retail demand, so the policy
            // stands and nothing is recorded.
            Assert.IsNull(runtime.ReevaluateChannelChoice(301, openHouseholdBreadNeedUnits: 0, deliveryCostCents: 0, diag));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectWholesaleContracts, runtime.SupplyProtection,
                "an ambiguous morning leaves the policy alone");
            Assert.AreEqual(decisionsBefore, runtime.ChannelDecisions.Count,
                "a no-change morning records no decision");
            StringAssert.Contains("no change", string.Join(" | ", diag), "diag: " + string.Join(" | ", diag));
        }

        [Test]
        public void Reevaluate_StillStarvedUnderRetailFirst_PausesDueAccounts()
        {
            // R4: policy already ProtectRetailCounter, counter still
            // starved, retail demand (open need 96u) meets daily capacity
            // (96u) — the due accounts pause so the shop's whole output
            // can serve walk-in households. The pause is gradual: the
            // policy flip gets one morning first (see the R1 test).
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            BindGlenn(runtime, diag);
            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.Unspecified, 0, diag));
            Assert.AreEqual(96, runtime.DailyProductionCapacityUnits(), "one oven, 4 firings, 24 loaves a batch");
            Assert.IsNull(runtime.AddWholesaleAccount(
                BoardingHouseAccount(units: 24, dueDay: 301, priceCents: 4), 301, "scenario", diag));
            string accountId = runtime.WholesaleAccounts[0].AccountId;

            Assert.IsNull(runtime.ReevaluateChannelChoice(301, openHouseholdBreadNeedUnits: 96, deliveryCostCents: 0, diag));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, runtime.SupplyProtection,
                "the policy was already retail-first; no policy decision is recorded for it");
            Assert.IsFalse(runtime.WholesaleAccounts[0].IsActive, "the due account pauses");
            BakeryChannelDecision pause = runtime.ChannelDecisions[runtime.ChannelDecisions.Count - 1];
            Assert.AreEqual(BakeryChannelDecision.AccountPausedKind, pause.Kind);
            Assert.AreEqual("P12", pause.Decider);
            Assert.AreEqual(301, pause.DayIndex);
            Assert.AreEqual(accountId, runtime.WholesaleAccounts[0].AccountId, "the account stays on the books, paused");
        }

        [Test]
        public void Reevaluate_PlayerOwnedBakery_IsSkippedNotDecidedFor()
        {
            // D-04: no NPC owner bound (a player-owned bakery) — the
            // morning rule does NOT decide for the player. The standing
            // policy is kept, nothing is recorded, and the gap is named.
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.IsNull(runtime.AddWholesaleAccount(
                BoardingHouseAccount(units: 24, dueDay: 301, priceCents: 4), 301, "scenario", diag));
            int decisionsBefore = runtime.ChannelDecisions.Count;

            Assert.IsNull(runtime.ReevaluateChannelChoice(301, openHouseholdBreadNeedUnits: 96, deliveryCostCents: 0, diag));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, runtime.SupplyProtection,
                "the standing policy is kept");
            Assert.AreEqual(decisionsBefore, runtime.ChannelDecisions.Count,
                "no decision is recorded for a player-owned bakery");
            Assert.IsTrue(runtime.WholesaleAccounts[0].IsActive, "no account is paused for the player");
            StringAssert.Contains("D-04", string.Join(" | ", diag), "diag: " + string.Join(" | ", diag));
        }

        [Test]
        public void Reevaluate_LegacySaveWithNoHistory_RecordsStandingPolicyUnchanged()
        {
            // Backstop (H-2/H-3): a bound runtime with no decision on
            // record records its standing policy first, unchanged, before
            // evaluating — the history is never silently empty.
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            BindGlenn(runtime, diag);
            Assert.AreEqual(0, runtime.ChannelDecisions.Count);

            Assert.IsNull(runtime.ReevaluateChannelChoice(301, openHouseholdBreadNeedUnits: 0, deliveryCostCents: 0, diag));
            Assert.AreEqual(1, runtime.ChannelDecisions.Count);
            BakeryChannelDecision standing = runtime.ChannelDecisions[0];
            Assert.AreEqual(BakeryChannelDecision.StandingPolicyKind, standing.Kind);
            Assert.AreEqual("ProtectRetailCounter", standing.OldValue);
            Assert.AreEqual("ProtectRetailCounter", standing.NewValue, "the policy itself is unchanged");
            StringAssert.Contains("no prior decision", standing.Reason, "reason: " + standing.Reason);
        }

        [Test]
        public void RecordedRetailAverage_UsesTheSameThreeDayWindowAsPlanning()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 299, diag));
            Assert.AreEqual(1, WorkDay(runtime, 299, diag));
            Assert.AreEqual(24, runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 24, 299, diag)[0].Units,
                "24 loaves sell on day 299");

            Assert.AreEqual(0, runtime.RecordedRetailAverageUnitsPerDay(299),
                "sales on day 299 do not count toward day 299's own average");
            Assert.AreEqual(8, runtime.RecordedRetailAverageUnitsPerDay(300),
                "24 units over the 3-day window [297,300) average 8/day (ceiling)");
            Assert.AreEqual(0, runtime.RecordedRetailAverageUnitsPerDay(303),
                "the window slides: day-299 sales leave the 3-day window by day 303");
        }

        [Test]
        public void DailyProductionCapacity_IsTheSmallerOfOvenAndLabourBudgets()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            Assert.AreEqual(0, runtime.DailyProductionCapacityUnits(), "no oven installed, no capacity");

            Assert.IsNull(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            Assert.AreEqual(96, runtime.DailyProductionCapacityUnits(),
                "1 oven × 4 firings × 24 loaves; labour allows 4 batches (840m ÷ 180m)");

            Assert.IsNull(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            Assert.AreEqual(96, runtime.DailyProductionCapacityUnits(),
                "a second oven adds firings but labour still binds at 4 batches");
        }

        // ---- H-6: wholesale is opt-in only ----

        [Test]
        public void NoImplicitWholesaleAccount_UnderDirectRetail()
        {
            // Glenn chooses direct retail: the account set stays empty
            // through the initial resolution, planning, and morning
            // re-evaluation. The only ways an account exists are an
            // explicit config entry, a voluntary AddWholesaleAccount, or
            // a recorded decision to opt in — none of which happened here.
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            BindGlenn(runtime, diag);
            Assert.IsNull(runtime.ResolveInitialChannelPolicy(
                BakerySupplyProtectionPolicy.Unspecified, 0, diag));
            Assert.IsNull(runtime.ResolveInitialSalesChannels(BakerySalesChannelPolicy.Retail, 290, diag));
            Assert.AreEqual(0, runtime.WholesaleAccounts.Count);

            Assert.IsNotNull(runtime.PlanWholesaleBatches(1, diag));
            Assert.IsNotNull(runtime.PlanRetailBatches(1, 10, diag));
            Assert.IsNull(runtime.ReevaluateChannelChoice(1, openHouseholdBreadNeedUnits: 10, deliveryCostCents: 0, diag));
            Assert.IsNull(runtime.ReevaluateChannelChoice(2, openHouseholdBreadNeedUnits: 10, deliveryCostCents: 0, diag));

            Assert.AreEqual(0, runtime.WholesaleAccounts.Count,
                "planning and re-evaluation never conjure a wholesale account");
            foreach (BakeryChannelDecision decision in runtime.ChannelDecisions)
                Assert.AreNotEqual(BakeryChannelDecision.AccountAddedKind, decision.Kind,
                    "no account was added");
        }

        [Test]
        public void DevelopmentTownGlennBakery_IsRetailOnly_AndHasNoAutomaticGeneralStoreAgreement()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            Assert.IsNotNull(configuration);
            Assert.AreEqual(BakerySalesChannelPolicy.Retail, configuration.OpeningBakerySalesChannels,
                "Glenn's authored scenario decision enables direct consumer retail only");
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, configuration.OpeningBakerySupplyProtection,
                "the enabled retail counter receives the existing scarcity protection policy");
            DevelopmentTownConfiguration.BusinessDefinition bakery = configuration.FindBusinessByOperation("bakery");
            Assert.IsNotNull(bakery);
            Assert.IsEmpty(bakery.opening.wholesaleAgreements,
                "the development town must not create an automatic Glenn-to-General-Store obligation");
        }

        [Test]
        public void SalesChannels_RetailAndWholesaleAreDistinctAndEnforcedForNewTransactions()
        {
            var diag = new List<string>();
            BakeryShopRuntime runtime = StockedShop(diag);
            Assert.IsNull(runtime.ResolveInitialSalesChannels(BakerySalesChannelPolicy.Retail, 290, diag));
            Assert.IsTrue(runtime.RetailChannelEnabled);
            Assert.IsFalse(runtime.WholesaleChannelEnabled);
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 290, diag));
            Assert.AreEqual(1, WorkDay(runtime, 290, diag));
            Assert.AreEqual(1, runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 1, 290, diag).Sum(sale => sale.Units),
                "the retail-only decision permits the existing direct retail authority to sell its physical loaf");
            StringAssert.Contains("new wholesale agreement refused", runtime.AddWholesaleAccount(
                BoardingHouseAccount(), 290, "P12", diag));

            Assert.IsNull(runtime.SetSalesChannels(BakerySalesChannelPolicy.RetailAndWholesale,
                291, "P12", "Glenn elects to add a wholesale channel", diag));
            Assert.IsTrue(runtime.RetailChannelEnabled);
            Assert.IsTrue(runtime.WholesaleChannelEnabled);
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(), 291, "P12", diag));
            Assert.AreEqual(1, runtime.WholesaleAccounts.Count);
        }

        [Test]
        public void WholesaleOnlyPolicy_RefusesRetailWithoutConsumingFinishedBread()
        {
            var diag = new List<string>();
            BakeryShopRuntime runtime = StockedShop(diag);
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 290, diag));
            Assert.AreEqual(1, WorkDay(runtime, 290, diag));
            int shelfBefore = runtime.RetailUnitsAvailableForSale(BakeryBreadCatalog.BreadLoafId, 290, diag);
            Assert.Greater(shelfBefore, 0);
            Assert.IsNull(runtime.ResolveInitialSalesChannels(BakerySalesChannelPolicy.Wholesale, 290, diag));

            Assert.AreEqual(0, runtime.RetailUnitsAvailableForSale(BakeryBreadCatalog.BreadLoafId, 290, diag));
            Assert.AreEqual(0, runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 1, 290, diag).Count);
            Assert.AreEqual(shelfBefore, runtime.BreadShelf.Sum(lot => lot.UnitCount),
                "a channel refusal leaves the finished goods physically in bakery ownership");
            Assert.IsTrue(runtime.WholesaleChannelEnabled);
            Assert.IsFalse(runtime.RetailChannelEnabled);
        }

        [Test]
        public void SalesChannelChoice_RoundTrips_AndDoesNotCancelAcceptedWholesaleObligations()
        {
            var diag = new List<string>();
            BakeryShopRuntime runtime = StockedShop(diag);
            Assert.IsNull(runtime.ResolveInitialSalesChannels(BakerySalesChannelPolicy.RetailAndWholesale, 290, diag));
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(dueDay: 294), 290, "P12", diag));
            Assert.IsNull(runtime.SetSalesChannels(BakerySalesChannelPolicy.Retail, 292, "P12",
                "Glenn leaves wholesale for direct household trade", diag));

            BakeryShopRuntime.BakeryShopRuntimeSaveDto saved = runtime.CaptureSaveDto();
            var restored = new BakeryShopRuntime(BakeryInstanceId);
            restored.LoadFromSaveDto(saved);

            Assert.AreEqual(BakerySalesChannelPolicy.Retail, restored.SalesChannels);
            Assert.IsTrue(restored.RetailChannelEnabled);
            Assert.IsFalse(restored.WholesaleChannelEnabled,
                "no new wholesale contracts may be opened under retail-only policy");
            Assert.AreEqual(1, restored.WholesaleAccounts.Count,
                "an already-accepted account is preserved through Save/Continue");
            Assert.IsTrue(restored.WholesaleAccounts[0].IsActive,
                "a later channel preference does not silently cancel a contractual obligation");
            Assert.IsTrue(restored.ChannelDecisions.Any(decision =>
                decision != null && decision.Kind == BakeryChannelDecision.SalesChannelsChangeKind
                && decision.OldValue == BakerySalesChannelPolicy.RetailAndWholesale.ToString()
                && decision.NewValue == BakerySalesChannelPolicy.Retail.ToString()));
        }

        // ---- H-5: the evaluator, pure (no runtime) ----

        [Test]
        public void Evaluator_NullSignalsOrNoCapacityOrNothingDue_StandsPat()
        {
            BakeryChannelProposal nullProposal = BakeryChannelChoiceEvaluator.Evaluate(null);
            Assert.IsFalse(nullProposal.HasChange);
            StringAssert.Contains("no channel signals", string.Join("; ", nullProposal.Reasons));

            BakeryChannelProposal noCapacity = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 10, openNeed: 10, retailAvailable: 0, dueUnits: 24, dueRevenue: 96,
                capacity: 0, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectRetailCounter));
            Assert.IsFalse(noCapacity.HasChange);
            StringAssert.Contains("no production capacity", string.Join("; ", noCapacity.Reasons));

            BakeryChannelProposal nothingDue = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 10, openNeed: 10, retailAvailable: 0, dueUnits: 0, dueRevenue: 0,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectWholesaleContracts));
            Assert.IsFalse(nothingDue.HasChange);
            StringAssert.Contains("no wholesale due", string.Join("; ", nothingDue.Reasons));
        }

        [Test]
        public void Evaluator_StarvedCounter_RecommendsRetailFirst_WithoutPausing()
        {
            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 5, openNeed: 30, retailAvailable: 0, dueUnits: 24, dueRevenue: 96,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectWholesaleContracts));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, proposal.RecommendedPolicy);
            Assert.IsFalse(proposal.PauseDueWholesaleAccounts,
                "the policy flip gets one morning before any pause");
        }

        [Test]
        public void Evaluator_RetailDemandMeetsCapacity_RecommendsRetailFirst()
        {
            // Open need is covered by the shelf (10u <= 24u), so R1 does
            // not fire; recorded demand 90 + 10 = 100 >= capacity 96 (R2).
            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 90, openNeed: 10, retailAvailable: 24, dueUnits: 24, dueRevenue: 96,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectWholesaleContracts));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, proposal.RecommendedPolicy);
            Assert.IsFalse(proposal.PauseDueWholesaleAccounts);
            StringAssert.Contains("adds no throughput", string.Join("; ", proposal.Reasons));
        }

        [Test]
        public void Evaluator_WholesaleAddsThroughputThatPays_RecommendsContracts()
        {
            // Due 24u exceeds the retail-absorbable surplus (96 − 90 = 6u);
            // net wholesale 96c beats expected retail 24 × 8c × (10/96) ≈ 20c.
            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 10, openNeed: 80, retailAvailable: 96, dueUnits: 24, dueRevenue: 96,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectRetailCounter));
            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectWholesaleContracts, proposal.RecommendedPolicy);
            Assert.IsFalse(proposal.PauseDueWholesaleAccounts);
            StringAssert.Contains("adds throughput that pays", string.Join("; ", proposal.Reasons));
        }

        [Test]
        public void Evaluator_WholesaleNeedsObservedRetailDay_BeforeItCanWin()
        {
            // Same shape as the paying-wholesale case, but no recorded
            // sales yet: the expected-retail comparison would be invented,
            // so the policy stands.
            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 0, openNeed: 80, retailAvailable: 96, dueUnits: 24, dueRevenue: 96,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectRetailCounter));
            Assert.IsFalse(proposal.HasChange);
            StringAssert.Contains("no recorded retail sales", string.Join("; ", proposal.Reasons));
        }

        [Test]
        public void Evaluator_DeliveryCostEatsTheWholesaleMargin_StandsPat()
        {
            // Net wholesale 96c − 90c delivery = 6c, below the expected
            // retail ≈ 20c for the same loaves.
            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 10, openNeed: 80, retailAvailable: 96, dueUnits: 24, dueRevenue: 96,
                deliveryCost: 90, capacity: 96, retailPrice: 8,
                current: BakerySupplyProtectionPolicy.ProtectRetailCounter));
            Assert.IsFalse(proposal.HasChange);
        }

        [Test]
        public void Evaluator_AlreadyRetailFirstAndStillStarved_PausesDueAccounts()
        {
            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 0, openNeed: 96, retailAvailable: 0, dueUnits: 24, dueRevenue: 96,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectRetailCounter));
            Assert.AreEqual(BakerySupplyProtectionPolicy.Unspecified, proposal.RecommendedPolicy,
                "the policy is already retail-first; nothing to flip");
            Assert.IsTrue(proposal.PauseDueWholesaleAccounts);
        }

        [Test]
        public void Evaluator_WholesaleFitsTheSurplus_StandsPat()
        {
            // Due 24u fits the retail-absorbable surplus (96 − 10 = 86u):
            // wholesale rides alongside retail, no policy change needed.
            BakeryChannelProposal proposal = BakeryChannelChoiceEvaluator.Evaluate(Signals(
                recordedAvg: 10, openNeed: 0, retailAvailable: 24, dueUnits: 24, dueRevenue: 96,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectRetailCounter));
            Assert.IsFalse(proposal.HasChange);
        }

        [Test]
        public void Evaluator_IsDeterministic_SameSignalsSameProposal()
        {
            var signals = Signals(
                recordedAvg: 10, openNeed: 80, retailAvailable: 96, dueUnits: 24, dueRevenue: 96,
                capacity: 96, retailPrice: 8, current: BakerySupplyProtectionPolicy.ProtectRetailCounter);
            BakeryChannelProposal first = BakeryChannelChoiceEvaluator.Evaluate(signals);
            BakeryChannelProposal second = BakeryChannelChoiceEvaluator.Evaluate(signals);
            Assert.AreEqual(first.RecommendedPolicy, second.RecommendedPolicy);
            Assert.AreEqual(first.PauseDueWholesaleAccounts, second.PauseDueWholesaleAccounts);
            Assert.AreEqual(string.Join(";", first.Reasons), string.Join(";", second.Reasons),
                "a deterministic rule returns the same reasons for the same signals");
        }

        // ---- fixtures ----

        private static BakeryChannelSignals Signals(
            int recordedAvg, int openNeed, int retailAvailable, int dueUnits, int dueRevenue,
            int capacity, int retailPrice, BakerySupplyProtectionPolicy current, int deliveryCost = 0)
        {
            return new BakeryChannelSignals
            {
                DayIndex = 301,
                RecordedRetailAverageUnitsPerDay = recordedAvg,
                OpenHouseholdBreadNeedUnits = openNeed,
                RetailUnitsAvailable = retailAvailable,
                DueWholesaleUnits = dueUnits,
                DueWholesaleRevenueCents = dueRevenue,
                DeliveryCostCents = deliveryCost,
                DailyProductionCapacityUnits = capacity,
                RetailPriceCentsPerUnit = retailPrice,
                CurrentPolicy = current,
            };
        }

        private static void BindGlenn(BakeryShopRuntime runtime, List<string> diag)
        {
            Assert.IsNull(runtime.BindOwnerPerson(
                BusinessOwnerIdentity.Npc(12, "Glenn Pierce", "Pierce"), diag),
                "Glenn binds as the NPC owner");
            Assert.AreEqual(12, runtime.OwnerPersonId);
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BakeryShopRuntime StockedShop(List<string> diag, int day = 290)
        {
            var runtime = new BakeryShopRuntime(BakeryInstanceId);
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, day, diag);
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.IsNull(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            return runtime;
        }

        private static int WorkDay(BakeryShopRuntime runtime, int day, List<string> diag)
        {
            return runtime.WorkDay(day, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, new EntityIdRegistry());
        }

        private static BakeryWholesaleAccount BoardingHouseAccount(int units = 24, int dueDay = 300, int priceCents = 4)
        {
            return new BakeryWholesaleAccount
            {
                BuyerBusinessId = "boardinghouse-biz-1",
                BuyerType = BusinessType.BoardingHouse,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = units,
                DeliveryCadenceDays = 1,
                NextDeliveryDayIndex = dueDay,
                PricePerUnitCents = priceCents,
            };
        }
    }
}
