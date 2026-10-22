using System;
using System.Collections.Generic;
using LandLedgers.Civic;
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

    /// <summary>Pass IX WS6: aggregated outcome of one production drive over NPC holdings.</summary>
    [Serializable]
    public sealed class NpcPropertyLifeReport
    {
        public int HoldingsAudited;
        public int TaxesLevied;
        public int TaxesPaidCents;
        public int TreasuryCreditedCents;
        public int MaintenanceNeedsFunded;
        public int HoldingsDegraded;
        public int SalesAttempted;
        public bool SaleCompleted;
        public string SaleHoldingId = string.Empty;
        public string SaleNote = string.Empty;

        public NpcPropertyLifeReport() { }
    }

    /// <summary>
    /// Pass IX WS6: the real authorities and named buyer a brokered distress
    /// sale needs. Any missing piece refuses the sale loudly.
    /// </summary>
    public sealed class NpcDistressSaleContext
    {
        public EntityIdRegistry Ids;
        public FinancialObligationAuthority Authority;
        public CreditCashBridge Cash;
        public TitleAuthority Titles;
        public HousingAuthority Housing;
        public CreditOfferWorkflow Workflow;
        public CreditEventLog Events;
        public PropertyTaxService TaxService;
        public PropertyDisputeService Disputes;
        public int BuyerHouseholdId = -1;
        public int SalePriceCents;
        public bool BuyerAssumesNote;
        public bool CreditorConsentsToAssumption;

        public NpcDistressSaleContext() { }
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

        /// <summary>Pass IX WS6: all registered holdings (the production drive enumerates these).</summary>
        public IReadOnlyCollection<NpcPropertyHolding> Holdings => holdings.Values;

        /// <summary>
        /// Pass IX WS6: the service's own property-tax book for the production
        /// drive. Levies made through <see cref="LevyPropertyTax"/> with this
        /// service are the ones the daily drive can audit; assessments levied
        /// into a caller-owned book stay with that caller.
        /// </summary>
        public PropertyTaxService Taxes { get; } = new PropertyTaxService();

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
        /// Pass IX WS6 (A1-07/A1-11): the NPC tax leg against the civic
        /// treasury. For every levied assessment on every holding, pays what
        /// the owner's real cash covers (arrears stand for the rest). The
        /// payment waterfall is fees-first: penalties/interest are the County
        /// Tax Collector's collection income and move to the collector's real
        /// cash store when one is registered; the principal is credited to
        /// the civic treasury exactly once per cent actually paid (the
        /// treasury credit equals the increase in AmountPaidCents minus the
        /// fee portion, so a second audit can never re-credit the same
        /// cents). When no collector store is registered the fee portion
        /// also credits the treasury — nothing vanishes. Returns the total
        /// credited to the treasury; <paramref name="totalPaidCents"/> is
        /// the total that left owners' cash.
        /// </summary>
        public static int AuditTaxes(
            PropertyTaxService tax, CreditCashBridge cash,
            IEnumerable<NpcPropertyHolding> persons,
            FinancialObligationAuthority obligations,
            CivicTreasuryService treasury, int dayIndex, List<string> diag,
            out int totalPaidCents)
        {
            totalPaidCents = 0;
            int totalCredited = 0;
            diag = diag ?? new List<string>();
            if (tax == null || cash == null || persons == null || treasury == null)
            {
                diag.Add("NpcPropertyLife.AuditTaxes: tax service, cash bridge, holdings and treasury are required — nothing audited.");
                return 0;
            }
            // `obligations` is accepted so the drive's call contract carries
            // the one debt authority; the audit itself creates no debt —
            // unpaid balances remain tax arrears on the assessment.
            _ = obligations;
            foreach (NpcPropertyHolding holding in persons)
            {
                if (holding == null) continue;
                string payer = "household:" + holding.OwnerHouseholdId;
                foreach (string assessmentId in new List<string>(holding.TaxAssessmentIds))
                {
                    TaxAssessment assessment = tax.GetAssessment(assessmentId);
                    if (assessment == null || assessment.Status == TaxAssessmentStatus.Satisfied) continue;
                    int owed = assessment.TotalOwedCents();
                    if (owed <= 0) continue;
                    IRealCashStore store = cash.FindStore(payer);
                    if (store == null)
                    {
                        diag.Add($"NpcPropertyLife.AuditTaxes: '{payer}' has no real cash store — {owed}c tax on '{holding.ParcelId}' stands as arrears.");
                        continue;
                    }
                    int amount = Math.Min(owed, store.ReadBalanceCents());
                    if (amount <= 0)
                    {
                        diag.Add($"NpcPropertyLife.AuditTaxes: '{payer}' holds no cash — {owed}c tax on '{holding.ParcelId}' stands as arrears.");
                        continue;
                    }
                    // Fees-first waterfall (collector keeps finder/arrears-fee income).
                    int totalFees = Math.Max(0, assessment.PenaltiesAccruedCents + assessment.InterestAccruedCents);
                    int feePortion = Math.Min(amount, Math.Max(0, totalFees - assessment.AmountPaidCents));
                    int principalPortion = amount - feePortion;
                    bool collectorPaid = false;
                    if (feePortion > 0 && cash.FindStore("County Tax Collector") != null)
                    {
                        string feeProblem = MoveCash(cash, payer, "County Tax Collector", feePortion, dayIndex,
                            $"property tax fees {assessment.TaxYear} on '{holding.ParcelId}'", diag);
                        if (feeProblem != null)
                        {
                            diag.Add("NpcPropertyLife.AuditTaxes: collector fee move failed: " + feeProblem);
                            continue;
                        }
                        collectorPaid = true;
                    }
                    int treasuryPortion = principalPortion + (collectorPaid ? 0 : feePortion);
                    if (treasuryPortion > 0)
                    {
                        string debitProblem = DebitCashOnly(cash, payer, treasuryPortion, dayIndex,
                            $"property tax principal {assessment.TaxYear} on '{holding.ParcelId}' to civic treasury", diag);
                        if (debitProblem != null)
                        {
                            if (collectorPaid)
                                MoveCash(cash, "County Tax Collector", payer, feePortion, dayIndex,
                                    "property tax fee returned — principal debit failed", diag);
                            diag.Add("NpcPropertyLife.AuditTaxes: principal debit failed: " + debitProblem);
                            continue;
                        }
                    }
                    int paidBefore = assessment.AmountPaidCents;
                    string payProblem = tax.PayTax(assessment, amount, dayIndex, diag);
                    if (payProblem != null)
                    {
                        if (collectorPaid)
                            MoveCash(cash, "County Tax Collector", payer, feePortion, dayIndex,
                                "property tax payment returned — assessment refused", diag);
                        if (treasuryPortion > 0)
                            CreditCashOnly(cash, payer, treasuryPortion, dayIndex,
                                "property tax principal returned — assessment refused", diag);
                        diag.Add("NpcPropertyLife.AuditTaxes: assessment refused payment: " + payProblem);
                        continue;
                    }
                    int absorbed = Math.Max(0, assessment.AmountPaidCents - paidBefore);
                    // The treasury is credited exactly the portion of the
                    // absorbed payment that was debited for it — never more
                    // than was paid, never twice for the same cents.
                    int credit = Math.Min(treasuryPortion, absorbed);
                    if (credit > 0)
                    {
                        treasury.TryCredit(credit, CivicLedgerEntryKind.TaxReceived, dayIndex,
                            $"npc property tax {assessment.TaxYear} — {holding.ParcelId} ({payer})", diag);
                        totalCredited += credit;
                    }
                    totalPaidCents += absorbed;
                    holding.Decisions.Add($"day {dayIndex}: tax audit paid {absorbed}c on '{holding.ParcelId}' ({credit}c principal/fees to civic treasury{(collectorPaid ? $", {feePortion}c fees to County Tax Collector" : "")}).");
                }
            }
            return totalCredited;
        }

        /// <summary>Debits one real cash store through the bridge with no purse recipient (the treasury is the recipient store).</summary>
        private static string DebitCashOnly(CreditCashBridge cash, string payer, int amountCents, int dayIndex, string purpose, List<string> diag)
        {
            if (amountCents <= 0) return null;
            CreditCashAccount window = cash.OpenWindow(payer, diag);
            if (window == null) return $"no real cash store for '{payer}'.";
            if (window.BalanceCents < amountCents)
            {
                cash.DiscardWindow(window);
                return $"'{payer}' holds {window.BalanceCents}c, needs {amountCents}c.";
            }
            window.BalanceCents -= amountCents;
            return cash.CommitWindow(window, dayIndex, purpose, "civic-treasury", diag);
        }

        /// <summary>Credits one real cash store through the bridge (reversal path only).</summary>
        private static string CreditCashOnly(CreditCashBridge cash, string payer, int amountCents, int dayIndex, string purpose, List<string> diag)
        {
            if (amountCents <= 0) return null;
            CreditCashAccount window = cash.OpenWindow(payer, diag);
            if (window == null) return $"no real cash store for '{payer}'.";
            window.BalanceCents += amountCents;
            return cash.CommitWindow(window, dayIndex, purpose, "civic-treasury", diag);
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
            List<string> diag,
            PropertyDisputeService disputes = null)
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

            // Pass IX WS6 (A1-06): tenants on the holding are non-owner
            // occupants displaced by this acquisition — each gets a
            // Reported, Contested dispute claim on the record.
            if (disputes != null)
            {
                foreach (string tenancyId in new List<string>(holding.TenancyIds))
                {
                    if (tenancies.TryGetValue(tenancyId ?? string.Empty, out NpcPropertyTenancy tenancy) && tenancy != null)
                    {
                        disputes.RecordAcquisitionDisputes(null, holding.ParcelId,
                            buyerName, "household:" + tenancy.TenantHouseholdId, dayIndex, diag);
                    }
                }
                // The chain itself is also checked when a title authority is at hand.
                disputes.RecordAcquisitionDisputes(titles, holding.ParcelId, buyerName, null, dayIndex, diag);
            }

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

        #region Production drive (Pass IX WS6, A1-07 driver half)

        /// <summary>
        /// Attempts ONE brokered sale of a distressed holding through the
        /// real <see cref="SellProperty"/> path (real buyer cash, real title
        /// transfer, seller note settled/assumed — never restored). Distress
        /// = the owner carries an unsettled obligation in the authority or
        /// a delinquent/lien-filed tax assessment on the holding. Without a
        /// named buyer and the real authorities the sale is refused loudly,
        /// not faked.
        /// </summary>
        public NpcPropertySaleResult TryBrokerSaleFromDistress(
            NpcPropertyHolding holding, NpcDistressSaleContext context, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new NpcPropertySaleResult
            {
                HoldingId = holding != null ? holding.HoldingId : string.Empty,
                ParcelId = holding != null ? holding.ParcelId : string.Empty,
            };
            if (holding == null) return FailSale(result, "no holding to broker.", diag);
            if (context == null || context.Authority == null || context.Cash == null || context.Titles == null)
                return FailSale(result, "brokered sale needs the obligation authority, cash bridge and title authority — not supplied.", diag);
            if (context.BuyerHouseholdId < 0 || context.BuyerHouseholdId == holding.OwnerHouseholdId)
                return FailSale(result, "no real, different buyer household named — no sale brokered.", diag);
            if (context.SalePriceCents <= 0)
                return FailSale(result, "no real sale price — no sale brokered.", diag);
            if (!IsDistressed(holding, context.Authority, context.TaxService))
                return FailSale(result, $"holding '{holding.HoldingId}' is not distressed — no forced sale.", diag);
            return SellProperty(context.Ids, context.Authority, context.Cash, context.Titles,
                context.Housing, context.Workflow, holding, context.BuyerHouseholdId,
                context.SalePriceCents, context.BuyerAssumesNote, context.CreditorConsentsToAssumption,
                dayIndex, context.Events, diag, context.Disputes);
        }

        /// <summary>Distress predicate for the brokered-sale path (see <see cref="TryBrokerSaleFromDistress"/>).</summary>
        public bool IsDistressed(NpcPropertyHolding holding, FinancialObligationAuthority obligations, PropertyTaxService tax)
        {
            if (holding == null) return false;
            if (obligations != null && obligations.TotalOutstandingFor("household:" + holding.OwnerHouseholdId) > 0)
            {
                foreach (FinancialObligation obligation in obligations.Obligations)
                {
                    if (obligation != null && !obligation.Settled
                        && string.Equals(obligation.Debtor, "household:" + holding.OwnerHouseholdId, StringComparison.Ordinal)
                        && obligation.Status == FinancialObligationStatus.Delinquent)
                        return true;
                }
            }
            if (tax != null)
            {
                foreach (string assessmentId in holding.TaxAssessmentIds)
                {
                    TaxAssessment assessment = tax.GetAssessment(assessmentId);
                    if (assessment != null && assessment.TotalOwedCents() > 0
                        && (assessment.Status == TaxAssessmentStatus.Delinquent
                            || assessment.Status == TaxAssessmentStatus.LienFiled))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pass IX WS6: the periodic production step over hub holdings —
        /// (1) tax audit into the civic treasury, (2) maintenance pass
        /// (fund the oldest affordable need per holding from real cash;
        /// neglect degrades the rest), (3) at most ONE overdue/distress
        /// sale through <see cref="TryBrokerSaleFromDistress"/>. Called
        /// daily by SimulationDrivers.DriveDemographicDay; a world with no
        /// holdings (or no cash stores) no-ops cleanly with diagnostics,
        /// never invented money or buyers.
        /// </summary>
        public NpcPropertyLifeReport DriveNpcPropertyLife(
            CreditCashBridge cash, PropertyTaxService tax,
            FinancialObligationAuthority obligations, CivicTreasuryService treasury,
            int dayIndex, List<string> diag, NpcDistressSaleContext saleContext = null,
            EntityIdRegistry ids = null)
        {
            diag = diag ?? diagnostics;
            var report = new NpcPropertyLifeReport();
            report.HoldingsAudited = holdings.Count;
            if (holdings.Count == 0) return report;

            // 0) Annual levy (Pass IX review R-07): the production levy path
            // into this drive's tax book — the same catch-up rule the player
            // tax driver uses: every registered holding owes exactly one
            // assessment per tax year, levied when the current year's is
            // missing. The assessed basis is the holding's recorded
            // acquisition price (the only real valuation on the record);
            // the rate/due/penalty calibration is PropertyTaxCalibration's,
            // shared with the player leg.
            if (tax != null)
            {
                int taxYear = PropertyTaxCalibration.TaxYearForDay(dayIndex);
                foreach (NpcPropertyHolding holding in new List<NpcPropertyHolding>(holdings.Values))
                {
                    if (holding == null) continue;
                    if (tax.HasAssessmentForParcelYear(holding.ParcelId, taxYear)) continue;
                    int levyCents = PropertyTaxCalibration.ComputeLevyCents(holding.AcquisitionPriceCents);
                    if (levyCents <= 0) continue;
                    TaxAssessment levied = LevyPropertyTax(tax, ids, holding, taxYear, levyCents,
                        dayIndex + PropertyTaxCalibration.DueDaysAfterLevy,
                        PropertyTaxCalibration.PenaltyBpsMonthly,
                        PropertyTaxCalibration.InterestBpsAnnual,
                        dayIndex, diag);
                    if (levied != null) report.TaxesLevied++;
                }
            }

            // 1) Tax audit.
            if (cash != null && tax != null && treasury != null)
            {
                report.TreasuryCreditedCents = AuditTaxes(tax, cash, holdings.Values, obligations, treasury, dayIndex, diag, out int paid);
                report.TaxesPaidCents = paid;
            }
            else
            {
                diag.Add("NpcPropertyLife.Drive: tax audit skipped — cash bridge, tax service or treasury not supplied.");
            }

            // 2) Maintenance pass.
            foreach (NpcPropertyHolding holding in new List<NpcPropertyHolding>(holdings.Values))
            {
                if (holding == null) continue;
                if (cash != null)
                {
                    foreach (NpcPropertyMaintenanceNeed need in holding.MaintenanceNeeds)
                    {
                        if (need == null || need.Funded) continue;
                        string problem = FundMaintenance(cash, holding, need.NeedId, dayIndex, diag);
                        if (problem == null) report.MaintenanceNeedsFunded++;
                        break; // at most one funded need per holding per drive
                    }
                }
                float before = holding.Condition01;
                DegradeForNeglect(holding, dayIndex, diag);
                if (holding.Condition01 < before) report.HoldingsDegraded++;
            }

            // 3) At most ONE distress sale.
            foreach (NpcPropertyHolding holding in new List<NpcPropertyHolding>(holdings.Values))
            {
                if (holding == null) continue;
                if (!IsDistressed(holding, obligations, tax)) continue;
                report.SalesAttempted = 1;
                NpcPropertySaleResult sale = TryBrokerSaleFromDistress(holding, saleContext, dayIndex, diag);
                report.SaleCompleted = sale != null && sale.Sold;
                report.SaleHoldingId = holding.HoldingId;
                report.SaleNote = sale != null ? (sale.Sold ? "sold" : sale.FailureReason) : "no result";
                break;
            }
            return report;
        }

        #endregion

        #region Save / Load

        [Serializable]
        public sealed class NpcPropertyLifeSaveDto
        {
            public List<NpcPropertyHolding> Holdings = new List<NpcPropertyHolding>();
            public List<NpcPropertyTenancy> Tenancies = new List<NpcPropertyTenancy>();
            // Pass IX review R-07 (additive): the service's own tax book —
            // without it every levied assessment died with the session and
            // the production audit ran over an empty book after any load.
            public PropertyTaxService.PropertyTaxSaveDto Taxes;
        }

        public NpcPropertyLifeSaveDto CaptureSaveDto()
        {
            var dto = new NpcPropertyLifeSaveDto();
            foreach (NpcPropertyHolding holding in holdings.Values)
                if (holding != null) dto.Holdings.Add(holding);
            foreach (NpcPropertyTenancy tenancy in tenancies.Values)
                if (tenancy != null) dto.Tenancies.Add(tenancy);
            dto.Taxes = Taxes.CaptureSaveDto();
            return dto;
        }

        public void LoadFromSaveDto(NpcPropertyLifeSaveDto dto, List<string> diag)
        {
            diag = diag ?? diagnostics;
            holdings.Clear();
            tenancies.Clear();
            Taxes.LoadFromSaveDto(dto != null ? dto.Taxes : null);
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
