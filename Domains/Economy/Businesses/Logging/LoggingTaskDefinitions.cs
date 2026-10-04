using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>W4C: the logging task ids — the steps below the sawmill on the
    /// Canon §8.5A causal chain (standing timber -> felling -> extraction /
    /// skidding / hauling -> log landing or pond -> mill intake).</summary>
    public static class LoggingTaskIds
    {
        /// <summary>Skid felled logs from stump to the landing (draft team work).</summary>
        public const string SkidLogs = "skid-logs";
        /// <summary>Limb and buck felled trees into log lengths (crosscut/buck saw).</summary>
        public const string LimbBuckLogs = "limb-buck-logs";
        /// <summary>Load logs onto the wagon at the landing (cant-hook work).</summary>
        public const string LoadLogWagon = "load-log-wagon";
        /// <summary>Haul log lots from the landing to the sawmill log yard.</summary>
        public const string HaulLogsToMill = "haul-logs-to-mill";
    }

    /// <summary>
    /// W4C: the logging task definitions, registered through the TTS-2 task
    /// system — every task is skill-gated (forestry) and equipment-gated per
    /// NX-1 (Canon 4.1): equipment classes are DATA, and a task with no
    /// usable equipment is refused at execution, never silently worked.
    ///
    /// The "fell-timber" task stays owned by T1E (TimberHarvest
    /// .RegisterTaskDefinitions: forestry skill, felling-axe OR crosscut-saw)
    /// — this package never re-registers it; duplicates are rejected
    /// deterministically by TaskAuthority anyway.
    ///
    /// Calibration (canon-grounded, cf. Canon §8.5A and the logger/feller tool
    /// list: felling axe, crosscut saw, cant hook/peavey, chains/ropes, draft
    /// skidding team):
    /// - felling itself: T1E's FellMinutesPerLog = 90/log (crew of two, hand
    ///   tools) — referenced, not redefined;
    /// - skidding: the slow haul from stump to landing behind a draft team;
    /// - limb/buck: hand-saw work on the felled tree;
    /// - loading: cant-hook/parbuckle work at the landing;
    /// - hauling: journey-model minutes carry the travel; the task covers
    ///   loading + unloading handling per log.
    /// </summary>
    public static class LoggingTaskDefinitions
    {
        /// <summary>Calibration: minutes to skid one log from stump to landing.</summary>
        public const int SkidMinutesPerLog = 60;
        /// <summary>Calibration: minutes to limb and buck one felled tree into log lengths.</summary>
        public const int LimbBuckMinutesPerLog = 30;
        /// <summary>Calibration: minutes to load one log onto the wagon.</summary>
        public const int LoadMinutesPerLog = 15;
        /// <summary>Calibration: handling minutes per log on a haul (loading + unloading); travel comes from the journey model.</summary>
        public const int HaulHandlingMinutesPerLog = 10;

        /// <summary>
        /// Registers the four W4C logging task definitions. The forestry skill
        /// is ensured first through the T1E registration (idempotent).
        /// "fell-timber" is deliberately NOT registered here.
        /// </summary>
        public static void RegisterTaskDefinitions(
            TaskAuthority authority, SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (authority == null)
            {
                diagnostics.Add("LoggingTaskDefinitions: no TaskAuthority — logging tasks not registered.");
                return;
            }

            // T1E owns the forestry skill registration (TTS-3 extension path);
            // re-running it is a no-op once registered.
            TimberHarvest.RegisterSkills(skillService, diagnostics);

            RegisterOne(authority, diagnostics, new TaskDefinition(
                LoggingTaskIds.SkidLogs, "Skid logs", SkidMinutesPerLog),
                TimberHarvest.ForestrySkillId, new[] { "skidding" },
                new[]
                {
                    // NX-1A: coded — skidding is draft-team work (Canon §8.5A:
                    // extraction/skidding; Canon tool list: draft skidding
                    // team, chains/ropes). No team, no skidding.
                    EquipmentRequirementCodes.Asset("draft-skid-team"),
                });

            RegisterOne(authority, diagnostics, new TaskDefinition(
                LoggingTaskIds.LimbBuckLogs, "Limb and buck logs", LimbBuckMinutesPerLog),
                TimberHarvest.ForestrySkillId, new[] { "limbing", "bucking" },
                new[]
                {
                    // NX-1A: coded — limb and buck with the crosscut or the
                    // buck saw (Canon 4.1; either hand method is real).
                    EquipmentRequirementCodes.Asset("crosscut-saw") + "|" +
                    EquipmentRequirementCodes.Asset("buck-saw"),
                });

            RegisterOne(authority, diagnostics, new TaskDefinition(
                LoggingTaskIds.LoadLogWagon, "Load log wagon", LoadMinutesPerLog),
                TimberHarvest.ForestrySkillId, new[] { "loading" },
                new[]
                {
                    // NX-1A: coded — cant-hook/parbuckle loading (Canon tool
                    // list: cant hook/peavey). No hook, no loading.
                    EquipmentRequirementCodes.Asset("cant-hook"),
                });

            RegisterOne(authority, diagnostics, new TaskDefinition(
                LoggingTaskIds.HaulLogsToMill, "Haul logs to mill", HaulHandlingMinutesPerLog),
                TimberHarvest.ForestrySkillId, new[] { "hauling" },
                new[]
                {
                    // NX-1A: coded — hauling needs a real wagon (Canon §8.5A:
                    // extraction/skidding/hauling). No wagon, no haul.
                    EquipmentRequirementCodes.Asset("log-wagon"),
                });
        }

        private static void RegisterOne(
            TaskAuthority authority,
            List<string> diagnostics,
            TaskDefinition definition,
            string skillId,
            string[] learningTags,
            string[] equipmentClasses)
        {
            definition.SetRequiredSkill(skillId, learningTags);
            foreach (var code in equipmentClasses)
                definition.EquipmentClasses.Add(code);

            string rejection;
            if (authority.RegisterDefinition(definition, out rejection))
            {
                diagnostics.Add($"LoggingTaskDefinitions: registered '{definition.DefinitionId}' " +
                    $"(forestry skill; equipment: {string.Join(", ", equipmentClasses)}).");
            }
            else
            {
                diagnostics.Add($"LoggingTaskDefinitions: '{definition.DefinitionId}' not registered: {rejection}");
            }
        }
    }
}
