using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-4 Group C: farm implement kinds as data (Canon Part V: CropFarm).
    /// Soil-working tasks require a suitable implement and draft/human power;
    /// each implement gates the task it serves. The hired traveling threshing
    /// outfit is NOT owned equipment — it is a ServiceProvider custody grant
    /// (Canon 6.3; see EquipmentCustody).
    /// </summary>
    public static class FarmImplements
    {
        public sealed class ImplementSpec
        {
            public string Kind;
            public string DisplayName;
            public string GatedTaskId;
            public string SourceNote;
        }

        public static readonly List<ImplementSpec> All = new List<ImplementSpec>
        {
            new ImplementSpec { Kind = "moldboard-plow", DisplayName = "Moldboard Plow", GatedTaskId = "plow-field", SourceNote = "Canon Part V: CropFarm (capacity)" },
            new ImplementSpec { Kind = "harrow", DisplayName = "Harrow", GatedTaskId = "harrow-field", SourceNote = "Canon Part V: CropFarm (capacity)" },
            new ImplementSpec { Kind = "cultivator", DisplayName = "Cultivator", GatedTaskId = "cultivate-field", SourceNote = "Canon Part V: CropFarm (capacity)" },
            new ImplementSpec { Kind = "grain-drill", DisplayName = "Grain Drill", GatedTaskId = "plant-grain", SourceNote = "Canon Part V: CropFarm (grain planting crew, capacity)" },
            new ImplementSpec { Kind = "broadcast-seeder", DisplayName = "Broadcast Seeder", GatedTaskId = "plant-grain", SourceNote = "Canon Part V: CropFarm (capacity)" },
            new ImplementSpec { Kind = "grain-cradle", DisplayName = "Grain Cradle", GatedTaskId = "harvest-grain", SourceNote = "Canon Part V: CropFarm (grain harvest hand)" },
            new ImplementSpec { Kind = "reaper-binder", DisplayName = "Horse-Drawn Reaper / Self-Binder", GatedTaskId = "harvest-grain", SourceNote = "Canon Part V: CropFarm (grain harvest hand, capacity)" },
            new ImplementSpec { Kind = "threshing-separator", DisplayName = "Threshing Separator", GatedTaskId = "thresh-grain", SourceNote = "Canon Part V: CropFarm (threshing crew, capacity)" },
            new ImplementSpec { Kind = "scythe", DisplayName = "Scythe", GatedTaskId = "cut-hay", SourceNote = "Canon Part V: CropFarm (hay worker)" },
            new ImplementSpec { Kind = "horse-mower", DisplayName = "Horse-Drawn Mower", GatedTaskId = "cut-hay", SourceNote = "Canon Part V: CropFarm (hay worker, capacity)" },
            new ImplementSpec { Kind = "hay-rake", DisplayName = "Hay Rake / Tedder", GatedTaskId = "rake-hay", SourceNote = "Canon Part V: CropFarm (hay worker, capacity)" },
            new ImplementSpec { Kind = "farm-wagon", DisplayName = "Farm Wagon", GatedTaskId = "haul-harvest", SourceNote = "Canon Part V: CropFarm (capacity)" },
        };

        /// <summary>Equipment requirement code for the implement gating a task.</summary>
        public static string RequirementForTask(string taskId)
        {
            foreach (var spec in All)
                if (spec.GatedTaskId == taskId)
                    return EquipmentRequirementCodes.Asset(spec.Kind);
            return null;
        }
    }
}
