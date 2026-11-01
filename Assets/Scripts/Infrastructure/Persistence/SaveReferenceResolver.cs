using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Persistence
{
    public sealed class SaveReferenceResolver
    {
        private const string BusinessProfilesResourcePath = "Core/Economy/BusinessProfiles";
        private const string LegacyCropFarmShellBuildingId = "crop_farm_shell";
        private const string LegacyRanchShellBuildingId = "ranch_shell";
        private const string LegacyTownHallBuildingId = TownHallState.TownHallBuildingId;
        private const string LegacySchoolhouseBuildingId = SchoolhouseState.SchoolhouseBuildingId;
        private const string PreferredCropFarmReplacementBuildingId = "single_story_business_natural_wood";
        private const string PreferredRanchReplacementBuildingId = "single_story_business_green_wood";
        private const string PreferredTownHallReplacementBuildingId = "single_story_civic_natural_wood";

        private const int MaxTrackedMissingIds = 6;

        private readonly Dictionary<string, BuildingDefinition> buildingDefinitions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BusinessProfileDefinition> businessProfiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> legacyBuildingAliasTargets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> looseBuildingAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> looseBusinessProfileAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> ambiguousLooseBuildingAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> ambiguousLooseBusinessProfileAliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missingBuildingDefinitionIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> missingBusinessProfileIds = new(StringComparer.OrdinalIgnoreCase);

        private int buildingLookupCount;
        private int businessProfileLookupCount;
        private int buildingMissCount;
        private int businessProfileMissCount;
        private int legacyBuildingAliasHitCount;
        private int looseBuildingAliasHitCount;
        private int looseBusinessProfileAliasHitCount;

        public SaveReferenceResolver(TownWorldController townWorld, IEnumerable<BusinessProfileDefinition> explicitProfiles = null)
        {
            RegisterBuildingDefinitions(townWorld);
            RegisterBusinessProfiles(explicitProfiles);
            RegisterBusinessProfiles(Resources.LoadAll<BusinessProfileDefinition>(BusinessProfilesResourcePath));
        }

        public int TotalReferenceMissCount => buildingMissCount + businessProfileMissCount;
        public bool HasReferenceMisses => TotalReferenceMissCount > 0;

        public bool TryResolveBuildingDefinition(string buildingDefinitionId, out BuildingDefinition definition)
        {
            string normalizedId = NormalizeId(buildingDefinitionId);
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                definition = null;
                return false;
            }

            buildingLookupCount++;
            if (buildingDefinitions.TryGetValue(normalizedId, out definition))
            {
                if (legacyBuildingAliasTargets.ContainsKey(normalizedId))
                {
                    legacyBuildingAliasHitCount++;
                }

                return true;
            }

            string looseId = NormalizeLooseId(normalizedId);
            if (!string.IsNullOrWhiteSpace(looseId)
            && !ambiguousLooseBuildingAliases.Contains(looseId)
            && looseBuildingAliases.TryGetValue(looseId, out string exactId)
            && buildingDefinitions.TryGetValue(exactId, out definition))
            {
                looseBuildingAliasHitCount++;
                return true;
            }

            buildingMissCount++;
            TrackMissingId(missingBuildingDefinitionIds, normalizedId);
            return false;
        }

        public bool TryResolveBusinessProfile(string profileId, out BusinessProfileDefinition profile)
        {
            string normalizedId = NormalizeId(profileId);
            if (string.IsNullOrWhiteSpace(normalizedId))
            {
                profile = null;
                return false;
            }

            businessProfileLookupCount++;
            if (businessProfiles.TryGetValue(normalizedId, out profile))
            {
                return true;
            }

            string looseId = NormalizeLooseId(normalizedId);
            if (!string.IsNullOrWhiteSpace(looseId)
            && !ambiguousLooseBusinessProfileAliases.Contains(looseId)
            && looseBusinessProfileAliases.TryGetValue(looseId, out string exactId)
            && businessProfiles.TryGetValue(exactId, out profile))
            {
                looseBusinessProfileAliasHitCount++;
                return true;
            }

            businessProfileMissCount++;
            TrackMissingId(missingBusinessProfileIds, normalizedId);
            return false;
        }

        public string BuildDiagnosticsSummary()
        {
            string buildingMisses = missingBuildingDefinitionIds.Count > 0
            ? $" Building miss ids: {string.Join(", ", missingBuildingDefinitionIds)}."
            : string.Empty;
            string profileMisses = missingBusinessProfileIds.Count > 0
            ? $" Profile miss ids: {string.Join(", ", missingBusinessProfileIds)}."
            : string.Empty;

            return $"Reference resolution | Building lookups={buildingLookupCount}, Legacy alias hits={legacyBuildingAliasHitCount}, Loose building hits={looseBuildingAliasHitCount}, Building misses={buildingMissCount}, Profile lookups={businessProfileLookupCount}, Loose profile hits={looseBusinessProfileAliasHitCount}, Profile misses={businessProfileMissCount}."
            + buildingMisses
            + profileMisses;
        }

        private void RegisterBuildingDefinitions(TownWorldController townWorld)
        {
            TownGenerationSettings settings = townWorld != null ? townWorld.Settings : null;
            if (settings != null)
            {
                RegisterBuildingDefinition(settings.townHallDefinition != null
                ? settings.townHallDefinition.PhysicalBuildingDefinition
                : null);
            }

            BuildingDefinition[] catalog = settings != null ? settings.buildingCatalog : null;
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Length; i++)
                {
                    RegisterBuildingDefinition(catalog[i]);
                }
            }

            // Final-archive hardening: Resources-backed definitions provide a recovery layer when a
            // saved building id no longer appears in the current scene/catalog but the authored asset still exists.
            RegisterBuildingDefinitions(Resources.LoadAll<BuildingDefinition>(string.Empty));

            RegisterLegacyAgriculturalBuildingAliases();
            RegisterLegacyCivicBuildingAliases();

            if (townWorld == null || townWorld.Buildings == null)
            {
                return;
            }

            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                RegisterBuildingDefinition(building != null ? building.definition : null);
            }

            RegisterLegacyAgriculturalBuildingAliases();
            RegisterLegacyCivicBuildingAliases();
        }

        private void RegisterBuildingDefinitions(IEnumerable<BuildingDefinition> definitions)
        {
            if (definitions == null)
            {
                return;
            }

            foreach (BuildingDefinition definition in definitions)
            {
                RegisterBuildingDefinition(definition);
            }
        }

        private void RegisterBuildingDefinition(BuildingDefinition definition)
        {
            string normalizedId = NormalizeId(definition != null ? definition.BuildingId : null);
            if (definition == null || string.IsNullOrWhiteSpace(normalizedId))
            {
                return;
            }

            buildingDefinitions[normalizedId] = definition;
            RegisterLooseAlias(looseBuildingAliases, ambiguousLooseBuildingAliases, normalizedId, normalizedId);
            RegisterLooseAlias(looseBuildingAliases, ambiguousLooseBuildingAliases, definition.name, normalizedId);
        }

        private void RegisterLegacyAgriculturalBuildingAliases()
        {
            RegisterLegacyBuildingAlias(LegacyCropFarmShellBuildingId, PreferredCropFarmReplacementBuildingId, FindFirstRegularWorkplaceDefinition);
            RegisterLegacyBuildingAlias(LegacyRanchShellBuildingId, PreferredRanchReplacementBuildingId, FindFirstRegularWorkplaceDefinition);
        }

        private void RegisterLegacyCivicBuildingAliases()
        {
            RegisterLegacyBuildingAlias(LegacyTownHallBuildingId, PreferredTownHallReplacementBuildingId, FindFirstCivicDefinition);
            RegisterLegacyBuildingAlias(LegacySchoolhouseBuildingId, PreferredTownHallReplacementBuildingId, FindFirstCivicDefinition);
        }

        private void RegisterLegacyBuildingAlias(string legacyBuildingId, string preferredReplacementBuildingId, Func<BuildingDefinition> fallback)
        {
            string normalizedLegacyId = NormalizeId(legacyBuildingId);
            if (string.IsNullOrWhiteSpace(normalizedLegacyId) || buildingDefinitions.ContainsKey(normalizedLegacyId))
            {
                return;
            }

            if (!buildingDefinitions.TryGetValue(NormalizeId(preferredReplacementBuildingId), out BuildingDefinition replacement))
            {
                replacement = fallback != null ? fallback() : null;
            }

            if (replacement == null)
            {
                return;
            }

            buildingDefinitions[normalizedLegacyId] = replacement;
            legacyBuildingAliasTargets[normalizedLegacyId] = replacement.BuildingId;
            RegisterLooseAlias(looseBuildingAliases, ambiguousLooseBuildingAliases, normalizedLegacyId, normalizedLegacyId);
        }

        private BuildingDefinition FindFirstRegularWorkplaceDefinition()
        {
            foreach (BuildingDefinition definition in buildingDefinitions.Values)
            {
                if (definition != null
                && definition.CanHostWorkplace
                && definition.AgriculturalSiteRole == AgriculturalSiteRole.None
                && definition.PrimaryUse != BuildingUseType.Civic)
                {
                    return definition;
                }
            }

            return null;
        }

        private BuildingDefinition FindFirstCivicDefinition()
        {
            foreach (BuildingDefinition definition in buildingDefinitions.Values)
            {
                if (definition != null && definition.PrimaryUse == BuildingUseType.Civic)
                {
                    return definition;
                }
            }

            return null;
        }

        private void RegisterBusinessProfiles(IEnumerable<BusinessProfileDefinition> profiles)
        {
            if (profiles == null)
            {
                return;
            }

            foreach (BusinessProfileDefinition profile in profiles)
            {
                string normalizedId = NormalizeId(profile != null && profile.Business != null ? profile.Business.BusinessId : null);
                if (profile == null || profile.Business == null || string.IsNullOrWhiteSpace(normalizedId))
                {
                    continue;
                }

                businessProfiles[normalizedId] = profile;
                RegisterLooseAlias(looseBusinessProfileAliases, ambiguousLooseBusinessProfileAliases, normalizedId, normalizedId);
                RegisterLooseAlias(looseBusinessProfileAliases, ambiguousLooseBusinessProfileAliases, profile.name, normalizedId);
            }
        }

        private static void RegisterLooseAlias(Dictionary<string, string> aliasMap, HashSet<string> ambiguousAliases, string alias, string targetId)
        {
            string looseAlias = NormalizeLooseId(alias);
            string normalizedTarget = NormalizeId(targetId);
            if (aliasMap == null || ambiguousAliases == null || string.IsNullOrWhiteSpace(looseAlias) || string.IsNullOrWhiteSpace(normalizedTarget))
            {
                return;
            }

            if (ambiguousAliases.Contains(looseAlias))
            {
                return;
            }

            if (aliasMap.TryGetValue(looseAlias, out string existingTarget))
            {
                if (!string.Equals(existingTarget, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    aliasMap.Remove(looseAlias);
                    ambiguousAliases.Add(looseAlias);
                }

                return;
            }

            aliasMap[looseAlias] = normalizedTarget;
        }

        private static void TrackMissingId(HashSet<string> set, string id)
        {
            if (set == null || string.IsNullOrWhiteSpace(id) || set.Contains(id) || set.Count >= MaxTrackedMissingIds)
            {
                return;
            }

            set.Add(id);
        }

        private static string NormalizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeLooseId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] buffer = new char[value.Length];
            int length = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c))
                {
                    buffer[length++] = char.ToLowerInvariant(c);
                }
            }

            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
    }
}
