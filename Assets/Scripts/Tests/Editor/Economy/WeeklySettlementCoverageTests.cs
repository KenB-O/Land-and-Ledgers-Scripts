using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// P1: the weekly settlement + payroll loops must cover every BusinessType.
    /// The newer types (Tannery..Lawyer, appended after the original 19) were
    /// silently skipped by the hardcoded per-type loops — their workers were never
    /// paid and their weekly nets never settled. The loops now iterate
    /// SharedBusinessRuntimeManager.WeeklySettlementBusinessTypes; this fixture
    /// pins the coverage so a future appended type cannot silently drop out.
    /// </summary>
    [TestFixture]
    public sealed class WeeklySettlementCoverageTests
    {
        [Test]
        public void WeeklySettlementBusinessTypes_CoversEveryTypeExceptGeneralStore()
        {
            var covered = new HashSet<BusinessType>(
                SharedBusinessRuntimeManager.WeeklySettlementBusinessTypes);

            foreach (BusinessType type in (BusinessType[])Enum.GetValues(typeof(BusinessType)))
            {
                if (type == BusinessType.GeneralStore)
                {
                    Assert.IsFalse(covered.Contains(type),
                        "GeneralStore must stay excluded — its payroll/settlement runs through GeneralStoreRuntimeManager.");
                    continue;
                }

                Assert.IsTrue(covered.Contains(type),
                    $"BusinessType.{type} is missing from WeeklySettlementBusinessTypes: no weekly settlement or payroll.");
            }
        }

        [Test]
        public void WeeklySettlementBusinessTypes_HasNoDuplicates()
        {
            var seen = new HashSet<BusinessType>();
            foreach (BusinessType type in SharedBusinessRuntimeManager.WeeklySettlementBusinessTypes)
            {
                Assert.IsTrue(seen.Add(type), $"BusinessType.{type} appears twice in WeeklySettlementBusinessTypes.");
            }
        }

        [Test]
        public void WeeklySettlementBusinessTypes_CountMatchesEnumMinusGeneralStore()
        {
            int enumCount = Enum.GetValues(typeof(BusinessType)).Length;
            Assert.AreEqual(enumCount - 1, SharedBusinessRuntimeManager.WeeklySettlementBusinessTypes.Length,
                "Exactly one type (GeneralStore) is excluded; every other enum value must be present.");
        }
    }
}
