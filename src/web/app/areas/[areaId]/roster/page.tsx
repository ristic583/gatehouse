import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { AreaRoster } from "@/features/roster/components/area-roster";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInRoster({ params }: { params: Promise<{ areaId: string }> }) {
  await connection();
  const { areaId } = await params;
  if ((await getCurrentSession()) === null) {
    redirect(`/auth/login?returnTo=/areas/${areaId}/roster`);
  }
  return <AreaRoster areaId={areaId} />;
}

export default function AreaRosterPage({ params }: { params: Promise<{ areaId: string }> }) {
  return (
    <div className="flex flex-col gap-4 p-6">
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInRoster params={params} />
      </Suspense>
    </div>
  );
}
