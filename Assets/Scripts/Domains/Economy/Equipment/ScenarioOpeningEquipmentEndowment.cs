using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Population;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// Slice S2 (2026-11-05, plan 02 §4): applies the explicitly
    /// configured, finite development-scenario opening equipment to its
    /// existing NPC Bakery. Mirrors ScenarioOpeningStockEndowment
    /// (GeneralStoreRuntimeManager.cs): per-business-key, scenario-owned,
    /// zero by default, NewGame-only, idempotent via the
    /// BusinessRuntimeState endowment marker, and never changes cash.
    /// A bakery whose business key has no opening-equipment entry gets
    /// nothing; a scenario whose config does not use this field gets
    /// nothing. The code installs exactly what the config lists, once.
    /// No fuel, no flour, no wholesale account, no bread: equipment
    /// alone bakes nothing — it only makes the kit and oven Precheck
    /// gates passable on real, scenario-authored equipment.
    /// </summary>
    public static class ScenarioOpeningEquipmentEndowment
    {
        public static bool TryApply(
            DevelopmentTownConfiguration configuration,
            string businessKey,
            BakeryShopRuntime bakeryRuntime,
            BusinessRuntimeState businessRuntimeState,
            BusinessEquipmentRegister register,
            int dayIndex,
            List<string> diag,
            out string report,
            out string error)
        {
            report = string.Empty;
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (configuration == null || !configuration.TryGetBusiness(businessKey, out _)
                || bakeryRuntime == null || businessRuntimeState == null || register == null)
            {
                error = "Development opening equipment requires a valid configuration, configured business, bakery runtime, business runtime state, and equipment register.";
                return false;
            }

            string businessInstanceId = bakeryRuntime.BusinessInstanceId;
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                error = "Development opening equipment requires a bakery runtime with a business instance id.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(register.BusinessInstanceId)
                && !string.Equals(register.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
            {
                error = $"Development opening equipment for '{businessInstanceId}' refused: the equipment register belongs to '{register.BusinessInstanceId}'.";
                return false;
            }

            DevelopmentTownConfiguration.BusinessOpeningEquipment equipment = null;
            foreach (DevelopmentTownConfiguration.BusinessOpeningEquipment candidate in configuration.OpeningEquipment)
                if (candidate != null && string.Equals(candidate.businessKey, businessKey, StringComparison.OrdinalIgnoreCase))
                {
                    equipment = candidate;
                    break;
                }
            if (equipment == null)
            {
                report = $"No opening equipment is configured for business '{businessKey}'; no oven or kit was installed.";
                return true;
            }

            // Validate everything before mutating anything, so a refusal
            // installs nothing.
            WorkstationDefinition ovenDefinition = WorkstationCatalog.BakeOven;
            if (string.IsNullOrWhiteSpace(ovenDefinition.RequiredSpaceKind))
            {
                error = "Development opening equipment refused: the BakeOven definition names no required space kind.";
                return false;
            }
            var kitIds = new List<string>();
            var seenKitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string kitId in equipment.kitIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(kitId) || ToolKitCatalog.Get(kitId.Trim()) == null)
                {
                    error = $"Opening equipment for '{businessKey}' names an unknown tool kit '{kitId}' — nothing was installed.";
                    return false;
                }
                if (!seenKitIds.Add(kitId.Trim()))
                {
                    error = $"Opening equipment for '{businessKey}' lists tool kit '{kitId}' twice — nothing was installed.";
                    return false;
                }
                kitIds.Add(kitId.Trim());
            }
            var ovens = new List<DevelopmentTownConfiguration.OvenEquipment>();
            foreach (DevelopmentTownConfiguration.OvenEquipment oven in equipment.ovens ?? Array.Empty<DevelopmentTownConfiguration.OvenEquipment>())
            {
                if (oven == null || oven.componentAssetIds == null
                    || oven.componentAssetIds.Length != ovenDefinition.Components.Count)
                {
                    error = $"Opening equipment for '{businessKey}' lists an oven whose component count must equal the BakeOven definition ({ovenDefinition.Components.Count}) — nothing was installed.";
                    return false;
                }
                foreach (string assetId in oven.componentAssetIds)
                    if (string.IsNullOrWhiteSpace(assetId))
                    {
                        error = $"Opening equipment for '{businessKey}' lists an oven with a blank component asset id — nothing was installed.";
                        return false;
                    }
                ovens.Add(oven);
            }

            string endowmentId = $"development-town-opening-equipment:{configuration.ConfigurationId}:{businessKey}:{businessInstanceId}";
            if (businessRuntimeState.HasAppliedScenarioEndowment(endowmentId))
            {
                report = $"Opening equipment already recorded for {businessInstanceId}; no oven or kit recreated.";
                return true;
            }

            // Defense in depth, independent of the marker (the marker is
            // persisted — BusinessRuntimeState save/load round-trips the
            // applied-endowment ids — but a register or runtime restored
            // without it must still never double-install): skip an oven
            // the bakery already holds with the same component ids, and
            // re-registering the same asset/kit instance id replaces,
            // never duplicates (BusinessEquipmentRegister dedupes by id).
            var installed = new List<string>();
            foreach (DevelopmentTownConfiguration.OvenEquipment oven in ovens)
            {
                bool alreadyInstalled = false;
                foreach (BakeryOven existing in bakeryRuntime.Ovens)
                {
                    if (existing?.Station?.ComponentAssetIds == null) continue;
                    if (SameComponents(existing.Station.ComponentAssetIds, oven.componentAssetIds))
                    {
                        alreadyInstalled = true;
                        break;
                    }
                }

                // Component assets are (re-)registered unconditionally:
                // the register is the oven's readiness evidence, and a
                // register lost while the oven survived must be rebuilt.
                // RegisterAsset dedupes by id, so this never duplicates.
                var componentIds = new List<string>();
                for (int i = 0; i < oven.componentAssetIds.Length; i++)
                {
                    string assetId = oven.componentAssetIds[i].Trim();
                    register.RegisterAsset(new EquipmentAsset
                    {
                        AssetId = assetId,
                        Kind = ovenDefinition.Components[i].EquipmentKind,
                        Condition01 = 1f,
                        OwnerKind = "business",
                        OwnerId = businessInstanceId,
                        LocationId = businessInstanceId,
                    });
                    componentIds.Add(assetId);
                }

                if (alreadyInstalled)
                {
                    diag.Add($"ScenarioOpeningEquipmentEndowment: bakery {businessInstanceId} already holds an oven with components [{string.Join(", ", componentIds)}]; oven not re-installed, component assets re-registered.");
                    continue;
                }

                string refusal = bakeryRuntime.AddOven(ovenDefinition.RequiredSpaceKind, componentIds, diag);
                if (!string.IsNullOrWhiteSpace(refusal))
                {
                    error = $"Opening equipment for '{businessKey}' could not install a bake oven: {refusal}";
                    return false;
                }
                installed.Add($"oven {bakeryRuntime.Ovens.Count - 1} in '{ovenDefinition.RequiredSpaceKind}' (components {string.Join(", ", componentIds)})");
                diag.Add($"ScenarioOpeningEquipmentEndowment: installed bake {installed[installed.Count - 1]} for {businessInstanceId}.");
            }

            for (int n = 0; n < kitIds.Count; n++)
            {
                string kitId = kitIds[n];
                var kit = new ToolKitInstance
                {
                    InstanceId = $"{businessInstanceId}-{kitId}-{n}",
                    KitId = kitId,
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = businessInstanceId,
                    LocationId = businessInstanceId,
                };
                register.RegisterKit(kit);
                installed.Add($"kit '{kitId}' ({kit.InstanceId})");
                diag.Add($"ScenarioOpeningEquipmentEndowment: installed tool kit '{kitId}' ({kit.InstanceId}) for {businessInstanceId}.");
            }

            if (string.IsNullOrWhiteSpace(register.BusinessInstanceId))
                register.BusinessInstanceId = businessInstanceId;
            if (!businessRuntimeState.RecordAppliedScenarioEndowment(endowmentId))
            {
                error = $"Could not record opening equipment allocation '{endowmentId}'.";
                return false;
            }

            report = installed.Count == 0
                ? $"Opening equipment entry for '{businessKey}' ({businessInstanceId}) lists no ovens or kits; nothing was installed."
                : $"Applied configured opening equipment to '{businessKey}' ({businessInstanceId}): {string.Join("; ", installed)}.";
            return true;
        }

        private static bool SameComponents(List<string> installed, string[] configured)
        {
            if (installed == null || configured == null || installed.Count != configured.Length) return false;
            var installedSet = new HashSet<string>(installed, StringComparer.Ordinal);
            foreach (string assetId in configured)
                if (string.IsNullOrWhiteSpace(assetId) || !installedSet.Contains(assetId.Trim())) return false;
            return true;
        }
    }
}
