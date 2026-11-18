# Codex Pass 7 — Sawmill Labor Recovery

Date: 2026-11-18
Project: Land & Ledgers
Unity: 6000.4.2f1

## Finding

The November 17 PlayMode evidence recorded the sawmill job refusing assignment because P11 had zero of 600 daily work minutes left for an 81-minute task. P11 is the sawmill owner Enzo Kwindt, not a mill employee. The authored town also assigned the owner logging work. The opening-day planner reserves 60 minutes for management; the forestry drive can commit the remaining 540 minutes for six 90-minute felling tasks. This is a legitimate exhausted daily budget, not evidence that the daily labor limit should be raised.

The production integration selected the owner alone as sawyer. It did not fall back to the mill's active employees after TaskAuthority rejected that unstarted assignment. Consequently, an available employed worker could not take the existing mill job.

## Repair

When the initial sawmill task is refused specifically because the worker lacks daily work minutes, the orchestration now checks the mill owner's active employment relationships. It skips candidates with no remaining budget and offers the same planned job and log lot to an active employee. The driver allows reassignment only after the preceding task is cancelled; TaskAuthority still assigns, starts and accounts for the replacement worker's actual task duration. Other refusal causes do not trigger this fallback.

The regression covers an exhausted owner with management and felling commitments, refusal without log consumption, reassignment to an employed sawyer, one production run, 8 lumber units from 2 logs under the existing conversion profile, 60 minutes committed to the worker, and rejection of reassignment after job completion.

## Verification

- Unity compilation: completed successfully; no compiler errors were reported.
- Focused Unity EditMode suites: 135 passed, 0 failed across sawmill production (36), TaskAuthority (10), work-time budgets (10), logging camp site (15), logging runtime (18), sawmill-to-yard transfers (25), wholesale settlement (11), lumber-chain end-to-end (3), and lumber buyer (7).
- `git diff --check`: passed for the scoped changes.
- PlayMode: attempted once. Unity entered the domain-reload phase, but the Editor remained on a `Hold on (busy)` window for several minutes and no reliable runtime observations were available. The Editor was not killed or restarted. A live logging-to-lumber run, physical worker reachability, yard transfer, and downstream purchase therefore remain unverified.
- A separate `dotnet build Assembly-CSharp.csproj --no-restore` attempt could not parse Unity's generated project because the path `F:\Land & Ledgers` is emitted with an unescaped ampersand in the generated XML. Unity's own compile succeeded; the generated project file was not edited.

## Remaining verification

This repair addresses the specific owner-only sawyer selection failure. It does not establish that the entire harvesting, hauling, sawmill, lumberyard and carpentry chain completes in live gameplay. Verify those physical and economic handoffs in a responsive PlayMode session before treating the chain as complete.
