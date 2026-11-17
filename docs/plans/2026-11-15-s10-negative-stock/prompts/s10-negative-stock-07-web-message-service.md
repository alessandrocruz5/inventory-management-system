---
type: prompt
plan: "[[2026-11-15-s10-negative-stock]]"
id: 07
unit: IMS-14
status: done
model: haiku
effort: low
depends_on: []
parallel_group: A
files:
  create: []
  modify:
    - web/src/app/core/problems/problem-messages.ts
    - web/src/app/core/problems/problem-messages.spec.ts
    - web/src/app/locations/locations.service.ts
    - web/src/app/locations/locations.service.spec.ts
  delete: []
---
# 07 · Web: problem message and `setAllowNegativeStock`

## Task
Map the new `negative-balances-exist` problem slug to a friendly message, and add a `LocationsService` method that flips a location's negative-stock toggle. Cover both with Vitest.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-14`. Stop if `git branch --show-current` says otherwise. Angular (standalone, signals, zoneless), Vitest via `npm test`.
- Hidden coupling (CLAUDE.md): every ProblemDetails slug in Kernel `ProblemTypes` must be in `PROBLEM_MESSAGES`. The API returns `type: "/problems/negative-balances-exist"` (422) when an Admin turns the toggle off while a balance there is below zero.
- The API (prompt 04): `PUT /api/locations/{id}` with `{ code, name, version, allowNegativeStock }`. `allowNegativeStock` is optional; when it's sent, the toggle changes. It returns the updated `Location`, with a new `version`.

### 1. `web/src/app/core/problems/problem-messages.ts`
Add, after `'stock-not-zero'`:
```ts
'negative-balances-exist':
  'Some items here are below zero. Receive or correct them to zero or above before turning negative stock off.',
```
In `problem-messages.spec.ts`, add `it('maps negative-balances-exist', …)` in the `problemMessage` describe, copying `it('maps stock-not-zero', …)`.

### 2. `web/src/app/locations/locations.service.ts`
Add, after `update`:
```ts
/** Turns a physical location's negative-stock toggle on or off (Admin). Sends the row's code, name and version unchanged. */
setAllowNegativeStock(location: Location, allowNegativeStock: boolean): Promise<Location> {
  return firstValueFrom(
    this.http.put<Location>(`/api/locations/${encodeURIComponent(location.id)}`, {
      code: location.code,
      name: location.name,
      version: location.version,
      allowNegativeStock,
    }),
  );
}
```
Leave `LocationInput`, `update` and `create` unchanged: the edit dialog never sends the toggle, so it stays unchanged.
In `locations.service.spec.ts`, copy `it('update sends the location version', …)` to `it('setAllowNegativeStock puts code, name, version and the flag', …)`: expect method `PUT`, URL `/api/locations/l1`, and body `{ code, name, version, allowNegativeStock: true }`.

## Do not
- Touch files outside `files`. If you must, stop and report why.
- Add npm packages, abstractions or config.
- Create or switch branches, commit, or push.

## Done when
```bash
cd /home/apa/Code/portfolio/inventory-management-system/web
npm run lint && npm run build && npm test -- --watch=false
grep -n "negative-balances-exist" src/app/core/problems/problem-messages.ts
```
