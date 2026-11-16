using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D-0 (expanded campaign, 2026-11-16): diagnostic-only instrumentation
    /// on the NPC construction need evaluator. The evaluator's single "no
    /// evidenced need" count is split three ways (no-occupancy-data,
    /// suitably-housed, no-sharing) and every household skipped for no
    /// evidenced need gets one trace line naming its home building,
    /// occupancy count, provided sleeping places, gap, uncovered persons,
    /// parcels held and cash — so one Unity run distinguishes H1 (system
    /// blind, zero occupancies) from H2 (suitably housed, zero genuinely
    /// correct). No behaviour change: the queue conditions are identical,
    /// and these tests pin the queued/skipped outcomes alongside the new
    /// diagnostics. Fixture reuses the DEF-011 evaluator patterns.
    /// </summary>
    [TestFixture]
    public sealed class NpcConstructionNeedEvaluatorDiagnosticsTests
    {
        private sealed class FakePricePort : IConstructionMaterialPricePort
        {
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int UnitPriceCents(ConstructionMaterialRequirement requirement)
            {
                if (requirement == null) return -1;
                return Prices.TryGetValue(requirement.MaterialDisplayName, out int price) ? price : -1;
            }
        }

        private sealed class EvaluatorFixture
        {
            public List<string> Diag = new List<string>();
            public BuildingDesignCatalog Catalog = new BuildingDesignCatalog();
            public FakePricePort Prices = new FakePricePort();
            public CreditCashBridge Cash = new CreditCashBridge();
            public HousingAuthority Housing = new HousingAuthority();
            public TitleAuthority Titles = new TitleAuthority();
            public NpcConstructionCommissioner Commissioner = new NpcConstructionCommissioner();
            public Dictionary<int, PersonState> People = new Dictionary<int, PersonState>();
            public Dictionary<int, List<int>> MembersByHousehold = new Dictionary<int, List<int>>();

            public EvaluatorFixture()
            {
                Prices.Prices["fieldstone"] = 50;
                Prices.Prices["lumber"] = 40;
            }

            public void FundHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
            }

            public void AddAdult(int personId)
            {
                People[personId] = new PersonState { id = personId, age = 30, deathDayIndex = -1 };
            }

            public void RegisterParcel(string parcelId, int householdId)
            {
                var pdiag = new List<string>();
                Assert.IsNotNull(Titles.RegisterParcel(parcelId, "lot", 1f,
                    "household:" + householdId, TitleBasis.Grant, 0, pdiag),
                    string.Join("; ", pdiag));
            }

            public void RegisterCabinDesigns()
            {
                var diag = new List<string>();
                var cabin = new BuildingDesign
                {
                    DesignId = "cabin-a", DisplayName = "Settler cabin",
                    WorksKind = ConstructionWorksKind.Unspecified, KindLabel = "cabin",
                };
                var foundation = new BuildingDesignPhase
                {
                    PhaseName = "Foundation", Sequence = 0, LaborMinutes = 240,
                };
                foundation.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-fieldstone", MaterialKind = ConstructionMaterialKind.Masonry,
                    MaterialDisplayName = "fieldstone", RequiredUnits = 40, UnitLabel = "perch",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                var framing = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 1, LaborMinutes = 480,
                };
                framing.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Lumber,
                    MaterialDisplayName = "lumber", RequiredUnits = 600, UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                cabin.Phases.Add(foundation);
                cabin.Phases.Add(framing);
                cabin.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(cabin, diag), string.Join("; ", diag));

                var small = new BuildingDesign
                {
                    DesignId = "cabin-small", DisplayName = "Small cabin",
                    WorksKind = ConstructionWorksKind.Unspecified, KindLabel = "cabin",
                };
                var smallFraming = new BuildingDesignPhase
                {
                    PhaseName = "Framing", Sequence = 0, LaborMinutes = 240,
                };
                smallFraming.Materials.Add(new ConstructionMaterialRequirement
                {
                    RequirementId = "REQ-lumber", MaterialKind = ConstructionMaterialKind.Lumber,
                    MaterialDisplayName = "lumber", RequiredUnits = 200, UnitLabel = "board feet",
                    ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                });
                small.Phases.Add(smallFraming);
                small.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
                Assert.IsNull(Catalog.RegisterDesign(small, diag), string.Join("; ", diag));
            }

            public void OccupyHousehold(int householdId, List<int> personIds, int sleepingCapacity)
            {
                var hdiag = new List<string>();
                Assert.IsNotNull(Housing.RegisterBuilding("bld-h" + householdId, "parcel-9",
                    "house", 0, "test", hdiag), string.Join("; ", hdiag));
                AccommodationSpace space = Housing.DefineSpace("bld-h" + householdId, sleepingCapacity, hdiag);
                Assert.IsNotNull(space, string.Join("; ", hdiag));
                foreach (int personId in personIds)
                    Assert.IsNotNull(Housing.Occupy(personId, householdId, space.SpaceId,
                        AccommodationArrangement.OwnerOccupied, 0, hdiag),
                        string.Join("; ", hdiag));
            }

            public NpcConstructionNeedEvaluator NewEvaluator()
            {
                return NewEvaluatorWithHousing(Housing);
            }

            public NpcConstructionNeedEvaluator NewEvaluatorWithHousing(HousingAuthority housing)
            {
                return new NpcConstructionNeedEvaluator(
                    Commissioner, Catalog, Prices, Cash, housing, Titles,
                    householdId => MembersByHousehold.TryGetValue(householdId, out List<int> members)
                        ? members : new List<int>(),
                    personId => People.TryGetValue(personId, out PersonState person) ? person : null,
                    householdId => false,
                    householdId => false);
            }

            public static HouseholdState Household(int id, int homeBuildingId)
            {
                return new HouseholdState
                {
                    id = id,
                    homeBuildingId = homeBuildingId,
                    lifecycleState = HouseholdLifecycleState.Active,
                };
            }
        }

        private static string JoinedDiag(List<string> diag)
        {
            return string.Join("\n", diag);
        }

        private static string SummaryLine(List<string> diag)
        {
            // The per-household trace lines also start with
            // "NpcConstructionNeedEvaluator day", so match the summary by
            // its scan count, not by the day prefix.
            foreach (string line in diag)
                if (line.Contains("scanned ") && line.Contains("active household(s)"))
                    return line;
            return string.Empty;
        }

        private static List<string> TraceLines(List<string> diag)
        {
            var traces = new List<string>();
            foreach (string line in diag)
                if (line.Contains("no evidenced need -"))
                    traces.Add(line);
            return traces;
        }

        [Test]
        public void Diagnostics_NoOccupancyData_SplitCountsNoOccupancyAndTraceNamesParcelsAndCash()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102);
            f.MembersByHousehold[7] = new List<int> { 101, 102 };
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            f.AddAdult(103);
            f.MembersByHousehold[8] = new List<int> { 103 };
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState>
                {
                    EvaluatorFixture.Household(7, 5),
                    EvaluatorFixture.Household(8, 0),
                }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, JoinedDiag(f.Diag));
            string summary = SummaryLine(f.Diag);
            Assert.IsTrue(summary.Contains("scanned 2 active household(s), queued 0 request(s)"),
                "summary still reports the scan: " + summary);
            Assert.IsTrue(summary.Contains("no evidenced need 2 (no-occupancy-data 2, suitably-housed 0, no-sharing 0)"),
                "the split names the H1 case: " + summary);
            List<string> traces = TraceLines(f.Diag);
            Assert.AreEqual(2, traces.Count, JoinedDiag(f.Diag));
            Assert.IsTrue(traces[0].Contains("H7 no evidenced need - home building 5, members 2, occupancies 0, provided 0 sleeping place(s), gap 2, uncovered 2 person(s), parcels held 1, cash 30000c"),
                "the funded, parcel-holding household's trace names parcels and cash: " + traces[0]);
            Assert.IsTrue(traces[0].Contains("(overcrowding: no occupancy data; formation: no shared building)"),
                "the trace names both failed signals: " + traces[0]);
            Assert.IsTrue(traces[1].Contains("H8 no evidenced need - home building 0, members 1, occupancies 0, provided 0 sleeping place(s), gap 1, uncovered 1 person(s), parcels held 0, cash 0c"),
                "the unfunded household's trace names zero parcels and zero cash: " + traces[1]);
            Assert.IsTrue(traces[1].Contains("(overcrowding: no occupancy data; formation: no home building)"),
                "the trace names the no-home-building formation outcome: " + traces[1]);
        }

        [Test]
        public void Diagnostics_SuitablyHousedAlone_SplitCountsNoSharing()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102);
            f.MembersByHousehold[7] = new List<int> { 101, 102 };
            f.OccupyHousehold(7, new List<int> { 101, 102 }, 2);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, JoinedDiag(f.Diag));
            string summary = SummaryLine(f.Diag);
            Assert.IsTrue(summary.Contains("no evidenced need 1 (no-occupancy-data 0, suitably-housed 0, no-sharing 1)"),
                "a suitably housed household living alone counts as no-sharing: " + summary);
            List<string> traces = TraceLines(f.Diag);
            Assert.AreEqual(1, traces.Count, JoinedDiag(f.Diag));
            Assert.IsTrue(traces[0].Contains("H7 no evidenced need - home building 5, members 2, occupancies 2, provided 2 sleeping place(s), gap 0, uncovered 0 person(s)"),
                "the trace shows real occupancies covering the requirement: " + traces[0]);
            Assert.IsTrue(traces[0].Contains("(overcrowding: suitable; formation: no shared building)"),
                "the trace names the suitable outcome: " + traces[0]);
        }

        [Test]
        public void Diagnostics_SuitablyHousedSharingNotSmallest_CountsSuitablyHousedAndQueuedHouseholdHasNoTrace()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102 };
            f.MembersByHousehold[8] = new List<int> { 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102 }, 2);
            f.OccupyHousehold(8, new List<int> { 103 }, 2);
            f.RegisterParcel("parcel-7", 7);
            f.RegisterParcel("parcel-8", 8);
            f.FundHousehold(7, 30000);
            f.FundHousehold(8, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState>
                {
                    EvaluatorFixture.Household(7, 5),
                    EvaluatorFixture.Household(8, 5),
                }, 10, 400, f.Diag);

            Assert.AreEqual(1, requests.Count, JoinedDiag(f.Diag));
            Assert.AreEqual(8, requests[0].HouseholdId,
                "the smaller offshoot household still queues; the established household keeps the home");
            Assert.AreEqual(NpcConstructionReason.HouseholdFormation, requests[0].Reason);
            string summary = SummaryLine(f.Diag);
            Assert.IsTrue(summary.Contains("queued 1 request(s)"),
                "the queue outcome is unchanged: " + summary);
            Assert.IsTrue(summary.Contains("no evidenced need 1 (no-occupancy-data 0, suitably-housed 1, no-sharing 0)"),
                "the sharing-but-not-smallest household counts as suitably-housed: " + summary);
            List<string> traces = TraceLines(f.Diag);
            Assert.AreEqual(1, traces.Count, JoinedDiag(f.Diag));
            Assert.IsTrue(traces[0].Contains("H7 no evidenced need - home building 5, members 2, occupancies 2, provided 2 sleeping place(s), gap 0, uncovered 0 person(s)"),
                "only the skipped household gets a trace: " + traces[0]);
            Assert.IsTrue(traces[0].Contains("(overcrowding: suitable; formation: shares a home but is not the smallest household)"),
                "the trace names why formation did not fire for the established household: " + traces[0]);
            Assert.IsFalse(JoinedDiag(f.Diag).Contains("H8 no evidenced need"),
                "the queued household gets no no-evidence trace: " + JoinedDiag(f.Diag));
        }

        [Test]
        public void Diagnostics_OvercrowdingStillQueuesAndSummaryKeepsOldTokens()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101); f.AddAdult(102); f.AddAdult(103);
            f.MembersByHousehold[7] = new List<int> { 101, 102, 103 };
            f.OccupyHousehold(7, new List<int> { 101, 102, 103 }, 2);
            f.RegisterParcel("parcel-9", 7);
            f.FundHousehold(7, 30000);
            var evaluator = f.NewEvaluator();

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(1, requests.Count, JoinedDiag(f.Diag));
            Assert.AreEqual(7, requests[0].HouseholdId);
            Assert.AreEqual("cabin-small", requests[0].DesignId);
            Assert.AreEqual(NpcConstructionReason.Overcrowding, requests[0].Reason);
            Assert.IsTrue(requests[0].ReasonNotes.Contains("overcrowded by 1"),
                "the queued notes are unchanged: " + requests[0].ReasonNotes);
            string summary = SummaryLine(f.Diag);
            Assert.IsTrue(summary.Contains("queued 1 request(s)"), "summary: " + summary);
            Assert.IsTrue(summary.Contains("live/pending 0, no parcel 0, cannot fund 0, no design 0, shipment-blocked 0"),
                "the old skip tokens are unchanged: " + summary);
            Assert.IsTrue(summary.Contains("no evidenced need 0 (no-occupancy-data 0, suitably-housed 0, no-sharing 0), no members 0"),
                "the split sums to the old total: " + summary);
            Assert.AreEqual(0, TraceLines(f.Diag).Count,
                "a queued household produces no no-evidence trace: " + JoinedDiag(f.Diag));
        }

        [Test]
        public void Diagnostics_ProbeUnavailable_CountsNoOccupancyData()
        {
            var f = new EvaluatorFixture();
            f.RegisterCabinDesigns();
            f.AddAdult(101);
            f.MembersByHousehold[7] = new List<int> { 101 };
            var evaluator = f.NewEvaluatorWithHousing(null);

            List<NpcConstructionRequest> requests = evaluator.EvaluateDay(
                new List<HouseholdState> { EvaluatorFixture.Household(7, 5) }, 10, 400, f.Diag);

            Assert.AreEqual(0, requests.Count, JoinedDiag(f.Diag));
            string summary = SummaryLine(f.Diag);
            Assert.IsTrue(summary.Contains("no evidenced need 1 (no-occupancy-data 1, suitably-housed 0, no-sharing 0)"),
                "an unrunnable probe counts as no occupancy data: " + summary);
            List<string> traces = TraceLines(f.Diag);
            Assert.AreEqual(1, traces.Count, JoinedDiag(f.Diag));
            Assert.IsTrue(traces[0].Contains("H7 no evidenced need - home building 5, members 1, occupancies 0, provided 0 sleeping place(s), gap 0, uncovered 1 person(s), parcels held 0, cash 0c"),
                "the trace reports the evidenced (zero) values: " + traces[0]);
            Assert.IsTrue(traces[0].Contains("(overcrowding: probe unavailable; formation: no shared building)"),
                "the trace names the unavailable probe: " + traces[0]);
        }
    }
}
