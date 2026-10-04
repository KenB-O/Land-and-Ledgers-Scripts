using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: employer and institutional service arrangements — Canon §13.3C:
    /// dependable business from a mine or large employer, priority consumed
    /// during a cluster; care supported through a named institutional
    /// arrangement where historically justified.
    /// </summary>
    [TestFixture]
    public sealed class DoctorEmployerArrangementTests
    {
        private static EmployerServiceArrangement MineArrangement()
        {
            return new EmployerServiceArrangement
            {
                ArrangementId = "arr-mine-1",
                EmployerBusinessId = "mine-1",
                EmployerName = "Black Hills Mine",
                AllEmployeesCovered = true,
                RetainerCentsPerPeriod = 2000,
                PeriodDays = 30,
                PerVisitCents = 100,
                PriorityDuringCluster = true,
                StartDayIndex = 300,
                EndDayIndex = 0,
            };
        }

        [Test]
        public void AddEmployerArrangement_Valid_Registers()
        {
            var register = new DoctorServiceArrangementRegister();
            var diag = new List<string>();

            Assert.Null(register.AddEmployerArrangement(MineArrangement(), diag));
            Assert.AreEqual(1, register.EmployerArrangements.Count);
        }

        [Test]
        public void AddEmployerArrangement_DuplicateOrNameless_Refused()
        {
            var register = new DoctorServiceArrangementRegister();
            var diag = new List<string>();
            register.AddEmployerArrangement(MineArrangement(), diag);

            Assert.NotNull(register.AddEmployerArrangement(MineArrangement(), diag),
                "Arrangement ids are unique.");

            var nameless = MineArrangement();
            nameless.ArrangementId = "arr-mine-2";
            nameless.EmployerBusinessId = string.Empty;
            Assert.NotNull(register.AddEmployerArrangement(nameless, diag),
                "The employer business must be named — no anonymous coverage.");
        }

        [Test]
        public void FindEmployerArrangement_ActiveCoversWorker_ExpiredDoesNot()
        {
            var register = new DoctorServiceArrangementRegister();
            var diag = new List<string>();
            var arrangement = MineArrangement();
            arrangement.EndDayIndex = 330;
            register.AddEmployerArrangement(arrangement, diag);
            var worker = EntityId.For(EntityKind.Person, 555);

            Assert.NotNull(register.FindEmployerArrangement("mine-1", worker, 320),
                "Active arrangement covers the worker.");
            Assert.Null(register.FindEmployerArrangement("mine-1", worker, 340),
                "Expired arrangement covers nobody.");
            Assert.Null(register.FindEmployerArrangement("mill-2", worker, 320),
                "Another employer's workers are not covered.");
        }

        [Test]
        public void FindEmployerArrangement_NamedWorkerList_Filters()
        {
            var register = new DoctorServiceArrangementRegister();
            var diag = new List<string>();
            var arrangement = MineArrangement();
            arrangement.AllEmployeesCovered = false;
            arrangement.CoveredWorkerIds.Add(EntityId.For(EntityKind.Person, 556));
            register.AddEmployerArrangement(arrangement, diag);

            Assert.NotNull(register.FindEmployerArrangement("mine-1", EntityId.For(EntityKind.Person, 556), 320));
            Assert.Null(register.FindEmployerArrangement("mine-1", EntityId.For(EntityKind.Person, 557), 320),
                "Named-list coverage covers only the named.");
        }

        [Test]
        public void AddInstitutionalArrangement_RegistersAndCovers()
        {
            var register = new DoctorServiceArrangementRegister();
            var diag = new List<string>();

            var arrangement = new InstitutionalArrangement
            {
                ArrangementId = "arr-church-1",
                InstitutionName = "St. Anne's Aid Society",
                StipendCentsPerPeriod = 1500,
                PeriodDays = 30,
                StartDayIndex = 300,
            };
            arrangement.CoveredHouseholdIds.Add("household-7");

            Assert.Null(register.AddInstitutionalArrangement(arrangement, diag));

            Assert.NotNull(register.FindInstitutionalArrangement("household-7", 320));
            Assert.Null(register.FindInstitutionalArrangement("household-8", 320),
                "Named-household stipends cover only the named.");

            var nameless = new InstitutionalArrangement { ArrangementId = "arr-x-1" };
            Assert.NotNull(register.AddInstitutionalArrangement(nameless, diag),
                "The institution must be named.");
        }

        [Test]
        public void SaveLoad_RoundTripsArrangements()
        {
            var register = new DoctorServiceArrangementRegister();
            register.AddEmployerArrangement(MineArrangement(), new List<string>());

            var dto = register.CaptureSaveDto();
            var restored = new DoctorServiceArrangementRegister();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.EmployerArrangements.Count);
            Assert.AreEqual("arr-mine-1", restored.EmployerArrangements[0].ArrangementId);
            Assert.IsTrue(restored.EmployerArrangements[0].PriorityDuringCluster);
        }
    }
}
