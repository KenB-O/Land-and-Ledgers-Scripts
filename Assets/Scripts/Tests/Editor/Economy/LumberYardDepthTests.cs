using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2C: lumberyard depth — canon audit (Canon XXVII §8.2, §5.1–5.3,
    /// §6.4–6.6, Part VI §§6.1–6.4, §7.2–7.3; Tech X 6.2) against the W4B
    /// width implementation. Gaps filled: project stockpile earmarking
    /// (§6.4), delivery obligations to building sites (§6.5, VI §6.2),
    /// contractor accounts and receivables credit (VI §6.4, §7.3),
    /// landed-cost grade pricing (§5.1, §5.3), and audited cull disposal
    /// (§4.5). Written, not run (no Unity in this environment). Existing W4B
    /// LumberYardRuntimeTests are untouched and must keep passing.
    /// </summary>
    [TestFixture]
    public sealed class LumberYardDepthTests
    {
        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units, string millId, string standId)
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                SourceLogLotId = "log-" + lotNumber,
                StandId = standId,
                Species = "white pine",
                MillBusinessId = millId,
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        private static LumberYardShopRuntime StockedYard()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9401, 40, "mill-1", "stand-1"), "merchantable", 220, 85, diag));
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9402, 30, "mill-2", "stand-2"), "clear", 230, 95, diag));
            Assert.IsNull(yard.ReceiveHardwareFromBlacksmith(LumberYardHardwareKind.Nails, 20, "smith-1", 218, "iron-order-7", 220, 30, diag));
            return yard;
        }

        private static LumberYardGradePricePolicy PricedPolicy()
        {
            var policy = new LumberYardGradePricePolicy { MarkupRatio = 0.25f, SeasonalMultiplier = 1f };
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("clear", 1.5f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("merchantable", 1.0f));
            policy.GradeMultipliers.Add(new LumberYardGradePriceEntry("cull", 0.4f));
            return policy;
        }

        private static LumberYardShopRuntime AccountedYard(out LumberYardContractorAccount account)
        {
            var yard = StockedYard();
            var diag = new List<string>();
            account = new LumberYardContractorAccount
            {
                ContractorBusinessId = "builder-7",
                ContractorLabel = "Hanson & Sons Builders",
                CreditPosture = LumberYardCreditPosture.AccountOnRequest,
                Terms = new LumberYardSettlementTerms(30, 50000),
                OpenedDayIndex = 235,
            };
            Assert.IsNull(yard.ContractorLedger.OpenAccount(account, yard.IdRegistry, diag));
            return yard;
        }

        // ---- Project stockpile (Canon §6.4) ----

        [Test]
        public void Stockpile_Earmark_ProtectsFromRetailSale_AndReleases()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var lotId = yard.LumberStock.Lots[0].LotId; // 40 merchantable

            Assert.IsNull(yard.EarmarkLumberForProject(lotId, "project-house-3", 25, 240, diag));
            Assert.AreEqual(45, yard.LumberStock.AvailableUnitsForSale(null, null),
                "Earmarked stock leaves ordinary sale inventory.");
            Assert.AreEqual(25, yard.LumberStock.EarmarkedUnitsForProject("project-house-3", null, null));

            var refused = yard.TrySellLumberRetail("household-a", 50, null, null, 100, 241, diag);
            Assert.IsNull(refused, "Retail cannot buy earmarked stock.");

            var record = yard.TrySellLumberRetail("household-a", 45, null, null, 100, 241, diag);
            Assert.NotNull(record, "Unreserved stock still sells.");

            Assert.IsNull(yard.ReleaseProjectEarmark(lotId, 25, 242, diag));
            Assert.AreEqual(25, yard.LumberStock.AvailableUnitsForSale(null, null),
                "Released stock returns to sale inventory.");
            Assert.AreEqual(1 + 1, yard.Stockpile.History.Count, "Earmark and release are both in the audit history.");
        }

        [Test]
        public void Stockpile_ConstructionSale_DrawsProjectEarmarkFirst()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var lotId = yard.LumberStock.Lots[0].LotId; // 40 merchantable, day 220 (oldest)
            Assert.IsNull(yard.EarmarkLumberForProject(lotId, "project-house-3", 20, 240, diag));

            var record = yard.TrySellToConstructionProject("project-house-3", 30, 95, 0, 0, 241, diag);
            Assert.NotNull(record, "The project can draw its earmark plus ordinary stock.");
            Assert.AreEqual(30, record.UnitsSoldFor(ConstructionResourceKind.Lumber));
            Assert.AreEqual(0, yard.LumberStock.EarmarkedUnitsForProject("project-house-3", null, null),
                "Earmark is consumed by the project's own sale.");
            Assert.AreEqual(40, yard.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void Stockpile_Earmark_Refuses_MixedProjectOnSameLot_AndOverEarmark()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var lotId = yard.LumberStock.Lots[0].LotId;

            Assert.IsNull(yard.EarmarkLumberForProject(lotId, "project-a", 10, 240, diag));
            string refusal = yard.EarmarkLumberForProject(lotId, "project-b", 5, 240, diag);
            Assert.NotNull(refusal, "One lot serves one project — mixed reservations refused.");
            Assert.NotNull(yard.EarmarkLumberForProject(lotId, "project-a", 100, 240, diag),
                "Cannot earmark beyond the lot's unreserved balance.");
            Assert.AreEqual(10, yard.LumberStock.EarmarkedUnitsForProject("project-a", null, null),
                "Refused earmarks touch nothing.");
        }

        [Test]
        public void Stockpile_OtherProject_CannotSee_Earmark()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var lotId = yard.LumberStock.Lots[0].LotId;
            Assert.IsNull(yard.EarmarkLumberForProject(lotId, "project-house-3", 20, 240, diag));

            // Another project sees only the 50 unreserved units; 60 is refused.
            var refused = yard.TrySellToConstructionProject("project-other", 60, 95, 0, 0, 241, diag);
            Assert.IsNull(refused, "Earmarked stock is invisible to other projects.");
            var ok = yard.TrySellToConstructionProject("project-other", 50, 95, 0, 0, 241, diag);
            Assert.NotNull(ok);
            Assert.AreEqual(20, yard.LumberStock.TotalLumberUnits, "Only the earmark remains.");
        }

        // ---- Delivery (Canon §6.5, Part VI §6.2) ----

        [Test]
        public void Delivery_Schedule_RequiresNamedCarrier_WhenYardDelivers()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var sale = yard.TrySellToConstructionProject("project-house-3", 10, 95, 5, 35, 240, diag);
            Assert.NotNull(sale);
            var policy = new LumberYardDeliveryPolicy { PerUnitMileCents = 2, FlatChargeCents = 50, BuyerUnloadDiscountCents = 20 };

            string refusal = yard.ScheduleDeliveryForSale(sale, "house site, lot 9", 6, true, string.Empty, false, policy, 241, diag);
            Assert.NotNull(refusal, "Yard delivery without a named carrier is refused.");
            Assert.AreEqual(0, yard.DeliveryRegister.Orders.Count);

            Assert.IsNull(yard.ScheduleDeliveryForSale(sale, "house site, lot 9", 6, true, "yard teamster crew", true, policy, 241, diag));
            var order = yard.DeliveryRegister.Orders[0];
            Assert.AreEqual(sale.SaleId, order.SaleId, "Delivery fulfills a real sale.");
            // 50 flat + 10 units * 6 mi * 2c - 20 buyer-unload discount = 150c.
            Assert.AreEqual(150, order.ChargeCents, "Charge follows the policy: flat + per-unit-mile, minus buyer-unload discount.");
            Assert.IsTrue(order.BuyerProvidesUnloading);
            Assert.IsFalse(order.IsDelivered);

            Assert.IsNull(yard.MarkDeliveryComplete(order.OrderId, 243, diag));
            Assert.IsTrue(order.IsDelivered);
        }

        [Test]
        public void Delivery_Refuses_UnknownSale_AndAnonymousDestination()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var policy = new LumberYardDeliveryPolicy { PerUnitMileCents = 2, FlatChargeCents = 50 };
            var ghost = new LumberYardSaleRecord { SaleId = EntityId.For(EntityKind.Contract, 1), BuyerLabel = "ghost" };

            Assert.NotNull(yard.ScheduleDeliveryForSale(ghost, "lot 9", 6, false, string.Empty, false, policy, 241, diag),
                "Deliveries fulfill recorded sales, never thin air.");

            var sale = yard.TrySellToConstructionProject("project-house-3", 10, 95, 0, 0, 240, diag);
            Assert.NotNull(yard.ScheduleDeliveryForSale(sale, "   ", 6, false, string.Empty, false, policy, 241, diag),
                "Anonymous destinations refused.");
        }

        [Test]
        public void Delivery_BuyerHaulage_NeedsNoCarrier()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var sale = yard.TrySellLumberRetail("wheelwright-b", 5, null, null, 100, 240, diag);
            Assert.NotNull(sale);
            var policy = new LumberYardDeliveryPolicy { PerUnitMileCents = 2, FlatChargeCents = 50 };

            Assert.IsNull(yard.ScheduleDeliveryForSale(sale, "wheelwright shop", 2, false, string.Empty, false, policy, 241, diag),
                "Buyer-arranged haulage names no carrier.");
            Assert.AreEqual(string.Empty, yard.DeliveryRegister.Orders[0].CarrierLabel);
        }

        // ---- Contractor credit (Canon Part VI §6.4, §7.3) ----

        [Test]
        public void ContractorCredit_StrictPosture_RefusesAccountSale()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var strict = new LumberYardContractorAccount
            {
                ContractorBusinessId = "builder-strict",
                CreditPosture = LumberYardCreditPosture.Strict,
                Terms = new LumberYardSettlementTerms(30, 50000),
                OpenedDayIndex = 235,
            };
            Assert.IsNull(yard.ContractorLedger.OpenAccount(strict, yard.IdRegistry, diag));

            var invoice = yard.TrySellToConstructionProjectOnAccount(
                "project-house-3", "builder-strict", 10, 95, 5, 35, 240, diag);
            Assert.IsNull(invoice, "Strict posture means cash terms — no account sale.");
            Assert.AreEqual(70, yard.LumberStock.TotalLumberUnits, "Refused account sale consumes nothing.");
            Assert.AreEqual(0, yard.ContractorLedger.Invoices.Count);
        }

        [Test]
        public void ContractorCredit_AccountSale_CreatesReceivable_PaymentShrinksIt()
        {
            LumberYardContractorAccount account;
            var yard = AccountedYard(out account);
            var diag = new List<string>();

            var invoice = yard.TrySellToConstructionProjectOnAccount(
                "project-house-3", "builder-7", 20, 95, 10, 35, 240, diag);
            Assert.NotNull(invoice, "Account sale under an open account succeeds.");
            Assert.AreEqual(20 * 95 + 10 * 35, invoice.TotalCents);
            Assert.AreEqual(270, invoice.DueDayIndex, "Due day follows the account's settlement terms.");
            Assert.AreEqual(invoice.TotalCents, yard.ContractorLedger.OutstandingReceivablesCents(),
                "The invoice is a receivable, not cash.");
            Assert.AreEqual(1, yard.SalesHistory.Count, "Real lots moved on the account sale.");

            Assert.IsNull(yard.RecordContractorPayment(invoice.InvoiceId, 1000, 245, diag));
            Assert.AreEqual(invoice.TotalCents - 1000, invoice.BalanceCents);
            Assert.IsTrue(invoice.IsAccountReceivable, "Partial payment leaves a live receivable.");
        }

        [Test]
        public void ContractorCredit_Overpayment_Refused_NotPocketed()
        {
            LumberYardContractorAccount account;
            var yard = AccountedYard(out account);
            var diag = new List<string>();

            var invoice = yard.TrySellToConstructionProjectOnAccount(
                "project-house-3", "builder-7", 20, 95, 0, 0, 240, diag);
            Assert.NotNull(invoice);

            string refusal = yard.RecordContractorPayment(invoice.InvoiceId, invoice.TotalCents + 1, 245, diag);
            Assert.NotNull(refusal, "Overpayment is refused, never pocketed.");
            Assert.AreEqual(invoice.TotalCents, invoice.BalanceCents, "Balance untouched by refused payment.");
        }

        [Test]
        public void ContractorCredit_OverLimit_Refused_BeforeStockMoves()
        {
            LumberYardContractorAccount account;
            var yard = AccountedYard(out account);
            var diag = new List<string>();
            account.Terms.CreditLimitCents = 1000;

            var invoice = yard.TrySellToConstructionProjectOnAccount(
                "project-house-3", "builder-7", 20, 95, 0, 0, 240, diag);
            Assert.IsNull(invoice, "Over-limit account sale refused.");
            Assert.AreEqual(70, yard.LumberStock.TotalLumberUnits, "The limit gate fires before stock moves.");
        }

        [Test]
        public void ContractorCredit_WriteOff_AgedInvoice_StaysVisible()
        {
            LumberYardContractorAccount account;
            var yard = AccountedYard(out account);
            var diag = new List<string>();

            var invoice = yard.TrySellToConstructionProjectOnAccount(
                "project-house-3", "builder-7", 20, 95, 0, 0, 240, diag);
            Assert.NotNull(invoice);

            int writtenOff = yard.ContractorLedger.WriteOffAgedReceivables(240 + 400, 365, diag);
            Assert.AreEqual(invoice.TotalCents, writtenOff);
            Assert.IsTrue(invoice.IsBadDebt);
            Assert.AreEqual(0, yard.ContractorLedger.OutstandingReceivablesCents(),
                "Written-off debt leaves the live receivables.");
            Assert.AreEqual(1, yard.ContractorLedger.Invoices.Count,
                "Write-off erases collectability, not the record — the loss stays visible.");
        }

        [Test]
        public void ContractorCredit_DeliveryCharge_CanRide_TheInvoice()
        {
            LumberYardContractorAccount account;
            var yard = AccountedYard(out account);
            var diag = new List<string>();

            var sale = yard.TrySellToConstructionProject("project-house-3", 10, 95, 0, 0, 240, diag);
            Assert.NotNull(sale);
            var policy = new LumberYardDeliveryPolicy { PerUnitMileCents = 2, FlatChargeCents = 50 };
            Assert.IsNull(yard.ScheduleDeliveryForSale(sale, "house site, lot 9", 6, true, "yard teamster crew", false, policy, 241, diag));
            var order = yard.DeliveryRegister.Orders[0];

            var invoice = yard.IssueContractorInvoiceForSale("builder-7", sale, order, 241, diag);
            Assert.NotNull(invoice);
            Assert.AreEqual(10 * 95 + order.ChargeCents, invoice.TotalCents,
                "Delivered-material billing: materials plus the delivery charge on one invoice.");
            Assert.AreEqual(order.OrderId, invoice.DeliveryOrderId);
        }

        // ---- Landed-cost pricing (Canon §5.1, §5.3) ----

        [Test]
        public void Pricing_Quote_UsesLandedCost_AndGradeMultiplier()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var lotId = yard.LumberStock.Lots[0].LotId; // 40 merchantable @ 85c
            Assert.IsNull(yard.LumberStock.RecordLandedCharges(lotId, 400, 200, diag)); // +15c/unit landed

            var policy = PricedPolicy();
            int merchantableQuote = yard.QuoteRetailLumberPrice(null, "merchantable", policy, diag);
            // landed 100c * 1.25 markup * 1.0 grade * 1.0 seasonal = 125c.
            Assert.AreEqual(125, merchantableQuote);

            int clearQuote = yard.QuoteRetailLumberPrice(null, "clear", policy, diag);
            // clear lot: 95c landed * 1.25 * 1.5 = 178.125 -> 178c.
            Assert.AreEqual(178, clearQuote, "Clear commands the grade multiplier over landed cost.");
        }

        [Test]
        public void Pricing_Quote_Refuses_WhenNoMatchingStock()
        {
            var yard = StockedYard();
            var diag = new List<string>();
            var policy = PricedPolicy();

            Assert.AreEqual(-1, yard.QuoteRetailLumberPrice("oak", null, policy, diag),
                "No stock, no quote — prices are never invented.");
            Assert.AreEqual(-1, yard.QuoteRetailLumberPrice(null, "cull", policy, diag),
                "No cull on the shelf, no cull quote.");
        }

        [Test]
        public void Pricing_LandedCharges_Refuse_UnknownLot_AndNegatives()
        {
            var yard = StockedYard();
            var diag = new List<string>();

            Assert.NotNull(yard.LumberStock.RecordLandedCharges(
                EntityId.For(EntityKind.Lot, 999999), 100, 0, diag),
                "Charges attach to real lots only.");
            var lotId = yard.LumberStock.Lots[0].LotId;
            Assert.NotNull(yard.LumberStock.RecordLandedCharges(lotId, -5, 0, diag),
                "Negative charges refused.");
        }

        // ---- Cull disposal (Canon §4.5) ----

        [Test]
        public void CullDisposal_BurnsOnlyCull_RefusesMerchantableShortfall()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9401, 40, "mill-1", "stand-1"), "merchantable", 220, 85, diag));
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9402, 10, "mill-2", "stand-2"), "cull", 230, 20, diag));

            string refusal = yard.DisposeCullUnits(25, LumberYardCullDisposalReason.Burned, "storm-damaged", 250, diag);
            Assert.NotNull(refusal, "Only 10 cull units exist — disposal is atomic and refuses.");
            Assert.AreEqual(50, yard.LumberStock.TotalLumberUnits, "Refused disposal touches nothing.");

            Assert.IsNull(yard.DisposeCullUnits(10, LumberYardCullDisposalReason.Burned, "storm-damaged", 250, diag));
            Assert.AreEqual(40, yard.LumberStock.TotalLumberUnits, "Only the cull leaves.");
            Assert.AreEqual(1, yard.CullDisposal.Records.Count);
            var record = yard.CullDisposal.Records[0];
            Assert.AreEqual(10, record.Units);
            Assert.AreEqual(LumberYardCullDisposalReason.Burned, record.Reason);
            Assert.AreEqual(250, record.DayIndex);
            Assert.IsTrue(string.Join(" ", diag).Contains("visible"), "The disposal is announced for the ledger.");
        }

        [Test]
        public void CullDisposal_NeverTouches_EarmarkedStock()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var diag = new List<string>();
            Assert.IsNull(yard.ReceiveLumberFromSawmill(DocumentedMillLot(9401, 10, "mill-1", "stand-1"), "cull", 220, 20, diag));
            var lotId = yard.LumberStock.Lots[0].LotId;
            Assert.IsNull(yard.EarmarkLumberForProject(lotId, "project-house-3", 10, 240, diag));

            string refusal = yard.DisposeCullUnits(5, LumberYardCullDisposalReason.Discarded, string.Empty, 250, diag);
            Assert.NotNull(refusal, "Earmarked cull is a promise — disposal refuses.");
            Assert.AreEqual(10, yard.LumberStock.TotalLumberUnits);
        }

        // ---- Save/load carries the D2C registers ----

        [Test]
        public void SaveLoad_RoundTrip_Preserves_D2CRegisters()
        {
            LumberYardContractorAccount account;
            var yard = AccountedYard(out account);
            var diag = new List<string>();

            var lotId = yard.LumberStock.Lots[0].LotId;
            Assert.IsNull(yard.EarmarkLumberForProject(lotId, "project-house-3", 10, 240, diag));
            var invoice = yard.TrySellToConstructionProjectOnAccount(
                "project-house-3", "builder-7", 5, 95, 0, 0, 241, diag);
            Assert.NotNull(invoice);
            var policy = new LumberYardDeliveryPolicy { PerUnitMileCents = 2, FlatChargeCents = 50 };
            Assert.IsNull(yard.ScheduleDeliveryForSale(yard.SalesHistory[0], "lot 9", 6, false, string.Empty, false, policy, 242, diag));
            Assert.IsNull(yard.LumberStock.RecordLandedCharges(lotId, 100, 0, diag));

            var dto = yard.CaptureSaveDto();
            var restored = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(10, restored.LumberStock.EarmarkedUnitsForProject("project-house-3", null, null),
                "Earmarks survive save/load on the lots.");
            Assert.AreEqual(1, restored.ContractorLedger.Accounts.Count);
            Assert.AreEqual(1, restored.ContractorLedger.Invoices.Count);
            Assert.AreEqual(invoice.TotalCents, restored.ContractorLedger.OutstandingReceivablesCents());
            Assert.AreEqual(1, restored.DeliveryRegister.Orders.Count);
            Assert.AreEqual(100, restored.LumberStock.FindLot(lotId).LandedFreightCents,
                "Landed charges survive save/load.");
            Assert.AreEqual(1, restored.Stockpile.History.Count);
        }
    }
}
