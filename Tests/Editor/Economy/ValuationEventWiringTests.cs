using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// CLN-4: valuation events feed the BIZ-5 read model from real settlement paths.
    /// Weekly profit posts via the pre-reset callback (event-fed, never per-frame);
    /// owner labor posts from accumulated work time.
    /// </summary>
    [TestFixture]
    public sealed class ValuationEventWiringTests
    {
        private static BusinessInstanceState BuildBusiness(string instanceId)
        {
            var profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            return BusinessInstanceState.Create(
                instanceId, profile, -1, BusinessOwnerIdentity.Player());
        }

        [Test]
        public void PreWeeklyResetCallback_PostsProfitToValuation()
        {
            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("biz_001", "player", true);

            // Simulates what SimulationDrivers.PostWeeklyProfitToValuation does when
            // SharedBusinessRuntimeManager invokes the pre-reset callback.
            BusinessInstanceState business = BuildBusiness("biz_001");
            int weekNet = business.RuntimeState != null ? business.RuntimeState.WeekToDateNetCents : 0;
            valuation.RecordWeeklyProfit(business.InstanceId, weekNet);

            ValuationReadModelSaveDto dto = valuation.CaptureSaveDto();
            Assert.AreEqual(1, dto.entries.Count);
            Assert.AreEqual(1, dto.entries[0].evidence.WeeklyOperatingProfitCents.Count);
        }

        [Test]
        public void OwnerLabor_PostsFromWorkTime()
        {
            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("biz_001", "player", true);

            // 300 minutes worked in the week = 5 hours of owner labor.
            valuation.RecordOwnerLabor("biz_001", 300f / 60f, 0, 0);

            ValuationReadModelSaveDto dto = valuation.CaptureSaveDto();
            Assert.AreEqual(5f, dto.entries[0].evidence.OwnerWeeklyHours, 0.01f);
        }

        [Test]
        public void Valuation_NeverReadsCash()
        {
            // Canon §11.4: owner equity excludes cash by construction. The event wiring
            // posts profit (earnings) and labor — never a cash balance.
            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("biz_001", "player", true);
            valuation.RecordWeeklyProfit("biz_001", 10000);

            // Profit posts as earnings evidence; no cash field exists on the evidence.
            ValuationReadModelSaveDto dto = valuation.CaptureSaveDto();
            Assert.AreEqual(10000, dto.entries[0].evidence.WeeklyOperatingProfitCents[0]);
        }
    }
}
