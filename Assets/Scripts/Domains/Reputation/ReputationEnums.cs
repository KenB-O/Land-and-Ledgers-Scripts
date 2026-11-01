namespace LandLedgers.Reputation
{
    public enum ReputationSubcategory
    {
        DealTrust = 0,
        LenderTrust = 1,
        SupplierTrust = 2,
        LocalSocialTrust = 3,
        OperationalReliability = 4
    }

    public enum ReputationEventType
    {
        None = 0,
        CleanDealClosed = 1,
        DealClosedLate = 2,
        DealCollapsedByPlayer = 3,
        RenegotiatedAfterAgreement = 4,
        SellerFinancingHonored = 5,
        SellerFinancingMissed = 6,
        FailedFinancingAfterCommitment = 7,
        PostCloseStabilizationHonored = 8,
        LoanPaidOnTime = 20,
        LoanPaymentLate = 21,
        LoanDefaulted = 22,
        SupplierInvoicePaidOnTime = 40,
        SupplierInvoiceLate = 41,
        SupplierRelationshipStabilized = 42,
        WagesPaidOnTime = 60,
        WagesMissed = 61,
        EmployerCoveredInjuryRelief = 62,
        EmployerIgnoredWorkplaceInjury = 63,
        HarshEmploymentPractice = 64,
        BusinessRecoveredQuickly = 80,
        BusinessRemainedDisrupted = 81,
        BusinessReopenedReliably = 82,
        BusinessRepeatedNeglect = 83,
        SeverePublicStockout = 84,
        ChronicOverpricing = 85,
        SpoiledGoodsPublicFailure = 86,
        StorefrontRefurbished = 87,
        FairHiringPractice = 100,
        CommunityDisruption = 101,
        CommunityContribution = 102
    }

    public enum ReputationChangeReasonCode
    {
        None = 0,
        CleanDealClosed = 1,
        DealReliabilityDamaged = 2,
        RenegotiationTrustCost = 3,
        SellerFinancingReliability = 4,
        SellerFinancingMissed = 5,
        FinancingCredibilityDamaged = 6,
        PostCloseStabilization = 7,
        LenderReliability = 20,
        LoanPaymentConcern = 21,
        LoanDefault = 22,
        SupplierReliability = 40,
        SupplierPaymentConcern = 41,
        SupplierRelationshipStabilized = 42,
        LaborReliability = 60,
        WagePaymentConcern = 61,
        EmployerReliefTrust = 62,
        WorkplaceInjuryNeglected = 63,
        HarshEmploymentPractice = 64,
        OperationalRecovery = 80,
        LingeringOperationalDisruption = 81,
        ReliableReopening = 82,
        RepeatedBusinessNeglect = 83,
        PublicStockoutDamagedTrust = 84,
        ChronicOverpricingTrustCost = 85,
        SpoiledGoodsConcern = 86,
        RefurbishmentConfidence = 87,
        FairLocalPractice = 100,
        CommunityTrustDamaged = 101,
        CommunityContribution = 102
    }
}
