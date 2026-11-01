using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III foundation: subdivision / consolidation contracts.
    /// Canon: XXXI §6.3 (parcel severance); Rev XXIII §1.1.
    ///
    /// Rules the implementation must honor:
    /// - A split is not a sale: splitting creates no money.
    /// - Original title history stays traceable (ParentParcelIds lineage; retired, never deleted).
    /// - No invented proportional division of mortgages/encumbrances: unresolved
    ///   apportionment is represented as pending treatment or blocks finalization.
    /// - No split through an existing building footprint without a supported arrangement.
    /// - Subdivision must preserve legitimate access or surface an access problem;
    ///   never assume an easement magically exists.
    /// - Atomicity: a failed subdivision leaves the original property state intact.
    /// </summary>

    [Serializable]
    public class SubdivisionRequest
    {
        public string ParcelId;
        /// <summary>Two or more resulting footprints, each a list of grid-cell ranges.</summary>
        public List<List<GridRect>> ResultingFootprints = new List<List<GridRect>>();
        public string RequestedByHolder;
        public int Day;
    }

    [Serializable]
    public class SubdivisionValidation
    {
        public bool IsValid;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
    }

    [Serializable]
    public class SubdivisionResult
    {
        public bool Success;
        public List<string> NewParcelIds = new List<string>();
        public string RetiredParcelId;
        public string Message;
    }

    public interface IParcelSubdivisionService
    {
        SubdivisionValidation Validate(SubdivisionRequest request);
        SubdivisionResult Execute(SubdivisionRequest request);
    }
}
