using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D4H: party keys for the billing cash port — stable strings naming the
    /// real cash lot a payment moves out of and into. An owner is a real
    /// person or a real business; a builder is a real Builder business.
    /// </summary>
    public static class ConstructionBillingPartyKeys
    {
        public static string PersonKey(int personId) => "person:" + personId;

        public static string BusinessKey(string businessInstanceId) =>
            "business:" + (businessInstanceId ?? string.Empty);

        public static string ForParty(ConstructionContractParty party)
        {
            if (party == null) return string.Empty;
            return party.IsPerson
                ? PersonKey(party.PersonId)
                : BusinessKey(party.BusinessInstanceId);
        }
    }

    /// <summary>
    /// D4H: the money-movement authority the progress-billing service consumes.
    /// This is the seam between the billing records (D4G/D4H) and the real
    /// cash lots. The billing service never invents, mints, or assumes money:
    /// every owner-to-builder payment goes through this port, and the port is
    /// the authority on whether the money exists.
    ///
    /// Contract for implementations:
    /// - MoveCash is ATOMIC: either the full amount moves owner-to-builder
    ///   or nothing moves and a refusal explains why. No partial moves.
    /// - MoveCash moves only REAL lots: it refuses (loudly) when the source
    ///   party's balance cannot cover the amount. The billing service treats
    ///   any refusal as fatal for that draw and records nothing as settled.
    /// - Implementations must not fabricate a balance for an unknown party;
    ///   an unknown party is a refusal, not a zero.
    /// - The Unity-side production implementation adapts the real cash model
    ///   (person cash / BusinessRuntimeState cash lots); the EditMode tests
    ///   use an in-memory port holding real discrete balances.
    /// </summary>
    public interface IConstructionBillingCashPort
    {
        /// <summary>
        /// Moves amountCents from fromPartyKey to toPartyKey as one atomic
        /// step. Returns the refusal text, or null when the move succeeded.
        /// A refusal means nothing moved — the caller must not treat the
        /// draw as settled.
        /// </summary>
        string MoveCash(string fromPartyKey, string toPartyKey, int amountCents,
            int dayIndex, string memo, List<string> diagnostics);

        /// <summary>
        /// Reads a party's current cash balance. Returns the refusal text
        /// (unknown party, unreadable lots), or null with balanceCents set.
        /// </summary>
        string TryGetCashBalance(string partyKey, out int balanceCents);
    }
}
