using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.DraftPower
{
    /// <summary>
    /// EQU-3: the horse + implement + harness + driver work unit. Canon: "PlowField
    /// may require a plow, draft power and harness." Tech X §3.6: motive power is a
    /// genuine constraint — equipment requires a compatible power source/team before
    /// reservation succeeds. The unit is assembled, validated LOUDLY, worked, and
    /// released; a missing piece refuses the work, never fakes it.
    /// </summary>
    public sealed class DraftWorkUnit
    {
        public string UnitId = string.Empty;
        public List<EntityId> TeamAnimalIds = new List<EntityId>();
        public EntityId DriverPersonId = EntityId.Invalid;
        public EquipmentAsset Implement; // the plow
        public EquipmentAsset Harness;
        public string Purpose = string.Empty;

        /// <summary>Calibration: a real plow team. Tuning, not canon.</summary>
        public const int PlowTeamSize = 2;
    }

    public static class DraftWorkUnitService
    {
        /// <summary>
        /// Assembles a plow unit: reserves the team + driver through the one authority,
        /// reserves the implement + harness assets. Every piece is real or the unit
        /// fails loudly — no team, no plowing; no driver, no plowing.
        /// </summary>
        public static DraftWorkUnit AssemblePlowUnit(
            DraftPowerService draftPower,
            AnimalRegistry registry,
            List<EntityId> availableHorses,
            List<EntityId> availableDrivers,
            EquipmentAsset plow,
            EquipmentAsset harness,
            string purpose,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (draftPower == null)
            {
                diagnostics.Add("DraftWorkUnitService: no draft-power authority.");
                return null;
            }

            if (!draftPower.TryReserveAnimals(availableHorses, DraftWorkUnit.PlowTeamSize,
                id => HorseTrade.IsDraftCapable(registry.GetAnimal(id)),
                purpose ?? "plowing", out List<EntityId> team, diagnostics))
            {
                diagnostics.Add("DraftWorkUnitService: plow unit refused — no team, no plowing.");
                return null;
            }

            if (!draftPower.TryReserveDriver(availableDrivers, out EntityId driver, diagnostics))
            {
                draftPower.ReleaseAnimals(team);
                diagnostics.Add("DraftWorkUnitService: plow unit refused — no driver, no plowing.");
                return null;
            }

            string implementProblem = CheckImplement(plow, "plow");
            if (implementProblem != null)
            {
                draftPower.ReleaseAnimals(team);
                draftPower.ReleaseDriver(driver);
                diagnostics.Add("DraftWorkUnitService: plow unit refused — " + implementProblem);
                return null;
            }

            string harnessProblem = CheckImplement(harness, "harness");
            if (harnessProblem != null)
            {
                draftPower.ReleaseAnimals(team);
                draftPower.ReleaseDriver(driver);
                diagnostics.Add("DraftWorkUnitService: plow unit refused — " + harnessProblem);
                return null;
            }

            string conflict = plow.Reserve($"unit-team:{driver}", purpose ?? "plowing");
            if (conflict != null)
            {
                draftPower.ReleaseAnimals(team);
                draftPower.ReleaseDriver(driver);
                diagnostics.Add("DraftWorkUnitService: plow unit refused — " + conflict);
                return null;
            }
            harness.Reserve($"unit-team:{driver}", purpose ?? "plowing");

            var unit = new DraftWorkUnit
            {
                UnitId = $"UNIT-{Guid.NewGuid():N}".Substring(0, 13),
                TeamAnimalIds = team,
                DriverPersonId = driver,
                Implement = plow,
                Harness = harness,
                Purpose = purpose ?? "plowing",
            };
            diagnostics.Add(
                $"DraftWorkUnitService: plow unit {unit.UnitId} assembled — {team.Count} horses, " +
                $"driver {driver}, {plow.DisplayName} ({plow.AssetId}), harness {harness.AssetId}.");
            return unit;
        }

        private static string CheckImplement(EquipmentAsset asset, string expectedKind)
        {
            if (asset == null)
                return $"no {expectedKind} provided.";
            if (!string.Equals(asset.Kind, expectedKind, StringComparison.Ordinal))
                return $"wrong implement: '{asset.Kind}' is not a {expectedKind} — refused loudly.";
            if (!asset.IsUsable)
                return $"{expectedKind} {asset.AssetId} is wrecked (condition {asset.Condition01:0.00}) — repair it first (Tech X §3.9).";
            if (asset.IsReserved)
                return $"{expectedKind} {asset.AssetId} is already reserved — no double-booking (Tech X §3.8).";
            return null;
        }

        /// <summary>
        /// Works one plow session with the unit: wears the implement (Tech X §3.9)
        /// and reports the working horses so the caller feeds them honestly —
        /// working horses eat more than idle ones.
        /// </summary>
        public static string WorkPlowSession(
            DraftWorkUnit unit,
            float implementWear01,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (unit == null) return "DraftWorkUnitService: no unit to work.";
            unit.Implement.ApplyWear(Math.Max(0f, implementWear01));
            diagnostics.Add(
                $"DraftWorkUnitService: unit {unit.UnitId} worked — {unit.Implement.DisplayName} " +
                $"condition now {unit.Implement.Condition01:0.00}.");
            return null;
        }

        /// <summary>Releases every reservation the unit holds.</summary>
        public static void ReleaseUnit(DraftWorkUnit unit, DraftPowerService draftPower)
        {
            if (unit == null || draftPower == null) return;
            draftPower.ReleaseAnimals(unit.TeamAnimalIds);
            draftPower.ReleaseDriver(unit.DriverPersonId);
            unit.Implement?.Release();
            unit.Harness?.Release();
        }
    }
}
