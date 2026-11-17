using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// G-2 (W-G4 Workstream 2, expanded campaign 2026-11-16): the
    /// production IPlayerFurnishingPresenceDriver. Binds B7's presence
    /// contract to the live player systems, following the mapping in
    /// IPlayerFurnishingPresenceDriver's summary exactly:
    /// - PlayerPersonId, CurrentLocationId, IsTravelling: straight off
    ///   the PlayerDirector (SimulationDrivers.PlayerDirector).
    /// - CurrentTaskDefinitionId: the director's CurrentTaskId resolved
    ///   through the hub's TaskAuthority (FindTask) to its DefinitionId.
    ///   The interface doc's claim "there is no GetAllOpenTasks on the
    ///   authority" is wrong (E cp88: TaskAuthority.GetAllOpenTasks
    ///   exists at Core/Tasks/TaskAuthorityLifecycle.cs:283) — but the
    ///   binding does not need it: the director's current task is the
    ///   source, per the doc's own recipe.
    /// - IsOffMap: the drivers' live OffMapJourneyAuthority (the single
    ///   instance; a second one would read empty state, so the driver
    ///   shares it through SimulationDrivers' now-public accessor).
    /// - TryRouteMinutes: the hub's shared JourneyModel (hub.Journeys —
    ///   the drivers fall back to it, so it IS the shared instance),
    ///   at TravelMode.Foot. The player walks: no mount state exists on
    ///   the director, and Foot never under-books travel time. If a
    ///   mount system lands, this driver is the single seam to change.
    /// - TryTravelTo: PlayerDirector.TryIssueMoveIntent with the hub's
    ///   WorkTimeBudgetStore — real minutes booked against the budget,
    ///   the player never exempt — and a null avatar. No production
    ///   IPlayerAvatar implementation exists; the director works
    ///   headless (its own doc), so null is the honest value, not a
    ///   placeholder. A missing budget store refuses loudly instead of
    ///   issuing unbooked travel.
    /// - TryInterruptRoutineActivity / TryCancelTravel:
    ///   PlayerDirector.CancelCurrent through the hub's authority and
    ///   budgets (a task is interrupted through the authority, travel
    ///   stops with unspent minutes released).
    /// - TravelDestinationLocationId: the destination THIS driver issued,
    ///   and only while the director is still travelling — the director
    ///   keeps travelDestination private, so the driver remembers what
    ///   it issued. A trip issued by another system reports null
    ///   (unknown destination), and the contract refuses to join it.
    ///
    /// The driver resolves its scene objects lazily (FindAnyObjectByType
    /// with caching, the manager's AutoWire pattern) so boot ordering
    /// never matters — the contract is only used during gameplay, long
    /// after the scene is up. Any missing dependency fails closed: the
    /// read properties return safe defaults (no location, no task, not
    /// travelling, on-map) and the issuing methods refuse loudly, so
    /// nothing executes remotely on a half-wired scene. The driver holds
    /// no save seam: a mid-travel Continue drops the pending directed
    /// action, per the contract's own documented boundary.
    ///
    /// The authorized-agent source is NOT bound here: no production
    /// authorized-agent authority exists (no IsAuthorizedAgent
    /// implementation anywhere in production code), so the bootstrapper
    /// constructs the contract with a null source and the contract
    /// refuses agent actions loudly ("no authorized-agent source
    /// bound") — an agent is never assumed authorized. Binding a real
    /// agent source is a follow-up, not this slice.
    /// </summary>
    public sealed class PlayerFurnishingPresenceDriver : IPlayerFurnishingPresenceDriver
    {
        private SimulationDrivers drivers;
        private SimulationSystemsHub hub;
        private string issuedTravelDestination;

        private SimulationDrivers Drivers => drivers ??= Object.FindAnyObjectByType<SimulationDrivers>();
        private SimulationSystemsHub Hub => hub ??= Object.FindAnyObjectByType<SimulationSystemsHub>();
        private PlayerDirector Director => Drivers?.PlayerDirector;
        private TaskAuthority Tasks => Hub?.Tasks;
        private WorkTimeBudgetStore Budgets => Hub?.WorkTimeBudgets;
        private JourneyModel Journeys => Hub?.Journeys;
        private OffMapJourneyAuthority OffMap => Drivers?.OffMapJourneys;

        public EntityId PlayerPersonId
        {
            get
            {
                PlayerDirector director = Director;
                return director != null ? director.PlayerPersonId : default(EntityId);
            }
        }

        public string CurrentLocationId => Director?.CurrentLocationId;

        public bool IsTravelling
        {
            get
            {
                PlayerDirector director = Director;
                return director != null && director.IsTravelling;
            }
        }

        public string TravelDestinationLocationId
        {
            get
            {
                PlayerDirector director = Director;
                if (director == null || !director.IsTravelling) return null;
                return issuedTravelDestination;
            }
        }

        public string CurrentTaskDefinitionId
        {
            get
            {
                PlayerDirector director = Director;
                if (director == null) return null;
                EntityId taskId = director.CurrentTaskId;
                if (taskId.Kind == EntityKind.Unspecified) return null;
                TaskAuthority authority = Tasks;
                if (authority == null) return null;
                WorkTask task = authority.FindTask(taskId);
                return task?.DefinitionId;
            }
        }

        public bool IsOffMap
        {
            get
            {
                PlayerDirector director = Director;
                OffMapJourneyAuthority offMap = OffMap;
                if (director == null || offMap == null) return false;
                return offMap.IsOffMapPerson(director.PlayerPersonId.Id);
            }
        }

        public bool TryRouteMinutes(string fromLocationId, string toLocationId, out int minutes, out string reason)
        {
            minutes = 0;
            reason = null;
            JourneyModel journeys = Journeys;
            if (journeys == null)
            {
                reason = "PlayerFurnishingPresenceDriver: no journey model — the route cannot be computed.";
                return false;
            }
            JourneyRoute route = journeys.FindRoute(fromLocationId, toLocationId, TravelMode.Foot);
            if (!route.Found)
            {
                reason = route.Diagnostic;
                return false;
            }
            minutes = route.TotalMinutes;
            return true;
        }

        public bool TryTravelTo(string destinationLocationId, int minutes, out string reason)
        {
            reason = null;
            PlayerDirector director = Director;
            if (director == null)
            {
                reason = "PlayerFurnishingPresenceDriver: no player director — travel cannot be issued.";
                return false;
            }
            WorkTimeBudgetStore budgets = Budgets;
            if (budgets == null)
            {
                reason = "PlayerFurnishingPresenceDriver: no work-time budget store — travel minutes cannot be booked, so the trip is refused rather than issued unbooked.";
                return false;
            }
            if (!director.TryIssueMoveIntent(destinationLocationId, minutes, budgets, null, out reason))
                return false;
            issuedTravelDestination = destinationLocationId;
            return true;
        }

        public bool TryInterruptRoutineActivity(out string reason)
        {
            reason = null;
            PlayerDirector director = Director;
            if (director == null)
            {
                reason = "PlayerFurnishingPresenceDriver: no player director — nothing to interrupt.";
                return false;
            }
            if (!director.CancelCurrent(Tasks, Budgets, null))
            {
                reason = "PlayerFurnishingPresenceDriver: the player has no current activity to interrupt.";
                return false;
            }
            issuedTravelDestination = null;
            return true;
        }

        public bool TryCancelTravel(out string reason)
        {
            reason = null;
            PlayerDirector director = Director;
            if (director == null)
            {
                reason = "PlayerFurnishingPresenceDriver: no player director — no trip to cancel.";
                return false;
            }
            if (!director.IsTravelling)
            {
                reason = "PlayerFurnishingPresenceDriver: the player is not travelling — no trip to cancel.";
                return false;
            }
            if (!director.CancelCurrent(Tasks, Budgets, null))
            {
                reason = "PlayerFurnishingPresenceDriver: the trip could not be cancelled.";
                return false;
            }
            issuedTravelDestination = null;
            return true;
        }
    }
}
