using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// BIZ-1: capability-driven premises kinds (Tech X §3.2). The resolution NEVER comes
    /// from a BusinessType→building map — it comes from what the business's tasks and
    /// equipment actually require. This is Kennedy's "sometimes brick and mortar,
    /// sometimes not."
    /// </summary>
    public enum PremisesKind
    {
        Unspecified = 0,

        /// <summary>No dedicated premises required (Canon §3.1). Mobile/route or
        /// correspondence-style operation.</summary>
        NoDedicatedPremises = 1,

        /// <summary>Operates from a room in a house (e.g. tailor, barber starting out).</summary>
        HouseRoom = 2,

        /// <summary>Dedicated customer-facing storefront.</summary>
        DedicatedStorefront = 3,

        /// <summary>Finished barn space (animals, storage, workshop).</summary>
        BarnSpace = 4,

        /// <summary>Yard / shop area (lumber, wagons, repair).</summary>
        YardShopArea = 5,

        /// <summary>Shares premises with another business under a real space assignment.</summary>
        SharedPremises = 6,

        /// <summary>Mobile route operation (wagon route, no fixed site).</summary>
        MobileRoute = 7,
    }

    /// <summary>
    /// BIZ-1: the player's premises preference. A HINT to the resolver, never a command —
    /// capability requirements always win over preference (Tech X §3.2).
    /// </summary>
    public enum PremisesPreference
    {
        Auto = 0,
        PreferDedicated = 1,
        PreferShared = 2,
        PreferHomeBased = 3,
        PreferMobile = 4,

        /// <summary>Player asserts no premises are needed; the resolver still verifies
        /// the task/equipment requirements agree.</summary>
        NoneRequired = 5,
    }

    /// <summary>
    /// BIZ-1: what the business's tasks and equipment demand from a site. Supplied by
    /// the business's capabilities (BIZ-2); for BIZ-1 the creation intent carries it
    /// directly. Deliberately NOT keyed by BusinessType.
    /// </summary>
    [Serializable]
    public sealed class PremisesRequirement
    {
        [SerializeField]
        private bool needsCustomerFacingSpace;

        [SerializeField]
        private bool needsWorkshopSpace;

        [SerializeField]
        private bool needsAnimalHousing;

        [SerializeField]
        private bool needsYardStorage;

        [SerializeField]
        private bool needsFoodHandling;

        [SerializeField, Min(0)]
        private int minimumAreaSqFt;

        public bool NeedsCustomerFacingSpace => needsCustomerFacingSpace;
        public bool NeedsWorkshopSpace => needsWorkshopSpace;
        public bool NeedsAnimalHousing => needsAnimalHousing;
        public bool NeedsYardStorage => needsYardStorage;
        public bool NeedsFoodHandling => needsFoodHandling;
        public int MinimumAreaSqFt => Mathf.Max(0, minimumAreaSqFt);

        public PremisesRequirement(
            bool customerFacing = false,
            bool workshop = false,
            bool animalHousing = false,
            bool yardStorage = false,
            bool foodHandling = false,
            int minAreaSqFt = 0)
        {
            needsCustomerFacingSpace = customerFacing;
            needsWorkshopSpace = workshop;
            needsAnimalHousing = animalHousing;
            needsYardStorage = yardStorage;
            needsFoodHandling = foodHandling;
            minimumAreaSqFt = Mathf.Max(0, minAreaSqFt);
        }

        /// <summary>True when nothing about the work demands any physical site.</summary>
        public bool IsSiteFree =>
            !needsCustomerFacingSpace && !needsWorkshopSpace && !needsAnimalHousing
            && !needsYardStorage && !needsFoodHandling && minimumAreaSqFt <= 0;
    }

    /// <summary>
    /// BIZ-1: the resolved premises for a business — kind, concrete assignment, and a
    /// human-readable derivation trail (capability-driven, never type-mapped).
    /// </summary>
    [Serializable]
    public sealed class PremisesResolution
    {
        [SerializeField]
        private PremisesKind kind;

        [SerializeField]
        private int buildingId = -1;

        [SerializeField]
        private string derivationNotes = string.Empty;

        public PremisesKind Kind => kind;
        public int BuildingId => buildingId;
        public string DerivationNotes => derivationNotes ?? string.Empty;

        public PremisesResolution(PremisesKind kind, int buildingId, string derivationNotes)
        {
            this.kind = kind;
            this.buildingId = buildingId;
            this.derivationNotes = derivationNotes ?? string.Empty;
        }
    }

    /// <summary>
    /// BIZ-1: capability-driven premises resolver (Tech X §3.2). Requirements drive the
    /// kind; the player's preference only selects among equally-valid options or is
    /// overruled with a logged reason when requirements disagree.
    /// </summary>
    public static class PremisesResolver
    {
        public static PremisesResolution Resolve(
            PremisesRequirement requirement,
            PremisesPreference preference,
            int assignedBuildingId,
            List<string> diagnostics)
        {
            requirement ??= new PremisesRequirement();
            diagnostics ??= new List<string>();

            // Hard requirements first — these overrule any preference.
            if (requirement.NeedsAnimalHousing)
            {
                diagnostics.Add("Animal housing required by task/equipment needs -> BarnSpace (preference overruled).");
                return new PremisesResolution(PremisesKind.BarnSpace, assignedBuildingId,
                    "BarnSpace: animal housing is a hard task requirement (Tech X §3.2).");
            }

            if (requirement.NeedsYardStorage && !requirement.NeedsCustomerFacingSpace)
            {
                diagnostics.Add("Yard storage required, no customer foot traffic -> YardShopArea.");
                return new PremisesResolution(PremisesKind.YardShopArea, assignedBuildingId,
                    "YardShopArea: yard storage required, no customer-facing need (Tech X §3.2).");
            }

            if (requirement.NeedsCustomerFacingSpace)
            {
                if (preference == PremisesPreference.PreferShared)
                {
                    diagnostics.Add("Customer-facing + PreferShared -> SharedPremises (real space assignment required).");
                    return new PremisesResolution(PremisesKind.SharedPremises, assignedBuildingId,
                        "SharedPremises: customer-facing need met inside shared premises (Canon §3.3).");
                }

                if (preference == PremisesPreference.PreferHomeBased && !requirement.NeedsFoodHandling)
                {
                    diagnostics.Add("Customer-facing + PreferHomeBased, no food handling -> HouseRoom.");
                    return new PremisesResolution(PremisesKind.HouseRoom, assignedBuildingId,
                        "HouseRoom: small customer-facing work fits a house room (Tech X §3.2).");
                }

                diagnostics.Add("Customer-facing requirement -> DedicatedStorefront.");
                return new PremisesResolution(PremisesKind.DedicatedStorefront, assignedBuildingId,
                    "DedicatedStorefront: customer foot traffic requires a storefront (Tech X §3.2).");
            }

            if (requirement.NeedsWorkshopSpace || requirement.NeedsFoodHandling)
            {
                if (preference == PremisesPreference.PreferHomeBased)
                {
                    diagnostics.Add("Workshop/food work + PreferHomeBased -> HouseRoom.");
                    return new PremisesResolution(PremisesKind.HouseRoom, assignedBuildingId,
                        "HouseRoom: workshop/food work fits a house room at this scale (Tech X §3.2).");
                }

                diagnostics.Add("Workshop/food-handling requirement -> YardShopArea.");
                return new PremisesResolution(PremisesKind.YardShopArea, assignedBuildingId,
                    "YardShopArea: workshop/food-handling work needs a shop area (Tech X §3.2).");
            }

            // Nothing demands a site.
            if (requirement.IsSiteFree)
            {
                if (preference == PremisesPreference.PreferMobile)
                {
                    diagnostics.Add("No site requirements + PreferMobile -> MobileRoute.");
                    return new PremisesResolution(PremisesKind.MobileRoute, -1,
                        "MobileRoute: route operation, no fixed site (Canon §3.1).");
                }

                diagnostics.Add("No site requirements -> NoDedicatedPremises (Canon §3.1).");
                return new PremisesResolution(PremisesKind.NoDedicatedPremises, -1,
                    "NoDedicatedPremises: task/equipment needs require no fixed site (Canon §3.1).");
            }

            diagnostics.Add("Fallback: requirements present but unclassified -> SharedPremises.");
            return new PremisesResolution(PremisesKind.SharedPremises, assignedBuildingId,
                "SharedPremises: conservative fallback; refine requirements (Tech X §3.2).");
        }
    }

    /// <summary>
    /// BIZ-1: one owner's stake (Canon §3.5). Ownership %, capital contribution,
    /// profit/loss share, and managing authority are separate facts.
    /// </summary>
    [Serializable]
    public sealed class OwnershipShare
    {
        [SerializeField]
        private BusinessOwnerIdentity owner = BusinessOwnerIdentity.Town();

        [SerializeField, Range(0f, 1f)]
        private float ownershipPercent01 = 1f;

        [SerializeField, Min(0)]
        private int capitalContributionCents;

        [SerializeField, Range(0f, 1f)]
        private float profitShare01 = 1f;

        [SerializeField]
        private bool hasManagingAuthority = true;

        public BusinessOwnerIdentity Owner => owner ?? BusinessOwnerIdentity.Town();
        public float OwnershipPercent01 => Mathf.Clamp01(ownershipPercent01);
        public int CapitalContributionCents => Mathf.Max(0, capitalContributionCents);
        public float ProfitShare01 => Mathf.Clamp01(profitShare01);
        public bool HasManagingAuthority => hasManagingAuthority;

        public OwnershipShare(
            BusinessOwnerIdentity owner,
            float ownershipPercent01,
            int capitalContributionCents,
            float profitShare01,
            bool hasManagingAuthority)
        {
            this.owner = owner ?? BusinessOwnerIdentity.Town();
            this.ownershipPercent01 = Mathf.Clamp01(ownershipPercent01);
            this.capitalContributionCents = Mathf.Max(0, capitalContributionCents);
            this.profitShare01 = Mathf.Clamp01(profitShare01);
            this.hasManagingAuthority = hasManagingAuthority;
        }

        public static OwnershipShare Sole(BusinessOwnerIdentity owner, int capitalContributionCents = 0)
        {
            return new OwnershipShare(owner, 1f, capitalContributionCents, 1f, true);
        }
    }

    /// <summary>
    /// BIZ-1: the ownership/governance relationship set for a business (Canon §3.5).
    /// Validates that shares total 100%.
    /// </summary>
    [Serializable]
    public sealed class BusinessOwnership
    {
        [SerializeField]
        private List<OwnershipShare> shares = new List<OwnershipShare>();

        public IReadOnlyList<OwnershipShare> Shares => shares;

        public BusinessOwnership(IEnumerable<OwnershipShare> shares)
        {
            this.shares = shares != null ? new List<OwnershipShare>(shares) : new List<OwnershipShare>();
        }

        public static BusinessOwnership Sole(BusinessOwnerIdentity owner, int capitalContributionCents = 0)
        {
            return new BusinessOwnership(new[] { OwnershipShare.Sole(owner, capitalContributionCents) });
        }

        /// <summary>Human-readable problems; empty means the ownership is valid.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (shares == null || shares.Count == 0)
            {
                problems.Add("Business must have at least one owner (Canon §3.1).");
                return problems;
            }

            float total = 0f;
            bool anyManager = false;
            foreach (OwnershipShare share in shares)
            {
                total += share.OwnershipPercent01;
                anyManager |= share.HasManagingAuthority;
            }

            if (Mathf.Abs(total - 1f) > 0.001f)
            {
                problems.Add($"Ownership shares total {total:P1}; they must total 100% (Canon §3.5).");
            }

            if (!anyManager)
            {
                problems.Add("No share carries managing authority (Canon §3.5).");
            }

            return problems;
        }
    }

    /// <summary>
    /// BIZ-1: the player's/business-founder's intent (Canon §3.1). An intent is a request;
    /// the authority validates and executes it. Any of the 28 BusinessTypes may be created.
    /// </summary>
    [Serializable]
    public sealed class CreateBusinessIntent
    {
        [SerializeField]
        private BusinessType businessType = BusinessType.GeneralStore;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField]
        private BusinessOwnership ownership;

        [SerializeField]
        private PremisesPreference premisesPreference = PremisesPreference.Auto;

        [SerializeField]
        private PremisesRequirement premisesRequirement = new PremisesRequirement();

        [SerializeField, Min(0)]
        private int startingCapitalCents;

        [SerializeField]
        private List<string> capabilityIds = new List<string>();

        public BusinessType BusinessType => businessType;
        public string DisplayName => displayName ?? string.Empty;
        public BusinessOwnership Ownership => ownership;
        public PremisesPreference PremisesPreference => premisesPreference;
        public PremisesRequirement PremisesRequirement => premisesRequirement ?? new PremisesRequirement();
        public int StartingCapitalCents => Mathf.Max(0, startingCapitalCents);
        public IReadOnlyList<string> CapabilityIds => capabilityIds;

        public CreateBusinessIntent(
            BusinessType businessType,
            string displayName,
            BusinessOwnership ownership,
            PremisesRequirement premisesRequirement = null,
            PremisesPreference premisesPreference = PremisesPreference.Auto,
            int startingCapitalCents = 0)
        {
            this.businessType = businessType;
            this.displayName = displayName ?? string.Empty;
            this.ownership = ownership;
            this.premisesRequirement = premisesRequirement ?? new PremisesRequirement();
            this.premisesPreference = premisesPreference;
            this.startingCapitalCents = Mathf.Max(0, startingCapitalCents);
        }

        /// <summary>Human-readable problems; empty means the intent is actionable.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                problems.Add("Business needs a display name (Canon §3.1).");
            }

            if (ownership == null)
            {
                problems.Add("Business needs ownership/governance (Canon §3.1).");
            }
            else
            {
                problems.AddRange(ownership.Validate());
            }

            return problems;
        }
    }

    /// <summary>
    /// BIZ-1: the result of the creation workflow. Note what is NOT here: operating
    /// status. Creation never makes a business operating (Canon §3.2) — commerce does.
    /// </summary>
    public sealed class BusinessCreationResult
    {
        public bool Success { get; }
        public BusinessInstanceState Business { get; }
        public EntityId BusinessEntityId { get; }
        public PremisesResolution Premises { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        public BusinessCreationResult(
            bool success,
            BusinessInstanceState business,
            EntityId businessEntityId,
            PremisesResolution premises,
            List<string> diagnostics)
        {
            Success = success;
            Business = business;
            BusinessEntityId = businessEntityId;
            Premises = premises;
            Diagnostics = diagnostics ?? new List<string>();
        }

        public static BusinessCreationResult Failed(List<string> diagnostics)
        {
            return new BusinessCreationResult(false, null, default, null, diagnostics);
        }
    }

    /// <summary>
    /// BIZ-1: what the creation authority needs from the hosting game. Implemented by
    /// the runtime manager; the authority itself stays testable without Unity.
    /// </summary>
    public interface IBusinessCreationContext
    {
        EntityIdRegistry IdRegistry { get; }
        bool TryGetProfile(BusinessType type, out BusinessProfileDefinition profile);
        BusinessProfileDefinition BuildFallbackProfile(BusinessType type, string displayName);
        bool TryAssignPremises(PremisesKind kind, out int buildingId);
        void LogDiagnostic(string message);
    }

    /// <summary>
    /// BIZ-1: the Canon §3.1 universal workflow as code —
    /// intent → identity → ownership/governance → capability-driven premises →
    /// assets/working capital. Creating a business creates the entity; it does NOT make
    /// it operating (Canon §3.2, GHOST-DES-009). Operating status comes only from actual
    /// commerce, recorded via <see cref="BusinessOperatingLedger"/>.
    /// </summary>
    public sealed class BusinessCreationAuthority
    {
        /// <summary>
        /// Runs the full workflow. Returns false with diagnostics when the intent is
        /// invalid or the world cannot satisfy it — never a half-created business.
        /// </summary>
        public bool TryCreate(
            CreateBusinessIntent intent,
            IBusinessCreationContext context,
            out BusinessCreationResult result)
        {
            var diagnostics = new List<string>();
            result = BusinessCreationResult.Failed(diagnostics);

            if (intent == null)
            {
                diagnostics.Add("No creation intent supplied.");
                return false;
            }

            if (context == null)
            {
                diagnostics.Add("No creation context supplied.");
                return false;
            }

            // Step 1: validate the intent (Canon §3.1 — entity + ownership are required).
            List<string> intentProblems = intent.Validate();
            if (intentProblems.Count > 0)
            {
                diagnostics.AddRange(intentProblems);
                return false;
            }

            // Step 2: allocate identity (HF-1). Never reused, survives save/load.
            EntityId businessId = context.IdRegistry.Allocate(EntityKind.Business);
            diagnostics.Add($"Allocated {businessId} (HF-1, never reused).");

            // Step 3: resolve premises from task/equipment requirements (Tech X §3.2).
            // The preference is a hint; requirements win and say so in the diagnostics.
            int provisionalBuildingId = -1;
            PremisesResolution premises = PremisesResolver.Resolve(
                intent.PremisesRequirement, intent.PremisesPreference, provisionalBuildingId, diagnostics);

            if (premises.Kind != PremisesKind.NoDedicatedPremises
                && premises.Kind != PremisesKind.MobileRoute)
            {
                if (!context.TryAssignPremises(premises.Kind, out int buildingId))
                {
                    diagnostics.Add(
                        $"No suitable site available for {premises.Kind}; creation refused " +
                        "(premises are required by the work, not optional).");
                    return false;
                }

                premises = new PremisesResolution(premises.Kind, buildingId,
                    premises.DerivationNotes + $" Assigned building {buildingId}.");
            }

            // Step 4: profile (authored data when present; code fallback otherwise so any
            // of the 28 BusinessTypes is creatable even without an authored profile).
            if (!context.TryGetProfile(intent.BusinessType, out BusinessProfileDefinition profile)
                || profile == null)
            {
                profile = context.BuildFallbackProfile(intent.BusinessType, intent.DisplayName);
                diagnostics.Add($"No authored profile for {intent.BusinessType}; built fallback profile.");
            }

            // Step 5: build the instance. Ownership/governance from the intent (Canon §3.5).
            BusinessOwnerIdentity primaryOwner = intent.Ownership.Shares[0].Owner;
            string instanceId = $"{profile.Business.BusinessId}_{businessId.Id:000}";
            BusinessInstanceState instance;
            try
            {
                instance = BusinessInstanceState.Create(instanceId, profile, premises.BuildingId, primaryOwner);
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Instance construction failed: {ex.Message}");
                return false;
            }

            // Step 6: working capital assignment. Recorded as the owner's capital
            // contribution; it does NOT make the business operating (Canon §3.2).
            if (intent.StartingCapitalCents > 0)
            {
                diagnostics.Add(
                    $"Assigned {intent.StartingCapitalCents}c working capital " +
                    $"(owner contribution; Canon §3.2 — capital is not commerce).");
            }

            diagnostics.Add(
                $"Created '{intent.DisplayName}' ({intent.BusinessType}) as {businessId}: " +
                "entity exists, NOT operating — operating status requires actual commerce (Canon §3.2).");
            result = new BusinessCreationResult(true, instance, businessId, premises, diagnostics);
            return true;
        }
    }

    /// <summary>
    /// BIZ-1: operating status ledger (Canon §3.2). A business becomes operating when it
    /// CAN perform its activity AND real commerce occurs. Creation, capital assignment,
    /// and intent never mark operating — only <see cref="RecordCommerce"/> does.
    /// Keyed by HF-1 EntityId; persisted through its own save export.
    /// </summary>
    [Serializable]
    public sealed class BusinessOperatingLedger
    {
        [Serializable]
        private sealed class CommerceRecord
        {
            public int firstCommerceDayIndex;
            public string firstCommerceDescription = string.Empty;
        }

        /// <summary>CLN-1: persisted commerce record (save pipeline).</summary>
        [Serializable]
        public sealed class CommerceExport
        {
            public string businessKey = string.Empty;
            public int firstCommerceDayIndex;
            public string firstCommerceDescription = string.Empty;
        }

        [SerializeField]
        private List<string> keys = new List<string>();

        [SerializeField]
        private List<CommerceRecord> records = new List<CommerceRecord>();

        /// <summary>
        /// Records real commerce for a business. THIS is what makes it operating —
        /// the only path (Canon §3.2, GHOST-DES-009).
        /// </summary>
        public void RecordCommerce(EntityId businessId, int dayIndex, string description)
        {
            string key = businessId.ToString();
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] == key)
                {
                    return; // First commerce already recorded; operating since then.
                }
            }

            keys.Add(key);
            records.Add(new CommerceRecord
            {
                firstCommerceDayIndex = dayIndex,
                firstCommerceDescription = description ?? string.Empty,
            });
        }

        /// <summary>True only after real commerce has been recorded. Never true at creation.</summary>
        public bool IsOperating(EntityId businessId)
        {
            string key = businessId.ToString();
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] == key)
                {
                    return true;
                }
            }

            return false;
        }

        public int OperatingBusinessCount => keys != null ? keys.Count : 0;

        /// <summary>
        /// CLN-1: captures operating status for the save pipeline. First-commerce
        /// records only — operating status is never synthesized on load.
        /// </summary>
        public OperatingLedgerSaveDto CaptureSaveDto()
        {
            var dto = new OperatingLedgerSaveDto();
            if (keys != null && records != null)
            {
                for (int i = 0; i < keys.Count && i < records.Count; i++)
                {
                    CommerceRecord record = records[i];
                    if (record == null)
                    {
                        continue;
                    }

                    dto.records.Add(new CommerceExport
                    {
                        businessKey = keys[i] ?? string.Empty,
                        firstCommerceDayIndex = record.firstCommerceDayIndex,
                        firstCommerceDescription = record.firstCommerceDescription,
                    });
                }
            }

            return dto;
        }

        /// <summary>CLN-1: restores operating status from the save pipeline.</summary>
        public void LoadFromSaveDto(OperatingLedgerSaveDto dto)
        {
            keys.Clear();
            records.Clear();
            if (dto == null || dto.records == null)
            {
                return;
            }

            foreach (CommerceExport exported in dto.records)
            {
                if (exported == null || string.IsNullOrWhiteSpace(exported.businessKey))
                {
                    continue;
                }

                keys.Add(exported.businessKey);
                records.Add(new CommerceRecord
                {
                    firstCommerceDayIndex = exported.firstCommerceDayIndex,
                    firstCommerceDescription = exported.firstCommerceDescription ?? string.Empty,
                });
            }
        }
    }
}
