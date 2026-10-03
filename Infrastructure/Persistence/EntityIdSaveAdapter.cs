using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Persistence
{
    /// <summary>
    /// HF-1: persists the <see cref="EntityIdRegistry"/> per-kind cursors through the existing
    /// save envelope (a single <c>entityIdCursors</c> list on the top-level save DTO).
    ///
    /// Legacy kinds (Person/Household/Building) keep their existing M1 allocator fields as
    /// the authority; this adapter mirrors those next-values into the registry at the
    /// save/load boundary so the registry's typed view can never diverge or renumber them.
    /// New kinds persist solely through the cursor list.
    /// </summary>
    public static class EntityIdSaveAdapter
    {
        /// <summary>
        /// Save path: mirror legacy next-values into the registry, then snapshot all cursors
        /// onto the DTO. Call after the legacy nextPersonId/nextHouseholdId/nextBuildingId
        /// fields have been written.
        /// </summary>
        public static void WriteCursors(
            LandLedgersSaveGameDto dto,
            EntityIdRegistry registry,
            int nextPersonId,
            int nextHouseholdId,
            int nextBuildingId)
        {
            if (dto == null || registry == null)
            {
                return;
            }

            registry.SyncFromLegacy(EntityKind.Person, nextPersonId);
            registry.SyncFromLegacy(EntityKind.Household, nextHouseholdId);
            registry.SyncFromLegacy(EntityKind.Building, nextBuildingId);

            dto.entityIdCursors ??= new List<EntityIdCursorDto>();
            dto.entityIdCursors.Clear();
            foreach (EntityIdCursor cursor in registry.SnapshotCursors())
            {
                dto.entityIdCursors.Add(new EntityIdCursorDto
                {
                    kind = cursor.Kind,
                    nextId = cursor.NextId,
                });
            }
        }

        /// <summary>
        /// Load path: restore new-kind cursors from the DTO, then re-mirror the legacy
        /// next-values (legacy fields win for Person/Household/Building). Returns diagnostics.
        /// </summary>
        public static List<string> ReadCursors(
            LandLedgersSaveGameDto dto,
            EntityIdRegistry registry,
            int nextPersonId,
            int nextHouseholdId,
            int nextBuildingId)
        {
            var diagnostics = new List<string>();
            if (dto == null || registry == null)
            {
                return diagnostics;
            }

            if (dto.entityIdCursors != null)
            {
                var cursors = new List<EntityIdCursor>(dto.entityIdCursors.Count);
                foreach (EntityIdCursorDto cursorDto in dto.entityIdCursors)
                {
                    if (cursorDto == null)
                    {
                        continue;
                    }

                    cursors.Add(new EntityIdCursor(cursorDto.kind, cursorDto.nextId));
                }

                diagnostics.AddRange(registry.RestoreCursors(cursors));
            }

            // Legacy fields remain the authority for their kinds: re-mirror so a stale or
            // hand-edited cursor list can never renumber existing Person/Household/Building IDs.
            registry.SyncFromLegacy(EntityKind.Person, nextPersonId);
            registry.SyncFromLegacy(EntityKind.Household, nextHouseholdId);
            registry.SyncFromLegacy(EntityKind.Building, nextBuildingId);
            return diagnostics;
        }
    }

    /// <summary>HF-1: persisted per-kind ID cursor. Part of the save envelope.</summary>
    [Serializable]
    public sealed class EntityIdCursorDto
    {
        public EntityKind kind;
        public int nextId;
    }
}
