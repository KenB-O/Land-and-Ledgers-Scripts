using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Estates
{
    /// <summary>T3C: estate lifecycle.</summary>
    public enum EstateStatus
    {
        Unspecified = 0,
        Open = 1,        // heirs identified, assets inventoried
        DebtsSettling = 2, // creditors being paid from the estate
        Distributing = 3,  // titles transferring to heirs
        Closed = 4,        // debts settled, titles transferred, decedent archived
    }

    /// <summary>
    /// T3C: one heir's intestate share. Shares are fractions (numerator/
    /// denominator), never invented percentages. Only REAL kin from the
    /// HF-3 KinshipRegistry appear — no conjured heirs, ever.
    /// </summary>
    [Serializable]
    public sealed class HeirShare
    {
        public int PersonId = -1;
        public string Relation = string.Empty; // "spouse" or "child"
        public int ShareNumerator = 1;
        public int ShareDenominator = 1;
        public bool IsLifeInterest; // dower: life use of real property, not ownership

        public HeirShare() { }
    }

    /// <summary>T3C: one estate.</summary>
    [Serializable]
    public sealed class Estate
    {
        public string EstateId = string.Empty;
        public int DecedentPersonId = -1;
        public string DecedentName = string.Empty;
        public int OpenedDayIndex;
        public EstateStatus Status = EstateStatus.Unspecified;
        public List<HeirShare> Heirs = new List<HeirShare>();
        public List<string> ParcelIds = new List<string>();
        public List<string> BusinessInstanceIds = new List<string>();
        public List<string> EstateDebtIds = new List<string>(); // creditor claims against the estate
        public int ExecutorPersonId = -1;
        public List<string> SettledDebtIds = new List<string>();

        public Estate() { }
    }

    /// <summary>
    /// T3C: estates &amp; succession. Canon §8.3: "Death, incapacity, retirement,
    /// relocation or partner exit redistributes ownership and authority; it does
    /// not automatically delete a business." When a consequential Person dies,
    /// this service opens a continuity problem: surviving heirs (real kin only),
    /// inventoried assets, creditor claims paid FIRST, then title transfers
    /// through the T2F chain with TitleBasis.Inheritance.
    ///
    /// Intestate rule (historical research, 1870s western territories — the
    /// 1887 federal territorial act pattern + common-law dower; marked
    /// calibration, not canon doctrine):
    /// - Debts are settled before any heir takes.
    /// - Surviving spouse: 1/3 of personal property outright + LIFE USE
    ///   (dower) of 1/3 of real property — a life interest, not ownership.
    /// - Children split the remainder equally (2/3 of everything).
    /// - No spouse: children split all equally. No children: spouse takes all
    ///   personal + life use of 1/2 real. Neither: held and flagged (escheat
    ///   is a legal proceeding, never automatic confiscation).
    /// </summary>
    public sealed class EstateService
    {
        private readonly Dictionary<string, Estate> estates =
            new Dictionary<string, Estate>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Opens an estate for a decedent. Heirs come ONLY from the kinship
        /// registry (spouse + children). Parcels come from the title authority
        /// (current holder == decedent). Nothing is invented.
        /// </summary>
        public Estate OpenEstate(
            EntityIdRegistry ids, int decedentPersonId, string decedentName,
            KinshipRegistry kinship, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (decendentInvalid(decedentPersonId, diag)) return null;

            var estate = new Estate
            {
                EstateId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                DecedentPersonId = decedentPersonId,
                DecedentName = decedentName ?? string.Empty,
                OpenedDayIndex = dayIndex,
                Status = EstateStatus.Open,
            };

            // Heirs: real kin only.
            List<int> spouses = kinship != null
                ? kinship.GetRelated(decedentPersonId, KinshipRelation.Spouse) : new List<int>();
            List<int> children = kinship != null
                ? kinship.GetRelated(decedentPersonId, KinshipRelation.Child) : new List<int>();
            ComputeIntestateShares(estate, spouses, children, diag);

            // Assets: parcels are inventoried via RegisterDecedentParcel (the
            // T2F title authority stays the source of truth for who holds what).
            // Business interests transfer through the ownership/governance
            // records by the caller — BusinessInstanceState keys OwnerKind, not
            // person, so person-level business matching is a documented seam.

            estates[estate.EstateId] = estate;
            diag.Add($"EstateService: estate {estate.EstateId} opened for {estate.DecedentName} — " +
                $"{estate.Heirs.Count} heir entries.");
            return estate;
        }

        private bool decendentInvalid(int decedentPersonId, List<string> diag)
        {
            if (decedentPersonId < 0)
            {
                diag.Add("EstateService.OpenEstate: the decedent must be a real person id.");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Registers a parcel the decedent held (from the caller's parcel
        /// enumeration — the title authority remains the source of truth for
        /// who holds what; this only scopes the estate's inventory).
        /// </summary>
        public string RegisterDecedentParcel(Estate estate, string parcelId, TitleAuthority titles, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateService.RegisterDecedentParcel: estate required.";
            if (titles == null || string.IsNullOrWhiteSpace(parcelId)) return "EstateService.RegisterDecedentParcel: parcel required.";
            string holder = titles.CurrentHolder(parcelId);
            if (!string.Equals(holder, estate.DecedentName, StringComparison.Ordinal))
                return $"EstateService.RegisterDecedentParcel: '{parcelId}' is held by '{holder}', not the decedent — not inventoried.";
            if (!estate.ParcelIds.Contains(parcelId)) estate.ParcelIds.Add(parcelId);
            diag.Add($"EstateService: parcel '{parcelId}' inventoried in estate {estate.EstateId}.");
            return null;
        }

        /// <summary>
        /// The 1870s intestate rule (historical research — calibration).
        /// Spouse: 1/3 personal outright + life interest in 1/3 of realty.
        /// Children: equal shares of the remainder.
        /// </summary>
        public void ComputeIntestateShares(Estate estate, List<int> spouseIds, List<int> childIds, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return;
            spouseIds = spouseIds ?? new List<int>();
            childIds = childIds ?? new List<int>();

            bool hasSpouse = spouseIds.Count > 0;
            bool hasChildren = childIds.Count > 0;

            if (!hasSpouse && !hasChildren)
            {
                diag.Add($"EstateService: {estate.EstateId} — no spouse or children found in kinship; " +
                    "estate HELD and flagged. Escheat is a legal proceeding, never automatic.");
                return;
            }

            if (hasSpouse && hasChildren)
            {
                // Spouse: 1/3 outright (personal) + life interest in 1/3 of realty.
                estate.Heirs.Add(new HeirShare
                {
                    PersonId = spouseIds[0], Relation = "spouse",
                    ShareNumerator = 1, ShareDenominator = 3, IsLifeInterest = false,
                });
                estate.Heirs.Add(new HeirShare
                {
                    PersonId = spouseIds[0], Relation = "spouse (dower life interest)",
                    ShareNumerator = 1, ShareDenominator = 3, IsLifeInterest = true,
                });
                // Children split the remaining 2/3 equally.
                foreach (int child in childIds)
                {
                    estate.Heirs.Add(new HeirShare
                    {
                        PersonId = child, Relation = "child",
                        ShareNumerator = 2, ShareDenominator = 3 * Math.Max(1, childIds.Count),
                    });
                }
            }
            else if (hasSpouse)
            {
                estate.Heirs.Add(new HeirShare
                {
                    PersonId = spouseIds[0], Relation = "spouse",
                    ShareNumerator = 1, ShareDenominator = 1,
                });
                estate.Heirs.Add(new HeirShare
                {
                    PersonId = spouseIds[0], Relation = "spouse (dower life interest)",
                    ShareNumerator = 1, ShareDenominator = 2, IsLifeInterest = true,
                });
            }
            else
            {
                foreach (int child in childIds)
                {
                    estate.Heirs.Add(new HeirShare
                    {
                        PersonId = child, Relation = "child",
                        ShareNumerator = 1, ShareDenominator = Math.Max(1, childIds.Count),
                    });
                }
            }
            diag.Add($"EstateService: {estate.EstateId} — intestate shares computed for {estate.Heirs.Count} heir entries " +
                "(1870s territorial pattern: spouse 1/3 + dower life interest, children split remainder).");
        }

        /// <summary>
        /// Appoints an executor — a real person. Executor authority flows
        /// through the T2D DelegatedAuthority pattern (the caller grants it
        /// against the estate's businesses); this records the appointment.
        /// </summary>
        public string AppointExecutor(Estate estate, int executorPersonId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateService.AppointExecutor: estate required.";
            if (executorPersonId < 0)
                return "EstateService.AppointExecutor: the executor must be a real person.";
            if (executorPersonId == estate.DecedentPersonId)
                return "EstateService.AppointExecutor: the decedent cannot execute their own estate.";
            estate.ExecutorPersonId = executorPersonId;
            diag.Add($"EstateService: P{executorPersonId} appointed executor of estate {estate.EstateId}.");
            return null;
        }

        /// <summary>
        /// Records a creditor claim against the estate. Debts settle BEFORE
        /// heirs take — the instrument id must name a real T2A/SWN-3 debt.
        /// </summary>
        public string RecordEstateDebt(Estate estate, string debtInstrumentId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateService.RecordEstateDebt: estate required.";
            if (string.IsNullOrWhiteSpace(debtInstrumentId))
                return "EstateService.RecordEstateDebt: the debt instrument must be named.";
            if (!estate.EstateDebtIds.Contains(debtInstrumentId))
                estate.EstateDebtIds.Add(debtInstrumentId);
            estate.Status = EstateStatus.DebtsSettling;
            diag.Add($"EstateService: debt '{debtInstrumentId}' recorded against estate {estate.EstateId} — heirs wait for creditors.");
            return null;
        }

        public string MarkDebtSettled(Estate estate, string debtInstrumentId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateService.MarkDebtSettled: estate required.";
            if (!estate.EstateDebtIds.Contains(debtInstrumentId))
                return $"EstateService.MarkDebtSettled: '{debtInstrumentId}' is not a recorded estate debt.";
            estate.EstateDebtIds.Remove(debtInstrumentId);
            estate.SettledDebtIds.Add(debtInstrumentId);
            diag.Add($"EstateService: debt '{debtInstrumentId}' settled from estate {estate.EstateId}.");
            return null;
        }

        /// <summary>
        /// Distributes one parcel to an heir through the T2F title chain with
        /// TitleBasis.Inheritance. Refuses while estate debts remain — creditors
        /// first, always.
        /// </summary>
        public string DistributeParcel(
            Estate estate, string parcelId, int heirPersonId, string heirName,
            EntityIdRegistry ids, TitleAuthority titles, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateService.DistributeParcel: estate required.";
            if (estate.EstateDebtIds.Count > 0)
                return $"EstateService.DistributeParcel: {estate.EstateDebtIds.Count} estate debts unsettled — creditors before heirs.";
            if (!estate.ParcelIds.Contains(parcelId))
                return $"EstateService.DistributeParcel: '{parcelId}' is not in estate {estate.EstateId}.";
            bool isHeir = false;
            bool lifeInterest = false;
            foreach (HeirShare heir in estate.Heirs)
            {
                if (heir.PersonId == heirPersonId) { isHeir = true; lifeInterest = lifeInterest || heir.IsLifeInterest; }
            }
            if (!isHeir)
                return $"EstateService.DistributeParcel: P{heirPersonId} is not an heir of estate {estate.EstateId} — no conjured heirs.";

            string problem = titles.TransferTitle(ids, parcelId, heirName, TitleBasis.Inheritance,
                estate.EstateId, dayIndex,
                lifeInterest ? "intestate distribution — dower life interest" : "intestate distribution",
                diag);
            if (problem != null) return problem;
            estate.ParcelIds.Remove(parcelId);
            estate.Status = EstateStatus.Distributing;
            diag.Add($"EstateService: parcel '{parcelId}' → {heirName} by inheritance" +
                (lifeInterest ? " (life interest)." : "."));
            return null;
        }

        /// <summary>
        /// Closes the estate: all debts settled, all parcels distributed.
        /// The decedent is archived via the T2H PersonArchive by the caller —
        /// this only verifies readiness and marks closure.
        /// </summary>
        public string CloseEstate(Estate estate, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (estate == null) return "EstateService.CloseEstate: estate required.";
            if (estate.EstateDebtIds.Count > 0)
                return $"EstateService.CloseEstate: {estate.EstateDebtIds.Count} debts still unsettled.";
            if (estate.ParcelIds.Count > 0)
                return $"EstateService.CloseEstate: {estate.ParcelIds.Count} parcels undistributed.";
            estate.Status = EstateStatus.Closed;
            diag.Add($"EstateService: estate {estate.EstateId} closed — archive the decedent via PersonArchive.");
            return null;
        }

        public Estate Get(string estateId)
        {
            return estates.TryGetValue(estateId, out Estate estate) ? estate : null;
        }
    }
}
