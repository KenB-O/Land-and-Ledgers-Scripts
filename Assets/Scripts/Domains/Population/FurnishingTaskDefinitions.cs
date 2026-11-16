using System;
using LandLedgers.Tasks;

namespace LandLedgers.Population
{
    /// <summary>
    /// B7 (expanded campaign 2026-11-16): TaskAuthority definitions for
    /// directed furnishing work — install, move, remove — following the
    /// Pass2TaskCatalogs pattern (RegisterAll, duplicate ids rejected
    /// deterministically by the authority).
    ///
    /// These definitions exist so the production driver (G) CAN mint
    /// furnishing work through the shared TaskAuthority when it wires
    /// execution. The presence contract itself does NOT mint tasks: a
    /// minted task whose completion does nothing would look like the
    /// install happened, and completion needs a furnishing-specific outcome
    /// resolver that does not exist yet (G wiring). Registering the
    /// definitions authorizes the work; it does not start it.
    ///
    /// Base minutes are first-pass calibration, documented for the owner
    /// to correct (same standing as the plan's other proposed defaults):
    /// installing a piece of furniture is hands-on work on the scale of
    /// the 30-minute "Order from supplier" task; moving or removing one is
    /// half that. Unskilled timing (no skill id — no carpentry skill is
    /// required to carry a chair across a room), interruptible (the
    /// default — a directed action can itself be interrupted), Normal
    /// priority. CompatibleLocations is left empty on purpose: the target
    /// location lives on the DirectedFurnishingAction record, not on
    /// WorkTask.LocationId (which is never set anywhere except Copy).
    /// </summary>
    public static class FurnishingTaskDefinitions
    {
        /// <summary>Directed install of a furnishing item (B0 TryInstall work).</summary>
        public const string InstallId = "furnishing.install";

        /// <summary>Directed move of an installed furnishing item (B0 TryMove work).</summary>
        public const string MoveId = "furnishing.move";

        /// <summary>Directed removal of an installed furnishing item (B0 TryRemove work).</summary>
        public const string RemoveId = "furnishing.remove";

        /// <summary>First-pass calibration: install base minutes (see the class summary).</summary>
        public const int InstallBaseMinutes = 30;

        /// <summary>First-pass calibration: move base minutes (see the class summary).</summary>
        public const int MoveBaseMinutes = 15;

        /// <summary>First-pass calibration: remove base minutes (see the class summary).</summary>
        public const int RemoveBaseMinutes = 15;

        /// <summary>Registers the three furnishing definitions on the authority (Pass2TaskCatalogs pattern).</summary>
        public static void RegisterAll(TaskAuthority authority)
        {
            if (authority == null)
                throw new ArgumentNullException(nameof(authority));

            string ignored;

            var install = new TaskDefinition(InstallId, "Install furniture", InstallBaseMinutes);
            install.SetRequiredSkill(null, null);
            install.SetDefaultPriority(TaskPriority.Normal);
            install.DomainTags.Add("furnishing");
            install.DomainTags.Add("household");
            authority.RegisterDefinition(install, out ignored);

            var move = new TaskDefinition(MoveId, "Move furniture", MoveBaseMinutes);
            move.SetRequiredSkill(null, null);
            move.SetDefaultPriority(TaskPriority.Normal);
            move.DomainTags.Add("furnishing");
            move.DomainTags.Add("household");
            authority.RegisterDefinition(move, out ignored);

            var remove = new TaskDefinition(RemoveId, "Remove furniture", RemoveBaseMinutes);
            remove.SetRequiredSkill(null, null);
            remove.SetDefaultPriority(TaskPriority.Normal);
            remove.DomainTags.Add("furnishing");
            remove.DomainTags.Add("household");
            authority.RegisterDefinition(remove, out ignored);
        }
    }
}
