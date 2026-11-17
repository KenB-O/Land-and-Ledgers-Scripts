using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Population;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// F1 part 2 (expanded campaign 2026-11-16, O-F3): applies the
    /// explicitly configured, finite development-scenario opening
    /// endowment to the carpentry workshop. Mirrors
    /// ScenarioOpeningEquipmentEndowment (the bakery installer) and
    /// ScenarioMillOpeningEquipmentEndowment (the mill installer), but
    /// is carpentry-typed: it installs the configured opening assets
    /// (the workbench) and tool kits (the carpenter hand-tool kit) into
    /// the business equipment register, registers the equipment and
    /// workspace references on the business's generic configuration so
    /// the production gate can see them, establishes the carpenter
    /// workstation from the installed components (the bakery installer's
    /// convention: the functional space is the definition's required
    /// space kind), and receives the configured opening lumber into the
    /// workshop's generic inventory. Per business key, scenario-owned,
    /// zero by default, NewGame-only in the bootstrapper's call,
    /// idempotent via the BusinessRuntimeState endowment marker, and
    /// never changes cash. A workshop whose business key has no
    /// opening-equipment entry gets no tools; a scenario with no
    /// opening lumber configured gets no lumber. The code installs
    /// exactly what the config lists, once. The asset ids are
    /// Kennedy-authored; the slice fixture ids are refused here and in
    /// DevelopmentTownConfiguration.Validate. An installed workstation
    /// alone makes no furniture — the production binding
    /// (CarpentryProduction) starts and advances the F1 recipes, and
    /// the lumberyard supplies further lumber through the retail
    /// procurement path. The bed and bedroll recipes are NOT runnable
    /// from this endowment: they need hay, which has no priced supply
    /// (F7 finding F3).
    /// </summary>
    public static class ScenarioCarpentryOpeningEndowment
    {
        /// <summary>The carpentry test-fixture asset id prefix — never a production asset id.</summary>
        public const string FixtureAssetIdPrefix = "f1p2-fixture-";

        public static bool TryApply(
            DevelopmentTownConfiguration configuration,
            string businessKey,
            BusinessInstanceState business,
            BusinessEquipmentRegister register,
            BusinessWorkstations workstations,
            int dayIndex,
            List<string> diag,
            out string report,
            out string error)
        {
            report = string.Empty;
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (configuration == null || !configuration.TryGetBusiness(businessKey, out DevelopmentTownConfiguration.BusinessDefinition slot)
                || business == null || business.RuntimeState == null
                || string.IsNullOrWhiteSpace(business.InstanceId)
                || register == null || workstations == null)
            {
                error = "Development opening carpentry endowment requires a valid configuration, configured business, business instance with runtime state, equipment register, and workstation registry.";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(register.BusinessInstanceId)
                && !string.Equals(register.BusinessInstanceId, business.InstanceId, StringComparison.Ordinal))
            {
                error = $"Development opening carpentry endowment for '{business.InstanceId}' refused: the equipment register belongs to '{register.BusinessInstanceId}'.";
                return false;
            }

            // K1: direct owner-record iteration. The deleted OpeningEquipment
            // view returned this business's opening.equipment record; the
            // null check below keeps the same no-equipment report.
            DevelopmentTownConfiguration.BusinessOpeningEquipment equipment = slot.opening?.equipment;
            if (equipment == null)
            {
                report = $"No opening equipment is configured for business '{businessKey}'; no carpentry asset, kit, workstation, or lumber was installed.";
                return true;
            }

            // Validate everything before mutating anything, so a refusal
            // installs nothing.
            WorkstationDefinition workstationDefinition = WorkstationCatalog.CarpenterWorkstation;
            if (workstationDefinition == null
                || string.IsNullOrWhiteSpace(workstationDefinition.RequiredSpaceKind))
            {
                error = "Development opening carpentry endowment refused: the CarpenterWorkstation definition names no required space kind.";
                return false;
            }
            var assets = new List<DevelopmentTownConfiguration.OpeningAssetEntry>();
            var seenAssetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DevelopmentTownConfiguration.OpeningAssetEntry asset in equipment.assetEntries ?? Array.Empty<DevelopmentTownConfiguration.OpeningAssetEntry>())
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.assetId) || string.IsNullOrWhiteSpace(asset.kind))
                {
                    error = $"Opening equipment for '{businessKey}' contains an asset entry with a blank id or kind — nothing was installed.";
                    return false;
                }
                if (string.Equals(asset.assetId.Trim(), "slice-felling-axe", StringComparison.Ordinal)
                    || asset.assetId.Trim().StartsWith(FixtureAssetIdPrefix, StringComparison.Ordinal))
                {
                    error = $"Opening equipment for '{businessKey}' must not reuse the slice fixture id '{asset.assetId}' — nothing was installed.";
                    return false;
                }
                if (!seenAssetIds.Add(asset.assetId.Trim()))
                {
                    error = $"Opening equipment for '{businessKey}' lists asset '{asset.assetId}' twice — nothing was installed.";
                    return false;
                }
                assets.Add(asset);
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
            int openingLumberUnits = configuration.OpeningCarpentryLumberUnits;
            if (openingLumberUnits < 0)
            {
                error = $"Opening carpentry lumber for '{businessKey}' cannot be negative — nothing was installed.";
                return false;
            }

            // The workstation must be ready from exactly the components
            // this endowment installs — evaluate the candidate first, so a
            // config that names the wrong kinds refuses before anything
            // is installed. The functional space is the definition's
            // required space kind (the bakery installer's convention).
            string stationId = business.InstanceId + "-carpenter-workshop";
            var componentIds = new List<string>();
            var candidateViews = new Dictionary<string, WorkstationComponentView>(StringComparer.Ordinal);
            foreach (DevelopmentTownConfiguration.OpeningAssetEntry asset in assets)
            {
                componentIds.Add(asset.assetId.Trim());
                candidateViews[asset.assetId.Trim()] = new WorkstationComponentView
                {
                    AssetId = asset.assetId.Trim(),
                    Kind = asset.kind.Trim(),
                    Condition01 = 1f,
                    IsUsable = true,
                };
            }
            for (int n = 0; n < kitIds.Count; n++)
            {
                string kitInstanceId = $"{business.InstanceId}-{kitIds[n]}-{n}";
                componentIds.Add(kitInstanceId);
                candidateViews[kitInstanceId] = new WorkstationComponentView
                {
                    AssetId = kitInstanceId,
                    Kind = kitIds[n],
                    Condition01 = 1f,
                    IsUsable = true,
                };
            }
            var candidateStation = new WorkstationInstance
            {
                InstanceId = stationId,
                WorkstationId = workstationDefinition.WorkstationId,
                BusinessInstanceId = business.InstanceId,
                SpaceId = workstationDefinition.RequiredSpaceKind,
                ComponentAssetIds = new List<string>(componentIds),
            };
            var candidateDiag = new List<string>();
            string candidateRefusal = candidateStation.EvaluateReady(
                workstationDefinition,
                id => candidateViews.TryGetValue(id ?? string.Empty, out WorkstationComponentView view) ? view : (WorkstationComponentView?)null,
                candidateDiag);
            if (!string.IsNullOrWhiteSpace(candidateRefusal))
            {
                error = $"Opening carpentry endowment for '{businessKey}' refused: the configured components do not satisfy the carpenter workstation — {candidateRefusal}";
                return false;
            }

            string endowmentId = $"development-town-opening-carpentry:{configuration.ConfigurationId}:{businessKey}:{business.InstanceId}";
            if (business.RuntimeState.HasAppliedScenarioEndowment(endowmentId))
            {
                report = $"Opening carpentry endowment already recorded for {business.InstanceId}; no asset, kit, workstation, or lumber recreated.";
                return true;
            }

            // Defense in depth, independent of the marker (the marker is
            // persisted — BusinessRuntimeState save/load round-trips the
            // applied-endowment ids — but a register restored without it
            // must still never double-install): re-registering the same
            // asset/kit instance id replaces, never duplicates
            // (BusinessEquipmentRegister dedupes by id), and the lumber
            // receipt is idempotent by shipment id.
            var installed = new List<string>();
            foreach (DevelopmentTownConfiguration.OpeningAssetEntry asset in assets)
            {
                register.RegisterAsset(new EquipmentAsset
                {
                    AssetId = asset.assetId.Trim(),
                    Kind = asset.kind.Trim(),
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = business.InstanceId,
                    LocationId = business.InstanceId,
                });
                installed.Add($"asset '{asset.assetId.Trim()}' (kind '{asset.kind.Trim()}')");
                diag.Add($"ScenarioCarpentryOpeningEndowment: installed carpentry {installed[installed.Count - 1]} for {business.InstanceId}.");
            }
            for (int n = 0; n < kitIds.Count; n++)
            {
                string kitId = kitIds[n];
                var kit = new ToolKitInstance
                {
                    InstanceId = $"{business.InstanceId}-{kitId}-{n}",
                    KitId = kitId,
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = business.InstanceId,
                    LocationId = business.InstanceId,
                };
                register.RegisterKit(kit);
                installed.Add($"kit '{kitId}' ({kit.InstanceId})");
                diag.Add($"ScenarioCarpentryOpeningEndowment: installed tool kit '{kitId}' ({kit.InstanceId}) for {business.InstanceId}.");
            }

            // The production gate reads the business's generic
            // configuration, not the register: register the equipment
            // kinds and the workspace reference so a furniture start
            // can see the installed tools and station (the F1 fixture
            // registers the same three references).
            foreach (DevelopmentTownConfiguration.OpeningAssetEntry asset in assets)
                business.RegisterGenericEquipmentAsset(asset.kind.Trim());
            foreach (string kitId in kitIds)
                business.RegisterGenericEquipmentAsset(kitId);
            business.RegisterGenericWorkspace(workstationDefinition.WorkstationId);

            // Establish the workstation from the installed components and
            // register it — re-evaluated against the register itself, so
            // the installed station is proven ready, not merely claimed.
            var station = new WorkstationInstance
            {
                InstanceId = stationId,
                WorkstationId = workstationDefinition.WorkstationId,
                BusinessInstanceId = business.InstanceId,
                SpaceId = workstationDefinition.RequiredSpaceKind,
                ComponentAssetIds = new List<string>(componentIds),
            };
            var stationDiag = new List<string>();
            string stationRefusal = station.EvaluateReady(
                workstationDefinition,
                id => FindRegisterComponent(register, id),
                stationDiag);
            if (!string.IsNullOrWhiteSpace(stationRefusal))
            {
                error = $"Opening carpentry endowment for '{businessKey}' refused: the installed components do not satisfy the carpenter workstation — {stationRefusal}";
                return false;
            }
            string registerRefusal = workstations.RegisterInstance(station);
            if (!string.IsNullOrWhiteSpace(registerRefusal))
            {
                error = $"Opening carpentry endowment for '{businessKey}' refused: {registerRefusal}";
                return false;
            }
            installed.Add($"workstation '{workstationDefinition.WorkstationId}' in '{workstationDefinition.RequiredSpaceKind}'");
            diag.Add($"ScenarioCarpentryOpeningEndowment: established carpenter workstation {stationId} for {business.InstanceId}.");

            if (openingLumberUnits > 0)
            {
                string provenance = $"development-town-opening-carpentry-lumber (opening endowment for '{businessKey}', no purchase — see DevelopmentTownConfiguration.OpeningCarpentryLumberUnits)";
                if (!business.GenericConfiguration.TryReceiveInventoryShipment(
                        endowmentId + ":lumber", "lumber", openingLumberUnits, 0,
                        GenericVerticalCatalog.LumberDefinition(), provenance, dayIndex))
                {
                    error = $"Opening carpentry lumber for '{businessKey}' refused: the workshop could not receive {openingLumberUnits} lumber units.";
                    return false;
                }
                installed.Add($"{openingLumberUnits} lumber units (opening endowment, no purchase)");
                diag.Add($"ScenarioCarpentryOpeningEndowment: received {openingLumberUnits} opening lumber units for {business.InstanceId}.");
            }

            if (string.IsNullOrWhiteSpace(register.BusinessInstanceId))
                register.BusinessInstanceId = business.InstanceId;
            if (!business.RuntimeState.RecordAppliedScenarioEndowment(endowmentId))
            {
                error = $"Could not record opening carpentry endowment allocation '{endowmentId}'.";
                return false;
            }

            report = installed.Count == 0
                ? $"Opening equipment entry for '{businessKey}' ({business.InstanceId}) lists no assets or kits; nothing was installed."
                : $"Applied configured opening carpentry endowment to '{businessKey}' ({business.InstanceId}): {string.Join("; ", installed)}.";
            return true;
        }

        /// <summary>
        /// Maps a component id to its workstation view from the business's
        /// own register — assets by id, kits by instance id (kind = kit
        /// id). Null when the register holds no such component.
        /// </summary>
        public static WorkstationComponentView? FindRegisterComponent(BusinessEquipmentRegister register, string componentId)
        {
            if (register == null || string.IsNullOrWhiteSpace(componentId)) return null;
            foreach (EquipmentAsset asset in register.Assets ?? new List<EquipmentAsset>())
            {
                if (asset == null || !string.Equals(asset.AssetId, componentId, StringComparison.Ordinal)) continue;
                return new WorkstationComponentView
                {
                    AssetId = asset.AssetId,
                    Kind = asset.Kind,
                    Condition01 = asset.Condition01,
                    IsUsable = asset.IsUsable,
                };
            }
            foreach (ToolKitInstance kit in register.Kits ?? new List<ToolKitInstance>())
            {
                if (kit == null || !string.Equals(kit.InstanceId, componentId, StringComparison.Ordinal)) continue;
                return new WorkstationComponentView
                {
                    AssetId = kit.InstanceId,
                    Kind = kit.KitId,
                    Condition01 = kit.Condition01,
                    IsUsable = kit.IsUsable,
                };
            }
            return null;
        }
    }
}
