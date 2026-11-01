using System;
using System.Collections.Generic;
using LandLedgers.Tasks;

namespace LandLedgers.Population
{
    /// <summary>
    /// Worker C (Pass III), brief §12: a NARROW set of representative domestic
    /// task definitions on the EXISTING TaskAuthority — no second task system.
    ///
    /// Four families: meal preparation, fuel handling, animal care, basic
    /// chores. Domestic work uses real Person time (PersonScheduleTracker +
    /// WorkTimeBudgetStore) and is credited to FamilyLaborLedger — never wages
    /// (PL-04 taxonomy: family/domestic work is not payroll). It competes with
    /// employment availability through person-time exclusivity: the tracker
    /// refuses overlapping reservations, so a person cannot be independently
    /// scheduled for incompatible domestic and employment work.
    ///
    /// learningTags route into Worker D's experience formation; requiredSkillId
    /// scales duration only (existing estimator seam) — capability never gates
    /// ordinary domestic work (Canon XXXI Part III §3.1).
    /// </summary>
    public static class DomesticTaskCatalog
    {
        public const string MealPrepId = "domestic.meal-prep";
        public const string FuelHandlingId = "domestic.fuel-handling";
        public const string AnimalCareId = "domestic.animal-care";
        public const string ChoresId = "domestic.chores";

        // Skill ids match SkillIds (Core/Skills/SkillModels.cs) so Worker D's
        // experience formation routes domestic practice correctly. Referenced by
        // value to avoid dragging SkillService into this catalog's footprint.
        private const string SkillCooking = "cooking";
        private const string SkillCleaning = "cleaning";
        private const string SkillAnimalHusbandry = "animal-husbandry";
        private const string SkillBasicRepair = "basic-repair";

        /// <summary>True for definitions owned by this catalog (narrow scope guard).</summary>
        public static bool IsDomesticDefinition(string definitionId) =>
            string.Equals(definitionId, MealPrepId, StringComparison.Ordinal)
            || string.Equals(definitionId, FuelHandlingId, StringComparison.Ordinal)
            || string.Equals(definitionId, AnimalCareId, StringComparison.Ordinal)
            || string.Equals(definitionId, ChoresId, StringComparison.Ordinal);

        public static void RegisterAll(TaskAuthority tasks, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (tasks == null)
            {
                diagnostics.Add("DomesticTaskCatalog: no TaskAuthority — domestic definitions not registered.");
                return;
            }

            Register(tasks, diagnostics, MealPrepId, "Prepare a household meal", 45,
                SkillCooking, new[] { SkillCooking, SkillCleaning });
            Register(tasks, diagnostics, FuelHandlingId, "Fetch and handle household fuel", 30,
                SkillBasicRepair, new[] { SkillBasicRepair });
            Register(tasks, diagnostics, AnimalCareId, "Feed and tend household animals", 40,
                SkillAnimalHusbandry, new[] { SkillAnimalHusbandry });
            Register(tasks, diagnostics, ChoresId, "Basic household chores", 30,
                SkillCleaning, new[] { SkillCleaning });
        }

        private static void Register(TaskAuthority tasks, List<string> diagnostics,
            string id, string displayName, int baseMinutes, string skillId, string[] learningTags)
        {
            var definition = new TaskDefinition(id, displayName, baseMinutes);
            definition.SetRequiredSkill(skillId, learningTags);
            definition.DomainTags.Add("domestic");
            definition.DomainTags.Add("household");
            string rejection;
            if (!tasks.RegisterDefinition(definition, out rejection))
                diagnostics.Add("DomesticTaskCatalog: " + id + " not registered — " + rejection);
            else
                diagnostics.Add("DomesticTaskCatalog: registered " + id + " (" + baseMinutes + " min).");
        }
    }
}
