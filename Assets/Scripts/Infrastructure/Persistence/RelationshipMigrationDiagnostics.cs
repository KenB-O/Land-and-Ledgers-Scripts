using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;

namespace LandLedgers.Persistence
{
    /// <summary>
    /// Which legacy relationship family a diagnostic belongs to.
    /// </summary>
    public enum RelationshipMigrationCategory
    {
        Household = 0,
        Employment = 1,
    }

    /// <summary>
    /// Deterministic outcome of one legacy relationship claim under the 3A-D27/D28
    /// decision tables. Priority order (D28 §19.3): real reciprocal evidence >
    /// business payroll authority for compensation > single one-sided association >
    /// plausible inference (which alone never creates a relationship).
    /// </summary>
    public enum MigrationDiagnosticOutcome
    {
        /// <summary>Mutual agreement between the two legacy copies. Highest confidence.</summary>
        Corroborated = 0,

        /// <summary>One valid uncontested claim. Usable, with a diagnostic recorded.</summary>
        OneSidedClaim = 1,

        /// <summary>
        /// Direct conflict resolved by a deterministic precedence rule (never arbitrary choice).
        /// </summary>
        ConflictResolved = 2,

        /// <summary>
        /// Contradiction quarantined: no active relationship is created, all candidate claims are
        /// preserved with provenance. Never silently merged (PL-23; 3A-D27/D28).
        /// </summary>
        Quarantined = 3,

        /// <summary>Valid zero-state or preserved history; no relationship action needed.</summary>
        Informational = 4,
    }

    /// <summary>
    /// One deterministic decision about a single legacy relationship claim.
    /// </summary>
    [Serializable]
    public sealed class RelationshipMigrationDiagnostic
    {
        public RelationshipMigrationCategory Category;
        public MigrationDiagnosticOutcome Outcome;
        public int PersonId;
        public string CounterpartyId = string.Empty;
        public string LegacyCondition = string.Empty;
        public string Decision = string.Empty;
        public string Details = string.Empty;
        public string RuleReference = string.Empty;
    }

    /// <summary>
    /// A contradictory legacy claim set preserved with provenance. The quarantine retains every
    /// candidate claim so a later repair pass (or Kennedy) can resolve it explicitly - the
    /// auditor never guesses (migration rule: quarantine with diagnostics, never silently merge).
    /// </summary>
    [Serializable]
    public sealed class MigrationQuarantineRecord
    {
        public string QuarantineId = string.Empty;
        public RelationshipMigrationCategory Category;
        public int PersonId;
        public List<string> ConflictingClaims = new List<string>();
        public string RuleReference = string.Empty;
        public string Reason = string.Empty;
    }

    /// <summary>
    /// Minimal legacy person evidence for the audit. Built from PersonState by
    /// <see cref="RelationshipMigrationAuditAdapter"/>, or constructed directly in tests.
    /// </summary>
    [Serializable]
    public struct LegacyPersonClaim
    {
        public int PersonId;
        public string DisplayName;
        public int HouseholdId; // Person.householdId; < 0 = no pointer
        public int PersonWageCents; // household-side wage mirror (Person.wage), not authority
        public int WorkplaceBuildingId; // Person.workplaceBuildingId; < 0 = none
    }

    /// <summary>Minimal legacy household evidence: HouseholdState.id + memberIds.</summary>
    [Serializable]
    public struct LegacyHouseholdClaim
    {
        public int HouseholdId;
        public List<int> MemberIds;
    }

    /// <summary>
    /// Minimal legacy employment evidence: one WorkerSlot assignment. BusinessId is the
    /// business instance id; BusinessBuildingId is the building the person-side workplace
    /// pointer is matched against (current code links Person.workplaceBuildingId to
    /// BusinessInstanceState.AssignedBuildingId).
    /// </summary>
    [Serializable]
    public struct LegacySlotClaim
    {
        public string BusinessId;
        public int BusinessBuildingId;
        public string SlotId;
        public string AssignedWorkerId;
        public string AssignedWorkerDisplayName;
        public int WeeklyWageCents;
        public bool PaidActive;
        public bool SuspendedForMissedPayroll;
        public bool RequiredForOpening;
    }

    /// <summary>
    /// Result of a full relationship-migration audit. Deterministic: the same legacy state
    /// always produces the same report (diagnostics and quarantines are sorted before return).
    /// </summary>
    [Serializable]
    public sealed class RelationshipMigrationAuditReport
    {
        public List<RelationshipMigrationDiagnostic> Diagnostics = new List<RelationshipMigrationDiagnostic>();
        public List<MigrationQuarantineRecord> Quarantines = new List<MigrationQuarantineRecord>();

        public bool HasQuarantines => Quarantines != null && Quarantines.Count > 0;

        public int CountByOutcome(MigrationDiagnosticOutcome outcome)
        {
            int count = 0;
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Outcome == outcome)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Deterministic human-readable summary. Identical input always yields identical output.
        /// </summary>
        public string BuildSummary()
        {
            var lines = new List<string>(Diagnostics.Count + Quarantines.Count + 2);
            lines.Add($"Relationship migration audit: {Diagnostics.Count} diagnostics, {Quarantines.Count} quarantines.");
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                RelationshipMigrationDiagnostic d = Diagnostics[i];
                lines.Add($"[{d.Category}] P{d.PersonId} -> {d.CounterpartyId}: {d.Outcome} - {d.Decision} ({d.RuleReference})");
            }

            for (int i = 0; i < Quarantines.Count; i++)
            {
                MigrationQuarantineRecord q = Quarantines[i];
                lines.Add($"[QUARANTINE {q.QuarantineId}] [{q.Category}] P{q.PersonId}: {q.Reason} ({q.RuleReference})");
            }

            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// PKG-3 (PL-23): deterministic legacy relationship-migration diagnostics with quarantine.
    /// Implements the 3A-D27 (household) and 3A-D28 (employment) decision tables as a
    /// diagnostics-only audit: it reports what migration WOULD do and quarantines
    /// contradictions, but changes no state. Contradictory v5 household/employment copies are
    /// quarantined with diagnostics, never silently merged.
    /// </summary>
    public static class RelationshipMigrationAuditor
    {
        private const string HouseholdRule = "3A-D27";
        private const string EmploymentRule = "3A-D28";

        #region Household audit (3A-D27)

        /// <summary>
        /// Audits legacy household copies (Person.householdId vs Household.memberIds) per 3A-D27.
        /// General precedence: mutual agreement (strongest) > one valid uncontested claim (usable
        /// with diagnostic) > direct conflict (controlled precedence or quarantine) > missing
        /// endpoint (never fabricate endpoint).
        /// </summary>
        public static RelationshipMigrationAuditReport AuditHouseholds(
            IReadOnlyList<LegacyPersonClaim> persons,
            IReadOnlyList<LegacyHouseholdClaim> households)
        {
            var report = new RelationshipMigrationAuditReport();
            if (persons == null || households == null)
            {
                return report;
            }

            var householdIds = new HashSet<int>();
            for (int i = 0; i < households.Count; i++)
            {
                householdIds.Add(households[i].HouseholdId);
            }

            var personsSorted = new List<LegacyPersonClaim>(persons);
            personsSorted.Sort((a, b) => a.PersonId.CompareTo(b.PersonId));

            int quarantineIndex = 0;
            for (int i = 0; i < personsSorted.Count; i++)
            {
                LegacyPersonClaim person = personsSorted[i];
                bool hasPointer = person.HouseholdId >= 0;
                var containingLists = new List<int>();
                for (int h = 0; h < households.Count; h++)
                {
                    List<int> members = households[h].MemberIds;
                    if (members != null && members.Contains(person.PersonId))
                    {
                        containingLists.Add(households[h].HouseholdId);
                    }
                }

                containingLists.Sort();

                if (!hasPointer && containingLists.Count == 0)
                {
                    // Valid zero-membership Person; preserve.
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.Informational, person.PersonId, "-",
                        "Person has no household pointer and appears in no household list.",
                        "Valid zero-membership person; preserved.",
                        "No membership created.", HouseholdRule);
                    continue;
                }

                if (hasPointer && !householdIds.Contains(person.HouseholdId))
                {
                    // Person points to missing Household: no active membership, preserve diagnostic.
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.Informational, person.PersonId, $"H{person.HouseholdId}",
                        "Person.householdId points to a household id with no Household record.",
                        "No active membership created; legacy missing-reference diagnostic preserved.",
                        "Missing endpoint is never fabricated.", HouseholdRule);
                    continue;
                }

                if (hasPointer && containingLists.Count == 1 && containingLists[0] == person.HouseholdId)
                {
                    // Mutual agreement: strongest.
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.Corroborated, person.PersonId, $"H{person.HouseholdId}",
                        "Person.householdId = H and H.memberIds contains Person (mutual agreement).",
                        "Create one Active HouseholdMembership. Highest confidence.",
                        "Mutual corroboration.", HouseholdRule);
                    continue;
                }

                if (!hasPointer && containingLists.Count == 1)
                {
                    // Exactly one valid household contains Person.
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.OneSidedClaim, person.PersonId, $"H{containingLists[0]}",
                        "Person has no household pointer; exactly one valid household contains Person.",
                        "Create membership to that household. Record legacy one-sided reconciliation diagnostic.",
                        "One valid uncontested claim.", HouseholdRule);
                    continue;
                }

                if (hasPointer && !containingLists.Contains(person.HouseholdId) && containingLists.Count == 0)
                {
                    // Person points to valid H; H omits Person; no other H claims Person.
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.OneSidedClaim, person.PersonId, $"H{person.HouseholdId}",
                        "Person points to valid H; H omits Person; no other household claims Person.",
                        "Create membership to H. Record legacy one-sided reconciliation diagnostic.",
                        "One valid uncontested claim.", HouseholdRule);
                    continue;
                }

                if (hasPointer && containingLists.Contains(person.HouseholdId))
                {
                    // Mutual H1 corroboration wins; other list entries are stale duplicates.
                    var stale = new List<int>(containingLists);
                    stale.Remove(person.HouseholdId);
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.ConflictResolved, person.PersonId, $"H{person.HouseholdId}",
                        $"Person points to H{person.HouseholdId}; H{person.HouseholdId} and {JoinIds(stale)} both list Person.",
                        $"Mutual H{person.HouseholdId} corroboration wins; {JoinIds(stale)} entries treated as stale duplicates.",
                        "Person pointer is the deterministic migration precedence rule; stale duplicates diagnosed, not merged.",
                        HouseholdRule);
                    continue;
                }

                if (hasPointer && !containingLists.Contains(person.HouseholdId) && containingLists.Count >= 1)
                {
                    // Person points to H1 while H2 (or several) contains Person: pointer wins narrowly.
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.ConflictResolved, person.PersonId, $"H{person.HouseholdId}",
                        $"Person points to H{person.HouseholdId} while {JoinIds(containingLists)} lists Person.",
                        $"Create Active membership to H{person.HouseholdId}; remove {JoinIds(containingLists)} from the derived active index.",
                        "Controlled precedence: the singular Person pointer wins the direct-conflict case; conflict diagnostic preserved.",
                        HouseholdRule);
                    continue;
                }

                if (!hasPointer && containingLists.Count > 1)
                {
                    // No pointer, multiple lists: do NOT arbitrarily choose - quarantine.
                    var quarantine = new MigrationQuarantineRecord
                    {
                        QuarantineId = $"Q-HH-{person.PersonId}-{quarantineIndex++}",
                        Category = RelationshipMigrationCategory.Household,
                        PersonId = person.PersonId,
                        RuleReference = HouseholdRule,
                        Reason = "Person pointer absent and Person appears in multiple household lists: " +
                                 "no Active membership created until migration repair; all candidate claims retained.",
                    };
                    for (int c = 0; c < containingLists.Count; c++)
                    {
                        quarantine.ConflictingClaims.Add($"H{containingLists[c]}.memberIds contains P{person.PersonId}");
                    }

                    report.Quarantines.Add(quarantine);
                    AddDiagnostic(report, RelationshipMigrationCategory.Household,
                        MigrationDiagnosticOutcome.Quarantined, person.PersonId, JoinIds(containingLists),
                        "Person pointer absent and Person appears in multiple household lists.",
                        $"Quarantined as {quarantine.QuarantineId}; no Active membership created.",
                        "Never silently merge contradictory copies.", HouseholdRule);
                }
            }

            // Household references missing Person: do not manufacture Person.
            for (int h = 0; h < households.Count; h++)
            {
                LegacyHouseholdClaim household = households[h];
                if (household.MemberIds == null)
                {
                    continue;
                }

                var personIds = new HashSet<int>();
                for (int i = 0; i < personsSorted.Count; i++)
                {
                    personIds.Add(personsSorted[i].PersonId);
                }

                for (int m = 0; m < household.MemberIds.Count; m++)
                {
                    int memberId = household.MemberIds[m];
                    if (!personIds.Contains(memberId))
                    {
                        AddDiagnostic(report, RelationshipMigrationCategory.Household,
                            MigrationDiagnosticOutcome.Informational, memberId, $"H{household.HouseholdId}",
                            $"Household H{household.HouseholdId}.memberIds references missing Person P{memberId}.",
                            "Drop from the derived active index; retain diagnostic. Person is not manufactured.",
                            "Missing endpoint is never fabricated.", HouseholdRule);
                    }
                }
            }

            SortReport(report);
            return report;
        }

        #endregion

        #region Employment audit (3A-D28)

        /// <summary>
        /// Audits legacy employment copies (WorkerSlot assignments vs Person workplace/wage)
        /// per 3A-D28. Priority: real reciprocal evidence > business payroll authority for
        /// compensation (slot wage wins wage conflicts, because current payroll uses it) >
        /// single one-sided association > plausible inference (never creates employment).
        /// </summary>
        public static RelationshipMigrationAuditReport AuditEmployment(
            IReadOnlyList<LegacyPersonClaim> persons,
            IReadOnlyList<LegacySlotClaim> slots)
        {
            var report = new RelationshipMigrationAuditReport();
            if (persons == null || slots == null)
            {
                return report;
            }

            var personById = new Dictionary<int, LegacyPersonClaim>();
            for (int i = 0; i < persons.Count; i++)
            {
                personById[persons[i].PersonId] = persons[i];
            }

            var slotsSorted = new List<LegacySlotClaim>(slots);
            slotsSorted.Sort((a, b) =>
            {
                int c = string.Compare(a.BusinessId, b.BusinessId, StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.SlotId, b.SlotId, StringComparison.Ordinal);
            });

            // Group slots by (business, parsed person id) to detect duplicate assignments.
            var slotsByBusinessPerson = new Dictionary<string, Dictionary<int, List<LegacySlotClaim>>>();
            var assignedSlots = new List<LegacySlotClaim>();
            for (int i = 0; i < slotsSorted.Count; i++)
            {
                LegacySlotClaim slot = slotsSorted[i];
                if (string.IsNullOrWhiteSpace(slot.AssignedWorkerId))
                {
                    continue;
                }

                assignedSlots.Add(slot);

                if (IsSyntheticOwnerWorker(slot.AssignedWorkerId))
                {
                    // 3A-D28: do not manufacture Person; compatibility OwnerOperatorCoverage path.
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.Informational, -1, slot.BusinessId,
                        $"Slot {slot.SlotId} assigns synthetic worker '{slot.AssignedWorkerId}'.",
                        "Convert to compatibility OwnerOperatorCoverage representation; position can remain covered through owner operation.",
                        "Synthetic owner workers do not become fake Persons (3A-D29).", EmploymentRule);
                    continue;
                }

                if (!TryParsePersonId(slot.AssignedWorkerId, out int workerPersonId))
                {
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.Informational, -1, slot.BusinessId,
                        $"Slot {slot.SlotId} assigns unparseable worker id '{slot.AssignedWorkerId}'.",
                        "Position becomes vacant; preserve missing-person diagnostic.",
                        "Never fabricate the worker endpoint.", EmploymentRule);
                    continue;
                }

                if (!slotsByBusinessPerson.TryGetValue(slot.BusinessId, out Dictionary<int, List<LegacySlotClaim>> byPerson))
                {
                    byPerson = new Dictionary<int, List<LegacySlotClaim>>();
                    slotsByBusinessPerson[slot.BusinessId] = byPerson;
                }

                if (!byPerson.TryGetValue(workerPersonId, out List<LegacySlotClaim> group))
                {
                    group = new List<LegacySlotClaim>();
                    byPerson[workerPersonId] = group;
                }

                group.Add(slot);
            }

            int quarantineIndex = 0;

            // Cross-business conflicts: one quarantine per person (not per slot), listing every
            // conflicting attachment. If the person's workplace mutually corroborates one of the
            // businesses, that business wins and the other slots resolve as vacant instead.
            var businessesByPerson = new Dictionary<int, HashSet<string>>();
            var slotsByPerson = new Dictionary<int, List<LegacySlotClaim>>();
            for (int i = 0; i < assignedSlots.Count; i++)
            {
                LegacySlotClaim crossSlot = assignedSlots[i];
                if (IsSyntheticOwnerWorker(crossSlot.AssignedWorkerId)
                    || !TryParsePersonId(crossSlot.AssignedWorkerId, out int crossPid))
                {
                    continue;
                }

                if (!businessesByPerson.TryGetValue(crossPid, out HashSet<string> bizSet))
                {
                    bizSet = new HashSet<string>(StringComparer.Ordinal);
                    businessesByPerson[crossPid] = bizSet;
                }

                bizSet.Add(crossSlot.BusinessId);

                if (!slotsByPerson.TryGetValue(crossPid, out List<LegacySlotClaim> slotList))
                {
                    slotList = new List<LegacySlotClaim>();
                    slotsByPerson[crossPid] = slotList;
                }

                slotList.Add(crossSlot);
            }

            var crossBusinessQuarantined = new HashSet<int>();
            var mutualWinnerBusinessByPerson = new Dictionary<int, string>( );
            foreach (KeyValuePair<int, HashSet<string>> crossEntry in businessesByPerson)
            {
                int crossPid = crossEntry.Key;
                if (crossEntry.Value.Count < 2)
                {
                    continue;
                }

                string mutualWinner = null;
                if (personById.TryGetValue(crossPid, out LegacyPersonClaim crossPerson)
                    && slotsByPerson.TryGetValue(crossPid, out List<LegacySlotClaim> personSlots))
                {
                    for (int s = 0; s < personSlots.Count; s++)
                    {
                        if (personSlots[s].BusinessBuildingId >= 0
                            && personSlots[s].BusinessBuildingId == crossPerson.WorkplaceBuildingId)
                        {
                            mutualWinner = personSlots[s].BusinessId;
                            break;
                        }
                    }
                }

                if (mutualWinner != null)
                {
                    mutualWinnerBusinessByPerson[crossPid] = mutualWinner;
                    continue;
                }

                var crossQuarantine = new MigrationQuarantineRecord
                {
                    QuarantineId = $"Q-EMP-XBIZ-{crossPid}-{quarantineIndex++}",
                    Category = RelationshipMigrationCategory.Employment,
                    PersonId = crossPid,
                    RuleReference = EmploymentRule,
                    Reason = $"Person P{crossPid} has conflicting employment attachments across {crossEntry.Value.Count} businesses " +
                             "with no mutual corroboration: quarantined rather than inventing multiple legacy employment.",
                };
                List<LegacySlotClaim> conflicts = slotsByPerson[crossPid];
                for (int s = 0; s < conflicts.Count; s++)
                {
                    crossQuarantine.ConflictingClaims.Add(
                        $"{conflicts[s].BusinessId}.{conflicts[s].SlotId} assigns P{crossPid} at {conflicts[s].WeeklyWageCents}c/wk");
                }

                report.Quarantines.Add(crossQuarantine);
                crossBusinessQuarantined.Add(crossPid);
                AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                    MigrationDiagnosticOutcome.Quarantined, crossPid, string.Join(",", crossEntry.Value),
                    $"P{crossPid} attached to multiple businesses; Person workplace does not corroborate any.",
                    $"Quarantined as {crossQuarantine.QuarantineId}; no multiple legacy employment invented.",
                    "Legacy architecture assumed one workplace; treat as conflict, not new multi-employment intent.",
                    EmploymentRule);
            }

            // Duplicate assignments of the same person within one business.
            foreach (KeyValuePair<string, Dictionary<int, List<LegacySlotClaim>>> businessEntry in slotsByBusinessPerson)
            {
                string businessId = businessEntry.Key;
                foreach (KeyValuePair<int, List<LegacySlotClaim>> personEntry in businessEntry.Value)
                {
                    int workerPersonId = personEntry.Key;
                    List<LegacySlotClaim> group = personEntry.Value;
                    if (group.Count < 2)
                    {
                        continue;
                    }

                    bool hasPerson = personById.TryGetValue(workerPersonId, out LegacyPersonClaim person);
                    var corroborated = new List<LegacySlotClaim>();
                for (int i = 0; i < group.Count; i++)
                {
                    if (hasPerson && person.WorkplaceBuildingId == group[i].BusinessBuildingId && group[i].BusinessBuildingId >= 0)
                    {
                        corroborated.Add(group[i]);
                    }
                }

                if (corroborated.Count == 1)
                {
                    // Prefer the one mutually corroborated slot; others are duplicates (no double-pay).
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (group[i].SlotId == corroborated[0].SlotId)
                        {
                            continue;
                        }

                        AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                            MigrationDiagnosticOutcome.ConflictResolved, workerPersonId, businessId,
                            $"Person P{workerPersonId} assigned to multiple slots in {businessId}; " +
                            $"slot {corroborated[0].SlotId} is mutually corroborated.",
                            $"Duplicate slot {group[i].SlotId} does not create a second employment; no double-pay.",
                            "Legacy state did not establish multi-responsibility meaning; prefer the corroborated slot.",
                            EmploymentRule);
                    }
                }
                else
                {
                    var quarantine = new MigrationQuarantineRecord
                    {
                        QuarantineId = $"Q-EMP-{businessId}-{workerPersonId}-{quarantineIndex++}",
                        Category = RelationshipMigrationCategory.Employment,
                        PersonId = workerPersonId,
                        RuleReference = EmploymentRule,
                        Reason = $"Person P{workerPersonId} assigned to {group.Count} slots in {businessId} " +
                                 "with no single mutually corroborated slot: duplicates quarantined, no double-pay.",
                    };
                    for (int i = 0; i < group.Count; i++)
                    {
                        quarantine.ConflictingClaims.Add(
                            $"{businessId}.{group[i].SlotId} assigns P{workerPersonId} at {group[i].WeeklyWageCents}c/wk");
                    }

                    report.Quarantines.Add(quarantine);
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.Quarantined, workerPersonId, businessId,
                        $"Person P{workerPersonId} assigned to multiple slots in {businessId}; no single corroborated slot.",
                        $"Quarantined as {quarantine.QuarantineId}; duplicates quarantined, do not double-pay.",
                        "Never silently merge contradictory copies.", EmploymentRule);
                }
            }
        }

        // Per-slot employment decisions (skip slots already handled as duplicates).
            var handledDuplicateSlotKeys = new HashSet<string>();
            foreach (KeyValuePair<string, Dictionary<int, List<LegacySlotClaim>>> businessEntry in slotsByBusinessPerson)
            {
                foreach (KeyValuePair<int, List<LegacySlotClaim>> personEntry in businessEntry.Value)
                {
                    if (personEntry.Value.Count > 1)
                    {
                        for (int i = 0; i < personEntry.Value.Count; i++)
                        {
                            handledDuplicateSlotKeys.Add($"{personEntry.Value[i].BusinessId}|{personEntry.Value[i].SlotId}");
                        }
                    }
                }
            }

            for (int i = 0; i < assignedSlots.Count; i++)
            {
                LegacySlotClaim slot = assignedSlots[i];
                if (IsSyntheticOwnerWorker(slot.AssignedWorkerId)
                    || !TryParsePersonId(slot.AssignedWorkerId, out int workerPersonId))
                {
                    continue;
                }

                if (handledDuplicateSlotKeys.Contains($"{slot.BusinessId}|{slot.SlotId}"))
                {
                    continue;
                }

                if (crossBusinessQuarantined.Contains(workerPersonId))
                {
                    // Covered by the per-person cross-business quarantine above.
                    continue;
                }

                if (!personById.TryGetValue(workerPersonId, out LegacyPersonClaim person))
                {
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.Informational, workerPersonId, slot.BusinessId,
                        $"Slot {slot.SlotId} assigns missing Person P{workerPersonId}.",
                        "Position becomes vacant. Preserve missing-person diagnostic.",
                        "Do not manufacture Person.", EmploymentRule);
                    continue;
                }

                if (mutualWinnerBusinessByPerson.TryGetValue(workerPersonId, out string winnerBusiness)
                    && !string.Equals(winnerBusiness, slot.BusinessId, StringComparison.Ordinal))
                {
                    // Mutual corroboration wins elsewhere; this slot becomes vacant.
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.ConflictResolved, workerPersonId, slot.BusinessId,
                        $"Slot {slot.SlotId} assigns P{workerPersonId} but Person workplace mutually matches {winnerBusiness}.",
                        $"Mutual relationship with {winnerBusiness} wins; this slot becomes vacant.",
                        "Real reciprocal evidence outranks one-sided slot assignment.", EmploymentRule);
                    continue;
                }

                bool workplaceMatches = person.WorkplaceBuildingId >= 0
                    && slot.BusinessBuildingId >= 0
                    && person.WorkplaceBuildingId == slot.BusinessBuildingId;

                if (workplaceMatches)
                {
                    AuditReciprocalEmployment(report, person, slot);
                    continue;
                }

                // Workplace missing or points elsewhere. Cross-business conflicts were already
                // resolved or quarantined above, so this is a single-business attachment.
                if (person.WorkplaceBuildingId < 0)
                {
                    // Slot assigns valid P; Person workplace missing; P not assigned elsewhere.
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.OneSidedClaim, workerPersonId, slot.BusinessId,
                        $"Slot {slot.SlotId} assigns P{workerPersonId}; Person workplace missing; P not assigned elsewhere.",
                        "Create Employment from slot. Record one-sided employer diagnostic.",
                        "Single one-sided association.", EmploymentRule);
                    AuditSuspensionState(report, person, slot);
                    continue;
                }

                // Person workplace points to a business with no slot assigning them.
                AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                    MigrationDiagnosticOutcome.Informational, workerPersonId, slot.BusinessId,
                    $"Slot {slot.SlotId} assigns P{workerPersonId}; Person workplace points elsewhere with no corroborating slot.",
                    "Preserve LegacyEmploymentClaim diagnostic/history. Person becomes available unless another authoritative relation is found.",
                    "Plausible inference alone does not create employment.", EmploymentRule);
            }

            // Persons with a workplace pointer but no slot assigning them anywhere.
            var assignedPersonIds = new HashSet<int>();
            for (int i = 0; i < assignedSlots.Count; i++)
            {
                if (TryParsePersonId(assignedSlots[i].AssignedWorkerId, out int pid))
                {
                    assignedPersonIds.Add(pid);
                }
            }

            var personsSorted = new List<LegacyPersonClaim>(persons);
            personsSorted.Sort((a, b) => a.PersonId.CompareTo(b.PersonId));
            for (int i = 0; i < personsSorted.Count; i++)
            {
                LegacyPersonClaim person = personsSorted[i];
                if (person.WorkplaceBuildingId < 0 || assignedPersonIds.Contains(person.PersonId))
                {
                    continue;
                }

                int vacantCompatibleSlots = 0;
                for (int s = 0; s < slotsSorted.Count; s++)
                {
                    LegacySlotClaim slot = slotsSorted[s];
                    if (string.IsNullOrWhiteSpace(slot.AssignedWorkerId)
                        && slot.BusinessBuildingId >= 0
                        && slot.BusinessBuildingId == person.WorkplaceBuildingId)
                    {
                        vacantCompatibleSlots++;
                    }
                }

                if (vacantCompatibleSlots == 1)
                {
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.Informational, person.PersonId, "-",
                        $"Person P{person.PersonId} workplace points to a business with exactly one compatible vacant slot.",
                        "Do not auto-fill solely because the fit looks plausible. Keep diagnostic; avoid fabrication.",
                        "Plausible inference alone does not create employment.", EmploymentRule);
                }
                else
                {
                    AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                        MigrationDiagnosticOutcome.Informational, person.PersonId, "-",
                        $"Person P{person.PersonId} workplace points to a business, but no slot assigns Person.",
                        "Do not create payroll-active Employment solely from Person snapshot. Preserve LegacyEmploymentClaim diagnostic/history.",
                        "Plausible inference alone does not create employment.", EmploymentRule);
                }
            }

            SortReport(report);
            return report;
        }

        private static void AuditReciprocalEmployment(
            RelationshipMigrationAuditReport report,
            LegacyPersonClaim person,
            LegacySlotClaim slot)
        {
            if (person.PersonWageCents == slot.WeeklyWageCents)
            {
                AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                    MigrationDiagnosticOutcome.Corroborated, person.PersonId, slot.BusinessId,
                    $"Slot {slot.SlotId} assigns P{person.PersonId}; Person workplace matches; slot wage = Person wage ({slot.WeeklyWageCents}c).",
                    "Create Active EmploymentRelationship. Compensation from slot wage; Person wage corroborates.",
                    "Real reciprocal evidence.", EmploymentRule);
            }
            else
            {
                AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                    MigrationDiagnosticOutcome.ConflictResolved, person.PersonId, slot.BusinessId,
                    $"Slot {slot.SlotId} assigns P{person.PersonId}; Person workplace matches; wages differ " +
                    $"(slot {slot.WeeklyWageCents}c vs Person mirror {person.PersonWageCents}c).",
                    $"Create Employment. Slot wage wins ({slot.WeeklyWageCents}c) because current payroll uses it. Record wage-conflict diagnostic.",
                    "Business payroll authority outranks the household-side mirror for compensation.", EmploymentRule);
            }

            AuditSuspensionState(report, person, slot);

            if (!string.IsNullOrWhiteSpace(slot.AssignedWorkerDisplayName)
                && !string.IsNullOrWhiteSpace(person.DisplayName)
                && !string.Equals(slot.AssignedWorkerDisplayName.Trim(), person.DisplayName.Trim(), StringComparison.Ordinal))
            {
                AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                    MigrationDiagnosticOutcome.Informational, person.PersonId, slot.BusinessId,
                    $"Slot display name '{slot.AssignedWorkerDisplayName}' disagrees with Person name '{person.DisplayName}'.",
                    "Person identity wins. Display name is derived. Diagnostic optional.",
                    "Identity outranks display text.", EmploymentRule);
            }
        }

        private static void AuditSuspensionState(
            RelationshipMigrationAuditReport report,
            LegacyPersonClaim person,
            LegacySlotClaim slot)
        {
            if (slot.SuspendedForMissedPayroll)
            {
                AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                    MigrationDiagnosticOutcome.Informational, person.PersonId, slot.BusinessId,
                    $"Slot {slot.SlotId}: suspendedForMissedPayroll with worker still reciprocally attached.",
                    "Create Suspended Employment with reason EmployerPaymentDefault. Existing business operating liability remains separate.",
                    "Missed-payroll suspension is a relationship state, not a deletion.", EmploymentRule);
            }
            else if (!slot.PaidActive)
            {
                AddDiagnostic(report, RelationshipMigrationCategory.Employment,
                    MigrationDiagnosticOutcome.Informational, person.PersonId, slot.BusinessId,
                    $"Slot {slot.SlotId}: paidActive = false with worker still assigned and Person corroborating employer.",
                    "Create relationship as Suspended legacy state unless evidence shows the relationship already ended. Do not count as active labor.",
                    "Unpaid attachment is not active labor.", EmploymentRule);
            }
        }

        #endregion

        #region Helpers

        private static bool IsSyntheticOwnerWorker(string assignedWorkerId)
        {
            return !string.IsNullOrWhiteSpace(assignedWorkerId)
                && assignedWorkerId.StartsWith("owner:", StringComparison.Ordinal);
        }

        private static bool TryParsePersonId(string assignedWorkerId, out int personId)
        {
            personId = -1;
            return !string.IsNullOrWhiteSpace(assignedWorkerId)
                && int.TryParse(assignedWorkerId.Trim(), out personId);
        }

        private static string JoinIds(List<int> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return "-";
            }

            var parts = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                parts[i] = $"H{ids[i]}";
            }

            return string.Join(",", parts);
        }

        private static void AddDiagnostic(
            RelationshipMigrationAuditReport report,
            RelationshipMigrationCategory category,
            MigrationDiagnosticOutcome outcome,
            int personId,
            string counterpartyId,
            string legacyCondition,
            string decision,
            string details,
            string ruleReference)
        {
            report.Diagnostics.Add(new RelationshipMigrationDiagnostic
            {
                Category = category,
                Outcome = outcome,
                PersonId = personId,
                CounterpartyId = counterpartyId ?? string.Empty,
                LegacyCondition = legacyCondition ?? string.Empty,
                Decision = decision ?? string.Empty,
                Details = details ?? string.Empty,
                RuleReference = ruleReference ?? string.Empty,
            });
        }

        private static void SortReport(RelationshipMigrationAuditReport report)
        {
            report.Diagnostics.Sort((a, b) =>
            {
                int c = a.Category.CompareTo(b.Category);
                if (c != 0)
                {
                    return c;
                }

                c = a.PersonId.CompareTo(b.PersonId);
                if (c != 0)
                {
                    return c;
                }

                c = string.Compare(a.CounterpartyId, b.CounterpartyId, StringComparison.Ordinal);
                if (c != 0)
                {
                    return c;
                }

                return string.Compare(a.LegacyCondition, b.LegacyCondition, StringComparison.Ordinal);
            });

            report.Quarantines.Sort((a, b) => string.Compare(a.QuarantineId, b.QuarantineId, StringComparison.Ordinal));
        }

        #endregion
    }

    /// <summary>
    /// Builds legacy claim sets from live runtime state so the auditor can run against a real
    /// save (v5 or v6). Thin adapter only - all decision logic lives in
    /// <see cref="RelationshipMigrationAuditor"/>.
    /// </summary>
    public static class RelationshipMigrationAuditAdapter
    {
        public static void FromRuntimeState(
            PopulationState populationState,
            IReadOnlyList<BusinessInstanceState> businesses,
            out List<LegacyPersonClaim> personClaims,
            out List<LegacyHouseholdClaim> householdClaims,
            out List<LegacySlotClaim> slotClaims)
        {
            personClaims = new List<LegacyPersonClaim>();
            householdClaims = new List<LegacyHouseholdClaim>();
            slotClaims = new List<LegacySlotClaim>();

            if (populationState != null)
            {
                if (populationState.people != null)
                {
                    for (int i = 0; i < populationState.people.Count; i++)
                    {
                        PersonState person = populationState.people[i];
                        if (person == null)
                        {
                            continue;
                        }

                        personClaims.Add(new LegacyPersonClaim
                        {
                            PersonId = person.id,
                            DisplayName = person.DisplayName,
                            HouseholdId = person.householdId,
                            PersonWageCents = person.wage.weeklyWage,
                            WorkplaceBuildingId = person.workplaceBuildingId,
                        });
                    }
                }

                if (populationState.households != null)
                {
                    for (int i = 0; i < populationState.households.Count; i++)
                    {
                        HouseholdState household = populationState.households[i];
                        if (household == null)
                        {
                            continue;
                        }

                        householdClaims.Add(new LegacyHouseholdClaim
                        {
                            HouseholdId = household.id,
                            MemberIds = household.memberIds != null
                                ? new List<int>(household.memberIds)
                                : new List<int>(),
                        });
                    }
                }
            }

            if (businesses != null)
            {
                for (int b = 0; b < businesses.Count; b++)
                {
                    BusinessInstanceState business = businesses[b];
                    if (business == null || business.RuntimeState == null)
                    {
                        continue;
                    }

                    IReadOnlyList<WorkerSlotState> slots = business.RuntimeState.WorkerSlots;
                    if (slots == null)
                    {
                        continue;
                    }

                    for (int s = 0; s < slots.Count; s++)
                    {
                        WorkerSlotState slot = slots[s];
                        if (slot == null)
                        {
                            continue;
                        }

                        slotClaims.Add(new LegacySlotClaim
                        {
                            BusinessId = business.InstanceId,
                            BusinessBuildingId = business.AssignedBuildingId,
                            SlotId = slot.SlotId,
                            AssignedWorkerId = slot.AssignedWorkerId,
                            AssignedWorkerDisplayName = slot.AssignedWorkerDisplayName,
                            WeeklyWageCents = slot.WeeklyWageCents,
                            PaidActive = slot.IsPaidActive,
                            SuspendedForMissedPayroll = slot.SuspendedForMissedPayroll,
                            RequiredForOpening = slot.RequiredForOpening,
                        });
                    }
                }
            }
        }
    }
}
