using System.Collections.Generic;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// R34 (note 111 §6, as corrected by note 129), slice 1: the legal-parcel
    /// registry is seeded at New Game from the town plots — one ParcelRecord
    /// per town plot, its id the canonical plot key
    /// (PlayerTitleBridge.ParcelKeyForPlot), its footprint the plot's bounds,
    /// its use the plot's zone. Before this slice ParcelService.CreateParcel
    /// had zero production callers and the registry was empty in a live game;
    /// the title chain keys parcels "plot-{id}" and the subdivision validator
    /// passes the registry id straight into the chain, so the ruling's two
    /// halves ("via CreateParcel" AND "id derived from the plot id") cannot
    /// both hold — note 129 Q1 rules (b): seed via RegisterParcelRecord with
    /// Id = ParcelKeyForPlot(plot.id); the literal CreateParcel shape is
    /// REJECTED (F1 subdivision title join dead, F2 permanent two-identity
    /// split). Seam-contract tests only: the bootstrapper call site is
    /// harness-excluded and source-reviewed (R31/R47/R33 precedent). Note 129
    /// C1–C6 applied; Q1–Q7 ruled (canonical plot key, instance seam on
    /// ParcelService, field mapping, plot-link idempotency, :460 placement,
    /// InitializeSlice the only New Game path, compile-red CS1061).
    /// </summary>
    [TestFixture]
    public sealed class R34ParcelSeedingNewGameTests
    {
        private ParcelService parcels;

        [SetUp]
        public void SetUp()
        {
            parcels = new ParcelService();
        }

        private static List<TownPlot> Plots(params (int id, PlotZone zone, GridRect bounds)[] specs)
        {
            var list = new List<TownPlot>();
            foreach ((int id, PlotZone zone, GridRect bounds) spec in specs)
            {
                list.Add(new TownPlot { id = spec.id, zone = spec.zone, bounds = spec.bounds });
            }
            return list;
        }

        private static List<TownPlot> StandardFixture()
        {
            return Plots(
                (11, PlotZone.Residential, new GridRect(0, 0, 4, 5)),
                (12, PlotZone.Business, new GridRect(10, 0, 6, 3)),
                (13, PlotZone.Agricultural, new GridRect(0, 10, 2, 2)));
        }

        [Test]
        public void SeedParcelsFromTownPlots_OneParcelPerPlot()
        {
            int seeded = parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);

            Assert.AreEqual(3, seeded);
            Assert.AreEqual(3, parcels.ParcelCount);
            Assert.IsTrue(parcels.ParcelForPlot(11) != null, "plot 11 must seed a parcel");
            Assert.IsTrue(parcels.ParcelForPlot(12) != null, "plot 12 must seed a parcel");
            Assert.IsTrue(parcels.ParcelForPlot(13) != null, "plot 13 must seed a parcel");
        }

        [Test]
        public void SeedParcelsFromTownPlots_ParcelIdIsCanonicalPlotKey()
        {
            parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);

            ParcelRecord record = parcels.ParcelForPlot(11);
            Assert.AreEqual(PlayerTitleBridge.ParcelKeyForPlot(11), record.Id,
                "asserted through the bridge method, not a literal \"plot-11\" string");
            Assert.IsTrue(object.ReferenceEquals(record, parcels.GetParcel(PlayerTitleBridge.ParcelKeyForPlot(11))),
                "GetParcel by the canonical key must return the same record");
        }

        [Test]
        public void SeedParcelsFromTownPlots_FootprintAndAreaDeriveFromPlotBounds()
        {
            parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);

            ParcelRecord plot11 = parcels.ParcelForPlot(11);
            Assert.AreEqual(1, plot11.Footprint.Count, "one rect: the plot's own bounds");
            Assert.AreEqual(0, plot11.Footprint[0].xMin);
            Assert.AreEqual(0, plot11.Footprint[0].zMin);
            Assert.AreEqual(4, plot11.Footprint[0].width);
            Assert.AreEqual(5, plot11.Footprint[0].depth);
            Assert.AreEqual(80.0, plot11.AreaSquareMeters, 0.001, "20 cells x 4 m2");
            Assert.AreEqual(72.0, parcels.ParcelForPlot(12).AreaSquareMeters, 0.001, "18 cells x 4 m2");
            Assert.AreEqual(16.0, parcels.ParcelForPlot(13).AreaSquareMeters, 0.001, "4 cells x 4 m2");

            // C6 (note 129): pin the remaining §3.1 field assignments on the
            // seeded record, so a future edit cannot silently change the §2.6
            // mapping. WorldId is string.Empty here because the fixture seeds
            // with string.Empty, as the bootstrapper call site does.
            Assert.AreEqual(LandStatus.Unknown, plot11.Status);
            Assert.AreEqual(string.Empty, plot11.Jurisdiction);
            Assert.AreEqual(string.Empty, plot11.WorldId);
            Assert.AreEqual(string.Empty, plot11.AccessNotes);
        }

        [Test]
        public void SeedParcelsFromTownPlots_ZoneCarriedToParcelUse()
        {
            parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);

            Assert.AreEqual("Residential", parcels.ParcelForPlot(11).ParcelUse);
            Assert.AreEqual("Business", parcels.ParcelForPlot(12).ParcelUse);
            Assert.AreEqual("Agricultural", parcels.ParcelForPlot(13).ParcelUse);
        }

        [Test]
        public void SeedParcelsFromTownPlots_ReSeedIsIdempotent()
        {
            parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);
            string id11 = parcels.ParcelForPlot(11).Id;
            string id12 = parcels.ParcelForPlot(12).Id;
            string id13 = parcels.ParcelForPlot(13).Id;

            int reseeded = parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);

            Assert.AreEqual(0, reseeded);
            Assert.AreEqual(3, parcels.ParcelCount);
            Assert.AreEqual(id11, parcels.ParcelForPlot(11).Id, "same record, not a replacement");
            Assert.AreEqual(id12, parcels.ParcelForPlot(12).Id);
            Assert.AreEqual(id13, parcels.ParcelForPlot(13).Id);
        }

        [Test]
        public void SeedParcelsFromTownPlots_DegenerateInputsSeedSafely()
        {
            Assert.AreEqual(0, parcels.SeedParcelsFromTownPlots(null, "world-1"), "null list seeds nothing");
            Assert.AreEqual(0, parcels.ParcelCount);
            Assert.AreEqual(0, parcels.SeedParcelsFromTownPlots(new List<TownPlot>(), "world-1"), "empty list seeds nothing");

            List<TownPlot> degenerate = Plots((21, PlotZone.MixedUse, new GridRect(0, 0, 0, 0)));
            int seeded = parcels.SeedParcelsFromTownPlots(degenerate, string.Empty);

            Assert.AreEqual(1, seeded, "invalid bounds still seed: identity is recorded even when the geometry is degenerate");
            ParcelRecord record = parcels.ParcelForPlot(21);
            Assert.IsTrue(record != null);
            Assert.AreEqual(0, record.Footprint.Count, "empty footprint for invalid bounds");
            Assert.AreEqual(0.0, record.AreaSquareMeters, 0.001);
        }

        [Test]
        public void SeedParcelsFromTownPlots_SaveLoadRoundTripPreservesSeededRegistry()
        {
            parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);
            ParcelServiceSaveDto dto = parcels.CaptureSaveDto();

            var restored = new ParcelService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(3, restored.ParcelCount);
            Assert.AreEqual(PlayerTitleBridge.ParcelKeyForPlot(11), restored.ParcelForPlot(11).Id);
            Assert.AreEqual(72.0, restored.ParcelForPlot(12).AreaSquareMeters, 0.001);
            Assert.AreEqual("Agricultural", restored.ParcelForPlot(13).ParcelUse);
        }

        [Test]
        public void SeedParcelsFromTownPlots_PreExistingParcelKeptNotDuplicatedOrOverwritten()
        {
            parcels.CreateParcel("world-1", new List<GridRect> { new GridRect(0, 0, 4, 5) },
                "survey lot 1", "farm", "county", LandStatus.PrivateTitled, "11", "road frontage north");

            int seeded = parcels.SeedParcelsFromTownPlots(StandardFixture(), string.Empty);

            Assert.AreEqual(2, seeded, "plots 12 and 13 only; plot 11 already linked");
            Assert.AreEqual(3, parcels.ParcelCount);
            ParcelRecord plot11 = parcels.ParcelForPlot(11);
            Assert.AreEqual("survey lot 1", plot11.SurveyDescription, "the pre-existing record is untouched");
            Assert.IsTrue(plot11.Id.StartsWith("parcel-"), "the pre-existing record keeps its GUID id");
            Assert.IsTrue(parcels.ParcelForPlot(12) != null, "plot 12 seeds normally");
            Assert.IsTrue(parcels.ParcelForPlot(13) != null, "plot 13 seeds normally");
        }
    }
}
