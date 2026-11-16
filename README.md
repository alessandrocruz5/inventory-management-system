# inventory-management-system
General Inventory Management System with AI capabilities

## End-to-end tests
Playwright specs in `tests/e2e` cover the stock flow (T14: receive, issue, reverse), the `/admin/ledger` page as Admin and Staff, and the post-stock form at 375 px. CI's `e2e` job runs them on every PR.

**Prerequisites:** the .NET SDK from `global.json`, Node 26, and Docker on an **x64** host. SQL Server 2025 has no ARM64 image, so the stack does not run on Apple-silicon Macs.

**Password:** the specs sign in with the AppHost's persisted `seed-password`, because the seed only creates missing users. Export it without printing it:

```bash
export E2E_PASSWORD="$(dotnet user-secrets list --project src/AppHost | sed -n 's/^Parameters:seed-password = //p')"
```

**Run:**

```bash
cd tests/e2e && npm ci && npx playwright install chromium && npx playwright test
```

The suite reuses a running AppHost, or starts one (`--launch-profile http`).

**Side effects on a reused database:** every run adds `E2E-…` locations holding `CANDLE-WHT` stock and rebuilds the ledger on the demo tenant. Nothing can be hard-deleted.

**Coupling:** renaming the AppHost's `seed-password` parameter, the `web` port 4200, the seeded user emails, or a UI label the specs use will break the suite.
