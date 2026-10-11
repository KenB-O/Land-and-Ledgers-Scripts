using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III Worker B: read-only view linking a property-system building
    /// to its physical footprint. The legal parcel link lives on
    /// HousingFoundation.Building (ParcelId); the physical footprint and plot
    /// link come from the world side (PlacedBuilding data) through the
    /// directory — read, never written, here.
    ///
    /// Canon: preserved Rev XXVI §2.2 — legal right is distinct from physical
    /// possibility. This linker reports both sides side-by-side so a later
    /// parcel↔plot join (Worker A) can verify them; it asserts nothing about
    /// legal title itself, and it never resizes or moves anything physical.
    /// </summary>
    [Serializable]
    public sealed class BuildingFootprintView
    {
        public string BuildingId = string.Empty;
        /// <summary>Legal parcel link (HousingFoundation).</summary>
        public string ParcelId = string.Empty;
        public string BuildingKind = string.Empty;
        public bool HasPlacedRecord;
        /// <summary>Physical plot link (PlacedBuilding.plotId), -1 when unknown.</summary>
        public int PlotId = -1;
        /// <summary>Physical footprint in grid cells (read-only).</summary>
        public GridRect Footprint;
        public string Note = string.Empty;

        public BuildingFootprintView() { }
    }

    /// <summary>
    /// Pass III Worker B: source of physical footprint data. Production is
    /// wired by game code (Codex) against PlacedBuilding.plotId +
    /// PlacedBuilding.footprint, read-only. Keyed by the property-system
    /// building id.
    /// </summary>
    public interface IBuildingFootprintDirectory
    {
        bool TryGetPlacedFootprint(string buildingKey, out int plotId, out GridRect footprint, out string note);
    }

    /// <summary>
    /// Pass III Worker B: builds the parcel→building→footprint read model.
    /// Diagnostics only — no state is written, no prefab is touched.
    /// </summary>
    public sealed class BuildingFootprintLinker
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public List<BuildingFootprintView> Link(
            HousingAuthority housing, IBuildingFootprintDirectory directory, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var views = new List<BuildingFootprintView>();
            if (housing == null || directory == null)
            {
                diag.Add("BuildingFootprintLinker: housing authority and footprint directory are required.");
                return views;
            }

            foreach (Building building in housing.AllBuildings())
            {
                var view = new BuildingFootprintView
                {
                    BuildingId = building.BuildingId,
                    ParcelId = building.ParcelId,
                    BuildingKind = building.Kind,
                };
                if (directory.TryGetPlacedFootprint(building.BuildingId, out int plotId, out GridRect footprint, out string note))
                {
                    view.HasPlacedRecord = true;
                    view.PlotId = plotId;
                    view.Footprint = footprint;
                    view.Note = note ?? string.Empty;
                }
                else
                {
                    view.Note = "no placed-building record found for this property-system building id";
                    diag.Add($"BuildingFootprintLinker: building '{building.BuildingId}' (parcel '{building.ParcelId}') " +
                             "has no placed-building footprint record — link reported, nothing invented.");
                }
                views.Add(view);
            }
            return views;
        }
    }
}
