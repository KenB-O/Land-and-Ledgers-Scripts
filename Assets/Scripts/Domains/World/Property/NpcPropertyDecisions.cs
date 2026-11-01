using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Phase F (Real People): why the household is deciding. Shelter seeks
    /// the cheapest adequate roof; Ownership seeks a purchase it can carry;
    /// Investment seeks a purchase to hold for return (F4 rents it out).
    /// Append-only.
    /// </summary>
    public enum NpcPropertyMotive
    {
        Unspecified = 0,
        Shelter = 1,
        Ownership = 2,
        Investment = 3,
    }

    /// <summary>
    /// Phase F: the NPC property decision. Append-only.
    /// </summary>
    public enum NpcPropertyDecisionKind
    {
        Unspecified = 0,
        ContinueSearching = 1,
        Rent = 2,
        BuyExistingProperty = 3,
        BuyLandAndBuild = 4,
        WalkAway = 5,
    }

    /// <summary>
    /// Phase F: financing the household has ACTUALLY secured, assembled by
    /// the driver from real credit state — never assumed by the engine:
    /// <list type="bullet">
    /// <item>Seller-finance leg: a TermsAccepted negotiation's closing terms
    /// (the seller really offered them).</item>
    /// <item>Lender leg: an evaluated, acceptable credit offer from the real
    /// <c>CreditOfferWorkflow</c>.</item>
    /// </list>
    /// Cash is read from the real household ledger at decision time.
    /// </summary>
    [Serializable]
    public sealed class NpcPropertyFinancingAvailability
    {
        public int HouseholdId = -1;
        public int DocumentedMonthlyIncomeCents;

        // Seller-finance leg (from a TermsAccepted negotiation).
        public bool HasSellerFinanceTerms;
        public string SellerFinanceNegotiationId = string.Empty;
        /// <summary>The asset the terms attach to (space or parcel id); empty = the seller's terms apply to any purchase.</summary>
        public string SellerFinanceAssetId = string.Empty;
        public int SellerFinancePriceCents;
        public int SellerFinanceDownPaymentCents;
        /// <summary>The seller's own stated monthly payment — legitimate NPC knowledge.</summary>
        public int SellerFinanceMonthlyCents;
        public int SellerFinanceFinancedCents => Math.Max(0, SellerFinancePriceCents - SellerFinanceDownPaymentCents);

        // Lender leg (from an evaluated credit offer).
        public bool HasLenderOffer;
        public string LenderOfferId = string.Empty;
        public string LenderName = string.Empty;
        public int LenderOfferAmountCents;
        public int LenderOfferRateBps;
        public int LenderOfferTermDays;
        /// <summary>Estimated monthly payment, computed by the driver from the offer. 0 = the engine estimates it.</summary>
        public int LenderOfferMonthlyCents;

        public NpcPropertyFinancingAvailability() { }
    }

    /// <summary>
    /// Phase F: a recorded NPC property decision — what was chosen, the
    /// funding behind it, and EVERY rejected alternative with its reason
    /// (§26 observability). A decision carries no house and no money; F2/F3
    /// execute buys, Phase D's executor executes rentals.
    /// </summary>
    [Serializable]
    public sealed class NpcPropertyDecision
    {
        public string DecisionId = string.Empty;
        public string NeedId = string.Empty;
        public int HouseholdId = -1;
        public int DayIndex;
        public NpcPropertyDecisionKind Kind = NpcPropertyDecisionKind.Unspecified;
        public NpcPropertyMotive Motive = NpcPropertyMotive.Unspecified;
        /// <summary>The search path of the chosen option (Rental, Purchase, LandPurchaseAndBuild...).</summary>
        public HousingSearchPathKind ChosenPath = HousingSearchPathKind.Unspecified;
        public HousingSearchOption ChosenOption;
        public int AgreedPriceCents;
        public int CashDownCents;
        public int FinancedCents;
        public int EstimatedMonthlyBurdenCents;
        /// <summary>For land+build: the catalog design the NPC selected.</summary>
        public string ChosenDesignId = string.Empty;
        public int EstimatedBuildCostCents;
        public List<HousingRejectedAlternative> RejectedAlternatives = new List<HousingRejectedAlternative>();
        public string Rationale = string.Empty;
        public bool NeedWithdrawn;

        public NpcPropertyDecision() { }
    }

    /// <summary>
    /// Phase F (F1): the NPC property decision engine. A household with a
    /// housing need (or investment motive) evaluates REAL options and
    /// DECIDES: continue searching, rent, buy existing, or buy land + build
    /// — or WALKS AWAY.
    ///
    /// The decision reuses Phase D's <see cref="HousingSearchPlanner"/>
    /// ranking over the same opportunity source the player sees, then — for
    /// ownership paths — evaluates FINANCED affordability against real
    /// ledger cash, documented income, and financing the household actually
    /// secured (seller-finance terms from an accepted negotiation, a lender
    /// offer from the real credit workflow).
    ///
    /// Risk assessment is what the NPC could legitimately perform: down
    /// payment from current cash only, monthly burden against documented
    /// income with a reserve margin, a cash reserve left after closing. The
    /// NPC never uses omniscient valuation — asking prices are taken at
    /// face value; price negotiation happens in F2.
    /// </summary>
    public sealed class NpcPropertyDecisionEngine
    {
        /// <summary>
        /// TUNING: the share of documented monthly income the NPC will commit
        /// to housing (debt service + tax + maintenance). Canon prescribes no
        /// burden ratio; this is calibration, not doctrine.
        /// </summary>
        public double MaxMonthlyHousingBurdenShare01 { get; set; } = 0.5;

        /// <summary>
        /// TUNING: cash the NPC refuses to commit at closing — the reserve
        /// they keep for the household to live on.
        /// </summary>
        public int MinimumCashReserveAfterClosingCents { get; set; } = 0;

        /// <summary>
        /// TUNING: annual property-tax rate the assessor publishes, in basis
        /// points of price. 0 = unknown — tax is then excluded from the
        /// burden with a diagnostic note, never invented.
        /// </summary>
        public int AssumedAnnualPropertyTaxRateBps { get; set; } = 0;

        /// <summary>TUNING: monthly maintenance reserve the NPC budgets. 0 = none budgeted.</summary>
        public int AssumedMonthlyMaintenanceCents { get; set; } = 0;

        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public NpcPropertyDecision Decide(
            HousingSearchNeed need,
            HouseholdCompositionSummary composition,
            HouseholdLedger ledger,
            NpcPropertyMotive motive,
            IHousingOpportunitySource source,
            NpcPropertyFinancingAvailability financing,
            IConstructionMaterialPricePort pricePort,
            BuildingDesignCatalog catalog,
            HousingSearchNeedRegistry needs,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var decision = new NpcPropertyDecision
            {
                DecisionId = $"npd-{sequence++}",
                NeedId = need != null ? need.NeedId : string.Empty,
                HouseholdId = need != null ? need.HouseholdId : -1,
                DayIndex = dayIndex,
                Motive = motive,
            };
            if (need == null || ledger == null || source == null)
            {
                decision.Kind = NpcPropertyDecisionKind.ContinueSearching;
                decision.Rationale = "no search need, no ledger, or no opportunity source — nothing decided; the search continues.";
                diag.Add($"NpcPropertyDecisionEngine: {decision.Rationale}");
                return decision;
            }

            int cash = ledger.GetBalanceCents();
            int income = financing != null ? financing.DocumentedMonthlyIncomeCents : 0;
            var planner = new HousingSearchPlanner();
            HousingSearchDecision plan = planner.Plan(need, composition, cash, income, source, dayIndex, diag);
            decision.RejectedAlternatives.AddRange(plan.RejectedAlternatives);

            if (plan.Resolved && plan.ChosenOption != null)
            {
                HousingSearchPathKind path = plan.ChosenOption.PathKind;
                if (IsNonOwnershipPath(path))
                {
                    // Ownership-motivated households check financed ownership
                    // BEFORE settling for rent; shelter-motivated ones take
                    // the cheapest adequate roof the planner found.
                    if ((motive == NpcPropertyMotive.Ownership || motive == NpcPropertyMotive.Investment)
                        && TryDecideFinancedPurchase(decision, need, cash, income, source, financing,
                            pricePort, catalog, dayIndex, diag))
                        return decision;
                    return RecordRent(decision, plan, dayIndex, diag);
                }
                if (path == HousingSearchPathKind.Purchase || path == HousingSearchPathKind.SellerFinanceNegotiation)
                    return RecordCashPurchase(decision, plan, cash, dayIndex, diag);
                if (path == HousingSearchPathKind.LandPurchaseAndBuild)
                {
                    // The planner only checked the LAND price against cash —
                    // F1 still selects the design, estimates the build, and
                    // checks financing and risk before committing.
                    if (TryDecideSingleLandBuild(decision, plan.ChosenOption, need, cash, income,
                        financing, pricePort, catalog, dayIndex, diag))
                        return decision;
                    return RecordNoPurchase(decision, plan, needs, dayIndex, diag);
                }
                if (path == HousingSearchPathKind.Relocate)
                {
                    decision.Kind = NpcPropertyDecisionKind.ContinueSearching;
                    decision.ChosenPath = path;
                    decision.ChosenOption = plan.ChosenOption;
                    decision.Rationale = "relocation is a migration decision for the migration authority; the property search continues meanwhile.";
                    diag.Add($"NpcPropertyDecisionEngine: H{decision.HouseholdId} — {decision.Rationale}");
                    return decision;
                }
                // LenderApproach resolved on its own: a loan without a property
                // is not a property decision.
                decision.Kind = NpcPropertyDecisionKind.ContinueSearching;
                decision.ChosenPath = path;
                decision.ChosenOption = plan.ChosenOption;
                decision.Rationale = $"the planner resolved '{path}', which names no property — the search continues.";
                diag.Add($"NpcPropertyDecisionEngine: H{decision.HouseholdId} — {decision.Rationale}");
                return decision;
            }

            // The planner resolved nothing affordable in cash. Try financed ownership.
            if (TryDecideFinancedPurchase(decision, need, cash, income, source, financing,
                pricePort, catalog, dayIndex, diag))
                return decision;

            return RecordNoPurchase(decision, plan, needs, dayIndex, diag);
        }

        private static bool IsNonOwnershipPath(HousingSearchPathKind path)
        {
            return path == HousingSearchPathKind.Rental
                || path == HousingSearchPathKind.Boarding
                || path == HousingSearchPathKind.KinInquiry
                || path == HousingSearchPathKind.EmployerLodging
                || path == HousingSearchPathKind.TemporaryShelter;
        }

        private NpcPropertyDecision RecordRent(NpcPropertyDecision decision, HousingSearchDecision plan,
            int dayIndex, List<string> diag)
        {
            decision.Kind = NpcPropertyDecisionKind.Rent;
            decision.ChosenPath = plan.ChosenOption.PathKind;
            decision.ChosenOption = plan.ChosenOption;
            decision.Rationale = $"rent '{plan.ChosenOption.Description}' — cheapest adequate roof; ownership not taken.";
            diag.Add($"NpcPropertyDecisionEngine: H{decision.HouseholdId} RENTS ({decision.ChosenPath}). {decision.RejectedAlternatives.Count} alternative(s) rejected with reasons.");
            return decision;
        }

        private NpcPropertyDecision RecordCashPurchase(NpcPropertyDecision decision, HousingSearchDecision plan,
            int cash, int dayIndex, List<string> diag)
        {
            decision.Kind = NpcPropertyDecisionKind.BuyExistingProperty;
            decision.ChosenPath = plan.ChosenOption.PathKind;
            decision.ChosenOption = plan.ChosenOption;
            decision.AgreedPriceCents = plan.ChosenOption.TotalPriceCents;
            decision.CashDownCents = plan.ChosenOption.TotalPriceCents;
            decision.FinancedCents = 0;
            decision.EstimatedMonthlyBurdenCents = EstimateMonthlyTax(decision.AgreedPriceCents) + AssumedMonthlyMaintenanceCents;
            decision.Rationale = $"buy '{plan.ChosenOption.Description}' outright for {decision.AgreedPriceCents}c from real cash ({cash}c held).";
            diag.Add($"NpcPropertyDecisionEngine: H{decision.HouseholdId} BUYS existing property for {decision.AgreedPriceCents}c cash.");
            return decision;
        }

        /// <summary>
        /// Full land+build evaluation for a single land option (used when
        /// the planner resolved the land on price alone): design selection,
        /// build estimate, financing, and risk — before any commitment.
        /// </summary>
        private bool TryDecideSingleLandBuild(
            NpcPropertyDecision decision,
            HousingSearchOption landOption,
            HousingSearchNeed need,
            int cash,
            int income,
            NpcPropertyFinancingAvailability financing,
            IConstructionMaterialPricePort pricePort,
            BuildingDesignCatalog catalog,
            int dayIndex,
            List<string> diag)
        {
            var viable = new List<FinancedCandidate>();
            EvaluateLandBuild(decision, landOption, Math.Max(1, need.RequiredSleepingPlaces),
                cash, income, financing, pricePort, catalog, viable, diag);
            if (viable.Count == 0) return false;
            RecordFinancedDecision(decision, viable[0], NpcPropertyDecisionKind.BuyLandAndBuild, dayIndex, diag);
            return true;
        }

        private void RecordFinancedDecision(NpcPropertyDecision decision, FinancedCandidate best,
            NpcPropertyDecisionKind kind, int dayIndex, List<string> diag)
        {
            decision.Kind = kind;
            decision.ChosenPath = best.Option.PathKind;
            decision.ChosenOption = best.Option;
            decision.AgreedPriceCents = best.TotalPriceCents;
            decision.CashDownCents = best.CashDownCents;
            decision.FinancedCents = best.FinancedCents;
            decision.EstimatedMonthlyBurdenCents = best.MonthlyBurdenCents;
            decision.ChosenDesignId = best.DesignId ?? string.Empty;
            decision.EstimatedBuildCostCents = best.BuildCostCents;
            decision.Rationale = best.Rationale;
            diag.Add($"NpcPropertyDecisionEngine: H{decision.HouseholdId} DECIDES {kind} — '{best.Option.Description}' " +
                $"at {best.TotalPriceCents}c ({best.CashDownCents}c down, {best.FinancedCents}c financed, ~{best.MonthlyBurdenCents}c/mo).");
        }

        /// <summary>
        /// Financed ownership evaluation: every purchase-type option is
        /// tested against real cash + confirmed financing + the NPC's own
        /// risk checks. Returns true when a viable purchase was recorded.
        /// </summary>
        private bool TryDecideFinancedPurchase(
            NpcPropertyDecision decision,
            HousingSearchNeed need,
            int cash,
            int income,
            IHousingOpportunitySource source,
            NpcPropertyFinancingAvailability financing,
            IConstructionMaterialPricePort pricePort,
            BuildingDesignCatalog catalog,
            int dayIndex,
            List<string> diag)
        {
            int required = Math.Max(1, need.RequiredSleepingPlaces);
            var viable = new List<FinancedCandidate>();

            foreach (HousingSearchOption option in source.PropertiesForSale(required))
                EvaluateBuyExisting(decision, option, cash, income, financing, viable, diag);
            foreach (HousingSearchOption option in source.SellerFinanceOffers())
                EvaluateBuyExisting(decision, option, cash, income, financing, viable, diag);
            foreach (HousingSearchOption option in source.LandParcelsForSale())
                EvaluateLandBuild(decision, option, required, cash, income, financing, pricePort, catalog, viable, diag);

            if (viable.Count == 0) return false;

            // The NPC's own preference: lowest monthly burden first, then lowest price.
            viable.Sort((a, b) =>
            {
                int c = a.MonthlyBurdenCents.CompareTo(b.MonthlyBurdenCents);
                return c != 0 ? c : a.TotalPriceCents.CompareTo(b.TotalPriceCents);
            });
            FinancedCandidate best = viable[0];
            RecordFinancedDecision(decision,
                best,
                best.IsLandBuild ? NpcPropertyDecisionKind.BuyLandAndBuild : NpcPropertyDecisionKind.BuyExistingProperty,
                dayIndex, diag);
            diag.Add($"NpcPropertyDecisionEngine: {viable.Count - 1} other viable purchase(s) not taken.");
            return true;
        }

        private sealed class FinancedCandidate
        {
            public HousingSearchOption Option;
            public bool IsLandBuild;
            public string DesignId;
            public int TotalPriceCents;
            public int CashDownCents;
            public int FinancedCents;
            public int MonthlyBurdenCents;
            public int BuildCostCents;
            public string Rationale;
        }

        private void EvaluateBuyExisting(
            NpcPropertyDecision decision,
            HousingSearchOption option,
            int cash,
            int income,
            NpcPropertyFinancingAvailability financing,
            List<FinancedCandidate> viable,
            List<string> diag)
        {
            if (option == null) return;
            if (option.PathKind != HousingSearchPathKind.Purchase
                && option.PathKind != HousingSearchPathKind.SellerFinanceNegotiation)
                return;
            if (option.TotalPriceCents <= 0)
            {
                Reject(decision, option, "no real price named — a purchase without a price is not an option.");
                return;
            }
            int price = option.TotalPriceCents;
            int reserve = Math.Max(0, MinimumCashReserveAfterClosingCents);
            int cashAvailable = Math.Max(0, cash - reserve);

            // Funding stack: seller-finance terms when they attach to this asset, else cash + lender loan.
            bool sellerTermsApply = financing != null && financing.HasSellerFinanceTerms
                && (string.IsNullOrWhiteSpace(financing.SellerFinanceAssetId)
                    || string.Equals(financing.SellerFinanceAssetId, option.SpaceId, StringComparison.Ordinal)
                    || string.Equals(financing.SellerFinanceAssetId, option.ParcelId, StringComparison.Ordinal)
                    || string.Equals(financing.SellerFinanceAssetId, option.BuildingId, StringComparison.Ordinal));
            int down, financed, monthly;
            string fundingSummary;
            if (sellerTermsApply)
            {
                down = financing.SellerFinanceDownPaymentCents;
                financed = Math.Max(0, price - down);
                if (financing.SellerFinancePriceCents != price)
                {
                    Reject(decision, option, $"the accepted seller terms name {financing.SellerFinancePriceCents}c but the asking price is {price}c — terms and price disagree.");
                    return;
                }
                monthly = Math.Max(0, financing.SellerFinanceMonthlyCents);
                fundingSummary = $"seller-finance ({down}c down + {financed}c note at {monthly}c/mo)";
            }
            else
            {
                down = Math.Min(cashAvailable, price);
                financed = price - down;
                if (financed > 0)
                {
                    if (financing == null || !financing.HasLenderOffer || financing.LenderOfferAmountCents < financed)
                    {
                        Reject(decision, option, $"needs {financed}c of financing but no confirmed lender offer covers it (cash covers {down}c of {price}c).");
                        return;
                    }
                }
                monthly = financed > 0 ? ScaleLenderMonthly(financing, financed) : 0;
                fundingSummary = financed > 0
                    ? $"cash {down}c + lender loan {financed}c (~{monthly}c/mo)"
                    : $"cash {down}c";
            }

            monthly += EstimateMonthlyTax(price) + Math.Max(0, AssumedMonthlyMaintenanceCents);

            string risk = CheckRisk(decision, option, price, down, cash, reserve, monthly, income,
                $"ownership of '{option.Description}'");
            if (risk != null) { Reject(decision, option, risk); return; }

            viable.Add(new FinancedCandidate
            {
                Option = option,
                TotalPriceCents = price,
                CashDownCents = down,
                FinancedCents = financed,
                MonthlyBurdenCents = monthly,
                Rationale = $"buy '{option.Description}' for {price}c via {fundingSummary}; burden ~{monthly}c/mo against {income}c/mo documented income.",
            });
        }

        private void EvaluateLandBuild(
            NpcPropertyDecision decision,
            HousingSearchOption option,
            int requiredPlaces,
            int cash,
            int income,
            NpcPropertyFinancingAvailability financing,
            IConstructionMaterialPricePort pricePort,
            BuildingDesignCatalog catalog,
            List<FinancedCandidate> viable,
            List<string> diag)
        {
            if (option == null) return;
            if (option.PathKind != HousingSearchPathKind.LandPurchaseAndBuild) return;
            if (option.TotalPriceCents <= 0)
            {
                Reject(decision, option, "no real land price named.");
                return;
            }
            if (catalog == null || pricePort == null)
            {
                Reject(decision, option, "no design catalog or material price information — the build cost cannot be estimated, so land+build is not decidable.");
                return;
            }
            BuildingDesign design = ChooseAffordableDesign(catalog, requiredPlaces, pricePort, option, diag);
            if (design == null)
            {
                Reject(decision, option, $"no catalog design sleeps {requiredPlaces} with priced materials — cannot plan the build.");
                return;
            }
            int buildCost = EstimateBuildCostCents(design, pricePort);
            if (buildCost < 0)
            {
                Reject(decision, option, $"design '{design.DesignId}' has materials the price list does not name — build cost unknowable.");
                return;
            }
            int total = option.TotalPriceCents + buildCost;
            int reserve = Math.Max(0, MinimumCashReserveAfterClosingCents);
            int cashAvailable = Math.Max(0, cash - reserve);

            int down = Math.Min(cashAvailable, total);
            int financed = total - down;
            int monthly;
            string fundingSummary;
            if (financed > 0)
            {
                if (financing == null || !financing.HasLenderOffer || financing.LenderOfferAmountCents < financed)
                {
                    Reject(decision, option, $"land ({option.TotalPriceCents}c) + build (~{buildCost}c) = {total}c needs {financed}c of financing; no confirmed lender offer covers it.");
                    return;
                }
                monthly = ScaleLenderMonthly(financing, financed);
                fundingSummary = $"cash {down}c + lender loan {financed}c (~{monthly}c/mo)";
            }
            else
            {
                monthly = 0;
                fundingSummary = $"cash {down}c";
            }
            monthly += EstimateMonthlyTax(total) + Math.Max(0, AssumedMonthlyMaintenanceCents);

            string risk = CheckRisk(decision, option, total, down, cash, reserve, monthly, income,
                $"land+build '{option.Description}' with design '{design.DesignId}'");
            if (risk != null) { Reject(decision, option, risk); return; }

            viable.Add(new FinancedCandidate
            {
                Option = option,
                IsLandBuild = true,
                DesignId = design.DesignId,
                TotalPriceCents = total,
                CashDownCents = down,
                FinancedCents = financed,
                MonthlyBurdenCents = monthly,
                BuildCostCents = buildCost,
                Rationale = $"buy land '{option.Description}' ({option.TotalPriceCents}c) and build '{design.DesignId}' (~{buildCost}c) via {fundingSummary}; burden ~{monthly}c/mo against {income}c/mo documented income.",
            });
        }

        /// <summary>
        /// The NPC's own risk checks — what they can legitimately verify:
        /// the down payment exists NOW, a reserve survives closing, and the
        /// monthly burden fits DOCUMENTED income. No future income is assumed.
        /// </summary>
        private string CheckRisk(NpcPropertyDecision decision, HousingSearchOption option,
            int totalPrice, int cashDown, int cash, int reserve, int monthlyBurden, int income,
            string what)
        {
            if (cashDown > cash)
                return $"{what}: needs {cashDown}c cash at closing but the household holds {cash}c — the down payment must exist now.";
            if (cash - cashDown < reserve)
                return $"{what}: closing would leave {cash - cashDown}c against a {reserve}c minimum reserve.";
            if (monthlyBurden > 0 && income <= 0)
                return $"{what}: carries ~{monthlyBurden}c/mo but the household has no documented income — debt cannot be serviced.";
            int affordableMonthly = (int)Math.Floor(income * Math.Max(0.0, MaxMonthlyHousingBurdenShare01));
            if (monthlyBurden > affordableMonthly)
                return $"{what}: ~{monthlyBurden}c/mo exceeds the {affordableMonthly}c/mo the household will commit ({income}c documented income).";
            return null;
        }

        private void Reject(NpcPropertyDecision decision, HousingSearchOption option, string reason)
        {
            decision.RejectedAlternatives.Add(new HousingRejectedAlternative
            {
                OptionId = option != null ? option.OptionId : string.Empty,
                PathKind = option != null ? option.PathKind : HousingSearchPathKind.Unspecified,
                ProviderName = option != null ? option.ProviderName : string.Empty,
                Reason = reason,
            });
        }

        private int ScaleLenderMonthly(NpcPropertyFinancingAvailability financing, int financedCents)
        {
            if (financing == null || financing.LenderOfferAmountCents <= 0) return 0;
            int baseMonthly = financing.LenderOfferMonthlyCents > 0
                ? financing.LenderOfferMonthlyCents
                : EstimateLenderMonthlyCents(financing.LenderOfferAmountCents,
                    financing.LenderOfferRateBps, financing.LenderOfferTermDays);
            // Linear scaling of the offered payment to the drawn portion —
            // the NPC's own back-of-envelope, stated as such.
            return (int)Math.Ceiling(baseMonthly * (financedCents / (double)financing.LenderOfferAmountCents));
        }

        /// <summary>
        /// The NPC's back-of-envelope loan math: straight-line principal plus
        /// first-month interest. Documented as an estimate, never presented
        /// as the lender's schedule.
        /// </summary>
        public static int EstimateLenderMonthlyCents(int amountCents, int rateBps, int termDays)
        {
            int months = Math.Max(1, termDays / 30);
            int principalPart = (int)Math.Ceiling(amountCents / (double)months);
            int interestPart = (int)Math.Ceiling(amountCents * (rateBps / 10000.0) / 12.0);
            return principalPart + interestPart;
        }

        private int EstimateMonthlyTax(int priceCents)
        {
            if (AssumedAnnualPropertyTaxRateBps <= 0 || priceCents <= 0) return 0;
            return (int)Math.Ceiling(priceCents * (AssumedAnnualPropertyTaxRateBps / 10000.0) / 12.0);
        }

        /// <summary>
        /// The NPC picks the cheapest adequate catalog design — cheapest
        /// estimated materials cost among designs that sleep the household.
        /// </summary>
        private BuildingDesign ChooseAffordableDesign(BuildingDesignCatalog catalog, int requiredPlaces,
            IConstructionMaterialPricePort pricePort, HousingSearchOption landOption, List<string> diag)
        {
            BuildingDesign best = null;
            int bestCost = int.MaxValue;
            foreach (BuildingDesign design in catalog.AllDesigns())
            {
                if (design == null) continue;
                int capacity = 0;
                foreach (BuildingDesignSpaceSpec spec in design.SpaceSpecs ?? new List<BuildingDesignSpaceSpec>())
                    capacity += Math.Max(0, spec != null ? spec.SleepingCapacity : 0);
                if (capacity < requiredPlaces) continue;
                int cost = EstimateBuildCostCents(design, pricePort);
                if (cost < 0) continue;
                if (cost < bestCost) { bestCost = cost; best = design; }
            }
            return best;
        }

        /// <summary>
        /// Estimates a design's material cost from the supplier price list —
        /// the same prices the player sees. Returns -1 when any material is
        /// unpriced (cost unknowable, never guessed).
        /// </summary>
        public static int EstimateBuildCostCents(BuildingDesign design, IConstructionMaterialPricePort pricePort)
        {
            if (design == null || pricePort == null) return -1;
            int total = 0;
            foreach (BuildingDesignPhase phase in design.Phases ?? new List<BuildingDesignPhase>())
            {
                if (phase == null) continue;
                foreach (ConstructionMaterialRequirement requirement in phase.Materials ?? new List<ConstructionMaterialRequirement>())
                {
                    if (requirement == null || !requirement.IsCoherent) return -1;
                    int unitPrice = pricePort.UnitPriceCents(requirement);
                    if (unitPrice < 0) return -1;
                    total += unitPrice * Math.Max(0, requirement.RequiredUnits);
                }
            }
            return total;
        }

        /// <summary>
        /// Nothing viable to buy and the planner resolved nothing: either the
        /// household is priced out of everything (WALK AWAY — the need is
        /// withdrawn) or the market simply has nothing adequate right now
        /// (CONTINUE SEARCHING — the need stays open).
        /// </summary>
        private NpcPropertyDecision RecordNoPurchase(NpcPropertyDecision decision, HousingSearchDecision plan,
            HousingSearchNeedRegistry needs, int dayIndex, List<string> diag)
        {
            bool anyOption = plan.RejectedAlternatives.Count > 0;
            bool allAffordability = anyOption;
            foreach (HousingRejectedAlternative rejected in plan.RejectedAlternatives)
            {
                string reason = rejected != null ? rejected.Reason ?? string.Empty : string.Empty;
                if (!(reason.StartsWith("unaffordable", StringComparison.Ordinal)
                    || reason.StartsWith("ownership not affordable", StringComparison.Ordinal)
                    || reason.StartsWith("land+build not affordable", StringComparison.Ordinal)))
                {
                    allAffordability = false;
                    break;
                }
            }

            if (anyOption && allAffordability)
            {
                decision.Kind = NpcPropertyDecisionKind.WalkAway;
                decision.Rationale = "every adequate option is unaffordable in cash and no financing path closes the gap — the household walks away rather than commit to terms it cannot carry.";
                decision.NeedWithdrawn = true;
                needs?.MarkWithdrawn(decision.HouseholdId, dayIndex);
                diag.Add($"NpcPropertyDecisionEngine: H{decision.HouseholdId} WALKS AWAY — priced out; need withdrawn. " +
                    $"{decision.RejectedAlternatives.Count} rejection(s) recorded.");
            }
            else
            {
                decision.Kind = NpcPropertyDecisionKind.ContinueSearching;
                decision.Rationale = anyOption
                    ? "no adequate option is viable right now — the need stays open for re-evaluation."
                    : "no options are listed at all — the search continues.";
                diag.Add($"NpcPropertyDecisionEngine: H{decision.HouseholdId} CONTINUES SEARCHING. {decision.Rationale}");
            }
            return decision;
        }
    }
}
