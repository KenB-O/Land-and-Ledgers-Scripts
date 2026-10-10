using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// Phase G (G3): one activity a forming business will perform. The
    /// BusinessType is DESCRIPTIVE (which trade this resembles) — it NEVER
    /// grants operating permissions. A Person may start a MIXED business:
    /// one identity, several activities, as long as the resources support
    /// each of them (BIZ-2 capabilities, never type-gated).
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessActivitySpec
    {
        public BusinessType BusinessType = BusinessType.Generic;
        public List<string> CapabilityIds = new List<string>();

        /// <summary>Roles that must be covered by hires or the founder's own labor.</summary>
        public List<string> RequiredRoleIds = new List<string>();

        /// <summary>Stock categories that must be on hand before trading.</summary>
        public List<string> RequiredStockItemIds = new List<string>();

        /// <summary>Equipment that must be held with provenance.</summary>
        public List<string> RequiredEquipmentIds = new List<string>();

        public NpcBusinessActivitySpec() { }
    }

    /// <summary>
    /// Phase G (G3): the premises arrangement for a forming business —
    /// owned, rented, financed (Phase F), home room, shared, no site, or
    /// mobile route. Tenure is a fact about rights, resolved through the
    /// same authorities the player uses.
    /// </summary>
    [Serializable]
    public sealed class NpcPremisesArrangement
    {
        public NpcPremisesTenure Tenure = NpcPremisesTenure.Unspecified;
        public string ParcelOrBuildingRef = string.Empty;
        public string AgreementId = string.Empty;
        public int UpfrontCostCents;
        public int RecurringCostCents;
        public bool CustomerFacing;
        public bool Workshop;
        public bool AnimalHousing;
        public bool YardStorage;
        public bool FoodHandling;
        public int MinimumAreaSqFt;

        public NpcPremisesArrangement() { }
    }

    /// <summary>
    /// Phase G (G3): one formation asset — equipment or inventory — with
    /// real provenance. The provenance names the purchased lot, the
    /// purchase agreement, or the genuine grant/borrow agreement. Assets
    /// are never free and never conjured.
    /// </summary>
    [Serializable]
    public sealed class NpcFormationAsset
    {
        public string AssetKind = string.Empty; // "equipment" | "inventory"
        public string ItemId = string.Empty;
        public int Units;
        public int CostCents;
        public string Provenance = string.Empty;
        public bool Liquidated;

        public NpcFormationAsset() { }
    }

    /// <summary>
    /// Phase G (G3): one hire for the forming business, registered through
    /// the real employment authorities — never an informal handshake the
    /// payroll cannot see.
    /// </summary>
    [Serializable]
    public sealed class NpcFormationHire
    {
        public int PersonId = -1;
        public string RoleId = string.Empty;
        public int AgreedWeeklyWageCents;
        public int StartDayIndex;

        public NpcFormationHire() { }
    }

    /// <summary>
    /// Phase G (G3): one agreement the forming business enters — supplier,
    /// premises, credit, or service — through the real authorities.
    /// </summary>
    [Serializable]
    public sealed class NpcFormationAgreement
    {
        public string AgreementKind = string.Empty; // "supplier" | "premises" | "credit" | "service"
        public string AgreementId = string.Empty;
        public string Counterparty = string.Empty;
        public string Summary = string.Empty;

        public NpcFormationAgreement() { }
    }

    /// <summary>
    /// Phase G (G3): the NPC's formation intent. Everything the business
    /// needs is named here with real references — real Person decisions,
    /// actual cash, real property rights, equipment with provenance, real
    /// inventory lots, real hires, real agreements. Nothing is implied.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessFormationIntent
    {
        public int FounderPersonId = -1;
        public string FounderName = string.Empty;
        public string FounderSurname = string.Empty;
        public int FounderHouseholdId = -1;
        public string DisplayName = string.Empty;
        public List<NpcBusinessActivitySpec> Activities = new List<NpcBusinessActivitySpec>();
        public NpcPremisesArrangement Premises = new NpcPremisesArrangement();
        public List<NpcFormationAsset> Assets = new List<NpcFormationAsset>();
        public List<NpcFormationHire> Hires = new List<NpcFormationHire>();
        public List<NpcFormationAgreement> Agreements = new List<NpcFormationAgreement>();

        /// <summary>Obligation ids from the NPC credit loop (Phase E) backing this formation.</summary>
        public List<string> CreditObligationIds = new List<string>();

        /// <summary>The founder's own committed labor, scheduled through the time authority.</summary>
        public int OwnerLaborHoursPerWeek;

        /// <summary>Working capital beyond assets and premises (float for the first weeks).</summary>
        public int WorkingCapitalCents;

        /// <summary>
        /// Optional: the Proceed verdict that backs this formation. When
        /// present it must be a real Proceed verdict; formation never
        /// proceeds on a walk-away.
        /// </summary>
        public string InvestigationId = string.Empty;

        public NpcBusinessFormationIntent() { }

        /// <summary>
        /// Every cent the formation consumes, derived from the named
        /// resources — the conservation anchor the tests audit.
        /// </summary>
        public int TotalFormationCostCents()
        {
            int total = Math.Max(0, WorkingCapitalCents);
            if (Premises != null) total += Math.Max(0, Premises.UpfrontCostCents);
            if (Assets != null)
            {
                foreach (NpcFormationAsset asset in Assets)
                {
                    if (asset != null) total += Math.Max(0, asset.CostCents);
                }
            }

            return total;
        }
    }

    /// <summary>
    /// Phase G (G3): what the generic creation flow returned for an NPC
    /// founder. The NPC goes through the SAME BIZ-1 workflow as the player
    /// — this is the record of that, not a parallel path.
    /// </summary>
    [Serializable]
    public sealed class NpcCreatedBusiness
    {
        public string BusinessEntityKey = string.Empty;
        public string InstanceId = string.Empty;
        public string DisplayName = string.Empty;
        public List<string> CapabilityIds = new List<string>();
        public string OwnerName = string.Empty;
        public int FounderPersonId = -1;

        public NpcCreatedBusiness() { }
    }

    /// <summary>
    /// Phase G (G3): the port to the generic BIZ-1 creation flow. The
    /// Unity runtime implements this by building a real
    /// <see cref="CreateBusinessIntent"/> (NPC founder as
    /// <see cref="BusinessOwnerIdentity"/>) and running the real
    /// <see cref="BusinessCreationAuthority"/> — the SAME flow the player
    /// uses, no NPC-only shortcut. Tests use a fake port that records the
    /// intent it received.
    /// </summary>
    public interface INpcBusinessCreationPort
    {
        /// <summary>Returns null on success, a loud refusal otherwise.</summary>
        string TryCreateBusiness(NpcBusinessFormationIntent intent, List<string> diagnostics,
            out NpcCreatedBusiness created);
    }

    /// <summary>
    /// Phase G (G3): the register of formation assets with provenance.
    /// Upstream-provenance doctrine: every lot names its source; nothing
    /// teleports in.
    /// </summary>
    [Serializable]
    public sealed class NpcFormationAssetRegister
    {
        [SerializeField]
        private List<NpcFormationAsset> assets = new List<NpcFormationAsset>();

        public IReadOnlyList<NpcFormationAsset> Assets => assets;

        /// <summary>Returns null on success, a refusal otherwise. Provenance is mandatory.</summary>
        public string Record(NpcFormationAsset asset)
        {
            if (asset == null) return "Cannot record a null asset.";
            if (string.IsNullOrWhiteSpace(asset.ItemId)) return "Cannot record an asset without an item id.";
            if (asset.Units <= 0) return $"Cannot record asset '{asset.ItemId}': units must be positive.";
            if (string.IsNullOrWhiteSpace(asset.Provenance))
            {
                return $"Cannot record asset '{asset.ItemId}': provenance is required — equipment and inventory are never free.";
            }

            assets.Add(asset);
            return null;
        }

        public int CountForBusiness(string businessInstanceId)
        {
            // Assets are recorded per formation run; the register is scoped
            // to one formation by the service. Count is informational.
            return assets != null ? assets.Count : 0;
        }

        public NpcFormationAssetRegisterSaveDto CaptureSaveDto()
        {
            var dto = new NpcFormationAssetRegisterSaveDto();
            if (assets != null) dto.Assets.AddRange(assets);
            return dto;
        }

        public void LoadFromSaveDto(NpcFormationAssetRegisterSaveDto dto)
        {
            assets.Clear();
            if (dto == null || dto.Assets == null) return;
            assets.AddRange(dto.Assets);
        }
    }

    /// <summary>CLN-1: persisted formation assets (save pipeline).</summary>
    [Serializable]
    public sealed class NpcFormationAssetRegisterSaveDto
    {
        public List<NpcFormationAsset> Assets = new List<NpcFormationAsset>();

        public NpcFormationAssetRegisterSaveDto() { }
    }

    /// <summary>
    /// Phase G (G3): the result of a formation run, with full resource
    /// accounting — every cent and lot traceable. A failed formation
    /// leaves no partial state: cash is refunded and the abort is loud.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessFormationResult
    {
        public bool Success;
        public string FailureReason = string.Empty;
        public NpcCreatedBusiness CreatedBusiness;
        public int HouseholdCashOutCents;
        public int PremisesCostCents;
        public int EquipmentCostCents;
        public int InventoryCostCents;
        public int WorkingCapitalCents;
        public List<string> EmploymentIds = new List<string>();
        public List<string> AgreementIds = new List<string>();
        public List<string> Diagnostics = new List<string>();
        public bool CashRefunded;

        public NpcBusinessFormationResult() { }
    }

    /// <summary>
    /// Phase G (G3): everything formation needs from the hosting game.
    /// Household cash, employment and time come from the REAL authorities;
    /// business creation goes through the generic BIZ-1 port.
    /// </summary>
    public sealed class NpcFormationContext
    {
        public HouseholdLedger HouseholdLedger;
        public EmploymentRelationshipRegistry Employments;
        public PersonScheduleTracker ScheduleTracker;
        public INpcBusinessCreationPort CreationPort;
        public NpcFormationAssetRegister AssetRegister;
        public NpcBusinessEventLog Events;
        public EntityIdRegistry Ids;
        public int DayIndex;
    }

    /// <summary>
    /// Phase G (G3): formation with real resources. The NPC founder is the
    /// principal (identity preserved as an NPC owner); cash leaves the
    /// household ledger outflow by outflow (conserved — the ledger refuses
    /// overdrafts); the business is created through the SAME generic flow
    /// as the player; hires go through the employment authorities;
    /// equipment and inventory carry provenance; agreements go through the
    /// real authorities; and the business must be genuinely operable —
    /// staffed, stocked, capable — before it is marked ready to trade.
    /// </summary>
    public sealed class NpcBusinessFormationService
    {
        /// <summary>
        /// Validates the intent against real resources. Human-readable
        /// problems; empty means the intent is actionable. No state changes.
        /// </summary>
        public List<string> Validate(NpcBusinessFormationIntent intent, NpcFormationContext context)
        {
            var problems = new List<string>();
            if (intent == null)
            {
                problems.Add("No formation intent supplied.");
                return problems;
            }

            if (context == null)
            {
                problems.Add("No formation context supplied.");
                return problems;
            }

            if (intent.FounderPersonId < 0)
            {
                problems.Add("Formation needs a real founder Person — identity is preserved, never anonymous.");
            }

            if (string.IsNullOrWhiteSpace(intent.DisplayName))
            {
                problems.Add("The business needs a display name.");
            }

            if (intent.Activities == null || intent.Activities.Count == 0)
            {
                problems.Add("Formation needs at least one activity — a business with nothing to do is not a business.");
            }
            else
            {
                // NO BusinessType permission gate: a mixed business is fine
                // when the resources support every activity. We check the
                // resources, never the type.
                foreach (NpcBusinessActivitySpec activity in intent.Activities)
                {
                    if (activity == null)
                    {
                        problems.Add("Formation activity is null.");
                    }
                }
            }

            if (intent.Premises == null || intent.Premises.Tenure == NpcPremisesTenure.Unspecified)
            {
                problems.Add("Formation needs a real premises arrangement (owned, rented, financed, home, shared, no-site, or mobile).");
            }
            else if (intent.Premises.Tenure != NpcPremisesTenure.NoSite
                && intent.Premises.Tenure != NpcPremisesTenure.MobileRoute
                && string.IsNullOrWhiteSpace(intent.Premises.ParcelOrBuildingRef))
            {
                problems.Add("The premises arrangement names no parcel or building — rights must reference something real.");
            }

            if (intent.Assets != null)
            {
                foreach (NpcFormationAsset asset in intent.Assets)
                {
                    if (asset == null) { problems.Add("Formation asset is null."); continue; }
                    if (string.IsNullOrWhiteSpace(asset.Provenance))
                    {
                        problems.Add($"Asset '{asset.ItemId}': provenance is required — never free, never conjured.");
                    }

                    if (asset.Units <= 0)
                    {
                        problems.Add($"Asset '{asset.ItemId}': units must be positive.");
                    }
                }
            }

            if (intent.Hires != null)
            {
                foreach (NpcFormationHire hire in intent.Hires)
                {
                    if (hire == null) { problems.Add("Formation hire is null."); continue; }
                    if (hire.PersonId < 0)
                    {
                        problems.Add($"Hire for role '{hire.RoleId}': needs a real Person.");
                    }

                    if (hire.AgreedWeeklyWageCents <= 0)
                    {
                        problems.Add($"Hire P{hire.PersonId} ('{hire.RoleId}'): wage must be positive — the payroll cannot see a handshake.");
                    }
                }
            }

            // Opening conditions: staffed, stocked, capable — per activity,
            // from the plan's real resources.
            CheckOpeningConditions(intent, problems);

            int totalCost = intent.TotalFormationCostCents();
            if (context.HouseholdLedger == null)
            {
                problems.Add("Formation needs the founder household's real ledger — cash must come from somewhere.");
            }
            else if (context.HouseholdLedger.GetBalanceCents() < totalCost)
            {
                problems.Add($"Household {intent.FounderHouseholdId} holds {context.HouseholdLedger.GetBalanceCents()}c " +
                    $"but formation costs {totalCost}c — insufficient funds, nothing moves.");
            }

            if (context.CreationPort == null)
            {
                problems.Add("Formation needs the generic creation port — no NPC-only shortcut exists.");
            }

            return problems;
        }

        private void CheckOpeningConditions(NpcBusinessFormationIntent intent, List<string> problems)
        {
            var coveredRoles = new HashSet<string>(StringComparer.Ordinal);
            if (intent.Hires != null)
            {
                foreach (NpcFormationHire hire in intent.Hires)
                {
                    if (hire != null && !string.IsNullOrWhiteSpace(hire.RoleId))
                    {
                        coveredRoles.Add(hire.RoleId);
                    }
                }
            }

            // The founder's own labor covers the "owner" role.
            if (intent.OwnerLaborHoursPerWeek > 0)
            {
                coveredRoles.Add("owner");
            }

            var heldEquipment = new HashSet<string>(StringComparer.Ordinal);
            var stockedItems = new HashSet<string>(StringComparer.Ordinal);
            if (intent.Assets != null)
            {
                foreach (NpcFormationAsset asset in intent.Assets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.ItemId)) continue;
                    if (string.Equals(asset.AssetKind, "equipment", StringComparison.OrdinalIgnoreCase))
                    {
                        heldEquipment.Add(asset.ItemId);
                    }
                    else if (string.Equals(asset.AssetKind, "inventory", StringComparison.OrdinalIgnoreCase))
                    {
                        stockedItems.Add(asset.ItemId);
                    }
                }
            }

            foreach (NpcBusinessActivitySpec activity in intent.Activities)
            {
                if (activity == null) continue;
                if (activity.RequiredRoleIds != null)
                {
                    foreach (string role in activity.RequiredRoleIds)
                    {
                        if (!string.IsNullOrWhiteSpace(role) && !coveredRoles.Contains(role))
                        {
                            problems.Add($"Opening conditions: role '{role}' ({activity.BusinessType}) has no hire and no owner labor — the business would not be staffed.");
                        }
                    }
                }

                if (activity.RequiredStockItemIds != null)
                {
                    foreach (string item in activity.RequiredStockItemIds)
                    {
                        if (!string.IsNullOrWhiteSpace(item) && !stockedItems.Contains(item))
                        {
                            problems.Add($"Opening conditions: stock item '{item}' ({activity.BusinessType}) is not in the formation inventory — the business would open empty.");
                        }
                    }
                }

                if (activity.RequiredEquipmentIds != null)
                {
                    foreach (string equipment in activity.RequiredEquipmentIds)
                    {
                        if (!string.IsNullOrWhiteSpace(equipment) && !heldEquipment.Contains(equipment))
                        {
                            problems.Add($"Opening conditions: equipment '{equipment}' ({activity.BusinessType}) has no provenance — the business could not do the work.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Executes the formation: cash out (conserved), creation through
        /// the generic flow, hires, assets, agreements, opening check.
        /// Any failure unwinds cleanly — cash refunded, nothing half-made.
        /// </summary>
        public NpcBusinessFormationResult Execute(NpcBusinessFormationIntent intent, NpcFormationContext context)
        {
            var result = new NpcBusinessFormationResult();
            List<string> problems = Validate(intent, context);
            if (problems.Count > 0)
            {
                result.Success = false;
                result.FailureReason = string.Join(" | ", problems);
                result.Diagnostics.AddRange(problems);
                context?.Events?.Record(context.DayIndex, NpcBusinessEventKind.FormationAborted,
                    intent != null ? intent.FounderPersonId : -1, string.Empty,
                    $"formation of '{intent?.DisplayName}' aborted in validation: {result.FailureReason}");
                return result;
            }

            int day = context.DayIndex;
            string businessLabel = $"'{intent.DisplayName}' (P{intent.FounderPersonId})";
            context.Events?.Record(day, NpcBusinessEventKind.FormationValidated, intent.FounderPersonId,
                string.Empty,
                $"formation of {businessLabel} validated: {intent.Activities.Count} activit(ies), " +
                $"formation cost {intent.TotalFormationCostCents()}c, all resources real.");

            // Step 1: move the cash — outflow by outflow, each with a real
            // purpose. The ledger refuses overdrafts; a refusal aborts loudly.
            int totalOut = 0;
            string cashRefusal = MoveFormationCash(intent, context, result, ref totalOut);
            if (cashRefusal != null)
            {
                return AbortWithRefund(intent, context, result, totalOut,
                    $"cash movement failed: {cashRefusal}");
            }

            result.HouseholdCashOutCents = totalOut;
            context.Events?.Record(day, NpcBusinessEventKind.FormationCashMoved, intent.FounderPersonId,
                string.Empty,
                $"formation of {businessLabel}: {totalOut}c moved from household {intent.FounderHouseholdId} " +
                $"(premises {result.PremisesCostCents}c, equipment {result.EquipmentCostCents}c, " +
                $"inventory {result.InventoryCostCents}c, working capital {result.WorkingCapitalCents}c) — conserved.");

            // Step 2: create through the SAME generic BIZ-1 flow the player uses.
            var createDiagnostics = new List<string>();
            string creationRefusal = context.CreationPort.TryCreateBusiness(intent, createDiagnostics, out NpcCreatedBusiness created);
            result.Diagnostics.AddRange(createDiagnostics);
            if (creationRefusal != null || created == null)
            {
                return AbortWithRefund(intent, context, result, totalOut,
                    $"generic creation flow refused: {creationRefusal ?? "no business returned"}");
            }

            result.CreatedBusiness = created;
            context.Events?.Record(day, NpcBusinessEventKind.FormationCreated, intent.FounderPersonId,
                created.InstanceId,
                $"formation of {businessLabel}: created as {created.BusinessEntityKey} through the generic " +
                $"BIZ-1 flow (owner: {created.OwnerName}); entity exists, NOT operating — commerce comes later.");

            // Step 3: register hires through the real employment authorities.
            foreach (NpcFormationHire hire in intent.Hires)
            {
                var relationship = new EmploymentRelationship
                {
                    Id = $"emp-{created.InstanceId}-{hire.PersonId}-{hire.RoleId}",
                    EmployeePersonId = hire.PersonId,
                    EmployerBusinessId = created.InstanceId,
                    RoleDisplayName = hire.RoleId,
                    Compensation = CompensationTerms.FromWeeklyWage(hire.AgreedWeeklyWageCents, "NPC formation hire"),
                    StartDayIndex = Math.Max(day, hire.StartDayIndex),
                    EndDayIndex = -1,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Source = EmploymentSource.NpcFormation,
                };
                string refusal = context.Employments.Register(relationship);
                if (refusal != null)
                {
                    return AbortWithRefund(intent, context, result, totalOut,
                        $"hire registration failed: {refusal}");
                }

                result.EmploymentIds.Add(relationship.Id);
                context.Events?.Record(day, NpcBusinessEventKind.FormationHireRegistered, intent.FounderPersonId,
                    created.InstanceId,
                    $"P{hire.PersonId} hired as '{hire.RoleId}' at {hire.AgreedWeeklyWageCents}c/week " +
                    $"({relationship.Id}) — real employment, payroll-visible.");
            }

            // Step 4: record assets with provenance (upstream-provenance doctrine).
            if (context.AssetRegister == null)
            {
                return AbortWithRefund(intent, context, result, totalOut, "no asset register — provenance cannot be recorded.");
            }

            foreach (NpcFormationAsset asset in intent.Assets)
            {
                string refusal = context.AssetRegister.Record(asset);
                if (refusal != null)
                {
                    return AbortWithRefund(intent, context, result, totalOut,
                        $"asset recording failed: {refusal}");
                }

                context.Events?.Record(day, NpcBusinessEventKind.FormationAssetRecorded, intent.FounderPersonId,
                    created.InstanceId,
                    $"asset recorded: {asset.Units}x '{asset.ItemId}' ({asset.AssetKind}), {asset.CostCents}c, provenance '{asset.Provenance}'.");
            }

            // Step 5: agreements through the real authorities (recorded here;
            // the authorities themselves executed them during investigation).
            foreach (NpcFormationAgreement agreement in intent.Agreements)
            {
                result.AgreementIds.Add(agreement.AgreementId);
                context.Events?.Record(day, NpcBusinessEventKind.FormationAgreementRecorded, intent.FounderPersonId,
                    created.InstanceId,
                    $"{agreement.AgreementKind} agreement '{agreement.AgreementId}' with {agreement.Counterparty}: {agreement.Summary}");
            }

            // Step 6: opening conditions — genuinely operable before trading.
            var openingProblems = new List<string>();
            CheckOpeningConditions(intent, openingProblems);
            if (openingProblems.Count > 0)
            {
                return AbortWithRefund(intent, context, result, totalOut,
                    $"opening conditions failed after creation: {string.Join(" | ", openingProblems)}");
            }

            result.Success = true;
            context.Events?.Record(day, NpcBusinessEventKind.FormationReady, intent.FounderPersonId,
                created.InstanceId,
                $"formation of {businessLabel} COMPLETE: {created.InstanceId} is staffed, stocked and capable — " +
                $"ready to trade. Total formation cost {totalOut}c, every cent traceable to household {intent.FounderHouseholdId}.");
            return result;
        }

        private string MoveFormationCash(NpcBusinessFormationIntent intent, NpcFormationContext context,
            NpcBusinessFormationResult result, ref int totalOut)
        {
            int day = context.DayIndex;
            HouseholdLedger ledger = context.HouseholdLedger;

            int premisesCost = intent.Premises != null ? Math.Max(0, intent.Premises.UpfrontCostCents) : 0;
            if (premisesCost > 0)
            {
                string refusal = ledger.RecordOutflow(day, premisesCost,
                    $"formation premises ({intent.Premises.Tenure}) for '{intent.DisplayName}'",
                    intent.Premises.AgreementId);
                if (refusal != null) return refusal;
                result.PremisesCostCents = premisesCost;
                totalOut += premisesCost;
            }

            if (intent.Assets != null)
            {
                foreach (NpcFormationAsset asset in intent.Assets)
                {
                    int cost = Math.Max(0, asset.CostCents);
                    if (cost <= 0) continue;
                    string refusal = ledger.RecordOutflow(day, cost,
                        $"formation {asset.AssetKind} {asset.Units}x '{asset.ItemId}' for '{intent.DisplayName}'",
                        asset.Provenance);
                    if (refusal != null) return refusal;
                    if (string.Equals(asset.AssetKind, "equipment", StringComparison.OrdinalIgnoreCase))
                    {
                        result.EquipmentCostCents += cost;
                    }
                    else
                    {
                        result.InventoryCostCents += cost;
                    }

                    totalOut += cost;
                }
            }

            int workingCapital = Math.Max(0, intent.WorkingCapitalCents);
            if (workingCapital > 0)
            {
                string refusal = ledger.RecordOutflow(day, workingCapital,
                    $"formation working capital for '{intent.DisplayName}'",
                    "business:" + intent.DisplayName);
                if (refusal != null) return refusal;
                result.WorkingCapitalCents = workingCapital;
                totalOut += workingCapital;
            }

            return null;
        }

        private NpcBusinessFormationResult AbortWithRefund(NpcBusinessFormationIntent intent,
            NpcFormationContext context, NpcBusinessFormationResult result, int totalOut, string reason)
        {
            result.Success = false;
            result.FailureReason = reason;
            result.Diagnostics.Add(reason);

            // Unwind: every cent that left returns, documented. No partial state.
            if (totalOut > 0 && context.HouseholdLedger != null)
            {
                string refundRefusal = context.HouseholdLedger.RecordInflow(
                    context.DayIndex, totalOut, HouseholdIncomeSource.OtherDocumented,
                    "formation-abort", $"formation of '{intent.DisplayName}' aborted — capital returned", "formation");
                result.CashRefunded = refundRefusal == null;
                result.Diagnostics.Add(refundRefusal == null
                    ? $"refunded {totalOut}c to household {intent.FounderHouseholdId}."
                    : $"REFUND FAILED: {refundRefusal}");
            }

            // Precision about what stands: the BIZ-1 creation itself is
            // atomic (the authority never half-creates), so an abort AFTER
            // creation leaves the entity standing as a non-operating shell —
            // recorded here, never silent. Cash is always refunded.
            string standing = result.CreatedBusiness != null
                ? $" Entity {result.CreatedBusiness.BusinessEntityKey} stands as a non-operating shell; the formation did not complete."
                : " No business entity was created.";
            context.Events?.Record(context.DayIndex, NpcBusinessEventKind.FormationAborted,
                intent.FounderPersonId, result.CreatedBusiness?.InstanceId ?? string.Empty,
                $"formation of '{intent.DisplayName}' ABORTED: {reason} " +
                (result.CashRefunded ? $"({totalOut}c refunded.{standing})" : $"(no cash had moved.{standing})"));
            return result;
        }
    }
}
