# Authorization contract

Roles, scopes, policies, requirements and handlers. Spec §3 (roles = role + scope, hard rules). Decisions: ADR-002 (Keycloak asserts identity + tenant only; roles live in the tenant DB).

## 1. Identity and current user

- Authentication: JWT bearer from Keycloak realm `gatehouse` (authority `http://localhost:8080/realms/gatehouse`, audience `gatehouse-api`). Claims used: `sub`, `email`, `given_name`, `family_name`, `tenant_id`.
- `TenantResolutionMiddleware` (runs after authentication) sets `ITenantContext` from `tenant_id` (ADR-001). Failures: `403 tenant.missing_claim`, `403 tenant.unknown`, `403 tenant.inactive`.
- `ICurrentUser` (scoped, `Gatehouse.Application.Identity`), resolved lazily by `CurrentUserAccessor`:

```csharp
public interface ICurrentUser
{
    TenantId TenantId { get; }
    string Subject { get; }
    string Email { get; }
    PersonId? PersonId { get; }                    // null until linked
    bool IsTenantAdmin { get; }
    bool IsSecurityStaff { get; }
    IReadOnlySet<SiteId>? SecurityStaffSiteIds { get; }  // null = tenant-wide (only meaningful if IsSecurityStaff)
    Task<PersonId> RequirePersonIdAsync(CancellationToken ct); // fails identity.person_not_provisioned
}
```

- **Person linking:** on the first request, find `Person` by `IdentitySubject == sub`; if none, by `Email == lower(email)` with `IdentitySubject == null`, then set `IdentitySubject = sub` and save. No match → `PersonId` stays `null`; endpoints that need a person return `403 identity.person_not_provisioned`.

## 2. Role + scope matrix

Derived roles come from data (see `domain-model.md` §2.2); assigned roles from `RoleAssignment`.

| Role | Source | Scope | Can | Cannot |
|---|---|---|---|---|
| Employee | `Person.Type == Employee` | self | view map, request for self, activate own card, set PIN, upload photo | approve anything by this role |
| Manager | derived (has reports) | team = persons with `ManagerId == me` | request for team, onboard externals (as sponsor), activate team cards, approve `Manager` steps assigned to them | approve requests they made or that are for themselves |
| Sponsor | derived (has sponsored) | persons with `SponsorId == me` | set/end sponsorship, request for and activate cards of sponsored people | exceed max sponsorship duration |
| External / Visitor / Guest | `Person.Type` | self within sponsorship | request for self, photo, PIN | activate own card |
| Area owner | derived (`Area.Owners`) | owned areas | approve `AreaOwner` steps of owned areas, view roster, revoke in owned areas, access reviews | act outside owned areas, disable people tenant-wide |
| Security staff | `RoleAssignment(SecurityStaff, SiteId?)` | tenant, or listed sites | fulfillment exceptions, emergency revoke, task-scoped PIN reveal, reconciliation findings, door commands | approve requests by role |
| Tenant admin | `RoleAssignment(TenantAdmin)` | tenant | people, sites, levels, theme, settings, connectors, read all requests | approve or revoke area access by role |

**Hard rules (each has a test):**
1. **No self-approval:** the decider of a step is never the subject nor the requester (`NoSelfApprovalHandler` + domain check).
2. **Approval rights ≠ admin rights:** `TenantAdmin` and `SecurityStaff` never satisfy `ApproveStepRequirement` or `RevokeEntitlementRequirement`. `TenantAdmin` never satisfies `EmergencyRevokeRequirement`, `HandleFulfillmentRequirement` or `HandleStaffTaskRequirement`.
3. **Tenant + scope on every query:** the tenant is physical (ADR-001); scope is applied by the `VisibleTo(ICurrentUser)` / `HandledBy` query extensions in §5.
4. **No self-fulfillment (M3):** a security staff member never starts, confirms, fails or retries a fulfillment of an entitlement for which they are the subject or the requester (`NoSelfFulfillmentHandler`).

## 3. Policies

Names are constants in `Gatehouse.Application.Authorization.Policies`. Registered in `AuthorizationModule.AddGatehouseAuthorization(IServiceCollection)`.

| Policy constant | Value | Requirements |
|---|---|---|
| `Policies.TenantMember` | `"TenantMember"` | authenticated + `TenantMemberRequirement` (tenant resolved and active) |
| `Policies.LinkedPerson` | `"LinkedPerson"` | `TenantMember` + `LinkedPersonRequirement` (current user has an active Person) |
| `Policies.TenantAdmin` | `"TenantAdmin"` | `LinkedPerson` + `TenantAdminRequirement` |
| `Policies.SecurityStaff` | `"SecurityStaff"` | `LinkedPerson` + `SecurityStaffRequirement` (any scope) |
| `Policies.StaffConsole` | `"StaffConsole"` | `LinkedPerson` + `StaffConsoleRequirement` (security staff of any scope **or** tenant admin; read-only for the admin) |
| `Policies.AuditReader` | `"AuditReader"` | `LinkedPerson` + `AuditReaderRequirement` (tenant admin **or** tenant-wide security staff) |

The fallback policy is `TenantMember`: every endpoint requires it unless marked `AllowAnonymous` (only `/health`, `/alive`, `/openapi/*` in Development).

## 4. Resource-based requirements and handlers

Checked in handlers/endpoints with `IAuthorizationService.AuthorizeAsync(user, resource, requirement)`. ASP.NET semantics: the requirement succeeds if **any** handler calls `Succeed` and **no** handler calls `Fail`. `NoSelfApprovalHandler` uses `Fail`.

Requirements live in `Gatehouse.Application.Authorization.Requirements`, handlers in `Gatehouse.Application.Authorization.Handlers`.

| Requirement | Resource type | Handlers that can succeed | Handlers that fail | Milestone |
|---|---|---|---|---|
| `RequestAccessRequirement` | `PersonResource` (subject) | `SelfHandler` (subject == me), `TeamManagerHandler` (subject.ManagerId == me), `SponsorHandler` (subject.SponsorId == me) | - | M2 |
| `ApproveStepRequirement` | `ApprovalStepResource` | `TeamManagerHandler` (step `Manager` and `AssignedApproverId == me`), `AreaOwnerHandler` (step `AreaOwner` and me ∈ area owners) | `NoSelfApprovalHandler` (me == subject or me == requester) | M2 |
| `ViewEntitlementRequirement` | `EntitlementResource` | `SelfHandler` (subject or requester), `TeamManagerHandler`, `SponsorHandler`, `AreaOwnerHandler`, `SecurityStaffScopeHandler`, `TenantAdminHandler` | - | M2 |
| `RevokeEntitlementRequirement` | `EntitlementResource` | `AreaOwnerHandler` | - | M3 |
| `EmergencyRevokeRequirement` | `EntitlementResource` | `SecurityStaffScopeHandler` | - | M3 |
| `HandleFulfillmentRequirement` | `FulfillmentResource` | `SecurityStaffScopeHandler` (fulfillment's site in scope) | `NoSelfFulfillmentHandler` (me == subject or me == requester) | M3 |
| `HandleStaffTaskRequirement` | `StaffTaskResource` | `SecurityStaffScopeHandler` (task `SiteId` in scope; `SiteId == null` ⇒ tenant-wide staff only) | - | M3 |
| `StaffConsoleRequirement` | none | `SecurityStaffScopeHandler` (any scope), `TenantAdminHandler` | - | M3 |
| `AuditReaderRequirement` | none | `TenantAdminHandler`, `SecurityStaffScopeHandler` (only when `SecurityStaffSiteIds == null`) | - | M3 |
| `ActivateCardRequirement` | `PersonResource` | `SelfEmployeeHandler` (subject == me and `Type == Employee`), `TeamManagerHandler`, `SponsorHandler` | - | M8 |
| `ManagePersonRequirement` | `PersonResource` | `TenantAdminHandler`, `SponsorHandler`, `TeamManagerHandler` | - | M8 |
| `ViewAreaRosterRequirement` | `AreaResource` | `AreaOwnerHandler`, `SecurityStaffScopeHandler` | - | M3 |
| `DoorCommandRequirement` | `SiteResource` | `SecurityStaffScopeHandler` | - | M5 |
| `RevealPinRequirement` | `StaffTaskResource` | `PinRevealTaskHandler` (task kind `PinReveal`, `AssignedToId == me`, status `InProgress`, and the task still in my current security-staff scope: GH-LIVE-03) | - | M8 |
| `ManageTenantRequirement` | none | `TenantAdminHandler` | - | M2 |

Resource records (`Gatehouse.Application.Authorization.Resources`), built by the calling handler from loaded data so authorization handlers never query the DB:

```csharp
public sealed record PersonResource(PersonId PersonId, PersonType Type, PersonId? ManagerId, PersonId? SponsorId);
public sealed record AreaResource(AreaId AreaId, SiteId SiteId, IReadOnlySet<PersonId> OwnerIds);
public sealed record SiteResource(SiteId SiteId);
public sealed record EntitlementResource(EntitlementId EntitlementId, PersonResource Subject, PersonId RequestedById, AreaResource Area);
public sealed record ApprovalStepResource(EntitlementResource Entitlement, ApprovalStepId StepId, ApproverKind ApproverKind, PersonId? AssignedApproverId);
public sealed record FulfillmentResource(FulfillmentId FulfillmentId, SiteId SiteId, PersonId SubjectId, PersonId RequestedById);
public sealed record StaffTaskResource(StaffTaskId TaskId, StaffTaskKind Kind, SiteId? SiteId, PersonId? AssignedToId, StaffTaskStatus Status);
```

Handler behaviour summary (each handler handles several requirements by type switch):
- `SelfHandler`, `SelfEmployeeHandler`, `TeamManagerHandler`, `SponsorHandler`, `AreaOwnerHandler` compare `ICurrentUser.PersonId` with the resource fields.
- `SecurityStaffScopeHandler`: succeeds if `IsSecurityStaff` and (`SecurityStaffSiteIds == null` or contains the resource's site).
- `TenantAdminHandler`: succeeds if `IsTenantAdmin`. It is **never** registered for `ApproveStepRequirement` or `RevokeEntitlementRequirement`.
- `NoSelfApprovalHandler`: `Fail()` when me == subject or me == requester; otherwise does nothing.
- `NoSelfFulfillmentHandler` (M3): for `HandleFulfillmentRequirement`, `Fail()` when me == `SubjectId` or me == `RequestedById`; otherwise does nothing.
- Authorization failures map to `403` with code `auth.forbidden` unless the requirement has a specific code: `RequestAccessRequirement` → `request.forbidden_for_subject`, `ApproveStepRequirement` → `approval.self_approval_forbidden` if `NoSelfApprovalHandler` failed, else `approval.not_an_approver`, `RevokeEntitlementRequirement` → `entitlement.revoke_forbidden`, `HandleFulfillmentRequirement` → `fulfillment.self_handling_forbidden` if `NoSelfFulfillmentHandler` failed, else `404 fulfillment.not_found` (do not reveal out-of-scope work).

## 5. Query scoping (`Gatehouse.Application.Authorization.QueryScopes`)

Every list query over people or entitlements must go through one of these extension methods. Sites/floors/areas/levels are visible to every `TenantMember` (the map shows the whole tenant).

| Extension | Visible rows |
|---|---|
| `IQueryable<Person>.VisibleTo(ICurrentUser me)` | self; persons with `ManagerId == me` or `SponsorId == me`; persons with an entitlement in an area I own; all if TenantAdmin or SecurityStaff (site-scoped staff: persons with an entitlement on one of their sites) |
| `IQueryable<Entitlement>.VisibleTo(ICurrentUser me)` | subject or requester is me; subject is in my team or sponsored; area owned by me; all if TenantAdmin; site-scoped for SecurityStaff |
| `IQueryable<Fulfillment>.HandledBy(ICurrentUser me, entitlements, areas, floors)` (M3) | none unless security staff; all for tenant-wide staff; site-scoped staff: fulfillments whose entitlement's area's floor is on one of their sites. TenantAdmin gets **none** (hard rule 2) |
| `IQueryable<StaffTask>.VisibleTo(ICurrentUser me)` (M3) | all for TenantAdmin and tenant-wide staff; site-scoped staff: tasks with `SiteId` in their sites; others none |
| `IQueryable<Entitlement>.AwaitingDecisionBy(PersonId me)` | status `PendingApproval` whose `Pending` step is (`Manager` and `AssignedApproverId == me`) or (`AreaOwner` and me ∈ owners), **excluding** subject == me and requester == me |

## 6. Endpoint → policy map (M1-M2)

| Endpoint | Policy | Resource check |
|---|---|---|
| `GET /health`, `GET /alive` | anonymous | - |
| `GET /api/v1/me` | `TenantMember` | - |
| `GET /api/v1/sites` | `LinkedPerson` | - |
| `GET /api/v1/floors/{floorId}/access-map` | `LinkedPerson` | self only in M2 |
| `GET /api/v1/areas/{areaId}` | `LinkedPerson` | - |
| `POST /api/v1/access-requests` | `LinkedPerson` | `RequestAccessRequirement` on the subject |
| `GET /api/v1/access-requests` | `LinkedPerson` | `Entitlement.VisibleTo` |
| `GET /api/v1/access-requests/{entitlementId}` | `LinkedPerson` | `ViewEntitlementRequirement` |
| `GET /api/v1/approvals/inbox` | `LinkedPerson` | `AwaitingDecisionBy` |
| `POST /api/v1/approvals/{stepId}/approve` | `LinkedPerson` | `ApproveStepRequirement` |
| `POST /api/v1/approvals/{stepId}/reject` | `LinkedPerson` | `ApproveStepRequirement` |

Tests: `tests/Gatehouse.UnitTests/Authorization/*HandlerTests.cs` (one class per handler) and `tests/Gatehouse.IntegrationTests/Authorization/AuthorizationMatrixTests.cs` (one test per row of the M2 table).

## 7. Endpoint → policy map (M3)

| Endpoint | Policy | Resource check / scope | Error on failed resource check |
|---|---|---|---|
| `POST /api/v1/access-requests/{entitlementId}/cancel` | `LinkedPerson` | `ViewEntitlementRequirement`, then domain (requester only) | `404 entitlement.not_found`, `403 entitlement.cancel_forbidden` |
| `POST /api/v1/access-requests/{entitlementId}/revoke` | `LinkedPerson` | `ViewEntitlementRequirement`, `RevokeEntitlementRequirement` | `404 entitlement.not_found`, `403 entitlement.revoke_forbidden` |
| `POST /api/v1/access-requests/{entitlementId}/emergency-revoke` | `SecurityStaff` | `ViewEntitlementRequirement`, `EmergencyRevokeRequirement` | `404 entitlement.not_found`, `403 auth.forbidden` |
| `GET /api/v1/areas/{areaId}/roster` | `LinkedPerson` | `ViewAreaRosterRequirement` | `403 auth.forbidden` |
| `GET /api/v1/fulfillments?view=open\|done` | `SecurityStaff` | `HandledBy` | - |
| `GET /api/v1/fulfillments/{fulfillmentId}` | `SecurityStaff` | `HandledBy` | `404 fulfillment.not_found` |
| `POST /api/v1/fulfillments/{fulfillmentId}/start\|confirm\|fail\|retry` | `SecurityStaff` | `HandleFulfillmentRequirement` | `404 fulfillment.not_found`, `403 fulfillment.self_handling_forbidden` |
| `GET /api/v1/tasks?status=open\|resolved` | `StaffConsole` | `StaffTask.VisibleTo` | - |
| `POST /api/v1/tasks/{taskId}/claim`, `POST /api/v1/tasks/{taskId}/resolve` | `SecurityStaff` | `HandleStaffTaskRequirement` | `404 task.not_found`, `403 auth.forbidden` |
| `GET /api/v1/audit/verify` | `AuditReader` | - | - |
| `GET /api/v1/audit/entries?subjectType=&subjectId=&before=&limit=` | `AuditReader` | - | - |

Tests: handler unit tests per new rule (M3-05), endpoint tests per row (M3-06 … M3-12), and an isolation test per new read endpoint in `TenantIsolationTests`.

## 8. M4 additions

- `Gatehouse.ConnectorGateway` does not use user policies: every `/connector/v1/*` endpoint except `/enroll` requires the connector certificate scheme (`connector-protocol.md` §1); `/enroll` is authenticated by the one-time token. The connector context sets the tenant (CLAUDE.md tenancy rule 1).
- `POST /api/v1/dev/reset-demo-access?fulfillmentMode=Manual|Connector` (Development only): same policy as before (`TenantAdmin`); absent = seed default modes.
- `GET /api/v1/fulfillments/{id}` adds `connectorManaged`, `commandStatus`, `commandAttempts`, `nextAttemptAt`, `lastError`; same policy and scope.

## 9. M5 additions (live building)

Requirements (`Gatehouse.Application.Authorization.Requirements`, resource = `SiteId`):
- `ViewLiveSiteRequirement`: tenant admin, or security staff whose `SecurityStaffSiteIds` is `null` (tenant-wide) or contains the site.
- `OperateDoorsRequirement`: security staff whose `SecurityStaffSiteIds` is `null` or contains the site. Tenant admin **never** satisfies it (read-only live view).

| Endpoint | Policy | Resource check / scope | Error on failed resource check |
|---|---|---|---|
| `GET /api/v1/live/connectors?siteId=` | `StaffConsole` | instances covering viewable sites | - |
| `GET /api/v1/live/floors/{floorId}` | `StaffConsole` | `ViewLiveSiteRequirement` | `404 floor.not_found` |
| `GET /api/v1/live/alarms?status=open\|acknowledged&siteId=` | `StaffConsole` | viewable sites | - |
| `POST /api/v1/live/alarms/{alarmId}/acknowledge` | `SecurityStaff` | `ViewLiveSiteRequirement` then `OperateDoorsRequirement` | `404 alarm.not_found`, `403 auth.forbidden` |
| `POST /api/v1/live/door-commands` | `SecurityStaff` | `OperateDoorsRequirement` on the scope's site | `404 door_command.scope_not_found`, `403 auth.forbidden` |
| SignalR hub `/api/v1/live/hub` | `StaffConsole` | `JoinSite(siteId)` checks `ViewLiveSiteRequirement`; group `tenant:{tenantId}:site:{siteId}`; re-checked every 60 s (`LiveSubscriptionRevalidator`): a failing membership first gets client method `SubscriptionRevoked(siteId, code)` with code `auth.forbidden`, then is removed from the group (M11-18); the client shows "not permitted" and stops retrying | `HubException("auth.forbidden")` |

Live feed rows show person names to `StaffConsole` users only; card numbers never leave the gateway (hashed).

## 10. M7 additions (reconciliation)

Requirements (`Gatehouse.Application.Authorization.Requirements`), resource `PacsInstanceResource(PacsInstanceId PacsInstanceId, IReadOnlySet<SiteId> SiteIds)`:
- `ViewReconciliationRequirement`: `TenantAdminHandler`, or `SecurityStaffScopeHandler` when tenant-wide or at least one of the instance's sites is in scope.
- `RunReconciliationRequirement`: `SecurityStaffScopeHandler` only, when tenant-wide or **every** site of the instance is in scope. Tenant admin never satisfies it (read-only).

| Endpoint | Policy | Resource check / scope | Error on failed resource check |
|---|---|---|---|
| `GET /api/v1/reconciliation/instances` | `StaffConsole` | instances passing `ViewReconciliationRequirement` | - |
| `POST /api/v1/reconciliation/reports` `{pacsInstanceId}` → `202` | `SecurityStaff` | `ViewReconciliationRequirement`, then `RunReconciliationRequirement` | `404 reconciliation.instance_not_found`, `403 auth.forbidden` |
| `GET /api/v1/reconciliation/reports?pacsInstanceId=&limit=` | `StaffConsole` | instances passing `ViewReconciliationRequirement` (newest first, max 50) | - |
| `GET /api/v1/reconciliation/reports/{reportId}` | `StaffConsole` | `ViewReconciliationRequirement` on the report's instance | `404 reconciliation.report_not_found` |
| `POST /api/v1/tasks/{taskId}/resolve` | unchanged (`SecurityStaff` + `HandleStaffTaskRequirement`) | actions per task kind (`domain-model.md` §2.7) | `400 task.resolution_required` |

Live drift alarms (`UnauthorizedAccess`) use the M5 alarm endpoints and policies unchanged. Every new read endpoint has a cross-tenant 404 test.

## 11. M10 additions (platform operators, tenant admin, onboarding)

Decision: ADR-009. Two disjoint principals:

- **Platform operator**: authenticated only by scheme `PlatformBearer` (realm `gatehouse-platform`, audience `gatehouse-platform-api`, OTP required). Never has a `tenant_id`, never resolves a tenant, never reaches a tenant route. `IPlatformOperator` (scoped, `Gatehouse.Application.Platform`): `PlatformOperatorId Id`, `string Subject`, `string Email`. Linked like persons: by `sub`, else by `lower(email)` with `Subject == null`; no `Active` row → `403 platform.operator_not_found`.
- **Tenant admin** (unchanged role, §2): owns everything onboarding needs inside the tenant. Still never approves, revokes, emergency-revokes or handles fulfillments/tasks (hard rule 2), and never sees other tenants.

| Policy constant | Value | Requirements |
|---|---|---|
| `Policies.PlatformOperator` | `"PlatformOperator"` | authentication scheme `PlatformBearer` only + `PlatformOperatorRequirement` (`PlatformOperatorHandler`: active operator row) |

Rules (each has a test):
1. A `PlatformBearer` token on any tenant route → `401`; a tenant (or guest) token on any `/api/v1/platform/*` route → `401` (`PlatformAuthBoundaryTests`).
2. Platform handlers never inject `ITenantDb`, `ITenantContext` or `ITenantScopeFactory`; they read/write `CatalogDbContext` only (architecture test `Platform_handlers_do_not_depend_on_tenant_db`).
3. Platform DTOs carry counts and catalog fields only; no person names or emails from tenant databases (the first-admin email typed by the operator is catalog data).
4. Tenant admin routes take the tenant only from the claim; Keycloak calls use `ITenantContext.TenantId` as organization alias and mapper value.
5. A tenant admin cannot remove the last `TenantAdmin` assignment (`role.last_tenant_admin`) nor assign roles to persons of other types than `Employee`.

### Platform routes (`PlatformOperator`; catalog only)

| Endpoint | Notes | Errors |
|---|---|---|
| `GET /api/v1/platform/me` | `PlatformOperatorDto` | - |
| `GET /api/v1/platform/tenants` | `PlatformTenantSummaryDto[]` | - |
| `POST /api/v1/platform/tenants` `CreateTenantRequest` → `202 PlatformTenantDto` | starts provisioning | `400 validation.failed`, `409 tenant.already_exists`, `409 tenant.email_domain_taken` |
| `GET /api/v1/platform/tenants/{tenantId}` | `PlatformTenantDto` (provisioning, statistics, connectors, alerts) | `404 tenant.not_found` |
| `POST /api/v1/platform/tenants/{tenantId}/suspend` `{reason}` | catalog `Suspended` + Keycloak organization disabled | `404`, `409 tenant.invalid_status_transition` |
| `POST /api/v1/platform/tenants/{tenantId}/reactivate` | back to `Active` + organization enabled | `404`, `409 tenant.invalid_status_transition` |
| `POST /api/v1/platform/tenants/{tenantId}/provisioning/retry` | resume at the failed step | `404`, `409 tenant.provisioning_not_failed` |
| `POST /api/v1/platform/tenants/{tenantId}/admin-invite` | resend the first-admin email (only while the admin never signed in) | `404`, `409 tenant.admin_already_active` |
| `GET /api/v1/platform/alerts` | `PlatformAlertDto[]` (computed on read) | - |
| `GET /api/v1/platform/audit?tenantId=&before=&limit=` | `PlatformAuditEntryDto[]`, newest first, max 200 | - |

### Tenant admin routes (`TenantAdmin` unless noted; tenant from claim)

| Endpoint | Resource check / notes | Errors |
|---|---|---|
| `GET /api/v1/admin/identity-provider` | `IdentityProviderDto?` (`204` when none) | - |
| `PUT /api/v1/admin/identity-provider` `SaveIdentityProviderRequest` | create/replace the organization's single upstream IdP | `400 idp.invalid_config`, `400 idp.url_not_allowed`, `502 idp.provisioning_failed` |
| `POST /api/v1/admin/identity-provider/test` `SaveIdentityProviderRequest` | server-side metadata/discovery check, nothing saved → `IdpTestResultDto` | `400 idp.url_not_allowed` |
| `DELETE /api/v1/admin/identity-provider` | unlink + delete IdP; local admin login keeps working | `404 idp.not_configured` |
| `GET /api/v1/admin/security-levels` | `SecurityLevelDto[]` (read only in M10) | - |
| `POST /api/v1/admin/sites`, `PUT\|DELETE /api/v1/admin/sites/{siteId}` | | `404 site.not_found`, `409 site.not_empty` |
| `POST /api/v1/admin/sites/{siteId}/floors`, `PUT\|DELETE /api/v1/admin/floors/{floorId}` | | `404 floor.not_found`, `409 floor.not_empty` |
| `PUT\|DELETE /api/v1/admin/floors/{floorId}/background` (multipart `file`) | replaces/removes the drawing | `404 floor.not_found`, `400 floor.background_unsupported_type`, `413 floor.background_too_large` |
| `GET /api/v1/floors/{floorId}/background` | **`LinkedPerson`** (same visibility as the access map), `private, max-age=300`, `nosniff` | `404 floor.background_not_found` |
| `GET /api/v1/admin/floors/{floorId}/plan` | `FloorPlanDto` (editor read model) | `404 floor.not_found` |
| `POST /api/v1/admin/floors/{floorId}/areas`, `PUT\|DELETE /api/v1/admin/areas/{areaId}` | | `404 area.not_found`, `409 area.in_use` |
| `PUT /api/v1/admin/areas/{areaId}/owners` `{personIds}` | ≥ 1 active Employee | `400 area.owner_not_employee`, `409 area.last_owner` |
| `POST /api/v1/admin/areas/{areaId}/doors`, `PUT\|DELETE /api/v1/admin/doors/{doorId}` | `SaveDoorRequest.AreaId` on update moves the door to an area of the same floor | `404 door.not_found`, `409 door.ref_taken`, `400 door.invalid_area` |
| `POST /api/v1/admin/doors/{doorId}/readers`, `PUT\|DELETE /api/v1/admin/readers/{readerId}` | | `404 reader.not_found` |
| `POST /api/v1/admin/people/employees` `CreateEmployeeRequest` → `201 PersonSummaryDto` | | `409 person.email_taken` |
| `POST /api/v1/admin/people/import?dryRun=` (multipart `file`, CSV) → `PeopleImportResultDto` | | `400 people_import.invalid_file`, `413 people_import.too_large` |
| `POST /api/v1/admin/people/{personId}/terminate` → `PersonDetailDto` | Employees only, not self; `TerminationService` (entitlements and cards revoked) | `404 person.not_found`, `409 person.not_employee`, `409 person.terminated`, `403 person.self_termination_forbidden` |
| `GET /api/v1/admin/role-assignments`, `POST` `{personId, role, siteId?}`, `DELETE /{roleAssignmentId}` | | `404 role.assignment_not_found`, `409 role.assignment_exists`, `409 role.last_tenant_admin` |
| `GET\|POST /api/v1/admin/pacs-instances`, `GET\|PUT\|DELETE /api/v1/admin/pacs-instances/{pacsInstanceId}` | `PacsInstanceAdminDto` | `404 pacs.not_found`, `409 pacs.name_taken`, `409 pacs.in_use` |
| `PUT /api/v1/admin/pacs-instances/{pacsInstanceId}/mappings` `AccessLevelMappingRequest[]` | areas must be on the instance's sites | `400 mapping.area_not_on_instance_site` |
| `POST /api/v1/admin/pacs-instances/{pacsInstanceId}/connector` → `201 ConnectorEnrollmentDto` | token shown once, `Cache-Control: no-store`; sets `FulfillmentMode = Connector` | `409 connector.already_exists` |
| `POST /api/v1/admin/pacs-instances/{pacsInstanceId}/connector/revoke` | registration `Revoked`; instance back to `Manual` | `404 connector.not_found` |
| `GET /api/v1/admin/onboarding` | `OnboardingStatusDto` | - |
| `POST /api/v1/admin/onboarding/steps/{step}/skip` | only `Connector`, `Staff` | `409 onboarding.step_not_skippable` |
| `POST /api/v1/admin/onboarding/complete` | all required steps `Complete` | `409 onboarding.incomplete` |

Every new tenant read endpoint has a cross-tenant 404 (or "only own rows") test in `TenantIsolationTests`.

## 12. Lab findings fixes (2026-11-15)

- Cards: `SecurityStaff` see the cards of people they see (`VisibleTo`), may issue a card (`POST /api/v1/people/{id}/cards`) and deactivate one (`POST /api/v1/cards/{id}/deactivate`), besides `ManagePersonRequirement` (and `ActivateCardRequirement` for deactivate). Activation rules are unchanged.
- `PersonDetailDto` carries `CanIssueCards`, `CanDeactivateCards`, `CanTerminate` (TenantAdmin, active Employee, not self) and per entitlement `RevokeAction` (`revoke` for an area owner, `emergency-revoke` for security staff in scope, else null).
- A `Manager` step without an eligible manager or sponsor is assigned to a tenant admin other than the subject and requester (`ApproverResolver` fallback); only when none exists is the request refused with `approval.no_eligible_approver`.
- Requests for an area with `AccessControlled = false` are refused with `400 request.area_not_access_controlled`.
