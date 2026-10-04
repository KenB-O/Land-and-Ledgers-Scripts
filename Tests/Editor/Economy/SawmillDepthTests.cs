using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Tasks;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2B: sawmill depth — the blade as real equipment (procurement
    /// provenance, spare inventory, re-set vs file, retirement to scrap),
    /// run labor minutes, green-lumber stacking day, and the toll-sawing
    /// commercial form (customer logs in custody, explicit toll policy).
    /// Written, not run (no Unity in this environment).
    /// </summary>
    [TestFixture]
    public sealed class SawmillDepthTests
    {
        private static LogLot DocumentedLogLot(int lotNumber, int logs, string standId, string species = "white pine")
        {
            return new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LogUnits = logs,
                StandId = standId,
                Species = species,
                FelledBy = EntityId.For(EntityKind.Person, 5001),
                FelledDayIndex = 200,
            };
        }

        private static SawmillShopRuntime MillWithLogs(int logs)
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9101, logs, "stand-1"), diag));
            return mill;
        }

        private static SawmillBladeState ProcuredSpare(string bladeId, string source)
        {
            var blade = new SawmillBladeState(bladeId);
            string refusal = blade.RecordProcurement(source, 200, false, new List<string>());
            Assert.IsNull(refusal, "Test helper: spare procurement must be accepted.");
            return blade;
        }

        // ---- Blade procurement provenance ----

        [Test]
        public void Blade_Procurement_Requires_NamedSource_Or_Bootstrap()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();

            string refusal = mill.RecordBladeProcurement(string.Empty, 200, false, diag);
            Assert.NotNull(refusal, "Anonymous blade procurement must be refused.");

            Assert.IsNull(mill.RecordBladeProcurement("Hawkins Machine Works, Deadwood", 200, false, diag));
            Assert.AreEqual("Hawkins Machine Works, Deadwood", mill.Blade.ProcurementSource);
            Assert.IsFalse(mill.Blade.IsBootstrapEndowment);

            var bootstrapMill = new SawmillShopRuntime("mill-2", new EntityIdRegistry());
            Assert.IsNull(bootstrapMill.RecordBladeProcurement(string.Empty, 0, true, diag));
            Assert.AreEqual("BOOTSTRAP endowment", bootstrapMill.Blade.ProcurementSource);
            Assert.IsTrue(bootstrapMill.Blade.IsBootstrapEndowment);
        }

        [Test]
        public void Blade_Spare_Inventory_Refuses_Anonymous_Blades()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();

            var anonymous = new SawmillBladeState("spare-1"); // no procurement recorded
            Assert.NotNull(mill.ReceiveSpareBlade(anonymous, diag), "Anonymous steel is refused loudly.");
            Assert.AreEqual(0, mill.BladeInventory.Spares.Count);

            Assert.IsNull(mill.ReceiveSpareBlade(ProcuredSpare("spare-1", "Hawkins Machine Works"), diag));
            Assert.AreEqual(1, mill.BladeInventory.Spares.Count);
            Assert.AreEqual("mill-1", mill.BladeInventory.Spares[0].OwnerBusinessId);
        }

        // ---- File vs re-set ----

        [Test]
        public void Blade_Filing_Refuses_BrokenBlade()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            mill.Blade.ApplyWear(0.95f); // condition 0.05 — broken
            Assert.IsTrue(mill.Blade.IsBroken);

            string refusal = mill.RecordSawFiling(EntityId.For(EntityKind.Person, 5003), 211, diag);

            Assert.NotNull(refusal, "A broken saw is never filed back to perfect.");
            Assert.IsTrue(mill.Blade.IsBroken, "Refused filing changes nothing.");
            Assert.IsTrue(string.Join(" ", diag).Contains("re-set"), "The refusal must name the real remedy.");
        }

        [Test]
        public void Blade_Reset_Restores_BrokenBlade_And_Counts()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            mill.Blade.ApplyWear(0.95f);
            Assert.IsTrue(mill.Blade.IsBroken);

            string refusal = mill.RecordSawReset(EntityId.For(EntityKind.Person, 5003), 211, diag);

            Assert.IsNull(refusal);
            Assert.AreEqual(1f, mill.Blade.Condition01);
            Assert.IsFalse(mill.Blade.IsBroken);
            Assert.AreEqual(1, mill.Blade.ResetCount, "Re-sets are factual history on the blade.");
            Assert.IsTrue(mill.Blade.MaintenanceLog[0].Contains("re-set"));

            var result = mill.RunSawing(2, EntityId.For(EntityKind.Person, 5002), 211, diag);
            Assert.NotNull(result, "Output resumes after the re-set.");
        }

        [Test]
        public void Blade_Reset_Refuses_NonBrokenBlade()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            mill.Blade.ApplyWear(0.70f); // dull, not broken

            string refusal = mill.RecordSawReset(EntityId.For(EntityKind.Person, 5003), 211, diag);

            Assert.NotNull(refusal, "Dull saws are filed, not re-set.");
            Assert.AreEqual(0, mill.Blade.ResetCount);
        }

        [Test]
        public void MaintenanceTask_ReSetSaw_Registers_WithWorkstationGate()
        {
            var authority = new TaskAuthority();
            var diag = new List<string>();

            SawmillMaintenanceTasks.RegisterTaskDefinitions(authority, diag);

            var def = authority.GetDefinition(SawmillMaintenanceTasks.ReSetSawTaskId);
            Assert.NotNull(def, "re-set-saw is registered.");
            Assert.AreEqual(120, def.BaseMinutes, "Re-setting is heavier work than filing (45).");
            Assert.IsTrue(def.EquipmentClasses.Contains("workstation:sawmill-saw-line"),
                "Re-setting executes against the mill's saw line.");
            Assert.NotNull(authority.GetDefinition(SawmillMaintenanceTasks.FileSawTaskId),
                "file-saw still registers alongside.");
        }

        // ---- Spare blades: fit, retire, scrap ----

        [Test]
        public void Blade_FitSpare_Replaces_BrokenBlade_And_Parks_Old()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.RecordBladeProcurement("Hawkins Machine Works", 200, false, diag));
            Assert.IsNull(mill.ReceiveSpareBlade(ProcuredSpare("spare-1", "Hawkins Machine Works"), diag));

            mill.Blade.ApplyWear(0.95f); // fitted blade broken
            Assert.IsNull(mill.RunSawing(2, EntityId.For(EntityKind.Person, 5002), 210, diag),
                "Broken blade gates output before the swap.");

            Assert.IsNull(mill.FitSpareBlade("spare-1", 211, diag));

            Assert.AreEqual("spare-1", mill.Blade.BladeId, "The spare is now the fitted blade.");
            Assert.AreEqual(1f, mill.Blade.Condition01);
            Assert.AreEqual(1, mill.BladeInventory.Spares.Count, "The old blade parks back as a spare.");
            Assert.AreEqual("mill-1-blade", mill.BladeInventory.Spares[0].BladeId);

            var result = mill.RunSawing(2, EntityId.For(EntityKind.Person, 5002), 211, diag);
            Assert.NotNull(result, "Sawing resumes on the fitted spare.");
        }

        [Test]
        public void Blade_FitSpare_Refuses_When_NoSpare_OnHand()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            mill.Blade.ApplyWear(0.95f);

            string refusal = mill.FitSpareBlade("spare-1", 211, diag);

            Assert.NotNull(refusal, "No spare on hand — the mill stays gated, nothing conjured.");
            Assert.IsTrue(string.Join(" ", diag).Contains("no spare blade"),
                "The shortfall is loud and never auto-ordered.");
            Assert.IsNull(mill.RunSawing(2, EntityId.For(EntityKind.Person, 5002), 211, diag));
        }

        [Test]
        public void Blade_Retire_Moves_To_Scrap_And_Gates_Sawing()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();

            Assert.IsNull(mill.RetireFittedBlade("cracked plate", 211, diag));

            Assert.IsTrue(mill.Blade.IsRetired);
            Assert.AreEqual(1, mill.BladeInventory.Scrap.Count, "Identity and history kept in scrap.");
            Assert.AreEqual("cracked plate", mill.BladeInventory.Scrap[0].RetiredReason);
            Assert.IsNull(mill.RunSawing(2, EntityId.For(EntityKind.Person, 5002), 211, diag),
                "A retired blade gates sawing until a spare is fitted.");
            Assert.IsTrue(string.Join(" ", diag).Contains("retired scrap"));
        }

        // ---- Saw minutes + stacking day ----

        [Test]
        public void Sawing_Reports_SawMinutes_PerRun_FromData()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();

            var soft = mill.RunSawing(6, EntityId.For(EntityKind.Person, 5002), 210, diag);
            Assert.NotNull(soft);
            Assert.AreEqual(180, soft.SawMinutes, "6 logs x 30 saw-minutes/log (white pine data).");

            var ids = new EntityIdRegistry();
            var hardMill = new SawmillShopRuntime("mill-2", ids);
            Assert.IsNull(hardMill.ReceiveLogLot(DocumentedLogLot(9102, 10, "stand-2", "oak"), diag));
            var hard = hardMill.RunSawing(4, EntityId.For(EntityKind.Person, 5002), 210, diag);
            Assert.NotNull(hard);
            Assert.AreEqual(180, hard.SawMinutes, "4 logs x 45 saw-minutes/log (oak data).");
        }

        [Test]
        public void Sawing_Records_StackedDay_On_LumberLot()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();

            var result = mill.RunSawing(4, EntityId.For(EntityKind.Person, 5002), 210, diag);

            Assert.NotNull(result);
            Assert.AreEqual(210, result.LumberLot.StackedDayIndex,
                "Green-lumber handling starts at stacking; drying calibration is research-blocked.");
        }

        // ---- Toll sawing ----

        [Test]
        public void TollPolicy_Default_Offers_Nothing()
        {
            var policy = new SawmillTollPolicy();
            Assert.IsFalse(policy.OffersTollSawing, "Toll sawing is refused until the mill names its terms.");

            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 8, "stand-9"), "farmer-1", "A. Farmer", 209, diag));

            var result = mill.RunTollSawing(
                EntityId.For(EntityKind.Lot, 9201).ToString(), 4,
                EntityId.For(EntityKind.Person, 5002), 210, policy, diag);

            Assert.IsNull(result, "Custom work without named terms is refused, never assumed.");
            Assert.AreEqual(8, mill.TollLogStock.TotalCustodyLogUnits, "Refused runs consume nothing.");
        }

        [Test]
        public void TollIntake_Refuses_AnonymousCustomer_And_AnonymousLogs()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();

            Assert.NotNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 8, "stand-9"), string.Empty, string.Empty, 209, diag),
                "Toll logs need a named owner to return to.");

            var anonymous = DocumentedLogLot(9202, 8, string.Empty);
            Assert.NotNull(mill.ReceiveTollLogLot(anonymous, "farmer-1", "A. Farmer", 209, diag),
                "A customer's logs need the same stand provenance as the mill's own.");

            Assert.AreEqual(0, mill.TollLogStock.TotalCustodyLogUnits);
        }

        [Test]
        public void TollSawing_Splits_Output_Customer_Keeps_Minus_Toll()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 8, "stand-9"), "farmer-1", "A. Farmer", 209, diag));
            var policy = new SawmillTollPolicy(tollCashCentsPerLog: 5, tollInKindLumberShare01: 0.25f);

            var result = mill.RunTollSawing(
                EntityId.For(EntityKind.Lot, 9201).ToString(), 8,
                EntityId.For(EntityKind.Person, 5002), 210, policy, diag);

            Assert.NotNull(result);
            Assert.AreEqual(8, result.LogsSawn);
            Assert.AreEqual("farmer-1", result.CustomerId);
            Assert.AreEqual(24, result.CustomerLumberUnits, "32 lumber minus the 8-unit in-kind toll.");
            Assert.AreEqual(8, result.TollInKindLumberUnits);
            Assert.AreEqual(40, result.TollCashCents, "8 logs x 5c — returned for the ledger authority, never posted here.");
            Assert.AreEqual(16, result.CustomerFuelWoodUnits, "Slab/offcut byproducts belong to the customer.");
            Assert.AreEqual(240, result.SawMinutes, "8 logs x 30 saw-minutes/log.");

            Assert.AreEqual(8, mill.LumberStock.TotalLumberUnits, "The in-kind toll is the mill's lumber.");
            Assert.AreEqual(24, mill.TollPickup.LumberUnitsFor("farmer-1"), "The customer's lumber waits in custody, not mill inventory.");
            Assert.AreEqual(16, mill.TollPickup.FuelWoodUnitsFor("farmer-1"));
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits, "The mill's own log yard is untouched by toll work.");
            Assert.AreEqual(0, mill.TollLogStock.TotalCustodyLogUnits, "The toll lot was fully sawn.");
        }

        [Test]
        public void TollSawing_Refuses_When_Blade_Dull()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 8, "stand-9"), "farmer-1", "A. Farmer", 209, diag));
            mill.Blade.ApplyWear(0.70f); // dull
            var policy = new SawmillTollPolicy(tollCashCentsPerLog: 5, tollInKindLumberShare01: 0.25f);

            var result = mill.RunTollSawing(
                EntityId.For(EntityKind.Lot, 9201).ToString(), 4,
                EntityId.For(EntityKind.Person, 5002), 210, policy, diag);

            Assert.IsNull(result, "The §3.9 blade gate applies to toll work too.");
            Assert.AreEqual(8, mill.TollLogStock.TotalCustodyLogUnits, "Gated runs consume nothing.");
        }

        [Test]
        public void TollPickup_Collect_Preserves_Provenance()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 8, "stand-9"), "farmer-1", "A. Farmer", 209, diag));
            var policy = new SawmillTollPolicy(tollCashCentsPerLog: 5, tollInKindLumberShare01: 0.25f);
            Assert.NotNull(mill.RunTollSawing(
                EntityId.For(EntityKind.Lot, 9201).ToString(), 8,
                EntityId.For(EntityKind.Person, 5002), 210, policy, diag));

            var lines = mill.TollPickup.TryCollect("farmer-1", "lumber", 10, diag);

            Assert.AreEqual(10, TotalTaken(lines));
            Assert.AreEqual(14, mill.TollPickup.LumberUnitsFor("farmer-1"));
            Assert.IsTrue(lines[0].ProvenanceChain.Contains("stand-9"), "Provenance survives the customer's collection.");

            var shortfall = mill.TollPickup.TryCollect("farmer-1", "lumber", 100, diag);
            Assert.AreEqual(14, TotalTaken(shortfall), "Shortfalls return fewer lines — never invented units.");
            Assert.AreEqual(0, mill.TollPickup.LumberUnitsFor("farmer-1"));
        }

        private static int TotalTaken(List<SawmillTollDispenseLine> lines)
        {
            int total = 0;
            foreach (var line in lines) total += line.UnitsTaken;
            return total;
        }

        // ---- Save / load ----

        [Test]
        public void SaveLoad_RoundTrip_Preserves_BladeInventory_And_Toll()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            Assert.IsNull(mill.RecordBladeProcurement("Hawkins Machine Works", 200, false, diag));
            Assert.IsNull(mill.ReceiveSpareBlade(ProcuredSpare("spare-1", "Hawkins Machine Works"), diag));
            Assert.IsNull(mill.ReceiveTollLogLot(DocumentedLogLot(9201, 8, "stand-9"), "farmer-1", "A. Farmer", 209, diag));
            var policy = new SawmillTollPolicy(tollCashCentsPerLog: 5, tollInKindLumberShare01: 0.25f);
            Assert.NotNull(mill.RunTollSawing(
                EntityId.For(EntityKind.Lot, 9201).ToString(), 4,
                EntityId.For(EntityKind.Person, 5002), 210, policy, diag));
            mill.Blade.ApplyWear(0.20f);

            var dto = mill.CaptureSaveDto();
            var restored = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual("Hawkins Machine Works", restored.Blade.ProcurementSource);
            Assert.AreEqual(0.64f, restored.Blade.Condition01, 0.0001f,
                "4 toll logs x 0.04 wear + 0.20 direct wear.");
            Assert.AreEqual(1, restored.BladeInventory.Spares.Count);
            Assert.AreEqual("spare-1", restored.BladeInventory.Spares[0].BladeId);
            Assert.AreEqual("Hawkins Machine Works", restored.BladeInventory.Spares[0].ProcurementSource);
            Assert.AreEqual(4, restored.TollLogStock.TotalCustodyLogUnits, "Unsawn toll logs stay in custody.");
            Assert.AreEqual("farmer-1", restored.TollLogStock.CustodyLots[0].CustomerId);
            Assert.AreEqual(12, restored.TollPickup.LumberUnitsFor("farmer-1"), "4 logs -> 16 lumber, 4 in-kind toll, 12 to customer.");
            Assert.AreEqual(8, restored.TollPickup.FuelWoodUnitsFor("farmer-1"));
            Assert.AreEqual(4, restored.LumberStock.TotalLumberUnits, "The in-kind toll lumber survives.");
            Assert.AreEqual(210, restored.LumberStock.Lots[0].StackedDayIndex);
        }
    }
}
