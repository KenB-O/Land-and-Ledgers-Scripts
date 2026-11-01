// Pass III Worker E: performance measurements (§17). Reports measured numbers
// only (via Console.WriteLine); assertions use generous bounds to catch
// pathological regressions, not to enforce tight budgets.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using LandLedgers.Population;
using LandLedgers.World;
using LandLedgers.World.Property;

namespace LandLedgers.Editor.Orchestration
{
    [TestFixture]
    public sealed class Pass3SlicePerfTests
    {
        private List<string> Diag() => new List<string>();

        private static void Report(string label, long ms, string detail)
        {
            Console.WriteLine($"[PERF] {label}: {ms}ms {detail}");
        }

        [Test]
        public void Perf_160Acre_ParcelQueries()
        {
            // 160 acres ≈ 647,497 m²; at 4 m² per 2m cell ≈ 161,874 cells.
            // 402x402 = 161,604 cells ≈ 159.9 acres.
            var parcels = new ParcelService();
            var grid = new TownGrid(402, 402, 2f, new UnityEngine.Vector3(0, 0, 0));
            var sw = Stopwatch.StartNew();
            var parcel = parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(0, 0, 402, 402) },
                "160-acre quarter-section", "agriculture", "county",
                LandStatus.PrivateTitled, "", "section-line road");
            sw.Stop();
            Report("create-160acre-parcel", sw.ElapsedMilliseconds,
                $"cells={ParcelService.FootprintCellCount(parcel.Footprint)}");
            Assert.AreEqual(161604, ParcelService.FootprintCellCount(parcel.Footprint));

            // Thousands of spatial queries: parcel lookup by coordinate.
            // (Without a TownCell.plotId linkage the lookup returns null; this
            // measures the query path cost, not a linked result.)
            parcels.Grid = grid;
            sw.Restart();
            const int queries = 20000;
            int hits = 0;
            var rng = new Random(7); // deterministic
            for (int i = 0; i < queries; i++)
            {
                var coord = new GridCoord(rng.Next(0, 402), rng.Next(0, 402));
                if (parcels.ParcelAtLocation(coord) != null) hits++;
            }
            sw.Stop();
            Report("parcel-spatial-queries", sw.ElapsedMilliseconds, $"n={queries} hits={hits}");
            Assert.Less(sw.ElapsedMilliseconds, 30000);
        }

        [Test]
        public void Perf_MultipleSubdivisions()
        {
            var parcels = new ParcelService();
            var interests = new PropertyInterestStore();
            var subdivider = new ParcelSubdivisionService(parcels, interests);
            var farm = parcels.CreateParcel("world-1",
                new List<GridRect> { new GridRect(0, 0, 200, 200) },
                "40-acre farm", "agriculture", "county", LandStatus.PrivateTitled, "", "road");
            var titles = new TitleAuthority();
            titles.RegisterParcel(farm.Id, "farm", 40f, "P2", TitleBasis.Purchase, 1, Diag());
            subdivider.TitleHolderLookup = pid => titles.CurrentHolder(pid);

            // Three successive subdivisions (each halves one live parcel along x).
            string target = farm.Id;
            var sw = Stopwatch.StartNew();
            for (int round = 0; round < 3; round++)
            {
                ParcelRecord parent = parcels.GetParcel(target);
                GridRect r = parent.Footprint[0];
                int w = r.xMaxInclusive - r.xMin + 1;
                int d = r.zMaxInclusive - r.zMin + 1;
                int w1 = w / 2;
                var req = new SubdivisionRequest
                {
                    ParcelId = target, RequestedByHolder = "P2", Day = 10 + round,
                    ResultingFootprints = new List<List<GridRect>>
                    {
                        new List<GridRect> { new GridRect(r.xMin, r.zMin, w1, d) },
                        new List<GridRect> { new GridRect(r.xMin + w1, r.zMin, w - w1, d) },
                    },
                };
                SubdivisionResult res = subdivider.Execute(req);
                Assert.IsTrue(res.Success, res.Message);
                target = res.NewParcelIds[0];
                // Register title for children so the next round's holder check passes.
                foreach (string cid in res.NewParcelIds)
                    titles.RegisterParcel(cid, "child", 10f, "P2", TitleBasis.Grant, 11 + round, Diag());
            }
            sw.Stop();
            Report("three-subdivisions-40acre", sw.ElapsedMilliseconds,
                $"parcels={parcels.ParcelCount}");
            Assert.Less(sw.ElapsedMilliseconds, 60000);
            // Original + 2 + 2 + 2 = 7 parcels (3 retired, 4 live).
            Assert.AreEqual(7, parcels.ParcelCount);
        }

        [Test]
        public void Perf_ManyBuildings_Occupancy()
        {
            var housing = new HousingAuthority();
            var diag = Diag();
            var spaceIds = new List<string>();
            var sw = Stopwatch.StartNew();
            const int buildings = 200;
            for (int i = 0; i < buildings; i++)
            {
                string bid = "bldg-" + i;
                housing.RegisterBuilding(bid, "parcel-" + (i % 20), "house", i, "perf", diag);
                var space = housing.DefineSpace(bid, 4, diag);
                spaceIds.Add(space.SpaceId);
                housing.Occupy(1000 + i, 100 + (i % 50), space.SpaceId,
                    AccommodationArrangement.Rental, i, diag);
            }
            sw.Stop();
            Report("register-200-buildings-occupancy", sw.ElapsedMilliseconds,
                $"buildings={housing.AllBuildings().Count}");
            Assert.AreEqual(buildings, housing.AllBuildings().Count);

            sw.Restart();
            int total = 0;
            foreach (string sid in spaceIds)
                total += housing.CurrentOccupants(sid).Count;
            sw.Stop();
            Report("occupancy-lookups", sw.ElapsedMilliseconds, $"n={buildings} occupants={total}");
            Assert.AreEqual(buildings, total);
        }

        [Test]
        public void Perf_RepeatedDecisionCycles()
        {
            var service = new HouseholdDecisionService();
            var sw = Stopwatch.StartNew();
            const int cycles = 5000;
            for (int i = 0; i < cycles; i++)
            {
                service.Evaluate(new HouseholdPressureInput
                {
                    HouseholdId = (i % 50).ToString(), Day = 500 + i,
                    ReservesCents = 40 + (i % 2000), ObligationsDueCents = 300,
                    ExpectedIncomeCentsPerDay = i % 3 == 0 ? 50 : 0,
                    Dependents = 2, HasSearchableContacts = true,
                });
            }
            sw.Stop();
            Report("household-decision-cycles", sw.ElapsedMilliseconds, $"n={cycles}");
            Assert.Less(sw.ElapsedMilliseconds, 30000);
        }

        [Test]
        public void Perf_SaveLoad_LargeDataset()
        {
            var parcels = new ParcelService();
            var interests = new PropertyInterestStore();
            var titles = new TitleAuthority();
            var housing = new HousingAuthority();
            var diag = Diag();
            var rng = new Random(11); // deterministic
            for (int i = 0; i < 100; i++)
            {
                var p = parcels.CreateParcel("world-1",
                    new List<GridRect> { new GridRect(rng.Next(0, 500), rng.Next(0, 500), 10, 10) },
                    "lot " + i, "residential", "county", LandStatus.PrivateTitled, "", "");
                titles.RegisterParcel(p.Id, "lot " + i, 0.1f, "P" + (i % 10), TitleBasis.Purchase, i, diag);
                interests.Record(new PropertyInterest
                {
                    Id = PropertyInterestStore.NewInterestId(), ParcelId = p.Id,
                    Kind = InterestKind.Easement, HolderId = "County", StartDay = i, Notes = "perf",
                });
                housing.RegisterBuilding("pb-" + i, p.Id, "house", i, "perf", diag);
            }

            var sw = Stopwatch.StartNew();
            ParcelServiceSaveDto pdto = parcels.CaptureSaveDto();
            PropertyInterestStoreSaveDto idto = interests.CaptureSaveDto();
            TitleAuthority.TitleSaveDto tdto = titles.CaptureSaveDto();
            HousingAuthority.HousingSaveDto hdto = housing.CaptureSaveDto();
            sw.Stop();
            Report("save-capture-100-parcels", sw.ElapsedMilliseconds,
                $"parcels={pdto.parcels.Count} interests={idto.interests.Count}");

            sw.Restart();
            var p2 = new ParcelService(); p2.LoadFromSaveDto(pdto);
            var i2 = new PropertyInterestStore(); i2.LoadFromSaveDto(idto);
            var t2 = new TitleAuthority(); t2.LoadFromSaveDto(tdto);
            var h2 = new HousingAuthority(); h2.LoadFromSaveDto(hdto);
            sw.Stop();
            Report("save-restore-100-parcels", sw.ElapsedMilliseconds,
                $"parcels={p2.ParcelCount} buildings={h2.AllBuildings().Count}");
            Assert.AreEqual(100, p2.ParcelCount);
            Assert.AreEqual(100, h2.AllBuildings().Count);
            Assert.Less(sw.ElapsedMilliseconds, 30000);
        }
    }
}
