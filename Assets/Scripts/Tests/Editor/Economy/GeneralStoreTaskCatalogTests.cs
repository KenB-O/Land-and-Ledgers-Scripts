using LandLedgers.FirstLedger;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// TTS-5: the general store task catalog. All numbers are starting tuning
    /// values — the tests lock the MATH (scaling relationships), not the taste.
    /// </summary>
    [TestFixture]
    public sealed class GeneralStoreTaskCatalogTests
    {
        private static EntityId Store(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private TaskAuthority CreateCataloguedAuthority()
        {
            var authority = new TaskAuthority();
            GeneralStoreTaskCatalog.RegisterAll(authority);
            return authority;
        }

        [Test]
        public void RegisterAll_RegistersFourDefinitions()
        {
            var authority = CreateCataloguedAuthority();

            Assert.IsNotNull(authority.GetDefinition(GeneralStoreTaskCatalog.TendCustomerId));
            Assert.IsNotNull(authority.GetDefinition(GeneralStoreTaskCatalog.UnloadFreightId));
            Assert.IsNotNull(authority.GetDefinition(GeneralStoreTaskCatalog.StockShelvesId));
            Assert.IsNotNull(authority.GetDefinition(GeneralStoreTaskCatalog.CleanStoreId));
        }

        [Test]
        public void UnloadFreightMath_ScalesWithQuantity_AndGoodsClass()
        {
            // Kennedy's examples: three dozen eggs ≈ 2–3 min; a full wagon = tens of minutes.
            Assert.AreEqual(3, GeneralStoreTaskCatalog.ComputeUnloadMinutes(FreightGoodsClass.FragileAndPerishable, 36));
            Assert.AreEqual(3, GeneralStoreTaskCatalog.ComputeUnloadMinutes(FreightGoodsClass.Perishable, 36));
            Assert.AreEqual(2, GeneralStoreTaskCatalog.ComputeUnloadMinutes(FreightGoodsClass.Standard, 36));
            Assert.AreEqual(50, GeneralStoreTaskCatalog.ComputeUnloadMinutes(FreightGoodsClass.Perishable, 600));
            Assert.AreEqual(1, GeneralStoreTaskCatalog.ComputeUnloadMinutes(FreightGoodsClass.Standard, 1));
            Assert.AreEqual(1, GeneralStoreTaskCatalog.ComputeUnloadMinutes(FreightGoodsClass.Standard, 0));
        }

        [Test]
        public void TendCustomer_BaseFourMinutes_NotSelfServe()
        {
            var authority = CreateCataloguedAuthority();

            WorkTask task = GeneralStoreTaskCatalog.EnqueueTendCustomer(authority, Store(1), 1, "customer-mary");
            Assert.AreEqual(TaskStatus.Queued, task.Status); // waits until a worker is assigned
            Assert.AreEqual("customer-mary", task.CustomerRef);
            Assert.AreEqual(4, task.PlannedMinutes);
            Assert.AreEqual(TaskPriority.High, task.Priority);
        }

        [Test]
        public void UnloadFreight_Enqueue_CarriesQuantityDrivenPlan()
        {
            var authority = CreateCataloguedAuthority();

            WorkTask eggs = GeneralStoreTaskCatalog.EnqueueUnloadFreight(authority, Store(1), 1, FreightGoodsClass.FragileAndPerishable, 36, "shipment-7");
            Assert.AreEqual(3, eggs.PlannedMinutes);
            Assert.AreEqual("shipment-7", eggs.CustomerRef);
            Assert.IsTrue(eggs.HasPlannedMinutesOverride);
        }

        [Test]
        public void FreightTime_StillScales_WithWorkerSkill()
        {
            var skills = new SkillService();
            skills.RegisterStarterSet();

            var authority = CreateCataloguedAuthority();
            authority.SetDurationEstimator(new SkillTaskDurationEstimator(skills));

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            // Veteran freight handler (level 4): 3 base minutes shaved to the 1-minute floor.
            skills.GrantPractice(Person(30), SkillIds.FreightHandling, 300);
            WorkTask task = GeneralStoreTaskCatalog.EnqueueUnloadFreight(authority, Store(1), 1, FreightGoodsClass.FragileAndPerishable, 36);

            string reason;
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(30), budgets, out reason), reason);
            Assert.AreEqual(1, task.PlannedMinutes);

            // A big wagon still takes real time even for the veteran: 50 - 3 = 47.
            WorkTask wagon = GeneralStoreTaskCatalog.EnqueueUnloadFreight(authority, Store(1), 1, FreightGoodsClass.Perishable, 600);
            Assert.IsTrue(authority.AssignTask(wagon.TaskId, Person(30), budgets, out reason), reason);
            Assert.AreEqual(47, wagon.PlannedMinutes);
        }

        [Test]
        public void StockShelves_AndCleanStore_CarryDocumentedRates()
        {
            var authority = CreateCataloguedAuthority();

            WorkTask stock = GeneralStoreTaskCatalog.EnqueueStockShelves(authority, Store(1), 1, 5);
            Assert.AreEqual(10, stock.PlannedMinutes); // 5 lots x 2 min

            WorkTask clean = GeneralStoreTaskCatalog.EnqueueCleanStore(authority, Store(1), 1, "sales-floor");
            Assert.AreEqual(15, clean.PlannedMinutes);
            Assert.AreEqual("sales-floor", clean.CustomerRef);
        }
    }
}
