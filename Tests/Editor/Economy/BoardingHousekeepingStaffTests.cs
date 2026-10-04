using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1F: the chamber-staff roster — named chambermaids (real person ids)
    /// with Cleaning skill; capacity is the sum of on-duty minutes; an empty
    /// roster means the proprietor cleans (Canon §8.1E small-house fallback),
    /// while a roster whose hands all left turns nothing, loudly (the
    /// housekeeper-loss case).
    /// </summary>
    [TestFixture]
    public sealed class BoardingHousekeepingStaffTests
    {
        [Test]
        public void AssignChambermaid_RostersRealPeople()
        {
            var diag = new List<string>();
            var staff = new BoardingHousekeepingStaff();

            Assert.IsFalse(staff.HasRoster, "nobody named yet");
            Assert.NotNull(staff.AssignChambermaid(0, 5, 100, diag), "anonymous hands refused");
            Assert.Null(staff.AssignChambermaid(21, 5, 100, diag));
            Assert.IsTrue(staff.HasRoster);
            Assert.IsTrue(staff.HasActiveStaff(100));
            Assert.AreEqual(BoardingHouseShopRuntime.HousekeeperMinutesPerDay, staff.TotalMinutesToday(100));
            Assert.AreEqual(5, staff.LeadingStaffSkill(100));
        }

        [Test]
        public void AssignChambermaid_ReNamingUpdatesInsteadOfDuplicating()
        {
            var diag = new List<string>();
            var staff = new BoardingHousekeepingStaff();

            Assert.Null(staff.AssignChambermaid(21, 3, 100, diag));
            Assert.Null(staff.AssignChambermaid(21, 8, 110, diag, minutesPerDay: 120));
            Assert.AreEqual(1, staff.Staff.Count, "no duplicate roster entry");
            Assert.AreEqual(8, staff.Staff[0].CleaningSkillLevel);
            Assert.AreEqual(120, staff.Staff[0].MinutesPerDay);
            Assert.AreEqual(100, staff.Staff[0].AssignedFromDayIndex, "earliest assignment day kept");
        }

        [Test]
        public void ReleaseChambermaid_AllGoneMeansNobodyCleans()
        {
            var diag = new List<string>();
            var staff = new BoardingHousekeepingStaff();

            Assert.Null(staff.AssignChambermaid(21, 5, 100, diag));
            Assert.Null(staff.AssignChambermaid(22, 4, 100, diag));
            Assert.Null(staff.ReleaseChambermaid(21, 120, diag));
            Assert.IsTrue(staff.HasActiveStaff(120), "one hand remains");
            Assert.Null(staff.ReleaseChambermaid(22, 121, diag));
            Assert.IsTrue(staff.HasRoster, "the house remembers it had staff");
            Assert.IsFalse(staff.HasActiveStaff(121), "nobody on duty — the housekeeper-loss case");
            Assert.AreEqual(0, staff.TotalMinutesToday(121));
            Assert.AreEqual(0, staff.LeadingStaffSkill(121));
            Assert.NotNull(staff.ReleaseChambermaid(22, 122, diag), "already released");
        }

        [Test]
        public void EffectiveTurnoverMinutes_SkilledHandsWorkFaster()
        {
            Assert.AreEqual(16, BoardingHousekeepingStaff.EffectiveTurnoverMinutes(20, 8), "strong hand: 0.8x");
            Assert.AreEqual(20, BoardingHousekeepingStaff.EffectiveTurnoverMinutes(20, 4), "steady hand: 1.0x");
            Assert.AreEqual(25, BoardingHousekeepingStaff.EffectiveTurnoverMinutes(20, 1), "green hand: 1.25x");
        }

        [Test]
        public void Staff_SaveLoad_RoundTrips()
        {
            var diag = new List<string>();
            var staff = new BoardingHousekeepingStaff();
            Assert.Null(staff.AssignChambermaid(21, 7, 100, diag));
            Assert.Null(staff.ReleaseChambermaid(21, 150, diag));

            var restored = new BoardingHousekeepingStaff();
            restored.LoadFromSaveDto(staff.CaptureSaveDto());

            Assert.IsTrue(restored.HasRoster, "history survives the round trip");
            Assert.IsFalse(restored.HasActiveStaff(150));
            Assert.AreEqual(7, restored.Staff[0].CleaningSkillLevel);
        }
    }
}
