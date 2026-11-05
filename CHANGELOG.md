# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Sprint 1 ends at version 0.1.0.

## [Unreleased]

### Added

- Skeleton: Aspire AppHost (`sql`, `api`, `web`), ServiceDefaults, five module projects, Angular app, and architecture tests (T12) (IMS-2).
- Platform: Identity cookie auth (login, logout, me), tenants, `Api migrate` and `Api seed` modes, tenant middleware, and Angular login, guard and shell (IMS-3).
- Platform: AppHost ordering `sql` → `migrate` → `seed` → `api` → `web`, and a T12 rule against `Migrate*` calls outside the migrate step (IMS-3).
- Inventory: `Locations` and `ProvisionTenantAsync` (IMS-3).
- Tenancy: `TenantId` column default and save interceptor, named `Tenant` query filter, and row-level security on `security.TenantPolicy` with a session-context interceptor (IMS-4).
- Tenancy: `Locations` is tenant-owned, with route guard (T5), catalog (T6), isolation (T11) and rollback tests, plus a T12 rule against bare `IgnoreQueryFilters()` (IMS-4).
