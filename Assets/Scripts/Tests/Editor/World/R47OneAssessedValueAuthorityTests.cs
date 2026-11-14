using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>R47 (note 111 §11): the NPC levy uses the same assessed-value
    /// authority as the player levy — the player's GetAssessedValueCentsForPlot
    /// resolver, passed into the NPC drive as an optional trailing Func, with an
    /// explicit fallback to the holding's recorded acquisition price when the
    /// resolver cannot answer. Until NPC parcels have appreciation states on
    /// file the resolver answers 0 for every NPC parcel and the live levy is
    /// unchanged (fallback); the coverage extension is the §8.1 follow-up.</summary>
    [TestFixture]
    public sealed class R47OneAssessedValueAuthorityTests
    {
        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditCashBridge Cash = new CreditCashBridge();
            public NpcPropertyLifeService Life = new NpcPropertyLifeService();
            public List<string> Diag = new List<string>();

            public HouseholdLedger RegisterHousehold(int householdId, int cashCents)
            {
                var ledger = new HouseholdLedger(householdId);
                Assert.IsNull(ledger.RecordInflow(0, cashCents, HouseholdIncomeSource.OwnerContribution,
                    "test-seed", "test seed capital", "test"));
                Assert.IsNull(Cash.Register("household:" + householdId,
                    new HouseholdCashStore(householdId, ledger)));
                return ledger;
            }

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(Cash.Register(owner, purse));
                return purse;
            }
        }

        private static Func<int, int> ResolverFor(Dictionary<int, int> map)
        {
            return plotId => map.TryGetValue(plotId, out int value) ? value : 0;
        }

        private static NpcPropertyLifeReport Drive(Fixture f, CivicTreasuryService treasury, int day,
            Func<int, int> resolver)
        {
            return f.Life.DriveNpcPropertyLife(f.Cash, f.Life.Taxes, f.Authority, treasury, day,
                f.Diag, null, f.Ids, null, resolver);
        }

        private static NpcPropertyLifeReport DriveWithoutResolver(Fixture f, CivicTreasuryService treasury, int day)
        {
            return f.Life.DriveNpcPropertyLife(f.Cash, f.Life.Taxes, f.Authority, treasury, day,
                f.Diag, null, f.Ids);
        }

        private static TaxAssessment OnlyAssessment(Fixture f)
        {
            Assert.AreEqual(1, f.Life.Taxes.AllAssessments.Count, "exactly one assessment filed");
            return f.Life.Taxes.AllAssessments[0];
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
        public void T1_ResolverHigherThanAcquisitionPriceWins()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            Func<int, int> resolver = ResolverFor(new Dictionary<int, int> { { 7, 200000 } });

            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, resolver);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(PropertyTaxCalibration.ComputeLevyCents(200000), OnlyAssessment(f).AmountCents);
            Assert.AreEqual(3000, OnlyAssessment(f).AmountCents, "200000c assessed levies 3000c, not the 1500c fallback");
        }

        [Test]
        public void T2_ResolverLowerThanAcquisitionPriceWins()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            Func<int, int> resolver = ResolverFor(new Dictionary<int, int> { { 7, 50000 } });

            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, resolver);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(PropertyTaxCalibration.ComputeLevyCents(50000), OnlyAssessment(f).AmountCents);
            Assert.AreEqual(750, OnlyAssessment(f).AmountCents, "50000c assessed levies 750c — no clamp to the 1500c basis");
        }

        [Test]
        public void T3_ResolverZeroFallsBackWithDiagLine()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            Func<int, int> resolver = ResolverFor(new Dictionary<int, int> { { 7, 0 } });

            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, resolver);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1500, OnlyAssessment(f).AmountCents, "resolver 0 falls back to the 100000c acquisition price");
            Assert.IsTrue(DiagContains(f, "no appreciation state on file"),
                "the fallback is logged once per holding per drive");
        }

        [Test]
        public void T4_NullResolverFallsBackSilently()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();

            NpcPropertyLifeReport day10 = DriveWithoutResolver(f, treasury, 10);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1500, OnlyAssessment(f).AmountCents, "legacy callers keep the old behavior");
            Assert.IsTrue(!DiagContains(f, "no appreciation state on file"),
                "no resolver bound — the absence of a resolver is not an error, so no diag line");
        }

        [Test]
        public void T5_NonPlotParcelIdNeverConsultsResolver()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("parcel-1", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            int calls = 0;
            Func<int, int> counting = plotId => { calls++; return 200000; };

            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, counting);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1500, OnlyAssessment(f).AmountCents);
            Assert.AreEqual(0, calls, "a non-plot parcel id is a legitimate shape, not an error — the resolver is never consulted");
        }

        [Test]
        public void T6_MalformedPlotKeyNeverConsultsResolver()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("plot-7x", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            int calls = 0;
            Func<int, int> counting = plotId => { calls++; return 200000; };

            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, counting);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1500, OnlyAssessment(f).AmountCents);
            Assert.AreEqual(0, calls, "the strict parse refuses 'plot-7x' — the resolver is never consulted");
        }

        [Test]
        public void T7_Parity_SameDelegateSameAssessedValueSameLevyOnBothLegs()
        {
            var f = new Fixture();
            Func<int, int> resolver = ResolverFor(new Dictionary<int, int> { { 7, 200000 } });

            // Player half: the SAME delegate instance through the player driver.
            var playerTaxes = new PropertyTaxService();
            var playerDriver = new PlayerPropertyTaxDriver(playerTaxes,
                () => new List<int> { 7 }, resolver,
                (int cents, string label, out string message) => { message = null; return true; });
            playerDriver.ResolveWeek(10);
            Assert.AreEqual(1, playerTaxes.AllAssessments.Count, "player leg levies once");
            Assert.AreEqual("plot-7", playerTaxes.AllAssessments[0].ParcelId);
            Assert.AreEqual(3000, playerTaxes.AllAssessments[0].AmountCents, "player leg on 200000c assessed");

            // NPC half: the SAME delegate instance through the NPC drive.
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, resolver);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(3000, OnlyAssessment(f).AmountCents, "NPC leg on the same 200000c assessed");
            Assert.AreEqual(playerTaxes.AllAssessments[0].AmountCents, OnlyAssessment(f).AmountCents,
                "one authority — the same delegate yields the same levy on both legs");
        }

        [Test]
        public void T8_ExactlyOncePerTaxYearPreservedWithResolver()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            Func<int, int> resolver = ResolverFor(new Dictionary<int, int> { { 7, 200000 } });

            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, resolver);
            Assert.AreEqual(1, day10.TaxesLevied, "first drive of tax year 0 levies");
            Assert.AreEqual(3000, OnlyAssessment(f).AmountCents);

            NpcPropertyLifeReport day20 = Drive(f, treasury, 20, resolver);
            Assert.AreEqual(0, day20.TaxesLevied, "same tax year — never levied twice");
            Assert.AreEqual(1, f.Life.Taxes.AllAssessments.Count);

            NpcPropertyLifeReport year1 = Drive(f, treasury, 375, resolver);
            Assert.AreEqual(1, year1.TaxesLevied, "the next tax year levies once more");
            Assert.AreEqual(2, f.Life.Taxes.AllAssessments.Count);
            Assert.AreEqual(3000, f.Life.Taxes.AllAssessments[1].AmountCents, "the year-1 assessment uses the resolver too");
        }

        [Test]
        public void T9_NegativeResolverAnswerFallsBack()
        {
            var f = new Fixture();
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            Func<int, int> resolver = ResolverFor(new Dictionary<int, int> { { 7, -500 } });

            NpcPropertyLifeReport day10 = Drive(f, treasury, 10, resolver);

            Assert.AreEqual(1, day10.TaxesLevied);
            Assert.AreEqual(1500, OnlyAssessment(f).AmountCents, "negative clamped to 0, then rule 4 fallback");
            Assert.IsTrue(DiagContains(f, "no appreciation state on file"),
                "the negative answer takes the same fallback path as a 0 answer");
        }

        [Test]
        public void T10_SaveLoadRoundTripPreservesResolverLeviedAssessment()
        {
            var f = new Fixture();
            HouseholdLedger owner = f.RegisterHousehold(7, 10000);
            f.RegisterPurse("County Tax Collector", 0);
            f.Life.RegisterHolding("plot-7", "bld-1", 7, 0, 100000,
                "", "", NpcPropertyHoldingKind.OwnerOccupied, f.Diag);
            var treasury = new CivicTreasuryService();
            Func<int, int> resolver = ResolverFor(new Dictionary<int, int> { { 7, 200000 } });

            NpcPropertyLifeReport day5 = Drive(f, treasury, 5, resolver);
            Assert.AreEqual(1, day5.TaxesLevied, "day-5 drive levies 3000c on the 200000c assessed value");
            Assert.AreEqual(3000, OnlyAssessment(f).AmountCents);
            Assert.AreEqual(0, day5.TaxesPaidCents, "not yet due — the drive-levied assessment is due day 65 (R31 guard)");

            NpcPropertyLifeService.NpcPropertyLifeSaveDto dto = f.Life.CaptureSaveDto();
            Assert.IsNotNull(dto.Taxes, "the DTO carries the tax book");
            Assert.AreEqual(1, dto.Taxes.Assessments.Count);

            var restored = new NpcPropertyLifeService();
            restored.LoadFromSaveDto(dto, f.Diag);
            Assert.AreEqual(1, restored.Holdings.Count);
            Assert.AreEqual(1, restored.Taxes.AllAssessments.Count, "the tax book survives the round-trip");

            NpcPropertyLifeReport day70 = restored.DriveNpcPropertyLife(f.Cash, restored.Taxes,
                f.Authority, treasury, 70, f.Diag, null, f.Ids, null, resolver);
            Assert.AreEqual(3000, day70.TaxesPaidCents, "the restored service pays the resolver-levied assessment at due");
            Assert.AreEqual(3000, treasury.BalanceCents);
            Assert.AreEqual(10000 - 3000, owner.GetBalanceCents(), "owner debit == treasury credit");
            Assert.AreEqual(1, restored.Taxes.AllAssessments.Count, "no second levy on the restored book");
        }
    }
}
