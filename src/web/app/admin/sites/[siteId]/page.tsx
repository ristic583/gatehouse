import { connection } from "next/server";
import { Suspense } from "react";
import { SiteDetail } from "@/features/admin/sites/components/sites-admin";

async function Site({ params }: { params: Promise<{ siteId: string }> }) {
  await connection();
  const { siteId } = await params;
  return <SiteDetail siteId={siteId} />;
}

export default function AdminSitePage({ params }: PageProps<"/admin/sites/[siteId]">) {
  return (
    <Suspense fallback={<p className="gh-loading">Loading…</p>}>
      <Site params={params} />
    </Suspense>
  );
}
