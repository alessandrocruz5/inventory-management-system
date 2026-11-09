---
type: prompt
plan: "[[2026-11-09-s5c-posting-engine]]"
id: 06
unit: IMS-8
status: todo
model: opus
effort: high
depends_on: [05]
parallel_group: E
files:
  create:
    - tests/Inventory.IntegrationTests/PostingVectorTests.cs
    - tests/Inventory.IntegrationTests/PostingConcurrencyTests.cs
    - tests/Inventory.IntegrationTests/PostingClockTests.cs
  modify:
    - tests/Inventory.IntegrationTests/Inventory.IntegrationTests.csproj
    - tests/Inventory.IntegrationTests/ApiFactory.cs
  delete: []
---
# 06 · T1 set R, T2, T4, T10

## Task
Write the named S5c tests from spec §11 against the real API, plus the package and factory hook they need:
- T1: the API run of vector set R.
- T2: 20 parallel issues of the last unit.
- T4: idempotency, including a deterministic race.
- T10: a fixed clock.

Change only test code.

## Context
- **Repo and branch:** `/home/apa/Code/portfolio/inventory-management-system`, branch `IMS-8`. Don't switch it.
- **Prerequisite:** run `export DOTNET_SYSTEM_NET_DISABLEIPV6=1` first.
- **Routes from prompts 04–05:** `POST /api/documents/receipts` and `/issues`. Both need an `Idempotency-Key` header.
  - `201`: `{ id, type, reference, notes, postedAt, postedBy, movements[{id, lineNo, itemId, fromLocationId, toLocationId, quantity, totalCost, reversesMovementId}], balances[{itemId, locationId, onHand, stockValue, lastUnitCost}], balancesAsOf }`.
  - Replay: `200` + `Idempotent-Replayed: true`, same body shape.
  - Errors: `409 /problems/idempotency-key-reused` (`originalDocumentId`), `422 /problems/insufficient-stock`.
- **Helpers:** reuse the existing ones; don't copy them or add a helper file. Use `DatabaseFixture.SignInAsync`, `LocationsTests.CreateAsync`, `ItemsTests.CreateAsync` and `PostingTests.PostDocumentAsync(client, route, key, body, ct)` (all `internal static`, from prompt 04).
- **Fixture:**
  - `DatabaseFixture` (`AppConnectionString`, `CreateApp()`, `CreateTenantWithUserAsync`, `SignInAsync`).
  - `LedgerRows.OpenAsync(db, tenant, ct)`: a raw `inventory_app` connection with the tenant's session context.
  - `LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx)`: polls until a request is blocked by the holder's session.
  - `LedgerRows.LocationIdAsync(c, "SUP", ct)`.
  - Every test creates its own tenant (spec §11, test isolation).
- **Internals:** test code can call `Inventory.Features.Posting.RequestHash` and `PostingValidation` (`InternalsVisibleTo`).

### Package and factory
- `Inventory.IntegrationTests.csproj`: add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="…" />`. It's on the CLAUDE.md allowed list. Use the latest stable 10.x: check with `dotnet package search Microsoft.Extensions.TimeProvider.Testing --exact-match`, and pin it exactly, as the other references are.
- `ApiFactory.cs`: add `internal TimeProvider? Clock { get; init; }`. In `ConfigureWebHost`, if `Clock` is set, `builder.ConfigureTestServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton(Clock); })` (`Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.DependencyInjection.Extensions`). Construct it as `new ApiFactory(db.AppConnectionString) { Clock = fake }`. Leave the existing constructor and `NewClient` unchanged.

### `PostingVectorTests.cs`: T1, API run of set R (spec §7)
One `[Fact]` on location C, posting each step in order and asserting the movement's `totalCost` and C's balance to exactly 4 dp. Compare decimals with `GetValue<decimal>()`.

| Step | Action | Movement cost | C after |
|---|---|---|---|
| R1 | Receive 1 @ 10.00 | 10.0000 | 1 / 10.0000 |
| R2 | Receive 2 @ 10.01 | 20.0200 | 3 / 30.0200 |
| R3 | Issue 1 | 10.0067 | 2 / 20.0133 |
| R4 | Issue 1 | 10.0067 (midpoint rounds away from zero) | 1 / 10.0066 |
| R5 | Issue 1 | 10.0066 (the remainder) | 0 / 0.0000 |

Then assert that the issued total, 30.0200, equals the received total.

### `PostingConcurrencyTests.cs`
- **T2, `Twenty_parallel_issues_of_the_last_unit`.**
  1. Receive 1.
  2. Start 20 issues of 1, each with its own key, with `Task.WhenAll` on one signed-in client. Start them together behind a `TaskCompletionSource` gate.
  3. Assert exactly one `201` and nineteen `422 insufficient-stock`; no `500` and no `409`.
  4. The final balance is 0 / 0.0000, and there is exactly one Issue document for the tenant.
- **T4a, `Same_key_and_body_sequentially_replays`.** `201`, then `200` + `Idempotent-Replayed: true` with the same `id`. Exactly one document holds that key.
- **T4b, `Same_key_and_body_in_parallel_gives_one_document`.** 10 parallel identical receipts with the same key. Exactly one `201` and nine `200`, with no `409`, `422` or `500`, and all ten share one `id`. One document.
- **T4c, `Two_parallel_same_key_issues_of_the_last_unit`.**
  1. Receive 1.
  2. Fire two identical issues with key K in parallel.
  3. Assert exactly one `201` and one `200` replay, **never a `422`**. That's the spec audit's BLOCK case (document-first at step 4).
  4. The balance is 0.
- **T4d, `Same_key_different_body_is_409`.** It returns `originalDocumentId`, which equals the first document's id.
- **T4e, `Race_on_the_key_replays_when_the_holder_commits`** (deterministic step-4 race):
  1. Compute the hash the API will compute: build the `PostReceiptRequest`, call `PostingValidation.Receipt(request, errors)`, then `RequestHash.Receipt(validated)`.
  2. On a raw `inventory_app` connection (`LedgerRows.OpenAsync`), begin a transaction and `INSERT` a `StockDocuments` row with that `IdempotencyKey` and `RequestHash` (`Type 'Receipt'`, an explicit `Id`, `PostedAt`, `PostedBy`). Write the `INSERT` inline in the test. `LedgerRows.AddDocumentAsync` (`LedgerRows.cs:64`) hashes the key rather than taking a hash, and `LedgerRows.cs` isn't in your files. Don't commit yet.
  3. Send the receipt with key K on a background task. It passes the step-2 lookup, because RCSI hides the uncommitted row, and then blocks on the unique index at step 4.
  4. `await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx)`. Then commit the holder.
  5. Assert that the request returns `200` + `Idempotent-Replayed: true`, and that `id` equals the holder's document id.
- **T4f, `Race_on_the_key_proceeds_when_the_holder_rolls_back`.** The same setup, but roll the holder back. The request returns `201` with a new `id`, and its movement and balance exist.
- Parallel same-key reversals (also T4 in spec §11) land in S9. Add a one-line comment saying so.
- **`Deactivation_committed_mid_posting_is_seen_at_step_7`** (spec §6 hard rules: "one that starts later sees the change at step 7"). This proves the engine's `READCOMMITTEDLOCK` re-read. The inactive tests in 04 and 05 stop at the step-1 snapshot check and never reach it.
  1. As the tenant, create location L and item X, and receive 1 of X into L. Both balance rows now exist, so step 5 of the next posting inserts nothing and takes no FK lock on `Items`.
  2. On a raw `inventory_app` connection (`LedgerRows.OpenAsync`), begin a transaction and run `UPDATE inventory.Items SET IsActive = 0 WHERE Id = @x` inline. Don't commit. `inventory_app` has `UPDATE` on `Items`.
  3. Send a receipt of 1 X into L with a new key K on a background task. It passes step 1, because RCSI shows the item as active, and then blocks at step 7 on the holder's X lock.
  4. `await LedgerRows.WaitUntilBlockedAsync(db, holder, ct, tx)`, then commit the holder.
  5. Assert `422 /problems/inactive-item` with `itemId` = X. No `StockDocuments` row holds K, and L's balance is still 1.
- **`Deactivation_rolled_back_mid_posting_lets_it_post`.** The same setup, but roll the holder back. The receipt returns `201`, and L's balance is 2.
- If the request isn't blocked within `WaitUntilBlockedAsync`'s 10 s, the engine's step 7 isn't using `READCOMMITTEDLOCK`. Stop and report it; don't change production code.

### `PostingClockTests.cs`: T10
- **`PostedAt_comes_from_the_clock`.**
  1. `var fake = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 6, 14, 0, TimeSpan.Zero));`, then `await using var app = new ApiFactory(db.AppConnectionString) { Clock = fake };`.
  2. Post a receipt whose JSON body **also** carries `"postedAt": "2020-01-01T00:00:00Z"`. Expect a `201`, with `postedAt` and `balancesAsOf` equal to the fake time.
  3. In the database, the document's `PostedAt` and every movement's `PostedAt` equal it too.
  4. `fake.Advance(TimeSpan.FromMinutes(5))`, then post an issue → its `postedAt` is 5 minutes later.
- **`No_request_type_carries_a_date`.** Use reflection over the Inventory assembly (`typeof(Inventory.InventoryModuleExtensions).Assembly`). Find every type in a namespace starting with `Inventory.Features` whose name ends with `Request` or `Line`. Assert that none has a property of type `DateTime`, `DateTimeOffset`, `DateOnly` or `TimeOnly`, nullable or not. Also assert that the set includes `PostReceiptRequest` and `PostIssueRequest`, so the scan can't pass vacuously.

## Do not
- Touch files outside the `files` list. Don't change production code; if a test exposes a bug, stop and report it with the failing output.
- Add any package other than `Microsoft.Extensions.TimeProvider.Testing`.
- Use `Task.Delay` for synchronization. Use `WaitUntilBlockedAsync` and gates.
- Create or switch branches, commit, or push.

## Done when
```bash
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test            # if Testcontainers crashes at start-up: dotnet test --max-parallel-test-modules 1
for i in 1 2 3; do dotnet test --project tests/Inventory.IntegrationTests || break; done   # the concurrency tests pass 3 runs in a row
cd web && npm run lint && npm run build && npm test -- --watch=false
```
- T1 R, T2, T4a–f, both step-7 race tests and T10 pass, and so does every earlier test.
