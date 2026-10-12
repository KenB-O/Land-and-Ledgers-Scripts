using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    /// <summary>
    /// Pass IV (Worker 1): the task lifecycle transition table — every valid
    /// transition is allowed, everything else is refused. Append-only states;
    /// terminal states have no outbound transitions.
    /// </summary>
    [TestFixture]
    public sealed class TaskLifecyclePolicyTests
    {
        // All valid (from, to) pairs from TaskLifecyclePolicy.CanTransition.
        private static readonly (TaskStatus from, TaskStatus to)[] ValidTransitions =
        {
            (TaskStatus.Queued, TaskStatus.Assigned),
            (TaskStatus.Queued, TaskStatus.Cancelled),
            (TaskStatus.Assigned, TaskStatus.ResourcesReserved),
            (TaskStatus.Assigned, TaskStatus.InProgress),
            (TaskStatus.Assigned, TaskStatus.Queued),
            (TaskStatus.Assigned, TaskStatus.Cancelled),
            (TaskStatus.ResourcesReserved, TaskStatus.InProgress),
            (TaskStatus.ResourcesReserved, TaskStatus.Assigned),
            (TaskStatus.ResourcesReserved, TaskStatus.Cancelled),
            (TaskStatus.InProgress, TaskStatus.Paused),
            (TaskStatus.InProgress, TaskStatus.WaitingOnPassiveProcess),
            (TaskStatus.InProgress, TaskStatus.WaitingOnDependency),
            (TaskStatus.InProgress, TaskStatus.Complete),
            (TaskStatus.InProgress, TaskStatus.Failed),
            (TaskStatus.InProgress, TaskStatus.Interrupted),
            (TaskStatus.InProgress, TaskStatus.Cancelled),
            (TaskStatus.Paused, TaskStatus.InProgress),
            (TaskStatus.Paused, TaskStatus.Interrupted),
            (TaskStatus.Paused, TaskStatus.Cancelled),
            (TaskStatus.WaitingOnPassiveProcess, TaskStatus.InProgress),
            (TaskStatus.WaitingOnPassiveProcess, TaskStatus.Failed),
            (TaskStatus.WaitingOnPassiveProcess, TaskStatus.Cancelled),
            (TaskStatus.WaitingOnDependency, TaskStatus.InProgress),
            (TaskStatus.WaitingOnDependency, TaskStatus.Queued),
            (TaskStatus.WaitingOnDependency, TaskStatus.Cancelled),
            (TaskStatus.Interrupted, TaskStatus.Queued),
            (TaskStatus.Failed, TaskStatus.ReworkRequired),
        };

        [Test]
        public void CanTransition_AllValidPairs_Allowed()
        {
            foreach (var pair in ValidTransitions)
            {
                Assert.IsTrue(TaskLifecyclePolicy.CanTransition(pair.from, pair.to),
                    $"expected {pair.from} -> {pair.to} to be allowed");
            }
        }

        [Test]
        public void CanTransition_EverythingElse_Refused()
        {
            var valid = new System.Collections.Generic.HashSet<(TaskStatus, TaskStatus)>(ValidTransitions);
            var allStatuses = (TaskStatus[])System.Enum.GetValues(typeof(TaskStatus));
            int refused = 0;
            foreach (TaskStatus from in allStatuses)
            {
                foreach (TaskStatus to in allStatuses)
                {
                    if (from == to)
                    {
                        // Self-transitions are never listed in the table.
                        Assert.IsFalse(TaskLifecyclePolicy.CanTransition(from, to),
                            $"self-transition {from} -> {to} must be refused");
                        refused++;
                        continue;
                    }

                    if (valid.Contains((from, to)))
                    {
                        continue;
                    }

                    Assert.IsFalse(TaskLifecyclePolicy.CanTransition(from, to),
                        $"expected {from} -> {to} to be refused");
                    refused++;
                }
            }

            Assert.Greater(refused, 100, "the table must refuse far more than it allows");
        }

        [Test]
        public void CanTransition_TerminalStates_HaveNoOutboundTransitions()
        {
            var terminals = new[]
            {
                TaskStatus.Complete, TaskStatus.Failed, TaskStatus.Cancelled, TaskStatus.ReworkRequired,
            };
            var allStatuses = (TaskStatus[])System.Enum.GetValues(typeof(TaskStatus));
            foreach (TaskStatus terminal in terminals)
            {
                foreach (TaskStatus to in allStatuses)
                {
                    if (terminal == TaskStatus.Failed && to == TaskStatus.ReworkRequired)
                    {
                        continue; // the one sanctioned outbound edge
                    }

                    Assert.IsFalse(TaskLifecyclePolicy.CanTransition(terminal, to),
                        $"terminal {terminal} -> {to} must be refused");
                }
            }
        }

        [Test]
        public void HoldsWorkerCommitment_PassiveProcess_ReleasesWorker()
        {
            Assert.IsFalse(TaskLifecyclePolicy.HoldsWorkerCommitment(TaskStatus.WaitingOnPassiveProcess));
            Assert.IsTrue(TaskLifecyclePolicy.HoldsWorkerCommitment(TaskStatus.InProgress));
            Assert.IsTrue(TaskLifecyclePolicy.HoldsWorkerCommitment(TaskStatus.Paused));
            Assert.IsTrue(TaskLifecyclePolicy.HoldsWorkerCommitment(TaskStatus.WaitingOnDependency));
            Assert.IsTrue(TaskLifecyclePolicy.HoldsWorkerCommitment(TaskStatus.ResourcesReserved));
        }

        [Test]
        public void HoldsResourceReservations_PassiveProcess_HoldsResources()
        {
            Assert.IsTrue(TaskLifecyclePolicy.HoldsResourceReservations(TaskStatus.WaitingOnPassiveProcess));
            Assert.IsTrue(TaskLifecyclePolicy.HoldsResourceReservations(TaskStatus.Paused));
            Assert.IsFalse(TaskLifecyclePolicy.HoldsResourceReservations(TaskStatus.Complete));
            Assert.IsFalse(TaskLifecyclePolicy.HoldsResourceReservations(TaskStatus.Queued));
        }
    }
}
