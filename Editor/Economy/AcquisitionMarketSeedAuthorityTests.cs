using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Tests.Economy
{
    public sealed class AcquisitionMarketSeedAuthorityTests
    {
        [Test]
        public void FreshWorldSeedAuthorityDerivesMarketSeedFromWorldSeed()
        {
            AcquisitionMarketManager market = CreateMarket();
            try
            {
                market.ApplyFreshWorldSeedAuthority(1886);

                AcquisitionSaveDto dto = market.CaptureSaveDto();

                Assert.That(dto.marketSeed, Is.EqualTo(WorldSeedAuthority.DeriveChildSeed(1886, WorldSeedAuthority.AcquisitionMarketSeedCategory)));
            }
            finally
            {
                Destroy(market);
            }
        }

        [Test]
        public void LoadFromSavePreservesSavedMarketSeed()
        {
            AcquisitionMarketManager market = CreateMarket();
            try
            {
                market.ApplyFreshWorldSeedAuthority(1886);
                market.LoadFromSaveDto(new AcquisitionSaveDto
                {
                    marketSeed = 2468,
                    maxLandListings = 4,
                    maxBusinessListings = 2
                });

                AcquisitionSaveDto dto = market.CaptureSaveDto();

                Assert.That(dto.marketSeed, Is.EqualTo(2468));
            }
            finally
            {
                Destroy(market);
            }
        }

        private static AcquisitionMarketManager CreateMarket()
        {
            GameObject root = new("Acquisition Market Seed Authority Test");
            return root.AddComponent<AcquisitionMarketManager>();
        }

        private static void Destroy(AcquisitionMarketManager market)
        {
            if (market != null)
            {
                Object.DestroyImmediate(market.gameObject);
            }
        }
    }
}
