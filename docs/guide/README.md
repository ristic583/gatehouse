# Gatehouse guide

Explanatory chapters for understanding the system end to end. They describe what the code does today; the normative
sources stay `docs/SPEC.md`, `docs/contracts/` and `docs/adr/` (if a guide and a contract disagree, the contract wins
and the guide is fixed).

0. [Design reasoning](00-design-reasoning.md): the forces of the real problem and the decision each one led to.
1. [Database structure](01-database.md): catalog vs tenant databases, every table by module, one request through the tables.
2. [Tenants, roles and permissions](02-tenants-roles-permissions.md): what a tenant is, assigned vs derived roles, who may do what.
