using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using UnityEngine;

namespace LandLedgers.World.Journeys
{
    /// <summary>
    /// Builds the town journey nodes from actual road cells and building access
    /// anchors. Each building routes through a real junction on its connected
    /// road component; disconnected sites are never joined by a guessed edge.
    /// </summary>
    public static class TownJourneyNetworkBuilder
    {
        private const float MetersPerMile = 1609.344f;
        private const string RoadComponentPrefix = "town-road-component:";
        private static readonly GridCoord[] CardinalOffsets =
        {
            new GridCoord(1, 0), new GridCoord(-1, 0),
            new GridCoord(0, 1), new GridCoord(0, -1)
        };

        private sealed class RoadComponent
        {
            public int Id;
            public GridCoord Root;
            public float RootDistanceSquared;
            public readonly List<int> CellIndices = new();
            public string LocationId;
        }

        private readonly struct AccessChoice
        {
            public AccessChoice(int componentId, int distanceSteps)
            {
                ComponentId = componentId;
                DistanceSteps = distanceSteps;
            }

            public int ComponentId { get; }
            public int DistanceSteps { get; }
        }

        public sealed class BuildResult
        {
            internal BuildResult(List<JourneyLocation> locations, List<JourneyEdge> edges,
                int roadComponents, int routableBuildings, int buildingsWithoutRoadAccess,
                string diagnostic)
            {
                Locations = locations;
                Edges = edges;
                RoadComponents = roadComponents;
                RoutableBuildings = routableBuildings;
                BuildingsWithoutRoadAccess = buildingsWithoutRoadAccess;
                Diagnostic = diagnostic ?? string.Empty;
            }

            public IReadOnlyList<JourneyLocation> Locations { get; }
            public IReadOnlyList<JourneyEdge> Edges { get; }
            public int RoadComponents { get; }
            public int RoutableBuildings { get; }
            public int BuildingsWithoutRoadAccess { get; }
            public bool HasRoutableBuildings => RoutableBuildings > 0;
            public string Diagnostic { get; }
        }

        /// <summary>
        /// Replaces the reserved world-derived town subgraph while preserving
        /// unrelated authored journey locations and routes in the same model.
        /// </summary>
        public static BuildResult Synchronize(
            JourneyModel model,
            TownGrid grid,
            Vector3 townCenterWorld,
            IReadOnlyList<PlacedBuilding> buildings,
            IReadOnlyList<BusinessInstanceState> businesses)
        {
            return Synchronize(model, grid, townCenterWorld, null, buildings, businesses);
        }

        public static BuildResult Synchronize(
            JourneyModel model,
            TownGrid grid,
            Vector3 townCenterWorld,
            IReadOnlyList<TownPlot> plots,
            IReadOnlyList<PlacedBuilding> buildings,
            IReadOnlyList<BusinessInstanceState> businesses)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            BuildResult result = Build(grid, townCenterWorld, plots, buildings, businesses);
            if (grid != null)
                model.ReplaceTownWorldSubgraph(result.Locations, result.Edges);
            return result;
        }

        public static BuildResult Build(
            TownGrid grid,
            Vector3 townCenterWorld,
            IReadOnlyList<PlacedBuilding> buildings,
            IReadOnlyList<BusinessInstanceState> businesses)
        {
            return Build(grid, townCenterWorld, null, buildings, businesses);
        }

        public static BuildResult Build(
            TownGrid grid,
            Vector3 townCenterWorld,
            IReadOnlyList<TownPlot> plots,
            IReadOnlyList<PlacedBuilding> buildings,
            IReadOnlyList<BusinessInstanceState> businesses)
        {
            var locations = new List<JourneyLocation>();
            var edges = new List<JourneyEdge>();
            if (grid == null)
                return new BuildResult(locations, edges, 0, 0, 0, "No generated TownGrid is available.");

            var componentByCell = new int[grid.CellCount];
            var distanceByCell = new int[grid.CellCount];
            Array.Fill(componentByCell, -1);
            Array.Fill(distanceByCell, -1);
            var components = new List<RoadComponent>();
            var queue = new Queue<int>();

            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var start = new GridCoord(x, z);
                    int startIndex = grid.IndexOf(start);
                    if (componentByCell[startIndex] >= 0 || !grid.GetCell(start).IsRoad) continue;

                    var component = new RoadComponent { Id = components.Count };
                    componentByCell[startIndex] = component.Id;
                    distanceByCell[startIndex] = 0;
                    queue.Enqueue(startIndex);
                    while (queue.Count > 0)
                    {
                        int currentIndex = queue.Dequeue();
                        GridCoord current = CoordForIndex(currentIndex, grid.Width);
                        component.CellIndices.Add(currentIndex);
                        Vector3 currentWorld = grid.CoordToWorldCenter(current);
                        float dx = currentWorld.x - townCenterWorld.x;
                        float dz = currentWorld.z - townCenterWorld.z;
                        float distanceSquared = dx * dx + dz * dz;
                        if (component.CellIndices.Count == 1 || distanceSquared < component.RootDistanceSquared)
                        {
                            component.Root = current;
                            component.RootDistanceSquared = distanceSquared;
                        }

                        for (int direction = 0; direction < CardinalOffsets.Length; direction++)
                        {
                            GridCoord neighbor = current + CardinalOffsets[direction];
                            if (!grid.IsInBounds(neighbor) || !grid.GetCell(neighbor).IsRoad) continue;
                            int neighborIndex = grid.IndexOf(neighbor);
                            if (componentByCell[neighborIndex] >= 0) continue;
                            componentByCell[neighborIndex] = component.Id;
                            distanceByCell[neighborIndex] = distanceByCell[currentIndex] + 1;
                            queue.Enqueue(neighborIndex);
                        }
                    }
                    components.Add(component);
                }
            }

            if (components.Count == 0)
                return new BuildResult(locations, edges, 0, 0, CountBuildings(buildings),
                    "The generated grid contains no road cells.");

            // The discovery flood-fill starts at the first cell found in grid
            // order. Route lengths, however, are measured from the selected
            // junction nearest town center. Recompute each component's road
            // distances from that actual junction before attaching buildings.
            for (int i = 0; i < components.Count; i++)
                FillDistancesFromRoot(grid, components[i], componentByCell, distanceByCell);

            int centralComponentId = 0;
            for (int i = 1; i < components.Count; i++)
                if (components[i].RootDistanceSquared < components[centralComponentId].RootDistanceSquared)
                    centralComponentId = i;

            for (int i = 0; i < components.Count; i++)
            {
                RoadComponent component = components[i];
                component.LocationId = i == centralComponentId
                    ? "town-center"
                    : $"{RoadComponentPrefix}{component.Id:D3}:{component.Root.x}:{component.Root.z}";
                locations.Add(CreateLocation(component.LocationId, JourneyLocationKind.Waypoint,
                    i == centralComponentId ? "Town Center Road Junction" : $"Road Junction {component.Id + 1}",
                    grid.CoordToWorldCenter(component.Root), townCenterWorld));
            }

            Dictionary<int, BusinessInstanceState> businessByBuilding = BuildBusinessIndex(businesses);
            Dictionary<int, TownPlot> plotByBuilding = BuildPlotIndex(plots);
            int routableBuildings = 0;
            int buildingsWithoutRoadAccess = 0;
            var seenBuildingIds = new HashSet<int>();
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                {
                    PlacedBuilding building = buildings[i];
                    if (building == null || building.id < 0 || !seenBuildingIds.Add(building.id)) continue;
                    GridCoord accessCoord = default;
                    bool hasAccess = building.TryGetRoadAccessCoord(out accessCoord)
                        && grid.IsInBounds(accessCoord)
                        && TryFindRoadAccess(grid, accessCoord, componentByCell, distanceByCell, out _);
                    if (!hasAccess && plotByBuilding.TryGetValue(building.id, out TownPlot plot)
                        && plot != null && plot.buildingId == building.id && plot.id == building.plotId
                        && grid.IsInBounds(plot.roadAccessCell)
                        && TryFindRoadAccess(grid, plot.roadAccessCell, componentByCell, distanceByCell, out _))
                    {
                        // The plot's roadAccessCell is generated by the same
                        // world authority that validates plot frontage against
                        // the road grid. Prefer it when the building's own
                        // anchor is absent or does not touch a connected road.
                        // The plot must point back to this exact building and
                        // plot identity before it can supply road access.
                        accessCoord = plot.roadAccessCell;
                        hasAccess = true;
                    }

                    if (!hasAccess
                        || !TryFindRoadAccess(grid, accessCoord, componentByCell, distanceByCell, out AccessChoice access))
                    {
                        buildingsWithoutRoadAccess++;
                        continue;
                    }

                    RoadComponent component = components[access.ComponentId];
                    BusinessInstanceState business = businessByBuilding.TryGetValue(building.id, out BusinessInstanceState found)
                        ? found
                        : null;
                    string locationId = LocationIdForBuilding(building.id);
                    string displayName = business != null && !string.IsNullOrWhiteSpace(business.RuntimeDisplayName)
                        ? business.RuntimeDisplayName
                        : building.definition != null && !string.IsNullOrWhiteSpace(building.definition.DisplayName)
                            ? building.definition.DisplayName
                            : $"Building {building.id}";
                    JourneyLocationKind kind = business != null && business.BusinessType == BusinessType.GeneralStore
                        ? JourneyLocationKind.Store
                        : JourneyLocationKind.TownBuilding;

                    locations.Add(new JourneyLocation(locationId, kind, displayName,
                        MilesEastOf(townCenterWorld, grid.CoordToWorldCenter(accessCoord)),
                        MilesNorthOf(townCenterWorld, grid.CoordToWorldCenter(accessCoord)))
                    {
                        BusinessId = business != null ? business.InstanceId : string.Empty
                    });
                    float roadMiles = access.DistanceSteps * grid.CellSizeMeters / MetersPerMile;
                    edges.Add(new JourneyEdge(locationId, component.LocationId, roadMiles,
                        "measured connected TownGrid road cells"));
                    routableBuildings++;
                }
            }

            string summary = $"Town journey network: {locations.Count} locations, {edges.Count} road routes, " +
                $"{routableBuildings} road-accessible buildings across {components.Count} connected road component(s); " +
                $"{buildingsWithoutRoadAccess} building(s) lack connected road access.";
            return new BuildResult(locations, edges, components.Count, routableBuildings,
                buildingsWithoutRoadAccess, summary);
        }

        public static string LocationIdForBuilding(int buildingId) => $"building:{buildingId}";

        private static JourneyLocation CreateLocation(string id, JourneyLocationKind kind,
            string name, Vector3 world, Vector3 center)
        {
            return new JourneyLocation(id, kind, name,
                MilesEastOf(center, world), MilesNorthOf(center, world));
        }

        private static float MilesEastOf(Vector3 center, Vector3 point) => (point.x - center.x) / MetersPerMile;
        private static float MilesNorthOf(Vector3 center, Vector3 point) => (point.z - center.z) / MetersPerMile;

        private static bool TryFindRoadAccess(TownGrid grid, GridCoord accessCoord,
            int[] componentByCell, int[] distanceByCell, out AccessChoice choice)
        {
            choice = default;
            int directIndex = grid.IndexOf(accessCoord);
            if (grid.GetCell(accessCoord).IsRoad && componentByCell[directIndex] >= 0)
            {
                choice = new AccessChoice(componentByCell[directIndex], distanceByCell[directIndex]);
                return true;
            }

            bool found = false;
            int bestSteps = int.MaxValue;
            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                GridCoord neighbor = accessCoord + CardinalOffsets[i];
                if (!grid.IsInBounds(neighbor) || !grid.GetCell(neighbor).IsRoad) continue;
                int neighborIndex = grid.IndexOf(neighbor);
                int componentId = componentByCell[neighborIndex];
                int steps = distanceByCell[neighborIndex] + 1;
                if (componentId < 0 || steps >= bestSteps) continue;
                bestSteps = steps;
                choice = new AccessChoice(componentId, steps);
                found = true;
            }
            return found;
        }

        private static void FillDistancesFromRoot(TownGrid grid, RoadComponent component,
            int[] componentByCell, int[] distanceByCell)
        {
            for (int i = 0; i < component.CellIndices.Count; i++)
                distanceByCell[component.CellIndices[i]] = -1;

            var queue = new Queue<int>();
            int rootIndex = grid.IndexOf(component.Root);
            distanceByCell[rootIndex] = 0;
            queue.Enqueue(rootIndex);
            while (queue.Count > 0)
            {
                int currentIndex = queue.Dequeue();
                GridCoord current = CoordForIndex(currentIndex, grid.Width);
                for (int direction = 0; direction < CardinalOffsets.Length; direction++)
                {
                    GridCoord neighbor = current + CardinalOffsets[direction];
                    if (!grid.IsInBounds(neighbor)) continue;
                    int neighborIndex = grid.IndexOf(neighbor);
                    if (componentByCell[neighborIndex] != component.Id
                        || distanceByCell[neighborIndex] >= 0) continue;
                    distanceByCell[neighborIndex] = distanceByCell[currentIndex] + 1;
                    queue.Enqueue(neighborIndex);
                }
            }
        }

        private static Dictionary<int, BusinessInstanceState> BuildBusinessIndex(
            IReadOnlyList<BusinessInstanceState> businesses)
        {
            var index = new Dictionary<int, BusinessInstanceState>();
            if (businesses == null) return index;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.AssignedBuildingId < 0) continue;
                if (!index.ContainsKey(business.AssignedBuildingId))
                    index.Add(business.AssignedBuildingId, business);
            }
            return index;
        }

        private static Dictionary<int, TownPlot> BuildPlotIndex(IReadOnlyList<TownPlot> plots)
        {
            var index = new Dictionary<int, TownPlot>();
            if (plots == null) return index;
            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null || plot.buildingId < 0) continue;
                if (!index.ContainsKey(plot.buildingId)) index.Add(plot.buildingId, plot);
            }
            return index;
        }

        private static int CountBuildings(IReadOnlyList<PlacedBuilding> buildings)
        {
            int count = 0;
            if (buildings == null) return count;
            for (int i = 0; i < buildings.Count; i++)
                if (buildings[i] != null) count++;
            return count;
        }

        private static GridCoord CoordForIndex(int index, int width) => new(index % width, index / width);
    }
}
