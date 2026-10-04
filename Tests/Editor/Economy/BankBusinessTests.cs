using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Bank;
using LandLedgers.Economy.Financing;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// W7A: the bank as an operating business (BusinessType.Bank = 26).
    /// Canon §18.8: assets AND liabilities — the runtime wraps the T2A/NX-3B
    /// BankDepositLedger, vault cash is real specie lots with provenance, and
    /// owner capital is named-source money, never conjured. The bank creates
    /// no money: equity is cash on hand minus deposits owed.
    /// </summary>
    [TestFixture]
    public sealed class BankBusinessTests
    {
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
        }

        private static BankRuntime NewBank(string owner = "Kennedy Baldwin-ooms")
        {
            return new BankRuntime("bank-1", "First Bank of Town", owner);
        }

        private static List<SpecieLot> CapitalLots(int totalCents, string source)
        {
            return new List<SpecieLot>
            {
                new SpecieLot
                {
                    Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin",
                    AmountCents = totalCents, SourceName = source,
                },
            };
        }

        private BankRuntime OpenedBank(int capitalCents = 50000)
        {
            var bank = NewBank();
            Assert.IsNull(bank.EstablishVault("iron safe", false, true, diag));
            Assert.IsNull(bank.RecordOpeningCapital(capitalCents, "owner savings", CapitalLots(capitalCents, "owner savings"), 0, diag));
            return bank;
        }

        [Test]
        public void BankIsBusinessType26AndEnumIntact()
        {
            Assert.AreEqual(26, (int)BusinessType.Bank, "Bank is appended, never renumbered.");
            // Regression guard: a prior pass once dropped Restaurant=22. Verify 0-25 intact.
            Assert.AreEqual(0, (int)BusinessType.GeneralStore);
            Assert.AreEqual(22, (int)BusinessType.Restaurant);
            Assert.AreEqual(23, (int)BusinessType.Hotel);
            Assert.AreEqual(24, (int)BusinessType.Logging);
            Assert.AreEqual(25, (int)BusinessType.GrainElevator);
        }

        [Test]
        public void VaultRequiresRealVault()
        {
            var bank = NewBank();
            Assert.IsNotNull(bank.EstablishVault("a counter", false, false, diag),
                "A counter alone is not a bank.");
            Assert.IsFalse(bank.VaultEstablished);
            Assert.IsNull(bank.EstablishVault("vault room", true, false, diag));
            Assert.IsTrue(bank.VaultEstablished);
        }

        [Test]
        public void OpeningCapitalNeedsVaultNamedSourceAndCountedLots()
        {
            var bank = NewBank();
            Assert.IsNotNull(bank.RecordOpeningCapital(50000, "owner savings", CapitalLots(50000, "owner savings"), 0, diag),
                "Capital needs a vault to land in.");
            Assert.IsNull(bank.EstablishVault("iron safe", false, true, diag));

            Assert.IsNotNull(bank.RecordOpeningCapital(50000, "", CapitalLots(50000, "owner savings"), 0, diag),
                "Capital is never conjured — the source is named.");
            Assert.IsNotNull(bank.RecordOpeningCapital(50000, "owner savings", CapitalLots(40000, "owner savings"), 0, diag),
                "Lots must sum to the declared amount — count it, do not assert it.");
            Assert.IsNotNull(bank.RecordOpeningCapital(50000, "owner savings",
                new List<SpecieLot> { new SpecieLot { Kind = VaultSpecieKind.GoldCoin, AmountCents = 50000 } }, 0, diag),
                "Anonymous lots are refused (FVS provenance).");
        }

        [Test]
        public void OpeningCapitalIsCashWithoutLiability()
        {
            var bank = OpenedBank(50000);
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents);
            Assert.AreEqual(0, bank.Ledger.DepositsOwedCents(), "Owner capital creates no deposit liability.");
            Assert.AreEqual(50000, bank.OwnerEquityCents());
            Assert.AreEqual(50000, bank.VaultSpecieTotalCents());
            Assert.AreEqual(0, bank.CheckVaultReconciliation(diag), "Physical and accounting agree by construction.");
        }

        [Test]
        public void ValidateForOpeningGates()
        {
            var noOwner = new BankRuntime("bank-1", "First Bank of Town", "");
            Assert.IsNotNull(noOwner.ValidateForOpening(diag), "A bank needs a named owner.");

            var noVault = NewBank();
            Assert.IsNotNull(noVault.ValidateForOpening(diag), "A bank without a safe is not openable.");

            var noCapital = NewBank();
            Assert.IsNull(noCapital.EstablishVault("iron safe", false, true, diag));
            Assert.IsNotNull(noCapital.ValidateForOpening(diag), "No counted capital, no opening.");

            var opened = OpenedBank();
            Assert.IsNull(opened.ValidateForOpening(diag));
        }

        [Test]
        public void VaultDrawsOldestFirstAndNeverFakes()
        {
            var bank = OpenedBank(50000);
            Assert.IsNull(bank.ReceiveVaultLot(new SpecieLot
            {
                Kind = VaultSpecieKind.SilverCoin, KindName = "silver coin",
                AmountCents = 20000, SourceName = "depositor Samuel",
            }, 5, diag));

            List<SpecieLot> drawn = bank.DrawVaultLots(60000, diag);
            Assert.IsNotNull(drawn, "The vault holds 70000c — this must succeed.");
            int drawnTotal = 0;
            foreach (SpecieLot lot in drawn) drawnTotal += lot.AmountCents;
            Assert.AreEqual(60000, drawnTotal);
            Assert.AreEqual(10000, bank.VaultSpecieTotalCents());

            Assert.IsNull(bank.DrawVaultLots(20000, diag), "Short vault draws are refused — cash is never faked.");
            Assert.AreEqual(10000, bank.VaultSpecieTotalCents(), "Refused draws move nothing.");
        }

        [Test]
        public void ReconciliationReportsMismatchWithoutCorrecting()
        {
            var bank = OpenedBank(50000);
            // Simulate an unrecorded loss: specie leaves the vault with no
            // matching ledger entry (the pairing the operating layer owns).
            Assert.IsNotNull(bank.DrawVaultLots(10000, diag));
            int mismatch = bank.CheckVaultReconciliation(diag);
            Assert.AreEqual(-10000, mismatch, "The gap is reported, never silently corrected.");
            Assert.AreEqual(50000, bank.Ledger.CashOnHandCents, "The ledger is untouched by the count.");
        }

        [Test]
        public void PremisesRequireBankingHouseAndVault()
        {
            var req = new BankPremisesRequirements();
            Assert.IsNotNull(req.ValidatePremises("general-store", true, false, diag),
                "A bank needs a banking-house space.");
            Assert.IsNotNull(req.ValidatePremises(BankPremisesRequirements.BankingHouseSpaceKind, false, false, diag),
                "A counter alone is not a bank (Tech X §3.5).");
            Assert.IsNull(req.ValidatePremises(BankPremisesRequirements.BankingHouseSpaceKind, true, false, diag));
            Assert.IsNull(req.ValidatePremises(BankPremisesRequirements.BankingHouseSpaceKind, false, true, diag),
                "An installed safe qualifies as the vault.");
        }

        [Test]
        public void StaffRolesAreDefined()
        {
            List<BankStaffSlot> slots = BankStaffRoles.DefaultSlots();
            Assert.AreEqual(3, slots.Count);
            BankStaffSlot cashier = slots.Find(s => s.SlotId == BankStaffRoles.CashierSlotId);
            Assert.IsNotNull(cashier);
            Assert.IsTrue(cashier.RequiredForOpening, "The cashier is the officer in charge — required for opening.");
            Assert.IsNotNull(slots.Find(s => s.SlotId == BankStaffRoles.TellerSlotId));
            Assert.IsNotNull(slots.Find(s => s.SlotId == BankStaffRoles.ClerkSlotId));
        }

        [Test]
        public void SaveRoundTripPreservesVaultAndLedger()
        {
            var bank = OpenedBank(50000);
            bank.Ledger.OpenAccount("a1", "Samuel", DepositKind.Demand, 10000, 1, diag: diag);
            Assert.IsNull(bank.ReceiveVaultLot(new SpecieLot
            {
                Kind = VaultSpecieKind.GoldCoin, KindName = "gold coin",
                AmountCents = 10000, SourceName = "depositor Samuel",
            }, 1, diag));

            BankRuntime.BankRuntimeSaveDto dto = bank.ToSaveDto();
            var restored = new BankRuntime();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(bank.BusinessInstanceId, restored.BusinessInstanceId);
            Assert.AreEqual(bank.OwnerName, restored.OwnerName);
            Assert.IsTrue(restored.VaultEstablished);
            Assert.AreEqual(60000, restored.VaultSpecieTotalCents());
            Assert.AreEqual(60000, restored.Ledger.CashOnHandCents);
            Assert.AreEqual(10000, restored.Ledger.DepositsOwedCents());
            Assert.AreEqual(1, new List<DepositAccount>(restored.Ledger.Accounts).Count);
        }
    }
}
