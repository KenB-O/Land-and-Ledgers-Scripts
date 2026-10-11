using System;
using System.Collections.Generic;
using System.Diagnostics;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass III land core (Worker A): legal parcel identity, the single
    /// non-title rights store, atomic subdivision, and the title-chain transfer
    /// function. Deterministic; Roslyn harness (C# logic only, not Unity).
    /// </summary>
    public sealed class ParcelLandCoreTests
    {
        private ParcelService parcels;
        private PropertyInterestStore interests;
        private TitleAuthority titles;
        private ParcelSubdivisionService subdivisions;
        private EntityIdRegistry ids;
        private List<string> diag;
        private StubBuildingIndex buildings;

        [SetUp]
        public void SetUp()
        {
            parcels = new ParcelService();
            interests = new PropertyInterestStore();
            titles = new TitleAuthority();
            ids = new EntityIdRegistry();
            titles.IdAllocator = ids;
            diag = new List<string>();
            buildings = new StubBuildingIndex();
            subdivisions = new ParcelSubdivisionService(parcels, interests)
            {
                BuildingIndex = buildings,
                TitleHolderLookup = pid => titles.CurrentHolder(pid),
            };
        }

        private ParcelRecord MakeParcel(string plotId, int w, int d, string holder)
        {
            var parcel = parcels.CreateParcel(
                "world-1",
                new List<GridRect> { new GridRect(0, 0, w, d) },
                "survey lot 1", "farm", "county", LandStatus.PrivateTitled,
                plotId, "road frontage north");
            titles.RegisterParcel(parcel.Id, "lot 1", 40f, holder, TitleBasis.Grant, 0, diag);
            return parcel;
        }

        private static SubdivisionRequest SplitRequest(string parcelId, string holder, params GridRect[] children)
        {
            var request = new SubdivisionRequest
            {
                ParcelId = parcelId,
                RequestedByHolder = holder,
                Day = 100,
            };
            foreach (GridRect rect in children)
                request.ResultingFootprints.Add(new List<GridRect> { rect });
            return request;
        }

        [Test]
        public void ParcelIdentitySurvivesOwnerChange()
        {
            ParcelRecord parcel = MakeParcel("12", 10, 10, "Founder");
            string parcelId = parcel.Id;

            IOwnershipTransferService transfers = titles;
            string recordId = transfers.TransferTitle(parcelId, "Founder", "Buyer", "deed-1", 50);

            Assert.IsNotNull(recordId);
            Assert.AreEqual("Buyer", titles.CurrentHolder(parcelId));
            ParcelRecord after = parcels.GetParcel(parcelId);
            Assert.IsNotNull(after);
            Assert.AreEqual(parcelId, after.Id);
            Assert.AreEqual(400.0, after.AreaSquareMeters);
            Assert.AreEqual(2, titles.ChainOf(parcelId).Count);
        }

        [Test]
        public void TransferRejectsWrongFromHolder()
        {
            ParcelRecord parcel = MakeParcel("12", 10, 10, "Founder");
            IOwnershipTransferService transfers = titles;

            string recordId = transfers.TransferTitle(parcel.Id, "Impostor", "Buyer", "deed-9", 50);

            Assert.IsNull(recordId);
            Assert.AreEqual("Founder", titles.CurrentHolder(parcel.Id));
            Assert.AreEqual(1, titles.ChainOf(parcel.Id).Count);
        }

        [Test]
        public void TransferMapsInstrumentToPurchaseBasisAndDefaultsToGrant()
        {
            ParcelRecord parcel = MakeParcel("12", 10, 10, "Founder");
            IOwnershipTransferService transfers = titles;

            transfers.TransferTitle(parcel.Id, "Founder", "Buyer", "deed-1", 50);
            IReadOnlyList<TitleRecord> chain = titles.ChainOf(parcel.Id);
            Assert.AreEqual(TitleBasis.Purchase, chain[chain.Count - 1].Basis);
            Assert.IsNotNull(chain[chain.Count - 1].RecordId.ToString());

            string noInstrument = transfers.TransferTitle(parcel.Id, "Buyer", "Heir", "", 60);
            Assert.IsNotNull(noInstrument);
            chain = titles.ChainOf(parcel.Id);
            Assert.AreEqual(TitleBasis.Grant, chain[chain.Count - 1].Basis);
        }

        [Test]
        public void ParcelsHeldByListsCurrentHoldings()
        {
            ParcelRecord a = MakeParcel("1", 4, 4, "Player");
            ParcelRecord b = MakeParcel("2", 4, 4, "Player");
            ParcelRecord c = MakeParcel("3", 4, 4, "Neighbor");

            IOwnershipTransferService transfers = titles;
            transfers.TransferTitle(c.Id, "Neighbor", "Player", "deed-2", 10);

            IReadOnlyList<string> held = transfers.ParcelsHeldBy("Player");
            Assert.AreEqual(3, held.Count);
            Assert.Contains(a.Id, held);
            Assert.Contains(b.Id, held);
            Assert.Contains(c.Id, held);
            Assert.AreEqual(0, transfers.ParcelsHeldBy("Nobody").Count);
        }

        [Test]
        public void ParcelAtLocationResolvesThroughPlotLink()
        {
            ParcelRecord parcel = MakeParcel("7", 10, 10, "Founder");
            var grid = new TownGrid(10, 10, 2f, Vector3.zero);
            for (int x = 0; x < 10; x++)
                for (int z = 0; z < 10; z++)
                {
                    var cell = TownCell.CreateDefault();
                    cell.plotId = 7;
                    grid.SetCell(new GridCoord(x, z), cell);
                }
            parcels.Grid = grid;

            ParcelRecord found = parcels.ParcelAtLocation(new GridCoord(3, 4));
            Assert.IsNotNull(found);
            Assert.AreEqual(parcel.Id, found.Id);
            Assert.IsNull(parcels.ParcelAtLocation(new GridCoord(99, 99)));
        }

        [Test]
        public void HoldingGroupsParcelsWithoutMergingTitle()
        {
            ParcelRecord a = MakeParcel("1", 4, 4, "OwnerA");
            ParcelRecord b = MakeParcel("2", 4, 4, "OwnerB");
            HoldingRecord holding = parcels.CreateHolding("Estate", "two parcels");

            Assert.IsTrue(parcels.AddParcelToHolding(holding.Id, a.Id));
            Assert.IsTrue(parcels.AddParcelToHolding(holding.Id, b.Id));

            Assert.AreEqual(2, parcels.ParcelsInHolding(holding.Id).Count);
            Assert.AreEqual("OwnerA", titles.CurrentHolder(a.Id));
            Assert.AreEqual("OwnerB", titles.CurrentHolder(b.Id));
            Assert.IsTrue(parcels.RemoveParcelFromHolding(holding.Id, a.Id));
            Assert.AreEqual(1, parcels.ParcelsInHolding(holding.Id).Count);
        }

        [Test]
        public void SubdivisionHappyPathPreservesLineage()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");

            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual(2, result.NewParcelIds.Count);
            Assert.AreEqual(parent.Id, result.RetiredParcelId);
            Assert.IsTrue(parcels.GetParcel(parent.Id).IsRetired);

            double totalArea = 0;
            foreach (string childId in result.NewParcelIds)
            {
                ParcelRecord child = parcels.GetParcel(childId);
                Assert.IsNotNull(child);
                Assert.Contains(parent.Id, child.ParentParcelIds);
                Assert.AreEqual(parent.Id, child.RootSurveyId);
                Assert.IsFalse(child.IsRetired);
                Assert.AreEqual(200.0, child.AreaSquareMeters);
                totalArea += child.AreaSquareMeters;
            }
            Assert.AreEqual(400.0, totalArea);
        }

        [Test]
        public void SubdivisionRequiresExactTiling()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");

            // Gap: second child covers only 4 of 5 rows.
            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 4)));

            Assert.IsFalse(result.Success);
            Assert.IsFalse(parcels.GetParcel(parent.Id).IsRetired);
            Assert.AreEqual(1, parcels.ParcelCount);

            // Overlap: children share a row.
            result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 6), new GridRect(0, 5, 10, 5)));
            Assert.IsFalse(result.Success);
            Assert.AreEqual(1, parcels.ParcelCount);
        }

        [Test]
        public void SplitThroughBuildingIsRejected()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");
            buildings.ByParcel[parent.Id] = new List<ParcelBuildingFootprint>
            {
                // 4x4 building straddling the z=5 split line.
                new ParcelBuildingFootprint("building-1", new GridRect(3, 3, 4, 4)),
            };

            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsFalse(result.Success);
            Assert.IsFalse(parcels.GetParcel(parent.Id).IsRetired);
            Assert.AreEqual(1, parcels.ParcelCount);
        }

        [Test]
        public void BuildingWhollyInsideOneChildIsLinkedToThatChild()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");
            buildings.ByParcel[parent.Id] = new List<ParcelBuildingFootprint>
            {
                new ParcelBuildingFootprint("building-1", new GridRect(1, 1, 2, 2)),
            };

            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsTrue(result.Success, result.Message);
            ParcelRecord first = parcels.GetParcel(result.NewParcelIds[0]);
            ParcelRecord second = parcels.GetParcel(result.NewParcelIds[1]);
            Assert.Contains("building-1", first.BuildingIds);
            Assert.IsFalse(second.BuildingIds.Contains("building-1"));
        }

        [Test]
        public void AccessLossWarnsAndCarriesNote()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");

            SubdivisionValidation validation = subdivisions.Validate(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsTrue(validation.IsValid);
            Assert.IsNotEmpty(validation.Warnings);

            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));
            Assert.IsTrue(result.Success);
            ParcelRecord child = parcels.GetParcel(result.NewParcelIds[0]);
            Assert.IsTrue(child.AccessNotes.Contains("no easement created by subdivision"));
        }

        [Test]
        public void MortgageChargeBlocksSubdivision()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");
            interests.Record(new PropertyInterest
            {
                ParcelId = parent.Id,
                Kind = InterestKind.MortgageCharge,
                HolderId = "Bank",
                TermsRef = "obligation-1",
                StartDay = 10,
            });

            SubdivisionValidation validation = subdivisions.Validate(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsFalse(validation.IsValid);
            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));
            Assert.IsFalse(result.Success);
            Assert.IsFalse(parcels.GetParcel(parent.Id).IsRetired);
            Assert.AreEqual(1, parcels.ParcelCount);
        }

        [Test]
        public void NonMonetaryInterestsAreCarriedToChildren()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");
            interests.Record(new PropertyInterest
            {
                ParcelId = parent.Id,
                Kind = InterestKind.Easement,
                HolderId = "Neighbor",
                StartDay = 10,
                Notes = "drainage",
            });

            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsTrue(result.Success, result.Message);
            foreach (string childId in result.NewParcelIds)
            {
                IReadOnlyList<PropertyInterest> carried = interests.ActiveForParcel(childId);
                Assert.AreEqual(1, carried.Count);
                Assert.AreEqual(InterestKind.Easement, carried[0].Kind);
                Assert.AreEqual("Neighbor", carried[0].HolderId);
            }
            Assert.AreEqual(0, interests.ActiveForParcel(parent.Id).Count);
        }

        [Test]
        public void FailedExecutionRollsBackToOriginalState()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");
            interests.Record(new PropertyInterest
            {
                ParcelId = parent.Id,
                Kind = InterestKind.Easement,
                HolderId = "Neighbor",
                StartDay = 10,
            });

            // Seed the faulty store with the same interest so validation passes
            // and execution reaches the carry-forward mutation phase. This is
            // Record call #1; the injected fault fires on call #2 (first child
            // carry-forward inside Execute).
            var faultyInterests = new ThrowingInterestStore(2);
            var faulty = new ParcelSubdivisionService(parcels, faultyInterests)
            {
                BuildingIndex = buildings,
                TitleHolderLookup = pid => titles.CurrentHolder(pid),
            };
            // Seed the faulty store (Record call #1) with the same interest so
            // validation passes and execution reaches the carry-forward
            // mutation phase, where call #2 throws mid-execution.
            faultyInterests.Record(new PropertyInterest
            {
                ParcelId = parent.Id,
                Kind = InterestKind.Easement,
                HolderId = "Neighbor",
                StartDay = 10,
            });

            SubdivisionResult result = faulty.Execute(
                SplitRequest(parent.Id, "Founder", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Message.Contains("rolled back"));
            Assert.IsFalse(parcels.GetParcel(parent.Id).IsRetired);
            Assert.AreEqual(1, parcels.ParcelCount);
            Assert.AreEqual(1, faultyInterests.ActiveForParcel(parent.Id).Count);
        }

        [Test]
        public void InterestRecordQueryExtinguish()
        {
            ParcelRecord parcel = MakeParcel("12", 10, 10, "Founder");
            var lease = new PropertyInterest
            {
                ParcelId = parcel.Id,
                Kind = InterestKind.Lease,
                HolderId = "Tenant",
                TermsRef = "agreement-3",
                StartDay = 20,
            };
            interests.Record(lease);
            Assert.IsNotNull(lease.Id);
            Assert.IsTrue(lease.Id.StartsWith("interest-"));

            Assert.AreEqual(1, interests.ActiveForParcel(parcel.Id).Count);
            Assert.AreEqual(1, interests.ActiveForHolder("Tenant").Count);

            interests.Extinguish(lease.Id, 200, "term ended");
            Assert.AreEqual(0, interests.ActiveForParcel(parcel.Id).Count);
            Assert.AreEqual(0, interests.ActiveForHolder("Tenant").Count);
            Assert.IsTrue(interests.Get(lease.Id).IsExtinguished);
        }

        [Test]
        public void SaveLoadRoundTripDoesNotDuplicateParcels()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");
            HoldingRecord holding = parcels.CreateHolding("Estate", "notes");
            parcels.AddParcelToHolding(holding.Id, parent.Id);
            interests.Record(new PropertyInterest
            {
                ParcelId = parent.Id,
                Kind = InterestKind.UseRestriction,
                HolderId = "County",
                StartDay = 5,
            });

            ParcelServiceSaveDto parcelDto = parcels.CaptureSaveDto();
            PropertyInterestStoreSaveDto interestDto = interests.CaptureSaveDto();

            var freshParcels = new ParcelService();
            var freshInterests = new PropertyInterestStore();
            freshParcels.LoadFromSaveDto(parcelDto);
            freshInterests.LoadFromSaveDto(interestDto);
            // Loading the same snapshot twice must not duplicate anything.
            freshParcels.LoadFromSaveDto(parcelDto);
            freshInterests.LoadFromSaveDto(interestDto);

            Assert.AreEqual(1, freshParcels.ParcelCount);
            Assert.AreEqual(1, freshParcels.HoldingCount);
            Assert.AreEqual(parent.Id, freshParcels.GetParcel(parent.Id).Id);
            Assert.AreEqual(1, freshParcels.ParcelsInHolding(holding.Id).Count);
            Assert.AreEqual(1, freshInterests.ActiveForParcel(parent.Id).Count);
            Assert.AreEqual(1, freshInterests.InterestCount);
        }

        [Test]
        public void NonHolderCannotSubdivide()
        {
            ParcelRecord parent = MakeParcel("12", 10, 10, "Founder");

            SubdivisionResult result = subdivisions.Execute(
                SplitRequest(parent.Id, "Impostor", new GridRect(0, 0, 10, 5), new GridRect(0, 5, 10, 5)));

            Assert.IsFalse(result.Success);
            Assert.IsFalse(parcels.GetParcel(parent.Id).IsRetired);
        }

        [Test]
        public void Perf_LargeParcelCellToParcelLookups()
        {
            // ~81k cells on the 2 m grid (285 x 285), one parcel linked to plot 7.
            const int size = 285;
            ParcelRecord parcel = parcels.CreateParcel(
                "world-1",
                new List<GridRect> { new GridRect(0, 0, size, size) },
                "quarter section", "farm", "county", LandStatus.PrivateTitled,
                "7", string.Empty);
            var grid = new TownGrid(size, size, 2f, Vector3.zero);
            for (int x = 0; x < size; x++)
                for (int z = 0; z < size; z++)
                {
                    var cell = TownCell.CreateDefault();
                    cell.plotId = 7;
                    grid.SetCell(new GridCoord(x, z), cell);
                }
            parcels.Grid = grid;

            // Warm up, then time 20,000 deterministic lookups.
            Assert.IsNotNull(parcels.ParcelAtLocation(new GridCoord(0, 0)));
            var sw = Stopwatch.StartNew();
            int found = 0;
            for (int i = 0; i < 20000; i++)
            {
                int x = (i * 37) % size;
                int z = (i * 101) % size;
                if (parcels.ParcelAtLocation(new GridCoord(x, z)) != null) found++;
            }
            sw.Stop();
            Console.WriteLine($"PERF parcel-lookup: 20000 lookups over {size * size} cells took {sw.ElapsedMilliseconds} ms ({found} found)");
            Assert.AreEqual(20000, found);
            Assert.Less(sw.ElapsedMilliseconds, 5000L);

            // Time a full-coverage subdivision validation over the same ~81k cells.
            var request = SplitRequest(parcel.Id, null,
                new GridRect(0, 0, size, 142), new GridRect(0, 142, size, 143));
            subdivisions.TitleHolderLookup = null; // skip authority for the perf probe
            sw.Restart();
            SubdivisionValidation validation = subdivisions.Validate(request);
            sw.Stop();
            Console.WriteLine($"PERF subdivision-validate: {size * size} cells took {sw.ElapsedMilliseconds} ms (valid={validation.IsValid})");
            Assert.IsTrue(validation.IsValid);
            Assert.Less(sw.ElapsedMilliseconds, 10000L);
        }

        private sealed class StubBuildingIndex : IParcelBuildingIndex
        {
            public readonly Dictionary<string, List<ParcelBuildingFootprint>> ByParcel =
                new Dictionary<string, List<ParcelBuildingFootprint>>(StringComparer.Ordinal);

            public IReadOnlyList<ParcelBuildingFootprint> BuildingsOnParcel(string parcelId)
            {
                return ByParcel.TryGetValue(parcelId, out List<ParcelBuildingFootprint> list)
                    ? list
                    : new List<ParcelBuildingFootprint>();
            }
        }

        private sealed class ThrowingInterestStore : ISnapshotableInterestStore
        {
            private readonly PropertyInterestStore inner = new PropertyInterestStore();
            private readonly int throwOnRecordCall;
            private int recordCalls;

            public ThrowingInterestStore(int throwOnRecordCall)
            {
                this.throwOnRecordCall = throwOnRecordCall;
            }

            public void Record(PropertyInterest interest)
            {
                recordCalls++;
                if (recordCalls == throwOnRecordCall)
                    throw new InvalidOperationException("injected mid-execution fault");
                inner.Record(interest);
            }

            public void Extinguish(string interestId, int day, string reason) =>
                inner.Extinguish(interestId, day, reason);

            public IReadOnlyList<PropertyInterest> ActiveForParcel(string parcelId) =>
                inner.ActiveForParcel(parcelId);

            public IReadOnlyList<PropertyInterest> ActiveForHolder(string holderId) =>
                inner.ActiveForHolder(holderId);

            public PropertyInterestStoreSaveDto CaptureSaveDto() => inner.CaptureSaveDto();

            public void LoadFromSaveDto(PropertyInterestStoreSaveDto dto) => inner.LoadFromSaveDto(dto);
        }
    }
}
