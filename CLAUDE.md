# CLAUDE.md - Gatehouse

Multi-tenant physical access governance over existing PACS: request access on a map → owners approve → connector agents apply → live building view. Governance layer, not access control (controllers still decide open/deny).

- Spec `docs/SPEC.md` (read only sections a task names). Decisions `docs/adr/`. Contracts `docs/contracts/`. Code map `docs/codemap.md`.
- Coding rules beyond this file: `docs/conventions.md` (binding for every change; physical-access invariants in §4).
- Precedence: contract > code; ADR/contract > spec.
- Existing code: see `docs/codemap.md`.

## Tech baseline

.NET 10, C# latest, Minimal APIs, EF Core 10 + Npgsql, FluentValidation, Serilog; PostgreSQL (catalog + DB per tenant, ADR-001), Redis, RabbitMQ + **MassTransit 8.x** (ADR-005), Keycloak **26.x** (ADR-002), SignalR, .NET Aspire. Web: Next.js 16 App Router, TS strict, Tailwind v4, shadcn/ui, TanStack Query, Framer Motion, Node 24, npm. Tests: xUnit v3, Testcontainers, `Aspire.Hosting.Testing`, `FakeTimeProvider`, Vitest + Testing Library, Playwright.
**Never add:** MediatR, AutoMapper, FluentAssertions, MassTransit 9, Moq (use fakes/NSubstitute), Newtonsoft.Json, other ORMs. Versions only in `Directory.Packages.props` (no `Version` in `.csproj`).

## Commands (repo root; Docker needed for integration/e2e)

| What | Command |
|---|---|
| Tools | `dotnet tool restore` |
| Build | `dotnet build Gatehouse.slnx -nologo -v q -clp:ErrorsOnly` |
| Format check | `dotnet format Gatehouse.slnx --verify-no-changes` |
| Unit | `dotnet test tests/Gatehouse.UnitTests --nologo -v q --logger "console;verbosity=minimal"` |
| Integration (no AppHost) | same with `tests/Gatehouse.IntegrationTests --filter "Category!=AppHost"` |
| Integration, one module | same with `--filter "Category!=AppHost&FullyQualifiedName~Gatehouse.IntegrationTests.<Module>"` |
| AppHost smoke | same with `--filter "Category=AppHost"` (background) |
| One test | add `--filter "FullyQualifiedName~XTests"` |
| Contract (M4+) | `dotnet test tests/Gatehouse.ContractTests` |
| Run system | `dotnet run --project src/Gatehouse.AppHost` (background; web http://localhost:3000; stop when done) |
| Reset Keycloak (after editing `infra/keycloak/`) | stop the AppHost, `docker volume rm gatehouse-keycloak-data gatehouse-upstream-idp-data`, then in both tenant DBs `update persons set identity_subject = null` |
| CLI | `dotnet run --project src/Gatehouse.Cli -- <cmd>` e.g. `migrate --all` |
| Tenant migration | `dotnet ef migrations add <Name> --project src/Gatehouse.Infrastructure --context TenantDbContext --output-dir Persistence/Tenant/Migrations` |
| Catalog migration | same with `--context CatalogDbContext --output-dir Persistence/Catalog/Migrations` |
| Web | `cd src/web && npm ci && npm run --silent lint && npm run --silent typecheck && npm test && npm run --silent build` (`lint` = ESLint + Prettier check) |
| Web format | `cd src/web && npm run --silent format` (write) / `npm run --silent format:check` |
| Coverage (local, no gate) | `bash tests/coverage.sh` (.NET unit + integration without AppHost + contract → `artifacts/coverage/Summary.txt`, HTML) / `cd src/web && npm run --silent test:coverage` (→ `artifacts/coverage/web`) |
| E2E (AppHost running) | `cd tests/e2e && npm ci && npx playwright test --reporter=line` |
| Demo password | `dotnet user-secrets list --project src/Gatehouse.AppHost` (`Parameters:demo-user-password`) |

Never stop or modify Docker containers of other projects without the person's approval.

## C#

- `Directory.Build.props` fixes net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors, LangVersion latest, AnalysisLevel latest-recommended; never override per project.
- File-scoped namespace = folder path; one public type per file; `sealed` default; records for DTOs/commands/events/value objects; `_camelCase` fields, `Async` suffix, `I` interfaces.
- Folder-per-feature (ADR-004), modules: Tenancy, Identity, Sites, People, Access, Fulfillment, Credentials, Audit, Connectors, LiveBuilding, Reconciliation, Tasks, Theming.
  - Domain `src/Gatehouse.Domain/{Module}/{Entity}.cs`, events `{Module}/Events/`.
  - Application `src/Gatehouse.Application/{Module}/{UseCase}/{UseCase}Command|Query|Handler|Validator.cs`, DTOs `{Module}/Dtos/`, `{Module}Module.cs` with `Add{Module}Module` registering handlers explicitly.
  - Api `src/Gatehouse.Api/Features/{Module}/{Module}Endpoints.cs` → `Map{Module}Endpoints`.
  - Infrastructure `Persistence/{Tenant|Catalog}/Configurations/{Entity}Configuration.cs`, `Tenancy/`, `Security/`, `Messaging/`.
- Handler: `public sealed class XHandler(deps) { Task<Result<TOut>> HandleAsync(XCommand, CancellationToken) }`; one per use case; handlers never call handlers.
- Expected failures → `Result`/`Error` with codes from `domain-model.md` §4. Exceptions only for bugs/infrastructure. `catch (Exception)` only at host boundaries.
- `ToHttpResult()`: Validation 400 (+`errors`), NotFound 404, Conflict 409, Forbidden 403, Unauthorized 401, Failure 500; RFC 9457 problem+json with `code`, `traceId`.
- FluentValidation for request shape; invariants in domain.
- Time via `TimeProvider`; domain methods take `DateTimeOffset now`; never `DateTime(Offset).UtcNow/Now` in Domain/Application. Ids `XId.New()` (`Guid.CreateVersion7()`). Async + `CancellationToken` everywhere; no `.Result/.Wait()`.
- EF: `IEntityTypeConfiguration<T>`, snake_case, enums as strings, no lazy loading, `AsNoTracking()` + `Select` projections; app code uses `ITenantDb`.
- JSON: System.Text.Json, camelCase, `JsonStringEnumConverter`.

## TypeScript / web

- strict + `noUncheckedIndexedAccess`; no `any`; named exports (except Next route files); files kebab-case, components PascalCase, hooks `useXxx`.
- Features `src/web/features/{feature}/` (`components/`, `hooks/`, `api.ts`, `types.ts`); shared UI `src/web/components/ui/`.
- Server Components by default; client fetches via TanStack Query to `/api/proxy/...` (BFF); browser never sees tokens.
- DTO types in `src/web/lib/api/types.ts` match C# records field-for-field.
- Map states/icons/labels/colors only from `src/web/features/map/access-state.ts` + fixed `--gh-state-*` vars (ADR-007).
- Vitest files `*.test.ts(x)` beside code; prefer `getByRole`/`getByTestId`.

## Logging

- Serilog via ServiceDefaults; structured templates only, never interpolation.
- Never log PINs, passwords, tokens, cookies, connection strings, client secrets, full card numbers, photos, `Authorization` headers. Use `[Sensitive]` (→ `"***"`), `Pin`/`SensitivePin` (→ `****`), `CardNumberMask.Mask()`.
- Logging payloads ⇒ extend `LogRedactionTests`.
- Information = business milestones, Warning = recoverable, Error = needs attention. `TenantId` via enricher.

## Tenancy (every query, every message)

1. Tenant only from `tenant_id` claim (users), connector certificate (agents), `gh-tenant-id` header (consumers). Never from route/query/body/custom header.
2. Tenant data only via `TenantDbContext`/`ITenantDb` bound to `ITenantContext`; no hand-built tenant connection strings outside Cli/tests.
3. Background work: loop `ICatalogReader.GetActiveTenantsAsync()` → `ITenantScopeFactory.CreateScopeAsync(tenantId)`; never share a `TenantDbContext` across tenants.
4. Lists over people/entitlements use `VisibleTo(ICurrentUser)`/`AwaitingDecisionBy` (`authorization.md` §5); single resources use §4 requirements.
5. Redis keys and SignalR groups start `tenant:{tenantId}:`; every message has `gh-tenant-id`.
6. Every new read endpoint gets a cross-tenant 404 test in `tests/Gatehouse.IntegrationTests/Tenancy/TenantIsolationTests.cs`.
7. No self-approval, no admin-implies-approver (`authorization.md`).

## Tests

- Unit: Domain + Application, no I/O, folders mirror source, fakes over mocks.
- Integration: Testcontainers, `GatehouseApiFactory : WebApplicationFactory<Program>`, `TestAuth` headers `X-Test-Sub`/`X-Test-Email`/`X-Test-Tenant`; full-Aspire tests `[Trait("Category","AppHost")]`. `PostgresFixture` is an assembly fixture (no `[Collection]`): classes run in parallel, every factory gets its own catalog + tenant databases cloned from a seeded template (`DemoTemplate`), other tests use `PostgresFixture.UniqueName` databases. Never share or restore tenant data in `DisposeAsync`; never use fixed tenant database names or fixed ports. Containers are reused across runs (`gatehouse-test-*`, kept running; `docker rm -f` them to reset).
- Contract: `adapter-interface.md` §4. E2E: Playwright TS, one spec per area, `GATEHOUSE_WEB_URL`, `GATEHOUSE_DEMO_PASSWORD`.
- Class `{Subject}Tests`, method `{Behaviour}_{condition}_{expected}`.
- xUnit `Assert` only; each test arranges its own data.
