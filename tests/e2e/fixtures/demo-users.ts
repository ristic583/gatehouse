/** Every demo user from infra/keycloak/demo-users.md (key → email). */
export const demoUsers = {
  annaLarsen: "anna.larsen@nordlys.test",
  mikkelHolm: "mikkel.holm@nordlys.test",
  sofieBerg: "sofie.berg@nordlys.test",
  erikMoller: "erik.moller@nordlys.test",
  jonasNielsen: "jonas.nielsen@nordlys.test",
  frejaDahl: "freja.dahl@nordlys.test",
  oliverKruse: "oliver.kruse@nordlys.test",
  lenaSchmidt: "lena.schmidt@bricks.test",
  maxWeber: "max.weber@bricks.test",
  claraFischer: "clara.fischer@bricks.test",
  janKoch: "jan.koch@bricks.test",
  paulWagner: "paul.wagner@bricks.test",
  miaBecker: "mia.becker@bricks.test",
  tomRichter: "tom.richter@bricks.test",
} as const;

export type DemoUserKey = keyof typeof demoUsers;

export function demoPassword(): string {
  const password = process.env.GATEHOUSE_DEMO_PASSWORD;
  if (password === undefined || password === "") {
    throw new Error(
      "GATEHOUSE_DEMO_PASSWORD is not set. Read it with `dotnet user-secrets list --project src/Gatehouse.AppHost` " +
        "(key Parameters:demo-user-password) and export it before running Playwright.",
    );
  }
  return password;
}
