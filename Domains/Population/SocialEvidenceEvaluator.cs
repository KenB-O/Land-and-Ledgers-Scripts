// Pass VIII worker 8: honest social evidence for decision authorities.
//
// Decision authorities must never act on facts a person never learned, and
// must never invent confirming sources. This file is the single shared
// assessment: given the packets ONE person actually received about ONE
// subject, it reports what the evidence honestly supports.
//
// Corroboration policy (shared, documented once):
//   - a firsthand telling (SourceKind "direct-observation", the speaker
//     claims they directly observed the subject) counts as corroborated;
//   - otherwise two or more INDEPENDENT sources (distinct transmission
//     chain roots) count as corroborated;
//   - a single relayed rumor with one chain root is uncorroborated and
//     must not move a decision on its own. The authority may then seek
//     corroboration through its own legitimate records, never by inventing
//     another source.
//
// Confidence only decays along a chain (InformationPacketFactory.Relay
// takes the minimum); this evaluator never upgrades it.
using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// What one person's received packets honestly say about one subject.
    /// Built only from packets that person actually received: no omniscience.
    /// </summary>
    public sealed class SocialEvidenceAssessment
    {
        public int PersonId = -1;
        public string Subject = string.Empty;
        public int PacketCount;
        public int IndependentSources;
        public bool HasFirsthandTelling;
        public float MaxConfidence01;
        public List<string> ChainRoots = new List<string>();

        /// <summary>
        /// True when the evidence meets the shared corroboration policy: a
        /// firsthand telling, or two or more independent sources.
        /// </summary>
        public bool Corroborated => HasFirsthandTelling || IndependentSources >= 2;
    }

    /// <summary>
    /// Assesses the social evidence one person holds about one subject, for
    /// consumption by ordinary decision authorities (employment, procurement,
    /// credit, and others). Stable subject strings keep producers and
    /// consumers of the same evidence in agreement.
    /// </summary>
    public static class SocialEvidenceEvaluator
    {
        public const string DirectObservationSourceKind = "direct-observation";

        /// <summary>Subject for layoff risk at an employer business.</summary>
        public static string LayoffRiskSubject(string employerBusinessId)
        {
            return "layoff-risk:" + (employerBusinessId ?? string.Empty);
        }

        /// <summary>Subject for a supplier business being in trouble.</summary>
        public static string SupplierTroubleSubject(string supplierBusinessId)
        {
            return "supplier-trouble:" + (supplierBusinessId ?? string.Empty);
        }

        /// <summary>Subject for a borrowing business being in financial trouble.</summary>
        public static string BorrowerTroubleSubject(string businessId)
        {
            return "borrower-trouble:" + (businessId ?? string.Empty);
        }

        /// <summary>
        /// Reads only the packets the named person actually received about
        /// the subject. A null store or an unknown person yields an empty
        /// assessment, never an error that could be mistaken for evidence.
        /// </summary>
        public static SocialEvidenceAssessment Assess(
            IKnowledgeTransmissionStore knowledge, int personId, string subject)
        {
            var assessment = new SocialEvidenceAssessment
            {
                PersonId = personId,
                Subject = subject ?? string.Empty,
            };
            if (knowledge == null || personId < 0 || string.IsNullOrWhiteSpace(subject))
                return assessment;

            List<InformationPacket> packets = knowledge.PacketsFor(personId, subject);
            var roots = new HashSet<string>(StringComparer.Ordinal);
            if (packets != null)
            {
                foreach (InformationPacket packet in packets)
                {
                    if (packet == null) continue;
                    assessment.PacketCount++;
                    string root = KnowledgeTransmissionStore.ChainRoot(packet);
                    if (!string.IsNullOrWhiteSpace(root) && roots.Add(root))
                        assessment.ChainRoots.Add(root);
                    if (string.Equals(packet.SourceKind, DirectObservationSourceKind, StringComparison.Ordinal))
                        assessment.HasFirsthandTelling = true;
                    if (packet.Confidence01 > assessment.MaxConfidence01)
                        assessment.MaxConfidence01 = packet.Confidence01;
                }
            }
            assessment.IndependentSources = roots.Count;
            assessment.ChainRoots.Sort(StringComparer.Ordinal);
            return assessment;
        }

        /// <summary>
        /// An honest citation for diagnostics and decision records: what was
        /// heard, from how many independent sources, with what provenance.
        /// Never invents certainty the packets do not carry.
        /// </summary>
        public static string CiteEvidence(SocialEvidenceAssessment assessment)
        {
            if (assessment == null)
                return "no social evidence assessed";
            if (assessment.PacketCount == 0)
                return $"no packets about '{assessment.Subject}' received by P{assessment.PersonId}";
            string roots = assessment.ChainRoots.Count > 0
                ? string.Join(",", assessment.ChainRoots.ToArray())
                : "none";
            return $"{assessment.PacketCount} packet(s) about '{assessment.Subject}' held by P{assessment.PersonId}: " +
                   $"{assessment.IndependentSources} independent source(s) (chain roots {roots}), " +
                   $"firsthand telling={(assessment.HasFirsthandTelling ? "yes" : "no")}, " +
                   $"max confidence {assessment.MaxConfidence01:0.00}, " +
                   $"corroborated={(assessment.Corroborated ? "yes" : "no")}";
        }
    }
}
