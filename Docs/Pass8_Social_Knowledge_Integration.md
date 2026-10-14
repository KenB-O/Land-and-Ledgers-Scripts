# Pass VIII Worker 8. Social Knowledge Integration

## What Pass VII built and what was missing

Pass VII Workstream E built the information foundation. ConversationOpportunityService validates four legitimate channels (venue, workplace, household, correspondence). KnowledgeTransmissionStore records InformationPackets with full provenance, refuses duplicate tellings, and counts independent sources by distinct chain roots. SocialVisitPlanner plans motive-driven social visits. The foreman chain proved the path works end to end.

What was missing. Decision authorities did not consume packets. The Pass VII test for knowledge affecting an economic decision only recorded a refusal reason string that cited information. No production authority read PacketsFor, evaluated corroboration, or changed its outcome based on what a person actually heard. The foreman example was therefore a one-off proof, not a generalized integration. This pass closes that gap for three decision domains while keeping every safeguard.

## The three emergent chains

Each chain runs observation of a real state, then conversation through a legitimate channel, then a packet with provenance, then a decision authority that actually consumes the packet, then a real economic consequence.

### Chain 1. Layoff rumor leads a miner to seek another job

A miner is dismissed at mine-1. The dismissal is real registry state (EmploymentRelationship ended, EndReason Dismissed). The dismissed miner directly observes it and tells a still-employed coworker at the saloon through overlapping social visits. The packet carries SourceKind direct-observation with chain root P101. The new WorkerJobSecurityEvaluator reads only the coworker's own packets about the layoff-risk subject for mine-1, finds a firsthand telling, and returns SeekWork with a cited reason. The worker then commits real person-time (a JobSeeking social visit through the schedule tracker) and submits a real application through the new RecruitmentService.ApplyForEffort, which creates a real CandidateInquiry on mine-2's open effort. The hiring side can complete the chain through the existing HireFromInquiry, creating a real employment relationship. Nothing in this chain invents a person, a job, or an opportunity.

### Chain 2. Supplier trouble steers a merchant's order

Mill-1 really defaults on a trade obligation (a real FinancialObligation with status Defaulted). The creditor's clerk directly observes the default and tells the store manager at the saloon. The Procure executor now consults the manager's own packets about the supplier-trouble subject for mill-1 through the shared SocialEvidenceEvaluator. The trouble is corroborated by a firsthand telling, so the executor skips mill-1 and places the order with mill-2, the next known supplier with real goods. Cash moves buyer to mill-2 and is conserved. Mill-2's real goods leave its shelves. Mill-1's stock is untouched. The merchant was never told mill-1 could not deliver. The order simply went elsewhere on corroborated evidence.

### Chain 3. Borrower trouble reaches the lender, which seeks corroboration

A farm really misses a payment (a real delinquent obligation). Two independent observers directly observe it and each tells the bank's lending officer at the saloon. The lender's new BindSocialKnowledge intake lets BuildBorrowerInformation read only the officer's own packets about the borrower-trouble subject for farm-1. Two independent chain roots mean corroborated, so lender trust falls by the documented 0.15 and flows into the shared WorkingCapitalUnderwriting, which prices the loan higher. The loan is still approved on real projections, exposure, and collateral. The credit file discloses the social evidence with its chain roots. When the officer hears only one relayed rumor, the bank checks its own obligation records first. Records confirm, trust falls a little more and the file says why. Records show nothing, the rumor is noted as seeking corroboration and pricing is exactly unchanged.

## Files added

- Domains/Population/SocialEvidenceEvaluator.cs plus .meta. The single shared honest-evidence assessment. Reads only one person's received packets about one subject. Reports packet count, independent sources by distinct chain roots, firsthand tellings, max confidence, and whether the shared corroboration policy is met (firsthand telling, or two or more independent sources). Stable subject builders for layoff risk, supplier trouble, and borrower trouble. Never upgrades confidence.
- Domains/Population/WorkerJobSecurityEvaluator.cs plus .meta. Chain 1 decision authority. Returns SeekWork only for actively employed workers with corroborated layoff-risk evidence. Uncorroborated rumors and empty knowledge both return stay decisions with honest reasons.
- Tests/Editor/Population/SocialKnowledgeIntegrationTests.cs plus .meta. Nine tests proving the three chains and the safeguards.

## Files changed

- Domains/Economy/Recruitment/RecruitmentService.cs. Added the public ApplyForEffort method. A real person applies to a real open effort on their own initiative. Currently-employed applicants are accepted with their employment recorded honestly on the inquiry. One open inquiry per person per effort. Additive only.
- Domains/Economy/Labor/ManagerDecisionExecution.cs. Added the optional SocialKnowledge field to ManagerExecutionServices (null by default, no behavior change unwired). The Procure executor skips suppliers with corroborated trouble known to the manager person and merely notes uncorroborated rumors.
- Domains/Economy/Financing/BusinessLenderService.cs. Added BindSocialKnowledge, the ApplyBorrowerSocialEvidence policy inside BuildBorrowerInformation, the records-based corroboration check, and the social-evidence disclosure on the credit file. Trust reduction flows through the existing shared underwriting. Rumor alone can never refuse. Additive only.
- Orchestration/Systems/SimulationDrivers.cs. Wired SocialKnowledge equals hub.KnowledgeTransmissions into the production manager execution services. Added a boundary comment at the lender charter documenting that the lender intake stays unwired until the town bank has a designated lending officer person.

## Safeguard proofs

- No omniscient decisions. WorkerJobSecurityEvaluator, the Procure executor, and the lender all read only packets the deciding person actually received. Tests prove a worker who heard nothing stays, and an unwired lender prices identically with packets sitting unheard in the store.
- No invented independent sources. Duplicate tellings are refused by the store. Relayed rumors keep one chain root. Tests assert IndependentSourceCount stays 1 after repeats and relays.
- Confidence decay. The relay test asserts the held packet confidence is strictly below the original observation and the chain string is intact.
- No instant global knowledge. Packets exist only for recipients of real conversations. The non-omniscience test asserts the store holds nothing for persons outside the conversation.
- No mandatory opportunity from every conversation. Uncorroborated rumors produce stay decisions, unchanged orders, and unchanged pricing, each with a diagnostic that says corroboration is still sought.
- No rumor-only refusal. The lender reduces trust, which the shared underwriting may price or, for an already marginal borrower, refuse through its real trust floor. The refusal reason would come from underwriting, never from the rumor alone.
- No fake persons, no invented goods, no teleported stock. Every person, business, obligation, inquiry, and shipment in the tests is real state created through the production authorities.

## Source-only versus Unity-needed, exact boundary

Source-complete and harness-proven. The evaluators, the packet consumption in the Procure executor and the lender, the ApplyForEffort path, and the production wiring of the knowledge store into manager execution.

Needs live-runtime driving, same as Pass VII left it. The daily driver that calls WorkerJobSecurityEvaluator for employed workers does not exist yet in production. SocialVisitPlanner is still called from tests, so the saloon conversations that feed these chains still need the live daily schedule integration Codex was already assigned (drive social-visit planning from the live daily schedule, connect the saloon scene runtime to ConversationOpportunity validation and the transmission store).

Needs a content step, not a code step. The lender intake is source-ready but unwired in production because the town bank has no designated lending officer person yet. When bank staffing lands, call hub.BusinessLender.BindSocialKnowledge with hub.KnowledgeTransmissions and the officer person id at the lender charter site in SimulationDrivers. Inventing an officer would be a fake person, so this waits for real staffing.

## What remains incomplete and why

- The three further example chains from the brief (tenant hearing a boarding house is full, farmer equipment sharing, wage-driven migration evaluation) are not built. The shared evaluator and the subject-string pattern make them straightforward follow-ups, but each needs its own real state authority and decision consumer, and this pass stopped at three proven chains plus safeguards.
- The lender trust-reduction tuning figures (0.15 corroborated, 0.18 records-confirmed) are calibration choices documented in code. They were not tuned against long-run simulation outcomes.
- No Unity scene work was done or claimed. All results below are Roslyn harness only.

## Harness results on the final HEAD

9 new tests, all passing. Full suite. 1018 passed, 4 failed. The 4 failures are pre-existing on the base commit and unrelated to this work (three MoneyOrderServiceTests about indorsement, duplicate replacement, and settlement netting, plus P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear). Baseline before this work was 1009 passed with the same 4 failures. No regressions.
