using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Estates
{
    /// <summary>NX-3C: what a bequest conveys.</summary>
    public enum BequestKind
    {
        Unspecified = 0,
        Parcel = 1,            // real property — validated against the T2F chain
        BusinessInterest = 2,  // ownership interest in a named business
        PersonalProperty = 3,  // described movables (furniture, tools, livestock)
        CashAmount = 4,        // a sum from the estate's personal property
    }

    /// <summary>NX-3C: one bequest to a named beneficiary — always a real person.</summary>
    [Serializable]
    public sealed class Bequest
    {
        public BequestKind Kind = BequestKind.Unspecified;
        public int BeneficiaryPersonId = -1;
        public string BeneficiaryName = string.Empty;
        public string ParcelId = string.Empty;          // Parcel kind
        public string BusinessInstanceId = string.Empty; // BusinessInterest kind
        public string Description = string.Empty;        // PersonalProperty kind
        public int CashCents;                            // CashAmount kind
        public bool HeldForWitnessConflict;              // interested witness — held, not voided by fiat
        public bool Distributed;

        public Bequest() { }
    }

    /// <summary>
    /// NX-3C: a last will. Historical (1870s western territories): a will is a
    /// written instrument, signed by the testator and attested by credible
    /// witnesses (two or three by the dated statutes). An interested witness —
    /// one who also takes under the will — did not always void the will, but
    /// could void their own bequest; this service holds the bequest and flags
    /// it rather than deciding the law by fiat.
    /// </summary>
    [Serializable]
    public sealed class Will
    {
        public string WillId = string.Empty;
        public int TestatorPersonId = -1;
        public string TestatorName = string.Empty;
        public int ExecutorPersonId = -1;
        public List<Bequest> Bequests = new List<Bequest>();
        public List<int> WitnessPersonIds = new List<int>();
        public int SignedDayIndex;
        public bool Revoked;
        public bool Probated;
        public bool Contested;
        public string ContestGrounds = string.Empty;
        public int ContestantPersonId = -1;

        public Will() { }
    }

    /// <summary>
    /// NX-3C: probate of testate estates, alongside the T3C intestate flow.
    /// Probate validates the will against the world: the testator is the real
    /// decedent, witnesses are real and disinterested (or flagged), devised
    /// parcels are actually held by the testator per the T2F chain. Creditors
    /// still come before devisees — debts settle first under either rule.
    /// A contested will is FLAGGED and distributions pause; the contest is a
    /// legal proceeding, never resolved by fiat here.
    /// </summary>
    public sealed class ProbateService
    {
        private readonly Dictionary<string, Will> wills =
            new Dictionary<string, Will>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Records a will. The testator and witnesses must be real persons;
        /// the caller verifies this against the population.
        /// </summary>
        public Will RecordWill(
            EntityIdRegistry ids, int testatorPersonId, string testatorName,
            int executorPersonId, List<int> witnessPersonIds, int signedDayIndex,
            PopulationState population, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (testatorPersonId < 0)
            {
                diag.Add("ProbateService.RecordWill: the testator must be a real person.");
                return null;
            }
            if (executorPersonId < 0)
            {
                diag.Add("ProbateService.RecordWill: the executor must be a real person.");
                return null;
            }
            if (executorPersonId == testatorPersonId)
            {
                diag.Add("ProbateService.RecordWill: the testator cannot execute their own will.");
                return null;
            }
            witnessPersonIds = witnessPersonIds ?? new List<int>();
            if (witnessPersonIds.Count < 2)
            {
                diag.Add("ProbateService.RecordWill: at least two attesting witnesses are required (1870s practice).");
                return null;
            }
            if (population != null)
            {
                foreach (int witnessId in witnessPersonIds)
                {
                    if (population.GetPerson(witnessId) == null)
                    {
                        diag.Add($"ProbateService.RecordWill: witness P{witnessId} is not a known person.");
                        return null;
                    }
                    if (witnessId == testatorPersonId)
                    {
                        diag.Add("ProbateService.RecordWill: the testator cannot witness their own will.");
                        return null;
                    }
                }
            }
            var will = new Will
            {
                WillId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                TestatorPersonId = testatorPersonId,
                TestatorName = testatorName ?? string.Empty,
                ExecutorPersonId = executorPersonId,
                SignedDayIndex = signedDayIndex,
            };
            will.WitnessPersonIds.AddRange(witnessPersonIds);
            wills[will.WillId] = will;
            diag.Add($"ProbateService: will {will.WillId} recorded for '{will.TestatorName}' — " +
                $"{witnessPersonIds.Count} witnesses, executor P{executorPersonId}.");
            return will;
        }

        /// <summary>Adds a bequest to a recorded will. The beneficiary must be a real person.</summary>
        public string AddBequest(Will will, Bequest bequest, PopulationState population, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (will == null) return "ProbateService.AddBequest: a will is required.";
            if (will.Probated) return "ProbateService.AddBequest: the will is already probated — no late bequests.";
            if (bequest == null || bequest.Kind == BequestKind.Unspecified)
                return "ProbateService.AddBequest: the bequest must state what it conveys.";
            if (bequest.BeneficiaryPersonId < 0)
                return "ProbateService.AddBequest: the beneficiary must be a real person — no conjured devisees.";
            if (population != null && population.GetPerson(bequest.BeneficiaryPersonId) == null)
                return $"ProbateService.AddBequest: beneficiary P{bequest.BeneficiaryPersonId} is not a known person.";
            if (bequest.Kind == BequestKind.Parcel && string.IsNullOrWhiteSpace(bequest.ParcelId))
                return "ProbateService.AddBequest: a parcel bequest must name the parcel.";
            if (bequest.Kind == BequestKind.CashAmount && bequest.CashCents <= 0)
                return "ProbateService.AddBequest: a cash bequest must state a positive sum.";
            will.Bequests.Add(bequest);
            diag.Add($"ProbateService: bequest added to will {will.WillId} — {bequest.Kind} to P{bequest.BeneficiaryPersonId}.");
            return null;
        }

        /// <summary>
        /// Probates the will against the world: testator is the decedent,
        /// devised parcels are held by the testator per the T2F chain,
        /// interested witnesses are flagged (their bequests held).
        /// </summary>
        public string ProbateWill(
            Will will, Estate estate, TitleAuthority titles,
            int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (will == null) return "ProbateService.ProbateWill: a will is required.";
            if (will.Revoked) return $"ProbateService.ProbateWill: will {will.WillId} was revoked — it conveys nothing.";
            if (will.Contested)
                return $"ProbateService.ProbateWill: will {will.WillId} is contested ({will.ContestGrounds}) — distributions pause pending the proceeding.";
            if (estate == null) return "ProbateService.ProbateWill: the estate must be opened first.";
            if (will.TestatorPersonId != estate.DecedentPersonId)
                return $"ProbateService.ProbateWill: will {will.WillId} names testator P{will.TestatorPersonId}, " +
                    $"but the estate is for P{estate.DecedentPersonId} — not this decedent's will.";

            foreach (Bequest bequest in will.Bequests)
            {
                // Interested witness: holds their bequest, flags it — the 1870s
                // rule voided the bequest more often than the will; either way
                // this service does not decide the law by fiat.
                if (will.WitnessPersonIds.Contains(bequest.BeneficiaryPersonId))
                {
                    bequest.HeldForWitnessConflict = true;
                    diag.Add($"ProbateService: bequest to P{bequest.BeneficiaryPersonId} HELD — " +
                        "beneficiary witnessed the will (interested witness; flagged, not voided by fiat).");
                }
                if (bequest.Kind == BequestKind.Parcel && titles != null)
                {
                    string holder = titles.CurrentHolder(bequest.ParcelId);
                    if (!string.Equals(holder, will.TestatorName, StringComparison.Ordinal))
                    {
                        diag.Add($"ProbateService: parcel bequest '{bequest.ParcelId}' fails — " +
                            $"held by '{holder}', not the testator. The devise conveys nothing.");
                        bequest.HeldForWitnessConflict = true; // held: invalid devise, flagged
                    }
                }
            }

            will.Probated = true;
            estate.TestateWillId = will.WillId;
            diag.Add($"ProbateService: will {will.WillId} probated day {dayIndex} — {will.Bequests.Count} bequests, " +
                "creditors still before devisees.");
            return null;
        }

        /// <summary>
        /// Records a contest by a named interested party. Distributions pause;
        /// the contest is a legal proceeding, never resolved here by fiat.
        /// </summary>
        public string ContestWill(Will will, int contestantPersonId, string grounds,
            PopulationState population, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (will == null) return "ProbateService.ContestWill: a will is required.";
            if (contestantPersonId < 0)
                return "ProbateService.ContestWill: the contestant must be a real person.";
            if (population != null && population.GetPerson(contestantPersonId) == null)
                return $"ProbateService.ContestWill: contestant P{contestantPersonId} is not a known person.";
            if (string.IsNullOrWhiteSpace(grounds))
                return "ProbateService.ContestWill: the grounds must be stated.";
            will.Contested = true;
            will.ContestantPersonId = contestantPersonId;
            will.ContestGrounds = grounds;
            diag.Add($"ProbateService: will {will.WillId} CONTESTED by P{contestantPersonId} ({grounds}) — distributions pause.");
            return null;
        }

        /// <summary>
        /// Distributes one parcel bequest through the T2F chain. Refuses while
        /// estate debts remain (creditors first), while the will is contested,
        /// or when the bequest is held for witness conflict / invalid devise.
        /// </summary>
        public string DistributeBequest(
            Will will, Estate estate, Bequest bequest,
            EntityIdRegistry ids, TitleAuthority titles, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (will == null || estate == null || bequest == null)
                return "ProbateService.DistributeBequest: will, estate, and bequest are required.";
            if (!will.Probated) return $"ProbateService.DistributeBequest: will {will.WillId} is not probated.";
            if (will.Contested) return $"ProbateService.DistributeBequest: will {will.WillId} is contested — distributions pause.";
            if (will.Revoked) return $"ProbateService.DistributeBequest: will {will.WillId} was revoked.";
            if (bequest.HeldForWitnessConflict)
                return "ProbateService.DistributeBequest: this bequest is held (witness conflict / invalid devise) — flagged, not distributed.";
            if (bequest.Distributed) return "ProbateService.DistributeBequest: already distributed.";
            if (bequest.Kind != BequestKind.Parcel)
                return $"ProbateService.DistributeBequest: {bequest.Kind} bequests distribute through their own custodians (caller records the transfer).";
            if (estate.EstateDebtIds.Count > 0)
                return $"ProbateService.DistributeBequest: {estate.EstateDebtIds.Count} estate debts unsettled — creditors before devisees.";
            if (!estate.ParcelIds.Contains(bequest.ParcelId))
                return $"ProbateService.DistributeBequest: '{bequest.ParcelId}' is not inventoried in estate {estate.EstateId}.";

            string problem = titles.TransferTitle(ids, bequest.ParcelId, bequest.BeneficiaryName,
                TitleBasis.Inheritance, will.WillId, dayIndex, "testate distribution by probated will", diag);
            if (problem != null) return problem;
            bequest.Distributed = true;
            estate.ParcelIds.Remove(bequest.ParcelId);
            estate.Status = EstateStatus.Distributing;
            diag.Add($"ProbateService: parcel '{bequest.ParcelId}' → {bequest.BeneficiaryName} by will {will.WillId}.");
            return null;
        }

        public Will Get(string willId)
        {
            return wills.TryGetValue(willId, out Will will) ? will : null;
        }
    }
}
