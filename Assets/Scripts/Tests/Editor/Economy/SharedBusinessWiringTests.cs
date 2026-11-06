using NUnit.Framework;
using LandLedgers.Economy.Core;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 04 (2026-11-06) slice M5 (Obj 43, D-S6): harness tests for
    /// the pure parts of the loud-wiring sweep — the null-authority
    /// diagnostic strings. The MonoBehaviour logging (Debug.LogWarning
    /// calls in SharedBusinessRuntimeManager, week-keyed per
    /// authority+consequence) is source-reviewed; these tests pin the
    /// exact strings SBRM logs, so a wording change fails here first.
    /// Unity compilation, Test Runner, PlayMode, and SBRM itself are NOT
    /// claimed; SBRM edits are source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class SharedBusinessWiringTests
    {
        [Test]
        public void UnwiredAuthorityWarning_Format_NamesAuthorityAndConsequence()
        {
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: townWorld is not wired; map, building, and premises resolution is unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "townWorld", "map, building, and premises resolution is unavailable"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: an authority is not wired; the depending work was skipped",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(null, null),
                "blank inputs fall back to generic wording — a call site can never produce an empty warning");
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: logisticsRuntime is not wired; the business-to-business transfer was refused with 0 units",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "  logisticsRuntime  ", "  the business-to-business transfer was refused with 0 units  "),
                "surrounding whitespace is trimmed");
        }

        [Test]
        public void UnwiredAuthorityWarnings_CoverEveryAutoWireAuthority()
        {
            // The seven authorities AutoWire resolves (M5): the exact
            // (authority, consequence) pairs SBRM logs, one warning per
            // still-null authority per week.
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: townWorld is not wired; map, building, and premises resolution is unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "townWorld", "map, building, and premises resolution is unavailable"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: populationManager is not wired; population state, household visits, and demand collection are unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "populationManager", "population state, household visits, and demand collection are unavailable"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: timeManager is not wired; the simulation clock, week keys, and day index are unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "timeManager", "the simulation clock, week keys, and day index are unavailable"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: generalStoreRuntime is not wired; General Store transfers and recurring store orders are unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "generalStoreRuntime", "General Store transfers and recurring store orders are unavailable"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: playerPortfolio is not wired; automatic cash transfers and owner distributions are unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "playerPortfolio", "automatic cash transfers and owner distributions are unavailable"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: logisticsRuntime is not wired; business-to-business transfers and shipments are unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "logisticsRuntime", "business-to-business transfers and shipments are unavailable"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: EmploymentRegistry is not wired; workplace assignment reconciliation and employment wiring are unavailable",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "EmploymentRegistry", "workplace assignment reconciliation and employment wiring are unavailable"));
        }

        [Test]
        public void UnwiredAuthorityWarnings_CoverReturnZeroSites()
        {
            // The site-specific pairs SBRM logs at the silent-null
            // return-0 sites from the M0 audit.
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: populationManager is not wired; generated workplace assignments were not reconciled",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "populationManager", "generated workplace assignments were not reconciled"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: EmploymentRegistry is not wired; generated workplace assignments were not reconciled",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "EmploymentRegistry", "generated workplace assignments were not reconciled"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: populationManager is not wired; owner-operator person mirrors were not reconciled",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "populationManager", "owner-operator person mirrors were not reconciled"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: EmploymentRegistry is not wired; owner-operator person mirrors were not reconciled",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "EmploymentRegistry", "owner-operator person mirrors were not reconciled"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: playerPortfolio is not wired; automatic cash transfers were skipped for this mode",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "playerPortfolio", "automatic cash transfers were skipped for this mode"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: populationManager is not wired; occupied beds read as 0",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "populationManager", "occupied beds read as 0"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: linkedSawmillSupportNode is not wired; linked sawmill support production was skipped",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "linkedSawmillSupportNode", "linked sawmill support production was skipped"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: populationManager is not wired; paid service visits read as 0 (no service revenue)",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "populationManager", "paid service visits read as 0 (no service revenue)"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: generalStoreRuntime is not wired; the recurring General Store order was not resolved",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "generalStoreRuntime", "the recurring General Store order was not resolved"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: logisticsRuntime is not wired; the business-to-business transfer was refused with 0 units",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "logisticsRuntime", "the business-to-business transfer was refused with 0 units"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: generalStoreRuntime is not wired; the transfer to the General Store was refused with 0 units",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "generalStoreRuntime", "the transfer to the General Store was refused with 0 units"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: logisticsRuntime is not wired; the transfer to the General Store was refused with 0 units",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "logisticsRuntime", "the transfer to the General Store was refused with 0 units"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: townWorld is not wired; opening businesses were not ensured",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "townWorld", "opening businesses were not ensured"));
            Assert.AreEqual(
                "SharedBusinessRuntimeManager: townWorld is not wired; the obsolete legacy passive-business seed did not run (no businesses ensured)",
                SharedBusinessWiring.BuildUnwiredAuthorityWarning(
                    "townWorld", "the obsolete legacy passive-business seed did not run (no businesses ensured)"));
        }
    }
}
