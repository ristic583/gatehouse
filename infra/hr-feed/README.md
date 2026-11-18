# HR feed (mocked)

Gatehouse reads authoritative employees from one JSON file per tenant: `{HrSync:FeedDirectory}/{tenantId}.json`.
The AppHost sets `HrSync__FeedDirectory` to this folder for the api and workers. The Workers `HrSyncJob` reads the
feed every `HrSync:IntervalMinutes` (default 60); tenant admins can run it now with `POST /api/v1/admin/hr-sync`.

The file is a camelCase JSON array of records:

| Field | Type | Notes |
|---|---|---|
| `email` | string | Matched case-insensitively to `Person.Email`. |
| `givenName`, `familyName` | string | 1-100 characters. |
| `managerEmail` | string or null | Must be a person in the tenant (or in this feed); otherwise the record is skipped. |
| `employmentStart` | ISO 8601 | Becomes `Eligibility.Start`. |
| `employmentEnd` | ISO 8601 or null | Becomes `Eligibility.End`; null = open-ended. At this end the lifecycle worker terminates the person. |
| `timeZoneId` | string | IANA time zone, e.g. `Europe/Copenhagen`. |

Rules:
- Unknown emails become new `Authoritative` employees.
- Existing authoritative people are updated (names, manager, eligibility); a changed eligibility is audited as `person.eligibility_changed`.
- Local people (externals, visitors, guests) are never changed by the feed; their records are counted as skipped.
- People missing from the feed are never touched or deleted.

`nordlys.json` lists the seeded nordlys employees plus two new hires: `ida.jensen` (manager `mikkel.holm`) and
`lars.petersen` (employment ends 2030-01-01).
