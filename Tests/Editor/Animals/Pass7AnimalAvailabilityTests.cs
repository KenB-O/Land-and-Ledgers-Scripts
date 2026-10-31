using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Animals
{
    /// <summary>
    /// Pass VII Workstream B (Animals and Shared Resources): one horse, one
    /// identity, one availability truth. The mount path
    /// (HorseAuthority.TryReserveUse) and the draft path
    /// (DraftPowerService.TryReserveAnimals) contend on the SAME unified
    /// reservation record; rental, sale, travel, care, and injury all run
    /// through the production authorities. Player and NPC use the same rule.
    /// </summary>
    [TestFixture]
    public sealed class Pass7AnimalAvailabilityTests
    {
        private sealed class StubFeedStock : IHorseFeedStock
        {
            public int FeedStockUnits { get; private set; }
            public StubFeedStock(int units) { FeedStockUnits = units; }
            public int TakeFeedUnits(int requestedUnits)
            {
                int taken = System.Math.Min(System.Math.Max(0, requestedUnits), FeedStockUnits);
                FeedStockUnits -= taken;
                return taken;
            }
        }

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public AnimalRegistry Registry;
            public EquipmentReservationService Reservations;
            public AnimalAvailabilityAuthority Availability;
            public DraftPowerService DraftPower;
            public HorseRentalService Rentals = new HorseRentalService();

            public Fixture()
            {
                Registry = new AnimalRegistry(Ids);
                Reservations = new EquipmentReservationService();
                Availability = new AnimalAvailabilityAuthority(Reservations);
                DraftPower = new DraftPowerService(Reservations);
                DraftPower.NowGameSeconds = () => 0f;
            }

            public AnimalState AddHorse(
                AnimalOwnerKind ownerKind = AnimalOwnerKind.Household, string ownerId = "H-1")
            {
                AnimalState horse = Registry.RegisterAnimal(
                    AnimalSpecies.Horse, "draft", AnimalSex.Female, ownerKind, ownerId, 0, "test herd");
                Assert.IsNull(HorseAuthority.Configure(
                    horse, HorseUseCapabilities.GeneralPurpose, "farm-1", "stable-1", 0));
                return horse;
            }
        }

        private static List<EntityId> AsList(params EntityId[] ids) => new List<EntityId>(ids);

        // ---------- mandatory: one horse cannot plow and ride at once ----------

        [Test]
        public void DraftThenMount_OverlappingWindow_RefusedOnSameRecord()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            var diag = new List<string>();
            Assert.IsTrue(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out List<EntityId> team, diag));
            Assert.That(team.Count, Is.EqualTo(1));

            // The mount path now contends on the SAME reservation record.
            string refusal = HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:P1", f.Availability, 0f, 3600f, out _);
            Assert.IsNotNull(refusal, "a plowing horse cannot also be mounted");
            StringAssert.Contains("plowing", refusal);
        }

        [Test]
        public void MountThenDraft_OverlappingWindow_DraftFindsNoTeam()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            Assert.IsNull(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:P1", f.Availability, 0f, 3600f, out _));

            var diag = new List<string>();
            Assert.IsFalse(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out _, diag));
            StringAssert.Contains("no double-booking", string.Join(" ", diag.ToArray()));
            HorseAuthority.ReleaseUse(horse, f.Availability);
        }

        // ---------- mandatory: conflicting task refused; compatible windows work ----------

        [Test]
        public void OverlappingRentalWindow_Refused_NamesConflict()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            Assert.IsNull(f.Availability.TryReserve(
                horse.AnimalId.ToString(), "draft", "plow:field-1", 0f, 100f, out _));
            string refusal = f.Availability.TryReserve(
                horse.AnimalId.ToString(), "mount", "mount:P2", 50f, 150f, out _);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("plow:field-1", refusal);
        }

        [Test]
        public void CompatibleWindows_BothReservationsSucceed()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            Assert.IsNull(f.Availability.TryReserve(
                horse.AnimalId.ToString(), "draft", "plow:field-1", 0f, 100f, out _));
            Assert.IsNull(f.Availability.TryReserve(
                horse.AnimalId.ToString(), "mount", "mount:P2", 200f, 300f, out _));
            // Adjacent windows share no instant: also compatible.
            Assert.IsNull(f.Availability.TryReserve(
                horse.AnimalId.ToString(), "travel", "travel:P3", 100f, 200f, out _));
            Assert.IsFalse(f.Availability.IsAvailable(horse.AnimalId.ToString(), 150f));
            Assert.IsFalse(f.Availability.IsAvailable(horse.AnimalId.ToString(), 50f));
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 350f));
        }

        // ---------- mandatory: owner and custodian remain distinct ----------

        [Test]
        public void Rental_KeepsOwnerAndCustodianDistinct()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Household, "H-1");
            var borrowerLedger = new HouseholdLedger(7);
            var ownerLedger = new HouseholdLedger(1);
            Assert.IsNull(borrowerLedger.RecordInflow(0, 10000, HouseholdIncomeSource.Remittance, "test", "funding", "test"));
            var record = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "household",
                BorrowerId = "H-7",
                BorrowerName = "Borrower Seven",
                FromDayIndex = 1,
                ToDayIndex = 5,
                FeeCents = 500,
                TermsNote = "borrower liable for injury beyond normal wear",
                Availability = f.Availability,
                DayIndex = 0,
                BorrowerLedger = borrowerLedger,
                OwnerLedger = ownerLedger,
            }, new List<string>());
            Assert.IsNotNull(record);
            Assert.That(horse.OwnerKind, Is.EqualTo(AnimalOwnerKind.Household));
            Assert.That(horse.OwnerId, Is.EqualTo("H-1"));
            Assert.That(horse.CustodianKind, Is.EqualTo("household"));
            Assert.That(horse.CustodianId, Is.EqualTo("H-7"));
            // The fee moved borrower -> owner, nothing created or lost.
            Assert.That(borrowerLedger.GetBalanceCents(), Is.EqualTo(9500));
            Assert.That(ownerLedger.GetBalanceCents(), Is.EqualTo(500));
        }

        // ---------- mandatory: rental prevents conflicting owner use ----------

        [Test]
        public void Rental_PreventsConflictingOwnerUse()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Household, "H-1");
            var diag = new List<string>();
            var record = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "person",
                BorrowerId = "P-9",
                BorrowerName = "Renter",
                FromDayIndex = 2,
                ToDayIndex = 6,
                FeeCents = 0,
                Availability = f.Availability,
                DayIndex = 0,
            }, diag);
            Assert.IsNotNull(record, string.Join(" | ", diag.ToArray()));

            // The owner cannot mount the horse during the rental term.
            float day3 = 3 * 86400f;
            string refusal = HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:H-1", f.Availability, day3, day3 + 3600f, out _);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("rental", refusal);

            // The draft path skips the rented horse too.
            f.DraftPower.NowGameSeconds = () => day3;
            Assert.IsFalse(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out _, new List<string>()));
            f.DraftPower.NowGameSeconds = () => 0f;
        }

        // ---------- mandatory: sale ends former-owner authority ----------

        [Test]
        public void Sale_ReleasesLiveReservations_TransfersOwnershipAndCustody()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Business, "ranch-1");
            var diag = new List<string>();
            Assert.IsTrue(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out _, diag));
            Assert.IsTrue(f.DraftPower.IsAnimalReserved(horse.AnimalId));

            var sellerLedger = new HouseholdLedger(11);
            HorseSale sale = HorseTrade.ExecuteSale(
                f.Registry, horse.AnimalId, "ranch-1", "Ranch One", "farm-2", "Farm Two",
                50000, 3, sellerLedger, diag, f.Reservations, f.Rentals);
            Assert.IsNotNull(sale, string.Join(" | ", diag.ToArray()));

            // No stale reservations: the sold horse is free on the shared record.
            Assert.IsFalse(f.DraftPower.IsAnimalReserved(horse.AnimalId));
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 0f));
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.None));
            // Ownership and custody transferred cleanly to the buyer.
            Assert.That(horse.OwnerKind, Is.EqualTo(AnimalOwnerKind.Business));
            Assert.That(horse.OwnerId, Is.EqualTo("farm-2"));
            Assert.That(horse.CustodianId, Is.EqualTo("farm-2"));
            Assert.That(sellerLedger.GetBalanceCents(), Is.EqualTo(50000));
        }

        [Test]
        public void Sale_TerminatesActiveRental_Loudly()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Business, "ranch-1");
            var diag = new List<string>();
            var record = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Business,
                AuthorizingOwnerId = "ranch-1",
                BorrowerKind = "household",
                BorrowerId = "H-3",
                BorrowerName = "Renter Three",
                FromDayIndex = 1,
                ToDayIndex = 10,
                FeeCents = 0,
                Availability = f.Availability,
                DayIndex = 0,
            }, diag);
            Assert.IsNotNull(record, string.Join(" | ", diag.ToArray()));

            var sellerLedger = new HouseholdLedger(11);
            HorseSale sale = HorseTrade.ExecuteSale(
                f.Registry, horse.AnimalId, "ranch-1", "Ranch One", "farm-2", "Farm Two",
                40000, 4, sellerLedger, diag, f.Reservations, f.Rentals);
            Assert.IsNotNull(sale, string.Join(" | ", diag.ToArray()));
            Assert.That(record.Status, Is.EqualTo(HorseRentalStatus.TerminatedSale));
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 5 * 86400f));
            StringAssert.Contains("terminated by sale", string.Join(" ", diag.ToArray()));
        }

        // ---------- mandatory: injury blocks incompatible work ----------

        [Test]
        public void Injury_BlocksDraftMountAndRental_WithRealConsequences()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            Assert.IsNull(HorseAuthority.RecordInjury(horse, "stepped in a gopher hole", 0.7f, 5));
            Assert.That(horse.Health01, Is.LessThan(0.35f));

            // Suitability fails everywhere.
            Assert.IsFalse(HorseAuthority.IsWorkReady(horse));
            Assert.IsFalse(HorseTrade.IsDraftCapable(horse));
            string refusal = HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:P1", f.Availability, 0f, 3600f, out _);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("not fit for work", refusal);

            // The draft path cannot team the injured horse: no phantom team.
            var diag = new List<string>();
            Assert.IsFalse(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out _, diag));

            // The rental path refuses to hand over an unfit horse.
            var rental = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "person",
                BorrowerId = "P-2",
                FromDayIndex = 1,
                ToDayIndex = 3,
                Availability = f.Availability,
                DayIndex = 0,
            }, diag);
            Assert.IsNull(rental);
            StringAssert.Contains("not fit for work", string.Join(" ", diag.ToArray()));
        }

        [Test]
        public void LowCondition_BlocksDraftCapability()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            horse.Fatigue01 = 0.95f; // exhausted but uninjured
            Assert.IsFalse(HorseAuthority.IsWorkReady(horse));
            Assert.IsFalse(HorseTrade.IsDraftCapable(horse));
            horse.Fatigue01 = 0f;
            Assert.IsTrue(HorseTrade.IsDraftCapable(horse));
        }

        // ---------- mandatory: save/load preserves reservation and condition ----------

        [Test]
        public void SaveLoad_PreservesReservationAndCondition()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            horse.Fatigue01 = 0.42f;
            horse.Health01 = 0.81f;
            var diag = new List<string>();
            Assert.IsTrue(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out _, diag));

            // The unified authority's save section round-trips the live hold.
            var authorityDto = f.Reservations.CaptureSaveDto();
            var restoredAuthority = new EquipmentReservationService();
            restoredAuthority.LoadFromSaveDto(authorityDto);
            Assert.IsTrue(restoredAuthority.IsReserved(
                ReservationKeyConventions.Animal(horse.AnimalId.ToString()), 0f));

            // The registry round-trips the animal's condition.
            var animals = new List<AnimalState>();
            f.Registry.ExportState(animals,
                new List<HistoricalAnimalRecord>(), new List<LivestockCohort>(), new List<EggBatchState>());
            var restoredRegistry = new AnimalRegistry(new EntityIdRegistry());
            restoredRegistry.ImportState(animals, null, null, null);
            AnimalState restored = restoredRegistry.GetAnimal(horse.AnimalId);
            Assert.IsNotNull(restored);
            Assert.That(restored.Fatigue01, Is.EqualTo(0.42f));
            Assert.That(restored.Health01, Is.EqualTo(0.81f));

            // The rental service round-trips its own records.
            var rentalDto = f.Rentals.CaptureSaveDto();
            var restoredRentals = new HorseRentalService();
            restoredRentals.LoadFromSaveDto(rentalDto);
            Assert.That(restoredRentals.Rentals.Count, Is.EqualTo(0));
        }

        // ---------- mandatory: cancelled work releases reservations ----------

        [Test]
        public void CancelledWork_ReleasesReservationsCorrectly()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            var diag = new List<string>();
            Assert.IsTrue(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out List<EntityId> team, diag));
            f.DraftPower.ReleaseAnimals(team);
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 0f));

            Assert.IsNull(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:P1", f.Availability, 0f, 3600f, out string reservationId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(horse.CurrentReservationId));
            HorseAuthority.ReleaseUse(horse, f.Availability);
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 0f));
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.None));
            Assert.That(horse.CurrentReservationId, Is.EqualTo(string.Empty));
            // Release is idempotent: releasing twice harms nothing.
            HorseAuthority.ReleaseUse(horse, f.Availability);
            f.Reservations.Release("no-such-reservation");
        }

        // ---------- mandatory: no phantom animals or duplicate capacity ----------

        [Test]
        public void Reservations_NeverCreateAnimals_NeverDuplicateCapacity()
        {
            var f = new Fixture();
            int before = f.Registry.ActiveCount;
            AnimalState horse = f.AddHorse();
            Assert.That(f.Registry.ActiveCount, Is.EqualTo(before + 1));

            // Reserving marks the real animal unavailable; it creates nothing.
            Assert.IsNull(f.Availability.TryReserve(
                horse.AnimalId.ToString(), "draft", "plow:1", 0f, 100f, out _));
            Assert.That(f.Registry.ActiveCount, Is.EqualTo(before + 1));
            Assert.That(f.Reservations.AvailableCapacity(
                ReservationKeyConventions.Animal(horse.AnimalId.ToString()), 50f), Is.EqualTo(0f));

            // The same record cannot be taken twice: capacity never goes negative.
            Assert.IsNotNull(f.Availability.TryReserve(
                horse.AnimalId.ToString(), "mount", "mount:P1", 0f, 100f, out _));
            Assert.That(f.Reservations.AvailableCapacity(
                ReservationKeyConventions.Animal(horse.AnimalId.ToString()), 50f), Is.EqualTo(0f));
        }

        // ---------- mandatory: the same rule for player and NPC ----------

        [Test]
        public void PlayerAndNpc_UseTheSameAvailabilityRule()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            // The player mounts first through the shared interaction rule.
            Assert.IsNull(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:player-1", f.Availability, 0f, 7200f, out _));
            // An NPC's identical request is refused on the same record.
            string npcRefusal = HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:npc-7", f.Availability, 0f, 7200f, out _);
            Assert.IsNotNull(npcRefusal);
            HorseAuthority.ReleaseUse(horse, f.Availability);
            // And the reverse: NPC first, player refused.
            Assert.IsNull(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:npc-7", f.Availability, 0f, 7200f, out _));
            Assert.IsNotNull(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:player-1", f.Availability, 0f, 7200f, out _));
            HorseAuthority.ReleaseUse(horse, f.Availability);
        }

        // ---------- P1-5: condition lifecycle ----------

        [Test]
        public void CareDay_FeedsHorsesWithRealCost_UnfedHorsesDegrade()
        {
            var f = new Fixture();
            AnimalState idle = f.AddHorse();
            AnimalState worker = f.AddHorse();
            idle.Fatigue01 = 0.5f;
            worker.Fatigue01 = 0.6f;

            // Two horses need 2 + 3 = 5 units; the stock holds 1: shortfall.
            var stock = new StubFeedStock(1);
            var diag = new List<string>();
            int unfed = HorseCareDriver.CareDay(
                f.Registry, stock, h => h == worker, 10, diag);
            Assert.That(unfed, Is.EqualTo(2));
            Assert.That(stock.FeedStockUnits, Is.EqualTo(0));
            Assert.That(idle.Nutrition01, Is.LessThan(1f));
            Assert.That(idle.LastCareDayIndex, Is.EqualTo(10));

            // A full stock feeds and recovers through ApplyCare.
            var fullStock = new StubFeedStock(100);
            int unfed2 = HorseCareDriver.CareDay(
                f.Registry, fullStock, h => false, 11, new List<string>());
            Assert.That(unfed2, Is.EqualTo(0));
            Assert.That(idle.Fatigue01, Is.LessThan(0.5f));
            Assert.That(fullStock.FeedStockUnits, Is.EqualTo(100 - 4));
        }

        [Test]
        public void CareDay_RepeatedSameDayDoesNotConsumeOrApplyCareTwice()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            horse.Nutrition01 = 0.5f;
            horse.Fatigue01 = 0.6f;
            var stock = new StubFeedStock(2);
            var diagnostics = new List<string>();

            int firstResult = HorseCareDriver.CareDay(f.Registry, stock, _ => false, 10, diagnostics);
            float nutritionAfterFirstCare = horse.Nutrition01;
            float fatigueAfterFirstCare = horse.Fatigue01;
            int stockAfterFirstCare = stock.FeedStockUnits;

            int repeatedResult = HorseCareDriver.CareDay(f.Registry, stock, _ => false, 10, diagnostics);

            Assert.That(firstResult, Is.EqualTo(0));
            Assert.That(repeatedResult, Is.EqualTo(0));
            Assert.That(horse.LastCareDayIndex, Is.EqualTo(10));
            Assert.That(stock.FeedStockUnits, Is.EqualTo(stockAfterFirstCare));
            Assert.That(horse.Nutrition01, Is.EqualTo(nutritionAfterFirstCare));
            Assert.That(horse.Fatigue01, Is.EqualTo(fatigueAfterFirstCare));
        }

        [Test]
        public void WorkPlowSession_AccruesRealConditionCost()
        {
            var f = new Fixture();
            AnimalState h1 = f.AddHorse(AnimalOwnerKind.Business, "farm-1");
            AnimalState h2 = f.AddHorse(AnimalOwnerKind.Business, "farm-1");
            var driver = EntityId.For(EntityKind.Person, 41);
            var plow = new EquipmentAsset
            {
                AssetId = "EQ-plow-1", Kind = "plow", DisplayName = "Moldboard plow",
                Condition01 = 1f, OwnerKind = "business", OwnerId = "farm-1",
            };
            var harness = new EquipmentAsset
            {
                AssetId = "EQ-harness-1", Kind = "harness", DisplayName = "Team harness",
                Condition01 = 1f, OwnerKind = "business", OwnerId = "farm-1",
            };
            var diag = new List<string>();
            DraftWorkUnit unit = DraftWorkUnitService.AssemblePlowUnit(
                f.DraftPower, f.Registry, AsList(h1.AnimalId, h2.AnimalId), AsList(driver),
                plow, harness, "plowing", diag);
            Assert.IsNotNull(unit, string.Join(" | ", diag.ToArray()));

            float fatigueBefore = h1.Fatigue01;
            float nutritionBefore = h1.Nutrition01;
            Assert.IsNull(DraftWorkUnitService.WorkPlowSession(unit, f.Registry, 10f, 0.2f, diag));
            // Real cost: the acres plowed moved fatigue and nutrition, and the
            // cost scales with the horse's own state (no flat constants).
            Assert.That(h1.Fatigue01, Is.GreaterThan(fatigueBefore));
            Assert.That(h1.Nutrition01, Is.LessThan(nutritionBefore));
            DraftWorkUnitService.ReleaseUnit(unit, f.DraftPower);
            Assert.IsTrue(f.Availability.IsAvailable(h1.AnimalId.ToString(), 0f));
        }

        [Test]
        public void Overwork_BreaksDownCausally_NoRandomRoll()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            horse.Fatigue01 = 0.96f; // already near exhaustion
            float healthBefore = horse.Health01;
            HorseAuthority.ComputeWorkCost(horse, 0.2f,
                out _, out _, out float conditionCost01);
            Assert.That(conditionCost01, Is.GreaterThan(0f));
            HorseAuthority.ApplyWork(horse, 0.2f, 0.1f, conditionCost01);
            Assert.That(horse.Health01, Is.LessThan(healthBefore));

            // A fresh horse worked the same amount takes no health damage.
            AnimalState fresh = f.AddHorse();
            HorseAuthority.ComputeWorkCost(fresh, 0.2f,
                out _, out _, out float freshConditionCost);
            Assert.That(freshConditionCost, Is.EqualTo(0f));
        }

        // ---------- rental: return, damage, credit route, authorization ----------

        [Test]
        public void Rental_Return_ReleasesHold_RestoresCustody_RecordsDamageAsHold()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Household, "H-1");
            var diag = new List<string>();
            var record = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "household",
                BorrowerId = "H-7",
                BorrowerName = "Borrower Seven",
                FromDayIndex = 1,
                ToDayIndex = 5,
                FeeCents = 0,
                TermsNote = "borrower liable for injury beyond normal wear",
                Availability = f.Availability,
                DayIndex = 0,
            }, diag);
            Assert.IsNotNull(record, string.Join(" | ", diag.ToArray()));

            // The horse is hurt in the borrower's care (causal, named).
            Assert.IsNull(HorseAuthority.RecordInjury(horse, "fence collision while borrowed", 0.3f, 3));

            Assert.IsNull(f.Rentals.ReturnHorse(f.Registry, horse.AnimalId, 5, f.Availability, diag));
            Assert.That(record.Status, Is.EqualTo(HorseRentalStatus.Returned));
            StringAssert.Contains("open hold", record.DamageNote);
            StringAssert.Contains("borrower liable for injury beyond normal wear", record.DamageNote);
            // Custody restored to the owner; the hold released; ownership untouched.
            Assert.That(horse.CustodianId, Is.EqualTo("H-1"));
            Assert.That(horse.OwnerId, Is.EqualTo("H-1"));
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 3 * 86400f));
        }

        [Test]
        public void Rental_CreditRoute_RecordsPayableObligation()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Household, "H-1");
            var obligations = new FinancialObligationAuthority();
            var diag = new List<string>();
            var record = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "person",
                BorrowerId = "P-5",
                BorrowerName = "Borrower Five",
                FromDayIndex = 1,
                ToDayIndex = 4,
                FeeCents = 1200,
                Availability = f.Availability,
                DayIndex = 0,
                Obligations = obligations,
                Ids = f.Ids,
            }, diag);
            Assert.IsNotNull(record, string.Join(" | ", diag.ToArray()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(record.FeeObligationId));
            bool found = false;
            foreach (FinancialObligation o in obligations.Obligations)
            {
                if (o != null && o.ObligationId == record.FeeObligationId && o.Kind == FinancialObligationKind.Payable)
                { found = true; break; }
            }
            Assert.IsTrue(found, "the fee became a real Payable obligation");
        }

        [Test]
        public void Rental_UnauthorizedOrUnpayable_RefusedCleanly()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Household, "H-1");
            var diag = new List<string>();
            // Not the owner: refused, nothing reserved.
            var refused = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-IMPOSTOR",
                BorrowerKind = "person",
                BorrowerId = "P-5",
                FromDayIndex = 1,
                ToDayIndex = 4,
                Availability = f.Availability,
                DayIndex = 0,
            }, diag);
            Assert.IsNull(refused);
            StringAssert.Contains("not authorized", string.Join(" ", diag.ToArray()));
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 2 * 86400f));

            // Fee with no payment path: refused, the hold rolled back.
            var poorBorrower = new HouseholdLedger(9);
            var noPay = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "person",
                BorrowerId = "P-5",
                FromDayIndex = 1,
                ToDayIndex = 4,
                FeeCents = 500,
                Availability = f.Availability,
                DayIndex = 0,
                BorrowerLedger = poorBorrower,
                OwnerLedger = null,
                Obligations = null,
            }, diag);
            Assert.IsNull(noPay);
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 2 * 86400f));
            Assert.AreNotEqual("P-5", horse.CustodianId);
        }

        // ---------- P2-9: horseback bound to real animals ----------

        private static JourneyModel TwoTownModel()
        {
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(
                new JourneyLocation("farm-1", JourneyLocationKind.Farmstead, "Farm 1", 0f, 0f)));
            Assert.IsNull(model.RegisterLocation(
                new JourneyLocation("town", JourneyLocationKind.TownBuilding, "Town", 7f, 0f)));
            Assert.IsNull(model.AddEdge("farm-1", "town", 7f, "farm road"));
            return model;
        }

        [Test]
        public void PlanMountedTravel_BindsRealHorse_RefusesWithoutOne()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse();
            var authority = new TaskAuthority();
            JourneyTravel.RegisterTaskDefinitions(authority);
            var model = TwoTownModel();
            var person = EntityId.For(EntityKind.Person, 3);
            var diag = new List<string>();

            // No candidate horses: refused honestly, no phantom horse.
            WorkTask horseless = JourneyTravel.PlanMountedTravel(
                authority, model, f.Registry, f.Availability, person,
                new List<EntityId>(), "farm-1", "town", 0, 0f, diag);
            Assert.IsNull(horseless);
            StringAssert.Contains("no phantom horse", string.Join(" ", diag.ToArray()));

            // A real horse: the trip is planned AND the horse is reserved.
            WorkTask task = JourneyTravel.PlanMountedTravel(
                authority, model, f.Registry, f.Availability, person,
                AsList(horse.AnimalId), "farm-1", "town", 0, 0f, diag);
            Assert.IsNotNull(task, string.Join(" | ", diag.ToArray()));
            Assert.IsFalse(f.Availability.IsAvailable(horse.AnimalId.ToString(), 1800f));
            Assert.That(horse.CurrentUse, Is.EqualTo(HorseUseKind.Mounted));
            // The miles ridden cost the horse real fatigue.
            Assert.That(horse.Fatigue01, Is.GreaterThan(0f));

            // The same horse cannot be double-booked for a second trip.
            WorkTask second = JourneyTravel.PlanMountedTravel(
                authority, model, f.Registry, f.Availability, person,
                AsList(horse.AnimalId), "farm-1", "town", 0, 0f, diag);
            Assert.IsNull(second);
            HorseAuthority.ReleaseUse(horse, f.Availability);
        }

        [Test]
        public void PlanMountedTravel_SkipsUnsuitableHorses()
        {
            var f = new Fixture();
            AnimalState lame = f.AddHorse();
            AnimalState sound = f.AddHorse();
            Assert.IsNull(HorseAuthority.RecordInjury(lame, "test injury", 0.8f, 0));
            var authority = new TaskAuthority();
            JourneyTravel.RegisterTaskDefinitions(authority);
            var model = TwoTownModel();
            var person = EntityId.For(EntityKind.Person, 3);

            // The lame horse is skipped; the sound one carries the trip.
            WorkTask task = JourneyTravel.PlanMountedTravel(
                authority, model, f.Registry, f.Availability, person,
                AsList(lame.AnimalId, sound.AnimalId), "farm-1", "town", 0, 0f,
                new List<string>());
            Assert.IsNotNull(task);
            Assert.That(sound.CurrentUse, Is.EqualTo(HorseUseKind.Mounted));
            Assert.That(lame.CurrentUse, Is.EqualTo(HorseUseKind.None));
            HorseAuthority.ReleaseUse(sound, f.Availability);
        }

        // ---------- Slice C: the shared horse ----------

        [Test]
        public void SliceC_SharedHorse_CompetingTasks_RentalChangesCustody()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Household, "H-1");
            var diag = new List<string>();

            // Day 0: the horse plows (draft path holds the shared record).
            Assert.IsTrue(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out _, diag));

            // A rental overlapping the plow day is refused: genuine opportunity cost.
            var rentalAttempt = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "household",
                BorrowerId = "H-2",
                FromDayIndex = 0,
                ToDayIndex = 2,
                Availability = f.Availability,
                DayIndex = 0,
            }, diag);
            Assert.IsNull(rentalAttempt);
            StringAssert.Contains("plowing", string.Join(" ", diag.ToArray()));

            // The plow day ends (the draft hold is released); a compatible
            // window works: the horse is ridden on day 5.
            f.DraftPower.ReleaseAnimals(AsList(horse.AnimalId));
            float day5 = 5 * 86400f;
            Assert.IsNull(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, "mount:P1", f.Availability, day5, day5 + 3600f, out _));
            HorseAuthority.ReleaseUse(horse, f.Availability);

            // The rental for days 3-4 succeeds and custody moves.
            var rental = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "household",
                BorrowerId = "H-2",
                BorrowerName = "Neighbor Two",
                FromDayIndex = 3,
                ToDayIndex = 4,
                FeeCents = 0,
                Availability = f.Availability,
                DayIndex = 1,
            }, diag);
            Assert.IsNotNull(rental, string.Join(" | ", diag.ToArray()));
            Assert.That(horse.CustodianId, Is.EqualTo("H-2"));
            Assert.That(horse.OwnerId, Is.EqualTo("H-1"));

            // During the rental the owner cannot reclaim the horse for draft.
            f.DraftPower.NowGameSeconds = () => 3 * 86400f + 100f;
            Assert.IsFalse(f.DraftPower.TryReserveAnimals(
                AsList(horse.AnimalId), 1, id => HorseTrade.IsDraftCapable(f.Registry.GetAnimal(id)),
                "plowing", out _, new List<string>()));
            f.DraftPower.NowGameSeconds = () => 0f;

            // Return restores custody and frees the shared record.
            Assert.IsNull(f.Rentals.ReturnHorse(f.Registry, horse.AnimalId, 4, f.Availability, diag));
            Assert.That(horse.CustodianId, Is.EqualTo("H-1"));
            Assert.IsTrue(f.Availability.IsAvailable(horse.AnimalId.ToString(), 3 * 86400f));
        }

        // ---------- Slice D: horse impairment ----------

        [Test]
        public void SliceD_ImpairedHorse_CommitmentsRespond_NoFreeReplacement()
        {
            var f = new Fixture();
            AnimalState a = f.AddHorse(AnimalOwnerKind.Business, "farm-1");
            AnimalState b = f.AddHorse(AnimalOwnerKind.Business, "farm-1");
            AnimalState spare = f.AddHorse(AnimalOwnerKind.Household, "H-9");
            var driver = EntityId.For(EntityKind.Person, 41);
            var plow = new EquipmentAsset
            {
                AssetId = "EQ-plow-9", Kind = "plow", DisplayName = "Plow",
                Condition01 = 1f, OwnerKind = "business", OwnerId = "farm-1",
            };
            var harness = new EquipmentAsset
            {
                AssetId = "EQ-harness-9", Kind = "harness", DisplayName = "Harness",
                Condition01 = 1f, OwnerKind = "business", OwnerId = "farm-1",
            };
            var diag = new List<string>();

            // Horse A is injured mid-season: a named cause, a real health cost.
            Assert.IsNull(HorseAuthority.RecordInjury(a, "kicked by another horse", 0.75f, 12));
            Assert.IsFalse(HorseTrade.IsDraftCapable(a));

            // The plow unit cannot team the injured horse: farm capacity drops,
            // loudly — no phantom second horse is conjured.
            DraftWorkUnit brokenUnit = DraftWorkUnitService.AssemblePlowUnit(
                f.DraftPower, f.Registry, AsList(a.AnimalId, b.AnimalId), AsList(driver),
                plow, harness, "plowing", diag);
            Assert.IsNull(brokenUnit);
            StringAssert.Contains("no team, no plowing", string.Join(" ", diag.ToArray()));

            // Legitimate alternative: rent the neighbor's sound horse at a real price.
            var borrowerLedger = new HouseholdLedger(21);
            var ownerLedger = new HouseholdLedger(9);
            Assert.IsNull(borrowerLedger.RecordInflow(12, 5000, HouseholdIncomeSource.Remittance, "test", "funding", "test"));
            var rental = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = spare.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-9",
                BorrowerKind = "business",
                BorrowerId = "farm-1",
                BorrowerName = "Farm One",
                FromDayIndex = 13,
                ToDayIndex = 20,
                FeeCents = 1500,
                TermsNote = "plow team replacement",
                Availability = f.Availability,
                DayIndex = 12,
                BorrowerLedger = borrowerLedger,
                OwnerLedger = ownerLedger,
            }, diag);
            Assert.IsNotNull(rental, string.Join(" | ", diag.ToArray()));
            Assert.That(borrowerLedger.GetBalanceCents(), Is.EqualTo(3500));

            // The replacement is not free and not phantom: it is a real horse
            // with a real reservation and a real fee.
            Assert.That(spare.CustodianId, Is.EqualTo("farm-1"));
            Assert.IsFalse(f.Availability.IsAvailable(spare.AnimalId.ToString(), 14 * 86400f));

            // The injured horse still eats while it heals: impairment costs feed.
            var stock = new StubFeedStock(10);
            int unfed = HorseCareDriver.CareDay(f.Registry, stock, h => false, 13, new List<string>());
            Assert.That(unfed, Is.EqualTo(0));
            Assert.That(stock.FeedStockUnits, Is.LessThan(10));
        }

        [Test]
        public void SliceD_InjuredRentedHorse_ReturnRecordsDamage_OwnerToldLoudly()
        {
            var f = new Fixture();
            AnimalState horse = f.AddHorse(AnimalOwnerKind.Household, "H-1");
            var diag = new List<string>();
            var rental = f.Rentals.RentOut(new HorseRentalRequest
            {
                Registry = f.Registry,
                HorseId = horse.AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = "H-1",
                BorrowerKind = "person",
                BorrowerId = "P-8",
                BorrowerName = "Renter Eight",
                FromDayIndex = 1,
                ToDayIndex = 7,
                FeeCents = 0,
                TermsNote = "renter feeds and waters daily; injuries reported at once",
                Availability = f.Availability,
                DayIndex = 0,
            }, diag);
            Assert.IsNotNull(rental, string.Join(" | ", diag.ToArray()));

            // The renter's legitimate use goes through the rental's own hold
            // (same owner task never conflicts with itself).
            float day2 = 2 * 86400f;
            Assert.IsNull(HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, rental.RentalId, f.Availability,
                day2, day2 + 3600f, out _));
            HorseAuthority.ReleaseUse(horse, f.Availability);

            // Mid-rental the horse is injured; the commitment responds: the
            // horse cannot be worked, and return measures the damage.
            Assert.IsNull(HorseAuthority.RecordInjury(horse, "fell on the road", 0.8f, 4));
            Assert.That(horse.Health01, Is.LessThan(0.35f));
            Assert.IsFalse(HorseAuthority.IsWorkReady(horse));
            string hurtRefusal = HorseAuthority.TryReserveUse(
                horse, HorseUseKind.Mounted, rental.RentalId, f.Availability,
                4 * 86400f, 4 * 86400f + 3600f, out _);
            Assert.IsNotNull(hurtRefusal, "even the renter cannot work the injured horse");
            StringAssert.Contains("not fit for work", hurtRefusal);

            Assert.IsNull(f.Rentals.ReturnHorse(f.Registry, horse.AnimalId, 7, f.Availability, diag));
            StringAssert.Contains("open hold", rental.DamageNote);
            StringAssert.Contains("renter feeds and waters daily", rental.DamageNote);
            StringAssert.Contains("fell on the road", horse.HealthNotes);
        }
    }
}
