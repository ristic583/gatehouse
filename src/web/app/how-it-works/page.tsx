import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { HowItWorksView } from "@/features/showcase/how-it-works/components/how-it-works-view";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInHowItWorks() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/how-it-works");
  }
  return <HowItWorksView />;
}

export default function HowItWorksPage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-xl font-semibold">How it works</h1>
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInHowItWorks />
      </Suspense>
    </div>
  );
}
