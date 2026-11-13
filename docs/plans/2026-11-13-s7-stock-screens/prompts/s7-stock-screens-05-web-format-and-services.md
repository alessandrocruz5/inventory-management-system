---
type: prompt
plan: "[[2026-11-13-s7-stock-screens]]"
id: 05
unit: IMS-11
status: todo
model: sonnet
effort: medium
depends_on: [04]
parallel_group: E
files:
  create:
    - web/src/app/shared/format.ts
    - web/src/app/shared/format.spec.ts
    - web/src/app/stock/stock.service.ts
    - web/src/app/stock/stock.service.spec.ts
    - web/src/app/documents/documents.service.ts
    - web/src/app/documents/documents.service.spec.ts
  modify: []
  delete: []
---
# 05 · Web formatting and API services

## Task
Add the shared display formatters (money, quantity, Manila dates, a Manila day range, and labels) and the two HTTP services, `StockService` and `DocumentsService`, with Vitest specs. This is the formatting part of T13.

## Context
- Repo: `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-11`. Angular 22 (standalone, signals, zoneless), Vitest through `ng test`, TypeScript ~6.0. No new npm package (CLAUDE.md allowed list).
- Copy the service pattern from `web/src/app/items/items.service.ts:1-67`: `@Injectable({ providedIn: 'root' })`, `inject(HttpClient)`, `HttpParams`, `firstValueFrom`, `Paged<T>` from `web/src/app/core/api/paged.ts`. Copy the spec pattern from `web/src/app/items/items.service.spec.ts:1-40` (`provideHttpClient()`, `provideHttpClientTesting()`, `HttpTestingController`, `httpMock.verify()` in `afterEach`, `vi`/`describe`/`it`/`expect` globals).
- **API contract (plan D6), exactly:**
  - `GET /api/stock?itemId&locationId&page&pageSize` → `{ items: StockRow[]; page; pageSize; total; totals: { stockValue: number } }`. `StockRow = { itemId; sku; itemName; baseUnit; locationId; locationCode; locationName; onHand: number; stockValue: number; averageCost: number | null; lastUnitCost: number | null }`.
  - `GET /api/documents?type&postedFrom&postedTo&page&pageSize` → `Paged<DocumentSummary>`. `DocumentSummary = { id; type: DocumentType; reasonCode: AdjustmentReason | null; reference: string | null; postedAt: string; postedBy: string; lineCount: number; reversesDocumentId: string | null; reversedByDocumentId: string | null }`.
  - `GET /api/documents/{id}` → `DocumentDetail = { id; type; reasonCode; reference; notes: string | null; postedAt; postedBy; reversesDocumentId; reversedByDocumentId; lines: DocumentLine[] }`. `DocumentLine = { movementId: number; lineNo; itemId; sku; itemName; baseUnit; fromLocationId; fromLocationCode; fromLocationName; toLocationId; toLocationCode; toLocationName; quantity: number; unitCost: number | null; totalCost: number; reversesMovementId: number | null }`.
  - Posting (IMS-10 plan D3 bodies; the `Idempotency-Key` header is required): `POST /api/documents/receipts` `{ locationId, reference, notes, lines: [{ itemId, quantity, unitCost }] }`; `/issues` `{ locationId, reference, notes, lines: [{ itemId, quantity }] }`; `/transfers` `{ fromLocationId, toLocationId, reference, notes, lines: [{ itemId, quantity }] }`; `/adjustments` `{ locationId, reasonCode, reference, notes, lines: [{ itemId, quantityDelta, unitCost }] }`. The `201` (or `200` replay) body has at least `{ id: string; type; … }`.
- **`web/src/app/shared/format.ts`**, pure exported functions (use `formatCurrency`, `formatNumber` and `formatDate` from `@angular/common` with locale `'en-US'`, which is built in):
  - `formatMoney(value: number): string` → `formatCurrency(value, 'en-US', '₱', 'PHP', '1.2-2')`. `1234.5` → `₱1,234.50`; `0` → `₱0.00`. Angular rounds on the decimal digits, half away from zero, matching CLAUDE.md's AwayFromZero (spec: test `1234.505` → `₱1,234.51` and `2.345` → `₱2.35`).
  - `formatQuantity(value: number): string` → `formatNumber(value, 'en-US', '1.0-4')`. `10` → `10`; `1.25` → `1.25`; `1234.5` → `1,234.5`; `0.0001` → `0.0001`. Spec §10: "10, not 10.0000".
  - `formatManilaDateTime(iso: string): string` → `formatDate(iso, 'd MMM y, h:mm a', 'en-US', '+0800')`. `'2026-10-30T06:14:00Z'` → `30 Oct 2026, 2:14 PM`. Asia/Manila has no DST, so `+0800` is fixed.
  - `manilaDayRange(from: string | null, to: string | null): { postedFrom?: string; postedTo?: string }`. The inputs are `yyyy-mm-dd` Manila calendar days from `<input type="date">`; `to` is inclusive. Output is UTC `Z` instants: `postedFrom` = from-day 00:00 +08:00, and `postedTo` = (to-day + 1) 00:00 +08:00 (half-open). Compute with `Date.UTC(y, m - 1, d) - 8 * 3_600_000` and `new Date(ms).toISOString()`. **Never emit `+08:00`**: Angular's `HttpParams` leaves `+` unencoded and the server would read a space (plan D8). Example: `manilaDayRange('2026-10-30', '2026-10-30')` → `{ postedFrom: '2026-10-29T16:00:00.000Z', postedTo: '2026-10-30T16:00:00.000Z' }`. Omit a key whose input is null/empty.
  - `export type DocumentType = 'Receipt' | 'Issue' | 'Transfer' | 'Adjustment' | 'Reversal'` and `documentTypeLabel(type)`: Receipt → `Received`, Issue → `Used or sold`, Transfer → `Moved`, Adjustment → `Corrected`, Reversal → `Reversal`.
  - `export type AdjustmentReason = 'Damaged' | 'Lost' | 'Found' | 'CountCorrection' | 'Other'`, `export const ADJUSTMENT_REASONS: readonly AdjustmentReason[]` in that order, and `reasonLabel(reason)`: CountCorrection → `Count correction`, every other reason → its own name.
  - `export const SYSTEM_USER_ID = '00000000-0000-0000-0000-000000000000'` and `postedByLabel(postedBy: string, currentUserId: string | null | undefined): string`: `System` when it equals `SYSTEM_USER_ID` (S5d seed postings), `You` when it equals `currentUserId` (case-insensitive), else `Another user` (plan D3).
- **`web/src/app/stock/stock.service.ts`**: export the `StockRow`, `StockTotals` and `StockPage` (`extends Paged<StockRow>` plus `totals`) interfaces, and `StockQuery { itemId?: string; locationId?: string; page: number; pageSize: number }`. `StockService.list(query): Promise<StockPage>` sets only the present params. `StockService.onHand(itemId, locationId): Promise<number>` calls `list({ itemId, locationId, page: 1, pageSize: 1 })` and returns `items[0]?.onHand ?? 0`.
- **`web/src/app/documents/documents.service.ts`**:
  - Export `PostingType = 'receipt' | 'issue' | 'transfer' | 'adjustment'`, `POSTING_TYPES: readonly PostingType[]`, and `POSTING_ROUTES: Record<PostingType, string>` (`/api/documents/receipts`, `/issues`, `/transfers`, `/adjustments`).
  - Export the body interfaces `ReceiptBody`, `IssueBody`, `TransferBody`, `AdjustmentBody` (shapes above; `reference`/`notes` are `string | null`; adjustment `unitCost: number | null`), `PostingBody` as their union, `PostedDocument { id: string; type: DocumentType }`, `DocumentSummary`, `DocumentDetail`, `DocumentLine`, and `DocumentQuery { type?: DocumentType; postedFrom?: string; postedTo?: string; page: number; pageSize: number }`. Import `DocumentType`/`AdjustmentReason` from `../shared/format`.
  - `list(query): Promise<Paged<DocumentSummary>>` sets only the present params. `get(id): Promise<DocumentDetail>` (`encodeURIComponent(id)`).
  - `post(type: PostingType, idempotencyKey: string, body: PostingBody): Promise<PostedDocument>` → `this.http.post<PostedDocument>(POSTING_ROUTES[type], body, { headers: { 'Idempotency-Key': idempotencyKey } })`. Treat a `200` replay the same as a `201` (HttpClient resolves both).
- **Specs** (`format.spec.ts`, `stock.service.spec.ts`, `documents.service.spec.ts`):
  - Every example above for the formatters, including the `1234.505`/`2.345` rounding, negative money (`-5` → `-₱5.00`), `manilaDayRange` with only `from`, only `to`, both, and neither, and a month/year rollover (`'2026-12-31'` to → `postedTo: '2026-12-31T16:00:00.000Z'`). Also `postedByLabel` for all three cases, including an upper-case current id.
  - For the services: the URL, the method and the params set (and the ones *absent* when undefined). `post` sends the exact body and the `Idempotency-Key` header to each of the four routes. `onHand` returns `0` for an empty page.
- No component in this prompt. Nothing imports these yet; prompts 06–09 do.

## Do not
- Use `+08:00` in any value sent to the API. Use `double` math for money beyond display, or add a currency or date library.
- Touch files outside the `files` list. If you must, stop and report why.
- Add dependencies, abstractions or config that aren't listed here.
- Create or switch branches, commit, or push.

## Done when
```bash
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- `format.spec.ts`, `stock.service.spec.ts` and `documents.service.spec.ts` pass, and every existing spec passes unmodified.
