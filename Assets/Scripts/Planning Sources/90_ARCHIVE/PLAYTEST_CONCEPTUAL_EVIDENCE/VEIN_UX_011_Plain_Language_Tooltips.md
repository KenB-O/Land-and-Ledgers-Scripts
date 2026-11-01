# VEIN-UX-011 — Plain-language tooltips for complex systems

**Recorded:** 2026-10-19 (playtest state September 14, 1877)  
**Status:** Design finding, not implemented feature

## Problem
An ordinary player expects a *gold mine* but does not recognize terms such as quartz-bearing rock, composite assays, possessory claim interests, milling, tailings, or contingent liabilities. Specialist language can obscure decisions.

## Requirement
- Write concise, player-centered display names, with technical terms secondary, e.g. **Gold-bearing rock (quartz)**. A quartz vein can contain gold but visual appearance alone does not prove it.
- Every relevant unfamiliar term supports hover on desktop, focus/keyboard access and tap on controller/mobile, with a simple one-sentence tooltip and optional **More details** explainer.
- Disclose provenance/uncertainty explicitly: visually suspected gold-bearing rock, assayed rock (grade), processed material, physically recovered gold and actual sold gold are all different quantities.
- Do not reveal concealed geology or private NPC knowledge through generic tooltip text.
- Apply to labor, logistics, purchase offers, mineral/land title, loans/collateral, property rights, mills and accounting.
- Include short worked examples for dollar units, tons, troy ounces and cost interpretation when appropriate.
- Make labels consistent across search, dialogue, tooltips, alerts and ledgers.

## Sample tooltip
**Gold-bearing rock (quartz)** — “Quartz is a common hard rock-forming mineral that sometimes contains gold. We need testing to know how much gold, if any, can be recovered profitably.”

**Assay** — “A laboratory test estimating how much gold is in a sample. It does not mean we have already recovered that gold.”

**Tailings** — “Crushed rock and other material left after ore processing. It still takes up space and may need safe disposal.”

**Mineral claim** — “A right to explore and extract minerals on qualifying land. It is not automatically ownership of the entire parcel.”

## Implementation outline
Term registry with stable semantic IDs, localized short/long definitions, optional context-sensitive annotations and a validation rule against leaking hidden facts. Labels can be varied by UI, but definitions share the same registry. No LLM required.
