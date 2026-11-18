import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { ConsoleView } from "@/features/console/components/console-view";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInConsole() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/console");
  }
  return <ConsoleView />;
}

export default function ConsolePage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">Console</h1>
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInConsole />
      </Suspense>
    </div>
  );
}
