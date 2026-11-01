using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: the hotel's service-staff roster — Canon §8.1E roles
    /// (desk/night clerks, porters, chambermaids, cooks, waiters) as real
    /// persons with skill and daily minute budgets; release keeps history;
    /// an emptied role says so loudly (Canon §8.1E proprietor-dependence
    /// continuity case). Save round-trips the roster.
    /// </summary>
    [TestFixture]
    public sealed class HotelStaffTests
    {
        [Test]
        public void AssignStaff_RostersRoles_WithMinutesAndSkill()
        {
            var staff = new HotelStaff();
            var diag = new List<string>();
            Assert.IsNull(staff.AssignStaff(501, HotelStaffRole.Porter, 7, 3, diag));
            Assert.IsNull(staff.AssignStaff(502, HotelStaffRole.NightClerk, 4, 3, diag));
            Assert.IsNull(staff.AssignStaff(503, HotelStaffRole.Chambermaid, 2, 3, diag));

            Assert.IsTrue(staff.HasRoster);
            Assert.IsTrue(staff.HasActiveRole(HotelStaffRole.Porter, 3));
            Assert.AreEqual(HotelStaff.PorterMinutesPerDay, staff.TotalMinutesToday(HotelStaffRole.Porter, 3));
            Assert.AreEqual(HotelStaff.NightClerkMinutesPerDay, staff.TotalMinutesToday(HotelStaffRole.NightClerk, 3));
            Assert.AreEqual(7, staff.LeadingSkill(HotelStaffRole.Porter, 3));
            Assert.AreEqual(0, staff.LeadingSkill(HotelStaffRole.Porter, 2), "not yet assigned on day 2");
        }

        [Test]
        public void AssignStaff_RefusesAnonymousPerson_ClampsSkill()
        {
            var staff = new HotelStaff();
            var diag = new List<string>();
            Assert.IsNotNull(staff.AssignStaff(0, HotelStaffRole.Cook, 5, 3, diag));
            Assert.IsNotNull(staff.AssignStaff(511, HotelStaffRole.Cook, 5, -1, diag));
            Assert.IsNull(staff.AssignStaff(511, HotelStaffRole.Cook, 99, 3, diag));
            Assert.AreEqual(10, staff.LeadingSkill(HotelStaffRole.Cook, 3), "skill clamped to the 1-10 scale");
        }

        [Test]
        public void ReleaseStaff_EmptiesRole_Loudly_AndKeepsHistory()
        {
            var staff = new HotelStaff();
            var diag = new List<string>();
            staff.AssignStaff(521, HotelStaffRole.Waiter, 6, 1, diag);
            staff.AssignStaff(522, HotelStaffRole.Waiter, 3, 1, diag);
            Assert.IsNull(staff.ReleaseStaff(521, HotelStaffRole.Waiter, 5, diag));
            Assert.IsTrue(staff.HasActiveRole(HotelStaffRole.Waiter, 5), "one waiter remains");
            Assert.IsNull(staff.ReleaseStaff(522, HotelStaffRole.Waiter, 5, diag));
            Assert.IsFalse(staff.HasActiveRole(HotelStaffRole.Waiter, 5), "no waiter remains");
            Assert.IsTrue(staff.HasRoster, "the house remembers it had waiters");
            Assert.IsTrue(diag[diag.Count - 1].Contains("NO"), "the empty role is said loudly");
            Assert.IsNotNull(staff.ReleaseStaff(999, HotelStaffRole.Waiter, 5, diag), "nobody to release");
        }

        [Test]
        public void EffectiveWorkMinutes_SkilledHandWorksFaster()
        {
            Assert.Less(HotelStaff.EffectiveWorkMinutes(60, 8), HotelStaff.EffectiveWorkMinutes(60, 2),
                "a skilled hand turns rooms faster than a green one");
            Assert.AreEqual(0, HotelStaff.EffectiveWorkMinutes(0, 8));
        }

        [Test]
        public void SaveLoad_RoundTripsRoster_WithHistory()
        {
            var staff = new HotelStaff();
            var diag = new List<string>();
            staff.AssignStaff(531, HotelStaffRole.DeskClerk, 5, 2, diag);
            staff.ReleaseStaff(531, HotelStaffRole.DeskClerk, 4, diag);

            var reloaded = new HotelStaff();
            reloaded.LoadFromSaveDto(staff.CaptureSaveDto());
            Assert.AreEqual(1, reloaded.Staff.Count, "history kept");
            Assert.IsFalse(reloaded.HasActiveRole(HotelStaffRole.DeskClerk, 5), "the release survived the round trip");
        }
    }
}
