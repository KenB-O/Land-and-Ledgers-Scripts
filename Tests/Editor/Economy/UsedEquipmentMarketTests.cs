using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.ImplementDealer;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D4L: the organized second-hand equipment market — condition grades from
    /// the existing condition model, listings as real records, price GUIDANCE
    /// (never auto-pricing), pre-purchase inspection with record-only misgrade
    /// disputes, title transfer with provenance intact, payment through real
    /// cash lots, dealer trade-ins (valuation via the existing service, credit
    /// recorded), and scrap/part-out sales at agreed prices (no synthetic
    /// scrap values). No auction — the canon describes none.
    /// </summary>
    [TestFixture]
    public sealed class UsedEquipmentMarketTests
    {
        private sealed class TestCashPort : IUsedEquipmentCashPort
        {
            private readonly Dictionary<string, int> balances =
                new Dictionary<string, int>(System.StringComparer.Ordinal);

            public void Fund(string partyKey, int cents) => balances[partyKey] = cents;
            public int BalanceOf(string partyKey) => balances.TryGetValue(partyKey, out int b) ? b : 0;

            public string MoveCash(string fromPartyKey, string toPartyKey, int amountCents,
                int dayIndex, string memo, List<string> diagnostics)
            {
                diagnostics = diagnostics ?? new List<string>();
                if (amountCents <= 0) return "TestCashPort: amount must be positive.";
                if (!balances.TryGetValue(fromPartyKey, out int from))
                    return $"TestCashPort: unknown party '{fromPartyKey}' — not a zero, a refusal.";
                if (from < amountCents)
                    return $"TestCashPort: '{fromPartyKey}' holds {from}c, needs {amountCents}c — no minting.";
                balances.TryGetValue(toPartyKey, out int to);
                balances[fromPartyKey] = from - amountCents;
                balances[toPartyKey] = to + amountCents;
                diagnostics.Add($"TestCashPort: moved {amountCents}c {fromPartyKey} -> {toPartyKey} ({memo}).");
                return null;
            }

            public string TryGetCashBalance(string partyKey, out int balanceCents)
            {
                if (!balances.TryGetValue(partyKey, out balanceCents))
                    return $"TestCashPort: unknown party '{partyKey}'.";
                return null;
            }
        }

        private static EquipmentAsset NewAsset(string assetId, string kind, float condition,
            string ownerKind, string ownerId)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                DisplayName = "Test " + kind,
                Condition01 = condition,
                OwnerKind = ownerKind,
                OwnerId = ownerId,
                LocationId = "main-st",
                MadeByBusinessId = "smith-1",
                MadeByBusinessName = "Test Smithy",
                MadeDayIndex = 5,
            };
        }

        private static UsedEquipmentMarketBook NewMarket() => new UsedEquipmentMarketBook();
        private static EntityIdRegistry NewRegistry() => new EntityIdRegistry();
        private static int PlowCost(string kind) => kind == "moldboard-plow" ? 10000 : 0;

        private static string ListingKey(UsedEquipmentListing listing) =>
            listing.ListingId.Kind + ":" + listing.ListingId.Id;

        // ---------- condition grades ----------

        [Test]
        public void GradeFor_AnchoredOnExistingModelThresholds()
        {
            Assert.AreEqual(UsedEquipmentConditionGrade.Sound, UsedEquipmentConditionGrades.GradeFor(0.95f));
            Assert.AreEqual(UsedEquipmentConditionGrade.Serviceable, UsedEquipmentConditionGrades.GradeFor(0.60f));
            Assert.AreEqual(UsedEquipmentConditionGrade.Worn, UsedEquipmentConditionGrades.GradeFor(0.30f));
            Assert.AreEqual(UsedEquipmentConditionGrade.Wrecked, UsedEquipmentConditionGrades.GradeFor(0.05f),
                "0.05 is EquipmentAsset.IsUsable's boundary — at/below it is Wrecked.");
            Assert.AreEqual(UsedEquipmentConditionGrade.Wrecked, UsedEquipmentConditionGrades.GradeFor(0.0f));
            Assert.AreEqual(UsedEquipmentConditionGrade.Serviceable, UsedEquipmentConditionGrades.GradeFor(0.50f),
                "0.50 is BusinessEquipmentRegister's worn threshold.");
        }

        // ---------- price guidance ----------

        [Test]
        public void Guidance_ConditionWeightedFromExistingValuation()
        {
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentPriceGuidance guidance = UsedEquipmentPriceGuide.GuidanceFor(plow, PlowCost);

            Assert.IsTrue(guidance.HasGuidance);
            Assert.AreEqual(10000, guidance.ReplacementCostCents);
            Assert.AreEqual(7000, guidance.ConditionWeightedValueCents,
                "Canon §11.6: the floor is replacement cost x condition.");
            Assert.AreEqual(3000, guidance.RenewalGapCents,
                "Canon §11.5 shape: (1 - condition) x cost is the renewal gap.");
        }

        [Test]
        public void Guidance_NoPriceData_LoudlyUnavailable()
        {
            EquipmentAsset oddity = NewAsset("x-1", "mystery-gadget", 0.80f, "business", "farm-1");
            UsedEquipmentPriceGuidance guidance = UsedEquipmentPriceGuide.GuidanceFor(oddity, PlowCost);

            Assert.IsFalse(guidance.HasGuidance,
                "Unknown kinds yield no guidance — never a synthetic number.");
            Assert.IsTrue(guidance.BasisNote.Contains("no replacement price data"));
        }

        // ---------- listings ----------

        [Test]
        public void ListForSale_RecordsGradeGuidanceAndProvenance()
        {
            var market = NewMarket();
            var registry = NewRegistry();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            plow.RecordMaintenance("Share replaced.", 20);

            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);

            Assert.IsNotNull(listing);
            Assert.AreEqual(UsedEquipmentListingStatus.Active, listing.Status);
            Assert.AreEqual(UsedEquipmentConditionGrade.Serviceable, listing.RecordedGrade);
            Assert.AreEqual(6500, listing.AskingPriceCents,
                "The seller sets the price — guidance is advisory.");
            Assert.AreEqual(7000, listing.PriceGuidanceCents,
                "Guidance recorded alongside the ask for transparency.");
            Assert.AreEqual(1, listing.MaintenanceHistorySnapshot.Count,
                "Maintenance history rides along for diligence (Canon §51).");
            Assert.AreEqual("Test Smithy", listing.MakerBusinessName);
        }

        [Test]
        public void ListForSale_RefusesNonOwner()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");

            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-2", "Other Farm",
                PlowCost, 30, NewRegistry(), diagnostics);

            Assert.IsNull(listing, "No selling what you don't hold.");
            Assert.AreEqual(0, market.Listings.Count);
        }

        [Test]
        public void ListForSale_RefusesReservedAsset()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            Assert.IsNull(plow.Reserve("task-9", "plowing"));

            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, NewRegistry(), diagnostics);

            Assert.IsNull(listing, "Reserved assets cannot be double-dealt (Tech X §3.8).");
        }

        [Test]
        public void ListForSale_RefusesNonPositivePrice()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");

            Assert.IsNull(market.ListForSale(plow, 0, "business", "farm-1", "Test Farm",
                PlowCost, 30, NewRegistry(), diagnostics));
            Assert.IsNull(market.ListForSale(plow, -100, "business", "farm-1", "Test Farm",
                PlowCost, 30, NewRegistry(), diagnostics));
        }

        [Test]
        public void WithdrawListing_RemovesFromSale()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, NewRegistry(), diagnostics);

            Assert.IsNull(market.WithdrawListing(ListingKey(listing), 31, "decided to keep it", diagnostics));
            Assert.AreEqual(UsedEquipmentListingStatus.Withdrawn, listing.Status);

            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("buyer-1"), 999999);
            UsedEquipmentSale sale = market.Purchase(
                ListingKey(listing), plow, "business", "buyer-1", "Buyer",
                port, 32, NewRegistry(), diagnostics);
            Assert.IsNull(sale, "Withdrawn listings are not for sale.");
        }

        // ---------- inspection + disputes ----------

        [Test]
        public void Inspection_Match_ConfirmsGradeNoDispute()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, NewRegistry(), diagnostics);

            EquipmentInspectionRecord inspection = market.RecordInspection(
                ListingKey(listing), plow, "person", "7", "Inspector",
                0.68f, 31, "looks honest", NewRegistry(), diagnostics);

            Assert.IsNotNull(inspection);
            Assert.AreEqual(EquipmentInspectionVerdict.MatchesRecordedGrade, inspection.Verdict);
            Assert.AreEqual(0, market.Disputes.Count);
            Assert.AreEqual(0, listing.OpenDisputeCount);
        }

        [Test]
        public void Inspection_Misgrade_OpensRecordOnlyDispute()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.30f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, NewRegistry(), diagnostics);
            // The seller claims Serviceable on a Worn plow.
            listing.RecordedGrade = UsedEquipmentConditionGrade.Serviceable;

            EquipmentInspectionRecord inspection = market.RecordInspection(
                ListingKey(listing), plow, "person", "7", "Inspector",
                0.30f, 31, "far more wear than claimed", NewRegistry(), diagnostics);

            Assert.IsNotNull(inspection);
            Assert.AreEqual(EquipmentInspectionVerdict.Misgraded, inspection.Verdict);
            Assert.AreEqual(1, market.Disputes.Count);
            Assert.AreEqual(1, market.Disputes.OpenCount());
            Assert.AreEqual(1, listing.OpenDisputeCount);
            Assert.AreEqual(UsedEquipmentListingStatus.Active, listing.Status,
                "Disputes are record-only: the book does not auto-withdraw, auto-reprice, or auto-resolve.");
            foreach (var kv in market.Disputes.Disputes)
                Assert.AreEqual(EquipmentDisputeStatus.Recorded, kv.Value.Status);
        }

        [Test]
        public void Purchase_BlockedWhileDisputeOpen_ResumesAfterExplicitResolution()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.30f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 4000, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);
            listing.RecordedGrade = UsedEquipmentConditionGrade.Serviceable; // misgraded
            market.RecordInspection(ListingKey(listing), plow, "person", "7", "Inspector",
                0.30f, 31, "misgraded", registry, diagnostics);

            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("farm-1"), 0);
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("buyer-1"), 50000);

            UsedEquipmentSale blocked = market.Purchase(
                ListingKey(listing), plow, "business", "buyer-1", "Buyer",
                port, 32, registry, diagnostics);
            Assert.IsNull(blocked, "Open disputes block sale by default (calibration).");

            string disputeKey = null;
            foreach (var kv in market.Disputes.Disputes) disputeKey = kv.Key;
            Assert.IsNull(market.RecordDisputeResolution(
                disputeKey, EquipmentDisputeStatus.Dismissed, "operator",
                "seller agreed to correct on relist; this sale cancelled instead", 33, diagnostics));
            Assert.AreEqual(0, market.Disputes.OpenCount());
            Assert.AreEqual(0, listing.OpenDisputeCount);

            UsedEquipmentSale sale = market.Purchase(
                ListingKey(listing), plow, "business", "buyer-1", "Buyer",
                port, 34, registry, diagnostics);
            Assert.IsNotNull(sale, "After explicit resolution, trade resumes.");
        }

        // ---------- purchase ----------

        [Test]
        public void Purchase_TransfersTitleAndMovesRealMoney()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);

            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("farm-1"), 1000);
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("buyer-1"), 20000);

            UsedEquipmentSale sale = market.Purchase(
                ListingKey(listing), plow, "business", "buyer-1", "Buyer Farm",
                port, 35, registry, diagnostics);

            Assert.IsNotNull(sale);
            Assert.AreEqual(6500, sale.PriceCents);
            Assert.AreEqual("buyer-1", plow.OwnerId);
            Assert.AreEqual("business", plow.OwnerKind);
            Assert.AreEqual(13500, port.BalanceOf(UsedEquipmentPartyKeys.BusinessKey("buyer-1")));
            Assert.AreEqual(7500, port.BalanceOf(UsedEquipmentPartyKeys.BusinessKey("farm-1")),
                "Real lots move: buyer down 6500c, seller up 6500c.");
            Assert.AreEqual(UsedEquipmentListingStatus.Sold, listing.Status);
            Assert.IsTrue(plow.OwnershipHistory.Count >= 1,
                "Title transfer appends to the ownership chain — provenance intact.");
            EquipmentOwnershipRecord last = plow.OwnershipHistory[plow.OwnershipHistory.Count - 1];
            Assert.AreEqual("farm-1", last.FromOwnerId);
            Assert.AreEqual("buyer-1", last.ToOwnerId);
            Assert.AreEqual(35, last.DayIndex);
        }

        [Test]
        public void Purchase_RefusedWhenBuyerCannotPay()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);

            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("farm-1"), 1000);
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("buyer-1"), 100); // short

            UsedEquipmentSale sale = market.Purchase(
                ListingKey(listing), plow, "business", "buyer-1", "Buyer",
                port, 35, registry, diagnostics);

            Assert.IsNull(sale, "The port refuses; nothing is sold and nothing moves.");
            Assert.AreEqual(UsedEquipmentListingStatus.Active, listing.Status);
            Assert.AreEqual("farm-1", plow.OwnerId, "Ownership unchanged.");
            Assert.AreEqual(100, port.BalanceOf(UsedEquipmentPartyKeys.BusinessKey("buyer-1")));
        }

        [Test]
        public void Purchase_RefusedWhenConditionDrifted()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);

            plow.ApplyWear(0.20f); // the asset wore after listing

            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("buyer-1"), 999999);

            UsedEquipmentSale sale = market.Purchase(
                ListingKey(listing), plow, "business", "buyer-1", "Buyer",
                port, 35, registry, diagnostics);
            Assert.IsNull(sale, "The listing no longer describes the asset — withdraw and relist.");
            Assert.AreEqual(UsedEquipmentListingStatus.Active, listing.Status);
        }

        [Test]
        public void Purchase_RefusedWhenSellerNoLongerOwns()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);

            // The asset moved out from under the listing (e.g. a private deal).
            Assert.IsNull(plow.TransferOwnership("business", "farm-9", "private deal", 33));

            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("buyer-1"), 999999);

            UsedEquipmentSale sale = market.Purchase(
                ListingKey(listing), plow, "business", "buyer-1", "Buyer",
                port, 35, registry, diagnostics);
            Assert.IsNull(sale, "Stale listings cannot sell.");
        }

        [Test]
        public void Purchase_SellerCannotBuyOwnListing()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);

            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("farm-1"), 999999);

            UsedEquipmentSale sale = market.Purchase(
                ListingKey(listing), plow, "business", "farm-1", "Test Farm",
                port, 35, registry, diagnostics);
            Assert.IsNull(sale);
        }

        // ---------- trade-ins ----------

        [Test]
        public void TradeIn_ValuationViaExistingService_CreditRecordedSeparately()
        {
            var dealer = new ImplementDealer("dealer-1", "Hart & Sons Implements", "main-st");
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-9", "moldboard-plow", 0.60f, "business", "farm-1");

            DealerTradeInRecord tradeIn = dealer.TakeTradeIn(
                plow, "business", "farm-1", "Test Farm",
                10000, 5500, 40, NewRegistry(), diagnostics);

            Assert.IsNotNull(tradeIn);
            Assert.AreEqual(6000, tradeIn.DealerValuationCents,
                "Valuation runs through the existing service: cost x condition (Canon §11.6).");
            Assert.AreEqual(5500, tradeIn.AgreedCreditCents,
                "The credit is the negotiated recorded amount — it may differ from the valuation.");
            Assert.AreEqual("dealer-1", plow.OwnerId,
                "Title transfers to the dealer with provenance intact.");
            Assert.AreEqual(DealerTradeInStatus.Recorded, tradeIn.Status);
        }

        [Test]
        public void TradeIn_AgreedCreditMayDifferFromValuation()
        {
            var dealer = new ImplementDealer("dealer-1", "Hart & Sons Implements", "main-st");
            var diagnostics = new List<string>();
            // Dealer wants the machine badly: credits ABOVE its valuation.
            EquipmentAsset plow = NewAsset("plow-9", "moldboard-plow", 0.60f, "business", "farm-1");

            DealerTradeInRecord tradeIn = dealer.TakeTradeIn(
                plow, "business", "farm-1", "Test Farm",
                10000, 7000, 40, NewRegistry(), diagnostics);

            Assert.IsNotNull(tradeIn);
            Assert.AreEqual(6000, tradeIn.DealerValuationCents);
            Assert.AreEqual(7000, tradeIn.AgreedCreditCents,
                "Canon §7.2D pricing freedom: the parties' deal stands.");
        }

        [Test]
        public void TradeIn_RequiresRealPriceData_NeverInvented()
        {
            var dealer = new ImplementDealer("dealer-1", "Hart & Sons Implements", "main-st");
            var diagnostics = new List<string>();
            EquipmentAsset oddity = NewAsset("x-1", "mystery-gadget", 0.60f, "business", "farm-1");

            DealerTradeInRecord tradeIn = dealer.TakeTradeIn(
                oddity, "business", "farm-1", "Test Farm",
                0, 1000, 40, NewRegistry(), diagnostics);

            Assert.IsNull(tradeIn, "No price data, no valuation — the book invents nothing.");
            Assert.AreEqual("farm-1", oddity.OwnerId, "Title never moved.");
        }

        [Test]
        public void TradeIn_ApplyCreditToSale_RecordedAndBounded()
        {
            var dealer = new ImplementDealer("dealer-1", "Hart & Sons Implements", "main-st");
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-9", "moldboard-plow", 0.60f, "business", "farm-1");
            DealerTradeInRecord tradeIn = dealer.TakeTradeIn(
                plow, "business", "farm-1", "Test Farm",
                10000, 5500, 40, registry, diagnostics);
            string key = tradeIn.TradeInId.Kind + ":" + tradeIn.TradeInId.Id;

            EntityId saleId = registry.Allocate(EntityKind.Contract);
            Assert.IsNull(dealer.ApplyTradeInCredit(key, saleId, 5500, 41, diagnostics));
            Assert.AreEqual(0, tradeIn.RemainingCreditCents);
            Assert.AreEqual(DealerTradeInStatus.CreditApplied, tradeIn.Status);
            Assert.AreEqual(1, dealer.TradeInBook.Applications.Count);
            Assert.AreEqual(5500, dealer.TradeInBook.Applications[0].CreditCents);

            string over = dealer.ApplyTradeInCredit(key, saleId, 1, 42, diagnostics);
            Assert.IsNotNull(over, "The book never mints credit beyond what was agreed.");
        }

        [Test]
        public void TradeIn_RefusesNonOwnerAndReserved()
        {
            var dealer = new ImplementDealer("dealer-1", "Hart & Sons Implements", "main-st");
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-9", "moldboard-plow", 0.60f, "business", "farm-1");

            Assert.IsNull(dealer.TakeTradeIn(plow, "business", "farm-2", "Other",
                10000, 1000, 40, NewRegistry(), diagnostics),
                "No trading what you don't hold.");

            Assert.IsNull(plow.Reserve("task-1", "plowing"));
            Assert.IsNull(dealer.TakeTradeIn(plow, "business", "farm-1", "Test Farm",
                10000, 1000, 40, NewRegistry(), diagnostics),
                "Reserved assets cannot be double-dealt.");
        }

        // ---------- salvage ----------

        [Test]
        public void ScrapSale_UsableAssetRefused()
        {
            var ledger = new EquipmentSalvageLedger();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.40f, "business", "farm-1");

            EquipmentScrapSale sale = ledger.RecordScrapSale(
                plow, "business", "farm-1", "Test Farm",
                "business", "smith-1", "Test Smithy",
                150, EquipmentSalvageDisposition.Scrapped, 50, "",
                NewRegistry(), diagnostics);

            Assert.IsNull(sale, "A usable plow is not scrap — repair it, list it, or keep it.");
            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void ScrapSale_BelowUsable_RecordsAgreedPrice()
        {
            var ledger = new EquipmentSalvageLedger();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.03f, "business", "farm-1");

            EquipmentScrapSale sale = ledger.RecordScrapSale(
                plow, "business", "farm-1", "Test Farm",
                "business", "smith-1", "Test Smithy",
                150, EquipmentSalvageDisposition.PartedOut, 50, "share cracked beyond repair",
                NewRegistry(), diagnostics);

            Assert.IsNotNull(sale);
            Assert.AreEqual(150, sale.AgreedPriceCents,
                "The price is whatever the parties agreed — the ledger never values scrap.");
            Assert.AreEqual(EquipmentSalvageDisposition.PartedOut, sale.Disposition);
            Assert.AreEqual("smith-1", plow.OwnerId);
            Assert.AreEqual(0f, plow.Condition01, "Dismantled: condition goes to zero.");
            Assert.IsTrue(plow.OwnershipHistory.Count >= 1, "Provenance intact through salvage.");
        }

        [Test]
        public void ScrapSale_NeedsNamedBuyer()
        {
            var ledger = new EquipmentSalvageLedger();
            var diagnostics = new List<string>();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.02f, "business", "farm-1");

            Assert.IsNull(ledger.RecordScrapSale(
                plow, "business", "farm-1", "Test Farm",
                "business", "", "",
                150, EquipmentSalvageDisposition.Scrapped, 50, "",
                NewRegistry(), diagnostics),
                "No anonymous scrap.");
            Assert.AreEqual("farm-1", plow.OwnerId, "Nothing moved.");
        }

        // ---------- save/load ----------

        [Test]
        public void MarketBook_SaveLoad_RoundTrip()
        {
            var market = NewMarket();
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-1", "moldboard-plow", 0.70f, "business", "farm-1");
            UsedEquipmentListing listing = market.ListForSale(
                plow, 6500, "business", "farm-1", "Test Farm",
                PlowCost, 30, registry, diagnostics);
            market.RecordInspection(ListingKey(listing), plow, "person", "7", "Inspector",
                0.68f, 31, "fine", registry, diagnostics);
            var port = new TestCashPort();
            port.Fund(UsedEquipmentPartyKeys.BusinessKey("buyer-1"), 999999);
            market.Purchase(ListingKey(listing), plow, "business", "buyer-1", "Buyer",
                port, 35, registry, diagnostics);

            UsedEquipmentMarketBook.UsedEquipmentMarketBookDto dto = market.ToSaveDto();
            var restored = new UsedEquipmentMarketBook();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.Listings.Count);
            Assert.AreEqual(1, restored.Sales.Count);
            Assert.AreEqual(1, restored.Inspections.Count);
            Assert.AreEqual(UsedEquipmentListingStatus.Sold,
                restored.FindListing(ListingKey(listing)).Status);
        }

        [Test]
        public void TradeInBook_SaveLoad_RoundTrip()
        {
            var dealer = new ImplementDealer("dealer-1", "Hart & Sons Implements", "main-st");
            var diagnostics = new List<string>();
            var registry = NewRegistry();
            EquipmentAsset plow = NewAsset("plow-9", "moldboard-plow", 0.60f, "business", "farm-1");
            DealerTradeInRecord tradeIn = dealer.TakeTradeIn(
                plow, "business", "farm-1", "Test Farm",
                10000, 5500, 40, registry, diagnostics);
            string key = tradeIn.TradeInId.Kind + ":" + tradeIn.TradeInId.Id;
            dealer.ApplyTradeInCredit(key, registry.Allocate(EntityKind.Contract), 2000, 41, diagnostics);

            ImplementDealerTradeInBook.ImplementDealerTradeInBookDto dto =
                dealer.TradeInBook.ToSaveDto();
            var restored = new ImplementDealerTradeInBook();
            restored.LoadFromSaveDto(dto);

            DealerTradeInRecord found = restored.Find(key);
            Assert.IsNotNull(found);
            Assert.AreEqual(5500, found.AgreedCreditCents);
            Assert.AreEqual(3500, found.RemainingCreditCents);
            Assert.AreEqual(1, restored.Applications.Count);
        }
    }
}
