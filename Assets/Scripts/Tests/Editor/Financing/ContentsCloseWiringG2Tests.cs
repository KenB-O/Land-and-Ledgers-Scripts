using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Financing
{
    /// <summary>
    /// G-2 (W-G4 Workstream 2, expanded campaign 2026-11-16): the
    /// contents-close wiring helpers and the production placement
    /// resolver. The manager hunks and the bootstrapper binding are
    /// harness-excluded (source-reviewed only, Unity proof owed from
    /// Codex); these tests pin the harness-compiled logic — the seller
    /// register scan, the player household lookup, and the resolver's
    /// placement-id match.
    /// </summary>
    public sealed class ContentsCloseWiringG2Tests
    {
        private static HouseholdFurnishingRegister RegisterWithBinding(int householdId, string assetId, int buildingInstanceId, string placementId)
        {
            var register = new HouseholdFurnishingRegister { HouseholdId = householdId };
            register.Bindings.Add(new FurnishingPlacementBinding
            {
                AssetId = assetId,
                BuildingInstanceId = buildingInstanceId,
                PlacementId = placementId,
            });
            return register;
        }

        [Test]
        public void SellerRegister_FoundWhenOneRegisterHoldsTheBuilding()
        {
            HouseholdFurnishingRegister seller = RegisterWithBinding(7, "bed-1", 5, "plc-1");
            HouseholdFurnishingRegister other = RegisterWithBinding(3, "table-1", 9, "plc-2");
            var registers = new List<HouseholdFurnishingRegister> { seller, other };
            Assert.AreSame(seller, ContentsCloseWiring.ResolveSellerRegisterForBuilding(5, registers));
            Assert.AreSame(other, ContentsCloseWiring.ResolveSellerRegisterForBuilding(9, registers));
        }

        [Test]
        public void SellerRegister_NullWhenNoRegisterHoldsTheBuilding()
        {
            HouseholdFurnishingRegister seller = RegisterWithBinding(7, "bed-1", 5, "plc-1");
            var registers = new List<HouseholdFurnishingRegister> { seller };
            Assert.IsNull(ContentsCloseWiring.ResolveSellerRegisterForBuilding(99, registers));
        }

        [Test]
        public void SellerRegister_NullWhenTwoRegistersHoldTheSameBuilding()
        {
            // Two households cannot furnish the same building: an
            // ambiguous match is a data defect and the close must fail
            // closed, not pick one.
            HouseholdFurnishingRegister first = RegisterWithBinding(7, "bed-1", 5, "plc-1");
            HouseholdFurnishingRegister second = RegisterWithBinding(3, "chair-1", 5, "plc-2");
            var registers = new List<HouseholdFurnishingRegister> { first, second };
            Assert.IsNull(ContentsCloseWiring.ResolveSellerRegisterForBuilding(5, registers));
        }

        [Test]
        public void SellerRegister_NullWhenRegistersNull()
        {
            Assert.IsNull(ContentsCloseWiring.ResolveSellerRegisterForBuilding(5, null));
        }

        [Test]
        public void PlayerHousehold_Found()
        {
            var population = new PopulationState();
            population.households.Add(new HouseholdState { id = 3, isPlayerHousehold = false });
            population.households.Add(new HouseholdState { id = 7, isPlayerHousehold = true });
            Assert.AreEqual(7, ContentsCloseWiring.FindPlayerHouseholdId(population));
        }

        [Test]
        public void PlayerHousehold_MinusOneWhenNoneFlagged()
        {
            var population = new PopulationState();
            population.households.Add(new HouseholdState { id = 3, isPlayerHousehold = false });
            Assert.AreEqual(-1, ContentsCloseWiring.FindPlayerHouseholdId(population));
        }

        [Test]
        public void PlayerHousehold_MinusOneWhenPopulationNull()
        {
            Assert.AreEqual(-1, ContentsCloseWiring.FindPlayerHouseholdId(null));
        }

        private static BuildingFootprintAuthority MakeBuilding(string name)
        {
            GameObject building = new(name);
            return building.AddComponent<BuildingFootprintAuthority>();
        }

        private static BuildingEquipmentPlacement MakePlacement(string name, string placementId, BuildingFootprintAuthority building)
        {
            GameObject item = new(name);
            BuildingEquipmentPlacement placement = item.AddComponent<BuildingEquipmentPlacement>();
            if (building != null) placement.AssociateWithBuilding(building);
            placement.Configure(placementId, "furniture.bed", "Bed", 0);
            return placement;
        }

        [Test]
        public void Resolver_ResolvesByPlacementIdAndReturnsOwnAuthority()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("G2 building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", "plc-1", building);
                itemObject = placement.gameObject;
                var resolver = new SceneContentsPlacementResolver(() => new[] { placement });
                bool resolved = resolver.TryResolvePlacement("plc-1", 5, out BuildingEquipmentPlacement found, out BuildingFootprintAuthority foundBuilding);
                Assert.IsTrue(resolved);
                Assert.AreSame(placement, found);
                // The returned building IS the placement's own authority —
                // the pair the transfer's cross-check requires.
                Assert.AreSame(building, foundBuilding);
                Assert.AreSame(placement.BuildingAuthority, foundBuilding);
            }
            finally
            {
                if (buildingObject != null) UnityEngine.Object.DestroyImmediate(buildingObject);
                if (itemObject != null) UnityEngine.Object.DestroyImmediate(itemObject);
            }
        }

        [Test]
        public void Resolver_FalseForUnknownPlacementId()
        {
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("G2 building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", "plc-1", building);
                itemObject = placement.gameObject;
                var resolver = new SceneContentsPlacementResolver(() => new[] { placement });
                bool resolved = resolver.TryResolvePlacement("plc-9", 5, out BuildingEquipmentPlacement found, out BuildingFootprintAuthority foundBuilding);
                Assert.IsFalse(resolved);
                Assert.IsNull(found);
                Assert.IsNull(foundBuilding);
            }
            finally
            {
                if (buildingObject != null) UnityEngine.Object.DestroyImmediate(buildingObject);
                if (itemObject != null) UnityEngine.Object.DestroyImmediate(itemObject);
            }
        }

        [Test]
        public void Resolver_FalseWhenPlacementHasNoAuthority()
        {
            GameObject itemObject = null;
            try
            {
                BuildingEquipmentPlacement placement = MakePlacement("Bed", "plc-1", null);
                itemObject = placement.gameObject;
                var resolver = new SceneContentsPlacementResolver(() => new[] { placement });
                bool resolved = resolver.TryResolvePlacement("plc-1", 5, out BuildingEquipmentPlacement found, out BuildingFootprintAuthority foundBuilding);
                Assert.IsFalse(resolved);
                Assert.IsNull(found);
                Assert.IsNull(foundBuilding);
            }
            finally
            {
                if (itemObject != null) UnityEngine.Object.DestroyImmediate(itemObject);
            }
        }

        [Test]
        public void Resolver_FalseForEmptyPlacementId()
        {
            var resolver = new SceneContentsPlacementResolver(() => new BuildingEquipmentPlacement[0]);
            Assert.IsFalse(resolver.TryResolvePlacement(string.Empty, 5, out _, out _));
            Assert.IsFalse(resolver.TryResolvePlacement(null, 5, out _, out _));
        }

        [Test]
        public void Resolver_NullSourceThrowsAtConstruction()
        {
            Assert.Throws<ArgumentNullException>(() => new SceneContentsPlacementResolver(null));
        }

        [Test]
        public void Resolver_DoesNotRefuseOnBuildingInstanceId()
        {
            // Pinned limitation, not an oversight: a
            // BuildingFootprintAuthority carries no int instance id and
            // no int-to-authority registry exists, so the resolver cannot
            // verify the building id against the scene. The building
            // binding is proven in PropertyContentsTransfer.CanTransfer
            // against the seller's register binding before the resolver
            // is ever called.
            GameObject buildingObject = null;
            GameObject itemObject = null;
            try
            {
                BuildingFootprintAuthority building = MakeBuilding("G2 building");
                buildingObject = building.gameObject;
                BuildingEquipmentPlacement placement = MakePlacement("Bed", "plc-1", building);
                itemObject = placement.gameObject;
                var resolver = new SceneContentsPlacementResolver(() => new[] { placement });
                bool resolved = resolver.TryResolvePlacement("plc-1", 999, out BuildingEquipmentPlacement found, out _);
                Assert.IsTrue(resolved);
                Assert.AreSame(placement, found);
            }
            finally
            {
                if (buildingObject != null) UnityEngine.Object.DestroyImmediate(buildingObject);
                if (itemObject != null) UnityEngine.Object.DestroyImmediate(itemObject);
            }
        }
    }
}
