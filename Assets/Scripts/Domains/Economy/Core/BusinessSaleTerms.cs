using System;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum BusinessSaleReviewStage
    {
        None = 0,
        ListingReviewed = 1,
        TermsReviewed = 2,
    }

    /// <summary>UI gate requiring a separate listing review, terms review and explicit final confirmation.</summary>
    public sealed class BusinessSaleReviewFlow
    {
        public int BuildingId { get; private set; } = -1;
        public BusinessSaleReviewStage Stage { get; private set; }
        public bool CanConfirm(int buildingId) => BuildingId == buildingId && Stage == BusinessSaleReviewStage.TermsReviewed;

        public BusinessSaleReviewStage ReviewListing(int buildingId)
        {
            if (buildingId < 0) return Stage;
            if (BuildingId != buildingId || Stage == BusinessSaleReviewStage.None)
            {
                BuildingId = buildingId;
                Stage = BusinessSaleReviewStage.ListingReviewed;
            }
            else if (Stage == BusinessSaleReviewStage.ListingReviewed)
            {
                Stage = BusinessSaleReviewStage.TermsReviewed;
            }
            return Stage;
        }

        public void Reset()
        {
            BuildingId = -1;
            Stage = BusinessSaleReviewStage.None;
        }
    }

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

        /// <summary>
        /// UBC A2: the seller's person id when the seller is an NPC person
        /// (BusinessOwnerIdentity.PersonId). -1 when the seller has no person
        /// record (Town sale, or a listing that never named one). Recorded so
        /// the proceeds path can credit the actual seller, and so the terms
        /// survive save/load with the seller's identity attached.
        /// </summary>
        [SerializeField]
        private int sellerPersonId = -1;

        /// <summary>
        /// UBC A2: the cash price the buyer paid for the business, excluding
        /// any seller-financed principal (that part is a note, not cash at
        /// closing — see RecordSellerFinanceAtClose). The seller's household
        /// is credited this amount at closing; it never vanishes.
        /// </summary>
        [SerializeField, Min(0)]
        private int purchasePriceCents;

        /// <summary>
        /// UBC A2: business cash the seller withdrew before closing
        /// (previous cash minus the opening-cash target). The withdrawal is
        /// credited to the seller like the price, never destroyed. Zero when
        /// the buyer keeps all business cash on hand.
        /// </summary>
        [SerializeField, Min(0)]
        private int sellerCashWithdrawalCents;

        /// <summary>
        /// UBC A2: what happens to the former owner as a worker. The former
        /// owner is NOT silently kept on as an employee: employment requires
        /// an agreement. Until the deal flow grows a transition term, this
        /// records the factual outcome — the former owner holds no operator
        /// role after closing and no transition arrangement was agreed.
        /// </summary>
        [SerializeField]
        private string transitionArrangement = string.Empty;

        public string SellerDisplayName => sellerDisplayName ?? string.Empty;
        public string BuyerDisplayName => buyerDisplayName ?? string.Empty;
        public int IncludedCashCents => Mathf.Max(0, includedCashCents);
        public string InventoryStatement => inventoryStatement ?? string.Empty;
        public string EmployeeOfferStatement => employeeOfferStatement ?? string.Empty;
        public int ClosingDayIndex => closingDayIndex;
        public int SellerPersonId => sellerPersonId;
        public int PurchasePriceCents => Mathf.Max(0, purchasePriceCents);
        public int SellerCashWithdrawalCents => Mathf.Max(0, sellerCashWithdrawalCents);
        public string TransitionArrangement => transitionArrangement ?? string.Empty;

        /// <summary>
        /// UBC A2: total cash the seller received at closing — the purchase
        /// price plus any pre-close cash withdrawal. Derived, never stored
        /// as competing truth.
        /// </summary>
        public int SellerProceedsCents => PurchasePriceCents + SellerCashWithdrawalCents;

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
            : this(
                sellerDisplayName,
                buyerDisplayName,
                includedCashCents,
                inventoryStatement,
                employeeOfferStatement,
                closingDayIndex,
                -1,
                0,
                0,
                string.Empty)
        {
        }

        /// <summary>
        /// UBC A2: full construction, including the seller's identity and the
        /// cash the seller actually received. The 6-argument constructor
        /// above keeps the original call sites (notably the to-Town path,
        /// where no cash price changes hands) working unchanged.
        /// </summary>
        public BusinessSaleTerms(
            string sellerDisplayName,
            string buyerDisplayName,
            int includedCashCents,
            string inventoryStatement,
            string employeeOfferStatement,
            int closingDayIndex,
            int sellerPersonId,
            int purchasePriceCents,
            int sellerCashWithdrawalCents,
            string transitionArrangement)
        {
            this.sellerDisplayName = sellerDisplayName ?? string.Empty;
            this.buyerDisplayName = buyerDisplayName ?? string.Empty;
            this.includedCashCents = Mathf.Max(0, includedCashCents);
            this.inventoryStatement = inventoryStatement ?? string.Empty;
            this.employeeOfferStatement = employeeOfferStatement ?? string.Empty;
            this.closingDayIndex = closingDayIndex;
            this.sellerPersonId = sellerPersonId;
            this.purchasePriceCents = Mathf.Max(0, purchasePriceCents);
            this.sellerCashWithdrawalCents = Mathf.Max(0, sellerCashWithdrawalCents);
            this.transitionArrangement = transitionArrangement ?? string.Empty;
        }

        /// <summary>Human-readable statement of what the sale included.</summary>
        public string BuildSummary()
        {
            string sellerRef = SellerPersonId >= 0
                ? $"seller {SellerDisplayName} (P{SellerPersonId}) -> buyer {BuyerDisplayName}"
                : $"seller {SellerDisplayName} -> buyer {BuyerDisplayName}";
            string proceeds = SellerProceedsCents > 0 || PurchasePriceCents > 0 || SellerCashWithdrawalCents > 0
                ? $" Price paid: {PurchasePriceCents}c. Seller pre-close cash withdrawal: {SellerCashWithdrawalCents}c. Seller proceeds: {SellerProceedsCents}c."
                : string.Empty;
            string transition = !string.IsNullOrWhiteSpace(TransitionArrangement)
                ? $" Transition: {TransitionArrangement}"
                : string.Empty;
            return $"Sale terms: {sellerRef}. " +
                   $"Cash included: {IncludedCashCents}c. " +
                   $"Inventory: {InventoryStatement}. " +
                   $"Employees: {EmployeeOfferStatement}.{proceeds}{transition}";
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
                sellerPersonId = SellerPersonId,
                purchasePriceCents = PurchasePriceCents,
                sellerCashWithdrawalCents = SellerCashWithdrawalCents,
                transitionArrangement = TransitionArrangement,
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
                dto.closingDayIndex,
                dto.sellerPersonId,
                Mathf.Max(0, dto.purchasePriceCents),
                Mathf.Max(0, dto.sellerCashWithdrawalCents),
                dto.transitionArrangement);
        }
    }
}
