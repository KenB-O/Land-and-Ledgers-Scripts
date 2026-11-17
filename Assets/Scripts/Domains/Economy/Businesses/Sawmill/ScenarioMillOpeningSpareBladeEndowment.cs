using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// C-5 (expanded campaign, Phase 1 forestry): applies the explicitly
    /// configured, finite development-scenario opening spare blades to one
    /// business. Mirrors ScenarioOpeningEmploymentEndowment (C-1) and
    /// ScenarioMillOpeningEquipmentEndowment: each listed
    /// BusinessSpareBladeDefinition for the business key becomes one
    /// SawmillBladeState in the mill's blade inventory, procured as a
    /// BOOTSTRAP endowment — so the filer loop's spare-fitting branch,
    /// unreachable live today (the inventory is empty in production), has a
    /// real spare to fit when the fitted blade retires. One spare is the
    /// natural number: the mill has ONE saw line and ONE fitted blade, and
    /// Canon Part V says the filer keeps "replacement saws". Zero by default,
    /// NewGame-only in the bootstrapper's call, idempotent via the
    /// BusinessRuntimeState endowment marker (marker check BEFORE validation,
    /// per the C-1 bug lesson — a legitimate second apply is a no-op, and the
    /// inventory's duplicate-id check then only fires for an id registered
    /// by some other path without the marker, which is a genuine refusal).
    /// The applicator also stamps the FITTED blade's procurement as BOOTSTRAP,
    /// but ONLY when its ProcurementSource is blank — never overwriting a
    /// recorded source (a later fitted procured spare keeps its provenance).
    /// Never changes cash at application time; never invents a supplier.
    /// </summary>
    public static class ScenarioMillOpeningSpareBladeEndowment
    {
        public static bool TryApply(
            DevelopmentTownConfiguration configuration,
            string businessKey,
            string businessInstanceId,
            BusinessRuntimeState businessRuntimeState,
            SawmillShopRuntime mill,
            int dayIndex,
            List<string> diag,
            out string report,
            out string error)
        {
            report = string.Empty;
            error = string.Empty;
            diag = diag ?? new List<string>();
            if (configuration == null || !configuration.TryGetBusiness(businessKey, out DevelopmentTownConfiguration.BusinessDefinition business)
                || string.IsNullOrWhiteSpace(businessInstanceId)
                || businessRuntimeState == null
                || mill == null)
            {
                error = "Development opening spare blades require a valid configuration, configured business, business instance id, business runtime state, and mill runtime.";
                return false;
            }

            // Idempotence FIRST, before validation: the validation below
            // refuses a blade id that is already a spare, and a legitimate
            // second apply is exactly that state (the first apply received
            // these blades and recorded the marker). Checking the marker
            // here makes the second call a no-op.
            string endowmentId = $"development-town-opening-spare-blades:{configuration.ConfigurationId}:{businessKey}:{businessInstanceId}";
            if (businessRuntimeState.HasAppliedScenarioEndowment(endowmentId))
            {
                report = $"Opening spare blades already recorded for {businessInstanceId}; no spare re-received.";
                return true;
            }

            // Validate everything before mutating anything, so a refusal
            // receives nothing. K2: direct owner-record iteration. The
            // deleted OpeningSpareBlades view projected this business's
            // own opening.spareBlades and stamped the owning key onto
            // them; the business is already resolved above, so its own
            // array is the entry set and no key filter is needed. All
            // error messages use the businessKey parameter, as before.
            var spares = new List<string>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DevelopmentTownConfiguration.BusinessSpareBladeDefinition entry in business.opening?.spareBlades ?? Array.Empty<DevelopmentTownConfiguration.BusinessSpareBladeDefinition>())
            {
                if (entry == null) continue;
                if (string.IsNullOrWhiteSpace(entry.bladeId))
                {
                    error = $"Opening spare blades for '{businessKey}' contains an entry with a blank blade id — nothing was received.";
                    return false;
                }
                string bladeId = entry.bladeId.Trim();
                if (!seenIds.Add(bladeId))
                {
                    error = $"Opening spare blades for '{businessKey}' lists blade '{bladeId}' twice — nothing was received.";
                    return false;
                }
                if (string.Equals(bladeId, mill.Blade.BladeId, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Opening spare blades for '{businessKey}' lists blade '{bladeId}', which is the fitted blade — a spare must be a different blade; nothing was received.";
                    return false;
                }
                bool alreadySpare = false;
                foreach (SawmillBladeState spare in mill.BladeInventory.Spares)
                {
                    if (spare != null && string.Equals(spare.BladeId, bladeId, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadySpare = true;
                        break;
                    }
                }
                if (alreadySpare)
                {
                    error = $"Opening spare blades for '{businessKey}' lists blade '{bladeId}', which is already a spare — nothing was received.";
                    return false;
                }
                spares.Add(bladeId);
            }

            // Stamp the fitted blade's provenance, but only when blank —
            // never overwrite a recorded source.
            if (string.IsNullOrWhiteSpace(mill.Blade.ProcurementSource))
            {
                string stampRefusal = mill.RecordBladeProcurement(string.Empty, dayIndex, isBootstrapEndowment: true, diag);
                if (!string.IsNullOrWhiteSpace(stampRefusal))
                {
                    error = $"Opening spare blades for '{businessKey}' could not stamp the fitted blade's provenance: {stampRefusal}.";
                    return false;
                }
            }

            var received = new List<string>();
            foreach (string bladeId in spares)
            {
                var spare = new SawmillBladeState(bladeId);
                string procurementRefusal = spare.RecordProcurement(string.Empty, dayIndex, isBootstrapEndowment: true, diag);
                if (!string.IsNullOrWhiteSpace(procurementRefusal))
                {
                    error = $"Opening spare blades for '{businessKey}' could not procure blade '{bladeId}': {procurementRefusal}.";
                    return false;
                }
                string receiveRefusal = mill.ReceiveSpareBlade(spare, diag);
                if (!string.IsNullOrWhiteSpace(receiveRefusal))
                {
                    error = $"Opening spare blades for '{businessKey}' could not receive blade '{bladeId}': {receiveRefusal}.";
                    return false;
                }
                received.Add($"'{bladeId}'");
                diag.Add($"ScenarioMillOpeningSpareBladeEndowment: received opening spare blade {bladeId} for {businessInstanceId}.");
            }

            if (!businessRuntimeState.RecordAppliedScenarioEndowment(endowmentId))
            {
                error = $"Could not record opening spare blades allocation '{endowmentId}'.";
                return false;
            }

            report = received.Count == 0
                ? $"No opening spare blades are configured for business '{businessKey}'; no spare was received."
                : $"Applied configured opening spare blades to '{businessKey}' ({businessInstanceId}): {string.Join("; ", received)}.";
            return true;
        }
    }
}
