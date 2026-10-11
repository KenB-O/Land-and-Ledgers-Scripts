using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Skills
{
    /// <summary>
    /// Pass III Worker D: the four capability authorities as stores.
    ///
    /// Canon: XXXI Part I §§1.2-1.4 (descriptive occupation, worker agency),
    /// Part III §§3.1-3.5 (no hard gates; competence changes execution),
    /// Part IV §§4.4-4.6 (evidence vs truth); Tech XIV Part II §§2.1-2.5;
    /// preserved Rev VII Part I §§1.1-1.5 (continuous capability, formation
    /// inputs, no calendar-time gains, diminishing returns, anti-inflation
    /// lock); preserved Rev XVII integration §§3-4 (four authorities, three
    /// catalogue layers), §12 (learning rules); Rev XVI §2.2.
    ///
    /// The four authorities (Rev XVII §3) — NEVER collapsed into one rating:
    ///   AptitudeStore            = learning-rate tendency per family (set at
    ///                              person creation; accelerates development,
    ///                              never creates fictional prior training).
    ///   ExperienceLedger         = what the person actually did (task history
    ///                              with difficulty, novelty, supervision,
    ///                              outcome). Idempotent: entries carry EntryId;
    ///                              save/load restore can NEVER double-credit.
    ///   DomainFamiliarityStore   = contextual knowledge (trade/industry/place).
    ///   PersonCapabilityStore    = present continuous capability 0..1.
    ///
    /// Person ids are the foundation-contract string form (EntityId.ToString()
    /// at the SkillService boundary) so these stores stay dependency-light.
    /// </summary>

    /// <summary>
    /// Learning-rate tendency per capability family. Set at person creation.
    /// Aptitude accelerates development; it NEVER creates fictional prior
    /// training (Rev XVII §3: "Aptitude is not skill").
    /// Canon: XXXI Part I §§1.2-1.4; Rev XVII integration §3; Rev XVI §2.2.
    /// </summary>
    [Serializable]
    public sealed class AptitudeStore
    {
        [SerializeField]
        private List<AptitudeRecord> records = new List<AptitudeRecord>();

        private readonly Dictionary<string, AptitudeRecord> lookup =
            new Dictionary<string, AptitudeRecord>(StringComparer.Ordinal);

        private static string Key(string personId, CapabilityFamily family)
        {
            return personId + "|" + ((int)family).ToString();
        }

        /// <summary>
        /// Sets the learning-rate tendency for a person/family. First write wins
        /// per person/family — aptitude is set at creation, not retrained later.
        /// Multiplier roughly 0.5 (slow) to 2.0 (quick); clamped to [0.25, 3.0].
        /// </summary>
        public void SetAptitude(string personId, CapabilityFamily family, double learningRateMultiplier, int day)
        {
            if (string.IsNullOrEmpty(personId) || family == CapabilityFamily.Unknown)
            {
                return;
            }

            string key = Key(personId, family);
            if (lookup.ContainsKey(key))
            {
                return; // first write wins: aptitude is a creation-time trait
            }

            var record = new AptitudeRecord
            {
                PersonId = personId,
                Family = family,
                LearningRateMultiplier = Math.Max(0.25, Math.Min(3.0, learningRateMultiplier)),
                SetDay = day,
            };
            records.Add(record);
            lookup[key] = record;
        }

        /// <summary>Learning-rate multiplier; 1.0 when no aptitude is recorded.</summary>
        public double GetLearningRate(string personId, CapabilityFamily family)
        {
            if (string.IsNullOrEmpty(personId))
            {
                return 1.0;
            }

            AptitudeRecord record;
            return lookup.TryGetValue(Key(personId, family), out record)
                ? record.LearningRateMultiplier
                : 1.0;
        }

        public int Count => records.Count;

        public List<AptitudeRecord> CaptureRecords()
        {
            return new List<AptitudeRecord>(records);
        }

        /// <summary>Clear-then-load: restore replaces state, never merges.</summary>
        public void LoadRecords(List<AptitudeRecord> loaded)
        {
            records.Clear();
            lookup.Clear();
            if (loaded == null)
            {
                return;
            }

            foreach (AptitudeRecord record in loaded)
            {
                if (record == null || string.IsNullOrEmpty(record.PersonId))
                {
                    continue;
                }

                string key = Key(record.PersonId, record.Family);
                if (lookup.ContainsKey(key))
                {
                    continue; // first wins, even across a restore
                }

                records.Add(record);
                lookup[key] = record;
            }
        }
    }

    /// <summary>
    /// The experience ledger: what the person has actually done and encountered.
    /// Each entry records difficulty, novelty, supervision, outcome and time spent
    /// (Rev VII §1.2). Entries carry deterministic EntryIds; recording the same
    /// entry twice is a no-op, so save/load restore can NEVER double-credit
    /// (canon §2.6: no experience from save restores or re-polled events).
    /// Canon: Rev VII §1.2; Rev XVII §12; XXXI Part III §3.3.
    /// </summary>
    [Serializable]
    public sealed class ExperienceLedger
    {
        [SerializeField]
        private List<ExperienceEntry> entries = new List<ExperienceEntry>();

        [SerializeField]
        private int sequence;

        private readonly HashSet<string> entryIds =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Records one task experience. Assigns a deterministic EntryId when the
        /// entry lacks one. Returns false when this exact entry was already
        /// recorded (idempotent — never double-credits).
        /// </summary>
        public bool TryRecord(ExperienceEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.PersonId) ||
                string.IsNullOrEmpty(entry.TaskCapabilityId))
            {
                return false;
            }

            if (string.IsNullOrEmpty(entry.EntryId))
            {
                entry.EntryId = NextEntryId(entry);
            }

            if (!entryIds.Add(entry.EntryId))
            {
                return false; // already recorded: no double credit
            }

            entries.Add(entry);
            return true;
        }

        private string NextEntryId(ExperienceEntry entry)
        {
            sequence++;
            return "exp-" + entry.PersonId + "-" + entry.TaskCapabilityId +
                   "-d" + entry.Day + "-" + sequence;
        }

        public IReadOnlyList<ExperienceEntry> GetEntries(string personId, string taskCapabilityId)
        {
            var result = new List<ExperienceEntry>();
            foreach (ExperienceEntry entry in entries)
            {
                if (string.Equals(entry.PersonId, personId, StringComparison.Ordinal) &&
                    string.Equals(entry.TaskCapabilityId, taskCapabilityId, StringComparison.Ordinal))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Counts entries for the same capability within the trailing window —
        /// the routine-work signal for diminishing returns (Rev XVII §12:
        /// "routine mastery = small refinement").
        /// </summary>
        public int CountRecent(string personId, string taskCapabilityId, int currentDay, int windowDays)
        {
            int count = 0;
            foreach (ExperienceEntry entry in entries)
            {
                if (string.Equals(entry.PersonId, personId, StringComparison.Ordinal) &&
                    string.Equals(entry.TaskCapabilityId, taskCapabilityId, StringComparison.Ordinal) &&
                    entry.Day <= currentDay && entry.Day > currentDay - windowDays)
                {
                    count++;
                }
            }

            return count;
        }

        public int Count => entries.Count;

        public List<ExperienceEntry> CaptureEntries()
        {
            return new List<ExperienceEntry>(entries);
        }

        public int CaptureSequence()
        {
            return sequence;
        }

        /// <summary>
        /// Clear-then-load. Rebuilds the idempotency set from persisted EntryIds;
        /// legacy entries without ids receive deterministic ones on load.
        /// </summary>
        public void LoadEntries(List<ExperienceEntry> loaded, int loadedSequence)
        {
            entries.Clear();
            entryIds.Clear();
            sequence = Math.Max(0, loadedSequence);
            if (loaded == null)
            {
                return;
            }

            foreach (ExperienceEntry entry in loaded)
            {
                if (entry == null || string.IsNullOrEmpty(entry.PersonId) ||
                    string.IsNullOrEmpty(entry.TaskCapabilityId))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.EntryId))
                {
                    entry.EntryId = NextEntryId(entry);
                }

                if (!entryIds.Add(entry.EntryId))
                {
                    continue; // duplicate in the save itself: keep first only
                }

                entries.Add(entry);
            }
        }
    }

    /// <summary>
    /// Contextual knowledge: trade, industry, market, place, organization,
    /// procedure, or customer base (Rev XVII §4, layer 3). A skilled negotiator
    /// does not automatically understand livestock or lumber markets.
    /// Canon: Rev XVII integration §4; Rev XVI §2.2.
    /// </summary>
    [Serializable]
    public sealed class DomainFamiliarityStore
    {
        [SerializeField]
        private List<DomainFamiliarity> records = new List<DomainFamiliarity>();

        private readonly Dictionary<string, DomainFamiliarity> lookup =
            new Dictionary<string, DomainFamiliarity>(StringComparer.Ordinal);

        private static string Key(string personId, string domain)
        {
            return personId + "|" + (domain ?? string.Empty);
        }

        public void SetFamiliarity(string personId, string domain, double familiarity01)
        {
            if (string.IsNullOrEmpty(personId) || string.IsNullOrEmpty(domain))
            {
                return;
            }

            string key = Key(personId, domain);
            DomainFamiliarity record;
            if (!lookup.TryGetValue(key, out record))
            {
                record = new DomainFamiliarity { PersonId = personId, Domain = domain };
                records.Add(record);
                lookup[key] = record;
            }

            record.Familiarity01 = Math.Max(0.0, Math.Min(1.0, familiarity01));
        }

        /// <summary>Familiarity 0..1; 0 when nothing is recorded.</summary>
        public double GetFamiliarity(string personId, string domain)
        {
            DomainFamiliarity record;
            return lookup.TryGetValue(Key(personId, domain), out record)
                ? record.Familiarity01
                : 0.0;
        }

        public List<DomainFamiliarity> CaptureRecords()
        {
            return new List<DomainFamiliarity>(records);
        }

        /// <summary>Clear-then-load.</summary>
        public void LoadRecords(List<DomainFamiliarity> loaded)
        {
            records.Clear();
            lookup.Clear();
            if (loaded == null)
            {
                return;
            }

            foreach (DomainFamiliarity record in loaded)
            {
                if (record == null || string.IsNullOrEmpty(record.PersonId) ||
                    string.IsNullOrEmpty(record.Domain))
                {
                    continue;
                }

                string key = Key(record.PersonId, record.Domain);
                if (lookup.ContainsKey(key))
                {
                    continue;
                }

                records.Add(record);
                lookup[key] = record;
            }
        }
    }

    /// <summary>
    /// Present continuous capability at a task-capability class, 0..1
    /// (Rev VII §1.1: continuous capability, no level-up progression).
    /// Ordinary work lives in the low-to-middle range; Mastered is exceptional
    /// and rare (Rev VII §1.3 anti-inflation lock).
    /// </summary>
    [Serializable]
    public sealed class PersonCapabilityStore
    {
        /// <summary>Default for a person with no recorded capability: a true novice.</summary>
        public const double NoviceCapability = 0.10;

        [SerializeField]
        private List<PersonCapabilityState> states = new List<PersonCapabilityState>();

        private readonly Dictionary<string, PersonCapabilityState> lookup =
            new Dictionary<string, PersonCapabilityState>(StringComparer.Ordinal);

        private static string Key(string personId, string taskCapabilityId)
        {
            return personId + "|" + (taskCapabilityId ?? string.Empty);
        }

        public double GetCapability(string personId, string taskCapabilityId)
        {
            PersonCapabilityState state;
            return lookup.TryGetValue(Key(personId, taskCapabilityId), out state)
                ? state.Capability01
                : NoviceCapability;
        }

        public void SetCapability(string personId, string taskCapabilityId, double capability01, int day)
        {
            if (string.IsNullOrEmpty(personId) || string.IsNullOrEmpty(taskCapabilityId))
            {
                return;
            }

            string key = Key(personId, taskCapabilityId);
            PersonCapabilityState state;
            if (!lookup.TryGetValue(key, out state))
            {
                state = new PersonCapabilityState
                {
                    PersonId = personId,
                    TaskCapabilityId = taskCapabilityId,
                };
                states.Add(state);
                lookup[key] = state;
            }

            state.Capability01 = Math.Max(0.0, Math.Min(1.0, capability01));
            state.LastUpdatedDay = day;
        }

        public List<PersonCapabilityState> CaptureStates()
        {
            return new List<PersonCapabilityState>(states);
        }

        /// <summary>Clear-then-load.</summary>
        public void LoadStates(List<PersonCapabilityState> loaded)
        {
            states.Clear();
            lookup.Clear();
            if (loaded == null)
            {
                return;
            }

            foreach (PersonCapabilityState state in loaded)
            {
                if (state == null || string.IsNullOrEmpty(state.PersonId) ||
                    string.IsNullOrEmpty(state.TaskCapabilityId))
                {
                    continue;
                }

                string key = Key(state.PersonId, state.TaskCapabilityId);
                if (lookup.ContainsKey(key))
                {
                    continue;
                }

                state.Capability01 = Math.Max(0.0, Math.Min(1.0, state.Capability01));
                states.Add(state);
                lookup[key] = state;
            }
        }
    }

    /// <summary>
    /// Inputs to experience formation for one real task episode.
    /// Canon: Rev VII §1.2 (prior experience, task exposure, difficulty,
    /// novelty, feedback, mentorship, aptitude, physical constraints...).
    /// </summary>
    [Serializable]
    public sealed class ExperienceFormationInputs
    {
        public string PersonId;
        public string TaskCapabilityId;
        /// <summary>0..1 task difficulty relative to the worker.</summary>
        public double Difficulty01;
        /// <summary>0..1 novelty of this task for this worker.</summary>
        public double Novelty01;
        public bool Supervised;
        /// <summary>TaskOutcome name at completion (Success, AcceptableWithVariation,
        /// RecoverableFailure, SeriousFailure).</summary>
        public string Outcome;
        /// <summary>Learning-rate multiplier from AptitudeStore (0.5 slow .. 2.0 quick).</summary>
        public double AptitudeMultiplier = 1.0;
        public double MinutesSpent;
        public int Day;
    }

    /// <summary>
    /// Experience formation: how real task episodes change capability
    /// (Rev VII §1.2; Rev XVII §12).
    ///
    /// Rules, all deterministic:
    /// - New exposure teaches strongly; routine mastery teaches little
    ///   (diminishing returns via recentSimilarCount).
    /// - Aptitude scales the gain but NEVER grants starting capability.
    /// - Success teaches most; failures still teach (less); serious failure
    ///   teaches caution (smallest gain, never negative — capability does not
    ///   un-learn from one bad day; atrophy is out of scope for this pass).
    /// - Supervised practice (mentorship) teaches faster.
    /// - Gains approach an asymptote: delta scales with (1 - capability), so
    ///   Mastered stays exceptional (Rev VII §1.3 anti-inflation).
    /// - CALENDAR TIME ALONE CONTRIBUTES NOTHING: this service only runs on
    ///   recorded task episodes. There is no daily tick, no passive accrual.
    /// </summary>
    public sealed class ExperienceFormationService
    {
        /// <summary>Base capability gain per hour of fully-novel, well-matched work.</summary>
        public const double BaseGainPerHour = 0.0003;

        /// <summary>
        /// Computes the capability delta for one task episode. Pure function of
        /// its inputs — no hidden state, no clock reads.
        /// </summary>
        public double ComputeDelta(
            double currentCapability01,
            ExperienceFormationInputs inputs,
            int recentSimilarCount)
        {
            if (inputs == null || inputs.MinutesSpent <= 0)
            {
                return 0.0;
            }

            double capability = Math.Max(0.0, Math.Min(1.0, currentCapability01));
            double hours = inputs.MinutesSpent / 60.0;
            double novelty = Math.Max(0.0, Math.Min(1.0, inputs.Novelty01));
            double difficulty = Math.Max(0.0, Math.Min(1.0, inputs.Difficulty01));
            double aptitude = Math.Max(0.25, Math.Min(3.0, inputs.AptitudeMultiplier));

            // Even fully routine work refines slightly; novelty drives learning.
            double noveltyFactor = 0.2 + 0.8 * novelty;
            // Harder tasks teach more when the worker can engage with them.
            double difficultyFactor = 0.5 + 0.5 * difficulty;
            // Mentorship accelerates (Rev XVII §12: teaching is an investment).
            double supervisionFactor = inputs.Supervised ? 1.25 : 1.0;
            double outcomeFactor = OutcomeFactor(inputs.Outcome);
            // Diminishing returns: the Nth recent repetition teaches less.
            double routineDiminishing = 1.0 / (1.0 + Math.Max(0, recentSimilarCount) * 0.15);

            double gain = BaseGainPerHour * hours * noveltyFactor * difficultyFactor *
                          supervisionFactor * outcomeFactor * aptitude * routineDiminishing;

            // Asymptotic approach: the closer to mastery, the smaller the gain.
            return gain * (1.0 - capability);
        }

        private static double OutcomeFactor(string outcome)
        {
            // Failures teach (what not to do); success teaches most.
            if (string.Equals(outcome, "Success", StringComparison.Ordinal))
            {
                return 1.0;
            }

            if (string.Equals(outcome, "AcceptableWithVariation", StringComparison.Ordinal))
            {
                return 0.8;
            }

            if (string.Equals(outcome, "RecoverableFailure", StringComparison.Ordinal))
            {
                return 0.5;
            }

            if (string.Equals(outcome, "SeriousFailure", StringComparison.Ordinal))
            {
                return 0.3;
            }

            return 0.8; // unknown outcome: treat as ordinary episode
        }
    }
}
