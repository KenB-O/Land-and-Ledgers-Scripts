using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Valuation;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.World
{
    /// <summary>R47 NPC seeding (note 136 §13 item 3, plan 04, note 143): NPC
    /// parcels get appreciation states seeded from their holdings' recorded
    /// acquisition prices, on a second list beside the player list, so the
    /// single assessed-value authority answers a live estimate for NPC plots
    /// and the NPC levy moves off the fallback. The manager-side change is
    /// source-reviewed only (AcquisitionMarketManager.cs is not compiled by
    /// the Roslyn harness); these tests run in a scratch build only, and the
    /// file is excluded from sources.txt (see plan 04 §6).</summary>
    [TestFixture]
    public sealed class R47NpcAppreciationSeedingTests
    {
        private sealed class Fixture : IDisposable
        {
            private readonly GameObject root;
            private readonly TownGenerationSettings settings;

            public TownWorldController TownWorld { get; }
            public AcquisitionMarketManager Market { get; }
            public NpcPropertyLifeService Life { get; } = new NpcPropertyLifeService();
            public EntityIdRegistry Ids { get; } = new EntityIdRegistry();
            public FinancialObligationAuthority Authority { get; } = new FinancialObligationAuthority();
            public CreditCashBridge Cash { get; } = new CreditCashBridge();
            public List<string> Diag { get; } = new List<string>();

            private Fixture(GameObject root, TownGenerationSettings settings,
                TownWorldController townWorld, AcquisitionMarketManager market)
            {
                this.root = root;
                this.settings = settings;
                TownWorld = townWorld;
                Market = market;
            }

            public static Fixture Create()
            {
                // Note 143 C1: construct the settings in code. AssetDatabase
                // returns null under the harness shim (UnityEditor.cs:79), so
                // loading the .asset would fail every test at Assert.NotNull.
                TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
                GameObject root = new GameObject("R47 NPC Seeding Test World");
                TownWorldController townWorld = root.AddComponent<TownWorldController>();
                AcquisitionMarketManager market = root.AddComponent<AcquisitionMarketManager>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();
                market.Configure(townWorld, null);
                market.RebuildMarket();
                return new Fixture(root, settings, townWorld, market);
            }

            public TownPlot FirstEmptyPlot()
            {
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot != null && plot.buildingId < 0)
                    {
                        return plot;
                    }
                }

                Assert.Fail("No empty plot found in the generated test town.");
                return null;
            }

            public TownPlot SecondEmptyPlot()
            {
                TownPlot first = FirstEmptyPlot();
                for (int i = 0; i < TownWorld.Plots.Count; i++)
                {
                    TownPlot plot = TownWorld.Plots[i];
                    if (plot != null && plot.buildingId < 0 && plot.id != first.id)
                    {
                        return plot;
                    }
                }

                Assert.Fail("No second empty plot found in the generated test town.");
                return null;
            }

            public NpcPropertyHolding RegisterHolding(TownPlot plot, int priceCents, int householdId = 7)
            {
                NpcPropertyHolding holding = Life.RegisterHolding(
                    PlayerTitleBridge.ParcelKeyForPlot(plot.id), "bld-1", householdId, 0, priceCents,
                    "", "", NpcPropertyHoldingKind.OwnerOccupied, Diag);
                Assert.IsNotNull(holding);
                return holding;
            }

            public NpcPropertyLifeReport Drive(CivicTreasuryService treasury, int day, Func<int, int> resolver)
            {
                return Life.DriveNpcPropertyLife(Cash, Life.Taxes, Authority, treasury, day,
                    Diag, null, Ids, null, resolver);
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            }
        }

        private static bool DiagContains(Fixture f, string substring)
        {
            foreach (string line in f.Diag)
            {
                if (line != null && line.Contains(substring)) return true;
            }
            return false;
        }

        [Test]
        public void S1_SeedFromHoldingCreatesStateWithRecordedPrice()
        {
            using Fixture f = Fixture.Create();
            TownPlot plot = f.FirstEmptyPlot();
            f.RegisterHolding(plot, 100000);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);

            Assert.AreEqual(100000, f.Market.GetAssessedValueCentsForPlot(plot.id),
                "a seeded NPC state answers the recorded price");
            AcquisitionSaveDto dto = f.Market.CaptureSaveDto();
            Assert.AreEqual(1, dto.npcLandAppreciations.Count, "exactly one NPC state persisted");
            Assert.AreEqual(plot.id, dto.npcLandAppreciations[0].plotId);
            Assert.AreEqual(100000, dto.npcLandAppreciations[0].purchaseBasisCents);
            Assert.AreEqual(100000, dto.npcLandAppreciations[0].currentEstimatedValueCents);
        }

        [Test]
        public void S2_SecondSeedCreatesNothing()
        {
            using Fixture f = Fixture.Create();
            TownPlot plot = f.FirstEmptyPlot();
            f.RegisterHolding(plot, 100000);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);

            Assert.AreEqual(1, f.Market.CaptureSaveDto().npcLandAppreciations.Count,
                "a second seed creates nothing");
            Assert.AreEqual(100000, f.Market.GetAssessedValueCentsForPlot(plot.id));
        }

        [Test]
        public void S3_NonPlotParcelSeedsNothing()
        {
            using Fixture f = Fixture.Create();
            f.Life.RegisterHolding("parcel-1", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);

            Assert.AreEqual(0, f.Market.CaptureSaveDto().npcLandAppreciations.Count,
                "a non-plot parcel id seeds nothing");
            var treasury = new CivicTreasuryService();
            NpcPropertyLifeReport day10 = f.Drive(treasury, 10, f.Market.GetAssessedValueCentsForPlot);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1500, f.Life.Taxes.AllAssessments[0].AmountCents,
                "levy falls back to the 100000c acquisition price");
            Assert.IsTrue(!DiagContains(f, "no appreciation state on file"),
                "the resolver is never consulted for a non-plot parcel, so no fallback diagnostic fires");
        }

        [Test]
        public void S4_PlotKeyWithNoLivePlotSeedsNothing()
        {
            using Fixture f = Fixture.Create();
            int missingPlotId = int.MaxValue;
            f.Life.RegisterHolding(PlayerTitleBridge.ParcelKeyForPlot(missingPlotId), "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);

            Assert.AreEqual(0, f.Market.CaptureSaveDto().npcLandAppreciations.Count,
                "a plot key naming no plot seeds nothing");
            var treasury = new CivicTreasuryService();
            NpcPropertyLifeReport day10 = f.Drive(treasury, 10, f.Market.GetAssessedValueCentsForPlot);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1500, f.Life.Taxes.AllAssessments[0].AmountCents, "levy falls back unchanged");
            Assert.IsTrue(DiagContains(f, "no appreciation state on file"),
                "the key parses but no state exists, so the fallback diagnostic fires");
        }

        [Test]
        public void S5_RefreshPrunesStateWhoseHoldingWasRemoved()
        {
            using Fixture f = Fixture.Create();
            TownPlot plot = f.FirstEmptyPlot();
            f.RegisterHolding(plot, 100000);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);
            Assert.AreEqual(1, f.Market.CaptureSaveDto().npcLandAppreciations.Count);

            f.Market.RefreshNpcLandAppreciations(new List<NpcPropertyHolding>(), 10);

            Assert.AreEqual(0, f.Market.CaptureSaveDto().npcLandAppreciations.Count,
                "refresh with no live holding for the plot prunes the state");
            Assert.AreEqual(0, f.Market.GetAssessedValueCentsForPlot(plot.id));
        }

        [Test]
        public void S6_RefreshNeverTouchesPlayerList()
        {
            using Fixture f = Fixture.Create();
            TownPlot playerPlot = f.FirstEmptyPlot();
            playerPlot.playerOwned = true;
            TownPlot npcPlot = f.SecondEmptyPlot();
            f.RegisterHolding(npcPlot, 100000);

            int totalBefore = f.Market.CalculateOwnedAssetValueCents();
            Assert.AreEqual(1, f.Market.OwnedLandAppreciations.Count,
                "the player refresh backfills exactly one player state");
            Assert.IsTrue(totalBefore > 0, "the player total is non-trivial");
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);
            Assert.AreEqual(100000, f.Market.GetAssessedValueCentsForPlot(npcPlot.id),
                "NPC state seeded");

            int totalAfter = f.Market.CalculateOwnedAssetValueCents();
            Assert.AreEqual(100000, f.Market.GetAssessedValueCentsForPlot(npcPlot.id),
                "the player refresh does not prune the NPC state");
            int expectedPlayerTotal = 0;
            foreach (LandAppreciationState state in f.Market.OwnedLandAppreciations)
            {
                expectedPlayerTotal += Math.Max(state.currentEstimatedValueCents, state.TotalBasisCents);
            }
            Assert.AreEqual(expectedPlayerTotal, totalAfter, "the player total counts only player states");
            Assert.AreEqual(totalBefore, totalAfter, "the player total is unchanged by the NPC seed");
        }

        [Test]
        public void S7_PlayerAssetTotalUnchangedByNpcSeeding()
        {
            using Fixture f = Fixture.Create();
            TownPlot playerPlot = f.FirstEmptyPlot();
            playerPlot.playerOwned = true;
            TownPlot npcPlot = f.SecondEmptyPlot();
            f.RegisterHolding(npcPlot, 100000);

            int totalBefore = f.Market.CalculateOwnedAssetValueCents();
            Assert.IsTrue(totalBefore > 0, "the player total is non-trivial before seeding");
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);
            int totalAfter = f.Market.CalculateOwnedAssetValueCents();

            Assert.AreEqual(totalBefore, totalAfter,
                "seeding NPC states never changes the player asset total");
        }

        [Test]
        public void S8_SaveLoadRoundTripPreservesNpcStates()
        {
            using Fixture f = Fixture.Create();
            TownPlot plot = f.FirstEmptyPlot();
            f.RegisterHolding(plot, 100000);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);
            AcquisitionSaveDto dto = f.Market.CaptureSaveDto();
            Assert.AreEqual(1, dto.npcLandAppreciations.Count);

            using Fixture g = Fixture.Create();
            g.Market.LoadFromSaveDto(dto);
            Assert.AreEqual(100000, g.Market.GetAssessedValueCentsForPlot(plot.id),
                "the restored NPC state answers the recorded price");
            _ = g.Market.CalculateOwnedAssetValueCents();
            Assert.AreEqual(100000, g.Market.GetAssessedValueCentsForPlot(plot.id),
                "the player refresh does not prune the restored NPC state");
        }

        [Test]
        public void S9_ZeroPriceHoldingSeedsWithFloor()
        {
            using Fixture f = Fixture.Create();
            TownPlot plot = f.FirstEmptyPlot();
            f.RegisterHolding(plot, 0);
            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);

            AcquisitionSaveDto dto = f.Market.CaptureSaveDto();
            Assert.AreEqual(1, dto.npcLandAppreciations.Count);
            Assert.AreEqual(1, dto.npcLandAppreciations[0].purchaseBasisCents,
                "a zero price seeds at the state's own floor, never at 0");
            Assert.AreEqual(1, f.Market.GetAssessedValueCentsForPlot(plot.id),
                "the resolver answers the 1c floor");

            var treasury = new CivicTreasuryService();
            NpcPropertyLifeReport day10 = f.Drive(treasury, 10, f.Market.GetAssessedValueCentsForPlot);
            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1, f.Life.Taxes.AllAssessments[0].AmountCents, "1c assessed levies 1c");
            Assert.IsTrue(!DiagContains(f, "no appreciation state on file"),
                "a seeded state means no fallback diagnostic");
        }

        [Test]
        public void S10_LevyMovesOffFallbackAfterSeeding()
        {
            using Fixture f = Fixture.Create();
            TownPlot plot = f.FirstEmptyPlot();
            f.RegisterHolding(plot, 100000);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport year0 = f.Drive(treasury, 10, f.Market.GetAssessedValueCentsForPlot);
            Assert.AreEqual(1, year0.TaxesLevied);
            Assert.AreEqual(1500, f.Life.Taxes.AllAssessments[0].AmountCents,
                "before seeding the levy falls back to the 100000c price");
            Assert.IsTrue(DiagContains(f, "no appreciation state on file"),
                "the fallback diagnostic fires before seeding");

            f.Market.SeedNpcLandAppreciations(f.Life.Holdings, 10);
            f.Market.RefreshNpcLandAppreciations(f.Life.Holdings, 10);
            int liveAnswer = f.Market.GetAssessedValueCentsForPlot(plot.id);
            Assert.IsTrue(liveAnswer > 0, "after seeding the resolver answers a live estimate");

            f.Diag.Clear();
            NpcPropertyLifeReport year1 = f.Drive(treasury, 375, f.Market.GetAssessedValueCentsForPlot);
            Assert.AreEqual(1, year1.TaxesLevied);
            Assert.AreEqual(PropertyTaxCalibration.ComputeLevyCents(liveAnswer),
                f.Life.Taxes.AllAssessments[1].AmountCents,
                "the year-1 levy uses the live estimate, not a hardcoded evaluator value");
            Assert.IsTrue(!DiagContains(f, "no appreciation state on file"),
                "no fallback diagnostic after seeding");

            NpcPropertyLifeReport year1Again = f.Drive(treasury, 380, f.Market.GetAssessedValueCentsForPlot);
            Assert.AreEqual(0, year1Again.TaxesLevied, "exactly once per tax year");
            Assert.AreEqual(2, f.Life.Taxes.AllAssessments.Count);
        }
    }
}
