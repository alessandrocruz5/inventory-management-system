# Inventory System — Sprints
Status: [ ] planned · [~] in progress · [x] merged · [-] cancelled (excluded from changelog)

> Each unit's plan, prompts and acceptance criteria are in its `plan:` link. `/build-unit {KEY-id}` runs them.
> A unit with `spec:` instead of `plan:` isn't planned yet: `/plan-feature {KEY-id}` plans it from that spec slice and replaces `spec:` with `plan:` and `prompts:`. Models are provisional until then.

## Sprint 1 — Stock ledger core   (planned 2026-11-04)
Epic: IMS-1 · Branch base: `main` · Plan: [[Inventory System — Sprint 1 Spec]]
- [~] IMS-2 — S1 Skeleton · Added · plan: [[2026-11-04-s1-skeleton]] prompts: 01, 02, 03, 04, 05, 06 · depends: — · model: sonnet
- [ ] IMS-3 — S2 Platform · Added · spec: §12 S2 · depends: IMS-2 · model: opus
- [ ] IMS-4 — S3 Tenancy enforcement · Added · spec: §12 S3 · depends: IMS-3 · model: opus
- [ ] IMS-5 — S4 Master data · Added · spec: §12 S4 · depends: IMS-4 · model: sonnet
- [ ] IMS-6 — S5a Ledger schema · Added · spec: §12 S5a · depends: IMS-5 · model: opus
- [ ] IMS-7 — S5b Costing rules · Added · spec: §12 S5b · depends: IMS-2 · model: opus
- [ ] IMS-8 — S5c Posting engine · Added · spec: §12 S5c · depends: IMS-6, IMS-7 · model: opus
- [ ] IMS-9 — S5d Opening stock · Added · spec: §12 S5d · depends: IMS-8 · model: opus
- [ ] IMS-10 — S6 Transfers and adjustments · Added · spec: §12 S6 · depends: IMS-8 · model: opus
- [ ] IMS-11 — S7 Stock screens · Added · spec: §12 S7 · depends: IMS-10 · model: sonnet
- [ ] IMS-12 — S8 Item history · Added · spec: §12 S8 · depends: IMS-10 · model: sonnet
- [ ] IMS-13 — S9 Reversals · Added · spec: §12 S9 · depends: IMS-10, IMS-11 · model: opus
- [ ] IMS-14 — S10 Negative stock · Added · spec: §12 S10 · depends: IMS-8, IMS-11 · model: opus
- [ ] IMS-15 — S11 Ledger health · Added · spec: §12 S11 · depends: IMS-13 · model: opus
- [ ] IMS-16 — S12 End to end · Added · spec: §12 S12 · depends: IMS-13 · model: sonnet
- [ ] IMS-17 — S13 Compose output (stretch) · Added · spec: §12 S13 · depends: IMS-16 · model: haiku
- [ ] IMS-18 — S14 Azure (stretch) · Added · spec: §12 S14 · depends: IMS-17 · model: sonnet

**Downstream (locked by IMS-2; plan [[2026-11-04-s1-skeleton]]):**
- Every test project references `xunit.v3` only (no `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`), has `<OutputType>Exe</OutputType>`, and runs under MTP through `global.json`. Create each test project together with its first test.
- `Inventory.Domain` depends only on `System.*` (not `System.Data`), itself and `Kernel`. `Kernel` never depends on EF Core, ASP.NET Core, SqlClient or `System.Data`, so its "ProblemDetails types" are slug constants or exceptions that Api maps.
- A module implementation exposes exactly one public type, `<M>ModuleExtensions`; the exception is namespaces ending in `.Migrations`, because EF generates migrations as public. Everything else is `internal`, and test projects get `InternalsVisibleTo`. Widening the surface means editing T12 in the same PR, with the reason.
- In Platform, only the `Platform.Identity` namespace touches `System.Security.Claims`.
- Only `src/Api` and `tests/**` reference a module implementation. A module may reference another module's `.Contracts`.
- Aspire resources are `sql`, `api` and `web`, and the database is `inventory` (`ConnectionStrings:inventory`). S2 wires the dev proxy `/api` → `services__api__http__0`, adds the `inventory_app` password parameter, and adds a T12 rule against `Database.Migrate*` outside the migrate step.
- npm's allowed list includes `eslint` and `typescript-eslint`, as angular-eslint's peers.
- The CI job ids `build` and `test` are the ruleset's required check contexts. S12 adds `e2e`.
