using System;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;

namespace LandLedgers.Economy.Businesses.Newspaper
{
    /// <summary>
    /// NX-3A: newspaper work as data (TTS-5 pattern). Typesetting by hand and
    /// presswork are skilled labor against the minute quantum — a frontier
    /// weekly was typically set and printed by the editor plus one devil.
    /// All minute values are calibration (Canon Part XV).
    /// </summary>
    public static class NewspaperTaskCatalog
    {
        public const string TypesetPageId = "news.typeset-page";
        public const string PressworkId = "news.presswork";
        public const string JobPrintId = "news.job-print";

        /// <summary>TUNING: minutes to hand-set one newspaper page.</summary>
        public const int TypesetPageMinutes = 120;

        /// <summary>TUNING: minutes of presswork per 100 copies.</summary>
        public const int PressworkMinutesPerHundred = 30;

        /// <summary>TUNING: minutes per job-print order (setup + run).</summary>
        public const int JobPrintMinutes = 45;

        public const string TypesettingSkillId = "typesetting";
        public const string PressworkSkillId = "presswork";

        /// <summary>Registers the newspaper skills via the TTS-3 extension path.</summary>
        public static void RegisterSkills(SkillService skillService, System.Collections.Generic.List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new System.Collections.Generic.List<string>();
            if (skillService == null)
            {
                diagnostics.Add("NewspaperTaskCatalog: no SkillService — typesetting/presswork skills not registered.");
                return;
            }
            RegisterOne(skillService,
                new SkillDefinition(TypesettingSkillId, "Typesetting",
                    "Hand composition: type cases, composing stick, galley. (TTS-3 extension path; Canon Part V.)"),
                diagnostics);
            RegisterOne(skillService,
                new SkillDefinition(PressworkSkillId, "Presswork",
                    "Operating the hand/cylinder press: inking, feeding, stacking. (TTS-3 extension path; Canon Part V.)"),
                diagnostics);
        }

        private static void RegisterOne(SkillService skillService, SkillDefinition def, System.Collections.Generic.List<string> diagnostics)
        {
            if (skillService.GetSkill(def.SkillId) != null) return;
            if (!skillService.RegisterSkill(def, out string rejection))
                diagnostics.Add($"NewspaperTaskCatalog: skill '{def.SkillId}' rejected: {rejection}");
        }

        /// <summary>Registers the three newspaper task definitions. Safe to call once at boot.</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string ignored;

            var typeset = new TaskDefinition(TypesetPageId, "Typeset newspaper page", TypesetPageMinutes);
            typeset.SetRequiredSkill(TypesettingSkillId, new[] { TypesettingSkillId });
            typeset.SetDefaultPriority(TaskPriority.Normal);
            typeset.DomainTags.Add("printing");
            typeset.DomainTags.Add("skilled-trade");
            authority.RegisterDefinition(typeset, out ignored);

            var press = new TaskDefinition(PressworkId, "Run the press", PressworkMinutesPerHundred);
            press.SetRequiredSkill(PressworkSkillId, new[] { PressworkSkillId });
            press.SetDefaultPriority(TaskPriority.Normal);
            press.DomainTags.Add("printing");
            press.DomainTags.Add("skilled-trade");
            authority.RegisterDefinition(press, out ignored);

            var job = new TaskDefinition(JobPrintId, "Job printing", JobPrintMinutes);
            job.SetRequiredSkill(PressworkSkillId, new[] { PressworkSkillId, TypesettingSkillId });
            job.SetDefaultPriority(TaskPriority.Normal);
            job.DomainTags.Add("printing");
            authority.RegisterDefinition(job, out ignored);
        }
    }
}
