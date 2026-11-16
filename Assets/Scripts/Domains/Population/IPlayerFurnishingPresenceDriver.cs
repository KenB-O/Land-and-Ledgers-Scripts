using LandLedgers.Primitives;

namespace LandLedgers.Population
{
    /// <summary>
    /// B7 (expanded campaign 2026-11-16): the seam between the
    /// player-physical-presence contract
    /// (<see cref="PlayerFurnishingPresenceContract"/>) and the production
    /// player systems. The contract lives in Domains/Population and must NOT
    /// reference Orchestration/Player (DEPENDENCY_RULES: Orchestration
    /// coordinates Domains; Domains use Core primitives), and the harness
    /// does not compile PlayerDirector.cs in the B lane, so the contract
    /// talks to the player ONLY through this interface. Agent G binds the
    /// production driver; tests bind a fake.
    ///
    /// PRODUCTION BINDING (G wiring — the driver is G's work, not this
    /// slice's):
    /// - PlayerPersonId, CurrentLocationId, IsTravelling: read straight off
    ///   the PlayerDirector (PlayerPersonId, CurrentLocationId, IsTravelling).
    /// - CurrentTaskDefinitionId: read the director's CurrentTaskId (public)
    ///   and resolve it through the TaskAuthority (FindTask) to its
    ///   DefinitionId; null/empty when the director has no current task.
    ///   There is no GetAllOpenTasks on the authority, and the director is
    ///   the single source for what the player is doing — its own IsBusy
    ///   rule has the same scope, and TaskLifecyclePolicy blocks a second
    ///   concurrent start for the same assignee, so at most one in-progress
    ///   task per assignee exists in practice. If the director's current
    ///   task is ever not the player's only in-progress task, the driver
    ///   must report a non-routine id first (the contract refuses on any
    ///   non-routine task).
    /// - IsOffMap: OffMapJourneyAuthority.IsOffMapPerson(playerPersonId.Id).
    /// - TryRouteMinutes: the production journey/route authority
    ///   (FindRoute between the two location ids at the travel mode the
    ///   driver serves). Must return the REAL routed minutes, never an
    ///   estimate that teleports the player, and must fail loudly when no
    ///   route exists (NX-2C weather closures honored, unreachable refused
    ///   with the model's diagnostic).
    /// - TryTravelTo: PlayerDirector.TryIssueMoveIntent(destination,
    ///   minutes, budgets, avatar, out reason) — real minutes booked
    ///   against the work-time budget, player never exempt. The driver
    ///   must remember the destination it issued, because the director
    ///   keeps travelDestination private (only inside its
    ///   DescribeActivity string) and the contract can only join a trip
    ///   it can prove ends at the target.
    /// - TryInterruptRoutineActivity: PlayerDirector.CancelCurrent
    ///   (authority, budgets, avatar) — interrupts the director's current
    ///   task through the authority (work preserved, remainder released)
    ///   and stops travel with unspent minutes released. Only ever called
    ///   by the contract for a routine (life-task) interruption.
    /// - TryCancelTravel: PlayerDirector.CancelCurrent while travelling —
    ///   stops the trip, releases unspent minutes.
    /// - TravelDestinationLocationId: the destination of the player's
    ///   current trip when the driver knows it (a trip the driver itself
    ///   issued), else null. The contract treats unknown-destination
    ///   travel as travelling elsewhere and refuses — it never assumes a
    ///   trip ends at the target. A public PlayerDirector travel-destination
    ///   getter would let the driver report trips issued by other systems
    ///   too; until one exists, only driver-issued trips can be joined.
    /// </summary>
    public interface IPlayerFurnishingPresenceDriver
    {
        /// <summary>The player person, as an EntityId of kind Person.</summary>
        EntityId PlayerPersonId { get; }

        /// <summary>The player's current logical location id.</summary>
        string CurrentLocationId { get; }

        /// <summary>True while the player is mid-travel.</summary>
        bool IsTravelling { get; }

        /// <summary>
        /// Destination of the player's current trip when the driver knows it,
        /// else null. See the interface summary: only driver-issued trips
        /// are knowable today.
        /// </summary>
        string TravelDestinationLocationId { get; }

        /// <summary>
        /// Definition id of the player's current in-progress task, or
        /// null/empty when the player has no in-progress task. Resolved from
        /// the director's current task through the task authority.
        /// </summary>
        string CurrentTaskDefinitionId { get; }

        /// <summary>True while the player person is off-map.</summary>
        bool IsOffMap { get; }

        /// <summary>
        /// Real routed travel minutes between two location ids. Returns false
        /// (with a reason) when no route exists; minutes must be positive for
        /// distinct locations — a non-positive result is a broken route
        /// provider, and the contract refuses on it rather than teleporting.
        /// </summary>
        bool TryRouteMinutes(string fromLocationId, string toLocationId, out int minutes, out string reason);

        /// <summary>
        /// Issues real travel to the destination for the given minutes
        /// (booked against the work-time budget). Returns false with a reason
        /// when the director refuses the move intent.
        /// </summary>
        bool TryTravelTo(string destinationLocationId, int minutes, out string reason);

        /// <summary>
        /// Interrupts the player's current routine activity through the
        /// director/authority. Returns false with a reason when there is no
        /// routine activity to interrupt or the interruption is refused.
        /// </summary>
        bool TryInterruptRoutineActivity(out string reason);

        /// <summary>
        /// Stops the player's current travel, releasing unspent minutes.
        /// Returns false with a reason when the player is not travelling or
        /// the cancel is refused.
        /// </summary>
        bool TryCancelTravel(out string reason);
    }

    /// <summary>
    /// B7: authorized-agent source for the presence contract. The player may
    /// delegate a presence-required furnishing action to an authorized agent
    /// (owner direction: delegation through authorized agents is legitimate
    /// where historically and contractually valid). The contract holds this
    /// source nullable: a null source plus a supplied agent id is refused
    /// loudly ("no authorized-agent source bound") — an agent is never
    /// assumed authorized. The source answers ONLY whether the person is
    /// authorized for the household; it does NOT prove the agent is at the
    /// target location — the executing driver must confirm the agent is
    /// physically at the target before executing (G wiring item; B7 has no
    /// agent-location authority and inventing one would be inventing a
    /// system).
    /// </summary>
    public interface IFurnishingAgentAuthorization
    {
        /// <summary>True when the person is an authorized agent for the household.</summary>
        bool IsAuthorizedAgent(string householdId, int agentPersonId);
    }

    /// <summary>
    /// B7: the caller's execution of a directed furnishing action once the
    /// contract has brought it to Ready. Follows the B0 TryInstall/TryMove/
    /// TryRemove convention — returns null on success, a refusal string on
    /// failure. The contract proves presence, travel, deferral and
    /// interruption; the executor is the caller's work (a B0 install, a
    /// purchase settlement, a move), and a failed executor leaves the action
    /// Failed loudly, never silently retried.
    /// </summary>
    /// <param name="action">The directed action, in Ready status.</param>
    /// <returns>Null on success, otherwise the refusal reason.</returns>
    public delegate string FurnishingActionExecutor(DirectedFurnishingAction action);
}
