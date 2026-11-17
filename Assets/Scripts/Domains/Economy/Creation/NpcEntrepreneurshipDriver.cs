using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// Pass V P2-3: the autonomous opportunity-evaluation driver that was
    /// missing. Each week, for each eligible adult NPC, it runs the real
    /// pipeline:
    ///
    ///   observation (real unfulfilled needs the person witnessed)
    ///     -> recognition (the log's own thresholds; never manufactures demand)
    ///     -> investigation (real person time reserved; evidence from real
    ///        sources; Proceed/WalkAway verdict on the evidence)
    ///     -> formation (only on a real Proceed verdict, through the SAME
    ///        INpcBusinessCreationPort the player uses; a refusal stands)
    ///
    /// There is deliberately NO quota: the driver never targets a number of
    /// businesses, and demand is never guaranteed — a formed business lives
    /// or dies on real conditions through NpcBusinessLifecycleService.
    /// Owner restrictions: the founder must be an adult with full labor-market
    /// access, must not already own a business, must have free person-time,
    /// and must be able to fund the formation cost from confirmed capital.
    /// </summary>
    public sealed class NpcEntrepreneurshipDriver
    {
        /// <summary>All per-call inputs. Ports may be null (treated as absent).</summary>
        public sealed class EntrepreneurshipContext
        {
            public PopulationState Population;
            public HouseholdMembershipRegistry Memberships;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdNeedRegistry Needs;
            public PersonScheduleTracker Schedules;
            public NpcBusinessEventLog Events;
            /// <summary>The creation port (the adapter in production, a fake in tests).</summary>
            public INpcBusinessCreationPort CreationPort;
            /// <summary>Real goods suppliers, for candidate input suppliers.</summary>
            public SupplierDirectory Suppliers;
            /// <summary>True when the person already owns a business.</summary>
            public Func<int, bool> PersonAlreadyOwnsBusiness;
            /// <summary>
            /// Real premises rights for the opportunity (owned, rented,
            /// financed, home, or shared — the same rights the player uses).
            /// Null result means no viable premises: the investigation walks
            /// away on that ground.
            /// </summary>
            public Func<NpcOpportunity, NpcPremisesOption> PremisesFinder;
            /// <summary>
            /// Real equipment with provenance (e.g. used-equipment listings).
            /// Null result means no proven equipment: the investigation walks
            /// away on that ground.
            /// </summary>
            public Func<NpcOpportunity, NpcEquipmentOption> EquipmentFinder;
            /// <summary>
            /// B2: the commercial lease market. When set, the weekly drive
            /// re-seeds it from the town's actual unassigned commercial
            /// buildings, and formation against a lease option pays the
            /// deposit and first rent before creation and signs the lease
            /// after it. Null means no leaseable premises exist.
            /// </summary>
            public CommercialLeaseMarket LeaseMarket;
            /// <summary>
            /// B2: the town's actual unassigned commercial buildings, as
            /// (building, plot) refs. When null, the market is not
            /// re-seeded weekly. Seeded listings carry no landlord and are
            /// never offered until a landlord is known.
            /// </summary>
            public Func<List<CommercialLeasePremisesRef>> UnassignedCommercialPremises;
            public int InvestigationMinutes = 120;
            public int WorkingCapitalCents = 2000;
            /// <summary>Minimum distinct witness days before recognition.</summary>
            public int MinRecognitionDays = 2;
            /// <summary>Minimum observations before recognition.</summary>
            public int MinRecognitionObservations = 3;
        }

        private readonly Dictionary<int, NpcOpportunityObservationLog> logs =
            new Dictionary<int, NpcOpportunityObservationLog>();
        private readonly List<NpcBusinessInvestigation> investigations = new List<NpcBusinessInvestigation>();
        private readonly List<string> diagnostics = new List<string>();
        private int investigationSequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int TrackedPersonCount => logs.Count;
        public int ActiveInvestigationCount
        {
            get
            {
                int count = 0;
                foreach (NpcBusinessInvestigation i in investigations)
                    if (i != null && i.Verdict == NpcInvestigationVerdict.Underway) count++;
                return count;
            }
        }

        /// <summary>Evaluates one week of autonomous entrepreneurship.</summary>
        public void EvaluateWeek(int dayIndex, EntrepreneurshipContext ctx, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ctx == null)
            {
                diag.Add("Entrepreneurship: no context — nothing evaluated.");
                return;
            }

            if (ctx.Population == null || ctx.CreationPort == null)
            {
                diag.Add("Entrepreneurship: population or creation port missing — nothing evaluated.");
                return;
            }

            // B2: re-seed the lease market from the town's actual
            // unassigned commercial buildings. Idempotent: buildings with
            // a listing are skipped. Seeded listings carry no landlord
            // and are never offered; the market gains a landlord when a
            // building is released by a closing business.
            if (ctx.LeaseMarket != null && ctx.UnassignedCommercialPremises != null)
            {
                List<CommercialLeasePremisesRef> unassigned = ctx.UnassignedCommercialPremises();
                if (unassigned != null)
                    ctx.LeaseMarket.SeedFromPremises(unassigned, dayIndex, diag);
            }

            foreach (PersonState person in ctx.Population.people)
            {
                if (person == null) continue;
                if (!IsEligibleFounder(person, ctx)) continue;

                // 1. Observe: real unfulfilled needs this person legitimately
                //    knows about — their own household's open needs, witnessed
                //    going unmet. Bounded per week. The log is only created
                //    when the person actually observes something.
                ObserveUnfulfilledNeeds(person, ctx, dayIndex, diag);
                if (!logs.TryGetValue(person.id, out NpcOpportunityObservationLog log) || log == null)
                    continue;

                // 2. Recognize: the log's own thresholds decide.
                List<NpcOpportunity> fresh = log.RecognizeOpportunities(
                    ctx.MinRecognitionObservations, ctx.MinRecognitionDays,
                    dayIndex, ctx.Events, diag);

                // 3. Investigate watch-stage opportunities with no active
                //    investigation, then conclude and possibly form.
                foreach (NpcOpportunity opportunity in log.Opportunities)
                {
                    if (opportunity == null) continue;
                    if (opportunity.Status != NpcOpportunityStatus.Watch) continue;
                    if (HasActiveInvestigation(opportunity.OpportunityId)) continue;
                    InvestigateAndMaybeForm(person, opportunity, log, ctx, dayIndex, diag);
                }
            }
        }

        private static bool IsEligibleFounder(PersonState person, EntrepreneurshipContext ctx)
        {
            if (person.ageBand != AgeBand.Adult18Plus) return false;
            if (person.laborAccessLevel != LaborAccessLevel.FullLaborMarket) return false;
            if (ctx.PersonAlreadyOwnsBusiness != null && ctx.PersonAlreadyOwnsBusiness(person.id)) return false;
            return true;
        }

        private static int HouseholdOf(int personId, EntrepreneurshipContext ctx)
        {
            if (ctx.Memberships == null) return -1;
            HouseholdMembership m = ctx.Memberships.GetActiveMembership(personId);
            return m != null ? m.HouseholdId : -1;
        }

        private NpcOpportunityObservationLog LogFor(int personId, int householdId)
        {
            if (!logs.TryGetValue(personId, out NpcOpportunityObservationLog log) || log == null)
            {
                log = new NpcOpportunityObservationLog(personId, householdId);
                logs[personId] = log;
            }

            return log;
        }

        private void ObserveUnfulfilledNeeds(PersonState person,
            EntrepreneurshipContext ctx, int dayIndex, List<string> diag)
        {
            if (ctx.Needs == null) return;
            int householdId = HouseholdOf(person.id, ctx);
            if (householdId < 0) return;

            NpcOpportunityObservationLog log = null;
            int recorded = 0;
            foreach (HouseholdPurchasingNeed need in ctx.Needs.GetOpenNeeds(householdId))
            {
                if (need == null || need.Status != PurchasingNeedStatus.Open) continue;
                if (recorded >= 5) break; // bounded: a person notices so much per week
                if (!IsExecutableProductIdentity(need.ItemId))
                {
                    // Category-level reserve needs preserve household need,
                    // but are not a product or a purchasable business offer.
                    // Keep the observation available to household services;
                    // do not let it justify a fictional retailer.
                    continue;
                }
                // Only needs old enough to be genuinely "unfulfilled", not
                // needs opened this week that shopping may still serve.
                if (dayIndex - need.CreatedDayIndex < 7) continue;

                if (log == null) log = LogFor(person.id, householdId);
                NpcMarketObservation observation = NpcOpportunityObservationLog.FromHouseholdNeed(
                    need.NeedSequence, need.HouseholdId, need.ItemId,
                    Math.Max(1, need.UnitsNeeded), need.CreatedDayIndex,
                    person.id, householdId, $"need:{need.NeedSequence}");
                // The witness day is today: the person sees the need STILL
                // unfulfilled. (The need's created day stays in the detail.)
                observation.DayIndex = dayIndex;
                string refusal = log.RecordObservation(observation, ctx.Events);
                if (refusal == null) recorded++;
            }

            if (recorded > 0)
                diag.Add($"Entrepreneurship: P{person.id} recorded {recorded} unfulfilled-demand observation(s).");
        }

        private static bool IsExecutableProductIdentity(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;
            if (string.Equals(itemId, HouseholdItemCatalog.UnspecifiedStapleFoodId, StringComparison.Ordinal)
                || string.Equals(itemId, HouseholdItemCatalog.UnspecifiedMeatId, StringComparison.Ordinal)
                || string.Equals(itemId, HouseholdItemCatalog.UnspecifiedHouseholdGoodsId, StringComparison.Ordinal)
                || string.Equals(itemId, HouseholdItemCatalog.UnspecifiedClothingId, StringComparison.Ordinal))
                return false;

            // This observation source is household goods demand. Only exact,
            // authored item ids can become candidates; broad category labels
            // remain unmet household needs without creating an unsupported SKU.
            return HouseholdItemCatalog.Get(itemId) != null;
        }

        private bool HasActiveInvestigation(string opportunityId)
        {
            foreach (NpcBusinessInvestigation i in investigations)
            {
                if (i != null && i.Verdict == NpcInvestigationVerdict.Underway
                    && string.Equals(i.OpportunityId, opportunityId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void InvestigateAndMaybeForm(PersonState person, NpcOpportunity opportunity,
            NpcOpportunityObservationLog log, EntrepreneurshipContext ctx, int dayIndex, List<string> diag)
        {
            // Owner restriction: the founder's household must hold real
            // savings. The formation cost is estimated from real premises and
            // equipment options; hopes are not capital.
            NpcPremisesOption premises = ctx.PremisesFinder != null ? ctx.PremisesFinder(opportunity) : null;
            NpcEquipmentOption equipment = ctx.EquipmentFinder != null ? ctx.EquipmentFinder(opportunity) : null;
            int formationCost = Math.Max(0, ctx.WorkingCapitalCents)
                + (premises != null ? Math.Max(0, premises.UpfrontCostCents) : 0)
                + (equipment != null ? Math.Max(0, equipment.CostCents) : 0);

            int ownCash = 0;
            int householdId = HouseholdOf(person.id, ctx);
            if (ctx.Ledgers != null && householdId >= 0)
            {
                HouseholdLedger ledger = ctx.Ledgers.Get(householdId);
                if (ledger != null) ownCash = Math.Max(0, ledger.GetBalanceCents());
            }

            if (formationCost > 0 && ownCash < formationCost)
            {
                diag.Add($"Entrepreneurship: P{person.id} cannot fund '{opportunity.OpportunityId}' " +
                    $"({ownCash}c own cash vs {formationCost}c formation cost) — not investigated.");
                return;
            }

            var investigation = new NpcBusinessInvestigation();
            string startRefusal = investigation.Start(
                $"inv-{dayIndex}-{investigationSequence++}",
                opportunity, ctx.Schedules, ctx.InvestigationMinutes, dayIndex, ctx.Events, diag);
            if (startRefusal != null)
            {
                diag.Add($"Entrepreneurship: P{person.id} could not investigate '{opportunity.OpportunityId}': {startRefusal}");
                return;
            }

            investigations.Add(investigation);

            // Evidence from real sources only.
            var evidence = new NpcInvestigationEvidence();
            foreach (NpcMarketObservation obs in log.Observations)
            {
                if (obs != null && string.Equals(obs.ItemOrServiceId, opportunity.ItemOrServiceId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(obs.ObservationId))
                    evidence.CustomerObservationIds.Add(obs.ObservationId);
            }

            IGoodsSupplier supplier;
            int wholesaleCents;
            if (TryFindItemSupplier(ctx, opportunity.ItemOrServiceId, out supplier, out wholesaleCents))
            {
                evidence.CandidateSuppliers.Add(new NpcCandidateSupplier
                {
                    SupplierId = supplier.SupplierBusinessId,
                    ItemId = opportunity.ItemOrServiceId,
                    UnitPriceCents = wholesaleCents,
                    TermsNote = "wholesale inquiry at observed counter price",
                });
            }

            if (premises != null) evidence.PremisesOptions.Add(premises);
            if (equipment != null) evidence.EquipmentOptions.Add(equipment);

            evidence.Financing.OwnCashCents = Math.Min(ownCash, formationCost);
            evidence.EstimatedFormationCostCents = formationCost;
            int weeklyUnmet = Math.Max(0, opportunity.EstimatedUnmetUnitsPerDay) * 7;
            int retailPrice = RealRetailPrice(ctx, opportunity.ItemOrServiceId);
            int wholesalePrice = evidence.CandidateSuppliers.Count > 0
                ? evidence.CandidateSuppliers[0].UnitPriceCents : 0;
            // The investigator's own estimate — recorded AS an estimate.
            evidence.InvestigatorRevenueEstimateCentsPerWeek = weeklyUnmet * Math.Max(0, retailPrice);
            evidence.InvestigatorCostEstimateCentsPerWeek = weeklyUnmet * Math.Max(0, wholesalePrice);

            string concludeRefusal = investigation.Conclude(evidence, log, dayIndex, ctx.Events, diag);
            if (concludeRefusal != null)
            {
                diag.Add($"Entrepreneurship: investigation '{investigation.InvestigationId}' could not conclude: {concludeRefusal}");
                return;
            }

            if (investigation.Verdict != NpcInvestigationVerdict.Proceed)
            {
                diag.Add($"Entrepreneurship: P{person.id} walked away from '{opportunity.OpportunityId}' — " +
                    string.Join(" | ", investigation.WalkAwayReasons));
                return;
            }

            FormBusiness(person, opportunity, investigation, premises, equipment, evidence, ctx, dayIndex, diag);
        }

        private static int RealRetailPrice(EntrepreneurshipContext ctx, string itemId)
        {
            IGoodsSupplier supplier;
            int priceCents;
            return TryFindItemSupplier(ctx, itemId, out supplier, out priceCents) ? priceCents : 0;
        }

        /// <summary>
        /// Finds a real supplier offering the item (item-resolving suppliers
        /// only — category-level suppliers cannot price a specific item).
        /// </summary>
        private static bool TryFindItemSupplier(EntrepreneurshipContext ctx, string itemId,
            out IGoodsSupplier supplier, out int priceCents)
        {
            supplier = null;
            priceCents = 0;
            if (ctx.Suppliers == null || string.IsNullOrWhiteSpace(itemId)) return false;
            foreach (IGoodsSupplier candidate in ctx.Suppliers.All)
            {
                var resolving = candidate as IItemResolvingSupplier;
                if (resolving == null || !resolving.OffersItem(itemId)) continue;
                int price = resolving.ItemPricePerUnitCents(itemId);
                if (price <= 0) continue;
                supplier = candidate;
                priceCents = price;
                return true;
            }

            return false;
        }

        private void FormBusiness(PersonState person, NpcOpportunity opportunity,
            NpcBusinessInvestigation investigation, NpcPremisesOption premises,
            NpcEquipmentOption equipment, NpcInvestigationEvidence evidence,
            EntrepreneurshipContext ctx, int dayIndex, List<string> diag)
        {
            var intent = new NpcBusinessFormationIntent
            {
                FounderPersonId = person.id,
                FounderName = person.firstName ?? string.Empty,
                FounderSurname = person.lastName ?? string.Empty,
                FounderHouseholdId = HouseholdOf(person.id, ctx),
                DisplayName = $"{person.lastName} {opportunity.ItemOrServiceId} Store".Trim(),
                InvestigationId = investigation.InvestigationId,
                OwnerLaborHoursPerWeek = 40,
                WorkingCapitalCents = Math.Max(0, ctx.WorkingCapitalCents),
            };
            intent.Activities.Add(new NpcBusinessActivitySpec
            {
                BusinessType = BusinessType.Generic,
                CapabilityIds = new List<string> { "retail" },
                RequiredStockItemIds = new List<string> { opportunity.ItemOrServiceId },
            });
            if (premises != null)
            {
                intent.Premises = new NpcPremisesArrangement
                {
                    Tenure = premises.Tenure,
                    ParcelOrBuildingRef = premises.ParcelOrBuildingRef,
                    AgreementId = premises.AgreementRef,
                    UpfrontCostCents = premises.UpfrontCostCents,
                    RecurringCostCents = premises.RecurringCostCents,
                    CustomerFacing = true,
                    Workshop = false,
                };
            }

            // B2: fixtures included in a commercial lease are part of the
            // leased premises, not owned equipment — they are never a
            // formation asset and never liquidate on close. The equipment
            // option is evidence for the investigation only.
            bool leasedFixtures = equipment != null
                && string.Equals(equipment.ItemId, CommercialLeaseCalibration.LeasedFixturesItemId, StringComparison.Ordinal)
                && equipment.Provenance != null
                && equipment.Provenance.StartsWith(CommercialLeaseCalibration.LeasedFixturesProvenancePrefix, StringComparison.Ordinal);
            if (equipment != null && !leasedFixtures)
            {
                intent.Assets.Add(new NpcFormationAsset
                {
                    AssetKind = "equipment",
                    ItemId = equipment.ItemId,
                    Units = 1,
                    CostCents = equipment.CostCents,
                    Provenance = equipment.Provenance,
                });
            }

            // B2: a formation against a leased shop pays the deposit and
            // the first month's rent BEFORE creation (first month +
            // deposit is what a tenant pays at signing), and signs the
            // lease only after the business exists. On a creation refusal
            // both payments are refunded in full — the founder pays for
            // no shop he did not get.
            CommercialLeaseListing leaseListing = null;
            int leaseDepositCents = 0;
            int leaseFirstRentCents = 0;
            HouseholdLedger founderLedger = null;
            HouseholdLedger landlordLedger = null;
            if (ctx.LeaseMarket != null && premises != null && premises.Tenure == NpcPremisesTenure.Rented
                && !string.IsNullOrWhiteSpace(premises.AgreementRef))
            {
                leaseListing = ctx.LeaseMarket.FindListingById(premises.AgreementRef);
                if (leaseListing == null || !leaseListing.IsOfferable)
                {
                    diag.Add($"Entrepreneurship: formation for P{person.id} refused — the lease listing '{premises.AgreementRef}' is not offerable.");
                    ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationAborted, person.id, string.Empty,
                        $"P{person.id} formation refused: lease listing '{premises.AgreementRef}' is not offerable.");
                    return;
                }

                leaseDepositCents = leaseListing.DepositCents;
                leaseFirstRentCents = leaseListing.MonthlyRentCents;
                int leaseUpfrontCents = leaseDepositCents + leaseFirstRentCents;
                if (leaseUpfrontCents != premises.UpfrontCostCents)
                {
                    diag.Add($"Entrepreneurship: formation for P{person.id} refused — lease listing '{leaseListing.ListingId}' " +
                        $"charges {leaseUpfrontCents}c upfront (deposit {leaseDepositCents}c + first rent {leaseFirstRentCents}c) " +
                        $"but the premises option carries {premises.UpfrontCostCents}c. The finder and the listing disagree; nothing moved.");
                    ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationAborted, person.id, string.Empty,
                        $"P{person.id} formation refused: lease upfront {leaseUpfrontCents}c disagrees with the premises option {premises.UpfrontCostCents}c.");
                    return;
                }

                int founderHouseholdId = HouseholdOf(person.id, ctx);
                int landlordHouseholdId = HouseholdOf(leaseListing.LandlordPersonId, ctx);
                founderLedger = ctx.Ledgers != null && founderHouseholdId >= 0 ? ctx.Ledgers.Get(founderHouseholdId) : null;
                landlordLedger = ctx.Ledgers != null && landlordHouseholdId >= 0 ? ctx.Ledgers.Get(landlordHouseholdId) : null;
                if (founderLedger == null || landlordLedger == null)
                {
                    diag.Add($"Entrepreneurship: formation for P{person.id} refused — founder ledger " +
                        $"{(founderLedger == null ? "missing" : "found")} / landlord ledger {(landlordLedger == null ? "missing" : "found")} " +
                        $"(founder H{founderHouseholdId}, landlord P{leaseListing.LandlordPersonId} H{landlordHouseholdId}). No lease payment moved.");
                    ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationAborted, person.id, string.Empty,
                        $"P{person.id} formation refused: founder or landlord ledger missing for the lease payment.");
                    return;
                }

                string depositRefusal = founderLedger.TryTransferTo(landlordLedger, dayIndex, leaseDepositCents,
                    $"lease deposit for building {leaseListing.BuildingId} via {leaseListing.ListingId}",
                    HouseholdIncomeSource.OtherDocumented, leaseListing.ListingId,
                    $"lease deposit held for {leaseListing.ListingId} (refundable on lease end)",
                    "person:" + person.id);
                if (depositRefusal != null)
                {
                    diag.Add($"Entrepreneurship: formation for P{person.id} refused — lease deposit transfer failed: {depositRefusal}");
                    ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationAborted, person.id, string.Empty,
                        $"P{person.id} formation refused: lease deposit transfer failed.");
                    return;
                }

                string firstRentRefusal = founderLedger.TryTransferTo(landlordLedger, dayIndex, leaseFirstRentCents,
                    $"first month commercial rent for building {leaseListing.BuildingId} via {leaseListing.ListingId}",
                    HouseholdIncomeSource.CommercialRentReceived, leaseListing.ListingId,
                    $"first month commercial rent for building {leaseListing.BuildingId} via {leaseListing.ListingId}",
                    "person:" + person.id);
                if (firstRentRefusal != null)
                {
                    string depositRefundRefusal = landlordLedger.TryTransferTo(founderLedger, dayIndex, leaseDepositCents,
                        $"lease deposit refund for {leaseListing.ListingId} (first rent transfer failed)",
                        HouseholdIncomeSource.OtherDocumented, leaseListing.ListingId,
                        $"lease deposit refund for {leaseListing.ListingId}",
                        "person:" + leaseListing.LandlordPersonId);
                    diag.Add($"Entrepreneurship: formation for P{person.id} refused — first rent transfer failed: {firstRentRefusal}. " +
                        $"Deposit refund {(depositRefundRefusal == null ? "returned" : "FAILED: " + depositRefundRefusal)}.");
                    ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationAborted, person.id, string.Empty,
                        $"P{person.id} formation refused: first rent transfer failed; deposit refund {(depositRefundRefusal == null ? "returned" : "FAILED")}.");
                    return;
                }
            }

            string refusal = ctx.CreationPort.TryCreateBusiness(intent, diag, out NpcCreatedBusiness created);
            if (refusal == null && created != null)
            {
                // R-01: scope the intent's formation assets to the created business.
                foreach (NpcFormationAsset asset in intent.Assets)
                {
                    if (asset != null) asset.OwnerBusinessId = created.InstanceId;
                }
            }

            if (refusal != null || created == null)
            {
                // B2: a refused creation refunds the deposit and first
                // rent in full — the founder pays for no shop he did not
                // get. The refunds cannot fail in the same tick (the
                // landlord just received both), but a failure is logged
                // loudly instead of hidden.
                if (leaseListing != null && founderLedger != null && landlordLedger != null)
                {
                    string depositRefund = landlordLedger.TryTransferTo(founderLedger, dayIndex, leaseDepositCents,
                        $"lease deposit refund for {leaseListing.ListingId} (formation refused)",
                        HouseholdIncomeSource.OtherDocumented, leaseListing.ListingId,
                        $"lease deposit refund for {leaseListing.ListingId}",
                        "person:" + leaseListing.LandlordPersonId);
                    string rentRefund = landlordLedger.TryTransferTo(founderLedger, dayIndex, leaseFirstRentCents,
                        $"first month rent refund for {leaseListing.ListingId} (formation refused)",
                        HouseholdIncomeSource.CommercialRentReceived, leaseListing.ListingId,
                        $"first month rent refund for {leaseListing.ListingId}",
                        "person:" + leaseListing.LandlordPersonId);
                    diag.Add($"Entrepreneurship: formation for P{person.id} refused: {refusal ?? "no business returned"} — no business created. " +
                        $"Lease deposit refund {(depositRefund == null ? "returned" : "FAILED: " + depositRefund)}; " +
                        $"first rent refund {(rentRefund == null ? "returned" : "FAILED: " + rentRefund)}.");
                }
                else
                {
                    diag.Add($"Entrepreneurship: formation for P{person.id} refused: {refusal ?? "no business returned"} — no business created.");
                }

                ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationAborted, person.id, string.Empty,
                    $"P{person.id} formation refused after Proceed verdict: {refusal ?? "no business returned"}.");
                return;
            }

            // B2: the business exists — sign the lease. The first month
            // is prepaid, so the rent settlement starts at period 1. An
            // activation failure here cannot happen after the pre-checks
            // above (the listing was offerable a moment ago in the same
            // tick); if it ever does, the business is formed and the
            // payments are recorded on the ledgers by their reasons, and
            // the failure is logged loudly for a human to reconcile —
            // never silently continued as if a lease existed.
            if (leaseListing != null)
            {
                CommercialLeaseAgreement agreement = ctx.LeaseMarket.ActivateLease(leaseListing,
                    person.id, intent.FounderHouseholdId, created.InstanceId,
                    leaseDepositCents, firstRentPeriodPrepaid: true, dayIndex, diag);
                if (agreement == null)
                {
                    diag.Add($"Entrepreneurship: P{person.id} formed '{created.DisplayName}' ({created.InstanceId}) but the lease " +
                        $"on '{leaseListing.ListingId}' could NOT be activated — deposit {leaseDepositCents}c and first rent " +
                        $"{leaseFirstRentCents}c were paid and are recorded on the ledgers; the lease must be reconciled by hand.");
                }
                else
                {
                    diag.Add($"Entrepreneurship: P{person.id} signed lease '{agreement.AgreementId}' for building {leaseListing.BuildingId} " +
                        $"(deposit {leaseDepositCents}c, first month {leaseFirstRentCents}c prepaid).");
                }
            }

            // The opportunity is acted on: a later week must not form a
            // second business from the same recognition.
            opportunity.Status = NpcOpportunityStatus.ActedOn;
            opportunity.StatusReason = $"formed '{created.DisplayName}' ({created.InstanceId}) on day {dayIndex}.";
            ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationCreated, person.id, created.InstanceId,
                $"P{person.id} formed '{created.DisplayName}' from opportunity '{opportunity.OpportunityId}' " +
                $"after a Proceed verdict (investigation '{investigation.InvestigationId}').");

            diag.Add($"Entrepreneurship: P{person.id} formed '{created.DisplayName}' ({created.InstanceId}) from opportunity '{opportunity.OpportunityId}'.");
        }

        // ================= save/load (clear-then-load) =================

        [Serializable]
        public sealed class NpcEntrepreneurshipDriverSaveDto
        {
            public List<NpcOpportunityObservationLogSaveDto> ObservationLogs = new List<NpcOpportunityObservationLogSaveDto>();
            public List<NpcBusinessInvestigationSaveDto> Investigations = new List<NpcBusinessInvestigationSaveDto>();
            public int InvestigationSequence;
        }

        public NpcEntrepreneurshipDriverSaveDto CaptureSaveDto()
        {
            var dto = new NpcEntrepreneurshipDriverSaveDto { InvestigationSequence = investigationSequence };
            foreach (KeyValuePair<int, NpcOpportunityObservationLog> kv in logs)
            {
                if (kv.Value == null) continue;
                dto.ObservationLogs.Add(kv.Value.CaptureSaveDto());
            }

            foreach (NpcBusinessInvestigation investigation in investigations)
            {
                if (investigation == null) continue;
                dto.Investigations.Add(investigation.CaptureSaveDto());
            }

            return dto;
        }

        public void LoadFromSaveDto(NpcEntrepreneurshipDriverSaveDto dto)
        {
            logs.Clear();
            investigations.Clear();
            diagnostics.Clear();
            investigationSequence = 0;
            if (dto == null) return;
            investigationSequence = Math.Max(0, dto.InvestigationSequence);
            if (dto.ObservationLogs != null)
            {
                foreach (NpcOpportunityObservationLogSaveDto logDto in dto.ObservationLogs)
                {
                    if (logDto == null) continue;
                    var log = new NpcOpportunityObservationLog();
                    log.LoadFromSaveDto(logDto);
                    if (log.PersonId >= 0) logs[log.PersonId] = log;
                }
            }

            if (dto.Investigations != null)
            {
                foreach (NpcBusinessInvestigationSaveDto invDto in dto.Investigations)
                {
                    if (invDto == null) continue;
                    var investigation = new NpcBusinessInvestigation();
                    investigation.LoadFromSaveDto(invDto);
                    investigations.Add(investigation);
                }
            }
        }
    }
}
