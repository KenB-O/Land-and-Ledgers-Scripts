using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// BIZ-1 (addendum): the premises section of the Create Business form.
    /// GHOST-DES-031 locks the flow: Businesses → Create Business → Choose Business
    /// Type → Premises, where Premises offers: No premises required / Use owned
    /// property / Lease property / Acquire property. This adds Kennedy's extensions:
    /// use existing compatible space (house room, barn, yard, shared) and mobile route.
    /// </summary>
    public enum PremisesMode
    {
        Unspecified = 0,

        /// <summary>No dedicated premises required (Canon §3.1, GHOST-DES-031).</summary>
        NoPremisesRequired = 1,

        /// <summary>Use an owned parcel; the dropdown lists ALL owned parcels including
        /// ones already used by another business (GHOST-DES-031) — if occupied, the
        /// player assigns actual functional space, never blocks the parcel.</summary>
        UseOwnedProperty = 2,

        /// <summary>Lease a property (GHOST-DES-031).</summary>
        LeaseProperty = 3,

        /// <summary>Acquire (buy) a property as part of formation (GHOST-DES-031).</summary>
        AcquireProperty = 4,

        /// <summary>Kennedy's extension: use existing compatible space — a house room,
        /// barn space, yard area, or shared premises with a real space assignment
        /// (Canon §3.3: one building, several economic uses).</summary>
        UseExistingCompatibleSpace = 5,

        /// <summary>Kennedy's extension: mobile route operation, no fixed site.</summary>
        MobileRoute = 6,
    }

    /// <summary>
    /// BIZ-1 (addendum): the compatible-space variant for
    /// <see cref="PremisesMode.UseExistingCompatibleSpace"/>.
    /// </summary>
    public enum CompatibleSpaceKind
    {
        Unspecified = 0,
        HouseRoom = 1,
        BarnSpace = 2,
        YardArea = 3,
        SharedPremises = 4,
    }

    /// <summary>
    /// BIZ-1 (addendum): plain-C# premises section of the Create Business form.
    /// UI-agnostic: the Unity view binds its dropdowns/fields to this.
    /// </summary>
    [Serializable]
    public sealed class CreateBusinessPremisesSection
    {
        [SerializeField]
        private PremisesMode mode = PremisesMode.Unspecified;

        [SerializeField]
        private CompatibleSpaceKind compatibleSpaceKind = CompatibleSpaceKind.Unspecified;

        [SerializeField]
        private string selectedPropertyId = string.Empty;

        [SerializeField]
        [TextArea(1, 3)]
        [Tooltip("GHOST-DES-031: when the property is already occupied, the actual functional space assigned (rooms, floor, storefront, yard, outbuilding).")]
        private string functionalSpaceAssignment = string.Empty;

        [SerializeField]
        private bool propertyAlreadyOccupied;

        [SerializeField, Min(0)]
        private int leaseOrPurchasePriceCents;

        public PremisesMode Mode => mode;
        public CompatibleSpaceKind CompatibleSpaceKind => compatibleSpaceKind;
        public string SelectedPropertyId => selectedPropertyId ?? string.Empty;
        public string FunctionalSpaceAssignment => functionalSpaceAssignment ?? string.Empty;
        public bool PropertyAlreadyOccupied => propertyAlreadyOccupied;
        public int LeaseOrPurchasePriceCents => Mathf.Max(0, leaseOrPurchasePriceCents);

        public void SetMode(PremisesMode value) => mode = value;
        public void SetCompatibleSpaceKind(CompatibleSpaceKind value) => compatibleSpaceKind = value;
        public void SetSelectedPropertyId(string value) => selectedPropertyId = value ?? string.Empty;
        public void SetFunctionalSpaceAssignment(string value) => functionalSpaceAssignment = value ?? string.Empty;
        public void SetPropertyAlreadyOccupied(bool value) => propertyAlreadyOccupied = value;
        public void SetLeaseOrPurchasePriceCents(int value) => leaseOrPurchasePriceCents = Mathf.Max(0, value);
    }

    /// <summary>
    /// BIZ-1 (addendum): one row of the ownership section (GHOST-DES-035). Ownership %,
    /// capital contribution, profit/loss share, debt exposure, and managing authority
    /// are separate settings. The business ledger records whole obligations; the debt
    /// exposure share is the internal economic split only.
    /// </summary>
    [Serializable]
    public sealed class CreateBusinessOwnerRow
    {
        [SerializeField]
        private BusinessOwnerIdentity owner = BusinessOwnerIdentity.Player();

        [SerializeField, Range(0f, 1f)]
        private float ownershipPercent01 = 1f;

        [SerializeField, Min(0)]
        private int capitalContributionCents;

        [SerializeField, Range(0f, 1f)]
        private float profitShare01 = 1f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("GHOST-DES-035: internal economic share of business debt. The ledger records the whole obligation.")]
        private float debtExposureShare01 = 1f;

        [SerializeField]
        private bool hasManagingAuthority = true;

        public BusinessOwnerIdentity Owner => owner ?? BusinessOwnerIdentity.Player();
        public float OwnershipPercent01 => Mathf.Clamp01(ownershipPercent01);
        public int CapitalContributionCents => Mathf.Max(0, capitalContributionCents);
        public float ProfitShare01 => Mathf.Clamp01(profitShare01);
        public float DebtExposureShare01 => Mathf.Clamp01(debtExposureShare01);
        public bool HasManagingAuthority => hasManagingAuthority;

        public void SetOwner(BusinessOwnerIdentity value) => owner = value ?? BusinessOwnerIdentity.Player();
        public void SetOwnershipPercent(float value) => ownershipPercent01 = Mathf.Clamp01(value);
        public void SetCapitalContributionCents(int value) => capitalContributionCents = Mathf.Max(0, value);
        public void SetProfitShare(float value) => profitShare01 = Mathf.Clamp01(value);
        public void SetDebtExposureShare(float value) => debtExposureShare01 = Mathf.Clamp01(value);
        public void SetHasManagingAuthority(bool value) => hasManagingAuthority = value;

        public OwnershipShare ToOwnershipShare()
        {
            return new OwnershipShare(owner, ownershipPercent01, capitalContributionCents, profitShare01, hasManagingAuthority);
        }
    }

    /// <summary>
    /// BIZ-1 (addendum): known capability → premises-requirement hints used by form
    /// validation until BIZ-2 ships real capability definitions. Extension point:
    /// <see cref="RegisterHint"/>. Deliberately keyed by CAPABILITY, never by
    /// BusinessType (Tech X §3.2).
    /// </summary>
    public static class CapabilityPremisesHints
    {
        private static readonly Dictionary<string, PremisesRequirement> hints =
            new Dictionary<string, PremisesRequirement>(StringComparer.OrdinalIgnoreCase)
            {
                // Retail / customer-facing trade.
                { "retail", new PremisesRequirement(customerFacing: true, minAreaSqFt: 400) },
                { "general_goods", new PremisesRequirement(customerFacing: true, minAreaSqFt: 600) },
                { "food_service", new PremisesRequirement(customerFacing: true, foodHandling: true, minAreaSqFt: 400) },
                // Production / craft.
                { "slaughter", new PremisesRequirement(foodHandling: true, yardStorage: true, minAreaSqFt: 800) },
                { "butchery", new PremisesRequirement(customerFacing: true, foodHandling: true, minAreaSqFt: 500) },
                { "tanning", new PremisesRequirement(workshop: true, yardStorage: true, minAreaSqFt: 600) },
                { "blacksmithing", new PremisesRequirement(workshop: true, yardStorage: true, minAreaSqFt: 600) },
                { "wagon_repair", new PremisesRequirement(workshop: true, yardStorage: true, minAreaSqFt: 1000) },
                { "milling", new PremisesRequirement(workshop: true, minAreaSqFt: 800) },
                { "baking", new PremisesRequirement(foodHandling: true, minAreaSqFt: 400) },
                // Land / animals.
                { "animal_housing", new PremisesRequirement(animalHousing: true, minAreaSqFt: 2000) },
                { "crop_production", new PremisesRequirement(minAreaSqFt: 20000) },
                { "livery", new PremisesRequirement(animalHousing: true, yardStorage: true, minAreaSqFt: 2000) },
                // Movement.
                { "freight_haul", new PremisesRequirement(yardStorage: true, minAreaSqFt: 1500) },
                { "route_trade", new PremisesRequirement() },
                // Services needing only a room.
                { "lodging", new PremisesRequirement(customerFacing: true, minAreaSqFt: 1200) },
                { "personal_service", new PremisesRequirement(customerFacing: true, minAreaSqFt: 200) },
                { "medical", new PremisesRequirement(customerFacing: true, minAreaSqFt: 400) },
            };

        public static void RegisterHint(string capabilityId, PremisesRequirement requirement)
        {
            if (string.IsNullOrWhiteSpace(capabilityId) || requirement == null)
            {
                return;
            }

            hints[capabilityId] = requirement;
        }

        public static bool TryGetHint(string capabilityId, out PremisesRequirement requirement)
        {
            requirement = null;
            return !string.IsNullOrWhiteSpace(capabilityId) && hints.TryGetValue(capabilityId, out requirement);
        }

        /// <summary>
        /// Merges the requirements of all selected capabilities (union: any capability's
        /// need becomes the business's need). Unknown capability ids contribute nothing
        /// and are reported via <paramref name="unknownCapabilityIds"/>.
        /// </summary>
        public static PremisesRequirement MergeForCapabilities(
            IEnumerable<string> capabilityIds,
            List<string> unknownCapabilityIds)
        {
            bool customerFacing = false, workshop = false, animalHousing = false;
            bool yardStorage = false, foodHandling = false;
            int minArea = 0;

            if (capabilityIds != null)
            {
                foreach (string id in capabilityIds)
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    if (!TryGetHint(id, out PremisesRequirement hint))
                    {
                        unknownCapabilityIds?.Add(id);
                        continue;
                    }

                    customerFacing |= hint.NeedsCustomerFacingSpace;
                    workshop |= hint.NeedsWorkshopSpace;
                    animalHousing |= hint.NeedsAnimalHousing;
                    yardStorage |= hint.NeedsYardStorage;
                    foodHandling |= hint.NeedsFoodHandling;
                    minArea = Math.Max(minArea, hint.MinimumAreaSqFt);
                }
            }

            return new PremisesRequirement(customerFacing, workshop, animalHousing, yardStorage, foodHandling, minArea);
        }
    }

    /// <summary>
    /// BIZ-1 (addendum): the UI-agnostic Create Business form model (GHOST-DES-029).
    /// Plain C# data + validation. The Unity view binds its dropdowns/fields to this;
    /// the presenter converts a valid form into a <see cref="CreateBusinessIntent"/>
    /// and runs the canonical workflow. No Unity UI code here — Kennedy builds the
    /// visual form against <see cref="ICreateBusinessView"/>.
    /// </summary>
    [Serializable]
    public sealed class CreateBusinessFormModel
    {
        [SerializeField]
        private BusinessType businessType = BusinessType.GeneralStore;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField]
        private CreateBusinessPremisesSection premises = new CreateBusinessPremisesSection();

        [SerializeField]
        private List<CreateBusinessOwnerRow> ownerRows = new List<CreateBusinessOwnerRow>();

        [SerializeField]
        private List<string> capabilityIds = new List<string>();

        [SerializeField, Min(0)]
        private int workingCapitalCents;

        public BusinessType BusinessType => businessType;
        public string DisplayName => displayName ?? string.Empty;
        public CreateBusinessPremisesSection Premises => premises ??= new CreateBusinessPremisesSection();
        public IReadOnlyList<CreateBusinessOwnerRow> OwnerRows => ownerRows;
        public IReadOnlyList<string> CapabilityIds => capabilityIds;
        public int WorkingCapitalCents => Mathf.Max(0, workingCapitalCents);

        public void SetBusinessType(BusinessType value) => businessType = value;
        public void SetDisplayName(string value) => displayName = value ?? string.Empty;
        public void SetWorkingCapitalCents(int value) => workingCapitalCents = Mathf.Max(0, value);

        public void AddOwnerRow(CreateBusinessOwnerRow row)
        {
            ownerRows ??= new List<CreateBusinessOwnerRow>();
            if (row != null)
            {
                ownerRows.Add(row);
            }
        }

        public void ClearOwnerRows()
        {
            ownerRows?.Clear();
        }

        public void AddCapability(string capabilityId)
        {
            capabilityIds ??= new List<string>();
            if (!string.IsNullOrWhiteSpace(capabilityId) && !capabilityIds.Contains(capabilityId))
            {
                capabilityIds.Add(capabilityId);
            }
        }

        public void RemoveCapability(string capabilityId)
        {
            capabilityIds?.Remove(capabilityId);
        }

        /// <summary>
        /// Full form validation with human-readable errors. Enforces the canon premises
        /// rules: capability-driven premises (Tech X §3.2) — the chosen premises mode
        /// must satisfy the merged capability requirements, or the error says exactly
        /// what is missing (e.g. "slaughter capability needs yard space with sanitation").
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add("Give the business a name.");
            }

            // Ownership (GHOST-DES-035): at least one owner, shares total 100%, a manager.
            if (ownerRows == null || ownerRows.Count == 0)
            {
                errors.Add("Add at least one owner — a business is always owned by someone (Canon §3.1).");
            }
            else
            {
                float total = 0f;
                bool anyManager = false;
                foreach (CreateBusinessOwnerRow row in ownerRows)
                {
                    total += row.OwnershipPercent01;
                    anyManager |= row.HasManagingAuthority;
                }

                if (Mathf.Abs(total - 1f) > 0.001f)
                {
                    errors.Add($"Ownership adds up to {total:P0} — it must total 100% (Canon §3.5).");
                }

                if (!anyManager)
                {
                    errors.Add("Name a managing owner — someone must hold managing authority (Canon §3.5).");
                }
            }

            // Capabilities → requirements (capability-driven, never type-driven).
            var unknown = new List<string>();
            PremisesRequirement requirement = CapabilityPremisesHints.MergeForCapabilities(capabilityIds, unknown);
            foreach (string id in unknown)
            {
                errors.Add($"Unknown capability '{id}' — pick from the capability list.");
            }

            // Premises mode chosen?
            if (premises == null || premises.Mode == PremisesMode.Unspecified)
            {
                errors.Add("Choose how this business is housed: no premises, owned property, leased, acquired, compatible space, or mobile route (GHOST-DES-031).");
                return errors;
            }

            // The canon premises rules, in plain language.
            switch (premises.Mode)
            {
                case PremisesMode.NoPremisesRequired:
                    if (!requirement.IsSiteFree)
                    {
                        errors.Add($"No-premises won't work here: {DescribeUnmetNeeds(requirement)} " +
                            "Pick a premises option that fits the work (Tech X §3.2).");
                    }
                    break;

                case PremisesMode.MobileRoute:
                    if (requirement.NeedsCustomerFacingSpace || requirement.NeedsWorkshopSpace
                        || requirement.NeedsAnimalHousing || requirement.NeedsFoodHandling)
                    {
                        errors.Add($"A mobile route can't satisfy: {DescribeUnmetNeeds(requirement)} " +
                            "Mobile operation fits route trade, not fixed-site work (Tech X §3.2).");
                    }
                    break;

                case PremisesMode.UseOwnedProperty:
                case PremisesMode.LeaseProperty:
                case PremisesMode.AcquireProperty:
                    if (string.IsNullOrWhiteSpace(premises.SelectedPropertyId))
                    {
                        errors.Add("Pick a property for the business to occupy (GHOST-DES-031).");
                    }

                    if (premises.PropertyAlreadyOccupied
                        && string.IsNullOrWhiteSpace(premises.FunctionalSpaceAssignment))
                    {
                        errors.Add("That property already hosts a business — assign actual functional space " +
                            "(rooms, floor, yard, outbuilding) instead of blocking the parcel (GHOST-DES-031).");
                    }
                    break;

                case PremisesMode.UseExistingCompatibleSpace:
                    if (premises.CompatibleSpaceKind == CompatibleSpaceKind.Unspecified)
                    {
                        errors.Add("Say which compatible space the business uses: house room, barn, yard, or shared premises.");
                    }
                    else
                    {
                        string mismatch = CheckCompatibleSpaceFit(premises.CompatibleSpaceKind, requirement);
                        if (!string.IsNullOrEmpty(mismatch))
                        {
                            errors.Add(mismatch);
                        }
                    }
                    break;
            }

            return errors;
        }

        /// <summary>Converts a VALID form into the canonical creation intent.</summary>
        public CreateBusinessIntent ToIntent()
        {
            var unknown = new List<string>();
            PremisesRequirement requirement =
                CapabilityPremisesHints.MergeForCapabilities(capabilityIds, unknown);

            var shares = new List<OwnershipShare>();
            int totalCapital = workingCapitalCents;
            if (ownerRows != null)
            {
                foreach (CreateBusinessOwnerRow row in ownerRows)
                {
                    shares.Add(row.ToOwnershipShare());
                    totalCapital += row.CapitalContributionCents;
                }
            }

            PremisesPreference preference = premises.Mode switch
            {
                PremisesMode.UseExistingCompatibleSpace => PremisesPreference.PreferShared,
                PremisesMode.MobileRoute => PremisesPreference.PreferMobile,
                PremisesMode.NoPremisesRequired => PremisesPreference.NoneRequired,
                _ => PremisesPreference.Auto,
            };

            return new CreateBusinessIntent(
                businessType,
                displayName,
                new BusinessOwnership(shares),
                requirement,
                preference,
                totalCapital);
        }

        private static string DescribeUnmetNeeds(PremisesRequirement requirement)
        {
            var needs = new List<string>();
            if (requirement.NeedsCustomerFacingSpace) needs.Add("customer-facing space");
            if (requirement.NeedsWorkshopSpace) needs.Add("workshop space");
            if (requirement.NeedsAnimalHousing) needs.Add("animal housing");
            if (requirement.NeedsYardStorage) needs.Add("yard storage");
            if (requirement.NeedsFoodHandling) needs.Add("food-handling space");
            if (requirement.MinimumAreaSqFt > 0) needs.Add($"{requirement.MinimumAreaSqFt} sq ft");
            return needs.Count > 0 ? string.Join(", ", needs) : "site requirements";
        }

        private static string CheckCompatibleSpaceFit(CompatibleSpaceKind kind, PremisesRequirement requirement)
        {
            // Plain-language fit checks: the chosen compatible space must satisfy the
            // merged capability requirements (Tech X §3.2).
            switch (kind)
            {
                case CompatibleSpaceKind.HouseRoom:
                    if (requirement.NeedsAnimalHousing)
                        return "A house room can't house animals — use barn space (Tech X §3.2).";
                    if (requirement.NeedsYardStorage)
                        return "A house room has no yard storage — use a yard area (Tech X §3.2).";
                    if (requirement.NeedsFoodHandling && requirement.MinimumAreaSqFt > 600)
                        return "Food handling at this scale needs more than a house room (Tech X §3.2).";
                    break;
                case CompatibleSpaceKind.BarnSpace:
                    if (requirement.NeedsCustomerFacingSpace)
                        return "A barn isn't customer-facing — use a storefront or shared premises (Tech X §3.2).";
                    break;
                case CompatibleSpaceKind.YardArea:
                    if (requirement.NeedsCustomerFacingSpace)
                        return "A yard area isn't customer-facing — use a storefront or shared premises (Tech X §3.2).";
                    if (requirement.NeedsFoodHandling)
                        return "Food handling needs an enclosed space, not an open yard (Tech X §3.2).";
                    break;
                case CompatibleSpaceKind.SharedPremises:
                    // Shared premises can host anything with a real space assignment (Canon §3.3).
                    break;
            }

            return null;
        }
    }
}
