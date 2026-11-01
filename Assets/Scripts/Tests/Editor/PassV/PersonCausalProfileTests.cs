using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Reporting;
using NUnit.Framework;

namespace LandLedgers.EditorTests.PassV
{
    /// <summary>
    /// Pass V (Worker 4, P3-1): the causal person read model derives every
    /// field from an authority. Age comes from the simulation date and the
    /// birth-date authority (no separate UI age counter); provenance, scenario
    /// origin, household, employment, obligations, and bounded demographic
    /// history are all present; redaction is visibility-respecting.
    /// </summary>
    [TestFixture]
    public sealed class PersonCausalProfileTests
    {
        [Test]
        public void CausalProfile_DerivesAgeFromBirthDateAuthority()
        {
            var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: false);
            driver.AdvanceDays(400); // into 1881

            PersonCausalProfile profile = driver.BuildCausalProfile(AutonomousSocietyDriver.P12Lena);
            Assert.AreEqual(AutonomousSocietyDriver.P12Lena, profile.PersonId);
            // Lena born 1870-02-28: 11 in spring 1881.
            Assert.AreEqual(11, profile.DerivedAgeYears,
                "age must derive from the birth date, not a cached counter");
            Assert.AreEqual(BirthDateProvenance.GeneratedExact, profile.BirthDateProvenance);
            Assert.AreEqual("authored scenario", profile.ScenarioOrigin);
            Assert.AreEqual(AutonomousSocietyDriver.H5Widow, profile.HouseholdId);
        }

        [Test]
        public void CausalProfile_SimulatedBirth_HasProvenanceAndHistory()
        {
            var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            driver.AdvanceDays(100); // past the day-40 birth

            // The newborn is the only person with a "simulated birth" origin.
            PersonCausalProfile newborn = null;
            for (int id = 14; id <= 20; id++)
            {
                PersonCausalProfile candidate = driver.BuildCausalProfile(id);
                if (candidate.PersonId == id && candidate.ScenarioOrigin == "simulated birth")
                {
                    newborn = candidate;
                    break;
                }
            }
            Assert.IsNotNull(newborn, "expected a simulated-birth person");
            Assert.AreEqual(0, newborn.DerivedAgeYears, "a newborn is 0 years old");
            Assert.AreEqual(BirthDateProvenance.GeneratedExact, newborn.BirthDateProvenance);
            Assert.AreEqual(AutonomousSocietyDriver.H1Miller, newborn.HouseholdId);
            Assert.Greater(newborn.DemographicHistory.Count, 0, "birth recorded in demographic history");
        }

        [Test]
        public void CausalProfile_EmploymentAndObligations_FromAuthorities()
        {
            var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            driver.AdvanceDays(30);

            PersonCausalProfile elena = driver.BuildCausalProfile(AutonomousSocietyDriver.P6Clerk);
            Assert.Greater(elena.Employments.Count, 0, "Elena's store employment comes from the registry");
            bool foundStore = false;
            foreach (CausalEmploymentRecord record in elena.Employments)
            {
                if (record.EmploymentId == "emp-p6-store")
                {
                    foundStore = true;
                    Assert.AreEqual("biz-store", record.EmployerBusinessId);
                    Assert.Greater(record.WeeklyWageCents, 0);
                }
            }
            Assert.IsTrue(foundStore, "the store employment record must be present");

            // H5's store credit is visible on a liable household member.
            PersonCausalProfile mara = driver.BuildCausalProfile(AutonomousSocietyDriver.P11Widow);
            bool foundCredit = false;
            foreach (CausalObligationSummary obligation in mara.Obligations)
            {
                if (obligation.Counterparty == "biz-store")
                {
                    foundCredit = true;
                }
            }
            Assert.IsTrue(foundCredit, "H5's store credit must appear in Mara's obligations");
        }

        [Test]
        public void CausalProfile_RedactForObserver_HidesSensitiveFields()
        {
            var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            driver.AdvanceDays(30);

            PersonCausalProfile profile = driver.BuildCausalProfile(AutonomousSocietyDriver.P11Widow);
            Assert.Greater(profile.Obligations.Count, 0, "precondition: obligations visible");
            profile.RedactForObserver();
            Assert.AreEqual(0, profile.Obligations.Count, "obligations hidden from strangers");
            Assert.AreEqual(0, profile.Employments.Count, "employment hidden from strangers");
            Assert.AreEqual(0, profile.DemographicHistory.Count, "history hidden from strangers");
            Assert.AreEqual(-1, profile.HouseholdId, "household hidden from strangers");
            // Identity and derived age remain: they are observable facts.
            Assert.AreEqual(AutonomousSocietyDriver.P11Widow, profile.PersonId);
            Assert.GreaterOrEqual(profile.DerivedAgeYears, 0);
        }

        [Test]
        public void CausalProfile_Deceased_KeepsRecordWithDeathEvent()
        {
            var driver = AutonomousSocietyDriver.BuildStandard(demographicEvents: true);
            driver.AdvanceDays(700); // past both deaths

            PersonCausalProfile mara = driver.BuildCausalProfile(AutonomousSocietyDriver.P11Widow);
            Assert.IsTrue(mara.IsDeceased, "Mara died on day 650");
            Assert.AreEqual(650, mara.DeathDayIndex);
            bool foundDeath = false;
            foreach (CausalDemographicEvent evt in mara.DemographicHistory)
            {
                if (evt.EventKind == "death")
                {
                    foundDeath = true;
                }
            }
            Assert.IsTrue(foundDeath, "the death is in the bounded demographic history");
        }
    }
}
