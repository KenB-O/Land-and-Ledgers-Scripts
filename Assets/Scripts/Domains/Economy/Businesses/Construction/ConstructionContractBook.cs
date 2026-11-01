using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D4G: the town's building-contract registry — bid requests, competing
    /// bids, and the written contracts that accepted bids become. Pure record
    /// state machine: it records money obligations but never moves money, and
    /// records material provenance requirements but never consumes material.
    ///
    /// Canon: Part VI §7.2 (bidding and estimating), §7.3 (backlog is not
    /// revenue; payment timing as contract data), §7.5 (capacity from real
    /// crew — overbooking creates delays, losses, reputation damage).
    ///
    /// Integration: builder capacity arrives as a
    /// <see cref="ConstructionContractCapacityReading"/> — real crew numbers
    /// read from the existing workforce model (BusinessRuntimeState worker
    /// slots) by an adapter outside this package. The book never invents crew.
    /// Progress billing itself is the D4H package; this book defines the
    /// <see cref="IConstructionProgressBilling"/> interface D4H will consume.
    /// </summary>
    public sealed class ConstructionContractBook
    {
        /// <summary>
        /// D4G: repairs estimated below this value belong on a repair work
        /// order, not a written building contract. CALIBRATION — the canon
        /// sets no threshold. $25.00.
        /// </summary>
        public const int MinimumWrittenContractPriceCents = 2500;

        private readonly Dictionary<string, ConstructionBidRequest> requests =
            new Dictionary<string, ConstructionBidRequest>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConstructionBid> bids =
            new Dictionary<string, ConstructionBid>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConstructionContract> contracts =
            new Dictionary<string, ConstructionContract>(StringComparer.Ordinal);

        private int nextRequestNumber = 1;
        private int nextBidNumber = 1;
        private int nextContractNumber = 1;

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public int RequestCount => requests.Count;
        public int BidCount => bids.Count;
        public int ContractCount => contracts.Count;

        public ConstructionBidRequest FindRequest(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return null;
            requests.TryGetValue(requestId, out ConstructionBidRequest request);
            return request;
        }

        public ConstructionBid FindBid(string bidId)
        {
            if (string.IsNullOrWhiteSpace(bidId)) return null;
            bids.TryGetValue(bidId, out ConstructionBid bid);
            return bid;
        }

        public ConstructionContract FindContract(string contractId)
        {
            if (string.IsNullOrWhiteSpace(contractId)) return null;
            contracts.TryGetValue(contractId, out ConstructionContract contract);
            return contract;
        }

        public List<ConstructionBid> BidsForRequest(string requestId)
        {
            var result = new List<ConstructionBid>();
            foreach (ConstructionBid bid in bids.Values)
            {
                if (bid != null && string.Equals(bid.RequestId, requestId, StringComparison.Ordinal))
                    result.Add(bid);
            }
            return result;
        }

        public List<ConstructionContract> ActiveContractsForBuilder(string builderBusinessId)
        {
            var result = new List<ConstructionContract>();
            foreach (ConstructionContract contract in contracts.Values)
            {
                if (contract != null && contract.IsActive
                    && contract.BuilderParty != null
                    && string.Equals(contract.BuilderParty.BusinessInstanceId, builderBusinessId, StringComparison.Ordinal))
                    result.Add(contract);
            }
            return result;
        }

        /// <summary>
        /// D4G: an owner (real person or real business) describes works and
        /// invites bids. Repairs below the written-contract threshold are
        /// refused — they belong on a repair work order. A one-builder invite
        /// list is a direct commission; an empty invite list is an open call.
        /// </summary>
        public ConstructionBidRequest OpenBidRequest(
            ConstructionContractParty ownerParty,
            ConstructionWorksKind worksKind,
            string worksDescription,
            string specSummary,
            int estimatedValueCents,
            int bidDeadlineDayIndex,
            List<string> invitedBuilderBusinessIds,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (ownerParty == null || !ownerParty.IsReal)
            {
                diag.Add("ConstructionContractBook: a bid request needs a real owner party — anonymous requests refused.");
                return null;
            }
            if (worksKind == ConstructionWorksKind.Unspecified)
            {
                diag.Add("ConstructionContractBook: the works kind must be named.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(worksDescription))
            {
                diag.Add("ConstructionContractBook: the works must be described in words.");
                return null;
            }
            if (worksKind == ConstructionWorksKind.Repair
                && estimatedValueCents > 0
                && estimatedValueCents < MinimumWrittenContractPriceCents)
            {
                diag.Add($"ConstructionContractBook: a {FormatMoney(estimatedValueCents)} repair is below the " +
                    $"{FormatMoney(MinimumWrittenContractPriceCents)} written-contract threshold — use a repair work order, not a building contract.");
                return null;
            }

            var request = new ConstructionBidRequest
            {
                RequestId = $"RBID-{nextRequestNumber++:D4}",
                OwnerParty = ownerParty,
                WorksKind = worksKind,
                WorksDescription = worksDescription,
                SpecSummary = specSummary ?? string.Empty,
                EstimatedValueCents = Math.Max(0, estimatedValueCents),
                CreatedDayIndex = dayIndex,
                BidDeadlineDayIndex = bidDeadlineDayIndex,
                Status = ConstructionBidRequestStatus.Open,
            };
            if (invitedBuilderBusinessIds != null)
            {
                foreach (string id in invitedBuilderBusinessIds)
                {
                    if (!string.IsNullOrWhiteSpace(id)) request.InvitedBuilderBusinessIds.Add(id);
                }
            }
            requests.Add(request.RequestId, request);
            diag.Add($"ConstructionContractBook: {request.RequestId} opened — {ownerParty.Describe()} seeks bids for {worksDescription}.");
            return request;
        }

        /// <summary>
        /// D4G: a real Builder business submits a competing bid. Refuses
        /// anonymous builders, late bids, uninvited builders, and a second
        /// live bid from the same builder on the same request.
        /// </summary>
        public ConstructionBid SubmitBid(
            string requestId,
            string builderBusinessId,
            string builderDisplayName,
            ConstructionContractPriceBasis priceBasis,
            int priceCents,
            int offeredStartDayIndex,
            int offeredDurationDays,
            ConstructionPaymentTerms paymentTerms,
            List<ConstructionBidEstimateLine> estimateLines,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionBidRequest request = FindRequest(requestId);
            if (request == null)
            {
                diag.Add($"ConstructionContractBook: unknown bid request '{requestId}'.");
                return null;
            }
            if (request.Status != ConstructionBidRequestStatus.Open)
            {
                diag.Add($"ConstructionContractBook: {requestId} is {request.Status}, not Open — bids closed.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(builderBusinessId) || string.IsNullOrWhiteSpace(builderDisplayName))
            {
                diag.Add("ConstructionContractBook: a bid needs a real, named builder — anonymous bids refused.");
                return null;
            }
            if (!request.InvitesBuilder(builderBusinessId))
            {
                diag.Add($"ConstructionContractBook: {builderDisplayName} was not invited to bid on {requestId}.");
                return null;
            }
            if (request.BidDeadlineDayIndex >= 0 && dayIndex > request.BidDeadlineDayIndex)
            {
                diag.Add($"ConstructionContractBook: {requestId} bid deadline was day {request.BidDeadlineDayIndex} — late bid refused.");
                return null;
            }
            foreach (ConstructionBid existing in BidsForRequest(requestId))
            {
                if (string.Equals(existing.BuilderBusinessId, builderBusinessId, StringComparison.Ordinal) && existing.IsLive)
                {
                    diag.Add($"ConstructionContractBook: {builderDisplayName} already holds live bid {existing.BidId} on {requestId} — withdraw it before rebidding.");
                    return null;
                }
            }
            if (priceBasis == ConstructionContractPriceBasis.Unspecified)
            {
                diag.Add("ConstructionContractBook: the bid must name its price basis (lump sum, cost-plus-percentage, cost-plus-fixed-fee).");
                return null;
            }
            if (priceCents <= 0)
            {
                diag.Add("ConstructionContractBook: the bid price must be positive.");
                return null;
            }

            var bid = new ConstructionBid
            {
                BidId = $"BID-{nextBidNumber++:D4}",
                RequestId = requestId,
                BuilderBusinessId = builderBusinessId,
                BuilderDisplayName = builderDisplayName,
                Status = ConstructionBidStatus.Submitted,
                PriceBasis = priceBasis,
                PriceCents = priceCents,
                OfferedStartDayIndex = offeredStartDayIndex,
                OfferedDurationDays = Math.Max(1, offeredDurationDays),
                PaymentTerms = paymentTerms ?? new ConstructionPaymentTerms(),
                SubmittedDayIndex = dayIndex,
            };
            if (estimateLines != null)
            {
                foreach (ConstructionBidEstimateLine line in estimateLines)
                {
                    if (line == null) continue;
                    bid.EstimateLines.Add(new ConstructionBidEstimateLine
                    {
                        ComponentName = line.ComponentName ?? string.Empty,
                        AmountCents = Math.Max(0, line.AmountCents),
                    });
                }
            }
            bids.Add(bid.BidId, bid);
            diag.Add($"ConstructionContractBook: {bid.BidId} submitted by {builderDisplayName} on {requestId} — " +
                $"{FormatMoney(priceCents)} ({priceBasis}).");
            return bid;
        }

        /// <summary>
        /// D4G: sets cost-plus fee detail on a bid before it is accepted.
        /// Only meaningful for cost-plus bids; refused otherwise.
        /// </summary>
        public string SetBidCostPlusTerms(string bidId, int estimatedCostCents, int feePercent, int fixedFeeCents, List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionBid bid = FindBid(bidId);
            if (bid == null) return $"ConstructionContractBook: unknown bid '{bidId}'.";
            if (!bid.IsLive) return $"ConstructionContractBook: {bidId} is {bid.Status}, not live.";
            if (bid.PriceBasis != ConstructionContractPriceBasis.CostPlusPercentage
                && bid.PriceBasis != ConstructionContractPriceBasis.CostPlusFixedFee)
                return $"ConstructionContractBook: {bidId} is {bid.PriceBasis} — cost-plus terms do not apply.";
            if (estimatedCostCents <= 0) return $"ConstructionContractBook: {bidId} — the cost estimate must be positive.";
            bid.EstimatedCostCents = estimatedCostCents;
            bid.CostPlusFeePercent = Math.Max(0, feePercent);
            bid.CostPlusFixedFeeCents = Math.Max(0, fixedFeeCents);
            diag.Add($"ConstructionContractBook: {bidId} cost-plus terms recorded — estimate {FormatMoney(estimatedCostCents)}, " +
                $"fee {bid.CostPlusFeePercent}%, fixed fee {FormatMoney(bid.CostPlusFixedFeeCents)}.");
            return null;
        }

        public string WithdrawBid(string bidId, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionBid bid = FindBid(bidId);
            if (bid == null) return $"ConstructionContractBook: unknown bid '{bidId}'.";
            if (!bid.IsLive) return $"ConstructionContractBook: {bidId} is {bid.Status}, not live — nothing to withdraw.";
            bid.Status = ConstructionBidStatus.Withdrawn;
            diag.Add($"ConstructionContractBook: {bidId} withdrawn by {bid.BuilderDisplayName} on day {dayIndex} — retained as history.");
            return null;
        }

        /// <summary>
        /// D4G: the owner accepts a bid — the acceptance CREATES the written
        /// contract. The winning bid becomes Accepted; every other live bid on
        /// the request becomes Rejected and is RETAINED as history. Refuses
        /// when the builder's real crew cannot carry another contract
        /// (Canon §7.5).
        /// </summary>
        public ConstructionContract AcceptBid(
            string bidId,
            ConstructionContractCapacityReading capacity,
            List<string> stageNames,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionBid bid = FindBid(bidId);
            if (bid == null)
            {
                diag.Add($"ConstructionContractBook: unknown bid '{bidId}'.");
                return null;
            }
            if (!bid.IsLive)
            {
                diag.Add($"ConstructionContractBook: {bidId} is {bid.Status}, not live — only a live bid can be accepted.");
                return null;
            }
            ConstructionBidRequest request = FindRequest(bid.RequestId);
            if (request == null)
            {
                diag.Add($"ConstructionContractBook: {bidId} names unknown request '{bid.RequestId}'.");
                return null;
            }
            if (request.Status != ConstructionBidRequestStatus.Open)
            {
                diag.Add($"ConstructionContractBook: {request.RequestId} is {request.Status}, not Open — the award window is closed.");
                return null;
            }

            int activeContracts = ActiveContractsForBuilder(bid.BuilderBusinessId).Count;
            string capacityRefusal = ConstructionContractCapacityRules.CheckCapacity(capacity, activeContracts);
            if (capacityRefusal != null)
            {
                diag.Add(capacityRefusal);
                return null;
            }
            if (capacity != null && !string.Equals(capacity.BuilderBusinessId, bid.BuilderBusinessId, StringComparison.Ordinal))
            {
                diag.Add("ConstructionContractBook: the capacity reading is for a different builder — refusing rather than guessing.");
                return null;
            }

            var contract = new ConstructionContract { ContractId = $"CTR-{nextContractNumber++:D4}" };
            string formRefusal = ConstructionContract.FormFromAcceptedBid(contract, request, bid, stageNames, dayIndex, diag);
            if (formRefusal != null)
            {
                diag.Add(formRefusal);
                return null;
            }

            bid.Status = ConstructionBidStatus.Accepted;
            request.Status = ConstructionBidRequestStatus.Awarded;
            request.AwardedBidId = bid.BidId;

            foreach (ConstructionBid rival in BidsForRequest(request.RequestId))
            {
                if (rival != null && rival.IsLive && !string.Equals(rival.BidId, bid.BidId, StringComparison.Ordinal))
                {
                    rival.Status = ConstructionBidStatus.Rejected;
                    rival.RejectionReason = $"Owner accepted {bid.BidId} ({bid.BuilderDisplayName}).";
                    diag.Add($"ConstructionContractBook: rival bid {rival.BidId} ({rival.BuilderDisplayName}) rejected — retained as history.");
                }
            }

            contracts.Add(contract.ContractId, contract);
            diag.Add($"ConstructionContractBook: {contract.ContractId} formed from {bid.BidId}.");
            return contract;
        }

        public string CancelBidRequest(string requestId, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            ConstructionBidRequest request = FindRequest(requestId);
            if (request == null) return $"ConstructionContractBook: unknown bid request '{requestId}'.";
            if (request.Status != ConstructionBidRequestStatus.Open)
                return $"ConstructionContractBook: {requestId} is {request.Status}, not Open — nothing to cancel.";
            request.Status = ConstructionBidRequestStatus.Cancelled;
            foreach (ConstructionBid bid in BidsForRequest(requestId))
            {
                if (bid != null && bid.IsLive)
                {
                    bid.Status = ConstructionBidStatus.Rejected;
                    bid.RejectionReason = $"Request {requestId} cancelled by the owner on day {dayIndex}.";
                }
            }
            diag.Add($"ConstructionContractBook: {requestId} cancelled on day {dayIndex} — live bids rejected, retained as history.");
            return null;
        }

        /// <summary>
        /// D4G: explicit expiry sweep — requests past their bid deadline with
        /// no award become Expired. Nothing expires automatically.
        /// </summary>
        public int SweepExpiredRequests(int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            int swept = 0;
            foreach (ConstructionBidRequest request in requests.Values)
            {
                if (request == null || request.Status != ConstructionBidRequestStatus.Open) continue;
                if (request.BidDeadlineDayIndex < 0 || dayIndex <= request.BidDeadlineDayIndex) continue;
                request.Status = ConstructionBidRequestStatus.Expired;
                swept++;
                diag.Add($"ConstructionContractBook: {request.RequestId} expired on day {dayIndex} — no award before the deadline.");
            }
            return swept;
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (Math.Max(0, cents) / 100f).ToString("N2");
        }

        // ---------- save DTO (inside the owning book class) ----------

        [Serializable]
        public sealed class ConstructionContractBookSaveDto
        {
            public List<ConstructionBidRequest> Requests = new List<ConstructionBidRequest>();
            public List<ConstructionBid> Bids = new List<ConstructionBid>();
            public List<ConstructionContract.ConstructionContractSaveDto> Contracts = new List<ConstructionContract.ConstructionContractSaveDto>();
            public int NextRequestNumber = 1;
            public int NextBidNumber = 1;
            public int NextContractNumber = 1;
        }

        public ConstructionContractBookSaveDto CaptureSaveDto()
        {
            var dto = new ConstructionContractBookSaveDto
            {
                NextRequestNumber = nextRequestNumber,
                NextBidNumber = nextBidNumber,
                NextContractNumber = nextContractNumber,
            };
            foreach (ConstructionBidRequest request in requests.Values)
            {
                if (request != null) dto.Requests.Add(request);
            }
            foreach (ConstructionBid bid in bids.Values)
            {
                if (bid != null) dto.Bids.Add(bid);
            }
            foreach (ConstructionContract contract in contracts.Values)
            {
                if (contract != null) dto.Contracts.Add(contract.CaptureSaveDto());
            }
            return dto;
        }

        public void LoadFromSaveDto(ConstructionContractBookSaveDto dto)
        {
            requests.Clear();
            bids.Clear();
            contracts.Clear();
            if (dto == null)
            {
                nextRequestNumber = 1;
                nextBidNumber = 1;
                nextContractNumber = 1;
                return;
            }
            foreach (ConstructionBidRequest request in dto.Requests)
            {
                if (request != null && !string.IsNullOrWhiteSpace(request.RequestId))
                    requests[request.RequestId] = request;
            }
            foreach (ConstructionBid bid in dto.Bids)
            {
                if (bid != null && !string.IsNullOrWhiteSpace(bid.BidId))
                    bids[bid.BidId] = bid;
            }
            foreach (ConstructionContract.ConstructionContractSaveDto contractDto in dto.Contracts)
            {
                ConstructionContract contract = ConstructionContract.FromSaveDto(contractDto);
                if (contract != null && !string.IsNullOrWhiteSpace(contract.ContractId))
                    contracts[contract.ContractId] = contract;
            }
            nextRequestNumber = Math.Max(1, dto.NextRequestNumber);
            nextBidNumber = Math.Max(1, dto.NextBidNumber);
            nextContractNumber = Math.Max(1, dto.NextContractNumber);
        }
    }
}
