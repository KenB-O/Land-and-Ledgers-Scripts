# Pass XL Through-Traveler Immigration — Historical Note

## What the governing bibles already say (checked first)

Canon XXXI, checked in the paginated original in this repo's
`Planning Sources` (and unchanged in docpkg-v3.0, 2026-10-31, whose
governing originals are Canon XXXI / Tech XIV, both 2026-10-07):

- §3.4, CANON LOCK: "Housing does not increment population. A household
  becomes a permanent resident only through an actual
  migration/occupancy path: awareness or referral -> opportunity
  evaluation -> acceptable work/income path where needed -> available
  accommodation -> travel -> physical arrival -> residence
  stabilization."
- §7.1: "Vacant housing alone does not create migration. People move
  for durable work, wages, business/land opportunity, kinship or another
  plausible pull, then housing determines whether and how the move can
  occur."
- §7.4: "A useful causal loop is work/business opportunity -> feasible
  housing -> household migration -> relationship/word-of-mouth spread
  -> more workers/customers/businesses -> more opportunity."
- §9.5: kinship, religious/community, and language networks "can lower
  information, travel and initial-housing barriers" and create leads,
  not direct population increments.

The through-traveler channel in `Domains/Population/ThroughTraveler.cs`
is built to follow that loop exactly, and to replace the old 4% monthly
arrival roll (`MigrationDriver.MonthlyArrivalProbability`) and the
distress/opportunity out-migration draw in the same driver, which
created or removed persons with no job reason, no housing check, and
no contact awareness. Those old paths are NOT deleted in this pass
(they are existing, tested behaviour); the new channel is the
condition-gated replacement for later play, and the old roll must be
retired by a Kennedy decision, not silently deleted here.

## Web research (general frontier history, not canon)

- Boarding houses were essential housing infrastructure for migrants,
  workers, and newcomers who could not yet afford a private home,
  clustered near work, rail yards, and commercial districts, and they
  provided social contact and information about jobs, not just a bed.
  (Headcount Coffee, "When Boarding Houses Ruled"; Beverly
  boarding-house study, 1880-1920, 102 boarding houses in the 1880
  census, boarders as a normal, affordable, temporary option.)
- Stage stops and trail stops were supply, mail, news, and horse-change
  nodes, not destinations in themselves; a creek, water, and a store
  turned a stop into a camp, then a stage stop, then a store, then a
  homestead. Travelers learned routes by word of mouth from previous
  travelers. (Rock Creek Station / Stricker Homesite, Idaho.)
- Chain migration: prospective migrants learn of opportunities, are
  provided with transportation and initial accommodation and
  employment, through primary social relationships with previous
  migrants (MacDonald definition, via Wikipedia / immigration
  history). Letters, remittances, and returnees kept home communities
  informed about living conditions and employment prospects, and
  each new arrival made people they knew more likely to follow.

## Grounded vs game abstraction

GROUNDED (supported by the sources above and by Canon §7.4):
- A traveler stops, lodges (boarding house first), visits the store
  and livery, and may stay where work and housing exist.
- A traveler who passes on still carries word back along the route;
  a good stop produces later arrivals.
- Kinship and correspondence lower information and initial-housing
  barriers; they do not create population by themselves.

GAME ABSTRACTION (not a historical claim):
- One reputation score (0-1) aggregating housing, work, growth, and
  opinion. No historical town had a single reputation number; this is
  a checkable game value, with its inputs named in
  `TownReputationModel`, so Kennedy can see exactly what it is.
- Traveler frequency, boarding capacity rules, and reputation weights
  are caller-supplied test parameters. No historical frequency is
  claimed for them.
- A "traveler visit" is a single-day record, not a multi-week wagon
  journey with feed, weather, and breakdowns. The journey systems
  exist elsewhere in code; this channel does not simulate them.

## Anachronisms explicitly avoided

- NO modern-tourism model: travelers are not tourists, sightseers,
  or hotel guests on holiday. They are route traffic, transient
  labour, and westbound migrants. No vacation, return-tourism, or
  review-score mechanics are modelled.
- NO instant settlement: a traveler who "stays" is not a resident
  until ArrivalSettlement runs (housing + household + job or explicit
  unemployed-with-plan). A boarding-house night is lodging, not
  residence.
- NO ethnic or cultural bonus: Canon §9.5 forbids converting
  ethnicity into an economic-stat bonus, and this channel does not.
- NO free carriage: a letter to a contact goes through
  PostalService with real postage and offices, not through this
  channel for free.

## Boarding house status in code (not faked)

`BoardingHouseShopRuntime` is real: room inventory, rate schedule,
boarder register, kitchen, housekeeping, reservations, conduct log,
and fuel stock, with a save DTO. What is NOT claimed: that a boarding
house exists in the six-person slice (it does not — the slice has
General Store, Bakery, Fuel Dealer only), or that a traveler can be
checked into a boarding house in Main Scene today. The traveler code
takes boarding vacancies as a caller-supplied input from that runtime;
a driver that passes 0 when no boarding house exists is correct, and
a driver that invents vacancies is not.
