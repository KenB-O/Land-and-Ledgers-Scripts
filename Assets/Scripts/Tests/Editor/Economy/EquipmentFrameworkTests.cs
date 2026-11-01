using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQP-1: the five cross-cutting equipment framework pieces — ToolKit,
    /// WorkstationCapability derivation, access/custody, supportability, and the
    /// unified reservation authority. Canon 4.1 (CANON LOCK): capability emerges
    /// from actual resources, never from a business label.
    /// </summary>
    [TestFixture]
    public sealed class EquipmentFrameworkTests
    {
        private static ToolKitDefinition CarpenterKit() =>
            new ToolKitDefinition("carpenter-hand-tool-kit", "Carpenter Hand Tool Kit", "builder",
                "Canon Part V: Builder",
                "rip saw", "crosscut saw", "hammer", "mallet", "plane", "chisels",
                "brace and bits", "square", "level", "sharpening stone");

        [Test]
        public void ToolKit_HasCanonicalContents_NotAGenericFlag()
        {
            var def = CarpenterKit();
            Assert.IsTrue(def.CanonicalContents.Contains("sharpening stone"),
                "Tech X §3.4: kits carry canonical contents, never a generic Tools=true flag.");
            Assert.AreEqual(10, def.CanonicalContents.Count);
        }

        [Test]
        public void ToolKit_WearAndRecondition_LifecycleHonest()
        {
            var kit = new ToolKitInstance { InstanceId = "kit-1", KitId = "carpenter-hand-tool-kit" };
            Assert.IsTrue(kit.IsUsable);
            kit.ApplyWear(0.97f);
            Assert.IsFalse(kit.IsUsable, "Wrecked kit is not deleted — it awaits reconditioning (Tech X §3.9).");
            kit.Recondition(0.9f, 41, "sharpened saws, replaced hammer handle");
            Assert.IsTrue(kit.IsUsable);
            Assert.AreEqual(1, kit.MaintenanceLog.Count);
            StringAssert.Contains("Day 41", kit.MaintenanceLog[0]);
        }

        [Test]
        public void ToolKit_NoDoubleBooking()
        {
            var kit = new ToolKitInstance { InstanceId = "kit-2", KitId = "doctor-bag" };
            Assert.IsNull(kit.Reserve("task-a", "house call"));
            string refusal = kit.Reserve("task-b", "surgery");
            Assert.IsNotNull(refusal, "Tech X §3.8: no double-booking.");
        }

        private static WorkstationDefinition ForgeStationDef()
        {
            var def = new WorkstationDefinition
            {
                WorkstationId = "forge-station",
                DisplayName = "Forge Station",
                RequiredSpaceKind = "smithy",
                SourceNote = "Tech X §3.5",
            };
            def.Components.Add(new WorkstationComponentRequirement("forge", 1));
            def.Components.Add(new WorkstationComponentRequirement("anvil", 1));
            def.Components.Add(new WorkstationComponentRequirement("smith-tools", 1));
            def.CapabilitiesGranted.Add("smith-equipment");
            return def;
        }

        private static WorkstationComponentView? FindIn(Dictionary<string, WorkstationComponentView> byId, string assetId)
        {
            if (byId.TryGetValue(assetId, out var view)) return view;
            return null;
        }

        [Test]
        public void Workstation_ReadyOnlyFromComponents_NeverFromSpaceAlone()
        {
            var def = ForgeStationDef();
            var station = new WorkstationInstance
            {
                InstanceId = "ws-1", WorkstationId = "forge-station",
                BusinessInstanceId = "smith-1", SpaceId = "smithy-main",
            };
            station.InstallComponent("forge-1");
            station.InstallComponent("anvil-1");
            // smith-tools missing:
            var parts = new Dictionary<string, WorkstationComponentView>
            {
                ["forge-1"] = new WorkstationComponentView { AssetId = "forge-1", Kind = "forge", Condition01 = 0.8f, IsUsable = true },
                ["anvil-1"] = new WorkstationComponentView { AssetId = "anvil-1", Kind = "anvil", Condition01 = 0.9f, IsUsable = true },
            };
            var diagnostics = new List<string>();
            string reason = station.EvaluateReady(def, id => FindIn(parts, id), diagnostics);
            Assert.IsNotNull(reason, "Missing smith-tools must refuse readiness.");
            StringAssert.Contains("smith-tools", reason);

            parts["tools-1"] = new WorkstationComponentView { AssetId = "tools-1", Kind = "smith-tools", Condition01 = 0.7f, IsUsable = true };
            station.InstallComponent("tools-1");
            Assert.IsNull(station.EvaluateReady(def, id => FindIn(parts, id), new List<string>()),
                "All components present and usable — the station is ready (Tech X §3.5).");
        }

        [Test]
        public void Workstation_WreckedComponent_BlocksReadiness()
        {
            var def = ForgeStationDef();
            var station = new WorkstationInstance
            {
                InstanceId = "ws-2", WorkstationId = "forge-station", SpaceId = "smithy-main",
            };
            station.InstallComponent("anvil-1");
            var parts = new Dictionary<string, WorkstationComponentView>
            {
                ["forge-1"] = new WorkstationComponentView { AssetId = "forge-1", Kind = "forge", Condition01 = 0.8f, IsUsable = true },
                ["anvil-1"] = new WorkstationComponentView { AssetId = "anvil-1", Kind = "anvil", Condition01 = 0.02f, IsUsable = false },
            };
            station.InstallComponent("forge-1");
            string reason = station.EvaluateReady(def, id => FindIn(parts, id), new List<string>());
            Assert.IsNotNull(reason, "A wrecked anvil blocks the station (Tech X §3.9) — it awaits repair, not deletion.");
        }

        [Test]
        public void Custody_BorrowedPlow_SatisfiesRequirement()
        {
            var resolver = new EquipmentAccessResolver();
            resolver.AddGrant(new EquipmentAccessGrant
            {
                EquipmentKind = "moldboard-plow", HolderKind = "business", HolderId = "farm-1",
                Custody = EquipmentCustodyKind.Borrowed, GranterName = "neighbor farm", ExpiryDayIndex = 90,
            });
            bool ok = resolver.HasAccess("business", "farm-1", "moldboard-plow",
                (k, id) => new List<string>(), 40, out string custody);
            Assert.IsTrue(ok, "Tech X §3.7: legitimate access counts, not only ownership.");
            StringAssert.Contains("borrowed", custody);
        }

        [Test]
        public void Custody_ExpiredGrant_SatisfiesNothing()
        {
            var resolver = new EquipmentAccessResolver();
            resolver.AddGrant(new EquipmentAccessGrant
            {
                EquipmentKind = "reaper", HolderKind = "business", HolderId = "farm-1",
                Custody = EquipmentCustodyKind.Rented, GranterName = "implement dealer", ExpiryDayIndex = 30,
            });
            bool ok = resolver.HasAccess("business", "farm-1", "reaper",
                (k, id) => new List<string>(), 45, out _);
            Assert.IsFalse(ok, "Expired grants satisfy nothing — access is never invented.");
        }

        [Test]
        public void Custody_ThreshingOutfit_AsServiceProvider()
        {
            var resolver = new EquipmentAccessResolver();
            resolver.AddGrant(new EquipmentAccessGrant
            {
                EquipmentKind = "threshing-separator", HolderKind = "business", HolderId = "farm-9",
                Custody = EquipmentCustodyKind.ServiceProvider, GranterName = "traveling threshing outfit",
                TermsNote = "$2/acre, week of Sep 10",
            });
            bool ok = resolver.HasAccess("business", "farm-9", "threshing-separator",
                (k, id) => new List<string>(), 250, out string custody);
            Assert.IsTrue(ok, "Canon 6.3: major machinery can be commercially useful without every farm owning it.");
            StringAssert.Contains("serviceprovider", custody);
        }

        [Test]
        public void Supportability_NoFuel_OvenNotOperational()
        {
            var context = new SupportContext();
            context.AvailableOperatorSkills.Add("baking");
            var requirements = new List<SupportRequirement>
            {
                new SupportRequirement("fuel", "oven-wood", 2),
                new SupportRequirement("operator-skill", "baking"),
            };
            var diagnostics = new List<string>();
            bool operational = Supportability.IsOperational(true, requirements, context, diagnostics);
            Assert.IsFalse(operational, "Canon 5.2: a fully-repaired oven with no fuel is not usable.");
            Assert.IsTrue(diagnostics.Count > 0);
        }

        [Test]
        public void Supportability_FuelAndOperator_Operational()
        {
            var context = new SupportContext();
            context.ConsumableUnits[SupportContext.Key("fuel", "oven-wood")] = 10;
            context.AvailableOperatorSkills.Add("baking");
            var requirements = new List<SupportRequirement>
            {
                new SupportRequirement("fuel", "oven-wood", 2),
                new SupportRequirement("operator-skill", "baking"),
            };
            Assert.IsTrue(Supportability.IsOperational(true, requirements, context, new List<string>()));
        }

        [Test]
        public void ReservationService_AtomicSet_RefusesPartial()
        {
            var service = new EquipmentReservationService();
            string plow = EquipmentReservationService.AssetKey("plow-1");
            string station = EquipmentReservationService.WorkstationKey("ws-1");
            Assert.IsNull(service.Reserve(plow, "task-a", "plowing"));
            var diagnostics = new List<string>();
            string refusal = service.ReserveAll(new List<string> { plow, station }, "task-b", "plowing", diagnostics);
            Assert.IsNotNull(refusal, "Atomic set: the plow is taken, so the workstation must not be held alone (Tech X §3.8).");
            Assert.IsFalse(service.IsReserved(station));
            service.Release(plow);
            Assert.IsFalse(service.IsReserved(plow));
        }

        [Test]
        public void MotivePower_SteamRequired_NeedsRecordedSource()
        {
            var diagnostics = new List<string>();
            var sources = new List<MotivePowerSource>
            {
                new MotivePowerSource(MotivePowerKind.Steam, "6hp steam engine", available: false),
            };
            Assert.IsFalse(MotivePower.IsAvailable("Steam", sources, diagnostics),
                "Tech X §3.6: an unavailable source satisfies nothing.");
            sources[0].Available = true;
            Assert.IsTrue(MotivePower.IsAvailable("Steam", sources, new List<string>()));
            Assert.IsTrue(MotivePower.IsAvailable("Human", null, new List<string>()),
                "Human power is the capable person themselves.");
        }
    }
}
