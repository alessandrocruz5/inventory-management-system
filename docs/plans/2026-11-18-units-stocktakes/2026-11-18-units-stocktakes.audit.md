## Audit · 2026-11-18-units-stocktakes · 2026-11-18 · Codex (GPT-6) · BLOCK
Status: applied 2026-11-18: 1 applied, 0 disputed

Graph built: none (graph.json absent) · Symbols traversed: 0 · Missed blast-radius items: 0

| Sev | Check | Finding | Evidence | Fix |
|---|---|---|---|---|
| BLOCK | Breakage | U2's rollback claim is unsafe: reverting the API removes `unit` from its request DTO, but the default JSON deserializer silently skips unmapped fields. A client still sending `quantity: 1, unit: "sack"` can therefore get a successful base-unit post for quantity 1, silently misposting stock. | Plan rollback at `2026-11-18-units-stocktakes.md:230`; repo search for `UnmappedMemberHandling`, `JsonUnmappedMemberHandling`, `ConfigureHttpJsonOptions` and `AddJsonOptions` returned no matches. Microsoft documents that unmapped fields are ignored by default: [System.Text.Json unmapped members](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members). | Replace the claim that `unit` becomes an unknown/rejected property with an explicit fail-closed rollback/deployment mitigation. Ensure any rollback cannot leave a client that submits alternate-unit quantities talking to an API that interprets them as base units; document the exact safe rollback sequence or require a compatibility guard before enabling U2 rollback. |

**Bloat estimate:** planned ~50 files / ~3,500 LOC; minimal viable ~45 files / ~3,000 LOC. Most scope maps directly to R1–R6 and the explicit API, schema, test and UI requirements; no standalone bloat finding.
**Model cost:** 7 opus prompts (01, 03, 05, 06, 07, 09, 10, 12 as listed); appropriate for high-stakes persistence, posting, shared-contract and state-machine work. Sonnet is suitable for the remaining prompts.

### Prompt for Claude
Run: `/apply-review 2026-11-18-units-stocktakes`

Or, without claude-config, update `/Users/alessandrocruz/Code/inventory-management-system/docs/plans/2026-11-18-units-stocktakes/2026-11-18-units-stocktakes.md:230`: correct the rollback statement because an older API silently skips the `unit` JSON field and may post an alternate-unit quantity as a base-unit quantity. Add a concrete fail-closed rollback/deployment mitigation that prevents this data corruption. Touch only the plan file. Never create or switch branches, commit or push unless this is build-unit's pr fix loop. Done when the rollback section describes a sequence/guard that cannot silently reinterpret requests carrying `unit`.

Audit correction: the model-cost line above names eight opus prompts (01, 03, 05, 06, 07, 09, 10, 12); its numeral should read **8**, not 7.
