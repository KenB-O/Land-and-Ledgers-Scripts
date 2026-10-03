using LandLedgers.Economy;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Valuation
{
    /// <summary>
    /// NX-1B drive-by: owner-labor replacement rate is never silently zero —
    /// local payroll evidence first, documented calibration fallback.
    /// </summary>
    public sealed class OwnerLaborRateTests
    {
        [Test]
        public void LocalPayrollSetsTheRate()
        {
            var registry = new EmploymentRelationshipRegistry();
            registry.Register(new EmploymentRelationship
            {
                EmployeePersonId = 1, EmployerBusinessId = "biz-1",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(6000, "hand"),
            });
            registry.Register(new EmploymentRelationship
            {
                EmployeePersonId = 2, EmployerBusinessId = "biz-1",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(9000, "hand"),
            });

            OwnerLaborRateResolver.ResolvedRate rate =
                OwnerLaborRateResolver.Resolve(registry, "biz-1");

            Assert.AreEqual(OwnerLaborRateResolver.RateSource.LocalPayroll, rate.Source);
            // Median weekly 7500¢ ÷ 60h = 125¢/h.
            Assert.AreEqual(125, rate.CentsPerHour);
            Assert.Greater(rate.CentsPerHour, 0);
        }

        [Test]
        public void NoPayrollFallsBackToDocumentedCalibration()
        {
            var registry = new EmploymentRelationshipRegistry();
            OwnerLaborRateResolver.ResolvedRate rate =
                OwnerLaborRateResolver.Resolve(registry, "biz-9");

            Assert.AreEqual(OwnerLaborRateResolver.RateSource.CalibrationFallback, rate.Source);
            Assert.AreEqual(OwnerLaborRateResolver.CalibrationFallbackCentsPerHour, rate.CentsPerHour);
            Assert.Greater(rate.CentsPerHour, 0); // never zero
            StringAssert.Contains("CALIBRATION", rate.BasisNote);
        }
    }
}
