"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { PeopleList } from "./people-list";
import { PersonDetail } from "./person-detail";

/** People page body: list on the left, `?person=<id>` detail on the right. */
export function PeopleView() {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const selected = params.get("person");

  return (
    <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
      <PeopleList selectedId={selected} onSelect={(id) => router.replace(`${pathname}?person=${id}`)} />
      {selected !== null ? (
        <PersonDetail key={selected} personId={selected} />
      ) : (
        <p className="text-sm text-muted-foreground">Select a person.</p>
      )}
    </div>
  );
}
