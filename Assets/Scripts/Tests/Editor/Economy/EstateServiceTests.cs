using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Economy.Estates;
using LandLedgers.Population;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// T3C: estates &amp; succession. Canon §8.3 — death redistributes ownership
    /// and authority; it does not delete a business. Intestate rule from
    /// 1870s territorial norms (historical research): debts first, spouse
    /// 1/3 + dower life interest, children split the remainder.
    /// </summary>
    [TestFixture]
    public sealed class EstateServiceTests
    {
        private EntityIdRegistry ids;
        private KinshipRegistry kinship;
        private TitleAuthority titles;
        private EstateService estates;

        [SetUp]
        public void SetUp()
        {
            ids = new EntityIdRegistry();
            kinship = new KinshipRegistry();
            titles = new TitleAuthority();
            estates = new EstateService();
        }

        private Estate OpenFor(int decedent, string name, int day = 100)
        {
            return estates.OpenEstate(ids, decedent, name, kinship, day, new List<string>());
        }

        [Test]
        public void OpenEstate_SpouseAndChildren_SharesComputed()
        {
            kinship.AddSpouses(1, 2, "test");       // 1 = decedent, 2 = spouse
            kinship.AddParentChild(1, 3, "test");   // children
            kinship.AddParentChild(1, 4, "test");

            Estate estate = OpenFor(1, "John Doe");
            Assert.IsNotNull(estate);
            Assert.AreEqual(EstateStatus.Open, estate.Status);
            // Spouse: 1/3 outright + dower life interest entry; 2 children split 2/3.
            Assert.AreEqual(4, estate.Heirs.Count);
            Assert.IsTrue(estate.Heirs.Exists(h => h.PersonId == 2 && !h.IsLifeInterest
                && h.ShareNumerator == 1 && h.ShareDenominator == 3));
            Assert.IsTrue(estate.Heirs.Exists(h => h.PersonId == 2 && h.IsLifeInterest),
                "Dower is a life interest, not ownership.");
            int childShares = 0;
            foreach (HeirShare h in estate.Heirs)
            {
                if (h.PersonId == 3 || h.PersonId == 4)
                {
                    Assert.AreEqual(2, h.ShareNumerator);
                    Assert.AreEqual(6, h.ShareDenominator); // 2/3 split 2 ways = 1/3 each
                    childShares++;
                }
            }
            Assert.AreEqual(2, childShares);
        }

        [Test]
        public void OpenEstate_NoKin_HeldAndFlagged()
        {
            Estate estate = OpenFor(9, "Lone Prospector");
            Assert.IsNotNull(estate);
            Assert.AreEqual(0, estate.Heirs.Count, "No kin → no heirs invented.");
            Assert.IsTrue(estates.Diagnostics.Count > 0);
        }

        [Test]
        public void DistributeParcel_BlockedWhileDebtsUnsettled()
        {
            kinship.AddParentChild(1, 3, "test");
            Estate estate = OpenFor(1, "John Doe");
            titles.RegisterParcel("p1", "40 acres", 40f, "John Doe", TitleBasis.HomesteadClaim, 10, new List<string>());
            Assert.IsNull(estates.RegisterDecedentParcel(estate, "p1", titles, new List<string>()));

            estates.RecordEstateDebt(estate, "note-1", new List<string>());
            string problem = estates.DistributeParcel(estate, "p1", 3, "Child Doe",
                ids, titles, 101, new List<string>());
            Assert.IsNotNull(problem, "Creditors before heirs — always.");
            Assert.AreEqual("John Doe", titles.CurrentHolder("p1"));

            Assert.IsNull(estates.MarkDebtSettled(estate, "note-1", new List<string>()));
            Assert.IsNull(estates.DistributeParcel(estate, "p1", 3, "Child Doe",
                ids, titles, 102, new List<string>()));
            Assert.AreEqual("Child Doe", titles.CurrentHolder("p1"));
            var chain = titles.ChainOf("p1");
            Assert.AreEqual(TitleBasis.Inheritance, chain[chain.Count - 1].Basis);
        }

        [Test]
        public void DistributeParcel_NonHeir_Refused()
        {
            kinship.AddParentChild(1, 3, "test");
            Estate estate = OpenFor(1, "John Doe");
            titles.RegisterParcel("p1", "40 acres", 40f, "John Doe", TitleBasis.HomesteadClaim, 10, new List<string>());
            estates.RegisterDecedentParcel(estate, "p1", titles, new List<string>());

            string problem = estates.DistributeParcel(estate, "p1", 99, "Stranger",
                ids, titles, 101, new List<string>());
            Assert.IsNotNull(problem, "No conjured heirs.");
            Assert.AreEqual("John Doe", titles.CurrentHolder("p1"));
        }

        [Test]
        public void CloseEstate_RequiresDebtsSettledAndParcelsDistributed()
        {
            kinship.AddParentChild(1, 3, "test");
            Estate estate = OpenFor(1, "John Doe");
            titles.RegisterParcel("p1", "40 acres", 40f, "John Doe", TitleBasis.HomesteadClaim, 10, new List<string>());
            estates.RegisterDecedentParcel(estate, "p1", titles, new List<string>());
            estates.RecordEstateDebt(estate, "note-1", new List<string>());

            Assert.IsNotNull(estates.CloseEstate(estate, new List<string>()));
            estates.MarkDebtSettled(estate, "note-1", new List<string>());
            Assert.IsNotNull(estates.CloseEstate(estate, new List<string>()), "Parcels still undistributed.");
            estates.DistributeParcel(estate, "p1", 3, "Child Doe", ids, titles, 102, new List<string>());
            Assert.IsNull(estates.CloseEstate(estate, new List<string>()));
            Assert.AreEqual(EstateStatus.Closed, estate.Status);
        }

        [Test]
        public void AppointExecutor_RequiresRealPerson_NotDecedent()
        {
            Estate estate = OpenFor(1, "John Doe");
            Assert.IsNotNull(estates.AppointExecutor(estate, -1, new List<string>()));
            Assert.IsNotNull(estates.AppointExecutor(estate, 1, new List<string>()),
                "The decedent cannot execute their own estate.");
            Assert.IsNull(estates.AppointExecutor(estate, 5, new List<string>()));
            Assert.AreEqual(5, estate.ExecutorPersonId);
        }
    }
}
