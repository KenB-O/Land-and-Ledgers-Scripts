using System;
using System.Collections.Generic;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy.Farming
{
    /// <summary>
    /// CLN-1: the farm sale-flow ledger. Records executed FRM sales (produce lots,
    /// livestock sales, wholesale purchases) so the transaction history round-trips
    /// through the save pipeline. Lots sell exactly once; the sold flag persists.
    /// </summary>
    [Serializable]
    public sealed class FarmFlowLedger
    {
        [SerializeField]
        private List<FarmProduceLot> produceLots = new List<FarmProduceLot>();

        [SerializeField]
        private List<FarmProduceSale> produceSales = new List<FarmProduceSale>();

        [SerializeField]
        private List<FarmLivestockSale> livestockSales = new List<FarmLivestockSale>();

        [SerializeField]
        private List<WholesaleCutPurchase> wholesalePurchases = new List<WholesaleCutPurchase>();

        public IReadOnlyList<FarmProduceLot> ProduceLots => produceLots;
        public IReadOnlyList<FarmProduceSale> ProduceSales => produceSales;
        public IReadOnlyList<FarmLivestockSale> LivestockSales => livestockSales;
        public IReadOnlyList<WholesaleCutPurchase> WholesalePurchases => wholesalePurchases;

        public void RecordProduceLot(FarmProduceLot lot)
        {
            if (lot != null)
            {
                produceLots.Add(lot);
            }
        }

        public void RecordProduceSale(FarmProduceSale sale)
        {
            if (sale != null)
            {
                produceSales.Add(sale);
            }
        }

        public void RecordLivestockSale(FarmLivestockSale sale)
        {
            if (sale != null)
            {
                livestockSales.Add(sale);
            }
        }

        public void RecordWholesalePurchase(WholesaleCutPurchase purchase)
        {
            if (purchase != null)
            {
                wholesalePurchases.Add(purchase);
            }
        }

        /// <summary>CLN-1: captures farm flow state for the save pipeline.</summary>
        public FarmFlowSaveDto CaptureSaveDto()
        {
            return new FarmFlowSaveDto
            {
                produceLots = new List<FarmProduceLot>(produceLots ?? new List<FarmProduceLot>()),
                produceSales = new List<FarmProduceSale>(produceSales ?? new List<FarmProduceSale>()),
                livestockSales = new List<FarmLivestockSale>(livestockSales ?? new List<FarmLivestockSale>()),
                wholesalePurchases = new List<WholesaleCutPurchase>(wholesalePurchases ?? new List<WholesaleCutPurchase>()),
            };
        }

        /// <summary>CLN-1: restores farm flow state from the save pipeline.</summary>
        public void LoadFromSaveDto(FarmFlowSaveDto dto)
        {
            produceLots.Clear();
            produceSales.Clear();
            livestockSales.Clear();
            wholesalePurchases.Clear();
            if (dto == null)
            {
                return;
            }

            if (dto.produceLots != null)
            {
                produceLots.AddRange(dto.produceLots);
            }

            if (dto.produceSales != null)
            {
                produceSales.AddRange(dto.produceSales);
            }

            if (dto.livestockSales != null)
            {
                livestockSales.AddRange(dto.livestockSales);
            }

            if (dto.wholesalePurchases != null)
            {
                wholesalePurchases.AddRange(dto.wholesalePurchases);
            }
        }
    }
}
