using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Farming.Delivery;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Primitives;
using LandLedgers.Persistence;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// FVS-4: the save aggregate for farm vertical-slice state. Follows the
    /// CLN-1 pattern: the SimulationSystemsHub owns one instance and calls
    /// CaptureSaveDto/LoadFromSaveDto at the save boundary. Owns no sim logic.
    /// </summary>
    [Serializable]
    public sealed class FarmSliceSystems
    {
        private DairyChain dairyChain = new DairyChain();
        private FeedLoop feedLoop = new FeedLoop();
        private PoultryChain poultryChain = new PoultryChain();
        private readonly List<DeliveryJob> deliveryJobs = new List<DeliveryJob>();
        private readonly List<FarmConstructionProject> constructionProjects = new List<FarmConstructionProject>();
        private CropChain cropChain;

        public DairyChain Dairy => dairyChain ??= new DairyChain();
        public FeedLoop Feed => feedLoop ??= new FeedLoop();
        public PoultryChain Poultry => poultryChain ??= new PoultryChain();
        public List<DeliveryJob> DeliveryJobs => deliveryJobs;
        public List<FarmConstructionProject> ConstructionProjects => constructionProjects;
        /// <summary>CRP-2: the crop chain (lazy; shares its own field authority).</summary>
        public CropChain Crops => cropChain ??= new CropChain(new CropFieldAuthority());

        public void SetFeedLoop(FeedLoop loop)
        {
            if (loop != null) feedLoop = loop;
        }

        public FarmSliceSaveDto CaptureSaveDto()
        {
            return new FarmSliceSaveDto
            {
                dairyCows = Dairy.CaptureSaveDto().cows,
                feedStockUnits = Feed.FeedStockUnits,
                feedSource = Feed.LastFeedSource,
                hens = new List<EntityId>(Poultry.HenIds),
                deliveryJobs = new List<DeliveryJob>(deliveryJobs),
                constructionProjects = new List<FarmConstructionProject>(constructionProjects),
                crop = Crops.CaptureSaveDto(),
            };
        }

        public void LoadFromSaveDto(FarmSliceSaveDto dto)
        {
            deliveryJobs.Clear();
            constructionProjects.Clear();
            if (dto == null) return;

            Dairy.LoadFromSaveDto(new DairyChainSaveDto { cows = dto.dairyCows ?? new List<DairyCowState>() });
            feedLoop = new FeedLoop(Math.Max(0, dto.feedStockUnits));
            feedLoop.LastFeedSource = dto.feedSource ?? string.Empty;
            if (dto.hens != null)
            {
                foreach (var henId in dto.hens) Poultry.RegisterHen(henId);
            }
            if (dto.deliveryJobs != null) deliveryJobs.AddRange(dto.deliveryJobs);
            if (dto.constructionProjects != null) constructionProjects.AddRange(dto.constructionProjects);
            if (dto.crop != null) Crops.LoadFromSaveDto(dto.crop);
        }
    }
}
