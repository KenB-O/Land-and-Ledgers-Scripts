// Pass IX (A2-10): the outcome of one migration-out — who left, why, and
// which employments the departure ended (records retained as history with
// EndReason.MigratedOut; see EmploymentRelationshipRegistry
// .EndEmploymentByReason). Callers that only need the side effects may
// ignore the returned outcome.
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Population
{
    /// <summary>Result of MigrationDriver.MigrateOut for one person.</summary>
    public sealed class MigrationOutcome
    {
        public int PersonId = -1;
        public bool Distressed;
        public int DayIndex;
        /// <summary>Employments ended by the departure (empty when the person held none).</summary>
        public List<EmploymentRelationship> EndedEmployments = new List<EmploymentRelationship>();
    }
}
