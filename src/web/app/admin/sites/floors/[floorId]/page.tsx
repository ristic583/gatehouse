import { connection } from "next/server";
import { Suspense } from "react";
import { FloorEditorPage } from "@/features/admin/sites/components/floor-editor-page";

async function Floor({ params }: { params: Promise<{ floorId: string }> }) {
  await connection();
  const { floorId } = await params;
  return <FloorEditorPage floorId={floorId} />;
}

export default function AdminFloorPage({ params }: PageProps<"/admin/sites/floors/[floorId]">) {
  return (
    <Suspense fallback={<p className="gh-loading">Loading…</p>}>
      <Floor params={params} />
    </Suspense>
  );
}
