// Series 2 Pass 9 (player binding + save adoption): the PRODUCTION
// IBusinessCashPort over a BusinessRuntimeState.
//
// This is the port Pass 7 and Pass 8 were waiting for. The only
// in-tree port over BusinessRuntimeState before this pass lived in
// the orchestrator path (SliceWorkAndPay.BusinessRuntimeCashPort,
// Orchestration/Player), which a Domains-layer driver must not
// reference — that would invert layering (production drivers
// depending on orchestrators). This class places the same pattern
// in a production/driver path (Domains/Economy/Core, the folder
// that owns BusinessRuntimeState), verbatim where it fits, so
// drivers, adapters, and carrier settlement can take a
// caller-supplied port that Codex wires to the real runtime.
//
// Cash rule, stated plainly: business cash moves ONLY through
// this port (or another IBusinessCashPort Codex wires to the same
// runtime). Household cash is NEVER booked here — HouseholdLedger
// records household cash only, and a business-to-business payment
// booked into a household ledger would invent a household party
// to a business payment (the Pass 7 correction, applied in both
// directions here).
//
// Infallibility proof, for the Pass 7 adapter cash gap: Credit
// returns a diagnostic ONLY when the runtime is null or the
// amount is negative. BusinessRuntimeState.AddCashCents clamps
// its argument to >= 0 and adds it, with no failure path, so for
// a non-null runtime and a non-negative revenue, Credit ALWAYS
// returns null. The Pass 7 adapters therefore post revenue AFTER
// stock moves without a real atomicity hole when wired to this
// port: their WARNING branch is defensive dead code for this
// port, not a live path. It is deliberately NOT deleted — a
// different port (a test fake, a future capped port) can still
// refuse, and the warning is the honest record if it ever does.
// No adapter file is edited in this pass; the proof is in the
// Pass 9 Roslyn tests (production port over a real
// BusinessRuntimeState: credit, debit, insufficient-funds, and
// negative-amount cases).
//
// TryDebit is the single-writer spend: BusinessRuntimeState.
// TrySpendCents refuses when cash is short, moving nothing.
using System;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Production IBusinessCashPort over a BusinessRuntimeState.
    /// Codex wires one per business (store, bakery, fuel dealer,
    /// carrier) from the business's real runtime; drivers and
    /// adapters take it as a caller-supplied port and never
    /// construct business cash themselves.
    /// </summary>
    public sealed class Series2BusinessRuntimeCashPort : IBusinessIdentityCashPort
    {
        private readonly BusinessRuntimeState runtime;

        public Series2BusinessRuntimeCashPort(BusinessRuntimeState runtime)
        {
            this.runtime = runtime;
        }

        /// <summary>Identity comes from the wrapped runtime itself; callers cannot relabel another Business's cash.</summary>
        public string BusinessId => runtime != null ? runtime.BusinessId : string.Empty;
        public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;

        public string TryDebit(int amountCents, string reason)
        {
            if (runtime == null)
            {
                return "business runtime cash port: no runtime";
            }

            if (amountCents < 0)
            {
                return "business runtime cash port: negative debit";
            }

            if (!runtime.TrySpendCents(amountCents))
            {
                return $"business runtime cash port: insufficient funds ({runtime.CurrentCashCents}c for {amountCents}c)";
            }

            return null;
        }

        public string Credit(int amountCents, string reason)
        {
            if (runtime == null)
            {
                return "business runtime cash port: no runtime";
            }

            if (amountCents < 0)
            {
                return "business runtime cash port: negative credit";
            }

            // AddCashCents has no failure path for a non-negative
            // amount (it clamps and adds), so this Credit cannot
            // fail for a non-negative revenue on a non-null runtime.
            // See the header proof and the Pass 9 tests.
            runtime.AddCashCents(amountCents);
            return null;
        }
    }
}
