// Pass IX WS4 (PASS8-009 / A4-15, A4-16, A4-17): semantics-preserving
// performance indexing for PersonScheduleTracker, KnowledgeTransmissionStore,
// DueEventScheduler and ManagerDecisionExecution.AdvanceShipments.
//
// These tests pin the OBSERVABLE behavior of the indexed implementations:
//  - save DTO byte-equivalence against golden serializations captured from
//    the pre-index implementations (same history, same order, same bytes);
//  - exact deterministic pop order for DueEventScheduler, checked against an
//    independent sorted-list reference model over a seeded operation stream;
//  - correctness of the indexed queries at campaign scale;
//  - repeatable before/after benchmarks (timings printed as PERF lines);
//  - A4-16 save-restore scaling measurements for the touched stores.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Integration
{
    [TestFixture]
    public sealed class Pass9PerformanceIndexingTests
    {
        private static void Report(string name, long ms)
        {
            Console.WriteLine("PERF WS4 " + name + ": " + ms + "ms");
        }

        // ------------------------------------------------------------------
        // Fixture builders shared by golden / correctness tests
        // ------------------------------------------------------------------

        private static PersonScheduleTracker BuildScheduleFixture()
        {
            var tracker = new PersonScheduleTracker();
            Assert.IsNull(tracker.TryReserve(7, PersonActivityKind.Work, 100, 480, 120, "shift"));
            Assert.IsNull(tracker.TryReserve(7, PersonActivityKind.Sleeping, 100, 1320, 120, "sleep"));
            Assert.IsNull(tracker.TryReserve(9, PersonActivityKind.Work, 100, 600, 60, "errand"));
            Assert.IsNull(tracker.TryReserve(7, PersonActivityKind.Cooking, 101, 700, 30, "cook"));
            tracker.Release("sched-100-0");
            return tracker;
        }

        private static KnowledgeTransmissionStore BuildKnowledgeFixture()
        {
            var store = new KnowledgeTransmissionStore();
            var diag = new List<string>();
            Assert.IsNull(store.RecordTransmission(new InformationPacket
            {
                PacketId = "pkt-a", Subject = "mine-trouble", ContentSummary = "ore cart broke",
                SpeakerPersonId = 11, RecipientPersonId = 102, ObservedDayIndex = 5,
                TransmittedDayIndex = 6, Confidence01 = 0.9f, SourceKind = "direct-observation",
                TransmissionChain = "11", IsRumor = false,
            }, diag));
            Assert.IsNull(store.RecordTransmission(new InformationPacket
            {
                PacketId = "pkt-b", Subject = "mine-trouble", ContentSummary = "ore cart broke",
                SpeakerPersonId = 13, RecipientPersonId = 103, ObservedDayIndex = 5,
                TransmittedDayIndex = 7, Confidence01 = 0.7f, SourceKind = "conversation",
                TransmissionChain = "11->13", IsRumor = true,
            }, diag));
            Assert.IsNull(store.RecordTransmission(new InformationPacket
            {
                PacketId = "pkt-c", Subject = "mine-trouble", ContentSummary = "ore cart broke",
                SpeakerPersonId = 12, RecipientPersonId = 102, ObservedDayIndex = 5,
                TransmittedDayIndex = 8, Confidence01 = 0.8f, SourceKind = "direct-observation",
                TransmissionChain = "12", IsRumor = false,
            }, diag));
            Assert.IsNull(store.RecordTransmission(new InformationPacket
            {
                PacketId = "pkt-d", Subject = "weather", ContentSummary = "storm coming",
                SpeakerPersonId = 12, RecipientPersonId = 102, ObservedDayIndex = 5,
                TransmittedDayIndex = 9, Confidence01 = 0.5f, SourceKind = "direct-observation",
                TransmissionChain = "12", IsRumor = false,
            }, diag));
            return store;
        }

        private static DueEventScheduler BuildDueEventFixture()
        {
            var scheduler = new DueEventScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 300f, "task-c", out ignored);
            scheduler.Schedule(DueEventKind.TendingDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.PassiveProcessComplete, 200f, "proc-b", out ignored);
            scheduler.Schedule(DueEventKind.TendingDue, 100f, "task-a2", out ignored);
            // Reschedule task-c earlier (replace-by-id) and cancel proc-b.
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 50f, "task-c", out ignored);
            scheduler.Cancel("due-PassiveProcessComplete-proc-b");
            scheduler.ScheduleWithEventId("birthday:7", DueEventKind.Birthday, 100f, "p7");
            return scheduler;
        }

        private static string CanonicalDueEvents(DueEventSchedulerSaveDto dto)
        {
            var parts = new List<string>();
            foreach (DueEvent e in dto.events)
            {
                parts.Add(e.EventId + "|" + e.Kind + "|" + e.DueGameSeconds + "|" + e.PayloadRef + "|" + e.Sequence);
            }
            return string.Join(";", parts) + "#next=" + dto.nextSequence;
        }

        private static ManagerDecisionExecutionLedger BuildLedgerFixture()
        {
            var ledger = new ManagerDecisionExecutionLedger();
            ledger.AddShipment(new ManagerProcurementShipment
            {
                ShipmentId = "ship-1", OrderId = "ord-1", BusinessInstanceId = "buyer-1",
                SupplierBusinessId = "supplier-1", ProductId = "flour", Quantity = 4f,
                UnitCostCents = 80, TotalCents = 320, CreatedDayIndex = 400,
                ExpectedArrivalDayIndex = 401, Status = "Delivered", FailureReason = string.Empty,
            });
            ledger.AddShipment(new ManagerProcurementShipment
            {
                ShipmentId = "ship-2", OrderId = "ord-2", BusinessInstanceId = "buyer-1",
                SupplierBusinessId = "supplier-1", ProductId = "sugar", Quantity = 2f,
                UnitCostCents = 50, TotalCents = 100, CreatedDayIndex = 402,
                ExpectedArrivalDayIndex = 410, Status = "InTransit", FailureReason = string.Empty,
            });
            ledger.AddShipment(new ManagerProcurementShipment
            {
                ShipmentId = "ship-3", OrderId = "ord-3", BusinessInstanceId = "buyer-9",
                SupplierBusinessId = "supplier-2", ProductId = "salt", Quantity = 1f,
                UnitCostCents = 25, TotalCents = 25, CreatedDayIndex = 403,
                ExpectedArrivalDayIndex = 404, Status = "Failed", FailureReason = "wagon lost",
            });
            return ledger;
        }

        // ------------------------------------------------------------------
        // Byte-equivalence goldens (captured from the pre-index code)
        // ------------------------------------------------------------------

        // Goldens captured 2026-10-22 from the pre-index implementations
        // (Roslyn harness run of this fixture on the unmodified stores).
        private const string ScheduleGolden = @"{""reservations"":[{""EndMinuteOfDay"":600,""ReservationId"":""sched-100-0"",""PersonId"":7,""Activity"":1,""DayIndex"":100,""StartMinuteOfDay"":480,""DurationMinutes"":120,""Label"":""shift"",""Released"":true},{""EndMinuteOfDay"":1440,""ReservationId"":""sched-100-1"",""PersonId"":7,""Activity"":6,""DayIndex"":100,""StartMinuteOfDay"":1320,""DurationMinutes"":120,""Label"":""sleep"",""Released"":false},{""EndMinuteOfDay"":660,""ReservationId"":""sched-100-2"",""PersonId"":9,""Activity"":1,""DayIndex"":100,""StartMinuteOfDay"":600,""DurationMinutes"":60,""Label"":""errand"",""Released"":false},{""EndMinuteOfDay"":730,""ReservationId"":""sched-101-3"",""PersonId"":7,""Activity"":4,""DayIndex"":101,""StartMinuteOfDay"":700,""DurationMinutes"":30,""Label"":""cook"",""Released"":false}],""nextReservationSequence"":4}";
        private const string KnowledgeGolden = @"{""Packets"":[{""PacketId"":""pkt-a"",""Subject"":""mine-trouble"",""ContentSummary"":""ore cart broke"",""SpeakerPersonId"":11,""RecipientPersonId"":102,""ObservedDayIndex"":5,""TransmittedDayIndex"":6,""Confidence01"":0.9,""SourceKind"":""direct-observation"",""TransmissionChain"":""11"",""IsRumor"":false},{""PacketId"":""pkt-b"",""Subject"":""mine-trouble"",""ContentSummary"":""ore cart broke"",""SpeakerPersonId"":13,""RecipientPersonId"":103,""ObservedDayIndex"":5,""TransmittedDayIndex"":7,""Confidence01"":0.7,""SourceKind"":""conversation"",""TransmissionChain"":""11-\u003E13"",""IsRumor"":true},{""PacketId"":""pkt-c"",""Subject"":""mine-trouble"",""ContentSummary"":""ore cart broke"",""SpeakerPersonId"":12,""RecipientPersonId"":102,""ObservedDayIndex"":5,""TransmittedDayIndex"":8,""Confidence01"":0.8,""SourceKind"":""direct-observation"",""TransmissionChain"":""12"",""IsRumor"":false},{""PacketId"":""pkt-d"",""Subject"":""weather"",""ContentSummary"":""storm coming"",""SpeakerPersonId"":12,""RecipientPersonId"":102,""ObservedDayIndex"":5,""TransmittedDayIndex"":9,""Confidence01"":0.5,""SourceKind"":""direct-observation"",""TransmissionChain"":""12"",""IsRumor"":false}],""NextPacketSequence"":1}";
        private const string LedgerGolden = @"{""Actions"":[],""PendingRequests"":[],""Shipments"":[{""ShipmentId"":""ship-1"",""OrderId"":""ord-1"",""BusinessInstanceId"":""buyer-1"",""SupplierBusinessId"":""supplier-1"",""ProductId"":""flour"",""Quantity"":4,""UnitCostCents"":80,""TotalCents"":320,""CreatedDayIndex"":400,""ExpectedArrivalDayIndex"":401,""Status"":""Delivered"",""FailureReason"":""""},{""ShipmentId"":""ship-2"",""OrderId"":""ord-2"",""BusinessInstanceId"":""buyer-1"",""SupplierBusinessId"":""supplier-1"",""ProductId"":""sugar"",""Quantity"":2,""UnitCostCents"":50,""TotalCents"":100,""CreatedDayIndex"":402,""ExpectedArrivalDayIndex"":410,""Status"":""InTransit"",""FailureReason"":""""},{""ShipmentId"":""ship-3"",""OrderId"":""ord-3"",""BusinessInstanceId"":""buyer-9"",""SupplierBusinessId"":""supplier-2"",""ProductId"":""salt"",""Quantity"":1,""UnitCostCents"":25,""TotalCents"":25,""CreatedDayIndex"":403,""ExpectedArrivalDayIndex"":404,""Status"":""Failed"",""FailureReason"":""wagon lost""}]}";
        private const string DueEventGolden = "due-TaskCompletionDue-task-c|TaskCompletionDue|50|task-c|5;due-TendingDue-task-a|TendingDue|100|task-a|2;due-TendingDue-task-a2|TendingDue|100|task-a2|4;birthday:7|Birthday|100|p7|6#next=7";

        [Test]
        public void ByteEquivalence_ScheduleDto_MatchesGolden()
        {
            string json = JsonUtility.ToJson(BuildScheduleFixture().CaptureSaveDto());
            Assert.AreEqual(ScheduleGolden, json, "schedule DTO bytes must not change");
            // Round-trip: restore -> capture must reproduce identical bytes.
            var dto = JsonUtility.FromJson<PersonScheduleTrackerSaveDto>(json);
            var restored = new PersonScheduleTracker();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(ScheduleGolden, JsonUtility.ToJson(restored.CaptureSaveDto()));
        }

        [Test]
        public void ByteEquivalence_KnowledgeDto_MatchesGolden()
        {
            string json = JsonUtility.ToJson(BuildKnowledgeFixture().CaptureSaveDto());
            Assert.AreEqual(KnowledgeGolden, json, "knowledge DTO bytes must not change");
            var dto = JsonUtility.FromJson<KnowledgeTransmissionSaveDto>(json);
            var restored = new KnowledgeTransmissionStore();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(KnowledgeGolden, JsonUtility.ToJson(restored.CaptureSaveDto()));
        }

        [Test]
        public void ByteEquivalence_LedgerDto_MatchesGolden()
        {
            string json = JsonUtility.ToJson(BuildLedgerFixture().CaptureSaveDto());
            Assert.AreEqual(LedgerGolden, json, "manager ledger DTO bytes must not change");
            var dto = JsonUtility.FromJson<ManagerDecisionExecutionSaveDto>(json);
            var restored = new ManagerDecisionExecutionLedger();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(LedgerGolden, JsonUtility.ToJson(restored.CaptureSaveDto()));
        }

        [Test]
        public void ByteEquivalence_DueEventDto_MatchesGolden()
        {
            string canonical = CanonicalDueEvents(BuildDueEventFixture().CaptureSaveDto());
            Assert.AreEqual(DueEventGolden, canonical,
                "due-event DTO event order/content must not change (DTO order is pop order)");
            var dto = BuildDueEventFixture().CaptureSaveDto();
            var restored = new DueEventScheduler();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(DueEventGolden, CanonicalDueEvents(restored.CaptureSaveDto()));
        }

        // ------------------------------------------------------------------
        // A4-17 determinism: exact pop order vs an independent reference model
        // ------------------------------------------------------------------

        [Test]
        public void DueEvents_Determinism_MatchesReferenceModel_SeededOpStream()
        {
            var scheduler = new DueEventScheduler();
            // Reference model: dictionary + full sort by (due, sequence) per pop.
            var model = new Dictionary<string, DueEvent>(StringComparer.Ordinal);
            long modelSequence = 1;
            var random = new System.Random(1234);
            var liveIds = new List<string>();
            float clock = 0f;

            for (int step = 0; step < 400; step++)
            {
                int op = random.Next(0, 5);
                if (op <= 1 || liveIds.Count == 0)
                {
                    // Schedule (sometimes a reschedule of a live id).
                    string payload = "payload-" + random.Next(0, 60);
                    float due = random.Next(0, 40) * 250f;
                    var kind = (DueEventKind)(1 + random.Next(0, 9));
                    string id;
                    string refusal = scheduler.Schedule(kind, due, payload, out id);
                    Assert.IsNull(refusal);
                    model[id] = new DueEvent(id, kind, due, payload, modelSequence++);
                    if (!liveIds.Contains(id)) liveIds.Add(id);
                }
                else if (op == 2)
                {
                    string id = liveIds[random.Next(0, liveIds.Count)];
                    scheduler.Cancel(id);
                    model.Remove(id);
                    liveIds.Remove(id);
                }
                else if (op == 3)
                {
                    string payload = "payload-" + random.Next(0, 60);
                    scheduler.CancelForPayload(payload);
                    var doomed = new List<string>();
                    foreach (KeyValuePair<string, DueEvent> entry in model)
                    {
                        if (entry.Value.PayloadRef == payload) doomed.Add(entry.Key);
                    }
                    foreach (string id in doomed) { model.Remove(id); liveIds.Remove(id); }
                }
                else
                {
                    clock += random.Next(0, 8) * 250f;
                    List<DueEvent> actual = scheduler.PopDue(clock);
                    var expected = new List<DueEvent>();
                    foreach (KeyValuePair<string, DueEvent> entry in model)
                    {
                        if (entry.Value.DueGameSeconds <= clock) expected.Add(entry.Value);
                    }
                    expected.Sort((a, b) =>
                    {
                        int byDue = a.DueGameSeconds.CompareTo(b.DueGameSeconds);
                        return byDue != 0 ? byDue : a.Sequence.CompareTo(b.Sequence);
                    });
                    foreach (DueEvent e in expected) { model.Remove(e.EventId); liveIds.Remove(e.EventId); }

                    Assert.AreEqual(expected.Count, actual.Count, "pop count at step " + step);
                    for (int i = 0; i < expected.Count; i++)
                    {
                        Assert.AreEqual(expected[i].EventId, actual[i].EventId,
                            "pop order at step " + step + ", position " + i);
                        Assert.AreEqual(expected[i].Sequence, actual[i].Sequence);
                    }
                    Assert.AreEqual(model.Count, scheduler.ScheduledCount, "scheduled count at step " + step);
                    DueEvent peeked = scheduler.PeekNext();
                    if (model.Count == 0)
                    {
                        Assert.IsNull(peeked);
                    }
                    else
                    {
                        var remaining = new List<DueEvent>(model.Values);
                        remaining.Sort((a, b) =>
                        {
                            int byDue = a.DueGameSeconds.CompareTo(b.DueGameSeconds);
                            return byDue != 0 ? byDue : a.Sequence.CompareTo(b.Sequence);
                        });
                        Assert.AreEqual(remaining[0].EventId, peeked.EventId, "peek at step " + step);
                    }
                }
            }
        }

        [Test]
        public void DueEvents_FixtureCohort_PopsInExactOrder()
        {
            var scheduler = BuildDueEventFixture();
            List<DueEvent> due = scheduler.PopDue(1000f);
            Assert.AreEqual(4, due.Count);
            Assert.AreEqual("due-TaskCompletionDue-task-c", due[0].EventId); // rescheduled to 50
            Assert.AreEqual("due-TendingDue-task-a", due[1].EventId);        // 100, earlier seq
            Assert.AreEqual("due-TendingDue-task-a2", due[2].EventId);       // 100, later seq
            Assert.AreEqual("birthday:7", due[3].EventId);                   // 100, latest seq
            Assert.AreEqual(0, scheduler.ScheduledCount);
        }

        // ------------------------------------------------------------------
        // Indexed-query correctness at scale
        // ------------------------------------------------------------------

        [Test]
        public void Schedule_IndexedQueries_FindConflictsAndWindows_AfterLargeHistory()
        {
            var tracker = new PersonScheduleTracker();
            // Two years of unrelated history for other persons/days.
            for (int day = 0; day < 730; day++)
            {
                for (int p = 100; p < 120; p++)
                {
                    Assert.IsNull(tracker.TryReserve(p, PersonActivityKind.Work, day, 480, 240, "shift"));
                }
            }

            Assert.IsNull(tracker.TryReserve(7, PersonActivityKind.Work, 100, 480, 120, "shift"));
            string refusal = tracker.TryReserve(7, PersonActivityKind.Cooking, 100, 500, 30, "cook");
            Assert.IsNotNull(refusal, "overlap with the indexed bucket must still be refused");
            StringAssert.Contains("already Work", refusal);
            Assert.IsFalse(tracker.IsFree(7, 100, 500, 30));
            Assert.IsTrue(tracker.IsFree(7, 100, 600, 30));
            Assert.AreEqual(1, tracker.ActiveFor(7, 100).Count);
            StringAssert.Contains("is Work", tracker.DescribeActivity(7, 100, 500));

            // Released reservations stop conflicting, exactly as before.
            tracker.Release("sched-100-" + (730 * 20));
            Assert.IsTrue(tracker.IsFree(7, 100, 500, 30));
            Assert.AreEqual(14601, tracker.Reservations.Count, "history is preserved in full");
        }

        [Test]
        public void Knowledge_IndexedStore_PreservesDuplicateAndSourceSemantics()
        {
            KnowledgeTransmissionStore store = BuildKnowledgeFixture();
            var diag = new List<string>();

            // Retelling of pkt-a through the same chain root (11) is refused
            // even though the speaker differs (11->13 chain): no new source.
            string refusal = store.RecordTransmission(new InformationPacket
            {
                PacketId = "pkt-e", Subject = "Mine-Trouble", ContentSummary = "ore cart broke",
                SpeakerPersonId = 13, RecipientPersonId = 102, ObservedDayIndex = 5,
                TransmittedDayIndex = 10, Confidence01 = 0.6f, SourceKind = "conversation",
                TransmissionChain = "11->13", IsRumor = true,
            }, diag);
            Assert.IsNotNull(refusal, "duplicate telling must be refused (case-insensitive subject)");
            StringAssert.Contains("already holds this telling", refusal);

            // Duplicate packet id refused globally.
            Assert.IsNotNull(store.RecordTransmission(new InformationPacket
            {
                PacketId = "pkt-a", Subject = "other", ContentSummary = "x",
                SpeakerPersonId = 1, RecipientPersonId = 5, TransmissionChain = "1",
            }, diag));

            Assert.AreEqual(2, store.IndependentSourceCount(102, "mine-trouble"),
                "roots 11 and 12 are independent; the 11->13 retelling is not");
            Assert.AreEqual(2, store.PacketsFor(102, "mine-trouble").Count);
            Assert.IsTrue(store.HasPacket(103, "pkt-b"));
            Assert.IsFalse(store.HasPacket(102, "pkt-b"));

            // Semantics survive a save round trip (indexes rebuilt on load).
            var restored = new KnowledgeTransmissionStore();
            restored.LoadFromSaveDto(store.CaptureSaveDto());
            Assert.AreEqual(2, restored.IndependentSourceCount(102, "mine-trouble"));
            Assert.IsNotNull(restored.RecordTransmission(new InformationPacket
            {
                PacketId = "pkt-f", Subject = "mine-trouble", ContentSummary = "ore cart broke",
                SpeakerPersonId = 12, RecipientPersonId = 102, ObservedDayIndex = 5,
                TransmittedDayIndex = 11, Confidence01 = 0.5f, SourceKind = "conversation",
                TransmissionChain = "12", IsRumor = true,
            }, diag), "duplicate telling still refused after restore");
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "field " + name);
            field.SetValue(target, value);
        }

        private static BusinessInstanceState MakeBusiness(string id)
        {
            var business = new BusinessInstanceState();
            SetField(business, "instanceId", id);
            Assert.IsTrue(business.TryAssignPremises(42), "premises assigned");
            var runtime = new BusinessRuntimeState();
            runtime.AddCashCents(10000);
            SetField(business, "runtimeState", runtime);
            return business;
        }

        [Test]
        public void AdvanceShipments_WithLargeDeliveredHistory_ProcessesOnlyActive()
        {
            var businesses = new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
            BusinessInstanceState buyer = MakeBusiness("buyer-1");
            businesses["buyer-1"] = buyer;

            var service = new ManagerDecisionExecutionService();
            service.Configure(new ManagerExecutionServices
            {
                FindBusiness = id =>
                {
                    BusinessInstanceState found;
                    return businesses.TryGetValue(id, out found) ? found : null;
                },
            });

            // Deep delivered history (must remain recorded, never rescanned
            // into deliveries) plus a few active shipments.
            var dto = new ManagerDecisionExecutionSaveDto();
            for (int i = 0; i < 5000; i++)
            {
                dto.Shipments.Add(new ManagerProcurementShipment
                {
                    ShipmentId = "old-" + i, BusinessInstanceId = "buyer-1", ProductId = "flour",
                    Quantity = 1f, UnitCostCents = 10, CreatedDayIndex = 1,
                    ExpectedArrivalDayIndex = 2, Status = "Delivered",
                });
            }
            dto.Shipments.Add(new ManagerProcurementShipment
            {
                ShipmentId = "due-1", BusinessInstanceId = "buyer-1", ProductId = "flour",
                Quantity = 3f, UnitCostCents = 10, CreatedDayIndex = 100,
                ExpectedArrivalDayIndex = 101, Status = "InTransit",
            });
            dto.Shipments.Add(new ManagerProcurementShipment
            {
                ShipmentId = "gone-1", BusinessInstanceId = "missing-biz", ProductId = "flour",
                Quantity = 1f, UnitCostCents = 10, CreatedDayIndex = 100,
                ExpectedArrivalDayIndex = 101, Status = "InTransit",
            });
            dto.Shipments.Add(new ManagerProcurementShipment
            {
                ShipmentId = "later-1", BusinessInstanceId = "buyer-1", ProductId = "sugar",
                Quantity = 1f, UnitCostCents = 10, CreatedDayIndex = 100,
                ExpectedArrivalDayIndex = 500, Status = "InTransit",
            });
            service.LoadFromSaveDto(dto);

            var diag = new List<string>();
            service.AdvanceShipments(101, diag);

            IReadOnlyList<ManagerProcurementShipment> shipments = service.Ledger.Shipments;
            Assert.AreEqual(5003, shipments.Count, "history preserved in full");
            Assert.AreEqual("Delivered", shipments[5000].Status);
            Assert.AreEqual("Failed", shipments[5001].Status);
            Assert.AreEqual("destination business no longer exists", shipments[5001].FailureReason);
            Assert.AreEqual("InTransit", shipments[5002].Status, "not-yet-due shipment untouched");
            Assert.AreEqual(3f, buyer.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f);
            Assert.IsFalse(service.Ledger.HasInTransitShipment("buyer-1", "flour"));
            Assert.IsTrue(service.Ledger.HasInTransitShipment("buyer-1", "sugar"));

            // Advancing again never double-receives.
            service.AdvanceShipments(102, diag);
            Assert.AreEqual(3f, buyer.GenericConfiguration.GetAvailableInventoryQuantity("flour"), 0.001f);
        }

        // ------------------------------------------------------------------
        // Benchmarks (before/after numbers are printed as PERF WS4 lines)
        // ------------------------------------------------------------------

        [Test]
        public void Bench_Schedule_CampaignScale()
        {
            var tracker = new PersonScheduleTracker();
            var sw = Stopwatch.StartNew();
            int reserved = 0;
            for (int day = 0; day < 730; day++)
            {
                for (int p = 1; p <= 20; p++)
                {
                    if (tracker.TryReserve(p, PersonActivityKind.Work, day, 480, 240, "shift") == null) reserved++;
                    if (tracker.TryReserve(p, PersonActivityKind.Sleeping, day, 1320, 120, "sleep") == null) reserved++;
                }
            }
            sw.Stop();
            Report("Schedule build 2y x 20 persons (" + reserved + " reservations)", sw.ElapsedMilliseconds);
            Assert.AreEqual(29200, reserved);

            var random = new System.Random(99);
            sw.Restart();
            int hits = 0;
            for (int q = 0; q < 30000; q++)
            {
                int p = 1 + random.Next(0, 20);
                int day = random.Next(0, 730);
                hits += tracker.ActiveFor(p, day).Count;
                if (!tracker.IsFree(p, day, 500, 30)) hits++;
                tracker.DescribeActivity(p, day, 500);
            }
            sw.Stop();
            Report("Schedule 30000 mixed queries over 29200 reservations", sw.ElapsedMilliseconds);
            Assert.Greater(hits, 0);
        }

        [Test]
        public void Bench_Knowledge_InsertsAndQueries()
        {
            var store = new KnowledgeTransmissionStore();
            var diag = new List<string>();
            var sw = Stopwatch.StartNew();
            int recorded = 0;
            for (int i = 0; i < 12000; i++)
            {
                string refusal = store.RecordTransmission(new InformationPacket
                {
                    Subject = "subject-" + (i % 40),
                    ContentSummary = "content-" + i,
                    SpeakerPersonId = 1000 + (i % 50),
                    RecipientPersonId = i % 300,
                    ObservedDayIndex = 1,
                    TransmittedDayIndex = i % 700,
                    Confidence01 = 0.5f,
                    SourceKind = "conversation",
                    TransmissionChain = (1000 + (i % 50)).ToString(),
                    IsRumor = true,
                }, diag);
                if (refusal == null) recorded++;
            }
            sw.Stop();
            Report("Knowledge 12000 inserts", sw.ElapsedMilliseconds);
            Assert.AreEqual(12000, recorded);

            sw.Restart();
            int total = 0;
            for (int q = 0; q < 3000; q++)
            {
                total += store.PacketsFor(q % 300, "subject-" + (q % 40)).Count;
                total += store.IndependentSourceCount(q % 300, "subject-" + (q % 40));
            }
            sw.Stop();
            Report("Knowledge 3000 PacketsFor+IndependentSourceCount queries over 12000 packets", sw.ElapsedMilliseconds);
            Assert.Greater(total, 0);
        }

        [Test]
        public void Bench_DueEvents_CohortDrain()
        {
            var scheduler = new DueEventScheduler();
            var sw = Stopwatch.StartNew();
            string ignored;
            for (int i = 0; i < 40000; i++)
            {
                scheduler.Schedule((DueEventKind)(1 + (i % 9)), (i % 500) * 100f, "payload-" + i, out ignored);
            }
            long scheduleMs = sw.ElapsedMilliseconds;
            for (int i = 0; i < 2000; i += 2)
            {
                scheduler.Schedule((DueEventKind)(1 + (i % 9)), 999999f, "payload-" + i, out ignored); // reschedule
                scheduler.Cancel("due-" + (DueEventKind)(1 + ((i + 1) % 9)) + "-payload-" + (i + 1));
            }
            sw.Stop();
            Report("DueEvents schedule 40000 + 2000 reschedule/cancel", sw.ElapsedMilliseconds);

            sw.Restart();
            List<DueEvent> due = scheduler.PopDue(1000000f);
            sw.Stop();
            Report("DueEvents drain " + due.Count + " events (one PopDue)", sw.ElapsedMilliseconds);
            Assert.AreEqual(39000, due.Count);
            Assert.AreEqual(0, scheduler.ScheduledCount);
            for (int i = 1; i < due.Count; i++)
            {
                Assert.IsTrue(
                    due[i - 1].DueGameSeconds < due[i].DueGameSeconds
                    || (due[i - 1].DueGameSeconds == due[i].DueGameSeconds && due[i - 1].Sequence < due[i].Sequence),
                    "drain order is (due, sequence) at " + i);
            }
        }

        [Test]
        public void Bench_AdvanceShipments_DailyScan()
        {
            var service = new ManagerDecisionExecutionService();
            service.Configure(new ManagerExecutionServices
            {
                FindBusiness = id => null,
            });
            var dto = new ManagerDecisionExecutionSaveDto();
            for (int i = 0; i < 120000; i++)
            {
                dto.Shipments.Add(new ManagerProcurementShipment
                {
                    ShipmentId = "old-" + i, BusinessInstanceId = "buyer-1", ProductId = "flour",
                    Quantity = 1f, UnitCostCents = 10, CreatedDayIndex = 1,
                    ExpectedArrivalDayIndex = 2, Status = i % 10 == 0 ? "Failed" : "Delivered",
                });
            }
            for (int i = 0; i < 25; i++)
            {
                dto.Shipments.Add(new ManagerProcurementShipment
                {
                    ShipmentId = "active-" + i, BusinessInstanceId = "buyer-1", ProductId = "flour",
                    Quantity = 1f, UnitCostCents = 10, CreatedDayIndex = 100,
                    ExpectedArrivalDayIndex = 100000, Status = "InTransit",
                });
            }
            service.LoadFromSaveDto(dto);

            var diag = new List<string>();
            var sw = Stopwatch.StartNew();
            for (int day = 200; day < 200 + 730; day++)
            {
                service.AdvanceShipments(day, diag);
            }
            sw.Stop();
            Report("AdvanceShipments 730 daily calls over 120025-shipment ledger (25 active)", sw.ElapsedMilliseconds);
            Assert.AreEqual(120025, service.Ledger.Shipments.Count);
        }

        // ------------------------------------------------------------------
        // A4-16: save-restore scaling profile for the touched stores.
        // Prints load+capture ms at two scales; a quadratic restore shows a
        // ~16x ratio for 4x data, a linear one ~4x.
        // ------------------------------------------------------------------

        [Test]
        public void Profile_SaveRestore_Scaling()
        {
            // Person schedule restore.
            foreach (int n in new[] { 25000, 100000 })
            {
                var dto = new PersonScheduleTrackerSaveDto { nextReservationSequence = n };
                for (int i = 0; i < n; i++)
                {
                    dto.reservations.Add(new PersonActivityReservation
                    {
                        ReservationId = "sched-" + i, PersonId = i % 200, DayIndex = i / 200,
                        Activity = PersonActivityKind.Work, StartMinuteOfDay = 480,
                        DurationMinutes = 60, Label = "x",
                    });
                }
                var tracker = new PersonScheduleTracker();
                var sw = Stopwatch.StartNew();
                tracker.LoadFromSaveDto(dto);
                PersonScheduleTrackerSaveDto back = tracker.CaptureSaveDto();
                sw.Stop();
                Report("A4-16 schedule restore+capture n=" + n, sw.ElapsedMilliseconds);
                Assert.AreEqual(n, back.reservations.Count);
            }

            // Knowledge restore.
            foreach (int n in new[] { 10000, 40000 })
            {
                var dto = new KnowledgeTransmissionSaveDto { NextPacketSequence = n + 1 };
                for (int i = 0; i < n; i++)
                {
                    dto.Packets.Add(new InformationPacket
                    {
                        PacketId = "pkt-" + i, Subject = "s" + (i % 40), ContentSummary = "c" + i,
                        SpeakerPersonId = i % 50, RecipientPersonId = i % 300,
                        TransmissionChain = (i % 50).ToString(), TransmittedDayIndex = i % 700,
                    });
                }
                var store = new KnowledgeTransmissionStore();
                var sw = Stopwatch.StartNew();
                store.LoadFromSaveDto(dto);
                KnowledgeTransmissionSaveDto back = store.CaptureSaveDto();
                sw.Stop();
                Report("A4-16 knowledge restore+capture n=" + n, sw.ElapsedMilliseconds);
                Assert.AreEqual(n, back.Packets.Count);
            }

            // Due-event restore.
            foreach (int n in new[] { 10000, 40000 })
            {
                var dto = new DueEventSchedulerSaveDto { nextSequence = n + 1 };
                var random = new System.Random(5);
                for (int i = 0; i < n; i++)
                {
                    dto.events.Add(new DueEvent("due-x-" + i, DueEventKind.TaskCompletionDue,
                        random.Next(0, 100000), "p" + i, i + 1));
                }
                var scheduler = new DueEventScheduler();
                var sw = Stopwatch.StartNew();
                scheduler.LoadFromSaveDto(dto);
                DueEventSchedulerSaveDto back = scheduler.CaptureSaveDto();
                sw.Stop();
                Report("A4-16 due-event restore+capture n=" + n, sw.ElapsedMilliseconds);
                Assert.AreEqual(n, back.events.Count);
            }

            // Manager ledger restore.
            foreach (int n in new[] { 25000, 100000 })
            {
                var dto = new ManagerDecisionExecutionSaveDto();
                for (int i = 0; i < n; i++)
                {
                    dto.Shipments.Add(new ManagerProcurementShipment
                    {
                        ShipmentId = "ship-" + i, BusinessInstanceId = "b1", ProductId = "flour",
                        Quantity = 1f, Status = i % 20 == 0 ? "InTransit" : "Delivered",
                        ExpectedArrivalDayIndex = 99999,
                    });
                }
                var ledger = new ManagerDecisionExecutionLedger();
                var sw = Stopwatch.StartNew();
                ledger.LoadFromSaveDto(dto);
                ManagerDecisionExecutionSaveDto back = ledger.CaptureSaveDto();
                sw.Stop();
                Report("A4-16 manager-ledger restore+capture n=" + n, sw.ElapsedMilliseconds);
                Assert.AreEqual(n, back.Shipments.Count);
            }
        }
    }
}
