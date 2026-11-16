using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Plan 20, T3-T5 (Agent 3 note 67 ruling (1)): Unity-only tests
    /// for TryGetServiceClerkPersonId (GeneralStoreRuntimeManager.cs:434-468,
    /// the IsPaidActive clerk gate, Plan 20 item (b), and the TryParse-continue
    /// nit, item (c)).
    ///
    /// HARNESS-EXCLUDED, Unity only: GeneralStoreRuntimeManager.cs cannot
    /// compile in the Roslyn harness (21 CS0246 errors across 12 unshimmable
    /// scene-runtime types; reproduced by Agent 3, note 67). This file is
    /// listed with a "!" line in the shared sources.txt and must never be
    /// added to the harness. Run in Unity (Codex/Sol); until that run exists
    /// the (b)/(c) behavior stands as "source inspection only" (note 67
    /// ruling (3)).
    ///
    /// The manager is constructed via AddComponent + reflection, following
    /// GeneralStoreEconomyRepairTests.cs. No scene, store definition,
    /// population, or time manager is needed: the method under test reads
    /// only runtimeState.WorkerSlots. Specs preserved from the Plan 20
    /// checkpoint (Agent 1 session, T3-T5 bodies).</summary>
    public sealed class GeneralStoreRuntimeManagerTests
    {
        [Test]
        public void SuspendedClerk_IsSkipped()
        {
            GameObject root = new("GeneralStoreRuntimeManager_SuspendedClerk");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("3", paidActive: true, suspended: true),
                    ClerkSlot("4", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(4, personId);

                SetSlots(manager,
                    ClerkSlot("3", paidActive: true, suspended: true));
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UnpaidClerk_IsSkipped_UnderIsPaidActiveRule()
        {
            GameObject root = new("GeneralStoreRuntimeManager_UnpaidClerk");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("3", paidActive: false, suspended: false));
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UnparseableClerkId_IsSkipped_NextSlotReturned()
        {
            GameObject root = new("GeneralStoreRuntimeManager_UnparseableClerkId");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("not-a-number", paidActive: true, suspended: false),
                    ClerkSlot("4", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(4, personId);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OwnerClerkSlot_ResolvesToOwnerPersonId()
        {
            // CAMPAIGN-B (DEF-042): the NPC store's owner-occupied clerk
            // slot ("owner:1", written by TryAssignOwnerOperator) resolves
            // to the owner — before the fix int.TryParse failed and the
            // slot was skipped, so the store had no usable clerk.
            GameObject root = new("GeneralStoreRuntimeManager_OwnerClerk");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("owner:1", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(1, personId);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OwnerClerkSlotByDisplayName_IsSkipped()
        {
            // "owner:{name}" has no person id; it is skipped, not invented.
            GameObject root = new("GeneralStoreRuntimeManager_OwnerClerkByName");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("owner:Emma", paidActive: true, suspended: false),
                    ClerkSlot("4", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(4, personId);

                SetSlots(manager,
                    ClerkSlot("owner:Emma", paidActive: true, suspended: false));
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OwnerClerkSlot_ResolvesThroughStateOverload()
        {
            // CAMPAIGN-B (DEF-042): the state overload serves a store other
            // than the manager's own (the dev NPC store) from its own slots.
            GameObject root = new("GeneralStoreRuntimeManager_OwnerClerkOverload");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                BusinessRuntimeState npcState = new BusinessRuntimeState();
                FieldInfo slotsField = typeof(BusinessRuntimeState).GetField(
                    "workerSlots", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(slotsField, "BusinessRuntimeState.workerSlots field missing");
                slotsField.SetValue(npcState, new List<WorkerSlotState>(
                    new[] { ClerkSlot("owner:1", paidActive: true, suspended: false) }));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(npcState, out int personId));
                Assert.AreEqual(1, personId);
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(null, out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static GeneralStoreRuntimeManager NewManager(GameObject root)
        {
            GeneralStoreRuntimeManager manager = root.AddComponent<GeneralStoreRuntimeManager>();
            Assert.NotNull(manager);
            return manager;
        }

        private static void SetSlots(GeneralStoreRuntimeManager manager, params WorkerSlotState[] slots)
        {
            BusinessRuntimeState state = new BusinessRuntimeState();
            FieldInfo slotsField = typeof(BusinessRuntimeState).GetField(
                "workerSlots", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(slotsField, "BusinessRuntimeState.workerSlots field missing");
            slotsField.SetValue(state, new List<WorkerSlotState>(slots));
            FieldInfo stateField = typeof(GeneralStoreRuntimeManager).GetField(
                "runtimeState", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(stateField, "GeneralStoreRuntimeManager.runtimeState field missing");
            stateField.SetValue(manager, state);
        }

        private static WorkerSlotState ClerkSlot(string workerId, bool paidActive, bool suspended)
        {
            return WorkerSlotState.FromSaveDto(new WorkerSlotSaveDto
            {
                slotId = "clerk_helper",
                slotDisplayName = "clerk_helper",
                assignedWorkerId = workerId,
                assignedWorkerDisplayName = workerId,
                weeklyWageCents = 100,
                requiredForOpening = false,
                paidActive = paidActive,
                suspendedForMissedPayroll = suspended,
            });
        }
    }
}
