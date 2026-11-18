import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { LiveView } from "@/features/live/components/live-view";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInLive() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/live");
  }
  return <LiveView />;
}

export default function LivePage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">Live building</h1>
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInLive />
      </Suspense>
    </div>
  );
}
