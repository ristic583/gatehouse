# 2. Tenants, roles and permissions

Who the tenants are, which roles exist, and what each role may do. Normative source: `docs/contracts/authorization.md`
(exact requirement and handler names), SPEC §2–3, ADR-001/002. This chapter explains the model.

## 2.1 Tenants

A **tenant** is one customer company. It has its own database (`gh_tenant_{slug}`), its own upstream identity provider,
its own sites, people, PACS and agents. Nothing is shared between tenants except the infrastructure.

| Tenant | Slug | Sign-in | Email domain | Sites | PACS |
|---|---|---|---|---|---|
| Nordlys A/S | `nordlys` | OIDC (Entra-like) | `nordlys.test` | HQ Copenhagen (3 floors), Warehouse Aarhus | LegacyPACS @ HQ, CloudPACS @ Warehouse |
| Bricks GmbH | `bricks` | SAML 2.0 | `bricks.test` | Berlin Office (1 floor) | CloudPACS |

Where a tenant is defined:
- **Catalog DB** `tenants` row: slug, status (`Active`/`Suspended`/`Deprovisioned`), DB name, encrypted connection string, IdP kind, Keycloak organization alias.
- **Keycloak**: one Organization per tenant (alias = slug) owning the email domain and linked to the tenant's IdP. A mapper
  sets the user attribute `tenant_id`, which ends up as the `tenant_id` claim in every token.
- **Tenant DB**: everything else.

There is **no platform super-admin in the app**: nobody can see across tenants through the web. Tenants are created by the
operator with `gatehouse-cli tenant create` (creates the database, migrates it, writes the catalog row).

How a request becomes "a request of tenant X": token `tenant_id` → `TenantResolutionMiddleware` → catalog row (must exist
and be `Active`, else `403 tenant.unknown` / `tenant.inactive`) → `ITenantContext` → `TenantDbContext` on that tenant's DB.

## 2.2 From signed-in user to person

Keycloak proves *who you are* (`sub`, `email`, `tenant_id`); it holds **no Gatehouse roles** (ADR-002). On the first
request Gatehouse links the token to a `persons` row: by `identity_subject == sub`, otherwise by email (then stores `sub`).
No matching person → `403 identity.person_not_provisioned`: being able to sign in is not enough, HR sync or an admin
must have created you as a person.

## 2.3 Roles: assigned vs derived

A role is always **role + scope** (whom or what it applies to).

| Role | How you get it | Scope |
|---|---|---|
| **Employee** | `persons.type = Employee` | yourself |
| **External / Visitor / Guest** | `persons.type`; must have a sponsor, start and end | yourself, within the sponsorship |
| **Manager** | *derived*: someone has `manager_id = you` | your direct reports |
| **Sponsor** | *derived*: someone has `sponsor_id = you` | the people you sponsor |
| **Area owner** | *derived*: you are in `area_owners` of an area | the areas you own |
| **Security staff** | *assigned*: `role_assignments(SecurityStaff, site_id)` | whole tenant (`site_id` null) or listed sites |
| **Tenant admin** | *assigned*: `role_assignments(TenantAdmin)` | whole tenant |

Only two roles are stored as roles. The other three come from relations in the data, so they follow the org chart
automatically: change someone's manager and the approver changes with it. One person can hold several roles
(Erik Møller is a manager and an area owner).

Non-human actors: a **connector agent** authenticates with its client certificate on the gateway only and can do nothing
in the user API; **System** is the actor of background jobs (expiry, termination, retention) in the audit log.

## 2.4 What each role may do

| Action | Who may | Notes |
|---|---|---|
| See the map (sites, floors, areas) | every tenant member | the map shows the whole tenant; the colour is your own state |
| Request access for someone | yourself; your manager; your sponsor | `RequestAccessRequirement` |
| Approve a `Manager` step | the manager assigned to that step | never for yourself or a request you made |
| Approve an `AreaOwner` step | any owner of that area | never for yourself or a request you made |
| See an entitlement | subject or requester, their manager/sponsor, owner of the area, security staff in scope, tenant admin | |
| Revoke (normal, reason required) | owner of that area | not admin, not security staff |
| Emergency revoke | security staff in scope | high-priority command |
| Handle fulfillment (start, confirm, fail, retry) | security staff in scope | never for own access; admin cannot |
| Handle staff tasks, reconciliation findings | security staff in scope | site-less tasks: tenant-wide staff only |
| Staff console | security staff (any scope), tenant admin (read-only) | |
| Door commands (unlock, lockdown) | security staff in scope | |
| Reveal a PIN | the staff member assigned to that `PinReveal` task, while it is `InProgress` | re-masks after 30 s, audited without the value |
| Read the audit log | tenant admin, tenant-wide security staff | |
| Area roster | owner of the area, security staff in scope | |
| Activate a card | the employee themself; manager; sponsor | externals/visitors/guests cannot activate their own |
| Manage a person (onboard externals, end sponsorship) | tenant admin, sponsor, manager | |
| Manage the tenant (people, sites, levels, theme, settings, connectors, retention) | tenant admin | |

## 2.5 The rules that hold it together

1. **No self-approval**: the decider is never the subject or the requester. Implemented as a handler that *fails*
   (`NoSelfApprovalHandler`); in ASP.NET one failing handler beats any succeeding one.
2. **Approval rights are not admin rights**: `TenantAdminHandler` is never registered for approve or revoke, and the admin
   gets no fulfillments to handle. Power over the system does not mean power over access.
3. **Tenant + scope on every query**: tenant is the physical DB; scope is applied by query extensions
   (`VisibleTo(me)`, `HandledBy(me)`, `AwaitingDecisionBy(me)`), so a list never contains rows you may not see.
4. **No self-fulfillment**: staff never process a fulfillment of their own access.

Out-of-scope work answers `404`, not `403`, where revealing that it exists would leak information (fulfillments).

## 2.6 How it is checked in code

- **Policies** on endpoints (`Policies.TenantMember` is the fallback for every endpoint; `LinkedPerson`, `TenantAdmin`,
  `SecurityStaff`, `StaffConsole`, `AuditReader`) answer "may you call this at all?".
- **Resource-based requirements** inside handlers answer "may you do this to *this* thing?": the handler loads the data,
  builds a resource record (`PersonResource`, `EntitlementResource`, …) and calls `IAuthorizationService.AuthorizeAsync`.
  Authorization handlers never query the DB themselves.
- The **domain** repeats the critical checks (no self-approval) so a bug in the API layer cannot bypass them.

Code: `src/Gatehouse.Application/Authorization/` (policies, requirements, handlers, query scopes),
`src/Gatehouse.Api/` (`TenantResolutionMiddleware`, endpoint policies). Demo users per role: `infra/keycloak/demo-users.md`.
