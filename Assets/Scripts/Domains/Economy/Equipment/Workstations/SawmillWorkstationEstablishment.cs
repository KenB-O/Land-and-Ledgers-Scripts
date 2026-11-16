using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;

namespace LandLedgers.Economy.Equipment.Workstations
{
    /// <summary>
    /// C-3 (expanded campaign, Phase 1 forestry): establishes the sawmill's
    /// saw-line workstation in the hub-owned per-business workstation registry,
    /// and registers the mill's support context (Canon 5.2). Called by the
    /// bootstrapper after the mill opening equipment endowment installs the
    /// saw-line components into the mill register, on NewGame and Continue
    /// alike — so the workstation is DERIVED from the register every boot,
    /// never persisted as a second source of truth. If a component asset
    /// leaves the register (sold, broken, transferred), the station is
    /// re-derived without it and the gate refuses naming the gap; readiness
    /// is derived by WorkstationInstance.EvaluateReady, never a bare flag.
    ///
    /// Decisions (provisional, real-life rule — Kennedy can correct):
    /// - OD-C6 motive power is Steam: no water site (flow/head, dam, race,
    ///   pond) is authored for the dev mill; Canon §8.5A says steam mills are
    ///   already plausible at the 1880 Black Hills opening; the mill's own
    ///   slab/offcut byproduct provides boiler fuel. The caller passes the
    ///   kind, so a corrected choice flows through without an edit here.
    /// - SpaceId is "<millBusinessId>-saw-floor": a stable derived label for
    ///   the saw floor inside the mill's premises. Physical placement (the
    ///   saw-line machine, interaction positions, storage) is Codex's job.
    /// - The sawyer files his own saw (no separate filer person is invented):
    ///   the filer loop names the sawyer as filer, so the mill's support
    ///   context carries the "saw-filing" repair capability directly.
    /// - Opening lubrication store is 30 units: about one month of sawing
    ///   days at 1 unit/day. C-5 builds consumption and replenishment; this
    ///   slice only opens the store and never tops it up.
    /// </summary>
    public static class SawmillWorkstationEstablishment
    {
        public const string SawmillSawLineId = "sawmill-saw-line";

        /// <summary>Required component kinds, from WorkstationCatalog.SawmillSawLine.</summary>
        public static readonly string[] RequiredComponentKinds = { "saw-line", "log-carriage", "drive-system" };

        /// <summary>Opening mill-lubrication store (units). Provisional calibration: one month of sawing days at 1 unit/day.</summary>
        public const int OpeningMillLubricationUnits = 30;

        public const string SawFilingCapability = "saw-filing";

        /// <summary>
        /// Establishes the saw-line station in the holder's workstation registry:
        /// space, component asset ids synced from the mill register, and a
        /// recorded motive-power source. Returns false (with the missing kinds
        /// named in error) when a required component kind is absent from the
        /// register — but the station IS still established with whatever the
        /// register holds, so the gate refuses naming the gap instead of
        /// claiming the workstation was never set up. Returns false without
        /// touching anything on a structural problem (null registry/register,
        /// blank holder id).
        /// </summary>
        public static bool TryEstablishStation(
            BusinessWorkstations stations,
            BusinessEquipmentRegister millRegister,
            MotivePowerKind motivePower,
            List<string> diag,
            out string report,
            out string error)
        {
            report = string.Empty;
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (stations == null)
            {
                error = "SawmillWorkstationEstablishment: no workstation registry supplied.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(stations.BusinessInstanceId))
            {
                error = "SawmillWorkstationEstablishment: the workstation registry names no holder.";
                return false;
            }
            if (millRegister == null)
            {
                error = "SawmillWorkstationEstablishment: no mill equipment register supplied.";
                return false;
            }

            WorkstationInstance station = stations.GetOrCreate(SawmillSawLineId);
            station.SpaceId = stations.BusinessInstanceId + "-saw-floor";

            // Sync the component list from the register: the station holds
            // exactly one register asset per required kind — the first usable
            // one, else the first present one (so a wrecked-only component
            // still names the condition gap at the gate instead of a missing
            // component). A component that left the register leaves the
            // station too; readiness stays derived.
            var installed = new List<string>();
            var missing = new List<string>();
            station.ComponentAssetIds.Clear();
            foreach (string kind in RequiredComponentKinds)
            {
                EquipmentAsset asset = FirstAssetOfKind(millRegister, kind);
                if (asset == null)
                {
                    missing.Add(kind);
                    continue;
                }
                station.InstallComponent(asset.AssetId);
                installed.Add(asset.AssetId);
            }

            // Motive power: record an available source of the requested kind
            // when none is recorded yet. An existing source (available or
            // not) is left alone — the gate derives from it, so a recorded
            // outage stays an outage instead of being silently repaired.
            bool hasSource = false;
            foreach (MotivePowerSource source in station.MotivePowerSources)
                if (source != null && source.Kind == motivePower) { hasSource = true; break; }
            if (!hasSource)
            {
                station.MotivePowerSources.Add(new MotivePowerSource(motivePower, MotivePowerDescription(motivePower), true)
                {
                    ProviderId = stations.BusinessInstanceId,
                });
            }

            diag.Add($"SawmillWorkstationEstablishment: saw-line '{SawmillSawLineId}' for '{stations.BusinessInstanceId}' in space '{station.SpaceId}' with components {string.Join(", ", installed)} and {motivePower} motive power.");
            report = $"SawmillWorkstationEstablishment: established saw-line '{SawmillSawLineId}' for '{stations.BusinessInstanceId}' (space '{station.SpaceId}', components {string.Join(", ", installed)}, {motivePower} motive power).";

            if (missing.Count > 0)
            {
                error = $"SawmillWorkstationEstablishment: saw-line '{SawmillSawLineId}' for '{stations.BusinessInstanceId}' is missing {missing.Count} required component(s) from the mill register: {string.Join(", ", missing)} — the station is established with what the register holds, and the gate will refuse until they are provisioned.";
                diag.Add(error);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Registers the mill's support context (Canon 5.2): the "saw-filing"
        /// repair capability (the sawyer files his own saw) and the opening
        /// lubrication store. Idempotent: the capability is added once, and an
        /// existing lubrication store is never topped up — a drawn-down store
        /// stays drawn down until C-5 builds consumption and replenishment.
        /// </summary>
        public static void RegisterMillSupport(SupportContext support)
        {
            if (support == null) return;
            if (!support.AvailableRepairCapabilities.Contains(SawFilingCapability))
                support.AvailableRepairCapabilities.Add(SawFilingCapability);
            string key = SupportContext.Key("consumable", "mill-lubrication");
            if (!support.ConsumableUnits.ContainsKey(key))
                support.ConsumableUnits[key] = OpeningMillLubricationUnits;
        }

        private static EquipmentAsset FirstAssetOfKind(BusinessEquipmentRegister register, string kind)
        {
            if (register?.Assets == null) return null;
            EquipmentAsset firstPresent = null;
            foreach (EquipmentAsset asset in register.Assets)
            {
                if (asset == null || !string.Equals(asset.Kind, kind, StringComparison.Ordinal)) continue;
                if (firstPresent == null) firstPresent = asset;
                if (asset.IsUsable) return asset;
            }
            return firstPresent;
        }

        private static string MotivePowerDescription(MotivePowerKind kind)
        {
            switch (kind)
            {
                case MotivePowerKind.Steam: return "sawmill steam engine";
                case MotivePowerKind.Water: return "sawmill mill race";
                default: return "sawmill motive power (" + kind + ")";
            }
        }
    }
}
