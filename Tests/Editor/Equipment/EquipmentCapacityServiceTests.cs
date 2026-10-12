using NUnit.Framework;

namespace LandLedgers.Economy.Equipment.Tests
{
    /// <summary>
    /// Pass IV Worker 2: equipment capacity service — all six capacity models,
    /// condition-driven usable fractions, and ownership-never-unlimited.
    /// </summary>
    [TestFixture]
    public sealed class EquipmentCapacityServiceTests
    {
        private EquipmentReservationService authority;
        private EquipmentCapacityService capacities;

        [SetUp]
        public void SetUp()
        {
            authority = new EquipmentReservationService();
            capacities = new EquipmentCapacityService(authority);
            EquipmentConditionProfiles.RegisterAll(capacities);
            capacities.KindLookup = key =>
            {
                if (key.StartsWith("asset:"))
                {
                    string id = key.Substring("asset:".Length);
                    if (id.StartsWith("plow")) return "plow";
                    if (id.StartsWith("wagon")) return "farm-freight-wagon";
                    if (id.StartsWith("oven")) return "oven";
                    if (id.StartsWith("shelf")) return "shelf";
                }
                return null;
            };
        }

        // ---------- UsableCapacityFraction ----------

        [Test]
        public void UsableCapacityFraction_FollowsProfileThresholds()
        {
            var cases = new (string kind, float condition, float expected)[]
            {
                ("plow", 1f, 1f),
                ("plow", 0.5f, 1f),
                ("plow", 0.34f, 0.6f),   // degraded below 0.35 -> 0.6
                ("plow", 0.04f, 0f),     // unusable below 0.05
                ("oven", 0.39f, 0.75f),  // degraded below 0.4 -> 0.75
                ("oven", 0.04f, 0f),
                ("shelf", 0.9f, 1f),     // shelf never degrades
                ("shelf", 0.04f, 0f),    // but unusable still applies
            };
            foreach (var c in cases)
            {
                var profile = EquipmentConditionProfiles.GetProfile(c.kind);
                Assert.IsNotNull(profile, "Profile must be authored for " + c.kind);
                capacities.RegisterCapacity("asset:frac-1", null, profile);
                Assert.AreEqual(c.expected,
                    capacities.UsableCapacityFraction("asset:frac-1", c.condition), 1e-4f,
                    $"kind={c.kind} condition={c.condition}");
            }
        }

        [Test]
        public void UsableCapacityFraction_UnknownKeyFallsBackToAssetTruth()
        {
            // No profile: mirrors EquipmentAsset.IsUsable (condition > 0.05).
            Assert.AreEqual(1f, capacities.UsableCapacityFraction("asset:unknown-1", 0.9f), 1e-4f);
            Assert.AreEqual(0f, capacities.UsableCapacityFraction("asset:unknown-1", 0.05f), 1e-4f);
        }

        // ---------- per-model CanAccept ----------

        [Test]
        public void Exclusive_OneUnitOnly()
        {
            capacities.RegisterCapacity("asset:plow-1",
                EquipmentConditionProfiles.GetDescriptor("plow"),
                EquipmentConditionProfiles.GetProfile("plow"));
            Assert.IsTrue(capacities.CanAccept("asset:plow-1", 0.9f, 1f, 0f));

            var request = new ReservationRequest("task-a", "Plowing");
            request.Items.Add(new ReservationItem("asset:plow-1", 1f, 0f, 100f));
            Assert.IsNull(authority.TryReserveAll(request, out _));

            Assert.IsFalse(capacities.CanAccept("asset:plow-1", 0.9f, 1f, 50f),
                "Exclusive equipment held by a reservation cannot accept another unit.");
            Assert.IsTrue(capacities.CanAccept("asset:plow-1", 0.9f, 1f, 150f),
                "Outside the reservation window the unit is free again.");
        }

        [Test]
        public void Exclusive_UnusableConditionRefuses()
        {
            capacities.RegisterCapacity("asset:plow-1",
                EquipmentConditionProfiles.GetDescriptor("plow"),
                EquipmentConditionProfiles.GetProfile("plow"));
            Assert.IsFalse(capacities.CanAccept("asset:plow-1", 0.04f, 1f, 0f),
                "A wrecked plow accepts nothing.");
        }

        [Test]
        public void Container_EnvelopeInItemUnits()
        {
            capacities.RegisterCapacity("asset:shelf-1",
                EquipmentConditionProfiles.GetDescriptor("shelf"),
                EquipmentConditionProfiles.GetProfile("shelf"));
            Assert.IsTrue(capacities.CanAccept("asset:shelf-1", 1f, 40f, 0f));
            Assert.IsFalse(capacities.CanAccept("asset:shelf-1", 1f, 41f, 0f),
                "Container capacity is the envelope (40 items), not the owner count.");

            var request = new ReservationRequest("task-a", "Storing");
            request.Items.Add(new ReservationItem("asset:shelf-1", 30f, 0f, 100f));
            Assert.IsNull(authority.TryReserveAll(request, out _));
            Assert.IsTrue(capacities.CanAccept("asset:shelf-1", 1f, 10f, 50f));
            Assert.IsFalse(capacities.CanAccept("asset:shelf-1", 1f, 11f, 50f));
        }

        [Test]
        public void Workstation_StationCount()
        {
            var descriptor = new EquipmentCapacityDescriptor(
                EquipmentCapacityModel.Workstation, 2, CapacityEnvelope.Unbounded(), 0f, string.Empty);
            capacities.RegisterCapacity("workstation:smithy-1", descriptor, null);
            Assert.IsTrue(capacities.CanAccept("workstation:smithy-1", 1f, 2f, 0f));
            Assert.IsFalse(capacities.CanAccept("workstation:smithy-1", 1f, 3f, 0f),
                "Two stations accept two units, not three.");

            var request = new ReservationRequest("task-a", "Forging");
            request.Items.Add(new ReservationItem("workstation:smithy-1", 1f, 0f, 100f));
            Assert.IsNull(authority.TryReserveAll(request, out _));
            Assert.IsTrue(capacities.CanAccept("workstation:smithy-1", 1f, 1f, 50f));
            Assert.IsFalse(capacities.CanAccept("workstation:smithy-1", 1f, 2f, 50f));
        }

        [Test]
        public void Throughput_RateLimited()
        {
            capacities.RegisterCapacity("asset:mill-1",
                EquipmentConditionProfiles.GetDescriptor("sawmill"),
                EquipmentConditionProfiles.GetProfile("sawmill"));
            // 12 units/hour at condition 1.0.
            Assert.IsTrue(capacities.CanAccept("asset:mill-1", 1f, 12f, 0f));
            Assert.IsFalse(capacities.CanAccept("asset:mill-1", 1f, 13f, 0f),
                "Throughput is rate-limited: 12 units/hour at full condition.");

            // Degraded condition scales the rate: 0.4 < 0.45 -> 0.7 fraction -> 8.4.
            Assert.IsTrue(capacities.CanAccept("asset:mill-1", 0.4f, 8f, 0f));
            Assert.IsFalse(capacities.CanAccept("asset:mill-1", 0.4f, 9f, 0f),
                "Degraded condition reduces the effective rate.");
        }

        [Test]
        public void PassiveProcess_BatchSlots()
        {
            capacities.RegisterCapacity("asset:oven-1",
                EquipmentConditionProfiles.GetDescriptor("oven"),
                EquipmentConditionProfiles.GetProfile("oven"));
            Assert.IsTrue(capacities.CanAccept("asset:oven-1", 1f, 4f, 0f), "Oven takes 4 batches.");
            Assert.IsFalse(capacities.CanAccept("asset:oven-1", 1f, 5f, 0f));

            var request = new ReservationRequest("task-a", "Baking");
            request.Items.Add(new ReservationItem("asset:oven-1", 4f, 0f, 3600f));
            Assert.IsNull(authority.TryReserveAll(request, out _));
            Assert.IsFalse(capacities.CanAccept("asset:oven-1", 1f, 1f, 1800f),
                "A full oven accepts no further batch while baking.");
            Assert.IsTrue(capacities.CanAccept("asset:oven-1", 1f, 1f, 7200f),
                "After the batch window the oven is free.");
        }

        [Test]
        public void Transport_RequiresMotivePowerAndEnvelope()
        {
            capacities.MotivePowerAvailable = id => id == "horse-team";
            capacities.RegisterCapacity("asset:wagon-1",
                EquipmentConditionProfiles.GetDescriptor("farm-freight-wagon"),
                EquipmentConditionProfiles.GetProfile("farm-freight-wagon"));
            Assert.IsTrue(capacities.CanAccept("asset:wagon-1", 1f, 600f, 0f));
            Assert.IsFalse(capacities.CanAccept("asset:wagon-1", 1f, 601f, 0f),
                "Cargo envelope is enforced: 600 kg max.");

            capacities.MotivePowerAvailable = id => false;
            Assert.IsFalse(capacities.CanAccept("asset:wagon-1", 1f, 100f, 0f),
                "Without motive power the wagon accepts nothing.");

            // No lookup at all: never assume motive power.
            capacities.MotivePowerAvailable = null;
            Assert.IsFalse(capacities.CanAccept("asset:wagon-1", 1f, 100f, 0f));
        }

        [Test]
        public void Ownership_NeverImpliesUnlimitedCapacity()
        {
            // Unregistered key: exclusive default — one unit, not infinity.
            Assert.IsTrue(capacities.CanAccept("asset:mystery-1", 1f, 1f, 0f));
            Assert.IsFalse(capacities.CanAccept("asset:mystery-1", 1f, 2f, 0f));
        }

        [Test]
        public void KindFallback_ResolvesInstanceKeys()
        {
            // Only kind-level registration; instance key resolves via KindLookup.
            Assert.IsTrue(capacities.CanAccept("asset:plow-7", 0.9f, 1f, 0f));
            Assert.IsFalse(capacities.CanAccept("asset:plow-7", 0.04f, 1f, 0f),
                "Kind profile applies to instance keys.");
        }

        [Test]
        public void Profiles_AuthoredPerKind_NoUniversalRate()
        {
            var plow = EquipmentConditionProfiles.GetProfile("plow");
            var shelf = EquipmentConditionProfiles.GetProfile("shelf");
            var wagon = EquipmentConditionProfiles.GetProfile("farm-freight-wagon");
            var oven = EquipmentConditionProfiles.GetProfile("oven");
            Assert.IsNotNull(plow); Assert.IsNotNull(shelf);
            Assert.IsNotNull(wagon); Assert.IsNotNull(oven);
            Assert.IsTrue(plow.WearPerActiveHour01 > 0f, "Plow wears with use.");
            Assert.AreEqual(0f, shelf.WearPerActiveHour01, 1e-6f, "Shelf ~zero wear.");
            Assert.IsTrue(wagon.WearPerMile01 > 0f, "Wagon wears per mile.");
            Assert.AreEqual(0f, wagon.WearPerActiveHour01, 1e-6f, "Wagon does not wear per clock hour.");
            Assert.IsTrue(oven.WearPerPassiveHour01 > 0f, "Oven wears per passive hour.");
            Assert.IsNull(EquipmentConditionProfiles.GetProfile("nonsense-kind"),
                "Unknown kinds get no profile — wear is never invented.");

            // CropChain calibration preserved through the profile: 2h session × 0.02 = 0.04.
            Assert.AreEqual(0.04f,
                EquipmentConditionProfiles.WearForActiveHours("plow",
                    EquipmentConditionProfiles.DefaultImplementSessionHours), 1e-6f);
        }
    }
}
