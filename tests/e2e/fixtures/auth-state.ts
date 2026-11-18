import path from "node:path";

/** Users signed in once by `auth.setup.ts`; their browser state is stored in `.auth/{username}.json`. */
export const authUsers = ["anna.larsen", "mikkel.holm", "sofie.berg", "freja.dahl", "lena.schmidt", "mia.becker", "jonas.nielsen", "erik.moller", "oliver.kruse"] as const;

export type AuthUser = (typeof authUsers)[number];

export function storageStateFor(user: AuthUser): string {
  return path.join(__dirname, "..", ".auth", `${user}.json`);
}

export function emailOf(user: AuthUser): string {
  return `${user}@${user === "lena.schmidt" || user === "mia.becker" ? "bricks" : "nordlys"}.test`;
}
