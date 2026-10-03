using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQP-4: Groups C+D (ranch handling, livery rentals, farm implements, store
    /// and boarding fixtures) plus the valuation wiring — the equipment register
    /// feeding the BIZ-5 transferable floor condition-weighted (Canon §11.6) and
    /// the replacement-need adjustment (Canon §11.5).
    /// </summary>
    [TestFixture]
    public sealed class EquipmentValuationTests
    {
        private static int AssetCost(string kind) => kind == "moldboard-plow" ? 10000 : 1000;
        private static int KitCost(string kitId) => kitId == "carpenter-hand-tool-kit" ? 5000 : 500;

        private static BusinessEquipmentRegister RegisterWithPlow(float condition)
        {
            var register = new BusinessEquipmentRegister { BusinessInstanceId = "biz-1" };
            register.RegisterAsset(new EquipmentAsset
            {
                AssetId = "plow-1", Kind = "moldboard-plow", Condition01 = condition,
                OwnerKind = "business", OwnerId = "biz-1",
            });
            return register;
        }

        [Test]
        public void Register_TransferableValue_IsConditionWeighted()
        {
            Assert.AreEqual(10000, RegisterWithPlow(1.0f).TransferableValueCents(AssetCost, KitCost));
            Assert.AreEqual(3000, RegisterWithPlow(0.3f).TransferableValueCents(AssetCost, KitCost),
                "Worn equipment contributes proportionally less (Canon §11.6).");
            Assert.AreEqual(0, RegisterWithPlow(0.02f).TransferableValueCents(AssetCost, KitCost),
                "A wrecked plow is not a floor contributor (Canon §11.6).");
        }

        [Test]
        public void Register_ReplacementNeed_OnlyForWornEquipment()
        {
            Assert.AreEqual(0, RegisterWithPlow(1.0f).ReplacementNeedCents(AssetCost, KitCost),
                "New equipment needs no replacement capital.");
            Assert.AreEqual(7000, RegisterWithPlow(0.3f).ReplacementNeedCents(AssetCost, KitCost),
                "Worn equipment creates a replacement need (Canon §11.5).");
            Assert.AreEqual(0, RegisterWithPlow(0.6f).ReplacementNeedCents(AssetCost, KitCost),
                "Above half condition: no planned replacement (calibration).");
        }

        [Test]
        public void SyncToReadModel_FloorAndNeed_ReachValuation()
        {
            var readModel = new EnterpriseValuationReadModel();
            readModel.RegisterBusiness("biz-1", "player", true);
            var diagnostics = new List<string>();

            // New plow: floor 10000c, no replacement need, no profits → gross = floor.
            EquipmentValuationSync.SyncToReadModel(
                RegisterWithPlow(1.0f), readModel, AssetCost, KitCost, diagnostics);
            EnterpriseValuationResult result = readModel.GetValuation("biz-1");
            Assert.AreEqual(10000, result.GrossGoingConcernValueCents);
            Assert.AreEqual(10000, result.OwnerEquityValueCents);

            // Worn plow: floor 3000c, need 7000c → equity honestly reduced.
            EquipmentValuationSync.SyncToReadModel(
                RegisterWithPlow(0.3f), readModel, AssetCost, KitCost, diagnostics);
            result = readModel.GetValuation("biz-1");
            Assert.AreEqual(3000, result.GrossGoingConcernValueCents);
            Assert.AreEqual(-4000, result.OwnerEquityValueCents,
                "Canon §11.5: worn-out productive assets drag owner equity down.");
        }

        [Test]
        public void SyncToReadModel_NeverAddsAssetsOnTopOfEarnings()
        {
            var readModel = new EnterpriseValuationReadModel();
            readModel.RegisterBusiness("biz-2", "player", true);
            // 12w × 1000c profit, 10h owner @ 25c → earnings value 117000c (BIZ-5 baseline).
            for (int i = 0; i < 12; i++) readModel.RecordWeeklyProfit("biz-2", 1000);
            readModel.RecordOwnerLabor("biz-2", 10f, 25, 0);

            var diagnostics = new List<string>();
            EquipmentValuationSync.SyncToReadModel(
                RegisterWithPlow(1.0f), readModel, AssetCost, KitCost, diagnostics);
            EnterpriseValuationResult result = readModel.GetValuation("biz-2");
            Assert.AreEqual(117000, result.GrossGoingConcernValueCents,
                "Tech X §9.3: the floor never stacks on top of earnings — max(), not sum.");
            Assert.AreEqual(117000, result.OwnerEquityValueCents);
        }

        private static WorkstationComponentView? FindIn(Dictionary<string, EquipmentAsset> byId, string assetId)
        {
            if (byId.TryGetValue(assetId, out var asset) && asset != null)
                return new WorkstationComponentView
                {
                    AssetId = asset.AssetId, Kind = asset.Kind,
                    Condition01 = asset.Condition01, IsUsable = asset.IsUsable,
                };
            return null;
        }

        [Test]
        public void HandlingPens_CattleHandling_HardRequirement()
        {
            var def = WorkstationCatalog.CattleHandlingPens;
            Assert.IsTrue(def.CapabilitiesGranted.Contains("handle-cattle"));
            var station = new WorkstationInstance
            {
                InstanceId = "pens-1", WorkstationId = "cattle-handling-pens",
                BusinessInstanceId = "ranch-1", SpaceId = "ranch-yard",
            };
            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["corral-1"] = new EquipmentAsset { AssetId = "corral-1", Kind = "corral-fencing", Condition01 = 0.8f },
            };
            station.InstallComponent("corral-1");
            string reason = station.EvaluateReady(def, id => FindIn(parts, id), new List<string>());
            Assert.IsNotNull(reason, "Cattle cannot be worked safely without chutes and gates (Canon Part V: Ranch).");
            StringAssert.Contains("handling-chute", reason);
        }

        [Test]
        public void StoreCounter_WeighingGate_HardRequirement()
        {
            var def = WorkstationCatalog.StoreCounter;
            Assert.IsTrue(def.CapabilitiesGranted.Contains("sell-by-weight"));
            var station = new WorkstationInstance
            {
                InstanceId = "counter-1", WorkstationId = "store-counter",
                BusinessInstanceId = "store-1", SpaceId = "general-store",
            };
            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["counter-1"] = new EquipmentAsset { AssetId = "counter-1", Kind = "counter", Condition01 = 0.9f },
                ["drawer-1"] = new EquipmentAsset { AssetId = "drawer-1", Kind = "cash-drawer", Condition01 = 0.9f },
            };
            station.InstallComponent("counter-1");
            station.InstallComponent("drawer-1");
            string reason = station.EvaluateReady(def, id => FindIn(parts, id), new List<string>());
            Assert.IsNotNull(reason, "A store that can't weigh can't sell by weight (Canon 4.2).");
            StringAssert.Contains("scale-set", reason);
        }

        [Test]
        public void RentalFleet_RentOut_NoDoubleBooking()
        {
            var fleet = new RentalFleet { BusinessInstanceId = "livery-1" };
            fleet.AddToFleet("buggy-1");
            var reservations = new EquipmentReservationService();
            var diagnostics = new List<string>();

            Assert.IsNull(fleet.RentOut("buggy-1", "person", "p1", "Traveler", 30, 500,
                "day trip", reservations, null, diagnostics));
            Assert.IsTrue(fleet.IsRentedOut("buggy-1"));
            Assert.IsTrue(reservations.IsReserved(EquipmentReservationService.AssetKey("buggy-1")));
            Assert.IsNotNull(fleet.RentOut("buggy-1", "person", "p2", "Other", 31, 500,
                "day trip", reservations, null, diagnostics),
                "A rented rig cannot be double-booked (Tech X §3.8).");
            Assert.IsNull(fleet.Return("buggy-1", 0.95f, diagnostics));
            Assert.IsFalse(fleet.IsRentedOut("buggy-1"));
        }

        [Test]
        public void FarmImplements_TaskGates_AreData()
        {
            Assert.AreEqual("asset:threshing-separator", FarmImplements.RequirementForTask("thresh-grain"));
            Assert.AreEqual("asset:horse-mower", FarmImplements.RequirementForTask("cut-hay"));
            Assert.IsNull(FarmImplements.RequirementForTask("bake-bread"),
                "Implements gate their own tasks only — never occupation gates (Tech X §3.10).");
        }
    }
}
