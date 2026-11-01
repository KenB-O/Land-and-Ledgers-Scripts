using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: medicine reorder policy — the bootstrap endowment is one-time and
    /// lots never auto-replenish, so the practice needs a visible signal
    /// before the cabinet runs dry (Canon §13.3D).
    /// </summary>
    [TestFixture]
    public sealed class DoctorMedicinePolicyTests
    {
        private static DoctorMedicineStock StockedCabinet(int dayIndex)
        {
            var stock = new DoctorMedicineStock();
            var registry = new EntityIdRegistry();
            DoctorMedicineBootstrap.ApplyBootstrapEndowment(stock, registry, dayIndex, new List<string>());
            return stock;
        }

        [Test]
        public void EvaluateReorderSignals_FullCabinet_NoSignal()
        {
            var stock = StockedCabinet(290); // 60 remedies, 30 dressings

            var signals = DoctorMedicinePolicy.EvaluateReorderSignals(
                stock, new DoctorMedicineReorderPolicy(), 300, new List<string>());

            Assert.AreEqual(0, signals.Count);
        }

        [Test]
        public void EvaluateReorderSignals_BelowThreshold_SignalsWithSuggestedQty()
        {
            var stock = StockedCabinet(290);
            var diag = new List<string>();
            // Drain remedies below the default 20-dose threshold.
            var drained = stock.TryDispenseDoses(DoctorTreatmentCatalog.MedicineDoseItemId, 45, 295, diag);
            Assert.NotNull(drained);

            var signals = DoctorMedicinePolicy.EvaluateReorderSignals(
                stock, new DoctorMedicineReorderPolicy(), 300, diag);

            Assert.AreEqual(1, signals.Count);
            var signal = signals[0];
            Assert.AreEqual(DoctorTreatmentCatalog.MedicineDoseItemId, signal.MedicineName);
            Assert.AreEqual(15, signal.DosesOnHand);
            Assert.AreEqual(60, signal.SuggestedOrderDoses);
            StringAssert.Contains("import order", signal.Reason, "Signals never order — the caller places real import orders.");
            Assert.IsTrue(diag.Count > 0, "Reorder signals must be loud.");
        }

        [Test]
        public void EvaluateReorderSignals_BothLow_BothSignal()
        {
            var stock = StockedCabinet(290);
            var diag = new List<string>();
            Assert.NotNull(stock.TryDispenseDoses(DoctorTreatmentCatalog.MedicineDoseItemId, 55, 295, diag));
            Assert.NotNull(stock.TryDispenseDoses(DoctorTreatmentCatalog.DressingItemId, 25, 295, diag));

            var signals = DoctorMedicinePolicy.EvaluateReorderSignals(
                stock, new DoctorMedicineReorderPolicy(), 300, diag);

            Assert.AreEqual(2, signals.Count);
        }

        [Test]
        public void EvaluateReorderSignals_CustomPolicy_Respected()
        {
            var stock = StockedCabinet(290);
            var diag = new List<string>();
            Assert.NotNull(stock.TryDispenseDoses(DoctorTreatmentCatalog.MedicineDoseItemId, 45, 295, diag));

            var policy = new DoctorMedicineReorderPolicy
            {
                RemedyThresholdDoses = 10, // 15 on hand > 10: no signal
                RemedyOrderDoses = 100,
            };

            var signals = DoctorMedicinePolicy.EvaluateReorderSignals(stock, policy, 300, diag);

            Assert.AreEqual(0, signals.Count, "Per-practice policy thresholds are honored.");
        }

        [Test]
        public void EvaluateReorderSignals_NullStock_NeverThrows()
        {
            var signals = DoctorMedicinePolicy.EvaluateReorderSignals(
                null, new DoctorMedicineReorderPolicy(), 300, new List<string>());

            Assert.AreEqual(0, signals.Count);
        }

        [Test]
        public void PracticeRuntime_EvaluateMedicineReorder_PassesThrough()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            DoctorMedicineBootstrap.ApplyBootstrapEndowment(runtime.MedicineStock, registry, 290, diag);
            Assert.NotNull(runtime.MedicineStock.TryDispenseDoses(DoctorTreatmentCatalog.MedicineDoseItemId, 55, 295, diag));

            var signals = runtime.EvaluateMedicineReorder(300, diag);

            Assert.AreEqual(1, signals.Count);
            Assert.AreEqual(DoctorTreatmentCatalog.MedicineDoseItemId, signals[0].MedicineName);
        }
    }
}
