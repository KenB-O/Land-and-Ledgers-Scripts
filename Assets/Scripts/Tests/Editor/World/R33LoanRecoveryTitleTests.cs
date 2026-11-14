using System.Collections.Generic;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// R33 (note 111 §5), slice 1: the loan-default recovery routes through the
    /// title chain. RecoverPlayerHolding moves a holding Player → "Bank
    /// Receiver" while the chain kept saying "Player"; the chain-first branch
    /// of IsPlayerOwnedPlot then answered player-owned for a bank-held plot
    /// (still buildable, never re-listable). RecordBankRecovery closes the
    /// last in-manager writer that performs a NEW parcel-ownership change
    /// without a chain record. Bridge-contract tests only: the manager helper
    /// and call site are harness-excluded and source-reviewed (R31/R47
    /// precedent). Note 127 C1–C5 applied; Q1–Q7 ruled (Purchase basis,
    /// generated instrument, "Bank Receiver" const, unconditional recording).
    /// </summary>
    [TestFixture]
    public sealed class R33LoanRecoveryTitleTests
    {
        private TitleAuthority titles;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            titles = new TitleAuthority();
            diag = new List<string>();
        }

        [Test]
        public void RecordBankRecovery_TransfersChainRecordedParcelToBankReceiver()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 11, "Ada Hawkins", "deal-11", 20000, 5, diag));
            string problem = PlayerTitleBridge.RecordBankRecovery(titles, 11, 15000, 50, diag);

            Assert.IsNull(problem, $"unexpected recording problem: {problem}");
            Assert.AreEqual(PlayerTitleBridge.BankReceiverHolderName, titles.CurrentHolder("plot-11"),
                "asserted through the const (note 127 N1)");
            Assert.IsFalse(new List<string>(titles.ParcelsHeldBy("Player")).Contains("plot-11"),
                "the recovered parcel must leave the player's holdings");
            Assert.IsTrue(new List<string>(titles.ParcelsHeldBy(PlayerTitleBridge.BankReceiverHolderName)).Contains("plot-11"),
                "the recovered parcel must be held by the Bank Receiver");
        }

        [Test]
        public void RecordBankRecovery_UnseenParcel_RegistersPlayerFirstThenTransfers()
        {
            string problem = PlayerTitleBridge.RecordBankRecovery(titles, 12, 15000, 50, diag);

            Assert.IsNull(problem, $"unexpected recording problem: {problem}");
            Assert.AreEqual(PlayerTitleBridge.BankReceiverHolderName, titles.CurrentHolder("plot-12"));

            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-12");
            Assert.AreEqual(2, chain.Count, "registration + recovery transfer");
            Assert.AreEqual("Player", chain[0].HolderName);
            Assert.AreEqual(TitleBasis.Unspecified, chain[0].Basis);
            Assert.AreEqual(PlayerTitleBridge.BankReceiverHolderName, chain[1].HolderName);
            Assert.AreEqual(TitleBasis.Purchase, chain[1].Basis);
        }

        [Test]
        public void RecordBankRecovery_NullAuthority_ReturnsError()
        {
            string problem = PlayerTitleBridge.RecordBankRecovery(null, 11, 15000, 50, diag);
            Assert.IsNotNull(problem);
            Assert.IsTrue(problem.Length > 0, "a recording problem must be named, not blank");
        }

        [Test]
        public void RecordBankRecovery_InstrumentIdShape()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 11, "Ada Hawkins", "deal-11", 20000, 5, diag));
            Assert.IsNull(PlayerTitleBridge.RecordBankRecovery(titles, 11, 15000, 50, diag));

            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-11");
            Assert.AreEqual("recovery-plot-11-day-50", chain[chain.Count - 1].InstrumentId);
        }

        [Test]
        public void RecordBankRecovery_BasisIsPurchase()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 11, "Ada Hawkins", "deal-11", 20000, 5, diag));
            Assert.IsNull(PlayerTitleBridge.RecordBankRecovery(titles, 11, 15000, 50, diag));

            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-11");
            Assert.AreEqual(TitleBasis.Purchase, chain[chain.Count - 1].Basis);
        }

        [Test]
        public void RecordBankRecovery_NoteCarriesRecoveredValue()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 11, "Ada Hawkins", "deal-11", 20000, 5, diag));
            Assert.IsNull(PlayerTitleBridge.RecordBankRecovery(titles, 11, 15000, 50, diag));

            IReadOnlyList<TitleRecord> chain11 = titles.ChainOf("plot-11");
            Assert.IsTrue(chain11[chain11.Count - 1].Note.Contains("15000"),
                "the note must carry the recovered value");

            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 13, "Ada Hawkins", "deal-13", 20000, 5, diag));
            Assert.IsNull(PlayerTitleBridge.RecordBankRecovery(titles, 13, 0, 50, diag));

            IReadOnlyList<TitleRecord> chain13 = titles.ChainOf("plot-13");
            Assert.AreEqual("loan-default recovery", chain13[chain13.Count - 1].Note);
        }

        [Test]
        public void RecordBankRecovery_AppendOnly_AcquisitionLinkUnchanged()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 11, "Ada Hawkins", "deal-11", 20000, 5, diag));

            IReadOnlyList<TitleRecord> before = titles.ChainOf("plot-11");
            Assert.AreEqual(2, before.Count, "registration + acquisition before the recovery");
            var acquisitionRecordId = before[1].RecordId;
            string acquisitionInstrument = before[1].InstrumentId;
            int acquisitionDay = before[1].DayIndex;

            Assert.IsNull(PlayerTitleBridge.RecordBankRecovery(titles, 11, 15000, 50, diag));

            IReadOnlyList<TitleRecord> chain = titles.ChainOf("plot-11");
            Assert.AreEqual(3, chain.Count, "registration + acquisition + recovery");
            Assert.AreEqual(string.Empty, chain[0].InstrumentId);
            Assert.AreEqual("deal-11", chain[1].InstrumentId);
            Assert.AreEqual(5, chain[1].DayIndex);
            Assert.AreEqual("recovery-plot-11-day-50", chain[2].InstrumentId);
            Assert.AreEqual(50, chain[2].DayIndex);
            Assert.AreEqual(acquisitionRecordId, chain[1].RecordId,
                "the recovery appends; it never rewrites the acquisition link");
            Assert.AreEqual(acquisitionInstrument, chain[1].InstrumentId);
            Assert.AreEqual(acquisitionDay, chain[1].DayIndex);
        }

        [Test]
        public void RecordBankRecovery_SaveLoadRoundTrip_PreservesRecovery()
        {
            Assert.IsNull(PlayerTitleBridge.RecordPlayerAcquisition(
                titles, 11, "Ada Hawkins", "deal-11", 20000, 5, diag));
            Assert.IsNull(PlayerTitleBridge.RecordBankRecovery(titles, 11, 15000, 50, diag));

            TitleAuthority.TitleSaveDto dto = titles.CaptureSaveDto();
            var restored = new TitleAuthority();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(PlayerTitleBridge.BankReceiverHolderName, restored.CurrentHolder("plot-11"));
            Assert.AreEqual(3, restored.ChainOf("plot-11").Count);
        }
    }
}
