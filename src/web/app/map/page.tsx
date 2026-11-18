import { PageHeading } from "@/components/page-heading";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { Suspense } from "react";
import { MapView } from "@/features/map/components/map-view";
import { getCurrentSession } from "@/lib/auth/current-session";

async function SignedInMap() {
  await connection();
  if ((await getCurrentSession()) === null) {
    redirect("/login?returnTo=/map");
  }
  return <MapView />;
}

export default function MapPage() {
  return (
    <div className="gh-page">
      <PageHeading
        eyebrow="SPATIAL COMMAND"
        title="Every space. Under control."
        description="Inspect your building. Understand every boundary. Govern access with precision."
      />
      <Suspense fallback={<p>Loading…</p>}>
        <SignedInMap />
      </Suspense>
    </div>
  );
}
