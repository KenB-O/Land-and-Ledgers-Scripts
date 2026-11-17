using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Wheelwright;
using LandLedgers.Primitives;
using EntityId = LandLedgers.Primitives.EntityId;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// UBC workstream J (manufacturing / economic network audit, 2026-11-17).
    /// Boundary pins for the audit's central finding: wagon wear is real in
    /// production (every haul damages the wagon) but the repair side of the
    /// loop is entirely undriven, and the repairer businesses do not exist
    /// in the development town. These tests pin the CURRENT boundary so any
    /// future repair connection (a wheelwright establishment, or a ruled
    /// freight-company self-maintenance path) is a deliberate change, not a
    /// silent behaviour shift. No production code is changed by J1: the top
    /// missing edge (wagon repair) needs an owner decision first (F-J1-1).
    /// </summary>
    [TestFixture]
    public sealed class UBCJ1ManufacturingNetworkTests
    {
        private const string AssetRelativePath = "Core/Population/DevelopmentTownConfiguration.asset";

        private static FreightResourcePool NewPool()
        {
            var pool = new FreightResourcePool();
            pool.AddWagon(new FreightWagon("WG-1", "Freight wagon 1", 600));
            pool.AddWagon(new FreightWagon("WG-2", "Freight wagon 2", 600));
            return pool;
        }

        private static List<EntityId> Animals(EntityIdRegistry registry, int count)
        {
            var list = new List<EntityId>();
            for (int i = 0; i < count; i++) list.Add(registry.Allocate(EntityKind.Animal));
            return list;
        }

        private static List<EntityId> Drivers(EntityIdRegistry registry, int count)
        {
            var list = new List<EntityId>();
            for (int i = 0; i < count; i++) list.Add(registry.Allocate(EntityKind.Person));
            return list;
        }

        [Test]
        public void WagonWear_AccumulatesAcrossHauls_WithNoAutoRepair()
        {
            // Five hauls through the real reserve/release path: each release
            // wears the wagon 0.02 (FreightResourcePool.ReleaseTeam default).
            // Nothing in production restores condition, so the wagon ends at
            // 0.90 still Available — wear with no repair and no consequence.
            var pool = NewPool();
            var registry = new EntityIdRegistry();
            var diagnostics = new List<string>();
            float before = pool.GetWagon("WG-1").Condition01;

            for (int haul = 0; haul < 5; haul++)
            {
                Assert.IsTrue(pool.TryReserveTeam(500, Animals(registry, 4), Drivers(registry, 2),
                    "test haul", out DraftTeamReservation reservation, diagnostics),
                    string.Join("; ", diagnostics));
                Assert.IsNotNull(reservation);
                pool.ReleaseTeam(reservation);
            }

            Assert.AreEqual(1f, before, 0.001f);
            Assert.AreEqual(0.90f, pool.GetWagon("WG-1").Condition01, 0.001f);
            Assert.AreEqual(FreightWagonStatus.Available, pool.GetWagon("WG-1").Status);
        }

        [Test]
        public void RepairDemand_NullShop_OpensNothing()
        {
            // The wheelwright bridge fails closed: with no shop there is no
            // repairer, so a worn wagon generates no order and stays Available
            // (it keeps hauling — see WornWagon_StillDispatchable).
            var pool = NewPool();
            var diagnostics = new List<string>();
            pool.GetWagon("WG-1").Damage(0.55f); // condition 0.45, below the 0.60 threshold

            var opened = RepairDemandService.ScanFleetForRepair(
                pool, null, "freight-1", "Freight Co", 60, diagnostics);

            Assert.AreEqual(0, opened.Count);
            Assert.AreEqual(FreightWagonStatus.Available, pool.GetWagon("WG-1").Status);
            Assert.IsTrue(diagnostics.Any(d => d.Contains("wheelwright shop")),
                "expected the refusal to name the missing wheelwright shop: " + string.Join("; ", diagnostics));
        }

        [Test]
        public void WornWagon_StillDispatchable_ConditionGatesNothing()
        {
            // SelectAvailableWagon filters on status, capacity, reservation
            // and holds — never on condition. A wagon at 0.05 still hauls.
            var pool = new FreightResourcePool();
            pool.AddWagon(new FreightWagon("WG-1", "Freight wagon 1", 600));
            var registry = new EntityIdRegistry();
            var diagnostics = new List<string>();
            pool.GetWagon("WG-1").Damage(0.95f); // condition 0.05
            Assert.AreEqual(0.05f, pool.GetWagon("WG-1").Condition01, 0.001f);

            bool ok = pool.TryReserveTeam(500, Animals(registry, 4), Drivers(registry, 2),
                "test haul", out DraftTeamReservation reservation, diagnostics);

            Assert.IsTrue(ok, string.Join("; ", diagnostics));
            Assert.IsNotNull(reservation);
            Assert.AreEqual("WG-1", reservation.WagonId);
        }

        [Test]
        public void DevTown_HasNoRepairOrSupplyBusinesses()
        {
            // The audit's central roster fact: the development town has exactly
            // seven businesses, and none of them is a blacksmith, wheelwright,
            // farm, dairy, grain mill/elevator, tannery, tailor, bank, doctor
            // or butcher. That is why the smith supply drivers (D3/D4/D5),
            // the wheelwright repair economy, the farrier service, the grain
            // mill, the dairy chain and toll sawing are all idle: the
            // businesses they need do not exist. Establishing any of them
            // needs the owner to provide the name first (naming rule).
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, AssetRelativePath));
            Assert.IsTrue(File.Exists(path), "Expected the authored development-town asset at '" + path + "'.");
            string asset = File.ReadAllText(path);

            int businessesStart = asset.IndexOf("  businesses:\n", StringComparison.Ordinal);
            Assert.GreaterOrEqual(businessesStart, 0, "businesses section missing from the asset");
            int businessesEnd = asset.IndexOf("\n  timberStands:", businessesStart, StringComparison.Ordinal);
            Assert.Greater(businessesEnd, businessesStart, "could not bound the businesses section");
            string businesses = asset.Substring(businessesStart, businessesEnd - businessesStart);

            string[] present = { "general-store", "bakery", "fuel-dealer", "freight-company", "sawmill", "lumber-yard", "carpentry-workshop" };
            foreach (string key in present)
                StringAssert.Contains("  - key: " + key + "\n", businesses, "expected business '" + key + "' in the development town");

            int count = 0;
            int at = 0;
            while ((at = businesses.IndexOf("  - key: ", at, StringComparison.Ordinal)) >= 0) { count++; at += 9; }
            Assert.AreEqual(7, count, "development town must have exactly seven businesses");

            string[] absent = { "blacksmith", "wheelwright", "farm", "dairy", "creamery", "grain-mill", "grain-elevator", "tannery", "tailor", "bank", "doctor", "butcher", "hotel", "restaurant", "saloon", "mine", "post-office" };
            foreach (string key in absent)
                StringAssert.DoesNotContain("  - key: " + key, businesses, "unexpected business '" + key + "' in the development town");
        }
    }
}
