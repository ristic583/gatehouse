import { notFound } from "next/navigation";
import { Suspense } from "react";

export default async function DesignPreviewPage() {
  if (process.env.NODE_ENV !== "development") notFound();
  const { PreviewWorkspace } = await import("@/features/showcase/design-preview/preview-workspace");
  return (
    <Suspense fallback={null}>
      <PreviewWorkspace />
    </Suspense>
  );
}
