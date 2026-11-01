using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W8D: timbering supports consume real lumber (W4 demand link) —
    /// demand is computed from the shaft plan, consumption is booked only
    /// with lumber-lot provenance, and untimbered depth stays visible demand
    /// rather than auto-filling.
    /// </summary>
    [TestFixture]
    public sealed class MineTimberingTests
    {
        [Test]
        public void TimberSetsNeeded_ComputesOpenDemandFromShaftPlan()
        {
            var plan = new MineShaftPlan();
            MineShaft shaft = plan.PlanShaft("No. 1 Shaft", MineralResourceKind.Coal, 100, null);
            var diag = new List<string>();
            shaft.RecordSinking(40, 25, 30000, 400, diag);

            int needed = MineTimberingLedger.TimberSetsNeeded(shaft);
            Assert.AreEqual(15 * MineShaftTaskCatalog.TimberSetsPerSunkFoot, needed,
                "15 untimbered feet × sets per foot");

            Assert.AreEqual(0, MineTimberingLedger.TimberSetsNeeded(null));
        }

        [Test]
        public void RecordTimbering_RequiresLumberProvenance()
        {
            var ledger = new MineTimberingLedger();
            var diag = new List<string>();

            Assert.IsNull(ledger.RecordTimbering("shaft-1", 10, 20, 410, null, diag),
                "anonymous timber refused — must trace to real lumber lots");

            var provenance = new List<string> { "stand S-7 | log lot L-42 | sawmill M-1 | day 395" };
            MineTimberingRecord record = ledger.RecordTimbering("shaft-1", 10, 20, 410, provenance, diag);
            Assert.IsNotNull(record);
            Assert.AreEqual("shaft-1", record.ShaftId);
            Assert.AreEqual(10, record.FeetTimbered);
            Assert.AreEqual(20, record.TimberSetsConsumed);
            Assert.AreEqual(1, record.LumberProvenanceChains.Count);
            Assert.AreEqual(20, ledger.TotalTimberSetsConsumed);
        }

        [Test]
        public void RecordTimbering_DiagnosesRateMismatchWithoutRefusing()
        {
            var ledger = new MineTimberingLedger();
            var diag = new List<string>();
            var provenance = new List<string> { "sawmill M-1 | day 395" };

            int expected = 10 * MineShaftTaskCatalog.TimberSetsPerSunkFoot;
            MineTimberingRecord record = ledger.RecordTimbering("shaft-1", 10, expected + 4, 410, provenance, diag);
            Assert.IsNotNull(record, "foreman's judgment stands — diagnosed, not refused");
            Assert.IsNotEmpty(diag);
        }

        [Test]
        public void RecordTimbering_RefusesZeroSets()
        {
            var ledger = new MineTimberingLedger();
            var diag = new List<string>();
            var provenance = new List<string> { "sawmill M-1 | day 395" };
            Assert.IsNull(ledger.RecordTimbering("shaft-1", 10, 0, 410, provenance, diag),
                "phantom timber refused");
            Assert.AreEqual(0, ledger.Records.Count);
        }

        [Test]
        public void SaveRoundTrip_PreservesTimberingLedger()
        {
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Coal);
            var diag = new List<string>();
            var provenance = new List<string> { "stand S-7 | log lot L-42 | sawmill M-1 | day 395" };
            state.TimberingLedger.RecordTimbering("shaft-1", 10, 20, 410, provenance, diag);

            var dto = state.CaptureSaveDto();
            MineRuntimeState restored = MineRuntimeState.FromSaveDto(dto);

            Assert.AreEqual(1, restored.TimberingLedger.Records.Count);
            Assert.AreEqual(20, restored.TimberingLedger.TotalTimberSetsConsumed);
            Assert.AreEqual("stand S-7 | log lot L-42 | sawmill M-1 | day 395",
                restored.TimberingLedger.Records[0].LumberProvenanceChains[0]);
        }
    }
}
