using System;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy
{
    /// <summary>
    /// P1: explicit business sale terms (Canon XXVII Part IX §9.1 — "Cash inclusion
    /// is an explicit transaction term"). Sale of a business does NOT silently
    /// transfer all business cash: the agreement must state what cash, working
    /// capital, inventory and other assets are included, and what the buyer offers
    /// continuing workers (§9.3: employees remain independent persons; the buyer
    /// may offer continued employment). Recorded at transfer by
    /// SharedBusinessRuntimeManager.TryTransferBusinessToTown and persisted on the
    /// business — the terms travel with the business, never silently.
    /// </summary>
    [Serializable]
    public sealed class BusinessSaleTerms
    {
        [SerializeField]
        private string sellerDisplayName = string.Empty;

        [SerializeField]
        private string buyerDisplayName = string.Empty;

        [SerializeField, Min(0)]
        private int includedCashCents;

        [SerializeField]
        private string inventoryStatement = string.Empty;

        [SerializeField]
        private string employeeOfferStatement = string.Empty;

        [SerializeField]
        private int closingDayIndex = -1;

        public string SellerDisplayName => sellerDisplayName ?? string.Empty;
        public string BuyerDisplayName => buyerDisplayName ?? string.Empty;
        public int IncludedCashCents => Mathf.Max(0, includedCashCents);
        public string InventoryStatement => inventoryStatement ?? string.Empty;
        public string EmployeeOfferStatement => employeeOfferStatement ?? string.Empty;
        public int ClosingDayIndex => closingDayIndex;

        /// <summary>Default construction for Unity serialization.</summary>
        public BusinessSaleTerms()
        {
        }

        public BusinessSaleTerms(
            string sellerDisplayName,
            string buyerDisplayName,
            int includedCashCents,
            string inventoryStatement,
            string employeeOfferStatement,
            int closingDayIndex)
        {
            this.sellerDisplayName = sellerDisplayName ?? string.Empty;
            this.buyerDisplayName = buyerDisplayName ?? string.Empty;
            this.includedCashCents = Mathf.Max(0, includedCashCents);
            this.inventoryStatement = inventoryStatement ?? string.Empty;
            this.employeeOfferStatement = employeeOfferStatement ?? string.Empty;
            this.closingDayIndex = closingDayIndex;
        }

        /// <summary>Human-readable statement of what the sale included.</summary>
        public string BuildSummary()
        {
            return $"Sale terms: seller {SellerDisplayName} -> buyer {BuyerDisplayName}. " +
                   $"Cash included: {IncludedCashCents}c. " +
                   $"Inventory: {InventoryStatement}. " +
                   $"Employees: {EmployeeOfferStatement}.";
        }

        /// <summary>CLN-1: captures the terms for the save pipeline.</summary>
        public BusinessSaleTermsSaveDto CaptureSaveDto()
        {
            return new BusinessSaleTermsSaveDto
            {
                sellerDisplayName = SellerDisplayName,
                buyerDisplayName = BuyerDisplayName,
                includedCashCents = IncludedCashCents,
                inventoryStatement = InventoryStatement,
                employeeOfferStatement = EmployeeOfferStatement,
                closingDayIndex = ClosingDayIndex,
            };
        }

        /// <summary>CLN-1: restores the terms from the save pipeline.</summary>
        public static BusinessSaleTerms FromSaveDto(BusinessSaleTermsSaveDto dto)
        {
            if (dto == null)
            {
                return new BusinessSaleTerms();
            }

            return new BusinessSaleTerms(
                dto.sellerDisplayName,
                dto.buyerDisplayName,
                Mathf.Max(0, dto.includedCashCents),
                dto.inventoryStatement,
                dto.employeeOfferStatement,
                dto.closingDayIndex);
        }
    }
}
