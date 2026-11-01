using LandLedgers.Economy;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// P4 money journey: the First Ledger's bookkeeping (Canon §1.2). Every
    /// owner-cash movement is recorded with day, signed amount, reason, and
    /// balance after; the record is player-visible and survives save/load.
    /// </summary>
    [TestFixture]
    public sealed class OwnerCashLedgerTests
    {
        private PlayerPortfolioManager CreateManager(out GameObject go)
        {
            go = new GameObject("portfolio-test");
            return go.AddComponent<PlayerPortfolioManager>();
        }

        [Test]
        public void AddOwnerCashRecordsMovementWithReasonAndBalance()
        {
            GameObject go;
            PlayerPortfolioManager manager = CreateManager(out go);
            try
            {
                manager.AddOwnerCash(250000, "First Ledger starting stake");
                Assert.AreEqual(1, manager.CashMovements.Count);
                OwnerCashMovement movement = manager.CashMovements[0];
                Assert.AreEqual(250000, movement.amountCents);
                Assert.AreEqual(250000, movement.balanceAfterCents);
                StringAssert.Contains("starting stake", movement.reason);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SpendOwnerCashRecordsNegativeMovement()
        {
            GameObject go;
            PlayerPortfolioManager manager = CreateManager(out go);
            try
            {
                manager.AddOwnerCash(250000, "stake");
                bool ok = manager.TrySpendOwnerCash(50000, "test purchase", out _);
                Assert.IsTrue(ok);
                Assert.AreEqual(2, manager.CashMovements.Count);
                OwnerCashMovement movement = manager.CashMovements[1];
                Assert.AreEqual(-50000, movement.amountCents);
                Assert.AreEqual(200000, movement.balanceAfterCents);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FailedSpendRecordsNoMovement()
        {
            GameObject go;
            PlayerPortfolioManager manager = CreateManager(out go);
            try
            {
                manager.AddOwnerCash(10000, "stake");
                bool ok = manager.TrySpendOwnerCash(999999, "too rich", out _);
                Assert.IsFalse(ok);
                Assert.AreEqual(1, manager.CashMovements.Count);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LedgerTextShowsRecentMovementsNewestFirst()
        {
            GameObject go;
            PlayerPortfolioManager manager = CreateManager(out go);
            try
            {
                manager.AddOwnerCash(250000, "First Ledger starting stake");
                manager.TrySpendOwnerCash(50000, "test purchase", out _);
                string text = manager.BuildOwnerCashLedgerText(8);
                StringAssert.Contains("starting stake", text);
                StringAssert.Contains("test purchase", text);
                Assert.IsTrue(text.IndexOf("test purchase") < text.IndexOf("starting stake"),
                    "newest movement should come first");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EmptyLedgerTextStatesNoMovements()
        {
            GameObject go;
            PlayerPortfolioManager manager = CreateManager(out go);
            try
            {
                StringAssert.Contains("no movements", manager.BuildOwnerCashLedgerText(8).ToLower());
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MovementsSurviveSaveLoad()
        {
            GameObject go;
            PlayerPortfolioManager manager = CreateManager(out go);
            try
            {
                manager.AddOwnerCash(250000, "First Ledger starting stake");
                manager.TrySpendOwnerCash(50000, "test purchase", out _);
                LandLedgers.Persistence.PlayerPortfolioSaveDto dto = manager.CaptureSaveDto();

                GameObject go2 = new GameObject("portfolio-test-2");
                try
                {
                    var restored = go2.AddComponent<PlayerPortfolioManager>();
                    restored.LoadFromSaveDto(dto);
                    Assert.AreEqual(2, restored.CashMovements.Count);
                    Assert.AreEqual(250000, restored.CashMovements[0].amountCents);
                    Assert.AreEqual(-50000, restored.CashMovements[1].amountCents);
                    Assert.AreEqual(200000, restored.OwnerCashCents);
                }
                finally
                {
                    Object.DestroyImmediate(go2);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MovementListIsCapped()
        {
            GameObject go;
            PlayerPortfolioManager manager = CreateManager(out go);
            try
            {
                for (int i = 0; i < 600; i++)
                {
                    manager.AddOwnerCash(100, "cap test");
                }

                Assert.AreEqual(500, manager.CashMovements.Count);
                Assert.AreEqual(60000, manager.OwnerCashCents);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
