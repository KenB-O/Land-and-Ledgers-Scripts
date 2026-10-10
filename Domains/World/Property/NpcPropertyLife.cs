using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;

namespace LandLedgers.World.Property
{
    /// <summary>Phase F: what the holding is used for. Append-only.</summary>
    public enum NpcPropertyHoldingKind
    {
        Unspecified = 0,
        OwnerOccupied = 1,
        RentedOut = 2,
        Vacant = 3,
    }

    /// <summary>Phase F: a maintenance need on an NPC-held property — recorded, costed, and either funded or left to degrade.</summary>
    [Serializable]
    public sealed class NpcPropertyMaintenanceNeed
    {
        public string NeedId = string.Empty;
        public string ParcelId = string.Empty;
        public string Description = string.Empty;
        public int EstimatedCostCents;
        public int ReportedDayIndex;
        public bool Funded;
        public int FundedDayIndex = -1;
        public int FundedCostCents;

        public NpcPropertyMaintenanceNeed() { }
    }

    /// <summary>
    /// Phase F: an NPC household's property holding — the parcel, the
    /// building on it, the financing still attached, its use, condition,
    /// tax assessments, tenancies and maintenance needs. One record per
    /// owned parcel; the title authority stays the ownership truth, this is
    /// the economic-life record.
    /// </summary>
    [Serializable]
    public sealed class NpcPropertyHolding
    {
        public string HoldingId = string.Empty;
        public string ParcelId = string.Empty;
        public string BuildingId = string.Empty;
        public int OwnerHouseholdId = -1;
        public int AcquiredDayIndex;
        public int AcquisitionPriceCents;
        /// <summary>Outstanding seller-finance note obligation (empty when none).</summary>
        public string SellerNoteObligationId = string.Empty;
        public string LenderObligationId = string.Empty;
        public NpcPropertyHoldingKind Kind = NpcPropertyHoldingKind.Unspecified;
        /// <summary>NPC-side condition record (0..1). The Building mirror update is Unity-side.</summary>
        public float Condition01 = 1f;
        public List<NpcPropertyMaintenanceNeed> MaintenanceNeeds = new List<NpcPropertyMaintenanceNeed>();
        public List<string> TenancyIds = new List<string>();
        public List<string> TaxAssessmentIds = new List<string>();
        public List<string> Decisions = new List<string>();

        public NpcPropertyHolding() { }
    }

    /// <summary>Phase F: one tenancy on an NPC-held property.</summary>
    [Serializable]
    public sealed class NpcPropertyTenancy
    {
        public string TenancyId = string.Empty;
        public string HoldingId = string.Empty;
        public string SpaceId = string.Empty;
        public int TenantHouseholdId = -1;
        public int MonthlyRentCents;
        public int StartDayIndex;
        public int EndDayIndex = -1;
        public List<string> RentObligationIds = new List<string>();

        public NpcPropertyTenancy() { }
    }

    /// <summary>Phase F: the outcome of an NPC property sale.</summary>
    [Serializable]
    public sealed class NpcPropertySaleResult
    {
        public bool Sold;
        public string FailureReason = string.Empty;
        public string HoldingId = string.Empty;
        public string ParcelId = string.Empty;
        public int SalePriceCents;
        public int SellerNoteSettledCents;
        public int SellerNetProceedsCents;
        public bool NoteAssumedByBuyer;
        public string AssumptionId = string.Empty;
        public string NewHolder = string.Empty;
        public List<string> ConservationNotes = new List<string>();

        public NpcPropertySaleResult() { }
    }

    /// <summary>
    /// Phase F (F4): post-acquisition property life. The acquired or built
    /// property enters the NPC's real economic life:
    /// <list type="bullet">
    /// <item>Property tax accrues through the real <see cref="PropertyTaxService"/>
    /// (levy → pay from real cash → delinquency → senior tax lien → tax sale —
    /// the real enforcement path; the lien is filed in Phase E's credit
    /// registry).</item>
    /// <item>Maintenance needs arise, are funded from real cash, or degrade
    /// the holding when neglected.</item>
    /// <item>The NPC may rent the property out: real tenancy, real rent
    /// obligations, real collection; arrears stay as obligations routable to
    /// Phase E's workout.</item>
    /// <item>The NPC may sell: an outstanding seller-finance note is settled
    /// from the proceeds or assumed by the buyer through the authority's
    /// real assumption machinery — never magically restored to the original
    /// seller.</item>
    /// <item>Default routes into the real workout/enforcement machinery.</item>
    /// </list>
    /// </summary>
    public sealed class NpcPropertyLifeService
    {
        /// <summary>TUNING: unfunded maintenance needs older than this many days degrade the holding.</summary>
        public int NeglectDaysBeforeDegrade { get; set; } = 90;
        /// <summary>TUNING: condition lost per neglected need per degradation pass.</summary>
        public float DegradePerNeed01 { get; set; } = 0.05f;

        private readonly List<string> diagnostics = new List<string>();
        private readonly Dictionary<string, NpcPropertyHolding> holdings =
            new Dictionary<string, NpcPropertyHolding>(StringComparer.Ordinal);
        private readonly Dictionary<string, NpcPropertyTenancy> tenancies =
            new Dictionary<string, NpcPropertyTenancy>(StringComparer.Ordinal);
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Registers the holding after an F2 closing or F3 completion.</summary>
        public NpcPropertyHolding RegisterHolding(
            string parcelId, string buildingId, int ownerHouseholdId,
            int acquiredDayIndex, int acquisitionPriceCents,
            string sellerNoteObligationId, string lenderObligationId,
            NpcPropertyHoldingKind kind, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(parcelId) || ownerHouseholdId < 0)
            {
                diag.Add("NpcPropertyLife.RegisterHolding: a parcel and a real owner household are required.");
                return null;
            }
            var holding = new NpcPropertyHolding
            {
                HoldingId = $"nph-{sequence++}",
                ParcelId = parcelId,
                BuildingId = buildingId ?? string.Empty,
                OwnerHouseholdId = ownerHouseholdId,
                AcquiredDayIndex = acquiredDayIndex,
                AcquisitionPriceCents = Math.Max(0, acquisitionPriceCents),
                SellerNoteObligationId = sellerNoteObligationId ?? string.Empty,
                LenderObligationId = lenderObligationId ?? string.Empty,
                Kind = kind,
            };
            holding.Decisions.Add($"day {acquiredDayIndex}: acquired '{parcelId}' for {holding.AcquisitionPriceCents}c.");
            holdings[holding.HoldingId] = holding;
            diag.Add($"NpcPropertyLife: holding '{holding.HoldingId}' — H{ownerHouseholdId} owns '{parcelId}' ({kind}).");
            return holding;
        }

        public NpcPropertyHolding FindHolding(string holdingId)
        {
            if (string.IsNullOrWhiteSpace(holdingId)) return null;
            holdings.TryGetValue(holdingId, out NpcPropertyHolding holding);
            return holding;
        }

        #region Property tax

        /// <summary>Levies the annual property tax on the holding's parcel — the real assessment.</summary>
        public TaxAssessment LevyPropertyTax(
            PropertyTaxService taxService, EntityIdRegistry ids, NpcPropertyHolding holding,
            int taxYear, int amountCents, int dueDayIndex,
            int penaltyBpsMonthly, int interestBpsAnnual, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (taxService == null || holding == null)
            {
                diag.Add("NpcPropertyLife.LevyPropertyTax: tax service and holding required.");
                return null;
            }
            TaxAssessment assessment = taxService.LevyAssessment(ids, holding.ParcelId,
                "household:" + holding.OwnerHouseholdId, taxYear, amountCents, dueDayIndex,
                penaltyBpsMonthly, interestBpsAnnual, diag);
            if (assessment != null)
            {
                holding.TaxAssessmentIds.Add(assessment.AssessmentId);
                holding.Decisions.Add($"day {dayIndex}: {taxYear} property tax levied — {amountCents}c due day {dueDayIndex} (assessment '{assessment.AssessmentId}').");
            }
            return assessment;
        }

        /// <summary>
        /// Pays the tax from the owner's REAL cash: the cash moves to the
        /// county collector, then the assessment records the payment. Both
        /// legs or neither.
        /// </summary>
        public string PayPropertyTax(
            PropertyTaxService taxService, CreditCashBridge cash, NpcPropertyHolding holding,
            TaxAssessment assessment, int amountCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (taxService == null || cash == null || holding == null || assessment == null)
                return "NpcPropertyLife.PayPropertyTax: tax service, cash bridge, holding and assessment required.";
            string payer = "household:" + holding.OwnerHouseholdId;
            string moveProblem = MoveCash(cash, payer, "County Tax Collector", amountCents, dayIndex,
                $"property tax {assessment.TaxYear} on '{holding.ParcelId}'", diag);
            if (moveProblem != null) return "NpcPropertyLife.PayPropertyTax: " + moveProblem;
            string payProblem = taxService.PayTax(assessment, amountCents, dayIndex, diag);
            if (payProblem != null)
            {
                // The assessment refused the payment — return the cash loudly rather than leaving it moved.
                string backProblem = MoveCash(cash, "County Tax Collector", payer, amountCents, dayIndex,
                    $"property tax payment returned — assessment refused: {payProblem}", diag);
                return $"NpcPropertyLife.PayPropertyTax: assessment refused ({payProblem}); cash return: {(backProblem ?? "clean")}.";
            }
            holding.Decisions.Add($"day {dayIndex}: paid {amountCents}c property tax on '{holding.ParcelId}' from real cash.");
            return null;
        }

        /// <summary>
        /// Advances the tax clock and files the senior tax lien when an
        /// assessment is delinquent — the REAL tax enforcement path (lien in
        /// Phase E's credit registry; the tax sale itself is the collector's
        /// auction, run through <see cref="PropertyTaxService"/>).
        /// </summary>
        public string ProcessTaxDelinquency(
            PropertyTaxService taxService, CreditRegistry registry, EntityIdRegistry ids,
            NpcPropertyHolding holding, TaxAssessment assessment, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (taxService == null || holding == null || assessment == null)
                return "NpcPropertyLife.ProcessTaxDelinquency: tax service, holding and assessment required.";
            taxService.AdvanceDay(dayIndex, diag);
            if (assessment.Status == TaxAssessmentStatus.Delinquent)
            {
                string lienProblem = taxService.FileTaxLien(assessment, ids, registry, dayIndex, diag);
                if (lienProblem != null) return "NpcPropertyLife.ProcessTaxDelinquency: " + lienProblem;
                holding.Decisions.Add($"day {dayIndex}: {assessment.TaxYear} tax delinquent — SENIOR tax lien filed on '{holding.ParcelId}'.");
                diag.Add($"NpcPropertyLife: H{holding.OwnerHouseholdId} tax delinquent on '{holding.ParcelId}' — senior lien filed (real enforcement path).");
            }
            return null;
        }

        #endregion

        #region Maintenance

        public NpcPropertyMaintenanceNeed ReportMaintenanceNeed(
            NpcPropertyHolding holding, string description, int estimatedCostCents,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (holding == null || string.IsNullOrWhiteSpace(description) || estimatedCostCents <= 0)
            {
                diag.Add("NpcPropertyLife.ReportMaintenanceNeed: holding, description and a positive estimate required.");
                return null;
            }
            var need = new NpcPropertyMaintenanceNeed
            {
                NeedId = $"npm-{sequence++}",
                ParcelId = holding.ParcelId,
                Description = description,
                EstimatedCostCents = estimatedCostCents,
                ReportedDayIndex = dayIndex,
            };
            holding.MaintenanceNeeds.Add(need);
            holding.Decisions.Add($"day {dayIndex}: maintenance need '{need.NeedId}' — {description} (~{estimatedCostCents}c).");
            diag.Add($"NpcPropertyLife: maintenance need '{need.NeedId}' on '{holding.ParcelId}': {description} (~{estimatedCostCents}c).");
            return need;
        }

        /// <summary>Funds a maintenance need from the owner's real cash; condition recovers toward 1.</summary>
        public string FundMaintenance(
            CreditCashBridge cash, NpcPropertyHolding holding, string needId,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (cash == null || holding == null)
                return "NpcPropertyLife.FundMaintenance: cash bridge and holding required.";
            NpcPropertyMaintenanceNeed need = null;
            foreach (NpcPropertyMaintenanceNeed candidate in holding.MaintenanceNeeds)
                if (candidate != null && string.Equals(candidate.NeedId, needId, StringComparison.Ordinal))
                    need = candidate;
            if (need == null) return $"NpcPropertyLife.FundMaintenance: unknown need '{needId}'.";
            if (need.Funded) return $"NpcPropertyLife.FundMaintenance: need '{needId}' already funded.";
            string payer = "household:" + holding.OwnerHouseholdId;
            string moveProblem = MoveCash(cash, payer, "maintenance:" + holding.ParcelId,
                need.EstimatedCostCents, dayIndex, $"maintenance: {need.Description}", diag);
            if (moveProblem != null) return "NpcPropertyLife.FundMaintenance: " + moveProblem;
            need.Funded = true;
            need.FundedDayIndex = dayIndex;
            need.FundedCostCents = need.EstimatedCostCents;
            holding.Condition01 = Math.Min(1f, holding.Condition01 + 0.10f);
            holding.Decisions.Add($"day {dayIndex}: funded maintenance '{need.NeedId}' ({need.FundedCostCents}c) — condition now {holding.Condition01:0.00}.");
            diag.Add($"NpcPropertyLife: maintenance '{need.NeedId}' funded ({need.FundedCostCents}c) on '{holding.ParcelId}'.");
            return null;
        }

        /// <summary>
        /// Neglect degrades the holding: unfunded needs older than the
        /// neglect threshold reduce condition. Observable, never silent.
        /// </summary>
        public void DegradeForNeglect(NpcPropertyHolding holding, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (holding == null) return;
            int neglected = 0;
            foreach (NpcPropertyMaintenanceNeed need in holding.MaintenanceNeeds)
            {
                if (need == null || need.Funded) continue;
                if (dayIndex - need.ReportedDayIndex >= Math.Max(1, NeglectDaysBeforeDegrade))
                    neglected++;
            }
            if (neglected > 0)
            {
                holding.Condition01 = Math.Max(0f, holding.Condition01 - neglected * Math.Max(0f, DegradePerNeed01));
                holding.Decisions.Add($"day {dayIndex}: {neglected} neglected maintenance need(s) — condition now {holding.Condition01:0.00}.");
                diag.Add($"NpcPropertyLife: '{holding.ParcelId}' degraded to {holding.Condition01:0.00} — {neglected} neglected need(s).");
            }
        }

        #endregion

        #region Renting out

        /// <summary>
        /// The owner rents out a space: real tenancy, tenant occupancy as
        /// Rental. Rent itself is billed/collected per period through real
        /// obligations below.
        /// </summary>
        public NpcPropertyTenancy EstablishTenancy(
            HousingAuthority housing, NpcPropertyHolding holding, string spaceId,
            int tenantHouseholdId, List<int> tenantPersonIds, int monthlyRentCents,
            int startDayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (housing == null || holding == null)
            {
                diag.Add("NpcPropertyLife.EstablishTenancy: housing authority and holding required.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(spaceId) || tenantHouseholdId < 0 || monthlyRentCents <= 0)
            {
                diag.Add("NpcPropertyLife.EstablishTenancy: a real space, a tenant household and a positive rent are required.");
                return null;
            }
            if (housing.FindSpace(spaceId) == null)
            {
                diag.Add($"NpcPropertyLife.EstablishTenancy: space '{spaceId}' is unknown to the housing authority — refusing rather than inventing it.");
                return null;
            }
            var tenancy = new NpcPropertyTenancy
            {
                TenancyId = $"npt-{sequence++}",
                HoldingId = holding.HoldingId,
                SpaceId = spaceId,
                TenantHouseholdId = tenantHouseholdId,
                MonthlyRentCents = monthlyRentCents,
                StartDayIndex = startDayIndex,
            };
            if (tenantPersonIds != null)
            {
                foreach (int personId in tenantPersonIds)
                {
                    if (personId < 0) continue;
                    housing.Occupy(personId, tenantHouseholdId, spaceId,
                        AccommodationArrangement.Rental, startDayIndex, diag);
                }
            }
            housing.RecordAgreement("rental", "household:" + holding.OwnerHouseholdId,
                "household:" + tenantHouseholdId, spaceId,
                $"rental of '{spaceId}' at {monthlyRentCents}c/month", startDayIndex, -1, string.Empty, diag);
            tenancies[tenancy.TenancyId] = tenancy;
            holding.TenancyIds.Add(tenancy.TenancyId);
            holding.Kind = NpcPropertyHoldingKind.RentedOut;
            holding.Decisions.Add($"day {startDayIndex}: rented '{spaceId}' to H{tenantHouseholdId} at {monthlyRentCents}c/month (tenancy '{tenancy.TenancyId}').");
            diag.Add($"NpcPropertyLife: H{holding.OwnerHouseholdId} rents '{spaceId}' to H{tenantHouseholdId} at {monthlyRentCents}c/month.");
            return tenancy;
        }

        /// <summary>Bills one rent period: a REAL payable obligation, tenant → landlord.</summary>
        public FinancialObligation BillRent(
            EntityIdRegistry ids, FinancialObligationAuthority authority,
            NpcPropertyHolding holding, string tenancyId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (authority == null || holding == null)
            {
                diag.Add("NpcPropertyLife.BillRent: obligation authority and holding required.");
                return null;
            }
            if (!tenancies.TryGetValue(tenancyId ?? string.Empty, out NpcPropertyTenancy tenancy) || tenancy == null)
            {
                diag.Add($"NpcPropertyLife.BillRent: unknown tenancy '{tenancyId}'.");
                return null;
            }
            FinancialObligation obligation = authority.Create(ids, FinancialObligationKind.Payable,
                "household:" + tenancy.TenantHouseholdId, "household:" + holding.OwnerHouseholdId,
                tenancy.MonthlyRentCents, dayIndex, "rent — tenancy " + tenancy.TenancyId,
                $"rent for '{tenancy.SpaceId}'", agreementId: tenancy.TenancyId);
            if (obligation == null)
            {
                diag.Add($"NpcPropertyLife.BillRent: the obligation authority refused the rent bill for tenancy '{tenancyId}'.");
                return null;
            }
            tenancy.RentObligationIds.Add(obligation.ObligationId);
            diag.Add($"NpcPropertyLife: billed {tenancy.MonthlyRentCents}c rent to H{tenancy.TenantHouseholdId} (obligation '{obligation.ObligationId}').");
            return obligation;
        }

        /// <summary>
        /// Collects rent from the tenant's REAL cash through the bridge. A
        /// short tenant pays what they hold; the remainder stays open on the
        /// obligation as arrears — real, and routable to Phase E's workout.
        /// Returns the collected amount, or -1 with a diagnostic on refusal.
        /// </summary>
        public int CollectRent(
            CreditCashBridge cash, FinancialObligationAuthority authority,
            CreditOfferWorkflow workflow, NpcPropertyHolding holding, string tenancyId,
            string obligationId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (cash == null || authority == null || workflow == null || holding == null)
            {
                diag.Add("NpcPropertyLife.CollectRent: cash bridge, obligation authority, workflow and holding required.");
                return -1;
            }
            if (!tenancies.TryGetValue(tenancyId ?? string.Empty, out NpcPropertyTenancy tenancy) || tenancy == null)
            {
                diag.Add($"NpcPropertyLife.CollectRent: unknown tenancy '{tenancyId}'.");
                return -1;
            }
            FinancialObligation obligation = authority.Find(obligationId);
            if (obligation == null || obligation.Settled)
            {
                diag.Add($"NpcPropertyLife.CollectRent: obligation '{obligationId}' is unknown or already settled.");
                return -1;
            }
            int owed = obligation.TotalOutstandingCents;
            if (owed <= 0) return 0;
            string tenant = "household:" + tenancy.TenantHouseholdId;
            string landlord = "household:" + holding.OwnerHouseholdId;
            IRealCashStore tenantStore = cash.FindStore(tenant);
            if (tenantStore == null)
            {
                diag.Add($"NpcPropertyLife.CollectRent: '{tenant}' has no real cash store — nothing collected, arrears stand.");
                return -1;
            }
            int collectible = Math.Min(owed, tenantStore.ReadBalanceCents());
            if (collectible <= 0)
            {
                authority.MarkDelinquent(obligationId);
                diag.Add($"NpcPropertyLife: H{tenancy.TenantHouseholdId} holds no cash — {owed}c rent stands as arrears (obligation '{obligationId}' marked delinquent).");
                return 0;
            }
            CreditCashAccount tenantCash = cash.OpenWindow(tenant, diag);
            CreditCashAccount landlordCash = cash.OpenWindow(landlord, diag);
            if (tenantCash == null || landlordCash == null)
            {
                cash.DiscardWindow(tenantCash); cash.DiscardWindow(landlordCash);
                diag.Add("NpcPropertyLife.CollectRent: no cash window — nothing collected.");
                return -1;
            }
            FinancialPaymentRecord payment = workflow.CollectPayment(obligationId, collectible, dayIndex,
                authority, tenantCash, landlordCash, out string message);
            if (payment == null)
            {
                cash.DiscardWindow(tenantCash); cash.DiscardWindow(landlordCash);
                diag.Add("NpcPropertyLife.CollectRent: payment refused: " + message);
                return -1;
            }
            string p1 = cash.CommitWindow(tenantCash, dayIndex, $"rent to '{landlord}'", landlord, diag);
            string p2 = cash.CommitWindow(landlordCash, dayIndex, $"rent from '{tenant}'", tenant, diag);
            if (p1 != null || p2 != null)
            {
                diag.Add("NpcPropertyLife.CollectRent: INCONSISTENCY — payment applied but cash commit refused: " + (p1 ?? p2));
                return -1;
            }
            int stillOwed = authority.Find(obligationId)?.TotalOutstandingCents ?? 0;
            if (stillOwed > 0)
            {
                authority.MarkDelinquent(obligationId);
                diag.Add($"NpcPropertyLife: collected {payment.AmountCents}c of {owed}c rent from H{tenancy.TenantHouseholdId}; {stillOwed}c stands as arrears (delinquent, workout-routable).");
            }
            else
            {
                diag.Add($"NpcPropertyLife: collected {payment.AmountCents}c rent from H{tenancy.TenantHouseholdId} in full.");
            }
            return payment.AmountCents;
        }

        #endregion

        #region Sale

        /// <summary>
        /// The NPC sells the holding. An outstanding seller-finance note is
        /// handled per Phase E — never magically restored to the original
        /// seller:
        /// <list type="bullet">
        /// <item>When the price covers the note, the proceeds settle it in
        /// full through the real payment machinery; the seller's security
        /// interest is released; the seller keeps the net.</item>
        /// <item>When the price does not cover the note, the buyer must
        /// assume it via the authority's real assumption machinery (with the
        /// creditor's consent); otherwise the sale is refused loudly.</item>
        /// </list>
        /// Title transfers to the buyer on a real conveyance; the holding
        /// record follows the parcel to its new owner.
        /// </summary>
        public NpcPropertySaleResult SellProperty(
            EntityIdRegistry ids,
            FinancialObligationAuthority authority,
            CreditCashBridge cash,
            TitleAuthority titles,
            HousingAuthority housing,
            CreditOfferWorkflow workflow,
            NpcPropertyHolding holding,
            int buyerHouseholdId,
            int salePriceCents,
            bool buyerAssumesNote,
            bool creditorConsentsToAssumption,
            int dayIndex,
            CreditEventLog events,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new NpcPropertySaleResult
            {
                HoldingId = holding != null ? holding.HoldingId : string.Empty,
                ParcelId = holding != null ? holding.ParcelId : string.Empty,
                SalePriceCents = salePriceCents,
            };
            if (holding == null || authority == null || cash == null || titles == null)
                return FailSale(result, "holding, obligation authority, cash bridge and title authority required.", diag);
            if (buyerHouseholdId < 0 || buyerHouseholdId == holding.OwnerHouseholdId)
                return FailSale(result, "the buyer must be a real, different household.", diag);
            if (salePriceCents <= 0)
                return FailSale(result, "no real sale price — not a sale.", diag);
            string sellerName = "household:" + holding.OwnerHouseholdId;
            string buyerName = "household:" + buyerHouseholdId;
            string holder = titles.CurrentHolder(holding.ParcelId);
            if (!string.Equals(holder, sellerName, StringComparison.Ordinal))
                return FailSale(result, $"parcel '{holding.ParcelId}' is held by '{holder}', not the seller '{sellerName}' — cannot sell what they do not hold.", diag);

            // The buyer's cash is real and sufficient — checked before anything moves.
            IRealCashStore buyerStore = cash.FindStore(buyerName);
            IRealCashStore sellerStore = cash.FindStore(sellerName);
            if (buyerStore == null || sellerStore == null)
                return FailSale(result, "buyer or seller has no registered real cash store.", diag);
            if (buyerStore.ReadBalanceCents() < salePriceCents)
                return FailSale(result, $"the buyer holds {buyerStore.ReadBalanceCents()}c against a {salePriceCents}c price — the sale cannot close.", diag);

            // Settle or assume the outstanding seller note FIRST — the claim
            // is resolved before title moves, never by restoring ownership.
            // Cash flow: the buyer pays the full price to the property
            // seller; the property seller (the note's debtor) settles the
            // note to the original seller (the creditor) from the proceeds.
            FinancialObligation note = !string.IsNullOrWhiteSpace(holding.SellerNoteObligationId)
                ? authority.Find(holding.SellerNoteObligationId) : null;
            int noteOwed = note != null && !note.Settled ? note.TotalOutstandingCents : 0;
            if (noteOwed > 0)
            {
                if (salePriceCents >= noteOwed)
                {
                    // (a) buyer → property seller: the full price.
                    string priceProblem = MoveCash(cash, buyerName, sellerName, salePriceCents, dayIndex,
                        $"purchase of '{holding.ParcelId}' from '{sellerName}'", diag);
                    if (priceProblem != null)
                        return FailSale(result, "the purchase cash failed to move: " + priceProblem, diag);
                    // (b) property seller → original creditor: the note, settled from the proceeds.
                    string settleProblem = SettleNoteFromProceeds(workflow, authority, cash,
                        note, sellerName, note.Creditor, noteOwed, dayIndex, diag);
                    if (settleProblem != null)
                    {
                        string backProblem = MoveCash(cash, sellerName, buyerName, salePriceCents, dayIndex,
                            $"sale of '{holding.ParcelId}' unwound — note settlement failed", diag);
                        return FailSale(result, "the seller note could not be settled from the proceeds: " + settleProblem +
                            $"; purchase cash returned ({backProblem ?? "clean"}).", diag);
                    }
                    result.SellerNoteSettledCents = noteOwed;
                    foreach (string securityId in note.SecurityInterestIds)
                        authority.ReleaseSecurity(securityId, $"note settled on sale of '{holding.ParcelId}'");
                    holding.SellerNoteObligationId = string.Empty;
                    diag.Add($"NpcPropertyLife: seller note '{note.ObligationId}' settled in full ({noteOwed}c) from the sale proceeds; security released. The original seller keeps a CASH claim satisfied — never the property.");
                }
                else
                {
                    if (!buyerAssumesNote)
                        return FailSale(result, $"the {noteOwed}c seller note exceeds the {salePriceCents}c price and the buyer will not assume it — the sale cannot clear the claim.", diag);
                    if (!creditorConsentsToAssumption)
                        return FailSale(result, "the buyer would assume the note but the creditor (original seller) does not consent — no assumption without consent.", diag);
                    ObligationAssumptionRecord assumption = authority.RecordAssumption(ids, note.ObligationId,
                        buyerName, releaseOriginal: true, novation: true, creditorConsented: true, dayIndex: dayIndex);
                    if (assumption == null)
                        return FailSale(result, "the obligation authority refused the assumption.", diag);
                    result.NoteAssumedByBuyer = true;
                    result.AssumptionId = assumption.AssumptionId;
                    holding.SellerNoteObligationId = string.Empty;
                    diag.Add($"NpcPropertyLife: buyer '{buyerName}' assumed the {noteOwed}c seller note (assumption '{assumption.AssumptionId}', original debtor released, creditor consented). The claim moves with the debt — ownership is never magically restored.");
                }
            }

            // Cash: buyer → seller, in full (unless already moved in the settlement path above).
            if (result.SellerNoteSettledCents == 0)
            {
                string cashProblem = MoveCash(cash, buyerName, sellerName, salePriceCents, dayIndex,
                    $"purchase of '{holding.ParcelId}' from '{sellerName}'", diag);
                if (cashProblem != null)
                    return FailSale(result, "the purchase cash failed to move: " + cashProblem, diag);
            }
            result.SellerNetProceedsCents = salePriceCents - result.SellerNoteSettledCents;

            // Title to the buyer on a real conveyance.
            string conveyanceId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N");
            string titleProblem = titles.TransferTitle(ids, holding.ParcelId, buyerName,
                TitleBasis.Purchase, conveyanceId, dayIndex,
                $"resale by '{sellerName}' for {salePriceCents}c", diag);
            if (titleProblem != null)
            {
                // Cash moved but title refused: reverse the purchase cash as
                // far as the seller's holdings allow; any shortfall becomes a
                // REAL payable seller → buyer (nothing vanishes). The note
                // settlement above was a real payment of a real debt and
                // stands as history.
                int sellerHolds = sellerStore.ReadBalanceCents();
                int returnable = Math.Min(salePriceCents, sellerHolds);
                string backProblem = returnable > 0
                    ? MoveCash(cash, sellerName, buyerName, returnable, dayIndex,
                        $"sale of '{holding.ParcelId}' unwound — title refused", diag)
                    : "the seller holds no cash to return.";
                int shortfall = salePriceCents - returnable;
                string shortfallNote = string.Empty;
                if (shortfall > 0 && ids != null)
                {
                    FinancialObligation shortfallOb = authority.Create(ids, FinancialObligationKind.Payable,
                        sellerName, buyerName, shortfall, dayIndex,
                        $"unwound resale of '{holding.ParcelId}' — purchase cash shortfall after title refusal",
                        "resale unwind shortfall");
                    shortfallNote = $" The {shortfall}c shortfall is booked as real payable " +
                        $"'{(shortfallOb != null ? shortfallOb.ObligationId : "FAILED")}'.";
                }
                return FailSale(result, $"title transfer refused ({titleProblem}); purchase cash returned {returnable}c of {salePriceCents}c ({backProblem ?? "clean"})." +
                    shortfallNote +
                    (result.SellerNoteSettledCents > 0 ? $" The {result.SellerNoteSettledCents}c note settlement stands as real history." : ""), diag);
            }
            result.NewHolder = titles.CurrentHolder(holding.ParcelId);

            housing?.RecordAgreement("resale", sellerName, buyerName, holding.ParcelId,
                $"resale of '{holding.ParcelId}' for {salePriceCents}c", dayIndex, -1, conveyanceId, diag);

            // The holding follows the parcel to its new owner.
            holding.OwnerHouseholdId = buyerHouseholdId;
            holding.AcquiredDayIndex = dayIndex;
            holding.AcquisitionPriceCents = salePriceCents;
            holding.Kind = NpcPropertyHoldingKind.Vacant;
            holding.TenancyIds.Clear();
            holding.Decisions.Add($"day {dayIndex}: sold to H{buyerHouseholdId} for {salePriceCents}c" +
                (result.NoteAssumedByBuyer ? " (buyer assumed the seller note)" : $" (note settled: {result.SellerNoteSettledCents}c)") + ".");

            result.Sold = true;
            result.ConservationNotes.Add($"buyer paid {salePriceCents}c cash; seller net {result.SellerNetProceedsCents}c after note settlement {result.SellerNoteSettledCents}c.");
            diag.Add($"NpcPropertyLife: SOLD '{holding.ParcelId}' H{holding.OwnerHouseholdId}→H{buyerHouseholdId} for {salePriceCents}c. Title → buyer.");
            events?.Record(dayIndex, CreditEventKind.PropertyDecision, buyerName, sellerName, salePriceCents,
                $"property resale: '{holding.ParcelId}' sold to '{buyerName}' for {salePriceCents}c" +
                (result.NoteAssumedByBuyer ? " (buyer assumed the outstanding seller note)" : $" (seller note settled: {result.SellerNoteSettledCents}c)"),
                note?.ObligationId ?? string.Empty, conveyanceId);
            return result;
        }

        private NpcPropertySaleResult FailSale(NpcPropertySaleResult result, string reason, List<string> diag)
        {
            result.Sold = false;
            result.FailureReason = reason;
            diag.Add("NpcPropertyLife.SellProperty: SALE FAILED — " + reason);
            return result;
        }

        /// <summary>
        /// Settles the note from the sale proceeds: the property seller
        /// (the note's debtor) pays the original seller (the creditor) the
        /// full outstanding amount through the real payment machinery.
        /// </summary>
        private string SettleNoteFromProceeds(
            CreditOfferWorkflow workflow, FinancialObligationAuthority authority,
            CreditCashBridge cash, FinancialObligation note, string debtorName, string creditorName,
            int amountCents, int dayIndex, List<string> diag)
        {
            CreditCashAccount buyerCash = cash.OpenWindow(debtorName, diag);
            CreditCashAccount creditorCash = cash.OpenWindow(creditorName, diag);
            if (buyerCash == null || creditorCash == null)
            {
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(creditorCash);
                return "no cash window for debtor or creditor.";
            }
            if (buyerCash.BalanceCents < amountCents)
            {
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(creditorCash);
                return $"the debtor holds {buyerCash.BalanceCents}c but the note needs {amountCents}c.";
            }
            FinancialPaymentRecord payment = workflow.CollectPayment(note.ObligationId, amountCents, dayIndex,
                authority, buyerCash, creditorCash, out string message);
            if (payment == null)
            {
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(creditorCash);
                return "the settlement payment was refused: " + message;
            }
            string p1 = cash.CommitWindow(buyerCash, dayIndex, $"seller-note settlement on resale", creditorName, diag);
            string p2 = cash.CommitWindow(creditorCash, dayIndex, $"seller-note settlement received on resale", debtorName, diag);
            if (p1 != null || p2 != null) return "the settlement cash commit was refused: " + (p1 ?? p2);
            return null;
        }

        #endregion

        #region Default → real enforcement

        /// <summary>
        /// Routes a defaulted property obligation into the REAL workout and
        /// enforcement machinery (Phase E): the creditor's workout response,
        /// up to foreclosure through <see cref="ForeclosureService"/>. The
        /// service marks delinquency; the workout decides.
        /// </summary>
        public void ProcessPropertyDefault(
            EntityIdRegistry ids, FinancialObligationAuthority authority, CreditRegistry registry,
            CreditCashBridge cash, CreditWorkoutService workout, ForeclosureService foreclosure,
            TitleAuthority titles, IReadOnlyDictionary<string, CreditParticipantService> creditors,
            NpcPropertyHolding holding, string obligationId, int dayIndex,
            CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (authority == null || holding == null || string.IsNullOrWhiteSpace(obligationId))
            {
                diag.Add("NpcPropertyLife.ProcessPropertyDefault: authority, holding and obligation required.");
                return;
            }
            FinancialObligation obligation = authority.Find(obligationId);
            if (obligation == null || obligation.Settled)
            {
                diag.Add($"NpcPropertyLife.ProcessPropertyDefault: obligation '{obligationId}' unknown or settled — nothing to enforce.");
                return;
            }
            authority.MarkDelinquent(obligationId);
            holding.Decisions.Add($"day {dayIndex}: obligation '{obligationId}' delinquent — routed to the real workout/enforcement machinery.");
            diag.Add($"NpcPropertyLife: H{holding.OwnerHouseholdId} defaulted on '{obligationId}' — entering the real workout path.");
            workout?.ProcessNamedObligations(ids, authority, registry, cash, foreclosure, titles,
                creditors, new[] { obligationId }, dayIndex, events, diag);
        }

        #endregion

        private static string MoveCash(CreditCashBridge cash, string payer, string payee, int amountCents,
            int dayIndex, string purpose, List<string> diag)
        {
            if (amountCents <= 0) return null;
            CreditCashAccount payerCash = cash.OpenWindow(payer, diag);
            CreditCashAccount payeeCash = cash.OpenWindow(payee, diag);
            if (payerCash == null || payeeCash == null)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"no real cash store for '{payer}' or '{payee}'.";
            }
            if (payerCash.BalanceCents < amountCents)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"'{payer}' holds {payerCash.BalanceCents}c, needs {amountCents}c.";
            }
            payerCash.BalanceCents -= amountCents;
            payeeCash.BalanceCents += amountCents;
            string p1 = cash.CommitWindow(payerCash, dayIndex, purpose, payee, diag);
            string p2 = cash.CommitWindow(payeeCash, dayIndex, purpose, payer, diag);
            return p1 ?? p2;
        }

        #region Save / Load

        [Serializable]
        public sealed class NpcPropertyLifeSaveDto
        {
            public List<NpcPropertyHolding> Holdings = new List<NpcPropertyHolding>();
            public List<NpcPropertyTenancy> Tenancies = new List<NpcPropertyTenancy>();
        }

        public NpcPropertyLifeSaveDto CaptureSaveDto()
        {
            var dto = new NpcPropertyLifeSaveDto();
            foreach (NpcPropertyHolding holding in holdings.Values)
                if (holding != null) dto.Holdings.Add(holding);
            foreach (NpcPropertyTenancy tenancy in tenancies.Values)
                if (tenancy != null) dto.Tenancies.Add(tenancy);
            return dto;
        }

        public void LoadFromSaveDto(NpcPropertyLifeSaveDto dto, List<string> diag)
        {
            diag = diag ?? diagnostics;
            holdings.Clear();
            tenancies.Clear();
            if (dto == null) return;
            if (dto.Holdings != null)
            {
                foreach (NpcPropertyHolding holding in dto.Holdings)
                {
                    if (holding == null || string.IsNullOrWhiteSpace(holding.HoldingId)) continue;
                    if (holdings.ContainsKey(holding.HoldingId))
                    {
                        diag.Add($"LoadFromSaveDto: duplicate holding '{holding.HoldingId}' skipped — no duplicated holdings.");
                        continue;
                    }
                    holdings[holding.HoldingId] = holding;
                }
            }
            if (dto.Tenancies != null)
            {
                foreach (NpcPropertyTenancy tenancy in dto.Tenancies)
                {
                    if (tenancy == null || string.IsNullOrWhiteSpace(tenancy.TenancyId)) continue;
                    if (tenancies.ContainsKey(tenancy.TenancyId))
                    {
                        diag.Add($"LoadFromSaveDto: duplicate tenancy '{tenancy.TenancyId}' skipped.");
                        continue;
                    }
                    tenancies[tenancy.TenancyId] = tenancy;
                }
            }
        }

        #endregion
    }
}
