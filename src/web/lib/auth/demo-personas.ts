/** Demo users a visitor can explore the public demo as (`infra/keycloak/demo-users.md`). Plain data: safe for client components. */
export interface DemoPersona {
  email: string;
  name: string;
  role: string;
  tenant: string;
}

export const demoPersonas: readonly DemoPersona[] = [
  { email: "anna.larsen@nordlys.test", name: "Anna Larsen", role: "Employee: requests access", tenant: "Nordlys A/S" },
  { email: "mikkel.holm@nordlys.test", name: "Mikkel Holm", role: "Manager: approves his team", tenant: "Nordlys A/S" },
  { email: "sofie.berg@nordlys.test", name: "Sofie Berg", role: "Area owner: approves her areas", tenant: "Nordlys A/S" },
  { email: "jonas.nielsen@nordlys.test", name: "Jonas Nielsen", role: "Security: live building view", tenant: "Nordlys A/S" },
  { email: "freja.dahl@nordlys.test", name: "Freja Dahl", role: "Tenant admin", tenant: "Nordlys A/S" },
  { email: "lena.schmidt@bricks.test", name: "Lena Schmidt", role: "Employee, another tenant", tenant: "Bricks GmbH" },
];

export function isDemoPersona(email: string): boolean {
  return demoPersonas.some((p) => p.email === email);
}
