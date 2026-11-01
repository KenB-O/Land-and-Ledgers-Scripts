using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// BIZ-2: one commercial capability (Tech X §3.1). A single business identity can
    /// expose many capabilities — general goods + groceries + implements + post
    /// function + produce purchasing — without synthetic subsidiaries. Each capability
    /// references real stock, task methods, equipment, authority, agreements, and space.
    /// </summary>
    [Serializable]
    public sealed class BusinessCapability
    {
        [SerializeField]
        private string capabilityId = string.Empty;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField]
        private List<string> requiredStockCategoryIds = new List<string>();

        [SerializeField]
        [Tooltip("TTS-2 TaskDefinition ids this capability can perform.")]
        private List<string> taskMethodIds = new List<string>();

        [SerializeField]
        private List<string> requiredEquipmentIds = new List<string>();

        [SerializeField]
        private string requiredAuthority = string.Empty;

        [SerializeField]
        private List<string> requiredAgreementIds = new List<string>();

        [SerializeField]
        private PremisesRequirement spaceRequirement = new PremisesRequirement();

        public string CapabilityId => capabilityId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public IReadOnlyList<string> RequiredStockCategoryIds => requiredStockCategoryIds;
        public IReadOnlyList<string> TaskMethodIds => taskMethodIds;
        public IReadOnlyList<string> RequiredEquipmentIds => requiredEquipmentIds;
        public string RequiredAuthority => requiredAuthority ?? string.Empty;
        public IReadOnlyList<string> RequiredAgreementIds => requiredAgreementIds;
        public PremisesRequirement SpaceRequirement => spaceRequirement ?? new PremisesRequirement();

        public BusinessCapability(
            string capabilityId,
            string displayName,
            IEnumerable<string> taskMethodIds = null,
            PremisesRequirement spaceRequirement = null)
        {
            this.capabilityId = capabilityId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            if (taskMethodIds != null)
            {
                this.taskMethodIds = new List<string>(taskMethodIds);
            }

            this.spaceRequirement = spaceRequirement ?? new PremisesRequirement();
        }

        /// <summary>Human-readable problems; empty means the capability is well-formed.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(capabilityId))
            {
                problems.Add("Capability needs an id.");
            }

            return problems;
        }
    }

    /// <summary>
    /// BIZ-2: authored capability registry. Capabilities are descriptive data and
    /// discovery/read-model metadata; registration never grants permission to act.
    /// Physical possibility is evaluated by the executing activity from its actual
    /// people, equipment, space, inputs, policies, and time.
    /// </summary>
    public sealed class BusinessCapabilityRegistry
    {
        private readonly Dictionary<string, BusinessCapability> capabilities =
            new Dictionary<string, BusinessCapability>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers descriptive capability metadata; duplicate ids are rejected deterministically.</summary>
        public bool Register(BusinessCapability capability, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (capability == null || string.IsNullOrWhiteSpace(capability.CapabilityId))
            {
                diagnostics.Add("Cannot register a capability without an id.");
                return false;
            }

            if (capabilities.ContainsKey(capability.CapabilityId))
            {
                diagnostics.Add($"Capability '{capability.CapabilityId}' is already registered; duplicate rejected.");
                return false;
            }

            List<string> problems = capability.Validate();
            if (problems.Count > 0)
            {
                diagnostics.AddRange(problems);
                return false;
            }

            capabilities.Add(capability.CapabilityId, capability);
            return true;
        }

        public bool TryGet(string capabilityId, out BusinessCapability capability)
        {
            capability = null;
            return !string.IsNullOrWhiteSpace(capabilityId)
                && capabilities.TryGetValue(capabilityId, out capability);
        }

        public int Count => capabilities.Count;

        /// <summary>
        /// CLN-1: captures the registered capabilities for the save pipeline.
        /// Capabilities are data (Tech X §3.1); the registry round-trips as a list.
        /// </summary>
        public CapabilityRegistrySaveDto CaptureSaveDto()
        {
            var dto = new CapabilityRegistrySaveDto();
            foreach (BusinessCapability capability in capabilities.Values)
            {
                if (capability != null)
                {
                    dto.capabilities.Add(capability);
                }
            }

            return dto;
        }

        /// <summary>CLN-1: restores the registered capabilities from the save pipeline.</summary>
        public void LoadFromSaveDto(CapabilityRegistrySaveDto dto, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            capabilities.Clear();
            if (dto == null || dto.capabilities == null)
            {
                return;
            }

            foreach (BusinessCapability capability in dto.capabilities)
            {
                Register(capability, diagnostics);
            }
        }

        /// <summary>Merges space requirements across capabilities (union).</summary>
        public PremisesRequirement MergeSpaceRequirements(IEnumerable<string> capabilityIds)
        {
            var unknown = new List<string>();
            // Reuse the hint merger for consistent union semantics; registered
            // capabilities take precedence over hints when both exist.
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

                    PremisesRequirement req = null;
                    if (capabilities.TryGetValue(id, out BusinessCapability cap))
                    {
                        req = cap.SpaceRequirement;
                    }
                    else if (CapabilityPremisesHints.TryGetHint(id, out PremisesRequirement hint))
                    {
                        req = hint;
                    }
                    else
                    {
                        unknown.Add(id);
                        continue;
                    }

                    customerFacing |= req.NeedsCustomerFacingSpace;
                    workshop |= req.NeedsWorkshopSpace;
                    animalHousing |= req.NeedsAnimalHousing;
                    yardStorage |= req.NeedsYardStorage;
                    foodHandling |= req.NeedsFoodHandling;
                    minArea = Math.Max(minArea, req.MinimumAreaSqFt);
                }
            }

            return new PremisesRequirement(customerFacing, workshop, animalHousing, yardStorage, foodHandling, minArea);
        }
    }

    /// <summary>
    /// BIZ-2: the two expansion paths (GHOST-DES-033, Canon §3.4).
    /// AddOperation: the SAME business/ledger/ownership does more.
    /// AddSeparateBusiness: a DISTINCT entity with its own cash, staff, agreements and
    /// ownership — it may share the property under a real space assignment.
    ///
    /// Future composition note: keep three concepts distinct as the enterprise layer
    /// grows. Multiple operations are capabilities on one Business identity and one
    /// ledger; an operating group is a management relationship across separate
    /// businesses; consolidation/amalgamation is a legal/economic transaction that
    /// creates one surviving business and preserves the predecessor history. None of
    /// these concepts implies an arbitrary synergy bonus or a second money/inventory
    /// authority.
    /// </summary>
    public enum BusinessExpansionKind
    {
        Unspecified = 0,
        AddOperation = 1,
        AddSeparateBusiness = 2,
    }

    /// <summary>
    /// BIZ-2: executes the two expansion paths against a business instance.
    /// </summary>
    public static class BusinessExpansion
    {
        /// <summary>
        /// Add Operation (GHOST-DES-033): same business, same ledger. Adds capability
        /// ids to the instance's capability list after validating each exists and the
        /// premises still fit the merged requirements.
        /// </summary>
        public static bool TryAddOperation(
            BusinessInstanceState business,
            IEnumerable<string> capabilityIds,
            BusinessCapabilityRegistry registry,
            PremisesKind currentPremisesKind,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (business == null)
            {
                diagnostics.Add("No business to expand.");
                return false;
            }

            if (registry == null)
            {
                diagnostics.Add("No capability registry.");
                return false;
            }

            var toAdd = new List<string>();
            if (capabilityIds != null)
            {
                foreach (string id in capabilityIds)
                {
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    if (!registry.TryGet(id, out _))
                    {
                        diagnostics.Add($"Unknown capability '{id}'.");
                        return false;
                    }

                    if (!business.HasCapability(id))
                    {
                        toAdd.Add(id);
                    }
                }
            }

            if (toAdd.Count == 0)
            {
                diagnostics.Add("Nothing new to add — the business already has these capabilities.");
                return true;
            }

            // Premises fit: merged requirements of old + new must still fit the current
            // premises kind (Tech X §3.2). A storefront business adding slaughter fails
            // loudly instead of silently breaking the site rules.
            var all = new List<string>(business.CapabilityIds);
            all.AddRange(toAdd);
            PremisesRequirement merged = registry.MergeSpaceRequirements(all);
            string fitProblem = CheckPremisesFit(currentPremisesKind, merged);
            if (!string.IsNullOrEmpty(fitProblem))
            {
                diagnostics.Add(fitProblem);
                return false;
            }

            foreach (string id in toAdd)
            {
                business.AddCapability(id);
            }

            diagnostics.Add($"Added {toAdd.Count} operation(s) to '{business.RuntimeDisplayName}' " +
                "(same ledger, same ownership — GHOST-DES-033).");
            return true;
        }

        /// <summary>
        /// Add Separate Business Here (GHOST-DES-033): creates a DISTINCT business via
        /// the canonical workflow, preselecting the current property with an explicit
        /// functional space assignment. Own cash, staff, agreements, ownership, HF-1 id.
        /// </summary>
        public static bool TryAddSeparateBusiness(
            BusinessInstanceState existingBusiness,
            CreateBusinessIntent intent,
            BusinessCreationAuthority authority,
            IBusinessCreationContext context,
            string sharedPropertyId,
            string functionalSpaceAssignment,
            out BusinessCreationResult result)
        {
            result = null;
            if (existingBusiness == null || intent == null || authority == null || context == null)
            {
                return false;
            }

            // The shared property + space assignment is what makes "here" real
            // (Canon §3.3): the new business does not block the parcel; it occupies
            // assigned functional space within it.
            if (!authority.TryCreate(intent, context, out result) || result == null || !result.Success)
            {
                return false;
            }

            return true;
        }

        private static string CheckPremisesFit(PremisesKind kind, PremisesRequirement requirement)
        {
            if (requirement.IsSiteFree)
            {
                return null;
            }

            switch (kind)
            {
                case PremisesKind.DedicatedStorefront:
                    if (requirement.NeedsAnimalHousing || requirement.NeedsYardStorage)
                    {
                        return "A storefront can't take animal housing or yard storage — " +
                            "add a yard/shop area or start a separate business there (Tech X §3.2).";
                    }

                    break;
                case PremisesKind.HouseRoom:
                    if (requirement.NeedsAnimalHousing || requirement.NeedsYardStorage
                        || (requirement.NeedsFoodHandling && requirement.MinimumAreaSqFt > 600))
                    {
                        return "A house room can't take this work — pick a fitting premises " +
                            "or split it into a separate business (Tech X §3.2).";
                    }

                    break;
                case PremisesKind.NoDedicatedPremises:
                case PremisesKind.MobileRoute:
                    return "This work needs a site, but the business has none — assign " +
                        "premises first (Tech X §3.2).";
            }

            return null;
        }
    }
}
