using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Population;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// A2 plan 27 (P10 live felling driver, note 110 C6): applies the
    /// explicitly configured, finite development-scenario opening
    /// equipment assets to the mill business. Mirrors
    /// ScenarioOpeningEquipmentEndowment (the bakery installer) but is
    /// mill-typed and asset-typed: it installs assets, not workstations
    /// — each listed OpeningAssetEntry becomes one EquipmentAsset in
    /// the business equipment register (kind from the entry, full
    /// condition, business-owned, located at the business). Per
    /// business key, scenario-owned, zero by default, NewGame-only in
    /// the bootstrapper's call, idempotent via the BusinessRuntimeState
    /// endowment marker, and never changes cash. A mill whose business
    /// key has no opening-equipment entry gets nothing; a scenario
    /// whose config does not use this field gets nothing. The code
    /// installs exactly what the config lists, once. The asset ids are
    /// Kennedy-authored; the slice fixture id "slice-felling-axe" is
    /// refused here and in DevelopmentTownConfiguration.Validate.
    /// An asset alone fells nothing — it only makes the felling
    /// equipment-gate codes passable on real, scenario-authored
    /// equipment.
    /// </summary>
    public static class ScenarioMillOpeningEquipmentEndowment
    {
        public static bool TryApply(
            DevelopmentTownConfiguration configuration,
            string businessKey,
            string businessInstanceId,
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
                || string.IsNullOrWhiteSpace(businessInstanceId)
                || businessRuntimeState == null || register == null)
            {
                error = "Development opening mill equipment requires a valid configuration, configured business, business instance id, business runtime state, and equipment register.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(register.BusinessInstanceId)
                && !string.Equals(register.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
            {
                error = $"Development opening mill equipment for '{businessInstanceId}' refused: the equipment register belongs to '{register.BusinessInstanceId}'.";
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
                report = $"No opening equipment is configured for business '{businessKey}'; no mill asset was installed.";
                return true;
            }

            // Validate everything before mutating anything, so a refusal
            // installs nothing.
            var assets = new List<DevelopmentTownConfiguration.OpeningAssetEntry>();
            var seenAssetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DevelopmentTownConfiguration.OpeningAssetEntry asset in equipment.assetEntries ?? Array.Empty<DevelopmentTownConfiguration.OpeningAssetEntry>())
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.assetId) || string.IsNullOrWhiteSpace(asset.kind))
                {
                    error = $"Opening equipment for '{businessKey}' contains an asset entry with a blank id or kind — nothing was installed.";
                    return false;
                }
                if (string.Equals(asset.assetId.Trim(), "slice-felling-axe", StringComparison.Ordinal))
                {
                    error = $"Opening equipment for '{businessKey}' must not reuse the slice fixture id 'slice-felling-axe' — nothing was installed.";
                    return false;
                }
                if (!seenAssetIds.Add(asset.assetId.Trim()))
                {
                    error = $"Opening equipment for '{businessKey}' lists asset '{asset.assetId}' twice — nothing was installed.";
                    return false;
                }
                assets.Add(asset);
            }

            string endowmentId = $"development-town-opening-mill-equipment:{configuration.ConfigurationId}:{businessKey}:{businessInstanceId}";
            if (businessRuntimeState.HasAppliedScenarioEndowment(endowmentId))
            {
                report = $"Opening mill equipment already recorded for {businessInstanceId}; no asset recreated.";
                return true;
            }

            // Defense in depth, independent of the marker (the marker is
            // persisted — BusinessRuntimeState save/load round-trips the
            // applied-endowment ids — but a register restored without it
            // must still never double-install): re-registering the same
            // asset id replaces, never duplicates
            // (BusinessEquipmentRegister dedupes by id).
            var installed = new List<string>();
            foreach (DevelopmentTownConfiguration.OpeningAssetEntry asset in assets)
            {
                register.RegisterAsset(new EquipmentAsset
                {
                    AssetId = asset.assetId.Trim(),
                    Kind = asset.kind.Trim(),
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = businessInstanceId,
                    LocationId = businessInstanceId,
                });
                installed.Add($"asset '{asset.assetId.Trim()}' (kind '{asset.kind.Trim()}')");
                diag.Add($"ScenarioMillOpeningEquipmentEndowment: installed mill {installed[installed.Count - 1]} for {businessInstanceId}.");
            }

            if (string.IsNullOrWhiteSpace(register.BusinessInstanceId))
                register.BusinessInstanceId = businessInstanceId;
            if (!businessRuntimeState.RecordAppliedScenarioEndowment(endowmentId))
            {
                error = $"Could not record opening mill equipment allocation '{endowmentId}'.";
                return false;
            }

            report = installed.Count == 0
                ? $"Opening equipment entry for '{businessKey}' ({businessInstanceId}) lists no assets; nothing was installed."
                : $"Applied configured opening mill equipment to '{businessKey}' ({businessInstanceId}): {string.Join("; ", installed)}.";
            return true;
        }
    }
}
