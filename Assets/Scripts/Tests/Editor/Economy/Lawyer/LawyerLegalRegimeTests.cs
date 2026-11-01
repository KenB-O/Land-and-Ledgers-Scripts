using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Lawyer;
using LandLedgers.Economy.Legal;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D3D: the dated legal-regime authority. Every dated fact asserted here
    /// comes from Canon Project Bible §1.6B (interest chronology, statehood,
    /// municipal debt, bankruptcy gap, homestead) and §1.6C (the authority
    /// list). The epoch is the spring-1880 campaign opening (Canon §2.1B);
    /// day 0 defaults to April 1, 1880.
    /// </summary>
    [TestFixture]
    public sealed class LawyerLegalRegimeTests
    {
        private List<string> diag;
        private LegalRegime regime;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            regime = new LegalRegime();
        }

        [Test]
        public void Epoch_DayZero_IsSpring1880_AndRoundTrips()
        {
            LegalRegimeDate d0 = regime.DateForDayIndex(0);
            Assert.AreEqual(1880, d0.Year);
            Assert.AreEqual(4, d0.Month);
            Assert.AreEqual(1, d0.Day);

            int statehood = regime.DayIndexForDate(1889, 11, 2);
            LegalRegimeDate back = regime.DateForDayIndex(statehood);
            Assert.AreEqual(1889, back.Year);
            Assert.AreEqual(11, back.Month);
            Assert.AreEqual(2, back.Day);
        }

        [Test]
        public void Jurisdiction_TerritoryUntilStatehood_ThenSouthDakota()
        {
            Assert.AreEqual(LegalJurisdiction.DakotaTerritory,
                regime.JurisdictionForDay(regime.DayIndexForDate(1889, 11, 1)));
            Assert.AreEqual(LegalJurisdiction.SouthDakota,
                regime.JurisdictionForDay(regime.DayIndexForDate(1889, 11, 2)));
            Assert.IsTrue(regime.PrivateLawContinuesAcrossStatehood,
                "Canon §1.6B: statehood does not reset private law.");
        }

        [Test]
        public void Usury_SevenPercentDefault_TwelvePercentWrittenCeiling()
        {
            Assert.AreEqual(7, regime.DefaultLegalRatePercent);
            int opening = regime.DayIndexForDate(1880, 4, 1);
            Assert.AreEqual(12, regime.MaxWrittenContractRatePercent(opening, false));

            string why;
            Assert.IsTrue(regime.IsAgreedRateLawful(10.0, opening, false, out why), why);
            Assert.IsFalse(regime.IsAgreedRateLawful(15.0, opening, false, out why), why);
        }

        [Test]
        public void Usury_CoveredBlackHillsCounties_NoCeiling_1881_Through_June1887()
        {
            int mid = regime.DayIndexForDate(1885, 3, 15);
            Assert.IsNull(regime.MaxWrittenContractRatePercent(mid, true),
                "Covered Black Hills counties: any mutually agreed rate, 1881-06/30/1887.");

            string why;
            Assert.IsTrue(regime.IsAgreedRateLawful(25.0, mid, true, out why), why);
            Assert.IsTrue(regime.IsAgreedRateLawful(25.0, mid, false, out why) == false,
                "Non-covered counties keep the ceiling throughout.");

            int ceilingReturns = regime.DayIndexForDate(1887, 7, 1);
            Assert.AreEqual(12, regime.MaxWrittenContractRatePercent(ceilingReturns, true),
                "The general ceiling returns July 1, 1887.");
            Assert.IsFalse(regime.IsAgreedRateLawful(12.01, ceilingReturns, true, out why), why);
            Assert.IsTrue(regime.IsAgreedRateLawful(12.0, ceilingReturns, true, out why), why);
        }

        [Test]
        public void MunicipalDebtCeiling_FourPercentFrom1886Act_FivePercentAtStatehood()
        {
            Assert.IsNull(regime.MunicipalDebtCeilingPercentOfAssessedValue(regime.DayIndexForDate(1886, 7, 29)),
                "No canon-stated ceiling before the July 30, 1886 federal act — none invented.");
            Assert.AreEqual(4.0, regime.MunicipalDebtCeilingPercentOfAssessedValue(regime.DayIndexForDate(1886, 7, 30)));
            Assert.AreEqual(5.0, regime.MunicipalDebtCeilingPercentOfAssessedValue(regime.DayIndexForDate(1889, 11, 2)));
        }

        [Test]
        public void PublicAid_SharplyRestrictedFrom1886Act()
        {
            Assert.AreEqual(PublicAidPosture.Unrestricted,
                regime.PublicAidPostureForDay(regime.DayIndexForDate(1886, 7, 29)));
            Assert.AreEqual(PublicAidPosture.SharplyRestricted,
                regime.PublicAidPostureForDay(regime.DayIndexForDate(1886, 7, 30)));
        }

        [Test]
        public void Bankruptcy_GapUntil1898Act()
        {
            Assert.AreEqual(FederalBankruptcyPosture.GapNoFederalRoute,
                regime.BankruptcyPostureForDay(regime.DayIndexForDate(1898, 6, 30)));
            Assert.AreEqual(FederalBankruptcyPosture.BankruptcyAct1898,
                regime.BankruptcyPostureForDay(regime.DayIndexForDate(1898, 7, 1)));
        }

        [Test]
        public void HomesteadAndSpousal_ProtectedThroughout()
        {
            int opening = regime.DayIndexForDate(1880, 4, 1);
            Assert.IsTrue(regime.HomesteadExemptionRecognized(opening),
                "Homestead/personal-property exemptions exist before statehood.");
            Assert.IsTrue(regime.SpousalParticipationRequiredForHomesteadTransferOrEncumbrance(opening));
        }

        [Test]
        public void CreditorRulesEra_Anchored1892_ContentIsResearchHold()
        {
            Assert.AreEqual(CreditorRulesEra.Territorial,
                regime.CreditorRulesEraForDay(regime.DayIndexForDate(1891, 12, 31)));
            Assert.AreEqual(CreditorRulesEra.SouthDakota1892Anchored,
                regime.CreditorRulesEraForDay(regime.DayIndexForDate(1892, 1, 1)));
        }

        [Test]
        public void DescribeDay_SummarizesRegime_ForDiagnostics()
        {
            string summary = regime.DescribeDay(regime.DayIndexForDate(1890, 1, 15));
            Assert.IsTrue(summary.Contains("SouthDakota"), summary);
            Assert.IsTrue(summary.Contains("1890-01-15"), summary);
        }

        // ---------- W9 refactor onto the regime ----------

        private static LawyerPracticeRuntime FitPractice(string id = "law-regime")
        {
            var practice = new LawyerPracticeRuntime(id);
            practice.SetOffice(new LawyerOfficeRequirements
            {
                HasDesk = true,
                HasLawBooks = true,
                HasWritingSupplies = true,
                HasDocumentForms = true,
                HasSeal = true,
                HasSecureRecords = true,
            });
            return practice;
        }

        [Test]
        public void DeedRecording_WithRegime_PreservesW9QuestionableInstrumentBehavior()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 1, "Kennedy Baldwin-ooms", LegalMatterKind.DeedDrafting, "deed", 10, diag: diag);
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-q", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);

            var deeds = new LawyerDeedService(practice.Office);
            var ids = new EntityIdRegistry();
            // Grantor is NOT the recorded holder: questionable instrument.
            var deed = deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.QuitclaimDeed,
                9, "Ada Hawkins", 2, "Shannon Cole", "p-q", "40 acres", 5000, 11, diag);
            Assert.IsNull(deeds.SealDeed(deed.DeedId, diag));
            Assert.IsNull(deeds.FinalizeDeed(deed.DeedId, 11, diag));

            // With the regime: W9 behavior preserved — recorded with a color-of-title warning.
            Assert.IsNull(deeds.RecordDeed(deed.DeedId, titles, ids, 12, "sale", diag, regime));
            Assert.AreEqual(DeedStatus.Recorded, deed.Status);
            Assert.AreEqual("Shannon Cole", titles.CurrentHolder("p-q"),
                "The T2F title authority moved title, not the regime.");
            Assert.IsTrue(regime.RecordingAcceptsQuestionableInstruments(12));
        }

        [Test]
        public void DeedRecording_WithoutRegime_BehaviorUnchanged()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 1, "Kennedy Baldwin-ooms", LegalMatterKind.DeedDrafting, "deed", 10, diag: diag);
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-q2", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);

            var deeds = new LawyerDeedService(practice.Office);
            var ids = new EntityIdRegistry();
            var deed = deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.WarrantyDeed,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole", "p-q2", "40 acres", 10000, 11, diag);
            Assert.IsNull(deeds.SealDeed(deed.DeedId, diag));
            Assert.IsNull(deeds.FinalizeDeed(deed.DeedId, 11, diag));

            // Optional regime parameter: existing callers compile and behave as before.
            Assert.IsNull(deeds.RecordDeed(deed.DeedId, titles, ids, 12, "sale", diag));
            Assert.AreEqual(DeedStatus.Recorded, deed.Status);
        }
    }
}
