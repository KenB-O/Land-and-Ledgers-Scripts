using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass VII Workstream E: one unit of transmitted information with full
    /// provenance. Rumors are flagged, never silently upgraded to fact.
    /// </summary>
    [Serializable]
    public sealed class InformationPacket
    {
        public string PacketId = string.Empty;
        public string Subject = string.Empty;
        public string ContentSummary = string.Empty;
        public int SpeakerPersonId = -1;
        public int RecipientPersonId = -1;
        public int ObservedDayIndex = -1;
        public int TransmittedDayIndex = -1;
        public float Confidence01;
        public string SourceKind = string.Empty;  // "direct-observation" | "conversation" | "rumor" | "letter" | ...
        public string TransmissionChain = string.Empty;  // person ids, "->" separated
        public bool IsRumor;
    }

    /// <summary>
    /// Determines whether two real persons have a legitimate communication
    /// opportunity right now: co-location (or a real channel), overlapping
    /// availability, compatible activity, and access. Returns null when the
    /// opportunity is legitimate, or a reason when it is not.
    /// </summary>
    public interface IConversationOpportunity
    {
        string CheckOpportunity(int speakerId, int listenerId, int dayIndex, List<string> diagnostics);
    }

    /// <summary>
    /// Minimal person belief/knowledge store extension. Only transmitted
    /// packets are knowable: no omniscience. Repeated conversation must not
    /// fabricate independent confirming sources for the same packet.
    /// </summary>
    public interface IKnowledgeTransmissionStore
    {
        string RecordTransmission(InformationPacket packet, List<string> diagnostics);
        List<InformationPacket> PacketsFor(int personId, string subject);
    }
}
