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
            Assert.AreEqual("furnishing-install:" + PurchaseId + ":bed-1", task.CustomerRef);
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
            Assert.AreEqual("furnishing-install:" + PurchaseId + ":bed-1", restoredTask.CustomerRef, "the ref still names the order and asset");
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
