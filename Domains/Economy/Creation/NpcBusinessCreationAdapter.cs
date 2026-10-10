using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// Phase G (G3): WRITTEN-FOR-UNITY. The runtime implementation of
    /// <see cref="INpcBusinessCreationPort"/> that runs the NPC founder's
    /// intent through the SAME generic BIZ-1 <see cref="BusinessCreationAuthority"/>
    /// the player uses — no NPC-only shortcut.
    ///
    /// The founder's identity is preserved as an NPC owner
    /// (<see cref="BusinessOwnerIdentity.Npc"/>); the descriptive
    /// BusinessType comes from the first activity; EVERY activity's
    /// capability ids are attached to the one business identity (BIZ-2:
    /// a mixed business is one identity with many capabilities — never a
    /// type permission gate).
    ///
    /// NOT compiled in the Roslyn harness: it needs the full Unity
    /// creation closure (BusinessInstanceState, ScriptableObject
    /// profiles). The formation orchestration over this port IS tested
    /// via a fake port; this adapter is Codex/Luna's Unity-run surface.
    /// </summary>
    public sealed class NpcBusinessCreationAdapter : INpcBusinessCreationPort, IBusinessCreationContext
    {
        private readonly EntityIdRegistry ids;
        private readonly BusinessCapabilityRegistry capabilityRegistry;

        /// <summary>
        /// Resolves a premises kind to a real building id. Supplied by the
        /// runtime manager (scene/building knowledge lives there, not here).
        /// Return -1 when no suitable site exists.
        /// </summary>
        public Func<PremisesKind, int> PremisesAssigner;

        /// <summary>
        /// Optional authored-profile lookup. When null or missing, the
        /// code fallback profile is built (any of the 28 BusinessTypes is
        /// creatable — Canon §3.1).
        /// </summary>
        public Func<BusinessType, BusinessProfileDefinition> AuthoredProfileLookup;

        public NpcBusinessCreationAdapter(EntityIdRegistry ids,
            BusinessCapabilityRegistry capabilityRegistry = null)
        {
            this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            this.capabilityRegistry = capabilityRegistry;
        }

        public string TryCreateBusiness(NpcBusinessFormationIntent intent, List<string> diagnostics,
            out NpcCreatedBusiness created)
        {
            created = null;
            diagnostics ??= new List<string>();
            if (intent == null)
            {
                return "Adapter: no formation intent.";
            }

            if (intent.Activities == null || intent.Activities.Count == 0)
            {
                return "Adapter: intent has no activities.";
            }

            // Descriptive type from the first activity; capabilities from ALL
            // activities — a mixed business is one identity, many capabilities.
            BusinessType descriptiveType = intent.Activities[0] != null
                ? intent.Activities[0].BusinessType
                : BusinessType.Generic;
            var allCapabilityIds = new List<string>();
            foreach (NpcBusinessActivitySpec activity in intent.Activities)
            {
                if (activity?.CapabilityIds == null) continue;
                foreach (string capabilityId in activity.CapabilityIds)
                {
                    if (string.IsNullOrWhiteSpace(capabilityId) || allCapabilityIds.Contains(capabilityId))
                    {
                        continue;
                    }

                    if (capabilityRegistry != null && !capabilityRegistry.TryGet(capabilityId, out _))
                    {
                        diagnostics.Add($"Adapter: capability '{capabilityId}' is not registered — passed through as data, never as permission.");
                    }

                    allCapabilityIds.Add(capabilityId);
                }
            }

            var ownership = BusinessOwnership.Sole(
                BusinessOwnerIdentity.Npc(intent.FounderPersonId, intent.FounderName, intent.FounderSurname),
                intent.TotalFormationCostCents());

            var requirement = new PremisesRequirement(
                customerFacing: intent.Premises?.CustomerFacing ?? false,
                workshop: intent.Premises?.Workshop ?? false,
                animalHousing: intent.Premises?.AnimalHousing ?? false,
                yardStorage: intent.Premises?.YardStorage ?? false,
                foodHandling: intent.Premises?.FoodHandling ?? false,
                minAreaSqFt: intent.Premises?.MinimumAreaSqFt ?? 0);

            var createIntent = new CreateBusinessIntent(
                descriptiveType,
                intent.DisplayName,
                ownership,
                requirement,
                MapPreference(intent.Premises),
                Math.Max(0, intent.WorkingCapitalCents));

            var authority = new BusinessCreationAuthority();
            if (!authority.TryCreate(createIntent, this, out BusinessCreationResult result) || result == null || !result.Success)
            {
                return "Adapter: generic BIZ-1 creation refused: "
                    + string.Join(" | ", result?.Diagnostics ?? new List<string>());
            }

            diagnostics.AddRange(result.Diagnostics);

            // Attach every activity's capabilities to the one identity (BIZ-2).
            if (result.Business != null)
            {
                foreach (string capabilityId in allCapabilityIds)
                {
                    result.Business.AddCapability(capabilityId);
                }
            }

            created = new NpcCreatedBusiness
            {
                BusinessEntityKey = result.BusinessEntityId.ToString(),
                InstanceId = result.Business?.InstanceId ?? string.Empty,
                DisplayName = intent.DisplayName,
                CapabilityIds = allCapabilityIds,
                OwnerName = intent.FounderName,
                FounderPersonId = intent.FounderPersonId,
            };
            diagnostics.Add($"Adapter: '{intent.DisplayName}' created as {created.BusinessEntityKey} " +
                $"through the generic BIZ-1 flow with {allCapabilityIds.Count} capabilit(ies) on one identity.");
            return null;
        }

        private static PremisesPreference MapPreference(NpcPremisesArrangement premises)
        {
            if (premises == null) return PremisesPreference.Auto;
            return premises.Tenure switch
            {
                NpcPremisesTenure.Shared => PremisesPreference.PreferShared,
                NpcPremisesTenure.HomeRoom => PremisesPreference.PreferHomeBased,
                NpcPremisesTenure.MobileRoute => PremisesPreference.PreferMobile,
                NpcPremisesTenure.NoSite => PremisesPreference.NoneRequired,
                _ => PremisesPreference.Auto,
            };
        }

        // IBusinessCreationContext — the authority's hosting-game surface.

        EntityIdRegistry IBusinessCreationContext.IdRegistry => ids;

        public bool TryGetProfile(BusinessType type, out BusinessProfileDefinition profile)
        {
            profile = AuthoredProfileLookup?.Invoke(type);
            return profile != null;
        }

        public BusinessProfileDefinition BuildFallbackProfile(BusinessType type, string displayName)
        {
            return BusinessProfileDefinition.CreateFallback(type, displayName);
        }

        public bool TryAssignPremises(PremisesKind kind, out int buildingId)
        {
            buildingId = -1;
            if (PremisesAssigner == null) return false;
            try
            {
                buildingId = PremisesAssigner(kind);
            }
            catch (Exception)
            {
                buildingId = -1;
            }

            return buildingId >= 0;
        }

        public void LogDiagnostic(string message)
        {
            Debug.Log("[NpcBusinessCreationAdapter] " + message);
        }
    }
}
