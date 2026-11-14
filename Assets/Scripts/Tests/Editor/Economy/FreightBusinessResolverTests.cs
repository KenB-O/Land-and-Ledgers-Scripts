using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Q33 (A2 plan 30, note 111 §13): one resolved mapping for the freight
    /// business. The authored instance id is a preference, the business type
    /// is the requirement. Pins the FreightBusinessResolver contract on real
    /// BusinessInstanceState objects plus the real resolver — no mirror.
    /// </summary>
    [TestFixture]
    public sealed class FreightBusinessResolverTests
    {
        private static BusinessInstanceState BuildBusiness(string instanceId, BusinessType type)
        {
            BusinessProfileDefinition profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            return BusinessInstanceState.Create(instanceId, profile, -1, BusinessOwnerIdentity.Town(), type);
        }

        [Test]
        public void PreferredIdWins()
        {
            var businesses = new List<BusinessInstanceState>
            {
                BuildBusiness("livery_freight_002", BusinessType.LiveryFreight),
                BuildBusiness("livery_freight_003", BusinessType.LiveryFreight),
                BuildBusiness("bakery_001", BusinessType.Bakery)
            };
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(businesses, out BusinessInstanceState freight);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(freight);
            Assert.AreEqual("livery_freight_003", freight.InstanceId);
        }

        [Test]
        public void FallbackToOnlyLiveryFreight()
        {
            var businesses = new List<BusinessInstanceState>
            {
                BuildBusiness("bakery_001", BusinessType.Bakery),
                BuildBusiness("livery_freight_007", BusinessType.LiveryFreight)
            };
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(businesses, out BusinessInstanceState freight);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(freight);
            Assert.AreEqual("livery_freight_007", freight.InstanceId);
        }

        [Test]
        public void FallbackOrderIsListOrder()
        {
            var businesses = new List<BusinessInstanceState>
            {
                BuildBusiness("livery_freight_007", BusinessType.LiveryFreight),
                BuildBusiness("livery_freight_009", BusinessType.LiveryFreight)
            };
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(businesses, out BusinessInstanceState freight);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(freight);
            Assert.AreEqual("livery_freight_007", freight.InstanceId);
        }

        [Test]
        public void NoLiveryFreightReturnsFalse()
        {
            var businesses = new List<BusinessInstanceState>
            {
                BuildBusiness("bakery_001", BusinessType.Bakery),
                BuildBusiness("store_001", BusinessType.GeneralStore)
            };
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(businesses, out BusinessInstanceState freight);
            Assert.IsFalse(resolved);
            Assert.IsNull(freight);
        }

        [Test]
        public void PreferredIdWithWrongTypeDoesNotResolve()
        {
            var businesses = new List<BusinessInstanceState>
            {
                BuildBusiness("livery_freight_003", BusinessType.Bakery),
                BuildBusiness("livery_freight_007", BusinessType.LiveryFreight)
            };
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(businesses, out BusinessInstanceState freight);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(freight);
            Assert.AreEqual("livery_freight_007", freight.InstanceId);

            // Second half: the mis-typed 003 never resolves on its own.
            var misTypedOnly = new List<BusinessInstanceState>
            {
                BuildBusiness("livery_freight_003", BusinessType.Bakery)
            };
            bool resolvedAlone = FreightBusinessResolver.TryResolveFreightBusiness(misTypedOnly, out BusinessInstanceState freightAlone);
            Assert.IsFalse(resolvedAlone);
            Assert.IsNull(freightAlone);
        }

        [Test]
        public void NullListReturnsFalse()
        {
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(null, out BusinessInstanceState freight);
            Assert.IsFalse(resolved);
            Assert.IsNull(freight);
        }

        [Test]
        public void EmptyListReturnsFalse()
        {
            var businesses = new List<BusinessInstanceState>();
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(businesses, out BusinessInstanceState freight);
            Assert.IsFalse(resolved);
            Assert.IsNull(freight);
        }

        [Test]
        public void PreferredIdMatchesCaseInsensitively()
        {
            var businesses = new List<BusinessInstanceState>
            {
                BuildBusiness("LIVERY_FREIGHT_003", BusinessType.LiveryFreight)
            };
            bool resolved = FreightBusinessResolver.TryResolveFreightBusiness(businesses, out BusinessInstanceState freight);
            Assert.IsTrue(resolved);
            Assert.IsNotNull(freight);
            Assert.AreEqual("LIVERY_FREIGHT_003", freight.InstanceId);
        }
    }
}
