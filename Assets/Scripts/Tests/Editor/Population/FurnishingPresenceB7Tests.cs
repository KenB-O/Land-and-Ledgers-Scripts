using System.Collections.Generic;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// B7 (Agent B, expanded campaign 2026-11-16): the player
    /// physical-presence contract for furnishing actions. Proves the
    /// presence table (presence-required vs presence-free actions), the
    /// directed-action gate (a directed install interrupts routine
    /// behaviour without teleportation — travel time is real; routine
    /// sleep/eat proceed without player commands; a directed action for an
    /// off-map player defers and never executes remotely; a busy player
    /// doing non-routine work refuses; a travelling player joins only a
    /// trip provably ending at the target), authorized-agent delegation,
    /// and the furnishing task definitions the production driver mints
    /// through the shared TaskAuthority.
    ///
    /// The production driver is Agent G's wiring (see
    /// IPlayerFurnishingPresenceDriver); these tests bind a fake driver
    /// that models the seam faithfully — route table, real travel minutes
    /// advanced per tick, the director's current task resolved through a
    /// REAL TaskAuthority, interruption through the REAL
    /// TaskAuthority.InterruptTask. No test claims Unity compilation or
    /// PlayMode. No prices, suppliers or goods are invented here.
    /// </summary>
    [TestFixture]
    public sealed class FurnishingPresenceB7Tests
    {
        private const string HouseholdId = "household-1";
        private const string OtherHouseholdId = "household-2";
        private const string HouseLocationId = "house-1";
        private const string MillLocationId = "mill-site";
        private const string DistantLocationId = "distant-markets";
        private const string BedAssetId = "bed-1";
        private const int AgentPersonId = 7;

        private static readonly EntityId Player = EntityId.For(EntityKind.Person, 1);

        /// <summary>Fake presence driver: models the seam, never the real director.</summary>
        private sealed class FakePresenceDriver : IPlayerFurnishingPresenceDriver
        {
            public EntityId PlayerPersonId { get; set; } = Player;
            public string CurrentLocationId { get; set; } = HouseLocationId;
            public bool IsTravelling { get; set; }
            public string TravelDestinationLocationId { get; set; }
            public bool IsOffMap { get; set; }

            /// <summary>Direct current-task override (used when Authority is null).</summary>
            public string ForcedCurrentTaskDefinitionId { get; set; }

            /// <summary>
            /// Optional real authority: when set, the current task is the
            /// director's current task resolved through it — the production
            /// driver's mechanism (director.CurrentTaskId + FindTask).
            /// </summary>
            public TaskAuthority Authority { get; set; }
            public WorkTimeBudgetStore Budgets { get; set; }
            public EntityId DirectorCurrentTaskId { get; set; }

            public string CurrentTaskDefinitionId
            {
                get
                {
                    if (Authority != null && DirectorCurrentTaskId.Kind != EntityKind.Unspecified)
                    {
                        WorkTask task = Authority.FindTask(DirectorCurrentTaskId);
                        return task == null ? null : task.DefinitionId;
                    }
                    return ForcedCurrentTaskDefinitionId;
                }
            }

            public Dictionary<string, int> Routes = new Dictionary<string, int>();
            public int TravelTotalMinutes { get; private set; }
            public int TravelElapsedMinutes { get; private set; }

            public int RouteCalls { get; private set; }
            public int TravelCalls { get; private set; }
            public int InterruptCalls { get; private set; }
            public int CancelTravelCalls { get; private set; }

            public bool InterruptFails { get; set; }
            public string InterruptFailureReason { get; set; } = "task is not interruptible.";
            public bool TravelFails { get; set; }
            public string TravelFailureReason { get; set; } = "move intent refused.";

            public bool TryRouteMinutes(string fromLocationId, string toLocationId, out int minutes, out string reason)
            {
                RouteCalls++;
                int routed;
                if (Routes.TryGetValue(fromLocationId + "|" + toLocationId, out routed))
                {
                    minutes = routed;
                    reason = null;
                    return true;
                }

                minutes = 0;
                reason = "no route from " + fromLocationId + " to " + toLocationId + ".";
                return false;
            }

            public bool TryTravelTo(string destinationLocationId, int minutes, out string reason)
            {
                TravelCalls++;
                if (TravelFails)
                {
                    reason = TravelFailureReason;
                    return false;
                }

                IsTravelling = true;
                TravelDestinationLocationId = destinationLocationId;
                TravelTotalMinutes = minutes;
                TravelElapsedMinutes = 0;
                reason = null;
                return true;
            }

            public bool TryInterruptRoutineActivity(out string reason)
            {
                InterruptCalls++;
                if (InterruptFails)
                {
                    reason = InterruptFailureReason;
                    return false;
                }

                if (Authority != null && DirectorCurrentTaskId.Kind != EntityKind.Unspecified)
                {
                    return Authority.InterruptTask(DirectorCurrentTaskId, Budgets, out reason);
                }

                if (FurnishingPresencePolicy.IsRoutineTaskDefinitionId(ForcedCurrentTaskDefinitionId))
                {
                    ForcedCurrentTaskDefinitionId = null;
                    reason = null;
                    return true;
                }

                reason = "no routine activity to interrupt.";
                return false;
            }

            public bool TryCancelTravel(out string reason)
            {
                CancelTravelCalls++;
                if (!IsTravelling)
                {
                    reason = "not travelling.";
                    return false;
                }

                IsTravelling = false;
                TravelDestinationLocationId = null;
                reason = null;
                return true;
            }

            /// <summary>Per-tick travel progress: the location changes only when the full minutes are advanced.</summary>
            public void AdvanceTravel(int minutes)
            {
                if (!IsTravelling)
                    return;
                TravelElapsedMinutes += minutes;
                if (TravelElapsedMinutes < TravelTotalMinutes)
                    return;
                IsTravelling = false;
                CurrentLocationId = TravelDestinationLocationId;
                TravelDestinationLocationId = null;
            }
        }

        /// <summary>Fake agent authorization: answers from a set table, never assumes.</summary>
        private sealed class FakeAgentAuthorization : IFurnishingAgentAuthorization
        {
            public Dictionary<string, HashSet<int>> Authorized = new Dictionary<string, HashSet<int>>();

            public bool IsAuthorizedAgent(string householdId, int agentPersonId)
            {
                HashSet<int> agents;
                return Authorized.TryGetValue(householdId, out agents) && agents.Contains(agentPersonId);
            }
        }

        private static FakePresenceDriver DriverWithRoute(int minutes = 90)
        {
            var driver = new FakePresenceDriver();
            driver.Routes[HouseLocationId + "|" + MillLocationId] = minutes;
            return driver;
        }

        private static PlayerFurnishingPresenceContract Contract(FakePresenceDriver driver)
        {
            return new PlayerFurnishingPresenceContract(driver);
        }

        private static PlayerFurnishingPresenceContract ContractWithAgent(
            FakePresenceDriver driver, FakeAgentAuthorization authorization)
        {
            return new PlayerFurnishingPresenceContract(driver, authorization);
        }

        // ---- Presence table (O-4 proposed defaults) ----

        [Test]
        public void PresenceRequired_BuyInstallMoveRemoveSellInspectSelectContents()
        {
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.Buy));
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.Install));
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.Move));
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.Remove));
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.Sell));
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.InspectProperty));
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.SelectContents));
        }

        [Test]
        public void PresenceFree_Use()
        {
            Assert.IsFalse(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.Use));
            Assert.AreEqual(PresenceRequirement.Free, FurnishingPresencePolicy.RequirementFor(FurnishingActionKind.Use));
        }

        [Test]
        public void PresenceRequired_UnspecifiedDefaultsRequired()
        {
            Assert.IsTrue(FurnishingPresencePolicy.IsPresenceRequired(FurnishingActionKind.Unspecified));
            Assert.AreEqual(PresenceRequirement.Required, FurnishingPresencePolicy.RequirementFor(FurnishingActionKind.Unspecified));
        }

        [Test]
        public void IsRoutineTaskDefinitionId_EatDrinkSleep()
        {
            Assert.IsTrue(FurnishingPresencePolicy.IsRoutineTaskDefinitionId(Pass2TaskCatalogs.EatMealId));
            Assert.IsTrue(FurnishingPresencePolicy.IsRoutineTaskDefinitionId(Pass2TaskCatalogs.DrinkId));
            Assert.IsTrue(FurnishingPresencePolicy.IsRoutineTaskDefinitionId(Pass2TaskCatalogs.SleepId));
        }

        [Test]
        public void IsRoutineTaskDefinitionId_NonRoutineAndEmptyAreFalse()
        {
            Assert.IsFalse(FurnishingPresencePolicy.IsRoutineTaskDefinitionId("test.work"));
            Assert.IsFalse(FurnishingPresencePolicy.IsRoutineTaskDefinitionId(FurnishingTaskDefinitions.InstallId));
            Assert.IsFalse(FurnishingPresencePolicy.IsRoutineTaskDefinitionId(null));
            Assert.IsFalse(FurnishingPresencePolicy.IsRoutineTaskDefinitionId(string.Empty));
        }

        // ---- Presence-free routine use ----

        [Test]
        public void Request_UseAction_ReadyImmediately_DriverUntouched()
        {
            var driver = DriverWithRoute();
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Use, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Ready, verdict);
            Assert.IsNotNull(action);
            Assert.AreEqual(DirectedActionStatus.Ready, action.Status);
            Assert.AreEqual(0, driver.RouteCalls);
            Assert.AreEqual(0, driver.TravelCalls);
            Assert.AreEqual(0, driver.InterruptCalls);
            Assert.AreEqual(HouseLocationId, driver.CurrentLocationId);
        }

        // ---- Player present / travel is real ----

        [Test]
        public void Request_Install_PlayerAlreadyAtTarget_ReadyNoTravel()
        {
            var driver = DriverWithRoute();
            driver.CurrentLocationId = MillLocationId;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Ready, verdict);
            Assert.IsNotNull(action);
            Assert.AreEqual(DirectedActionStatus.Ready, action.Status);
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void Request_Install_PlayerElsewhere_TravellingWithRealMinutes_LocationUnchanged()
        {
            var driver = DriverWithRoute(90);
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Travelling, verdict);
            Assert.IsNotNull(action);
            Assert.AreEqual(DirectedActionStatus.Travelling, action.Status);
            Assert.AreEqual(1, driver.TravelCalls);
            Assert.IsTrue(driver.IsTravelling);
            Assert.AreEqual(HouseLocationId, driver.CurrentLocationId, "no teleportation: the location is unchanged until the full minutes are advanced");
            Assert.IsTrue(action.Note.Contains("90"));
        }

        [Test]
        public void Advance_PartialMinutes_LocationUnchangedStillTravelling()
        {
            var driver = DriverWithRoute(90);
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            driver.AdvanceTravel(45);
            Assert.IsFalse(contract.Advance(action));
            Assert.AreEqual(DirectedActionStatus.Travelling, action.Status);
            Assert.AreEqual(HouseLocationId, driver.CurrentLocationId);
        }

        [Test]
        public void Advance_FullMinutes_ReadyAtTarget()
        {
            var driver = DriverWithRoute(90);
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            driver.AdvanceTravel(45);
            Assert.IsFalse(contract.Advance(action));
            driver.AdvanceTravel(45);
            Assert.IsTrue(contract.Advance(action));
            Assert.AreEqual(DirectedActionStatus.Ready, action.Status);
            Assert.AreEqual(MillLocationId, driver.CurrentLocationId);
        }

        [Test]
        public void Execute_ReadyAction_RunsExecutor_SetsDone()
        {
            var driver = DriverWithRoute();
            driver.CurrentLocationId = MillLocationId;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            bool executorCalled = false;
            DirectedFurnishingAction executed = null;
            string result = contract.Execute(action, a => { executorCalled = true; executed = a; return null; });

            Assert.IsNull(result);
            Assert.IsTrue(executorCalled);
            Assert.AreEqual(action, executed);
            Assert.AreEqual(DirectedActionStatus.Done, action.Status);
        }

        [Test]
        public void Execute_FailingExecutor_SetsFailedWithReason()
        {
            var driver = DriverWithRoute();
            driver.CurrentLocationId = MillLocationId;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            string result = contract.Execute(action, a => "install refused: asset not registered.");

            Assert.AreEqual("install refused: asset not registered.", result);
            Assert.AreEqual(DirectedActionStatus.Failed, action.Status);
            Assert.AreEqual("install refused: asset not registered.", action.RefusalReason);
        }

        [Test]
        public void Execute_BeforeReady_RefusesWithoutCallingExecutor()
        {
            var driver = DriverWithRoute(90);
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            bool executorCalled = false;
            string result = contract.Execute(action, a => { executorCalled = true; return null; });

            Assert.IsNotNull(result);
            Assert.IsFalse(executorCalled);
            Assert.AreEqual(DirectedActionStatus.Travelling, action.Status);
        }

        // ---- Routine interruption ----

        [Test]
        public void Request_Install_WhileRoutineSleep_InterruptsAndTravels()
        {
            var driver = DriverWithRoute(90);
            driver.ForcedCurrentTaskDefinitionId = Pass2TaskCatalogs.SleepId;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Travelling, verdict);
            Assert.AreEqual(1, driver.InterruptCalls);
            Assert.IsNull(driver.ForcedCurrentTaskDefinitionId);
            Assert.AreEqual(1, driver.TravelCalls);
        }

        [Test]
        public void Request_Install_WhileRoutineTaskInterruptionFails_RefusedNoRecord()
        {
            var driver = DriverWithRoute(90);
            driver.ForcedCurrentTaskDefinitionId = Pass2TaskCatalogs.SleepId;
            driver.InterruptFails = true;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action, "a refused request creates no record");
            Assert.IsTrue(reason.Contains(Pass2TaskCatalogs.SleepId));
            Assert.IsTrue(reason.Contains("not interruptible"));
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void Request_Install_WhileWorkTask_RefusedBusyNoRecord()
        {
            var driver = DriverWithRoute(90);
            driver.ForcedCurrentTaskDefinitionId = "test.work";
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action, "a refused request creates no record");
            Assert.IsTrue(reason.Contains("test.work"));
            Assert.AreEqual(0, driver.InterruptCalls, "a non-routine task is never interrupted by the contract");
            Assert.AreEqual(0, driver.TravelCalls);
        }

        // ---- Travelling ----

        [Test]
        public void Request_Install_WhileTravellingElsewhere_Refused()
        {
            var driver = DriverWithRoute(90);
            driver.IsTravelling = true;
            driver.TravelDestinationLocationId = DistantLocationId;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action);
            Assert.IsTrue(reason.Contains(DistantLocationId));
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void Request_Install_WhileTravellingToSameTarget_JoinsTrip()
        {
            var driver = DriverWithRoute(90);
            driver.IsTravelling = true;
            driver.TravelDestinationLocationId = MillLocationId;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Travelling, verdict);
            Assert.IsNotNull(action);
            Assert.AreEqual(DirectedActionStatus.Travelling, action.Status);
            Assert.AreEqual(0, driver.TravelCalls, "joining a trip issues no second travel");
        }

        [Test]
        public void Request_Install_WhileTravellingUnknownDestination_Refused()
        {
            var driver = DriverWithRoute(90);
            driver.IsTravelling = true;
            driver.TravelDestinationLocationId = null;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action, "an unprovable trip is never assumed to end at the target");
            Assert.AreEqual(0, driver.TravelCalls);
        }

        // ---- Off-map deferral ----

        [Test]
        public void Request_Install_PlayerOffMap_DeferredNeverExecutes()
        {
            var driver = DriverWithRoute(90);
            driver.IsOffMap = true;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Deferred, verdict);
            Assert.IsNotNull(action);
            Assert.AreEqual(DirectedActionStatus.Deferred, action.Status);
            Assert.AreEqual(0, driver.RouteCalls);
            Assert.AreEqual(0, driver.TravelCalls);

            bool executorCalled = false;
            string executeResult = contract.Execute(action, a => { executorCalled = true; return null; });
            Assert.IsNotNull(executeResult);
            Assert.IsFalse(executorCalled, "a deferred action never executes remotely");
            Assert.AreEqual(DirectedActionStatus.Deferred, action.Status);
        }

        [Test]
        public void RetryDeferred_StillOffMap_StaysDeferred()
        {
            var driver = DriverWithRoute(90);
            driver.IsOffMap = true;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            DirectedActionVerdict retry = contract.RetryDeferred(action, out reason);
            Assert.AreEqual(DirectedActionVerdict.Deferred, retry);
            Assert.AreEqual(DirectedActionStatus.Deferred, action.Status);
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void RetryDeferred_PlayerReturned_ProceedsToTravel()
        {
            var driver = DriverWithRoute(90);
            driver.IsOffMap = true;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            driver.IsOffMap = false;
            DirectedActionVerdict retry = contract.RetryDeferred(action, out reason);
            Assert.AreEqual(DirectedActionVerdict.Travelling, retry);
            Assert.AreEqual(DirectedActionStatus.Travelling, action.Status);
            Assert.AreEqual(1, driver.TravelCalls);
        }

        // ---- Authorized agents ----

        [Test]
        public void Request_Install_AuthorizedAgent_ReadyViaAgent_NoTravel()
        {
            var driver = DriverWithRoute(90);
            var authorization = new FakeAgentAuthorization();
            authorization.Authorized[HouseholdId] = new HashSet<int> { AgentPersonId };
            var contract = ContractWithAgent(driver, authorization);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, AgentPersonId, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Ready, verdict);
            Assert.IsNotNull(action);
            Assert.AreEqual(DirectedActionStatus.Ready, action.Status);
            Assert.IsTrue(action.ViaAuthorizedAgent);
            Assert.AreEqual(0, driver.TravelCalls);
            Assert.AreEqual(HouseLocationId, driver.CurrentLocationId, "the player does not travel for an agent's action");
        }

        [Test]
        public void Request_Install_UnauthorizedAgent_RefusedNoRecord()
        {
            var driver = DriverWithRoute(90);
            var authorization = new FakeAgentAuthorization();
            authorization.Authorized[OtherHouseholdId] = new HashSet<int> { AgentPersonId };
            var contract = ContractWithAgent(driver, authorization);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, AgentPersonId, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action);
            Assert.IsTrue(reason.Contains("not authorized"));
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void Request_Install_AgentButNoAuthorizationSource_RefusedNoRecord()
        {
            var driver = DriverWithRoute(90);
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, AgentPersonId, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action);
            Assert.IsTrue(reason.Contains("no authorized-agent source is bound"));
        }

        // ---- Routing failures: never teleport, never assume ----

        [Test]
        public void Request_Install_NoRoute_RefusedNoTravel()
        {
            var driver = new FakePresenceDriver();
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action);
            Assert.IsTrue(reason.Contains("no route"));
            Assert.AreEqual(0, driver.TravelCalls);
        }

        [Test]
        public void Request_Install_ZeroMinuteRoute_RefusedNeverTeleports()
        {
            var driver = DriverWithRoute(0);
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Refused, verdict);
            Assert.IsNull(action);
            Assert.IsTrue(reason.Contains("non-positive"));
            Assert.AreEqual(0, driver.TravelCalls);
            Assert.AreEqual(HouseLocationId, driver.CurrentLocationId);
        }

        // ---- Cancel ----

        [Test]
        public void CancelPending_Travelling_ReleasesTravel()
        {
            var driver = DriverWithRoute(90);
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.IsTrue(driver.IsTravelling);
            string cancelReason;
            Assert.IsTrue(contract.CancelPending(action, out cancelReason), cancelReason);
            Assert.AreEqual(DirectedActionStatus.Cancelled, action.Status);
            Assert.IsFalse(driver.IsTravelling);
            Assert.AreEqual(1, driver.CancelTravelCalls);
            Assert.IsFalse(contract.Advance(action));
        }

        [Test]
        public void CancelPending_DoneAction_Refused()
        {
            var driver = DriverWithRoute();
            driver.CurrentLocationId = MillLocationId;
            var contract = Contract(driver);
            DirectedFurnishingAction action;
            string reason;
            contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);
            contract.Execute(action, a => null);

            Assert.AreEqual(DirectedActionStatus.Done, action.Status);
            string cancelReason;
            Assert.IsFalse(contract.CancelPending(action, out cancelReason));
            Assert.IsTrue(cancelReason.Contains("terminal"));
        }

        // ---- Real TaskAuthority backing ----

        [Test]
        public void CurrentTaskDefinitionId_ResolvesRealTaskAuthority_SleepInterruptsThroughAuthority()
        {
            var authority = new TaskAuthority();
            Pass2TaskCatalogs.RegisterAll(authority);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask sleep = authority.CreateTask(Pass2TaskCatalogs.SleepId, Player, 1);
            string reason;
            Assert.IsTrue(authority.AssignTask(sleep.TaskId, Player, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(sleep.TaskId, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, sleep.Status);

            var driver = DriverWithRoute(90);
            driver.Authority = authority;
            driver.Budgets = budgets;
            driver.DirectorCurrentTaskId = sleep.TaskId;
            Assert.AreEqual(Pass2TaskCatalogs.SleepId, driver.CurrentTaskDefinitionId);

            var contract = Contract(driver);
            DirectedFurnishingAction action;
            DirectedActionVerdict verdict = contract.RequestDirectedAction(
                FurnishingActionKind.Install, HouseholdId, BedAssetId, MillLocationId, 0, out action, out reason);

            Assert.AreEqual(DirectedActionVerdict.Travelling, verdict);
            Assert.AreEqual(1, driver.InterruptCalls);
            Assert.AreEqual(TaskStatus.Queued, sleep.Status,
                "interruption preserves the work — the sleep task is requeued, not cancelled");
            Assert.IsTrue(sleep.IsOpen);
        }

        [Test]
        public void FurnishingTaskDefinitions_RegisterAll_RegistersThreeDefinitions()
        {
            var authority = new TaskAuthority();
            FurnishingTaskDefinitions.RegisterAll(authority);

            TaskDefinition install = authority.GetDefinition(FurnishingTaskDefinitions.InstallId);
            TaskDefinition move = authority.GetDefinition(FurnishingTaskDefinitions.MoveId);
            TaskDefinition remove = authority.GetDefinition(FurnishingTaskDefinitions.RemoveId);

            Assert.IsNotNull(install);
            Assert.IsNotNull(move);
            Assert.IsNotNull(remove);
            Assert.AreEqual(FurnishingTaskDefinitions.InstallBaseMinutes, install.BaseMinutes);
            Assert.AreEqual(FurnishingTaskDefinitions.MoveBaseMinutes, move.BaseMinutes);
            Assert.AreEqual(FurnishingTaskDefinitions.RemoveBaseMinutes, remove.BaseMinutes);
            Assert.IsTrue(install.Interruptible);
            Assert.IsTrue(move.Interruptible);
            Assert.IsTrue(remove.Interruptible);

            string reason;
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask(FurnishingTaskDefinitions.InstallId, Player, 1);
            Assert.IsNotNull(task);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Player, budgets, out reason), reason);
        }
    }
}
