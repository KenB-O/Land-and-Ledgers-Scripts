using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Transport;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Economy.Freight.Tests
{
    /// <summary>
    /// Pass IV Worker 2: wagon reconciliation — FreightWagon and EquipmentAsset
    /// wagons share one condition truth and one reservation truth.
    /// </summary>
    [TestFixture]
    public sealed class WagonReconciliationTests
    {
        private FreightResourcePool pool;
        private TransportAssetRegistry registry;
        private EquipmentReservationService authority;

        [SetUp]
        public void SetUp()
        {
            authority = new EquipmentReservationService();
            registry = new TransportAssetRegistry();
            pool = new FreightResourcePool();
            pool.AttachDraftPower(new DraftPower.DraftPowerService(authority));
            pool.AttachWagonRegistry(registry);
        }

        private static List<EntityId> Animals(params int[] ids)
        {
            var list = new List<EntityId>();
            foreach (int id in ids) list.Add(EntityId.For(EntityKind.Animal, id));
            return list;
        }

        private static List<EntityId> Drivers(params int[] ids)
        {
            var list = new List<EntityId>();
            foreach (int id in ids) list.Add(EntityId.For(EntityKind.Person, id));
            return list;
        }

        [Test]
        public void Adapter_MapsIdentityConditionAndCapacity()
        {
            var wagon = new FreightWagon("wagon-1", "Big Wagon", 600);
            wagon.Damage(0.25f);

            EquipmentAsset asset = FreightWagonAssetAdapter.ToEquipmentAsset(wagon);
            Assert.AreEqual("wagon-1", asset.AssetId, "Identity carries over.");
            Assert.IsTrue(asset.IsWagon, "Migrated wagon must satisfy IsWagon.");
            Assert.AreEqual(0.75f, asset.Condition01, 1e-4f, "Condition carries over.");
            Assert.AreEqual(600f, asset.PayloadMassCapacityKg, 1e-4f);
            Assert.AreEqual(4f, asset.CargoVolumeCapacityM3, 1e-4f,
                "600 units -> 4 m³ documented mapping.");
            Assert.IsTrue(asset.MaintenanceLog.Count > 0, "Migration is recorded.");
        }

        [Test]
        public void Migrate_IsIdempotent_SingleConditionTruth()
        {
            var wagon = new FreightWagon("wagon-1", "Big Wagon", 600);
            pool.AddWagon(wagon);
            var diag = new List<string>();
            pool.MigrateWagonsToRegistry(diag);
            pool.MigrateWagonsToRegistry(diag); // second run: no duplicates

            Assert.AreEqual(1, registry.Assets.Count, "Migration is idempotent.");
            EquipmentAsset asset = registry.Get("wagon-1");
            Assert.IsNotNull(asset);
            Assert.AreSame(asset, wagon.AttachedAsset);

            // One condition truth: damage through the wagon hits the asset...
            wagon.Damage(0.1f);
            Assert.AreEqual(0.9f, asset.Condition01, 1e-4f);
            Assert.AreEqual(0.9f, wagon.Condition01, 1e-4f);
            // ...and repair through the asset is visible on the wagon.
            asset.RepairTo(1f);
            Assert.AreEqual(1f, wagon.Condition01, 1e-4f);
        }

        [Test]
        public void TryReserveTeam_HoldsWagonThroughAuthority()
        {
            pool.AddWagon(new FreightWagon("wagon-1", "Big Wagon", 600));
            var diag = new List<string>();

            bool ok = pool.TryReserveTeam(500, Animals(1, 2), Drivers(7), "grain haul",
                out var team, diag);
            Assert.IsTrue(ok, "Team reservation must succeed: " + string.Join("; ", diag));
            Assert.IsNotNull(team);

            string key = ReservationKeyConventions.Asset("wagon-1");
            Assert.IsTrue(authority.IsReserved(key, 0f),
                "The wagon instance must be held in the unified authority.");
            Assert.IsTrue(registry.Get("wagon-1").IsReserved,
                "The asset-level flag must agree (single reservation truth).");
            Assert.AreEqual(0, pool.AvailableWagonCount,
                "A held wagon is not available.");

            // Second team: the only wagon is held — refused, and the failed
            // attempt must not leak animal/driver holds.
            bool second = pool.TryReserveTeam(500, Animals(3, 4), Drivers(8), "second haul",
                out var team2, diag);
            Assert.IsFalse(second, "One wagon cannot serve two teams.");
            Assert.IsNull(team2);
            Assert.IsFalse(pool.DraftPower.IsAnimalReserved(EntityId.For(EntityKind.Animal, 3)),
                "Atomic rollback: failed team holds no animals.");
            Assert.IsFalse(pool.DraftPower.IsDriverReserved(EntityId.For(EntityKind.Person, 8)),
                "Atomic rollback: failed team holds no driver.");

            pool.ReleaseTeam(team);
            Assert.IsFalse(authority.IsReserved(key, 0f), "Release clears the authority hold.");
            Assert.IsFalse(registry.Get("wagon-1").IsReserved, "Release clears the asset flag.");
            Assert.AreEqual(1, pool.AvailableWagonCount);
        }

        [Test]
        public void LegacyReservedIds_MigrateToAuthorityHolds()
        {
            // Pre-reconciliation save: legacy wagon list + legacy reserved ids,
            // no authority holds yet.
            var legacyPool = new FreightResourcePool();
            legacyPool.AddWagon(new FreightWagon("wagon-9", "Old Wagon", 600));
            var legacyDto = legacyPool.CaptureSaveDto();
            legacyDto.reservedWagonIds.Add("wagon-9");

            var restored = new FreightResourcePool();
            restored.AttachDraftPower(new DraftPower.DraftPowerService(authority));
            restored.AttachWagonRegistry(registry);
            restored.LoadFromSaveDto(legacyDto);
            var diag = new List<string>();
            restored.MigrateWagonsToRegistry(diag);

            Assert.IsTrue(authority.IsReserved(ReservationKeyConventions.Asset("wagon-9"), 0f),
                "Legacy reserved ids become authority holds on migration.");
            Assert.AreEqual(0, restored.AvailableWagonCount,
                "The migrated hold keeps the wagon unavailable.");
        }

        [Test]
        public void RepairWorkflow_SurvivesReconciliation()
        {
            pool.AddWagon(new FreightWagon("wagon-1", "Big Wagon", 600));
            var diag = new List<string>();
            Assert.IsTrue(pool.TrySendToRepair("wagon-1", diag), string.Join("; ", diag));
            Assert.AreEqual(0, pool.AvailableWagonCount, "Wagon in repair cannot haul.");
            Assert.IsTrue(pool.TryCompleteRepair("wagon-1", 0.5f, diag), string.Join("; ", diag));
            Assert.AreEqual(1, pool.AvailableWagonCount);
        }

        [Test]
        public void ReleaseTeamAfterTrip_UsesProfileWear()
        {
            pool.AddWagon(new FreightWagon("wagon-1", "Big Wagon", 600));
            var diag = new List<string>();
            Assert.IsTrue(pool.TryReserveTeam(100, Animals(1, 2), Drivers(7), "trip",
                out var team, diag), string.Join("; ", diag));

            pool.ReleaseTeamAfterTrip(team, miles: 20f, loadRatio01: 1f);
            // Wagon profile: 0.0005/mile × 20 + 1.0 × 0.01 = 0.02.
            Assert.AreEqual(0.98f, registry.Get("wagon-1").Condition01, 1e-4f,
                "Trip wear is profile-driven (per-mile + load), not a flat constant.");
        }
    }
}
