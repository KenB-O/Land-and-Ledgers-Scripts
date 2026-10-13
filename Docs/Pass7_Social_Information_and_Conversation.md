# Pass VII Workstream E: Social Information and Conversation

## What already existed

Conversation, knowledge, and social activity existed as concepts but Pass VI confirmed the ordinary NPC-to-NPC economic-information chain was absent. No NPC-to-NPC conversation ran. Socializing was never planned. The saloon runtime was test-only. There was no belief store and no foreman role. The flagship foreman chain (mine conditions to foreman knowledge to saloon conversation to bartender knowledge to downstream decision) could not run past its first step.

Information provenance concepts existed in the knowledge and evidence architecture but had no production transmission path.

## What was disconnected

A speaker could not encounter a listener. Knowledge could not move between persons. Rumors could not propagate or remain uncertain. The player could not learn mine conditions through legitimate social channels.

## What was changed

Minimal production information foundation (worker 4, merge b81d471):
- `KnowledgeTransmission.cs` (Domains/Population/): `InformationPacketFactory.Observe` creates firsthand observation packets; `Relay` creates transmitted packets preserving the full chain. `KnowledgeTransmissionStore` records transmissions with source, speaker, recipient, time, subject, confidence, original observation, transmission chain, restrictions, and contradictions. `IndependentSourceCount` prevents repeated conversation from fabricating independent confirming sources. `HasPacket` provides idempotency. Save/load preserves packets and transmission history (`KnowledgeTransmissionSaveDto`).
- `ConversationOpportunity.cs` (Domains/Population/): validates legitimate communication opportunities. Physical conversation requires co-location, availability, time, activity compatibility, relationships, and access. A person cannot converse in the saloon while working in a distant mine during the same interval. Communication cannot occur without a valid channel.
- `SocialPlanning.cs` (Domains/Population/): `SocialVisitPlanner` plans social visits from real motives (companionship, friendship, family, recreation, job seeking, business interest, loneliness, invitations, known gathering places). Not every interaction transmits economically useful information. Social activity without economic gain is a supported outcome.
- `SimulationSystemsHub` registers the `KnowledgeTransmissionStore` as a hub-owned authority (production registration, not test-only).

The foreman chain (Slice I):
- Real mine conditions exist through the mine production authorities.
- A foreman or informed worker legitimately knows them through employment and observation.
- The person visits a saloon or gathering location through the social planning path.
- A conversation occurs with a real bartender or person through a validated opportunity.
- Information transmits with provenance and uncertainty. The speaker may be wrong; the mine may discover a new vein; management may know more.
- The bartender's knowledge changes. The bartender may later convey it to the player or another person.
- The recipient can investigate through legitimate means and reconsider an economic decision.
- The player cannot automatically see hidden mine reserves. Knowledge is not omniscient. Off-screen persons use the same information rules.

Information consumers:
- Transferred information connects to employment search, recruitment, supplier choice, business risk judgment, and other decision evidence where the existing systems accept evidence inputs.
- Knowledge spreads through actual connections, not instantly to all NPCs.

## Why the correction follows Canon

Canon requires information to retain provenance and restricts NPCs from acting on facts they never learned. The packet architecture records source, chain, confidence, and contradictions. No global belief engine was introduced. The saloon uses the same social activity and information mechanics as other venues; it received no special conversation engine.

## Authoritative systems reused

Person identity, social relationships, spatial state, activity scheduling, the existing knowledge and evidence architecture, the hub due-event scheduler. No unrelated global belief engine. The saloon runtime remains scene-owned; the source provides the production adapter contract.

## Source entry points

- `InformationPacketFactory.Observe` and `Relay`: packet creation with provenance.
- `KnowledgeTransmissionStore.RecordTransmission`: the transmission authority.
- `KnowledgeTransmissionStore.PacketsFor` and `IndependentSourceCount`: knowledge queries.
- `ConversationOpportunity`: encounter validation.
- `SocialVisitPlanner.PlanSocialVisit`: motive-driven social planning.
- `SimulationSystemsHub.KnowledgeTransmissions`: the hub-registered store.

## Tests that prove it

- ForemanInformationSliceTests (2 passed): SliceI_ForemanInformation (mine conditions to foreman knowledge to saloon conversation to bartender knowledge to downstream decision influence, with provenance and uncertainty preserved, no perfect knowledge leak).
- SocialInformationTests (15 passed): encounters between real persons, time-respecting conversation, no sharing of unknown information as firsthand knowledge, legitimate knowledge change, provenance preservation, uncertain rumors, no fabricated independent sources, no communication without valid channel, save/load preservation, knowledge affecting economic decisions, non-omniscient player knowledge, social interaction without economic opportunity (SliceJ), off-screen persons under the same rules.

## What remains source-only

The transmission authority is hub-registered and tested. Automatic social-visit scheduling in live runtime (who decides to visit the saloon on a given evening) is not yet driven; `SocialVisitPlanner` is currently called from tests. NPC conversation UI and dialogue presentation are scene-owned.

## What Codex must connect in Unity

- Drive social-visit planning from the live daily schedule for persons with social motives.
- Connect the saloon scene runtime to `ConversationOpportunity` validation and the transmission store.
- Validate in PlayMode: a foreman with mine knowledge visits the saloon, converses, and the bartender's knowledge changes; the player learns of it only through legitimate conversation with the bartender.
