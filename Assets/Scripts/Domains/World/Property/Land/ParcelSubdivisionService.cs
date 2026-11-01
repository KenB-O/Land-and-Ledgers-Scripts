using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// A building standing on a parcel, for subdivision containment checks.
    /// Production wiring (Codex) adapts this from PlacedBuilding records
    /// (footprint + plot linkage); tests supply a stub.
    /// </summary>
    public readonly struct ParcelBuildingFootprint
    {
        public readonly string BuildingId;
        public readonly GridRect Footprint;

        public ParcelBuildingFootprint(string buildingId, GridRect footprint)
        {
            BuildingId = buildingId ?? string.Empty;
            Footprint = footprint;
        }
    }

    /// <summary>
    /// Source of building footprints for a parcel. Optional on the subdivision
    /// service: when absent, building-through-split checks are skipped with an
    /// explicit warning rather than silently assumed clear.
    /// </summary>
    public interface IParcelBuildingIndex
    {
        IReadOnlyList<ParcelBuildingFootprint> BuildingsOnParcel(string parcelId);
    }

    /// <summary>
    /// Pass III land core (Worker A): atomic parcel subdivision / consolidation,
    /// implementing the foundation <see cref="IParcelSubdivisionService"/> contract.
    ///
    /// Rules honored (foundation contract + Canon XXXI §6.3 parcel severance):
    /// - A split is not a sale: subdivision creates no money and moves no title.
    /// - Validate-then-execute. A failed subdivision leaves the original property
    ///   state intact (deep snapshot + rollback on mid-execution failure).
    /// - Original title history stays traceable: children carry ParentParcelIds
    ///   lineage and the parent is retired, never deleted.
    /// - No invented proportional division of monetary encumbrances: an active
    ///   MortgageCharge/TaxLien with no supported apportionment rule blocks
    ///   finalization until explicitly resolved.
    /// - No split through an existing building footprint without a supported
    ///   arrangement: a building's cells must sit wholly inside one child.
    /// - Access/frontage must be explicitly re-established per child; subdivision
    ///   never invents an easement (warning, not silent creation).
    /// - Resulting footprints must exactly tile the parent: no silent gain or
    ///   loss of land area (preserved Rev XIV §3.1A survey/provenance).
    /// - Rev XXIII §1.1: this service mutates ParcelRecords and interests only;
    ///   title stays in TitleAuthority, cash in HouseholdLedger — no second registries.
    ///
    /// Design decisions recorded (not guessed): see docs in Validate/Execute and
    /// the Pass III final report. Non-monetary interests (lease, easement, ROW,
    /// restriction, public-land entry, dispute claim) are carried to every child
    /// with a review note because the interest record carries no spatial extent
    /// to apportion by; monetary charges block instead of being split by an
    /// invented proportional rule.
    /// </summary>
    public sealed class ParcelSubdivisionService : IParcelSubdivisionService
    {
        private readonly ParcelService parcels;
        private readonly IPropertyInterestStore interests;
        // Rollback support: only available when the injected store also carries
        // the snapshot contract (the production PropertyInterestStore does).
        private readonly ISnapshotableInterestStore snapshotableInterests;
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Production must provide the building-footprint source. Without it a
        /// split is not safe to authorize because an existing structure could be
        /// divided across legal children.
        /// </summary>
        public IParcelBuildingIndex BuildingIndex { get; set; }

        /// <summary>
        /// Production must provide the title-holder lookup. Without it a
        /// subdivision cannot authorize a requester against the title chain.
        /// </summary>
        public Func<string, string> TitleHolderLookup { get; set; }

        public ParcelSubdivisionService(ParcelService parcels, IPropertyInterestStore interests)
        {
            this.parcels = parcels ?? throw new ArgumentNullException(nameof(parcels));
            this.interests = interests ?? throw new ArgumentNullException(nameof(interests));
            this.snapshotableInterests = interests as ISnapshotableInterestStore;
        }

        private static long CellKey(int x, int z) => ((long)(uint)x << 32) | (uint)z;

        private static void AddRectCells(HashSet<long> set, GridRect rect)
        {
            if (!rect.IsValid) return;
            for (int x = rect.xMin; x <= rect.xMaxInclusive; x++)
                for (int z = rect.zMin; z <= rect.zMaxInclusive; z++)
                    set.Add(CellKey(x, z));
        }

        public SubdivisionValidation Validate(SubdivisionRequest request)
        {
            var validation = new SubdivisionValidation();
            if (request == null)
            {
                validation.Errors.Add("subdivision request is null.");
                return validation;
            }
            ParcelRecord parent = parcels.GetParcel(request.ParcelId);
            if (parent == null)
            {
                validation.Errors.Add($"unknown parcel '{request.ParcelId}'.");
                return validation;
            }
            if (parent.IsRetired)
            {
                validation.Errors.Add($"parcel '{parent.Id}' is retired (superseded by subdivision/consolidation) — subdivide a live parcel.");
                return validation;
            }
            if (TitleHolderLookup == null)
            {
                validation.Errors.Add("title-holder lookup is unavailable — subdivision authorization cannot be verified.");
            }
            else if (string.IsNullOrWhiteSpace(request.RequestedByHolder))
            {
                validation.Errors.Add("subdivision requester is required — title authorization cannot be inferred.");
            }
            else
            {
                string holder = TitleHolderLookup(parent.Id);
                if (!string.Equals(holder, request.RequestedByHolder, StringComparison.Ordinal))
                    validation.Errors.Add($"'{request.RequestedByHolder}' does not hold title to '{parent.Id}' " +
                        $"(recorded holder: '{holder ?? "unknown"}') — only the title holder may subdivide.");
            }

            List<List<GridRect>> footprints = request.ResultingFootprints;
            if (footprints == null || footprints.Count < 2)
            {
                validation.Errors.Add("subdivision requires at least two resulting footprints.");
                return validation;
            }
            for (int i = 0; i < footprints.Count; i++)
            {
                if (footprints[i] == null || footprints[i].Count == 0)
                {
                    validation.Errors.Add($"resulting footprint {i} is empty.");
                    continue;
                }
                foreach (GridRect rect in footprints[i])
                    if (!rect.IsValid)
                        validation.Errors.Add($"resulting footprint {i} contains an invalid rect {rect}.");
            }
            if (validation.Errors.Count > 0) return validation;

            var parentCells = new HashSet<long>();
            foreach (GridRect rect in parent.Footprint) AddRectCells(parentCells, rect);
            if (parentCells.Count == 0)
            {
                validation.Errors.Add($"parent parcel '{parent.Id}' has no footprint cells to subdivide.");
                return validation;
            }

            var childSets = new List<HashSet<long>>(footprints.Count);
            for (int i = 0; i < footprints.Count; i++)
            {
                var set = new HashSet<long>();
                foreach (GridRect rect in footprints[i]) AddRectCells(set, rect);
                if (set.Count == 0)
                    validation.Errors.Add($"resulting footprint {i} covers no cells.");
                childSets.Add(set);
            }

            // Children must be pairwise disjoint.
            for (int i = 0; i < childSets.Count; i++)
                for (int j = i + 1; j < childSets.Count; j++)
                {
                    bool overlap = false;
                    HashSet<long> smaller = childSets[i].Count <= childSets[j].Count ? childSets[i] : childSets[j];
                    HashSet<long> larger = smaller == childSets[i] ? childSets[j] : childSets[i];
                    foreach (long key in smaller)
                        if (larger.Contains(key)) { overlap = true; break; }
                    if (overlap)
                        validation.Errors.Add($"resulting footprints {i} and {j} overlap — child parcels must be disjoint.");
                }

            // Each child must lie inside the parent, and together they must tile it exactly.
            var covered = new HashSet<long>();
            for (int i = 0; i < childSets.Count; i++)
            {
                bool outside = false;
                foreach (long key in childSets[i])
                {
                    if (!parentCells.Contains(key)) { outside = true; break; }
                    covered.Add(key);
                }
                if (outside)
                    validation.Errors.Add($"resulting footprint {i} extends outside the parent parcel boundary.");
            }
            if (covered.Count != parentCells.Count)
                validation.Errors.Add($"resulting footprints cover {covered.Count} of {parentCells.Count} parent cells — " +
                    "no land may be silently gained or lost by a subdivision.");

            ValidateBuildings(parent, childSets, validation);
            ValidateAccessAndStatus(parent, validation);
            ValidateEncumbrances(parent, validation);

            validation.IsValid = validation.Errors.Count == 0;
            return validation;
        }

        private void ValidateBuildings(ParcelRecord parent, List<HashSet<long>> childSets, SubdivisionValidation validation)
        {
            IParcelBuildingIndex index = BuildingIndex;
            if (index == null)
            {
                if (parent.BuildingIds != null && parent.BuildingIds.Count > 0)
                {
                    validation.Errors.Add("building index is unavailable — subdivision cannot verify that a live structure is not split.");
                }
                else
                {
                    validation.Warnings.Add("no building index provided for a parcel with no recorded buildings.");
                }
                return;
            }
            IReadOnlyList<ParcelBuildingFootprint> buildings = index.BuildingsOnParcel(parent.Id);
            if (buildings == null) return;
            for (int b = 0; b < buildings.Count; b++)
            {
                GridRect rect = buildings[b].Footprint;
                if (!rect.IsValid) continue;
                int containing = -1;
                bool split = false;
                bool unverified = false;
                for (int x = rect.xMin; x <= rect.xMaxInclusive && !split; x++)
                    for (int z = rect.zMin; z <= rect.zMaxInclusive && !split; z++)
                    {
                        long key = CellKey(x, z);
                        int found = -1;
                        for (int i = 0; i < childSets.Count; i++)
                            if (childSets[i].Contains(key)) { found = i; break; }
                        if (found < 0) { unverified = true; }
                        else if (containing < 0) containing = found;
                        else if (containing != found) split = true;
                    }
                string label = string.IsNullOrWhiteSpace(buildings[b].BuildingId) ? $"#{b}" : buildings[b].BuildingId;
                if (split)
                    validation.Errors.Add($"building '{label}' footprint {rect} is split across resulting parcels — " +
                        "not permitted without a supported arrangement.");
                else if (unverified)
                    validation.Warnings.Add($"building '{label}' footprint {rect} is not fully inside one resulting " +
                        "footprint — verify placement before finalizing.");
            }
        }

        private static void ValidateAccessAndStatus(ParcelRecord parent, SubdivisionValidation validation)
        {
            if (!string.IsNullOrWhiteSpace(parent.AccessNotes))
                validation.Warnings.Add("parent parcel records access/frontage (" + parent.AccessNotes.Trim() +
                    ") — confirm access for each child parcel; subdivision creates no easement.");
            if (parent.Status != LandStatus.Unknown && parent.Status != LandStatus.PrivateTitled)
                validation.Warnings.Add($"parcel status is {parent.Status} — subdivision is recorded against the current " +
                    "status; no status conversion or claim perfection is implied.");
        }

        private void ValidateEncumbrances(ParcelRecord parent, SubdivisionValidation validation)
        {
            IReadOnlyList<PropertyInterest> active = interests.ActiveForParcel(parent.Id);
            int carried = 0;
            foreach (PropertyInterest interest in active)
            {
                // Monetary charges: no supported apportionment rule exists, so an
                // invented proportional split is refused — resolve explicitly first.
                if (interest.Kind == InterestKind.MortgageCharge || interest.Kind == InterestKind.TaxLien)
                {
                    validation.Errors.Add($"{interest.Kind} '{interest.Id}' encumbers the parent parcel — " +
                        "no supported apportionment rule exists; resolve or apportion it explicitly before finalizing.");
                }
                else
                {
                    carried++;
                    if (interest.Kind == InterestKind.DisputeClaim)
                        validation.Warnings.Add($"dispute claim '{interest.Id}' will be carried to the child parcels — title remains disputed.");
                }
            }
            if (carried > 0)
                validation.Warnings.Add($"{carried} non-monetary interest(s) will be carried to each child parcel for " +
                    "review (interests carry no spatial extent to apportion by).");
        }

        public SubdivisionResult Execute(SubdivisionRequest request)
        {
            var result = new SubdivisionResult();
            SubdivisionValidation validation = Validate(request);
            if (!validation.IsValid)
            {
                result.Success = false;
                result.Message = "subdivision rejected: " + string.Join("; ", validation.Errors);
                return result;
            }

            // Deep snapshots: rollback restores exact prior state on mid-execution failure.
            ParcelServiceSaveDto parcelSnapshot = DeepCloneParcels();
            PropertyInterestStoreSaveDto interestSnapshot = DeepCloneInterests();

            try
            {
                ParcelRecord parent = parcels.GetParcel(request.ParcelId);
                int count = request.ResultingFootprints.Count;

                var childSets = new List<HashSet<long>>(count);
                for (int i = 0; i < count; i++)
                {
                    var set = new HashSet<long>();
                    foreach (GridRect rect in request.ResultingFootprints[i]) AddRectCells(set, rect);
                    childSets.Add(set);
                }

                var newIds = new List<string>(count);
                var children = new List<ParcelRecord>(count);
                for (int i = 0; i < count; i++)
                {
                    var child = new ParcelRecord
                    {
                        Id = ParcelService.NewParcelId(),
                        WorldId = parent.WorldId,
                        Footprint = new List<GridRect>(request.ResultingFootprints[i]),
                        BoundaryNote = $"Subdivided from {parent.Id} on day {request.Day}; cell-range boundaries as requested.",
                        SurveyDescription = (parent.SurveyDescription ?? string.Empty) + $" (subdivision child {i + 1} of {count})",
                        AreaSquareMeters = childSets[i].Count * DefaultCellSize,
                        ParcelUse = parent.ParcelUse,
                        Jurisdiction = parent.Jurisdiction,
                        Status = parent.Status,
                        ParentParcelIds = new List<string> { parent.Id },
                        RootSurveyId = string.IsNullOrEmpty(parent.RootSurveyId) ? parent.Id : parent.RootSurveyId,
                        AccessNotes = string.IsNullOrWhiteSpace(parent.AccessNotes)
                            ? string.Empty
                            : $"Carried from {parent.Id} — verify frontage per child; no easement created by subdivision. Was: {parent.AccessNotes.Trim()}",
                        IsRetired = false,
                        // The physical town-plot linkage must be re-surveyed after a legal
                        // split: one plot record cannot honestly map to N legal parcels.
                        PlotId = string.Empty,
                    };
                    children.Add(child);
                }

                AssignBuildingsToChildren(parent, children, childSets);
                foreach (ParcelRecord c in children)
                {
                    parcels.RegisterParcelRecord(c);
                    newIds.Add(c.Id);
                }

                parent.IsRetired = true;
                CarryInterests(parent, newIds, request.Day);

                result.Success = true;
                result.NewParcelIds = newIds;
                result.RetiredParcelId = parent.Id;
                result.Message = $"subdivided '{parent.Id}' into {count} parcels.";
                return result;
            }
            catch (Exception ex)
            {
                parcels.LoadFromSaveDto(parcelSnapshot);
                if (snapshotableInterests != null)
                    snapshotableInterests.LoadFromSaveDto(interestSnapshot);
                result.Success = false;
                result.Message = "subdivision failed mid-execution and was rolled back: " + ex.Message;
                return result;
            }
        }

        private const double DefaultCellSize = 4.0; // 2 m grid: 4 m² per cell.

        private static bool RectInsideSet(GridRect rect, HashSet<long> set)
        {
            if (!rect.IsValid) return false;
            for (int x = rect.xMin; x <= rect.xMaxInclusive; x++)
                for (int z = rect.zMin; z <= rect.zMaxInclusive; z++)
                    if (!set.Contains(CellKey(x, z))) return false;
            return true;
        }

        /// <summary>
        /// Links buildings to the child whose footprint wholly contains them.
        /// Anything the index cannot place precisely (no index, unverified
        /// placement, stale parent links) is carried to every child for review —
        /// buildings are never silently dropped by a subdivision.
        /// </summary>
        private void AssignBuildingsToChildren(ParcelRecord parent, List<ParcelRecord> children, List<HashSet<long>> childSets)
        {
            var indexedIds = new HashSet<string>(StringComparer.Ordinal);
            var placedIds = new HashSet<string>(StringComparer.Ordinal);
            IParcelBuildingIndex index = BuildingIndex;
            if (index != null)
            {
                IReadOnlyList<ParcelBuildingFootprint> buildings = index.BuildingsOnParcel(parent.Id);
                if (buildings != null)
                    foreach (ParcelBuildingFootprint building in buildings)
                    {
                        if (!building.Footprint.IsValid || string.IsNullOrWhiteSpace(building.BuildingId)) continue;
                        indexedIds.Add(building.BuildingId);
                        for (int i = 0; i < children.Count; i++)
                        {
                            if (RectInsideSet(building.Footprint, childSets[i]))
                            {
                                children[i].BuildingIds.Add(building.BuildingId);
                                placedIds.Add(building.BuildingId);
                                break;
                            }
                        }
                    }
            }
            var carry = new List<string>();
            foreach (string id in indexedIds)
                if (!placedIds.Contains(id)) carry.Add(id);
            foreach (string id in parent.BuildingIds)
                if (!indexedIds.Contains(id) && !carry.Contains(id)) carry.Add(id);
            if (carry.Count == 0) return;
            foreach (ParcelRecord child in children)
                foreach (string id in carry)
                    if (!child.BuildingIds.Contains(id)) child.BuildingIds.Add(id);
            diagnostics.Add($"ParcelSubdivisionService: {carry.Count} building link(s) could not be placed precisely " +
                $"after subdividing '{parent.Id}' — carried to every child for review: {string.Join(", ", carry)}.");
        }

        private void CarryInterests(ParcelRecord parent, List<string> newIds, int day)
        {
            IReadOnlyList<PropertyInterest> active = interests.ActiveForParcel(parent.Id);
            foreach (PropertyInterest interest in active)
            {
                interests.Extinguish(interest.Id, day, $"superseded by subdivision of '{parent.Id}'");
                foreach (string childId in newIds)
                {
                    interests.Record(new PropertyInterest
                    {
                        Id = PropertyInterestStore.NewInterestId(),
                        ParcelId = childId,
                        Kind = interest.Kind,
                        HolderId = interest.HolderId,
                        TermsRef = interest.TermsRef,
                        StartDay = day,
                        EndDay = interest.EndDay,
                        Notes = $"Carried from subdivision of '{parent.Id}' (was '{interest.Id}') — verify spatial applicability.",
                    });
                }
            }
        }

        private ParcelServiceSaveDto DeepCloneParcels()
        {
            ParcelServiceSaveDto dto = parcels.CaptureSaveDto();
            var deep = new ParcelServiceSaveDto();
            foreach (ParcelRecord record in dto.parcels) deep.parcels.Add(CloneParcel(record));
            foreach (HoldingRecord holding in dto.holdings)
                deep.holdings.Add(new HoldingRecord
                {
                    Id = holding.Id,
                    Name = holding.Name,
                    Notes = holding.Notes,
                    ParcelIds = new List<string>(holding.ParcelIds),
                });
            return deep;
        }

        private static ParcelRecord CloneParcel(ParcelRecord record)
        {
            return new ParcelRecord
            {
                Id = record.Id,
                WorldId = record.WorldId,
                Footprint = new List<GridRect>(record.Footprint),
                BoundaryNote = record.BoundaryNote,
                SurveyDescription = record.SurveyDescription,
                AreaSquareMeters = record.AreaSquareMeters,
                ParcelUse = record.ParcelUse,
                Jurisdiction = record.Jurisdiction,
                Status = record.Status,
                ParentParcelIds = new List<string>(record.ParentParcelIds),
                RootSurveyId = record.RootSurveyId,
                BuildingIds = new List<string>(record.BuildingIds),
                AccessNotes = record.AccessNotes,
                IsRetired = record.IsRetired,
                PlotId = record.PlotId,
            };
        }

        private PropertyInterestStoreSaveDto DeepCloneInterests()
        {
            var deep = new PropertyInterestStoreSaveDto();
            if (snapshotableInterests == null) return deep;
            PropertyInterestStoreSaveDto dto = snapshotableInterests.CaptureSaveDto();
            foreach (PropertyInterest interest in dto.interests)
                deep.interests.Add(new PropertyInterest
                {
                    Id = interest.Id,
                    ParcelId = interest.ParcelId,
                    Kind = interest.Kind,
                    HolderId = interest.HolderId,
                    TermsRef = interest.TermsRef,
                    StartDay = interest.StartDay,
                    EndDay = interest.EndDay,
                    Notes = interest.Notes,
                    IsExtinguished = interest.IsExtinguished,
                });
            return deep;
        }
    }

    /// <summary>Production adapter over the live TownWorld building records.</summary>
    public sealed class TownWorldParcelBuildingIndex : IParcelBuildingIndex
    {
        private readonly TownWorldController world;

        public TownWorldParcelBuildingIndex(TownWorldController world)
        {
            this.world = world;
        }

        public IReadOnlyList<ParcelBuildingFootprint> BuildingsOnParcel(string parcelId)
        {
            var result = new List<ParcelBuildingFootprint>();
            if (world == null || world.Buildings == null || string.IsNullOrWhiteSpace(parcelId)) return result;
            foreach (PlacedBuilding building in world.Buildings)
            {
                if (building == null || building.footprint.width <= 0 || building.footprint.depth <= 0) continue;
                string plotKey = building.plotId.ToString();
                if (!string.Equals(parcelId, plotKey, StringComparison.Ordinal)
                    && !string.Equals(parcelId, "plot-" + plotKey, StringComparison.Ordinal)) continue;
                result.Add(new ParcelBuildingFootprint(building.id.ToString(), building.footprint));
            }
            return result;
        }
    }
}
