# Inventory System — Sprints
Status: [ ] planned · [~] in progress · [x] merged · [-] cancelled (excluded from changelog)

> Each unit's plan, prompts and acceptance criteria are in its `plan:` link. `/build-unit {KEY-id}` runs them.
> A unit with `spec:` instead of `plan:` isn't planned yet: `/plan-feature {KEY-id}` plans it from that spec slice and replaces `spec:` with `plan:` and `prompts:`. Models are provisional until then.

## Sprint 1 — Stock ledger core   (planned 2026-11-04)
Epic: IMS-1 · Branch base: `main` · Plan: [[Inventory System — Sprint 1 Spec]]
- [ ] IMS-2 — S1 Skeleton · Added · spec: §12 S1 · depends: — · model: sonnet
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
