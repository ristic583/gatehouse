import { Suspense } from "react";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { apiGet, ApiError, NotSignedInError } from "@/lib/api/server-fetch";
import type { MeDto } from "@/lib/api/types";
import { entryFor } from "@/features/workspace/navigation";

async function WorkspaceEntry() {
  await connection();
  let destination = "/map";
  try {
    destination = entryFor(await apiGet<MeDto>("/v1/me"));
  } catch (error) {
    if (error instanceof NotSignedInError) destination = "/login?returnTo=/";
    else if (!(error instanceof ApiError)) throw error;
  }
  return redirect(destination);
}
export default function Home() {
  return (
    <Suspense fallback={<p>Opening your workspace…</p>}>
      <WorkspaceEntry />
    </Suspense>
  );
}
