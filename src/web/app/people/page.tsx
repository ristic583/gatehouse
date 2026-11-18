import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { PeopleView } from "@/features/people/components/people-view";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInPeople() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/people");
  }
  return <PeopleView />;
}

export default function PeoplePage() {
  return (
    <div className="flex min-w-0 flex-col gap-4 p-4 sm:p-6">
      <h1 className="text-xl font-semibold">People</h1>
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInPeople />
      </Suspense>
    </div>
  );
}
