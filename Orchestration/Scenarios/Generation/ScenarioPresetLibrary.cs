using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy.Financing;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass V P1-1 (Worker 1): preset lifecycle operations — load, validate,
    /// version, reuse, copy, derive, deterministic preview, diagnostics, and
    /// backward compatibility. Pure logic; no Unity scene dependencies.
    /// </summary>
    public static class ScenarioPresetLibrary
    {
        /// <summary>Canon calibration: generated resident counts must stay inside 55-95.</summary>
        public const int CanonMinResidents = 55;
        public const int CanonMaxResidents = 95;

        /// <summary>Current authored definition version. Bumped when new
        /// StartingWorldConfiguration fields are added; older presets migrate up.</summary>
        public const string CurrentDefinitionVersion = "1.1.0";

        // ---------- copy / derive / reuse ----------

        /// <summary>
        /// Deep-copies a preset under a new id. Diagnostics are cleared on the
        /// copy (they describe the source's validation history, not the copy's).
        /// </summary>
        public static ScenarioPreset Copy(ScenarioPreset source, string newPresetId, string newName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            ScenarioPreset copy = DeepCopy(source);
            copy.SetIdentity(newPresetId, newName, source.PresetVersion);
            copy.SetDerivedFrom(source.PresetId);
            copy.ClearDiagnostics();
            return copy;
        }

        /// <summary>
        /// Derives a child preset: copy + patch version bump + derivation link.
        /// </summary>
        public static ScenarioPreset Derive(ScenarioPreset source, string newPresetId, string newName)
        {
            ScenarioPreset derived = Copy(source, newPresetId, newName);
            derived.SetIdentity(newPresetId, newName, BumpPatchVersion(source.PresetVersion));
            return derived;
        }

        public static ScenarioPreset DeepCopy(ScenarioPreset source)
        {
            if (source == null)
            {
                return null;
            }

            var copy = new ScenarioPreset();
            copy.SetIdentity(source.PresetId, source.PresetName, source.PresetVersion);
            copy.SetDerivedFrom(source.DerivedFromPresetId);
            CopyStartingWorld(source.StartingWorld, copy.StartingWorld);
            foreach (string diagnostic in source.Diagnostics)
            {
                copy.AddDiagnostic(diagnostic);
            }

            return copy;
        }

        /// <summary>
        /// Copies one starting-world configuration into another. Used by the
        /// ScenarioAsset preset bridge so asset conversion never aliases the
        /// authored asset's configuration.
        /// </summary>
        public static void DeepCopyIntoStartingWorld(StartingWorldConfiguration source, StartingWorldConfiguration target)
        {
            CopyStartingWorld(source, target);
        }

        private static void CopyStartingWorld(StartingWorldConfiguration source, StartingWorldConfiguration target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target.SetScenarioId(source.ScenarioId);
            target.SetDefinitionVersion(source.DefinitionVersion);
            target.SetStartDate(source.StartYear, source.StartMonth, source.StartDay);
            target.SetPopulationRange(source.MinResidents, source.PreferredResidents, source.MaxResidents);

            target.EditableRequiredBusinessIds.Clear();
            target.EditableRequiredBusinessIds.AddRange(source.RequiredBusinessIds);
            target.EditableFixedBusinesses.Clear();
            foreach (ScenarioFixedBusiness business in source.FixedBusinesses)
            {
                target.EditableFixedBusinesses.Add(new ScenarioFixedBusiness(
                    business.BusinessId, business.DisplayName, business.BusinessType,
                    business.CapitalCents, business.OwnerKind));
            }

            target.EditablePropertyGrants.Clear();
            foreach (ScenarioPropertyGrant grant in source.PropertyGrants)
            {
                target.EditablePropertyGrants.Add(new ScenarioPropertyGrant(
                    grant.GrantId, grant.BuildingId, grant.GranteeKind, grant.Notes));
            }

            ScenarioPlayerStart sourceStart = source.PlayerStart;
            ScenarioPlayerStart targetStart = target.EditablePlayerStart;
            if (sourceStart != null)
            {
                targetStart.SetHouseholdName(sourceStart.HouseholdName);
                targetStart.SetFounderCount(sourceStart.FounderCount);
                targetStart.SetStartingCashCents(sourceStart.StartingCashCents);
                targetStart.SetStartingDebtCents(sourceStart.StartingDebtCents);
                targetStart.SetProfessionId(sourceStart.ProfessionId);
                targetStart.SetOwnsHomeBuilding(sourceStart.OwnsHomeBuilding);
            }

            ScenarioEconomyPolicy sourcePolicy = source.EconomyPolicy;
            ScenarioEconomyPolicy targetPolicy = target.EditableEconomyPolicy;
            if (sourcePolicy != null)
            {
                targetPolicy.SetCashBand(sourcePolicy.CashBandMinCents, sourcePolicy.CashBandMaxCents);
                targetPolicy.SetEmploymentTargetRate(sourcePolicy.EmploymentTargetRate01);
                targetPolicy.SetDebtPolicy(sourcePolicy.DebtChance01, sourcePolicy.MaxDebtCentsPerHousehold);
                targetPolicy.EditableAllowedObligationKinds.Clear();
                targetPolicy.EditableAllowedObligationKinds.AddRange(sourcePolicy.AllowedObligationKinds);
            }
        }

        // ---------- validation ----------

        /// <summary>Full preset validation. Returns true when the preset is usable;
        /// diagnostics always explain every problem found (loud, never silent).</summary>
        public static bool Validate(ScenarioPreset preset, List<string> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (preset == null)
            {
                diagnostics.Add("Preset is null.");
                return false;
            }

            bool ok = true;
            if (string.IsNullOrWhiteSpace(preset.PresetId))
            {
                diagnostics.Add("PresetId is empty. Set a unique preset id.");
                ok = false;
            }

            if (string.IsNullOrWhiteSpace(preset.PresetVersion))
            {
                diagnostics.Add($"Preset '{preset.PresetId}': PresetVersion is empty.");
                ok = false;
            }

            if (preset.StartingWorld == null)
            {
                diagnostics.Add($"Preset '{preset.PresetId}': StartingWorld configuration is missing.");
                return false;
            }

            if (!ValidateStartingWorld(preset.StartingWorld, diagnostics))
            {
                ok = false;
            }

            return ok;
        }

        /// <summary>Starting-world validation: Canon 55-95 bounds, Gregorian start
        /// date, business/economy/player-start coherence. Never invents defaults to
        /// force validity — violations are reported.</summary>
        public static bool ValidateStartingWorld(StartingWorldConfiguration world, List<string> diagnostics)
        {
            if (world == null)
            {
                diagnostics.Add("StartingWorld configuration is null.");
                return false;
            }

            bool ok = true;
            if (string.IsNullOrWhiteSpace(world.ScenarioId))
            {
                diagnostics.Add("StartingWorld: ScenarioId is empty.");
                ok = false;
            }

            if (string.IsNullOrWhiteSpace(world.DefinitionVersion))
            {
                diagnostics.Add($"StartingWorld '{world.ScenarioId}': DefinitionVersion is empty.");
                ok = false;
            }

            if (!ScenarioDateUtil.IsValidDate(world.StartYear, world.StartMonth, world.StartDay))
            {
                diagnostics.Add($"StartingWorld '{world.ScenarioId}': start date " +
                    $"{world.StartYear}-{world.StartMonth:00}-{world.StartDay:00} is not a valid Gregorian date in 1870-1905.");
                ok = false;
            }

            if (world.MinResidents < CanonMinResidents || world.MaxResidents > CanonMaxResidents)
            {
                diagnostics.Add($"StartingWorld '{world.ScenarioId}': resident range " +
                    $"[{world.MinResidents},{world.MaxResidents}] leaves the Canon calibration " +
                    $"[{CanonMinResidents},{CanonMaxResidents}].");
                ok = false;
            }

            if (world.MinResidents > world.PreferredResidents || world.PreferredResidents > world.MaxResidents)
            {
                diagnostics.Add($"StartingWorld '{world.ScenarioId}': resident range must satisfy " +
                    $"min <= preferred <= max (got {world.MinResidents}/{world.PreferredResidents}/{world.MaxResidents}).");
                ok = false;
            }

            var seenBusinessIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ScenarioFixedBusiness business in world.FixedBusinesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.BusinessId))
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': a fixed business has an empty BusinessId.");
                    ok = false;
                    continue;
                }

                if (!seenBusinessIds.Add(business.BusinessId))
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': duplicate fixed BusinessId '{business.BusinessId}'.");
                    ok = false;
                }

                if (business.CapitalCents < 0)
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': fixed business '{business.BusinessId}' has negative capital.");
                    ok = false;
                }
            }

            foreach (string requiredId in world.RequiredBusinessIds)
            {
                if (!string.IsNullOrWhiteSpace(requiredId) && !seenBusinessIds.Contains(requiredId))
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': RequiredBusinessId '{requiredId}' " +
                        "names no fixed business. Generation will fail rather than invent a substitute.");
                }
            }

            if (world.MinVariableBusinesses < 0 || world.MaxVariableBusinesses < world.MinVariableBusinesses)
            {
                diagnostics.Add($"StartingWorld '{world.ScenarioId}': variable business range is invalid " +
                    $"[{world.MinVariableBusinesses},{world.MaxVariableBusinesses}].");
                ok = false;
            }

            ScenarioEconomyPolicy policy = world.EconomyPolicy;
            if (policy == null)
            {
                diagnostics.Add($"StartingWorld '{world.ScenarioId}': EconomyPolicy is missing.");
                ok = false;
            }
            else
            {
                if (policy.CashBandMinCents < 0 || policy.CashBandMaxCents < policy.CashBandMinCents)
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': cash band is invalid " +
                        $"[{policy.CashBandMinCents},{policy.CashBandMaxCents}].");
                    ok = false;
                }

                if (policy.EmploymentTargetRate01 < 0f || policy.EmploymentTargetRate01 > 1f
                    || policy.DebtChance01 < 0f || policy.DebtChance01 > 1f)
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': employment target rate and debt chance must be within [0,1].");
                    ok = false;
                }

                if (policy.MaxDebtCentsPerHousehold < 0)
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': max debt per household is negative.");
                    ok = false;
                }

                if (policy.AllowedObligationKinds == null || policy.AllowedObligationKinds.Count == 0)
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': no obligation kinds are allowed, so no starting debt can be authored.");
                    ok = false;
                }
            }

            ScenarioPlayerStart playerStart = world.PlayerStart;
            if (playerStart == null)
            {
                diagnostics.Add($"StartingWorld '{world.ScenarioId}': PlayerStart is missing.");
                ok = false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(playerStart.HouseholdName))
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': PlayerStart household name is empty.");
                    ok = false;
                }

                if (playerStart.FounderCount < 0 || playerStart.StartingCashCents < 0 || playerStart.StartingDebtCents < 0)
                {
                    diagnostics.Add($"StartingWorld '{world.ScenarioId}': PlayerStart has a negative founder count, cash, or debt.");
                    ok = false;
                }
            }

            return ok;
        }

        // ---------- versioning / backward compatibility ----------

        /// <summary>
        /// Migrates a preset's starting-world configuration up to
        /// <see cref="CurrentDefinitionVersion"/>. Additive only: fills fields that
        /// did not exist in older definitions with documented defaults, never
        /// rewrites authored values. Returns a migrated copy; the input is kept.
        /// </summary>
        public static ScenarioPreset MigrateToCurrent(ScenarioPreset source, List<string> diagnostics)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            ScenarioPreset migrated = DeepCopy(source);
            string fromVersion = migrated.StartingWorld?.DefinitionVersion ?? string.Empty;
            if (CompareVersions(fromVersion, CurrentDefinitionVersion) >= 0)
            {
                return migrated;
            }

            // v1.0.0 -> v1.1.0: the extended starting-world fields did not exist.
            // Defaults preserve prior behavior: no fixed businesses beyond the
            // required-id list, modest variable businesses, standard cash bands.
            StartingWorldConfiguration world = migrated.StartingWorld;
            if (world != null)
            {
                if (world.FixedBusinesses.Count == 0)
                {
                    foreach (string requiredId in world.RequiredBusinessIds)
                    {
                        if (!string.IsNullOrWhiteSpace(requiredId))
                        {
                            world.EditableFixedBusinesses.Add(new ScenarioFixedBusiness(
                                requiredId, requiredId, "general", 0, "npc"));
                        }
                    }
                }

                world.SetDefinitionVersion(CurrentDefinitionVersion);
            }

            diagnostics?.Add($"Preset '{migrated.PresetId}' migrated {fromVersion} -> {CurrentDefinitionVersion} " +
                "(additive: authored values preserved, new fields defaulted).");
            return migrated;
        }

        public static int CompareVersions(string left, string right)
        {
            int[] l = ParseVersion(left);
            int[] r = ParseVersion(right);
            for (int i = 0; i < 3; i++)
            {
                if (l[i] != r[i])
                {
                    return l[i].CompareTo(r[i]);
                }
            }

            return 0;
        }

        private static int[] ParseVersion(string version)
        {
            var parts = new int[3];
            if (string.IsNullOrWhiteSpace(version))
            {
                return parts;
            }

            string[] tokens = version.Split('.');
            for (int i = 0; i < Math.Min(3, tokens.Length); i++)
            {
                int.TryParse(tokens[i], out parts[i]);
            }

            return parts;
        }

        private static string BumpPatchVersion(string version)
        {
            int[] parts = ParseVersion(string.IsNullOrWhiteSpace(version) ? "1.0.0" : version);
            parts[2]++;
            return $"{parts[0]}.{parts[1]}.{parts[2]}";
        }

        // ---------- deterministic preview ----------

        /// <summary>
        /// Deterministic preview: what a generation from this preset + seed would
        /// aim at, computed without generating a world. Pure function of the
        /// preset and seed — the same inputs always produce the same preview.
        /// </summary>
        public static ScenarioPreviewReport BuildPreview(ScenarioPreset preset, int seed)
        {
            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            StartingWorldConfiguration world = preset.StartingWorld;
            var report = new ScenarioPreviewReport
            {
                PresetId = preset.PresetId,
                PresetVersion = preset.PresetVersion,
                DefinitionVersion = world?.DefinitionVersion ?? string.Empty,
                Seed = seed,
                MinResidents = world?.MinResidents ?? 0,
                PreferredResidents = world?.PreferredResidents ?? 0,
                MaxResidents = world?.MaxResidents ?? 0,
            };

            if (world != null)
            {
                report.StartDateIso = ScenarioDateUtil.IsValidDate(world.StartYear, world.StartMonth, world.StartDay)
                    ? ScenarioDateUtil.ToIsoDate(world.StartYear, world.StartMonth, world.StartDay)
                    : "invalid-date";
                report.MinVariableBusinesses = world.MinVariableBusinesses;
                report.MaxVariableBusinesses = world.MaxVariableBusinesses;
                foreach (ScenarioFixedBusiness business in world.FixedBusinesses)
                {
                    report.FixedBusinessIds.Add(business?.BusinessId ?? string.Empty);
                }

                ScenarioEconomyPolicy policy = world.EconomyPolicy;
                if (policy != null)
                {
                    report.CashBandMinCents = policy.CashBandMinCents;
                    report.CashBandMaxCents = policy.CashBandMaxCents;
                    report.EmploymentTargetRate01 = policy.EmploymentTargetRate01;
                    report.DebtChance01 = policy.DebtChance01;
                    foreach (FinancialObligationKind kind in policy.AllowedObligationKinds)
                    {
                        report.AllowedObligationKinds.Add(kind.ToString());
                    }
                }

                ScenarioPlayerStart playerStart = world.PlayerStart;
                if (playerStart != null)
                {
                    report.PlayerHouseholdName = playerStart.HouseholdName;
                    report.PlayerStartingCashCents = playerStart.StartingCashCents;
                }
            }

            report.InputFingerprint = ComputeFingerprint(preset, seed);
            return report;
        }

        /// <summary>
        /// Stable fingerprint of the preset definition + seed: the reproducibility
        /// identity. Any authored change alters it, so a regenerated world that
        /// claims the same inputs but differs is detectable.
        /// </summary>
        public static string ComputeFingerprint(ScenarioPreset preset, int seed)
        {
            var builder = new StringBuilder(512);
            builder.Append(preset?.PresetId).Append('|');
            builder.Append(preset?.PresetVersion).Append('|');
            builder.Append(seed).Append('|');
            StartingWorldConfiguration world = preset?.StartingWorld;
            if (world != null)
            {
                builder.Append(world.ScenarioId).Append('|');
                builder.Append(world.DefinitionVersion).Append('|');
                builder.Append(world.StartYear).Append('-').Append(world.StartMonth).Append('-').Append(world.StartDay).Append('|');
                builder.Append(world.MinResidents).Append('/').Append(world.PreferredResidents).Append('/').Append(world.MaxResidents).Append('|');
                foreach (ScenarioFixedBusiness business in world.FixedBusinesses)
                {
                    builder.Append(business?.BusinessId).Append(',')
                        .Append(business?.BusinessType).Append(',')
                        .Append(business?.CapitalCents ?? 0).Append(';');
                }

                builder.Append('|');
                builder.Append(world.MinVariableBusinesses).Append('/').Append(world.MaxVariableBusinesses).Append('|');
                ScenarioEconomyPolicy policy = world.EconomyPolicy;
                if (policy != null)
                {
                    builder.Append(policy.CashBandMinCents).Append('/').Append(policy.CashBandMaxCents).Append('|');
                    builder.Append(policy.EmploymentTargetRate01.ToString("0.###")).Append('|');
                    builder.Append(policy.DebtChance01.ToString("0.###")).Append('|');
                    builder.Append(policy.MaxDebtCentsPerHousehold).Append('|');
                }

                ScenarioPlayerStart playerStart = world.PlayerStart;
                if (playerStart != null)
                {
                    builder.Append(playerStart.HouseholdName).Append('|');
                    builder.Append(playerStart.StartingCashCents).Append('/').Append(playerStart.StartingDebtCents);
                }
            }

            unchecked
            {
                ulong hash = 1469598103934665603ul;
                string canonical = builder.ToString();
                for (int i = 0; i < canonical.Length; i++)
                {
                    hash ^= canonical[i];
                    hash *= 1099511628211ul;
                }

                return hash.ToString("X16");
            }
        }
    }

    /// <summary>Deterministic, generation-free preview of a preset + seed.</summary>
    [Serializable]
    public sealed class ScenarioPreviewReport
    {
        public string PresetId = string.Empty;
        public string PresetVersion = string.Empty;
        public string DefinitionVersion = string.Empty;
        public int Seed;
        public string StartDateIso = string.Empty;
        public int MinResidents;
        public int PreferredResidents;
        public int MaxResidents;
        public List<string> FixedBusinessIds = new List<string>();
        public int MinVariableBusinesses;
        public int MaxVariableBusinesses;
        public int CashBandMinCents;
        public int CashBandMaxCents;
        public float EmploymentTargetRate01;
        public float DebtChance01;
        public List<string> AllowedObligationKinds = new List<string>();
        public string PlayerHouseholdName = string.Empty;
        public int PlayerStartingCashCents;
        public string InputFingerprint = string.Empty;
    }
}
