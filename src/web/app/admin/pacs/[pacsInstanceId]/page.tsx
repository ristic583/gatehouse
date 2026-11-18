import { connection } from "next/server";
import { Suspense } from "react";
import { PacsInstanceDetail } from "@/features/admin/pacs/components/pacs-admin";

async function Instance({ params }: { params: Promise<{ pacsInstanceId: string }> }) {
  await connection();
  const { pacsInstanceId } = await params;
  return <PacsInstanceDetail id={pacsInstanceId} />;
}

export default function AdminPacsInstancePage({ params }: PageProps<"/admin/pacs/[pacsInstanceId]">) {
  return (
    <Suspense fallback={<p className="gh-loading">Loading…</p>}>
      <Instance params={params} />
    </Suspense>
  );
}
