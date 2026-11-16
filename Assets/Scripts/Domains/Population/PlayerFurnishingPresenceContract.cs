using System;

namespace LandLedgers.Population
{
    /// <summary>
    /// B7 (expanded campaign 2026-11-16): lifecycle of one player-directed
    /// furnishing action. A record exists only once the gate reaches a
    /// verdict that keeps it — a refused request creates NO record.
    /// Requested is the initial status; the gate moves it to Ready,
    /// Travelling or Deferred. Travelling becomes Ready on arrival (the
    /// contract's Advance) or Deferred if the player leaves the map
    /// mid-trip. Ready becomes Done (executor succeeded) or Failed
    /// (executor refused, loudly, never silently retried). CancelPending
    /// moves Requested / Travelling / Ready / Deferred to Cancelled.
    /// </summary>
    public enum DirectedActionStatus
    {
        Requested = 0,
        Travelling = 1,
        Ready = 2,
        Deferred = 3,
        Done = 4,
        Failed = 5,
        Cancelled = 6,
    }

    /// <summary>
    /// Verdict of one gate attempt. Ready and Travelling and Deferred keep
    /// the action record; Refused creates no record on a fresh request and
    /// leaves a deferred record Deferred on a retry.
    /// </summary>
    public enum DirectedActionVerdict
    {
        Ready = 0,
        Travelling = 1,
        Deferred = 2,
        Refused = 3,
    }

    /// <summary>
    /// B7: one player-directed furnishing action, as the presence contract
    /// tracks it. Runtime-only record (B1/B4/B5/B6 precedent): there is no
    /// save seam in this slice, so a mid-travel Continue drops the pending
    /// directed action — the player still arrives (the director's own state
    /// persists through its own save path) but the furnishing action must be
    /// re-issued. Nothing is faked and nothing is lost except the request;
    /// the seam is a documented follow-up, not silent.
    ///
    /// The target location lives ON this record. WorkTask.LocationId is
    /// never set anywhere except Copy (always empty in practice), so the
    /// contract never relies on it — arrival is checked against the
    /// driver's CurrentLocationId.
    /// </summary>
    public sealed class DirectedFurnishingAction
    {
        /// <summary>Stable id for this directed action (Guid, "N" format).</summary>
        public string ActionId = string.Empty;

        /// <summary>What the player directed (Buy, Install, Move, ...).</summary>
        public FurnishingActionKind ActionKind;

        /// <summary>Household the action belongs to.</summary>
        public string HouseholdId = string.Empty;

        /// <summary>Asset the action concerns (empty when the action has none, e.g. InspectProperty).</summary>
        public string AssetId = string.Empty;

        /// <summary>Location the actor must be at for the action to execute.</summary>
        public string TargetLocationId = string.Empty;

        /// <summary>
        /// Person acting, as a legacy int id. 0 means the player acts
        /// personally; any other value means an authorized agent acts for the
        /// player (see ViaAuthorizedAgent).
        /// </summary>
        public int AgentPersonId;

        /// <summary>
        /// True when an authorized agent, not the player, performs this
        /// action. The executing driver must confirm the agent is physically
        /// at the target location before executing — this flag is the
        /// authorization, not a location proof.
        /// </summary>
        public bool ViaAuthorizedAgent;

        /// <summary>Current lifecycle status.</summary>
        public DirectedActionStatus Status;

        /// <summary>Refusal reason when the action Failed, else null.</summary>
        public string RefusalReason;

        /// <summary>Free-form notes (driver/interruption detail).</summary>
        public string Note = string.Empty;
    }

    /// <summary>
    /// B7 (expanded campaign 2026-11-16): the player physical-presence
    /// contract for furnishing actions. A player-directed buy, install,
    /// move, remove, sell, property inspection or contents selection goes
    /// through the same task / physical-arrival authorities as any other
    /// directed work, with priority over routine behaviour — and routine
    /// sleep / eat / drink proceed without player commands, never forced.
    ///
    /// What this contract proves (and its tests pin): a directed install
    /// interrupts routine behaviour without teleportation (travel time is
    /// real — the location is unchanged until the full minutes are
    /// advanced); routine sleep/eat proceed without player commands (Free
    /// verdict, the director untouched); a directed action for an off-map
    /// player defers and never executes remotely; a busy player doing
    /// non-routine work refuses ("cancel first"); a travelling player
    /// refuses unless the trip provably ends at the target.
    ///
    /// What it does NOT do (documented boundaries, G wiring items):
    /// - It does NOT mint TaskAuthority tasks for the furnishing work.
    ///   WorkTask.LocationId is always empty and completion would need a
    ///   furnishing-specific outcome resolver that does not exist — minting
    ///   a task whose completion does nothing would look like the install
    ///   happened. FurnishingTaskDefinitions registers the definitions so
    ///   the production driver (G) CAN mint them through the shared
    ///   authority when it wires execution.
    /// - It does NOT rewrite the day's plan. It interrupts an in-flight
    ///   routine task; the work-time budget is the shared arbiter (travel
    ///   + work minutes booked by the directed action are minutes the plan
    ///   cannot also spend).
    /// - It does NOT persist the pending action (no save seam — see the
    ///   record summary above).
    /// - It does NOT verify an authorized agent's location (see
    ///   IFurnishingAgentAuthorization).
    ///
    /// Wiring items for Agent D (player selection API, D-9): consume
    /// RequestDirectedAction / Advance / Execute for player-directed
    /// installs, moves and removals, and FurnishingPresencePolicy for the
    /// presence table; a contents selection (SelectContents) is a
    /// presence-required action, so D-6/D-7 must route it through this
    /// contract rather than executing it remotely.
    /// Wiring items for Agent F (purchase flow): a Buy is presence-required,
    /// so the purchase flow must hold the purchase at the contract's verdict
    /// — Ready (player present or authorized agent) executes, Travelling
    /// waits for Advance to Ready, Deferred waits for the player's return,
    /// Refused refuses the purchase rather than settling it remotely.
    /// Wiring items for Agent G: bind the production
    /// IPlayerFurnishingPresenceDriver (see that interface's summary for
    /// the exact mapping), drive Advance once per tick for Travelling
    /// actions, and call Execute with the B0 install / move / remove
    /// executor once Ready.
    /// </summary>
    public sealed class PlayerFurnishingPresenceContract
    {
        private readonly IPlayerFurnishingPresenceDriver driver;
        private readonly IFurnishingAgentAuthorization agentAuthorization;

        /// <summary>
        /// Builds the contract. The driver is required (a contract with no
        /// player to talk to refuses everything silently if it is allowed to
        /// exist — so it is not allowed to exist). The agent authorization
        /// source is optional; a null source plus a supplied agent id is
        /// refused loudly at request time, never assumed authorized.
        /// </summary>
        public PlayerFurnishingPresenceContract(
            IPlayerFurnishingPresenceDriver driver,
            IFurnishingAgentAuthorization agentAuthorization = null)
        {
            if (driver == null)
                throw new ArgumentNullException(nameof(driver));
            this.driver = driver;
            this.agentAuthorization = agentAuthorization;
        }

        /// <summary>
        /// Requests a player-directed furnishing action.
        /// - Presence-free kinds (Use): Ready at once; the driver is never
        ///   touched, routine use never interrupts anything.
        /// - Authorized agent (agentPersonId != 0, source bound and
        ///   authorizing): Ready via the agent, no travel issued, the
        ///   player's location untouched. The executing driver must confirm
        ///   the agent is at the target before executing.
        /// - Player acting personally: the player gate (off-map defers,
        ///   travelling joins or refuses, routine task interrupts,
        ///   non-routine task refuses, already-here is Ready, otherwise
        ///   real routed travel is issued).
        /// A Refused verdict creates no record (action is null).
        /// </summary>
        /// <param name="agentPersonId">0 for the player personally, else the acting agent's legacy int id.</param>
        /// <returns>The verdict; the action record when one is kept, else null.</returns>
        public DirectedActionVerdict RequestDirectedAction(
            FurnishingActionKind kind,
            string householdId,
            string assetId,
            string targetLocationId,
            int agentPersonId,
            out DirectedFurnishingAction action,
            out string reason)
        {
            action = null;
            reason = null;

            if (!FurnishingPresencePolicy.IsPresenceRequired(kind))
            {
                action = NewAction(kind, householdId, assetId, targetLocationId, agentPersonId);
                action.Status = DirectedActionStatus.Ready;
                action.Note = "Presence-free routine use — no travel, no interruption.";
                return DirectedActionVerdict.Ready;
            }

            if (string.IsNullOrEmpty(targetLocationId))
            {
                reason = "PlayerFurnishingPresenceContract.RequestDirectedAction: a presence-required "
                    + kind + " action needs a target location.";
                return DirectedActionVerdict.Refused;
            }

            if (agentPersonId != 0)
            {
                if (agentAuthorization == null)
                {
                    reason = "PlayerFurnishingPresenceContract.RequestDirectedAction: agent "
                        + agentPersonId + " was supplied for household " + householdId
                        + " but no authorized-agent source is bound — an agent is never assumed authorized.";
                    return DirectedActionVerdict.Refused;
                }

                if (!agentAuthorization.IsAuthorizedAgent(householdId, agentPersonId))
                {
                    reason = "PlayerFurnishingPresenceContract.RequestDirectedAction: agent "
                        + agentPersonId + " is not authorized for household " + householdId + ".";
                    return DirectedActionVerdict.Refused;
                }

                action = NewAction(kind, householdId, assetId, targetLocationId, agentPersonId);
                action.Status = DirectedActionStatus.Ready;
                action.ViaAuthorizedAgent = true;
                action.Note = "Ready via authorized agent " + agentPersonId
                    + " — the executing driver must confirm the agent is at the target location before executing.";
                return DirectedActionVerdict.Ready;
            }

            action = NewAction(kind, householdId, assetId, targetLocationId, agentPersonId);
            DirectedActionVerdict verdict = RunPlayerGate(action, out reason);
            if (verdict == DirectedActionVerdict.Refused)
            {
                action = null;
            }
            return verdict;
        }

        /// <summary>
        /// Advances a Travelling action one tick: when the driver's current
        /// location is the target, the action becomes Ready and this returns
        /// true. A Ready action stays Ready (true). Any other status returns
        /// false. A player who left the map mid-trip drops the action to
        /// Deferred (retry it with RetryDeferred after the return).
        /// </summary>
        public bool Advance(DirectedFurnishingAction action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (action.Status == DirectedActionStatus.Ready)
                return true;

            if (action.Status != DirectedActionStatus.Travelling)
                return false;

            if (driver.IsOffMap)
            {
                action.Status = DirectedActionStatus.Deferred;
                action.Note = "Player left the map mid-trip — action deferred, never executed remotely.";
                return false;
            }

            if (driver.CurrentLocationId == action.TargetLocationId)
            {
                action.Status = DirectedActionStatus.Ready;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes a Ready action through the caller's executor (a B0
        /// install / move / remove, a purchase settlement). Returns null on
        /// success (action Done) or the executor's refusal (action Failed,
        /// loudly, never silently retried). An action that is not Ready
        /// refuses without calling the executor; a null executor refuses
        /// without executing.
        /// </summary>
        public string Execute(DirectedFurnishingAction action, FurnishingActionExecutor executor)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (action.Status != DirectedActionStatus.Ready)
            {
                return "PlayerFurnishingPresenceContract.Execute: action " + action.ActionId
                    + " is not Ready (status " + action.Status + ") — execute only after arrival.";
            }

            if (executor == null)
            {
                return "PlayerFurnishingPresenceContract.Execute: no executor bound for action " + action.ActionId + ".";
            }

            string refusal = executor(action);
            if (refusal == null)
            {
                action.Status = DirectedActionStatus.Done;
                return null;
            }

            action.Status = DirectedActionStatus.Failed;
            action.RefusalReason = refusal;
            return refusal;
        }

        /// <summary>
        /// Re-runs the player gate for a Deferred action after the player
        /// returns to the map. Still off-map: stays Deferred (verdict
        /// Deferred). Otherwise the gate proceeds as a fresh request —
        /// travelling, ready or refused. A refused retry leaves the record
        /// Deferred (still pending, not failed); the verdict is Refused for
        /// this attempt. An action that is not Deferred refuses.
        /// </summary>
        public DirectedActionVerdict RetryDeferred(DirectedFurnishingAction action, out string reason)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            reason = null;
            if (action.Status != DirectedActionStatus.Deferred)
            {
                reason = "PlayerFurnishingPresenceContract.RetryDeferred: action " + action.ActionId
                    + " is not Deferred (status " + action.Status + ").";
                return DirectedActionVerdict.Refused;
            }

            return RunPlayerGate(action, out reason);
        }

        /// <summary>
        /// Cancels a pending action (Requested, Travelling, Ready or
        /// Deferred). A Travelling cancel stops the trip through the driver,
        /// releasing unspent minutes; if the driver refuses the cancel the
        /// action stays Travelling. Terminal actions (Done, Failed,
        /// Cancelled) refuse.
        /// </summary>
        public bool CancelPending(DirectedFurnishingAction action, out string reason)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            reason = null;
            switch (action.Status)
            {
                case DirectedActionStatus.Done:
                case DirectedActionStatus.Failed:
                case DirectedActionStatus.Cancelled:
                    reason = "PlayerFurnishingPresenceContract.CancelPending: action " + action.ActionId
                        + " is already terminal (status " + action.Status + ").";
                    return false;
                case DirectedActionStatus.Travelling:
                    if (!driver.TryCancelTravel(out reason))
                    {
                        return false;
                    }
                    action.Status = DirectedActionStatus.Cancelled;
                    return true;
                default:
                    action.Status = DirectedActionStatus.Cancelled;
                    return true;
            }
        }

        /// <summary>
        /// The player gate, shared by a fresh request and a deferred retry.
        /// Order matters: off-map defers first (never execute remotely);
        /// travelling joins a provable same-target trip or refuses; a current
        /// routine task is interrupted, a non-routine task refuses; already
        /// at the target is Ready; otherwise real routed travel is issued
        /// (never teleported — a non-positive route for distinct locations
        /// refuses loudly). Mutates the action's status; on Refused the
        /// status is left as the caller found it (Requested for a fresh
        /// request, Deferred for a retry).
        /// </summary>
        private DirectedActionVerdict RunPlayerGate(DirectedFurnishingAction action, out string reason)
        {
            reason = null;

            if (driver.IsOffMap)
            {
                action.Status = DirectedActionStatus.Deferred;
                reason = "PlayerFurnishingPresenceContract: the player is off-map — action "
                    + action.ActionId + " is recorded and deferred, never executed remotely.";
                return DirectedActionVerdict.Deferred;
            }

            if (driver.IsTravelling)
            {
                string destination = driver.TravelDestinationLocationId;
                if (!string.IsNullOrEmpty(destination) && destination == action.TargetLocationId)
                {
                    action.Status = DirectedActionStatus.Travelling;
                    action.Note = "Already travelling to the target — joining the trip, no second travel issued.";
                    return DirectedActionVerdict.Travelling;
                }

                reason = "PlayerFurnishingPresenceContract: the player is already travelling"
                    + (string.IsNullOrEmpty(destination) ? " (destination unknown — the trip cannot be proven to end at the target)" : " to " + destination)
                    + " — cancel the trip first.";
                return DirectedActionVerdict.Refused;
            }

            string currentTaskDefinitionId = driver.CurrentTaskDefinitionId;
            if (!string.IsNullOrEmpty(currentTaskDefinitionId))
            {
                if (FurnishingPresencePolicy.IsRoutineTaskDefinitionId(currentTaskDefinitionId))
                {
                    if (!driver.TryInterruptRoutineActivity(out reason))
                    {
                        reason = "PlayerFurnishingPresenceContract: could not interrupt the routine task "
                            + currentTaskDefinitionId + " — " + reason;
                        return DirectedActionVerdict.Refused;
                    }

                    action.Note = "Interrupted routine task " + currentTaskDefinitionId + " to act.";
                }
                else
                {
                    reason = "PlayerFurnishingPresenceContract: the player is busy with task "
                        + currentTaskDefinitionId + " — cancel it first.";
                    return DirectedActionVerdict.Refused;
                }
            }

            if (driver.CurrentLocationId == action.TargetLocationId)
            {
                action.Status = DirectedActionStatus.Ready;
                return DirectedActionVerdict.Ready;
            }

            int minutes;
            string routeReason;
            if (!driver.TryRouteMinutes(driver.CurrentLocationId, action.TargetLocationId, out minutes, out routeReason))
            {
                reason = "PlayerFurnishingPresenceContract: no route to " + action.TargetLocationId + " — " + routeReason;
                return DirectedActionVerdict.Refused;
            }

            if (minutes <= 0)
            {
                reason = "PlayerFurnishingPresenceContract: the route provider returned "
                    + minutes + " minutes for distinct locations " + driver.CurrentLocationId
                    + " to " + action.TargetLocationId + " — a non-positive route is a broken provider, never a teleport.";
                return DirectedActionVerdict.Refused;
            }

            string travelReason;
            if (!driver.TryTravelTo(action.TargetLocationId, minutes, out travelReason))
            {
                reason = "PlayerFurnishingPresenceContract: travel to " + action.TargetLocationId
                    + " was refused — " + travelReason;
                return DirectedActionVerdict.Refused;
            }

            action.Status = DirectedActionStatus.Travelling;
            action.Note = "Travelling to " + action.TargetLocationId + " (" + minutes + " minutes, real travel).";
            return DirectedActionVerdict.Travelling;
        }

        private static DirectedFurnishingAction NewAction(
            FurnishingActionKind kind,
            string householdId,
            string assetId,
            string targetLocationId,
            int agentPersonId)
        {
            return new DirectedFurnishingAction
            {
                ActionId = Guid.NewGuid().ToString("N"),
                ActionKind = kind,
                HouseholdId = householdId ?? string.Empty,
                AssetId = assetId ?? string.Empty,
                TargetLocationId = targetLocationId ?? string.Empty,
                AgentPersonId = agentPersonId,
                Status = DirectedActionStatus.Requested,
            };
        }
    }
}
