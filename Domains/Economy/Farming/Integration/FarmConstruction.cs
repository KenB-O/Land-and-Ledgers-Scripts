using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Bootstrap;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Integration
{
    public enum FarmConstructionKind
    {
        Unspecified = 0,
        Coop = 1,
        Barn = 2,
    }

    public enum FarmConstructionStatus
    {
        Unspecified = 0,
        Planned = 1,           // materials not yet acquired
        MaterialsAcquired = 2, // ready to build
        InProgress = 3,
        Complete = 4,
    }

    /// <summary>
    /// FVS-4: a mid-game farm construction project. Ordering is enforced, not
    /// assumed: materials must be BOUGHT FIRST (from a real supplier — the
    /// lumber yard), then the construction task runs. A project without
    /// materials can never start.
    /// </summary>
    [Serializable]
    public sealed class FarmConstructionProject
    {
        /// <summary>Calibration: lumber units per building kind.</summary>
        public static int LumberRequiredFor(FarmConstructionKind kind)
        {
            switch (kind)
            {
                case FarmConstructionKind.Coop: return 40;
                case FarmConstructionKind.Barn: return 200;
                default: return 0;
            }
        }

        public string ProjectId = string.Empty;
        public FarmConstructionKind Kind;
        public string Name = string.Empty; // e.g. "second hen coop"
        public int LumberRequired;
        public int LumberAcquired;
        public string LumberSource = string.Empty; // provenance: which lumber yard
        public FarmConstructionStatus Status;
        public int StartedDayIndex = -1;
        public int CompletedDayIndex = -1;

        public FarmConstructionProject() { }

        public FarmConstructionProject(string projectId, FarmConstructionKind kind, string name)
        {
            ProjectId = projectId ?? string.Empty;
            Kind = kind;
            Name = name ?? string.Empty;
            LumberRequired = LumberRequiredFor(kind);
            Status = FarmConstructionStatus.Planned;
        }

        public bool MaterialsReady => LumberAcquired >= LumberRequired && LumberRequired > 0;
    }

    /// <summary>
    /// FVS-4: a lumber supplier (the lumber yard business). Finite stock, named
    /// counterparty — the same provenance discipline as every other trade.
    /// </summary>
    public interface ILumberSupplier
    {
        string SupplierBusinessId { get; }
        string SupplierName { get; }
        int LumberStockUnits { get; }
        int PricePerUnitCents { get; }
        int SellLumber(int requestedUnits, int dayIndex, List<string> diagnostics);
    }

    /// <summary>FVS-4: construction orchestration for the farm loop.</summary>
    public static class FarmConstruction
    {
        public const string BuildCoopTaskId = "build-coop";
        public const string BuildBarnTaskId = "build-barn";

        /// <summary>Calibration: construction minutes.</summary>
        public const int CoopBuildMinutes = 480;
        public const int BarnBuildMinutes = 2400;

        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            var coop = new TaskDefinition(BuildCoopTaskId, "Build chicken coop", CoopBuildMinutes);
            coop.SetRequiredSkill(SkillIds.BasicRepair, new[] { "carpentry" });
            authority.RegisterDefinition(coop, out _);
            var barn = new TaskDefinition(BuildBarnTaskId, "Build barn", BarnBuildMinutes);
            barn.SetRequiredSkill(SkillIds.BasicRepair, new[] { "carpentry" });
            authority.RegisterDefinition(barn, out _);
        }

        /// <summary>
        /// Buys lumber for the project from a real supplier. The project cannot
        /// proceed until materials are in hand.
        /// </summary>
        public static string BuyMaterials(
            FarmConstructionProject project,
            ILumberSupplier supplier,
            int dayIndex,
            HouseholdLedger buyerLedger,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (project == null) return "FarmConstruction: no project.";
            if (supplier == null) return "FarmConstruction: no lumber supplier — materials must come from a real business.";
            if (buyerLedger == null) return "FarmConstruction: no buyer ledger — material purchases require provenance.";

            int need = project.LumberRequired - project.LumberAcquired;
            if (need <= 0)
            {
                diagnostics.Add($"FarmConstruction: project '{project.ProjectId}' already has its materials.");
                return null;
            }

            int sold = supplier.SellLumber(need, dayIndex, diagnostics);
            if (sold <= 0) return $"FarmConstruction: {supplier.SupplierName} could not supply {need} lumber units.";
            int costCents = sold * supplier.PricePerUnitCents;
            if (costCents > 0)
            {
                buyerLedger.RecordOutflow(dayIndex, costCents,
                    $"Lumber for {project.Name} ({project.Kind}): {sold} units from {supplier.SupplierName}",
                    supplier.SupplierName);
            }

            project.LumberAcquired += sold;
            project.LumberSource = supplier.SupplierName;
            if (project.MaterialsReady)
            {
                project.Status = FarmConstructionStatus.MaterialsAcquired;
                diagnostics.Add($"FarmConstruction: project '{project.ProjectId}' materials complete ({project.LumberAcquired} lumber from {supplier.SupplierName}).");
            }
            return null;
        }

        /// <summary>
        /// Starts construction. REFUSES unless materials are in hand — the
        /// ordering is enforced by code, never assumed.
        /// </summary>
        public static string StartConstruction(
            FarmConstructionProject project, EntityId workerId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (project == null) return "FarmConstruction: no project.";
            if (!project.MaterialsReady)
            {
                return $"FarmConstruction: project '{project.ProjectId}' cannot start — materials not acquired " +
                       $"({project.LumberAcquired}/{project.LumberRequired} lumber). Buy materials first.";
            }
            if (project.Status != FarmConstructionStatus.MaterialsAcquired)
            {
                return $"FarmConstruction: project '{project.ProjectId}' is {project.Status}; cannot start.";
            }
            project.Status = FarmConstructionStatus.InProgress;
            project.StartedDayIndex = dayIndex;
            diagnostics.Add($"FarmConstruction: project '{project.ProjectId}' started by {workerId} on day {dayIndex}.");
            return null;
        }

        /// <summary>
        /// Completes construction, returning the new farm building. The caller
        /// registers it on the live farm record and layout — buildings are
        /// never conjured; they arrive through this flow.
        /// </summary>
        public static FarmBuilding CompleteConstruction(
            FarmConstructionProject project, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (project == null)
            {
                diagnostics.Add("FarmConstruction: no project.");
                return null;
            }
            if (project.Status != FarmConstructionStatus.InProgress)
            {
                diagnostics.Add($"FarmConstruction: project '{project.ProjectId}' is {project.Status}, not in progress.");
                return null;
            }

            project.Status = FarmConstructionStatus.Complete;
            project.CompletedDayIndex = dayIndex;

            FarmBuildingKind kind = project.Kind == FarmConstructionKind.Coop
                ? FarmBuildingKind.Coop : FarmBuildingKind.Barn;
            var building = new FarmBuilding(kind, project.Name,
                project.Kind == FarmConstructionKind.Coop ? 40 : 20,
                dairySuitable: false);
            building.Condition = $"built day {dayIndex} (lumber: {project.LumberSource})";
            diagnostics.Add($"FarmConstruction: '{project.Name}' complete — building registered with provenance.");
            return building;
        }
    }
}
