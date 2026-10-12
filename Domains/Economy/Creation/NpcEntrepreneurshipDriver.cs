using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;

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

            if (equipment != null)
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

            string refusal = ctx.CreationPort.TryCreateBusiness(intent, diag, out NpcCreatedBusiness created);
            if (refusal != null || created == null)
            {
                diag.Add($"Entrepreneurship: formation for P{person.id} refused: {refusal ?? "no business returned"} — no business created.");
                ctx.Events?.Record(dayIndex, NpcBusinessEventKind.FormationAborted, person.id, string.Empty,
                    $"P{person.id} formation refused after Proceed verdict: {refusal ?? "no business returned"}.");
                return;
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
