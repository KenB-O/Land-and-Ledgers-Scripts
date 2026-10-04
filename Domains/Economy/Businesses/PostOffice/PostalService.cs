using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Postal
{
    /// <summary>NX-2A: what the post office carries.</summary>
    public enum MailKind
    {
        Unspecified = 0,
        Letter = 1,
        Postcard = 2,
        Parcel = 3,
    }

    /// <summary>
    /// NX-2A: a mail item's lifecycle. Nothing teleports: an item is only
    /// "there" when it physically arrived there.
    /// </summary>
    public enum MailStatus
    {
        Unspecified = 0,
        Posted = 1,        // accepted at an office, awaiting dispatch
        InTransit = 2,     // in a dispatched pouch on a real route
        Arrived = 3,       // at the destination office, awaiting in-person collection
        Collected = 4,     // picked up by the recipient (Canon §7.2: rural collection is in person)
        ForwardedOffMap = 5, // left the simulated world honestly — no fake return
    }

    /// <summary>NX-2A: one physical mail item with a real chain of custody.</summary>
    [Serializable]
    public sealed class MailItem
    {
        public string MailId = string.Empty;
        public MailKind Kind;
        public string SenderName = string.Empty;
        public int RecipientPersonId = -1; // -1 = named off-map recipient
        public string RecipientName = string.Empty;
        public string FromOfficeId = string.Empty;
        public string ToOfficeId = string.Empty; // "offmap" = leaves the simulated world
        public int PostedDayIndex;
        public int PostageCents;
        public MailStatus Status;
        public string CurrentOfficeId = string.Empty;
        public int DueArrivalDayIndex = -1;
        public List<string> History = new List<string>();

        public MailItem() { }
    }

    /// <summary>NX-2A: one post office — a real place with a real schedule.</summary>
    [Serializable]
    public sealed class PostalOffice
    {
        public string OfficeId = string.Empty;
        public string LocationId = string.Empty; // JRN-1 journey location
        public string BusinessInstanceId = string.Empty;
        public List<int> DepartureWeekdays = new List<int>(); // 0=Monday..6=Sunday (calibration convention)
        public int Seed;

        public PostalOffice() { }
    }

    /// <summary>
    /// NX-2A: a star-route carrier contract. Canon §16.4: the post office can be
    /// a paying transportation customer — mail service uses real schedules,
    /// handoff, reliability and contract terms. It is not free subsidy income.
    /// Historical: star-route contractors carried mail where rail did not, paid
    /// per-trip by the Post Office Department.
    /// </summary>
    [Serializable]
    public sealed class PostalContract
    {
        public string ContractId = string.Empty;
        public string OfficeId = string.Empty;
        public string RouteDescription = string.Empty;
        public string CarrierBusinessInstanceId = string.Empty;
        public int PayPerTripCents;
        public int StartDayIndex;
        public bool Active = true;
        public int TripsCompleted;
        public int TotalPaidCents;

        public PostalContract() { }
    }

    /// <summary>NX-2A: save data for the postal network authority.</summary>
    [Serializable]
    public sealed class PostalServiceSaveDto
    {
        public List<PostalOffice> offices = new List<PostalOffice>();
        public List<MailItem> mailItems = new List<MailItem>();
        public List<PostalContract> contracts = new List<PostalContract>();
        public int uncollectedPostageCents;
        public int sequence;
    }

    /// <summary>
    /// NX-2A: the postal network authority. Canon Part VII §7.1-7.2: postal
    /// service is a real information network — letters travel physically through
    /// the journey model on real schedules with real handoff between offices.
    /// Canon §16.4: the post office is a paying transportation customer.
    ///
    /// Historical basis (researched per standing instruction; rates are
    /// calibration per Canon Part XV): 1870s US letter rate 3c per half-ounce
    /// (cut to 2c in October 1883); postal cards 1c from 1873; star-route
    /// contractors carried mail where rail did not, paid per trip by the Post
    /// Office Department; Rural Free Delivery not until 1896 (1899 in SD per
    /// canon §7.2), so in-period collection is in person at the office.
    /// </summary>
    public sealed class PostalService
    {
        public const int LetterPostageCents = 3;
        public const int PostcardPostageCents = 1;
        // Parcel post proper lies beyond the horizon (canon §7.3); parcels here
        // are small packets carried by arrangement — calibration.
        public const int ParcelPostageCents = 25;
        // Seeded per-dispatch on-time probability (calibration). Late mail is
        // delayed, never lost — there is no claims system to resolve losses.
        public const double OnTimeProbability = 0.90;
        public const string OffMapOfficeId = "offmap";

        public const string SortMailTaskId = "postal.sort-mail";
        public const string DispatchPouchTaskId = "postal.dispatch-pouch";
        public const string DeliverLocalMailTaskId = "postal.deliver-local-mail";

        private readonly Dictionary<string, PostalOffice> offices =
            new Dictionary<string, PostalOffice>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MailItem> mail =
            new Dictionary<string, MailItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, PostalContract> contracts =
            new Dictionary<string, PostalContract>(StringComparer.Ordinal);
        private int sequence;
        private int uncollectedPostageCents;

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        public IReadOnlyCollection<PostalOffice> Offices => offices.Values;
        public IReadOnlyCollection<MailItem> MailItems => mail.Values;
        public IReadOnlyCollection<PostalContract> Contracts => contracts.Values;
        public int UncollectedPostageCents => Math.Max(0, uncollectedPostageCents);

        /// <summary>Registers a post office with its departure schedule.</summary>
        public string RegisterOffice(
            string officeId, string locationId, string businessInstanceId,
            List<int> departureWeekdays, int seed, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(officeId) || string.IsNullOrWhiteSpace(locationId))
                return "PostalService.RegisterOffice: office id and journey location id are required.";
            if (offices.ContainsKey(officeId))
                return $"PostalService.RegisterOffice: office '{officeId}' is already registered — no duplicate offices.";
            offices[officeId] = new PostalOffice
            {
                OfficeId = officeId,
                LocationId = locationId,
                BusinessInstanceId = businessInstanceId ?? string.Empty,
                DepartureWeekdays = departureWeekdays ?? new List<int> { 1, 4 }, // Tue/Fri default (calibration)
                Seed = seed,
            };
            diag.Add($"PostalService: office '{officeId}' registered at '{locationId}'.");
            return null;
        }

        /// <summary>
        /// Accepts a mail item and charges postage. Postage accumulates as
        /// uncollected revenue — the caller posts it to the business ledger with
        /// provenance (CollectPostageRevenue). No free carriage.
        /// </summary>
        public MailItem PostItem(
            MailKind kind, string senderName, int recipientPersonId, string recipientName,
            string fromOfficeId, string toOfficeId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (kind == MailKind.Unspecified)
            {
                diag.Add("PostalService.PostItem: mail kind is required.");
                return null;
            }
            if (!offices.TryGetValue(fromOfficeId, out _))
            {
                diag.Add($"PostalService.PostItem: unknown origin office '{fromOfficeId}' — mail is not accepted nowhere.");
                return null;
            }
            bool offMap = string.Equals(toOfficeId, OffMapOfficeId, StringComparison.OrdinalIgnoreCase);
            if (!offMap && !offices.TryGetValue(toOfficeId, out _))
            {
                diag.Add($"PostalService.PostItem: unknown destination office '{toOfficeId}'.");
                return null;
            }

            int postage = kind switch
            {
                MailKind.Letter => LetterPostageCents,
                MailKind.Postcard => PostcardPostageCents,
                MailKind.Parcel => ParcelPostageCents,
                _ => LetterPostageCents,
            };
            var item = new MailItem
            {
                MailId = $"mail-{dayIndex}-{sequence++}",
                Kind = kind,
                SenderName = senderName ?? string.Empty,
                RecipientPersonId = recipientPersonId,
                RecipientName = recipientName ?? string.Empty,
                FromOfficeId = fromOfficeId,
                ToOfficeId = toOfficeId,
                PostedDayIndex = dayIndex,
                PostageCents = postage,
                Status = MailStatus.Posted,
                CurrentOfficeId = fromOfficeId,
            };
            item.History.Add($"day {dayIndex}: posted at {fromOfficeId} ({postage}c postage).");
            mail[item.MailId] = item;
            uncollectedPostageCents += postage;
            diag.Add($"PostalService: {kind} {item.MailId} posted {fromOfficeId} → {toOfficeId} ({postage}c).");
            return item;
        }

        /// <summary>Nearest registered office by journey minutes (null = none reachable).</summary>
        public string FindNearestOfficeId(string locationId, JourneyModel journeys, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (journeys == null || string.IsNullOrWhiteSpace(locationId)) return null;
            string best = null;
            int bestMinutes = int.MaxValue;
            foreach (PostalOffice office in offices.Values)
            {
                JourneyRoute route = journeys.FindRoute(locationId, office.LocationId, TravelMode.Foot);
                if (!route.Found) continue;
                if (route.TotalMinutes < bestMinutes)
                {
                    bestMinutes = route.TotalMinutes;
                    best = office.OfficeId;
                }
            }
            if (best == null)
                diag.Add($"PostalService: no reachable post office from '{locationId}' — no postal service there.");
            return best;
        }

        public MailItem GetMailItem(string mailId)
        {
            if (string.IsNullOrEmpty(mailId)) return null;
            mail.TryGetValue(mailId, out MailItem item);
            return item;
        }

        /// <summary>
        /// Estimates whole transit days between two offices via the journey model
        /// (Wagon mode — mail travels by stage). Returns -1 when unroutable.
        /// Used for arrival estimates; actual arrival is driven by AdvanceDay.
        /// </summary>
        public int EstimateTransitDays(string fromOfficeId, string toOfficeId, JourneyModel journeys)
        {
            if (journeys == null) return -1;
            if (string.Equals(toOfficeId, OffMapOfficeId, StringComparison.OrdinalIgnoreCase)) return 1;
            if (!offices.TryGetValue(fromOfficeId, out PostalOffice from)) return -1;
            if (!offices.TryGetValue(toOfficeId, out PostalOffice to)) return -1;
            JourneyRoute route = journeys.FindRoute(from.LocationId, to.LocationId, TravelMode.Wagon);
            if (!route.Found) return -1;
            return Math.Max(1, (route.TotalMinutes + 1439) / 1440);
        }

        /// <summary>
        /// Advances the postal network one day: offices dispatch posted mail on
        /// their departure days along real journey routes (Canon §7.2, §16.4);
        /// in-transit items arrive; handoffs through intermediate offices are
        /// recorded. No route = no dispatch — mail waits honestly.
        /// </summary>
        public void AdvanceDay(JourneyModel journeys, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (journeys == null)
            {
                diag.Add("PostalService.AdvanceDay: no journey model — mail does not move without roads.");
                return;
            }

            int weekday = ((dayIndex % 7) + 7) % 7; // 0=Monday convention (calibration)

            foreach (PostalOffice office in offices.Values)
            {
                if (office.DepartureWeekdays == null || !office.DepartureWeekdays.Contains(weekday))
                    continue;

                // Group this office's posted mail by destination office.
                var byDestination = new Dictionary<string, List<MailItem>>(StringComparer.OrdinalIgnoreCase);
                foreach (MailItem item in mail.Values)
                {
                    if (item.Status != MailStatus.Posted) continue;
                    if (!string.Equals(item.CurrentOfficeId, office.OfficeId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!byDestination.TryGetValue(item.ToOfficeId, out List<MailItem> list))
                    {
                        list = new List<MailItem>();
                        byDestination[item.ToOfficeId] = list;
                    }
                    list.Add(item);
                }

                foreach (var kvp in byDestination)
                {
                    DispatchTo(office, kvp.Key, kvp.Value, journeys, dayIndex, diag);
                }
            }

            // Arrivals.
            foreach (MailItem item in mail.Values)
            {
                if (item.Status != MailStatus.InTransit) continue;
                if (item.DueArrivalDayIndex > dayIndex) continue;
                if (string.Equals(item.ToOfficeId, OffMapOfficeId, StringComparison.OrdinalIgnoreCase))
                {
                    item.Status = MailStatus.ForwardedOffMap;
                    item.History.Add($"day {dayIndex}: forwarded off-map — left the simulated world.");
                    diag.Add($"PostalService: {item.MailId} forwarded off-map.");
                }
                else
                {
                    item.Status = MailStatus.Arrived;
                    item.CurrentOfficeId = item.ToOfficeId;
                    item.History.Add($"day {dayIndex}: arrived at {item.ToOfficeId} — awaiting in-person collection.");
                    diag.Add($"PostalService: {item.MailId} arrived at {item.ToOfficeId}.");
                }
            }
        }

        private void DispatchTo(
            PostalOffice office, string destOfficeId, List<MailItem> items,
            JourneyModel journeys, int dayIndex, List<string> diag)
        {
            if (string.Equals(destOfficeId, OffMapOfficeId, StringComparison.OrdinalIgnoreCase))
            {
                // Off-map mail leaves on the next departure — it still takes a
                // real leg to the gateway in the arrival accounting below.
                foreach (MailItem item in items)
                {
                    item.Status = MailStatus.InTransit;
                    item.DueArrivalDayIndex = dayIndex + 1;
                    item.History.Add($"day {dayIndex}: dispatched {office.OfficeId} → off-map.");
                }
                diag.Add($"PostalService: {items.Count} item(s) dispatched {office.OfficeId} → off-map.");
                return;
            }

            if (!offices.TryGetValue(destOfficeId, out PostalOffice dest))
            {
                diag.Add($"PostalService: destination office '{destOfficeId}' unknown — {items.Count} item(s) wait.");
                return;
            }

            JourneyRoute route = journeys.FindRoute(office.LocationId, dest.LocationId, TravelMode.Wagon);
            if (!route.Found)
            {
                diag.Add($"PostalService: no route {office.OfficeId} → {destOfficeId} — {route.Diagnostic} Mail waits.");
                return;
            }

            int transitDays = Math.Max(1, (route.TotalMinutes + 1439) / 1440);
            // Seeded reliability: most dispatches on time, some miss the connection (calibration).
            var rng = new System.Random(office.Seed * 100003 + dayIndex * 31 + destOfficeId.GetHashCode());
            bool onTime = rng.NextDouble() < OnTimeProbability;

            // Record real handoffs through intermediate offices on the route.
            var handoffNotes = new List<string>();
            if (route.LegLocationIds != null)
            {
                foreach (string legLocationId in route.LegLocationIds)
                {
                    foreach (PostalOffice mid in offices.Values)
                    {
                        if (string.Equals(mid.OfficeId, office.OfficeId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(mid.OfficeId, destOfficeId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(mid.LocationId, legLocationId, StringComparison.OrdinalIgnoreCase))
                            handoffNotes.Add(mid.OfficeId);
                    }
                }
            }

            foreach (MailItem item in items)
            {
                item.Status = MailStatus.InTransit;
                item.DueArrivalDayIndex = dayIndex + transitDays + (onTime ? 0 : 1);
                item.History.Add($"day {dayIndex}: dispatched {office.OfficeId} → {destOfficeId} " +
                    $"({route.TotalMiles:0.0} mi, due day {item.DueArrivalDayIndex}).");
                foreach (string hop in handoffNotes)
                    item.History.Add($"day {dayIndex}: handed off through {hop} (Canon §16.4 handoff).");
            }
            diag.Add($"PostalService: {items.Count} item(s) dispatched {office.OfficeId} → {destOfficeId} " +
                $"({route.TotalMiles:0.0} mi, {transitDays}d{(onTime ? "" : ", +1d missed connection")}).");
        }

        /// <summary>
        /// In-person collection at the office (Canon §7.2: rural households travel
        /// to collect/send mail). Only the recipient's own mail is released.
        /// </summary>
        public List<MailItem> CollectMail(string officeId, int recipientPersonId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var collected = new List<MailItem>();
            foreach (MailItem item in mail.Values)
            {
                if (item.Status != MailStatus.Arrived) continue;
                if (!string.Equals(item.ToOfficeId, officeId, StringComparison.OrdinalIgnoreCase)) continue;
                if (item.RecipientPersonId != recipientPersonId) continue;
                item.Status = MailStatus.Collected;
                item.History.Add($"day {dayIndex}: collected in person at {officeId}.");
                collected.Add(item);
            }
            diag.Add($"PostalService: {collected.Count} item(s) collected at {officeId} by P{recipientPersonId}.");
            return collected;
        }

        /// <summary>
        /// Offers a star-route carrier contract: the post office as a paying
        /// transportation customer (Canon §16.4). Paid per completed trip.
        /// </summary>
        public PostalContract OfferContract(
            string officeId, string routeDescription, string carrierBusinessInstanceId,
            int payPerTripCents, int startDayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!offices.ContainsKey(officeId))
            {
                diag.Add($"PostalService.OfferContract: unknown office '{officeId}'.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(carrierBusinessInstanceId) || payPerTripCents <= 0)
            {
                diag.Add("PostalService.OfferContract: a real carrier and positive per-trip pay are required.");
                return null;
            }
            var contract = new PostalContract
            {
                ContractId = $"postcon-{officeId}-{sequence++}",
                OfficeId = officeId,
                RouteDescription = routeDescription ?? string.Empty,
                CarrierBusinessInstanceId = carrierBusinessInstanceId,
                PayPerTripCents = payPerTripCents,
                StartDayIndex = startDayIndex,
            };
            contracts[contract.ContractId] = contract;
            diag.Add($"PostalService: contract {contract.ContractId} — {carrierBusinessInstanceId} carries '{routeDescription}' at {payPerTripCents}c/trip.");
            return contract;
        }

        /// <summary>
        /// Settles one contracted trip. Returns the pay due; the caller moves it
        /// from the post office's business funds to the carrier with provenance.
        /// Undelivered trips are not paid — no free subsidy (Canon §16.4).
        /// </summary>
        public int CompleteContractTrip(string contractId, bool delivered, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!contracts.TryGetValue(contractId, out PostalContract contract) || !contract.Active)
            {
                diag.Add($"PostalService.CompleteContractTrip: unknown or inactive contract '{contractId}'.");
                return 0;
            }
            if (!delivered)
            {
                diag.Add($"PostalService: contract {contractId} trip not delivered — no pay (Canon §16.4).");
                return 0;
            }
            contract.TripsCompleted++;
            contract.TotalPaidCents += contract.PayPerTripCents;
            diag.Add($"PostalService: contract {contractId} trip complete — {contract.PayPerTripCents}c due to {contract.CarrierBusinessInstanceId}.");
            return contract.PayPerTripCents;
        }

        /// <summary>
        /// Returns accumulated postage for the caller to post to the business
        /// ledger with provenance (mirrors FreightCompanyRuntime.CollectFreightCharge).
        /// </summary>
        public int CollectPostageRevenue(List<string> diag)
        {
            diag = diag ?? diagnostics;
            int cents = Math.Max(0, uncollectedPostageCents);
            uncollectedPostageCents = 0;
            if (cents > 0)
                diag.Add($"PostalService: {cents}c postage revenue released for ledger posting (provenance: postage).");
            return cents;
        }

        /// <summary>NX-2A: postal work as TTS tasks (sorting, dispatch, local delivery).</summary>
        public void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            RegisterQuietly(authority, new TaskDefinition(SortMailTaskId, "Sort mail", 15));
            RegisterQuietly(authority, new TaskDefinition(DispatchPouchTaskId, "Dispatch mail pouch", 30));
            RegisterQuietly(authority, new TaskDefinition(DeliverLocalMailTaskId, "Deliver local mail", 5));
        }

        private static void RegisterQuietly(TaskAuthority authority, TaskDefinition definition)
        {
            authority.RegisterDefinition(definition, out _);
        }

        public PostalServiceSaveDto CaptureSaveDto()
        {
            return new PostalServiceSaveDto
            {
                offices = new List<PostalOffice>(offices.Values),
                mailItems = new List<MailItem>(mail.Values),
                contracts = new List<PostalContract>(contracts.Values),
                uncollectedPostageCents = uncollectedPostageCents,
                sequence = sequence,
            };
        }

        public void LoadFromSaveDto(PostalServiceSaveDto dto)
        {
            offices.Clear();
            mail.Clear();
            contracts.Clear();
            if (dto == null) return;
            if (dto.offices != null)
                foreach (PostalOffice o in dto.offices)
                    if (o != null && !string.IsNullOrEmpty(o.OfficeId)) offices[o.OfficeId] = o;
            if (dto.mailItems != null)
                foreach (MailItem m in dto.mailItems)
                    if (m != null && !string.IsNullOrEmpty(m.MailId)) mail[m.MailId] = m;
            if (dto.contracts != null)
                foreach (PostalContract c in dto.contracts)
                    if (c != null && !string.IsNullOrEmpty(c.ContractId)) contracts[c.ContractId] = c;
            uncollectedPostageCents = Math.Max(0, dto.uncollectedPostageCents);
            sequence = Math.Max(0, dto.sequence);
        }
    }
}
