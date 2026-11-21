// Land & Ledgers — Supply Chain Dependency Explorer tests (Lane 7, Tool 05).
//
// Known cases reproduced from the current tree's source-reviewed
// evidence (no run exists on this HEAD): the four chains, the first
// supported blocker rule, and the named breaks (F7-2, D-18, D-04,
// day-41 fuel death, F1 pool/lot freeze).

using System.Collections.Generic;
using System.Linq;
using LandLedgers.Diagnostics;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Diagnostics
{
    public sealed class SupplyChainDependencyExplorerTests
    {
        private static SupplyNodeObservation Observe(string nodeId, SupplyNodeState state, int stock, string note)
        {
            return new SupplyNodeObservation { NodeId = nodeId, State = state, StockUnits = stock, Note = note ?? string.Empty };
        }

        private static List<SupplyNodeObservation> AllPresent(string goodId)
        {
            var observations = new List<SupplyNodeObservation>();
            foreach (SupplyChainNode node in SupplyChainDependencyExplorer.KnownChain(goodId))
            {
                observations.Add(Observe(node.Id, SupplyNodeState.Present, 10, string.Empty));
            }
            return observations;
        }

        [Test]
        public void KnownChains_HaveFourGoods_WithExpectedNodeCounts()
        {
            Assert.AreEqual(4, SupplyChainDependencyExplorer.Goods.Length);
            Assert.AreEqual(4, SupplyChainDependencyExplorer.KnownChain("flour").Count);
            Assert.AreEqual(5, SupplyChainDependencyExplorer.KnownChain("fuel").Count);
            Assert.AreEqual(6, SupplyChainDependencyExplorer.KnownChain("timber").Count);
            Assert.AreEqual(6, SupplyChainDependencyExplorer.KnownChain("bread").Count);
            Assert.AreEqual(0, SupplyChainDependencyExplorer.KnownChain("gold").Count);
        }

        [Test]
        public void Trace_Flour_EmptyBakeryBin_NamesFlourStockAuthority()
        {
            var observations = new List<SupplyNodeObservation>
            {
                Observe("flour-millbrook", SupplyNodeState.Present, 100, string.Empty),
                Observe("flour-store-procurement", SupplyNodeState.Present, 50, string.Empty),
                Observe("flour-bakery-bin", SupplyNodeState.Empty, 0, string.Empty),
            };
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace("flour", observations);
            Assert.IsFalse(trace.Clean);
            Assert.AreEqual("flour-bakery-bin", trace.BlockerNode.Id);
            Assert.IsTrue(trace.Authority.Contains("BakeryShopRuntime.FlourStock"));
            Assert.AreEqual(IncidentSeverity.Warning, trace.Severity);
        }

        [Test]
        public void Trace_Fuel_DealerAbsent_NamesD18Break()
        {
            var observations = new List<SupplyNodeObservation>
            {
                Observe("fuel-sawmill-byproduct", SupplyNodeState.Present, 40, string.Empty),
                Observe("fuel-dealer-settlement", SupplyNodeState.Present, 40, string.Empty),
                Observe("fuel-dealer-stock", SupplyNodeState.Absent, -1, "No dealer runtime bound."),
            };
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace("fuel", observations);
            Assert.IsFalse(trace.Clean);
            Assert.AreEqual("fuel-dealer-stock", trace.BlockerNode.Id);
            Assert.IsTrue(trace.Cause.Contains("D-18"));
        }

        [Test]
        public void Trace_Fuel_ByproductRefusal_IsCritical_NamingSf7()
        {
            var observations = new List<SupplyNodeObservation>
            {
                Observe("fuel-sawmill-byproduct", SupplyNodeState.Refused, -1, "Settlement failed after custody moved."),
            };
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace("fuel", observations);
            Assert.IsFalse(trace.Clean);
            Assert.AreEqual(IncidentSeverity.Critical, trace.Severity);
            Assert.IsTrue(trace.Cause.Contains("SF-7"));
        }

        [Test]
        public void Trace_Timber_SupportNodeOnlyStock_NamesF7_2Break()
        {
            // E-13/F7-2: the support node writes category stock the lot-based yard transfer never reads.
            // The yard-transfer node observed Present with lot stock 0 is NOT the break the trace names;
            // the break is recorded on the transfer leg itself — verify the chain carries it.
            SupplyChainNode transfer = SupplyChainDependencyExplorer.KnownChain("timber")
                .First(n => n.Id == "timber-yard-transfer");
            Assert.IsTrue(transfer.KnownBreak.Contains("F7-2"));
            Assert.IsTrue(transfer.Authority.Contains("SawmillToYardTransferDriver"));
        }

        [Test]
        public void Trace_Bread_AllPresent_IsClean_ButNeverClaimsFlow()
        {
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace("bread", AllPresent("bread"));
            Assert.IsTrue(trace.Clean);
            Assert.IsTrue(trace.Cause.Contains("NOT proof the chain flows"));
        }

        [Test]
        public void Trace_StopsAtFirstBlocker_NotLaterOnes()
        {
            var observations = new List<SupplyNodeObservation>
            {
                Observe("bread-flour", SupplyNodeState.Empty, 0, string.Empty),
                Observe("bread-fuel", SupplyNodeState.Empty, 0, string.Empty),
            };
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace("bread", observations);
            Assert.AreEqual("bread-flour", trace.BlockerNode.Id);
        }

        [Test]
        public void Trace_UnknownNodes_AreSkipped_NeverBlockers()
        {
            var observations = new List<SupplyNodeObservation>
            {
                Observe("bread-flour", SupplyNodeState.Unknown, -1, string.Empty),
                Observe("bread-fuel", SupplyNodeState.Unknown, -1, string.Empty),
            };
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace("bread", observations);
            Assert.IsTrue(trace.Clean);
        }

        [Test]
        public void Trace_UnknownGood_IsUnknown_NeverClean()
        {
            SupplyChainTrace trace = SupplyChainDependencyExplorer.Trace("gold", new List<SupplyNodeObservation>());
            Assert.IsFalse(trace.Clean);
            Assert.AreEqual(IncidentSeverity.Unknown, trace.Severity);
        }

        [Test]
        public void BreadChain_CarriesD04AndDay41Breaks()
        {
            IReadOnlyList<SupplyChainNode> chain = SupplyChainDependencyExplorer.KnownChain("bread");
            Assert.IsTrue(chain.First(n => n.Id == "bread-staged-batches").KnownBreak.Contains("D-04"));
            Assert.IsTrue(chain.First(n => n.Id == "bread-fuel").KnownBreak.Contains("day 41"));
            Assert.IsTrue(chain.First(n => n.Id == "bread-retail").Evidence.Contains("8c"));
        }
    }
}
