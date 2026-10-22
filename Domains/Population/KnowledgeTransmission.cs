// Pass VII Workstream E: the minimal belief/knowledge extension.
//
// Only transmitted packets are knowable: no omniscience. Every packet
// carries provenance (source, speaker, recipient, observed/transmitted time,
// confidence, subject, transmission chain, rumor flag, contradictions).
// Rumors stay uncertain: the store never upgrades confidence or rumor flags.
// Repeated conversation never fabricates independent confirming sources:
// independent sources are counted by distinct transmission-chain roots, and
// duplicate transmissions are refused.
using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Builds information packets with honest provenance.
    /// </summary>
    public static class InformationPacketFactory
    {
        /// <summary>
        /// A firsthand observation: the speaker directly observed the subject.
        /// </summary>
        public static InformationPacket Observe(
            string subject,
            string contentSummary,
            int observerPersonId,
            int observedDayIndex,
            float confidence01,
            string packetId = "")
        {
            return new InformationPacket
            {
                PacketId = packetId ?? string.Empty,
                Subject = subject ?? string.Empty,
                ContentSummary = contentSummary ?? string.Empty,
                SpeakerPersonId = observerPersonId,
                ObservedDayIndex = observedDayIndex,
                Confidence01 = Clamp01(confidence01),
                SourceKind = "direct-observation",
                TransmissionChain = observerPersonId.ToString(),
                IsRumor = false,
            };
        }

        /// <summary>
        /// Relays a packet to a new recipient. The chain extends; the relay
        /// is secondhand (a rumor unless the relayer verified it); confidence
        /// degrades with each retelling and never improves by relaying.
        /// </summary>
        public static InformationPacket Relay(
            InformationPacket original,
            int newSpeakerPersonId,
            int newRecipientPersonId,
            int transmittedDayIndex,
            float confidence01,
            bool verified = false,
            string packetId = "")
        {
            if (original == null) return null;
            string chain = string.IsNullOrWhiteSpace(original.TransmissionChain)
                ? original.SpeakerPersonId.ToString()
                : original.TransmissionChain;
            if (!chain.EndsWith("->" + newSpeakerPersonId, StringComparison.Ordinal)
                && !string.Equals(chain, newSpeakerPersonId.ToString(), StringComparison.Ordinal))
            {
                chain = chain + "->" + newSpeakerPersonId;
            }

            return new InformationPacket
            {
                PacketId = packetId ?? string.Empty,
                Subject = original.Subject,
                ContentSummary = original.ContentSummary,
                SpeakerPersonId = newSpeakerPersonId,
                RecipientPersonId = newRecipientPersonId,
                ObservedDayIndex = original.ObservedDayIndex,
                TransmittedDayIndex = transmittedDayIndex,
                // A relay never knows better than its source.
                Confidence01 = Math.Min(Clamp01(confidence01), original.Confidence01),
                SourceKind = "conversation",
                TransmissionChain = chain,
                IsRumor = !verified,
            };
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    [Serializable]
    public sealed class KnowledgeTransmissionSaveDto
    {
        public List<InformationPacket> Packets = new List<InformationPacket>();
        public int NextPacketSequence;
    }

    /// <summary>
    /// The minimal person belief/knowledge store. Records transmitted
    /// information packets with full provenance. Implements
    /// IKnowledgeTransmissionStore.
    /// </summary>
    public sealed class KnowledgeTransmissionStore : IKnowledgeTransmissionStore
    {
        private readonly List<InformationPacket> packets = new List<InformationPacket>();

        // Pass IX WS4 (A4-15b): indexes so RecordTransmission stops doing two
        // full scans per insert (O(n^2) cumulative) and PacketsFor stops
        // scanning every packet. The list above remains the save truth and
        // full provenance history.
        //  - packetsById: duplicate-packet-id refusal (ids are global).
        //  - tellings: (recipient, subject, content, chain root) already
        //    recorded — the duplicate-telling refusal. Subject compares
        //    OrdinalIgnoreCase, content and root Ordinal, exactly as the
        //    pre-index scan did.
        //  - packetsByRecipient: per-recipient packets in insertion order,
        //    so PacketsFor filters/sorts the same sequence the full scan
        //    produced (identical results, identical sort input).
        private readonly Dictionary<string, List<InformationPacket>> packetsById =
            new Dictionary<string, List<InformationPacket>>(StringComparer.Ordinal);
        private readonly HashSet<TellingKey> tellings = new HashSet<TellingKey>();
        private readonly Dictionary<int, List<InformationPacket>> packetsByRecipient =
            new Dictionary<int, List<InformationPacket>>();

        private int nextPacketSequence = 1;

        private struct TellingKey : IEquatable<TellingKey>
        {
            private readonly int recipientPersonId;
            private readonly string subject;
            private readonly string contentSummary;
            private readonly string chainRoot;

            public TellingKey(int recipientPersonId, string subject, string contentSummary, string chainRoot)
            {
                this.recipientPersonId = recipientPersonId;
                this.subject = subject ?? string.Empty;
                this.contentSummary = contentSummary ?? string.Empty;
                this.chainRoot = chainRoot ?? string.Empty;
            }

            public bool Equals(TellingKey other)
            {
                return recipientPersonId == other.recipientPersonId
                    && string.Equals(subject, other.subject, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(contentSummary, other.contentSummary, StringComparison.Ordinal)
                    && string.Equals(chainRoot, other.chainRoot, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is TellingKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = recipientPersonId;
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(subject);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(contentSummary);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(chainRoot);
                    return hash;
                }
            }
        }

        private void IndexPacket(InformationPacket packet)
        {
            if (packet == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(packet.PacketId))
            {
                List<InformationPacket> sameId;
                if (!packetsById.TryGetValue(packet.PacketId, out sameId))
                {
                    sameId = new List<InformationPacket>();
                    packetsById[packet.PacketId] = sameId;
                }

                sameId.Add(packet);
            }

            tellings.Add(new TellingKey(
                packet.RecipientPersonId, packet.Subject, packet.ContentSummary, ChainRoot(packet)));

            List<InformationPacket> bucket;
            if (!packetsByRecipient.TryGetValue(packet.RecipientPersonId, out bucket))
            {
                bucket = new List<InformationPacket>();
                packetsByRecipient[packet.RecipientPersonId] = bucket;
            }

            bucket.Add(packet);
        }

        public IReadOnlyList<InformationPacket> AllPackets => packets;
        public int PacketCount => packets.Count;

        public string RecordTransmission(InformationPacket packet, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (packet == null)
                return "no packet to record";
            if (packet.SpeakerPersonId < 0 || packet.RecipientPersonId < 0)
                return "speaker and recipient must be real persons";
            if (packet.SpeakerPersonId == packet.RecipientPersonId)
                return "a person cannot transmit information to themselves";
            if (string.IsNullOrWhiteSpace(packet.Subject))
                return "a subject is required";

            if (string.IsNullOrWhiteSpace(packet.PacketId))
                packet.PacketId = $"pkt-{nextPacketSequence++}";

            if (packetsById.ContainsKey(packet.PacketId))
                return $"packet {packet.PacketId} is already recorded";

            // Duplicate transmissions of the same telling are refused: hearing
            // the same rumor twice from the same chain does not create a new
            // record, and never a new confirming source.
            string root = ChainRoot(packet);
            if (tellings.Contains(new TellingKey(packet.RecipientPersonId, packet.Subject, packet.ContentSummary, root)))
            {
                return $"P{packet.RecipientPersonId} already holds this telling of '{packet.Subject}' (chain root {root})";
            }

            packets.Add(packet);
            IndexPacket(packet);
            diagnostics.Add($"KnowledgeTransmission: recorded {packet.PacketId} '{packet.Subject}' " +
                            $"P{packet.SpeakerPersonId} -> P{packet.RecipientPersonId} " +
                            $"(chain {packet.TransmissionChain}, rumor={packet.IsRumor}, confidence {packet.Confidence01:0.00}).");
            return null;
        }

        public List<InformationPacket> PacketsFor(int personId, string subject)
        {
            var result = new List<InformationPacket>();
            List<InformationPacket> bucket;
            if (!packetsByRecipient.TryGetValue(personId, out bucket))
            {
                return result;
            }

            foreach (InformationPacket packet in bucket)
            {
                if (packet == null) continue;
                if (!string.IsNullOrWhiteSpace(subject)
                    && !string.Equals(packet.Subject, subject, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(packet);
            }
            result.Sort((a, b) => a.TransmittedDayIndex.CompareTo(b.TransmittedDayIndex));
            return result;
        }

        /// <summary>
        /// Counts INDEPENDENT sources for a subject: distinct transmission
        /// chain roots. The same rumor retold through the same chain is one
        /// source, however many mouths carried it.
        /// </summary>
        public int IndependentSourceCount(int personId, string subject)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (InformationPacket packet in PacketsFor(personId, subject))
            {
                roots.Add(ChainRoot(packet));
            }
            return roots.Count;
        }

        public bool HasPacket(int personId, string packetId)
        {
            if (string.IsNullOrWhiteSpace(packetId)) return false;
            List<InformationPacket> sameId;
            if (!packetsById.TryGetValue(packetId, out sameId))
                return false;
            // Packet ids are globally unique in practice (duplicate ids are
            // refused at record time); the per-id list keeps this exact even
            // for a hand-built DTO carrying a repeated id.
            foreach (InformationPacket packet in sameId)
            {
                if (packet != null && packet.RecipientPersonId == personId)
                    return true;
            }
            return false;
        }

        /// <summary>The first person id in the transmission chain: the origin.</summary>
        public static string ChainRoot(InformationPacket packet)
        {
            if (packet == null) return string.Empty;
            string chain = packet.TransmissionChain;
            if (string.IsNullOrWhiteSpace(chain))
                return packet.SpeakerPersonId.ToString();
            int arrow = chain.IndexOf("->", StringComparison.Ordinal);
            return arrow < 0 ? chain.Trim() : chain.Substring(0, arrow).Trim();
        }

        public KnowledgeTransmissionSaveDto CaptureSaveDto()
        {
            return new KnowledgeTransmissionSaveDto
            {
                Packets = new List<InformationPacket>(packets),
                NextPacketSequence = nextPacketSequence,
            };
        }

        public void LoadFromSaveDto(KnowledgeTransmissionSaveDto dto)
        {
            packets.Clear();
            packetsById.Clear();
            tellings.Clear();
            packetsByRecipient.Clear();
            nextPacketSequence = 1;
            if (dto == null) return;
            if (dto.Packets != null)
            {
                packets.AddRange(dto.Packets);
                foreach (InformationPacket packet in packets)
                {
                    IndexPacket(packet);
                }
            }
            nextPacketSequence = Math.Max(1, dto.NextPacketSequence);
        }
    }
}
