// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 9f (M-R10, expanded campaign 2026-11-20): the furnishing.install
// connection — a paid and delivered item stays in household custody until
// a legitimate furnishing.install task physically installs it at a valid
// authored placement; order, stage, custody and blocker survive a
// Save/Continue round-trip; no synchronous installation shortcut. The
// install itself runs through the real HouseholdFurnishingRegister and
// FurnitureInstallBinding on the harness building shim (whose placement
// now mirrors the production Configure / AssociateWithBuilding /
// TryGetBuildingLocalPlacement semantics — without that mirror no install
// could ever succeed in-harness).

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.GenericBusiness;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public class Lane9fFurnishingInstallTests
    {
        private const int HouseholdId = 7;
        private const int BuildingInstanceId = 12;
        private const int DayIndex = 42;
        private const string PurchaseId = "furn-store-7-furniture.bed-42";

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private BuildingFootprintAuthority building;
        private List<BuildingEquipmentPlacement> placements;
        private HouseholdFurnishingRegister register;
        private EntityId owner;
        private List<EntityId> members;
        private List<FurniturePurchaseOrder> orders;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            FurnishingTaskDefinitions.RegisterAll(tasks);
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(DayIndex);
            building = new BuildingFootprintAuthority();
            placements = new List<BuildingEquipmentPlacement>();
            register = new HouseholdFurnishingRegister { HouseholdId = HouseholdId };
            owner = EntityId.For(EntityKind.Household, HouseholdId);
            members = new List<EntityId> { EntityId.For(EntityKind.Person, 701) };
            orders = new List<FurniturePurchaseOrder>();
            diag = new List<string>();
        }

        private bool PlacementIsBound(string placementId)
        {
            if (string.IsNullOrWhiteSpace(placementId)) return false;
            if (register?.Bindings == null) return false;
            foreach (FurnishingPlacementBinding binding in register.Bindings)
            {
                if (binding != null && string.Equals(binding.BindingId, placementId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private BuildingEquipmentPlacement MakePlacement(string placementId, string definitionId)
        {
            var placement = new BuildingEquipmentPlacement();
            placement.AssociateWithBuilding(building);
            placement.Configure(placementId, definitionId, "Bed", 0);
            placements.Add(placement);
            return placement;
        }

        private EquipmentAsset MakeAsset(string assetId, string kind, string madeByBusinessId = "")
        {
            var asset = new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                DisplayName = kind,
                Condition01 = 1f,
                OwnerKind = "household",
                OwnerId = HouseholdId.ToString(),
                LocationId = "household:" + HouseholdId,
                MadeByBusinessId = madeByBusinessId ?? string.Empty,
            };
            Assert.IsNull(register.TryRegisterAsset(asset), "test asset registration should succeed");
            return asset;
        }

        private FurniturePurchaseOrder MakeOrder(string purchaseId, string assetId, EquipmentPurchaseStage stage)
        {
            var order = new FurniturePurchaseOrder
            {
                PurchaseId = purchaseId,
                Kind = FurniturePurchaseKind.New,
                HouseholdId = HouseholdId,
                BuildingInstanceId = BuildingInstanceId,
                DayIndex = DayIndex,
                Lines = new List<FurniturePurchaseLine>
                {
                    new FurniturePurchaseLine
                    {
                        ItemId = FurnitureItemSpecs.BedId,
                        Units = 1,
                        Stage = stage,
                        AssetIds = new List<string> { assetId },
                    },
                },
            };
            orders.Add(order);
            return order;
        }

        private FurnishingInstallTaskDriver.DriveDayResult Drive(bool arrived)
        {
            return FurnishingInstallTaskDriver.DriveDay(
                tasks, budgets, owner, members, HouseholdId, BuildingInstanceId,
                register, building, placements, PlacementIsBound, orders,
                (personId, buildingId) => arrived, DayIndex, diag);
        }

        private FurnishingInstallTaskDriver.DriveDayResult DriveWithRoute(DomesticActivityMovementResult movement)
        {
            return FurnishingInstallTaskDriver.DriveDay(
                tasks, budgets, owner, members, HouseholdId, BuildingInstanceId,
                register, building, placements, PlacementIsBound, orders,
                hasArrivedAtHome: null, dayIndex: DayIndex, diag: diag,
                placementContext: null,
                routeToInstallationTarget: (int personId, int buildingId, WorkTask task, out string reason) =>
                {
                    reason = movement == DomesticActivityMovementResult.Unsupported
                        ? "test physical route unavailable" : string.Empty;
                    return movement;
                });
        }

        private WorkTask LiveInstallTask(string assetId)
        {
            return FurnishingInstallTaskDriver.FindLiveInstallTask(tasks, owner, assetId);
        }

        // --- Minting ---

        [Test]
        public void PaidOrder_MintsInstallTask_AndKeepsAssetInCustody()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            FurnishingInstallTaskDriver.DriveDayResult result = Drive(arrived: false);

            Assert.AreEqual(1, result.MintedTasks, "one install task for the received asset");
            WorkTask task = LiveInstallTask("bed-1");
            Assert.IsNotNull(task, "the minted task is live under the household owner");
            Assert.AreEqual(FurnishingTaskDefinitions.InstallId, task.DefinitionId);
            Assert.AreEqual("furnishing-install:" + PurchaseId + ":bed-1:placement=plc-bed", task.CustomerRef);
            Assert.AreEqual(TaskStatus.Queued, task.Status, "minting never assigns work by itself — assignment waits for an arrived worker");
            Assert.IsNull(register.LiveBindingFor("bed-1"), "the asset stays in custody until its task completes");
            Assert.AreEqual(EquipmentPurchaseStage.Received, order.Lines[0].Stage, "the order stays Received until the install resolves");
            Assert.IsTrue(string.IsNullOrWhiteSpace(order.InstallationBlockedReason), "a free placement leaves no blocker");
        }

        [Test]
        public void InstallTask_DoesNotDoubleMint()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            FurnishingInstallTaskDriver.DriveDayResult first = Drive(arrived: false);
            FurnishingInstallTaskDriver.DriveDayResult second = Drive(arrived: false);

            Assert.AreEqual(1, first.MintedTasks);
            Assert.AreEqual(0, second.MintedTasks, "a live install task blocks a duplicate mint");
            Assert.AreEqual(1, tasks.GetOpenTasks(owner).Count, "exactly one open install task exists");
        }

        [Test]
        public void InstallTaskRef_RetainsLegacyAndColonBearingPurchaseIdentities()
        {
            string legacy = FurnishingInstallTaskDriver.BuildInstallTaskRef("purchase:legacy", "bed-1");
            Assert.IsTrue(FurnishingInstallTaskDriver.TryParseInstallTaskRef(
                legacy, out string oldPurchaseId, out string oldAssetId, out string oldPlacementId));
            Assert.AreEqual("purchase:legacy", oldPurchaseId);
            Assert.AreEqual("bed-1", oldAssetId);
            Assert.IsEmpty(oldPlacementId);

            string current = FurnishingInstallTaskDriver.BuildInstallTaskRef("purchase:current", "bed-2", "slot-2");
            Assert.IsTrue(FurnishingInstallTaskDriver.TryParseInstallTaskRef(
                current, out string purchaseId, out string assetId, out string placementId));
            Assert.AreEqual("purchase:current", purchaseId);
            Assert.AreEqual("bed-2", assetId);
            Assert.AreEqual("slot-2", placementId);
        }

        [Test]
        public void LegacyInstallTaskWithoutPlacement_IsReplacedBeforeAnyLabor()
        {
            EquipmentAsset asset = MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);
            Assert.IsNull(FurnishingInstallTaskDriver.TryMintInstallTask(
                tasks, owner, PurchaseId, asset, DayIndex, out WorkTask legacyTask));
            Assert.IsNotNull(legacyTask);

            FurnishingInstallTaskDriver.DriveDayResult migrated = Drive(arrived: false);
            Assert.AreEqual(1, migrated.CompletedTasks);
            Assert.AreEqual(TaskStatus.Failed, legacyTask.Status);
            Assert.AreEqual(0, legacyTask.MinutesWorked);
            Assert.IsNull(register.LiveBindingFor("bed-1"));
            Assert.AreEqual(EquipmentPurchaseStage.Received, order.Lines[0].Stage);

            FurnishingInstallTaskDriver.DriveDayResult resumed = Drive(arrived: false);
            WorkTask currentTask = LiveInstallTask("bed-1");
            Assert.AreEqual(1, resumed.MintedTasks);
            Assert.IsNotNull(currentTask);
            Assert.AreNotEqual(legacyTask.TaskId, currentTask.TaskId, "the compatibility migration closes the unbound legacy task and creates one slot-bound continuation");
            Assert.IsTrue(FurnishingInstallTaskDriver.TryParseInstallTaskRef(
                currentTask.CustomerRef, out _, out _, out string placementId));
            Assert.AreEqual("plc-bed", placementId);
        }

        [Test]
        public void MultipleCustodyAssets_DoNotReserveTheSameAuthoredPlacement()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            MakeAsset("bed-2", FurnitureItemSpecs.BedId);
            MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakeOrder(PurchaseId + "-second", "bed-2", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            FurnishingInstallTaskDriver.DriveDayResult result = Drive(arrived: false);

            List<WorkTask> live = tasks.GetOpenTasks(owner)
                .Where(task => FurnishingInstallTaskDriver.IsInstallTask(task)).ToList();
            Assert.AreEqual(1, result.MintedTasks, "one authored slot can back only one live installation obligation");
            Assert.AreEqual(1, live.Count);
            Assert.IsTrue(FurnishingInstallTaskDriver.TryParseInstallTaskRef(
                live[0].CustomerRef, out _, out _, out string reservedPlacement));
            Assert.AreEqual("plc-bed", reservedPlacement, "the chosen authored slot is durable on the task identity");
            Assert.IsNull(register.LiveBindingFor("bed-1"));
            Assert.IsNull(register.LiveBindingFor("bed-2"));
        }

        [Test]
        public void FailedOrCancelledInstallTask_ReleasesItsAuthoredPlacement()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            MakeAsset("bed-2", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder firstOrder = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakeOrder(PurchaseId + "-second", "bed-2", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            Drive(arrived: false);
            WorkTask firstTask = LiveInstallTask("bed-1");
            Assert.IsNotNull(firstTask);
            Assert.IsTrue(tasks.CompleteTask(firstTask.TaskId, TaskOutcome.RecoverableFailure, DayIndex, budgets));
            firstOrder.CancellationRequested = true;

            FurnishingInstallTaskDriver.DriveDayResult retry = Drive(arrived: false);

            Assert.AreEqual(1, retry.MintedTasks, "terminal work releases the slot for the next eligible obligation");
            Assert.IsNull(LiveInstallTask("bed-1"));
            Assert.IsNotNull(LiveInstallTask("bed-2"));
            Assert.IsTrue(FurnishingInstallTaskDriver.TryParseInstallTaskRef(
                LiveInstallTask("bed-2").CustomerRef, out _, out _, out string placementId));
            Assert.AreEqual("plc-bed", placementId);
        }

        [Test]
        public void InstallTask_NoFreePlacement_StaysInCustodyWithBlocker()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            // No placement at all: nothing is minted, nothing is guessed.

            FurnishingInstallTaskDriver.DriveDayResult result = Drive(arrived: true);

            Assert.AreEqual(0, result.MintedTasks);
            Assert.AreEqual(1, result.BlockedAssets);
            Assert.IsNull(LiveInstallTask("bed-1"), "no task is minted for an asset with nowhere to go");
            Assert.IsNull(register.LiveBindingFor("bed-1"), "the asset stays in custody");
            Assert.AreEqual(EquipmentPurchaseStage.Received, order.Lines[0].Stage);
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.InstallationBlockedReason), "the placement refusal is recorded as the blocker");
        }

        [Test]
        public void InstallTask_UnchangedPlacementBlockerIsReportedOnceAndRecoversWhenAuthoredSlotAppears()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);

            FurnishingInstallTaskDriver.DriveDayResult first = Drive(arrived: true);
            string blocker = order.InstallationBlockedReason;
            int firstIncidentCount = diag.Count(line => line != null && line.Contains("has no free authored placement"));
            FurnishingInstallTaskDriver.DriveDayResult repeated = Drive(arrived: true);

            Assert.AreEqual(1, first.BlockedAssets);
            Assert.AreEqual(1, repeated.BlockedAssets);
            Assert.IsFalse(string.IsNullOrWhiteSpace(blocker));
            Assert.AreEqual(firstIncidentCount, diag.Count(line => line != null && line.Contains("has no free authored placement")),
                "an unchanged durable order blocker must not generate another incident on each daily sweep");
            Assert.IsNull(LiveInstallTask("bed-1"));
            Assert.IsNull(register.LiveBindingFor("bed-1"), "the item remains in household custody while placement is unavailable");

            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);
            FurnishingInstallTaskDriver.DriveDayResult recovered = Drive(arrived: false);

            Assert.AreEqual(1, recovered.MintedTasks, "a genuinely changed authored layout permits a real install task");
            Assert.IsTrue(string.IsNullOrWhiteSpace(order.InstallationBlockedReason), "the stale placement blocker clears when placement becomes available");
            Assert.IsNull(register.LiveBindingFor("bed-1"), "minting still does not install the item");
        }

        // --- Working + resolving ---

        [Test]
        public void InstallTask_ResolvesOnlyAtValidAuthoredPlacement()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            FurnishingInstallTaskDriver.DriveDayResult result = Drive(arrived: true);

            Assert.AreEqual(1, result.InstalledAssets, "the arrived worker's completed task installs the asset");
            FurnishingPlacementBinding binding = register.LiveBindingFor("bed-1");
            Assert.IsNotNull(binding, "the asset is installed, not merely marked so");
            // TryInstall configures the placement with the binding id, so
            // the binding's PlacementId is the binding id, not the authored
            // "plc-bed" — the install proves itself by referencing the
            // authored placement component, which still carries the bed
            // definition in the home building.
            Assert.AreEqual(placements[0].PlacementId, binding.PlacementId, "the binding references the authored placement component, not a guessed spot");
            Assert.AreEqual(FurnitureItemSpecs.BedId, placements[0].DefinitionId);
            Assert.AreEqual(building, placements[0].BuildingAuthority);
            Assert.AreEqual("building:" + BuildingInstanceId, register.FindAsset("bed-1").LocationId);
            Assert.AreEqual(BuildingInstanceId, binding.BuildingInstanceId);
            Assert.IsTrue(order.IsOperational, "the order advances Received -> Installed -> Operational");
            Assert.AreEqual(EquipmentPurchaseStage.Operational, order.Lines[0].Stage);
            Assert.IsTrue(string.IsNullOrWhiteSpace(order.InstallationBlockedReason), "a completed install clears the blocker");
            Assert.IsNull(LiveInstallTask("bed-1"), "the completed task is no longer live");
        }

        [Test]
        public void InstallTask_WorkerNotArrived_WaitsInCustody()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            FurnishingInstallTaskDriver.DriveDayResult result = Drive(arrived: false);

            Assert.AreEqual(1, result.MintedTasks, "the task is minted — minting does not need arrival");
            Assert.AreEqual(0, result.InstalledAssets, "no minutes accrue from nowhere");
            Assert.AreEqual(0, result.CompletedTasks);
            Assert.AreEqual(TaskStatus.Queued, LiveInstallTask("bed-1").Status, "the task waits Queued for an arrived worker");
            Assert.IsNull(register.LiveBindingFor("bed-1"), "the asset stays in custody");
            Assert.AreEqual(EquipmentPurchaseStage.Received, order.Lines[0].Stage);
        }

        [Test]
        public void InstallTask_RouteTravelMustArriveAtReservedPlacementBeforeWork()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            FurnishingInstallTaskDriver.DriveDayResult travelling = DriveWithRoute(DomesticActivityMovementResult.Traveling);
            WorkTask task = LiveInstallTask("bed-1");
            Assert.AreEqual(1, travelling.MintedTasks);
            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.Assigned, task.Status, "the worker remains assigned while physically en route");
            Assert.AreEqual(0, task.MinutesWorked, "travel does not credit installation labor");
            Assert.IsNull(register.LiveBindingFor("bed-1"));
            Assert.AreEqual(EquipmentPurchaseStage.Received, order.Lines[0].Stage);

            FurnishingInstallTaskDriver.DriveDayResult arrived = DriveWithRoute(DomesticActivityMovementResult.Arrived);

            Assert.AreEqual(1, arrived.InstalledAssets);
            Assert.IsNotNull(register.LiveBindingFor("bed-1"));
            Assert.IsTrue(order.IsOperational);
        }

        [Test]
        public void InstallTask_UnsupportedRouteReleasesWorkerAndSameTaskRecovers()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);

            DriveWithRoute(DomesticActivityMovementResult.Unsupported);
            WorkTask task = LiveInstallTask("bed-1");
            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.Queued, task.Status, "an unsupported physical route releases the worker reservation");
            Assert.IsFalse(task.HasAssignee);
            Assert.AreEqual(0, task.MinutesWorked);
            Assert.IsNull(register.LiveBindingFor("bed-1"), "the item remains in custody while the route is unavailable");

            FurnishingInstallTaskDriver.DriveDayResult resumed = DriveWithRoute(DomesticActivityMovementResult.Arrived);

            Assert.AreSame(task, LiveInstallTask("bed-1"), "recovery retains the original install task identity");
            Assert.AreEqual(1, resumed.InstalledAssets);
            Assert.IsNotNull(register.LiveBindingFor("bed-1"));
        }

        [Test]
        public void InstallTask_SaveContinueRetainsAssignedWorkerAndReservedPlacementBeforeArrival()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);
            DriveWithRoute(DomesticActivityMovementResult.Traveling);
            WorkTask beforeSave = LiveInstallTask("bed-1");
            Assert.IsNotNull(beforeSave);
            Assert.AreEqual(TaskStatus.Assigned, beforeSave.Status);
            EntityId taskId = beforeSave.TaskId;
            EntityId workerId = beforeSave.AssigneeId;

            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(tasks.CaptureSaveDto());
            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.LoadFromSaveDto(budgets.CaptureSaveDto(), DayIndex);
            var restoredDiagnostics = new List<string>();
            FurnishingInstallTaskDriver.DriveDayResult continued = FurnishingInstallTaskDriver.DriveDay(
                restoredTasks, restoredBudgets, owner, members, HouseholdId, BuildingInstanceId,
                register, building, placements, PlacementIsBound, orders,
                hasArrivedAtHome: null, dayIndex: DayIndex, diag: restoredDiagnostics,
                routeToInstallationTarget: (int personId, int buildingId, WorkTask task, out string reason) =>
                {
                    reason = string.Empty;
                    Assert.AreEqual(taskId, task.TaskId);
                    Assert.AreEqual(workerId, task.AssigneeId);
                    return DomesticActivityMovementResult.Arrived;
                });

            WorkTask afterContinue = FurnishingInstallTaskDriver.FindLiveInstallTask(restoredTasks, owner, "bed-1");
            Assert.IsNull(afterContinue, "the continued task completed after the physical arrival callback");
            Assert.AreEqual(1, continued.InstalledAssets);
            Assert.IsTrue(order.IsOperational);
            Assert.IsNotNull(register.LiveBindingFor("bed-1"));
        }

        [Test]
        public void ResolveCompletedInstallTask_RefusesWhenPlacementTaken()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);
            Drive(arrived: false);
            WorkTask task = LiveInstallTask("bed-1");
            Assert.IsNotNull(task);
            // Complete the task by hand (assign + start + full work), then
            // take its placement with a second asset before resolving.
            Assert.IsTrue(tasks.AssignTask(task.TaskId, members[0], budgets, out string assignRefusal), assignRefusal);
            Assert.IsTrue(tasks.StartTask(task.TaskId, DayIndex, out string startRefusal), startRefusal);
            Assert.IsTrue(tasks.RecordWork(task.TaskId, task.MinutesRemaining, budgets, DayIndex, out string workRefusal), workRefusal);
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            EquipmentAsset other = MakeAsset("bed-2", FurnitureItemSpecs.BedId);
            Assert.IsNull(FurnitureInstallBinding.TryInstallHouseholdAsset(register, other, building,
                BuildingInstanceId, placements, PlacementIsBound,
                FurnishingAcquisitionSource.Purchase, PurchaseId, out _), "the second asset takes the only placement");

            string refusal = FurnishingInstallTaskDriver.ResolveCompletedInstallTask(
                task, order, register, building, BuildingInstanceId, placements, PlacementIsBound, diag);

            Assert.IsNotNull(refusal, "the completed task refuses when its placement is gone — no teleport, no second install on a taken spot");
            Assert.IsNull(register.LiveBindingFor("bed-1"), "the asset stays in custody");
            Assert.AreEqual(EquipmentPurchaseStage.Received, order.Lines[0].Stage, "the order does not advance on a refused install");
            Assert.IsFalse(string.IsNullOrWhiteSpace(order.InstallationBlockedReason), "the refusal is recorded as the blocker");
        }

        // --- Custody leg (workshop-made pieces, no order) ---

        [Test]
        public void WorkshopCustodyPiece_MintsAndInstalls_WithManufactureSource()
        {
            MakeAsset("chair-1", FurnitureItemSpecs.ChairId, madeByBusinessId: "carpentry-workshop");
            MakePlacement("plc-chair", FurnitureItemSpecs.ChairId);

            FurnishingInstallTaskDriver.DriveDayResult result = Drive(arrived: true);

            Assert.AreEqual(1, result.MintedTasks);
            Assert.AreEqual(1, result.InstalledAssets);
            FurnishingPlacementBinding binding = register.LiveBindingFor("chair-1");
            Assert.IsNotNull(binding);
            Assert.AreEqual(FurnishingAcquisitionSource.Manufacture, binding.AcquisitionSource, "a workshop piece installs with source Manufacture, not Purchase");
            Assert.AreEqual("carpentry-workshop", binding.AcquisitionSourceId);
        }

        // --- Failure + Save/Continue ---

        [Test]
        public void FailedTask_LeavesGoodsInCustody_AndRetriesNextDay()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);
            Drive(arrived: false);
            WorkTask task = LiveInstallTask("bed-1");
            Assert.IsNotNull(task);
            Assert.IsTrue(tasks.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, DayIndex, budgets), "failing the task is the honest terminal state for a failed install attempt");

            Assert.IsNull(register.LiveBindingFor("bed-1"), "a failed task leaves the goods in custody — nothing is installed, nothing is lost");
            Assert.AreEqual(EquipmentPurchaseStage.Received, order.Lines[0].Stage, "the order stays Received after a failed install");
            Assert.IsNull(LiveInstallTask("bed-1"), "the failed task is no longer live");

            FurnishingInstallTaskDriver.DriveDayResult retry = Drive(arrived: true);
            Assert.AreEqual(1, retry.MintedTasks, "the failed install is retryable — a fresh task mints the next day");
            Assert.AreEqual(1, retry.InstalledAssets, "the retry installs");
            Assert.IsTrue(order.IsOperational);
        }

        [Test]
        public void SaveLoad_RoundTrip_TaskSurvivesContinueAndInstalls()
        {
            MakeAsset("bed-1", FurnitureItemSpecs.BedId);
            FurniturePurchaseOrder order = MakeOrder(PurchaseId, "bed-1", EquipmentPurchaseStage.Received);
            MakePlacement("plc-bed", FurnitureItemSpecs.BedId);
            Drive(arrived: false);
            Assert.IsNotNull(LiveInstallTask("bed-1"), "a task is live before the save");

            // Continue: the task authority restores from its save DTO
            // (definitions + tasks, copy-on-load); the order is rebuilt
            // from its Pass 9 DTO with the same identity, stage, assets
            // and blocker — the resolver must re-find it by PurchaseId
            // from the restored task's CustomerRef, not by object identity.
            var savedTasks = tasks.CaptureSaveDto();
            var restoredTasks = new TaskAuthority();
            restoredTasks.LoadFromSaveDto(savedTasks);
            var restoredOrder = new FurniturePurchaseOrder
            {
                PurchaseId = PurchaseId,
                Kind = FurniturePurchaseKind.New,
                HouseholdId = HouseholdId,
                BuildingInstanceId = BuildingInstanceId,
                DayIndex = DayIndex,
                InstallationBlockedReason = order.InstallationBlockedReason,
                Lines = new List<FurniturePurchaseLine>
                {
                    new FurniturePurchaseLine
                    {
                        ItemId = FurnitureItemSpecs.BedId,
                        Units = 1,
                        Stage = EquipmentPurchaseStage.Received,
                        AssetIds = new List<string> { "bed-1" },
                    },
                },
            };
            var restoredOrders = new List<FurniturePurchaseOrder> { restoredOrder };

            WorkTask restoredTask = FurnishingInstallTaskDriver.FindLiveInstallTask(
                restoredTasks, owner, "bed-1");
            Assert.IsNotNull(restoredTask, "the install task survives the round-trip");
            Assert.AreEqual("furnishing-install:" + PurchaseId + ":bed-1:placement=plc-bed", restoredTask.CustomerRef,
                "the ref still names the order, asset, and reserved authored placement");
            Assert.IsTrue(FurnishingInstallTaskDriver.TryParseInstallTaskRef(
                restoredTask.CustomerRef, out _, out _, out string restoredPlacement));
            Assert.AreEqual("plc-bed", restoredPlacement, "the placement lock survives the TaskAuthority round-trip");
            Assert.AreEqual(TaskStatus.Queued, restoredTask.Status);

            FurnishingInstallTaskDriver.DriveDayResult result = FurnishingInstallTaskDriver.DriveDay(
                restoredTasks, budgets, owner, members, HouseholdId, BuildingInstanceId,
                register, building, placements, PlacementIsBound, restoredOrders,
                (personId, buildingId) => true, DayIndex, diag);

            Assert.AreEqual(0, result.MintedTasks, "the restored task is found, not duplicated");
            Assert.AreEqual(1, result.InstalledAssets, "the restored task installs after Continue");
            Assert.IsNotNull(register.LiveBindingFor("bed-1"));
            Assert.IsTrue(restoredOrder.IsOperational, "the restored order advances to Operational");
            Assert.IsTrue(string.IsNullOrWhiteSpace(restoredOrder.InstallationBlockedReason));
        }
    }
}
