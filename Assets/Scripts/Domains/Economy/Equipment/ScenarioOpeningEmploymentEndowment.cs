using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Population;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// C-1 (expanded campaign, Phase 1 forestry): applies the explicitly
    /// configured, finite development-scenario opening employments to one
    /// business. Mirrors ScenarioMillOpeningEquipmentEndowment (the mill
    /// asset installer) but registers people, not assets: each listed
    /// BusinessEmployeeDefinition for the business key becomes one
    /// EmploymentRelationship in the employment registry, so the hire is a
    /// real employment with a real agreed wage — the business pays it
    /// through the normal payroll path, and the camp roster reads it
    /// through the same GetActiveByEmployer the production code already
    /// uses. Zero by default, NewGame-only in the bootstrapper's call,
    /// idempotent via the BusinessRuntimeState endowment marker (plus the
    /// registry's first-wins duplicate-id rejection), and never changes
    /// cash at application time — wages are paid by payroll, never here.
    /// The registry used is the runtime state's when one is already wired
    /// (the shared wiring points it at the hub registry) and the hub-owned
    /// fallback otherwise, assigned back into the runtime state — so the
    /// roster, payroll, and hub save seam all see the same registry, and a
    /// Continue restores the employment from the hub save seam while the
    /// marker keeps a second wiring call a no-op.
    /// </summary>
    public static class ScenarioOpeningEmploymentEndowment
    {
        public static bool TryApply(
            DevelopmentTownConfiguration configuration,
            string businessKey,
            string businessInstanceId,
            BusinessRuntimeState businessRuntimeState,
            EmploymentRelationshipRegistry fallbackRegistry,
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
                || businessRuntimeState == null)
            {
                error = "Development opening employment requires a valid configuration, configured business, business instance id, and business runtime state.";
                return false;
            }

            // The registry is the hub-owned one whenever the runtime state
            // has none yet (mirrors WireEmploymentRegistryToBusinesses):
            // the employment must live where the hub save seam persists it,
            // or a Continue would lose the hire while the marker says it
            // was applied.
            EmploymentRelationshipRegistry registry = businessRuntimeState.EmploymentRegistry ?? fallbackRegistry;
            if (registry == null)
            {
                error = $"Opening employment for '{businessKey}' refused: no employment registry is available.";
                return false;
            }
            if (businessRuntimeState.EmploymentRegistry == null)
                businessRuntimeState.EmploymentRegistry = registry;

            // Idempotence FIRST, before validation: the validation below
            // refuses an employment id that is already registered, and a
            // legitimate second apply is exactly that state (the first
            // apply registered these ids and recorded the marker). Checking
            // the marker here makes the second call a no-op; the registry
            // duplicate-id check below then only fires for an id registered
            // by some other path without the marker, which is a genuine
            // refusal.
            string endowmentId = $"development-town-opening-employment:{configuration.ConfigurationId}:{businessKey}:{businessInstanceId}";
            if (businessRuntimeState.HasAppliedScenarioEndowment(endowmentId))
            {
                report = $"Opening employment already recorded for {businessInstanceId}; no employment re-registered.";
                return true;
            }

            // Validate everything before mutating anything, so a refusal
            // registers nothing.
            var hires = new List<(DevelopmentTownConfiguration.BusinessEmployeeDefinition entry, int personId, string employmentId)>();
            var seenPersons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // K1: direct owner-record iteration. The deleted OpeningEmployees
            // view flattened each business's opening.employees; the owning
            // business is already resolved above, so iterate its employees
            // directly with the same null guard.
            foreach (DevelopmentTownConfiguration.BusinessEmployeeDefinition entry in business.opening?.employees ?? Array.Empty<DevelopmentTownConfiguration.BusinessEmployeeDefinition>())
            {
                if (entry == null) continue;
                if (string.IsNullOrWhiteSpace(entry.personKey) || string.IsNullOrWhiteSpace(entry.role)
                    || entry.weeklyWageCents <= 0)
                {
                    error = $"Opening employment for '{businessKey}' contains an entry with a blank person, blank role, or non-positive wage — nothing was registered.";
                    return false;
                }
                if (string.Equals(entry.personKey, business.ownerPersonKey, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Opening employment for '{businessKey}' lists its owner '{entry.personKey}' as an employee — nothing was registered.";
                    return false;
                }
                if (!seenPersons.Add(entry.personKey.Trim()))
                {
                    error = $"Opening employment for '{businessKey}' lists '{entry.personKey}' twice — nothing was registered.";
                    return false;
                }
                int personId = configuration.ResolveRuntimePersonId(entry.personKey);
                if (personId < 0)
                {
                    error = $"Opening employment for '{businessKey}' references unresolvable person '{entry.personKey}' — nothing was registered.";
                    return false;
                }
                string employmentId = $"opening-employment:{configuration.ConfigurationId}:{businessKey}:{entry.personKey.Trim()}";
                if (registry.TryGetById(employmentId, out _))
                {
                    error = $"Opening employment for '{businessKey}' lists '{entry.personKey}' under an id that is already registered — nothing was registered.";
                    return false;
                }
                hires.Add((entry, personId, employmentId));
            }

            var registered = new List<string>();
            foreach ((DevelopmentTownConfiguration.BusinessEmployeeDefinition entry, int personId, string employmentId) in hires)
            {
                var relationship = new EmploymentRelationship
                {
                    Id = employmentId,
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessInstanceId,
                    RoleDisplayName = entry.role.Trim(),
                    Kind = EmploymentKind.Permanent,
                    LifecycleState = EmploymentLifecycleState.Active,
                    Compensation = CompensationTerms.FromWeeklyWage(entry.weeklyWageCents, "development-town opening employment"),
                    StartDayIndex = dayIndex,
                    EndDayIndex = -1,
                    Source = EmploymentSource.Authored,
                };
                string refusal = registry.Register(relationship);
                if (!string.IsNullOrWhiteSpace(refusal))
                {
                    error = $"Opening employment for '{businessKey}' could not register '{entry.personKey}': {refusal}.";
                    return false;
                }
                registered.Add($"'{entry.personKey.Trim()}' as {entry.role.Trim()} at {entry.weeklyWageCents}c/week");
                diag.Add($"ScenarioOpeningEmploymentEndowment: registered opening employment {employmentId} for {businessInstanceId}.");
            }

            if (!businessRuntimeState.RecordAppliedScenarioEndowment(endowmentId))
            {
                error = $"Could not record opening employment allocation '{endowmentId}'.";
                return false;
            }

            report = registered.Count == 0
                ? $"No opening employment is configured for business '{businessKey}'; no employment was registered."
                : $"Applied configured opening employment to '{businessKey}' ({businessInstanceId}): {string.Join("; ", registered)}.";
            return true;
        }
    }
}
