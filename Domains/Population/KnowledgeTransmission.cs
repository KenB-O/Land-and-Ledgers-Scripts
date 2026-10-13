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
        private int nextPacketSequence = 1;

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

            for (int i = 0; i < packets.Count; i++)
            {
                if (packets[i] != null && string.Equals(packets[i].PacketId, packet.PacketId, StringComparison.Ordinal))
                    return $"packet {packet.PacketId} is already recorded";
            }

            // Duplicate transmissions of the same telling are refused: hearing
            // the same rumor twice from the same chain does not create a new
            // record, and never a new confirming source.
            string root = ChainRoot(packet);
            for (int i = 0; i < packets.Count; i++)
            {
                InformationPacket existing = packets[i];
                if (existing == null) continue;
                if (existing.RecipientPersonId == packet.RecipientPersonId
                    && string.Equals(existing.Subject, packet.Subject, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.ContentSummary, packet.ContentSummary, StringComparison.Ordinal)
                    && string.Equals(ChainRoot(existing), root, StringComparison.Ordinal))
                {
                    return $"P{packet.RecipientPersonId} already holds this telling of '{packet.Subject}' (chain root {root})";
                }
            }

            packets.Add(packet);
            diagnostics.Add($"KnowledgeTransmission: recorded {packet.PacketId} '{packet.Subject}' " +
                            $"P{packet.SpeakerPersonId} -> P{packet.RecipientPersonId} " +
                            $"(chain {packet.TransmissionChain}, rumor={packet.IsRumor}, confidence {packet.Confidence01:0.00}).");
            return null;
        }

        public List<InformationPacket> PacketsFor(int personId, string subject)
        {
            var result = new List<InformationPacket>();
            foreach (InformationPacket packet in packets)
            {
                if (packet == null) continue;
                if (packet.RecipientPersonId != personId) continue;
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
            foreach (InformationPacket packet in packets)
            {
                if (packet != null && packet.RecipientPersonId == personId
                    && string.Equals(packet.PacketId, packetId, StringComparison.Ordinal))
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
            nextPacketSequence = 1;
            if (dto == null) return;
            if (dto.Packets != null) packets.AddRange(dto.Packets);
            nextPacketSequence = Math.Max(1, dto.NextPacketSequence);
        }
    }
}
