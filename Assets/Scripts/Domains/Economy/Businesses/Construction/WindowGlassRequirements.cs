using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// F1 (workstream F, glass leg): the window-glass requirement for a
    /// glazed build — how many panes a design's windows need, and which
    /// work package the panes belong to. One pane is one window light
    /// (single-strength common window glass, 8x10 to 10x12 in); one window
    /// takes one pane in these designs. Glass is NEVER a universal
    /// requirement: a design carries no glass unless the commission asks
    /// for glazed windows, and the requirement is built here only for a
    /// glazed commission — an unglazed build has no glass line at all.
    ///
    /// Window counts mirror the D2 provisional door/window counts
    /// (settler 1 window, family 2, workshop 2) and are PROVISIONAL
    /// calibrations for the owner to correct, exactly as D2's are. The
    /// openings phase is the "Framing - roof" phase in all three
    /// production designs — the roof phase carries the openings work
    /// (and D2's door hardware) until a later lane splits a dedicated
    /// openings phase, per the E2 note.
    ///
    /// The requirement names the lumber yard as its supplier
    /// (SupplierBusinessId "lumber-yard", the same id the lumber leg
    /// uses), so the procurement service discovers the yard as a real,
    /// registered supplier — never a phantom — and the yard sale adapter
    /// sells the panes from real glass lots.
    /// </summary>
    public static class WindowGlassRequirements
    {
        /// <summary>F1: the work package the glass requirement joins — the openings ("Framing - roof") phase.</summary>
        public const string OpeningsPhaseName = "Framing - roof";

        /// <summary>F1: panes per window. One pane per window light in these designs.</summary>
        public const int PanesPerWindow = 1;

        /// <summary>F1: windows per design, by catalog design id. Unknown designs have no windows recorded — a glazed commission on an unknown design is refused, never guessed.</summary>
        public static int WindowsForDesign(string designId)
        {
            if (string.IsNullOrWhiteSpace(designId)) return -1;
            switch (designId.Trim())
            {
                case "settler-cabin": return 1;
                case "family-cabin": return 2;
                case "carpenters-workshop": return 2;
                default: return -1;
            }
        }

        /// <summary>F1: panes a glazed build of this design needs (windows x panes per window), or -1 when the design has no recorded window count.</summary>
        public static int PanesForDesign(string designId)
        {
            int windows = WindowsForDesign(designId);
            if (windows < 0) return -1;
            return windows * PanesPerWindow;
        }

        /// <summary>
        /// F1: builds the glass material requirement for a glazed build
        /// of this design, or null when the design has no recorded window
        /// count. The requirement is coherent and priced through the
        /// yard price port like any other material: no price in the yard
        /// (no stocked glass lot) refuses the commission, never invents
        /// a price. The requirement id is stable per design so a
        /// re-commissioned glazed build does not mint new identities.
        /// </summary>
        public static ConstructionMaterialRequirement GlassRequirementFor(string designId)
        {
            int panes = PanesForDesign(designId);
            if (panes < 0) return null;
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-openings-glass",
                MaterialKind = ConstructionMaterialKind.Glass,
                MaterialDisplayName = "glass",
                RequiredUnits = panes,
                UnitLabel = "panes",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            };
        }

        /// <summary>
        /// F1: appends the glass requirement to the openings package of
        /// a glazed build. The package must be the openings phase
        /// (OpeningsPhaseName); appending to any other phase is refused
        /// loudly, and appending twice is refused — the glass line is
        /// created once per glazed build. Returns the refusal, or null.
        /// </summary>
        public static string AppendToOpeningsPackage(WorkPackage package, string designId, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (package == null)
                return "WindowGlassRequirements: no openings package — glass has nowhere to go.";
            if (!string.Equals(package.PhaseName, OpeningsPhaseName, StringComparison.Ordinal))
                return $"WindowGlassRequirements: package '{package.PackageId}' is phase '{package.PhaseName}', not the openings phase '{OpeningsPhaseName}' — glass is not appended to another phase.";
            ConstructionMaterialRequirement requirement = GlassRequirementFor(designId);
            if (requirement == null)
                return $"WindowGlassRequirements: design '{designId}' has no recorded window count — a glazed build cannot be priced, refused rather than guessed.";
            foreach (ConstructionMaterialRequirement existing in package.Materials ?? new List<ConstructionMaterialRequirement>())
            {
                if (existing != null && existing.MaterialKind == ConstructionMaterialKind.Glass)
                    return $"WindowGlassRequirements: package '{package.PackageId}' already carries a glass requirement — glass is added once per glazed build.";
            }
            package.Materials.Add(requirement);
            diag.Add($"WindowGlassRequirements: design '{designId}' glazed — {requirement.RequiredUnits} glass pane(s) added to the openings package '{package.PackageId}'.");
            return null;
        }
    }
}
