import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { MyBadge } from "@/features/badge/components/my-badge";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInBadge() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/me");
  }
  return <MyBadge />;
}

export default function MyBadgePage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">My badge</h1>
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInBadge />
      </Suspense>
    </div>
  );
}
