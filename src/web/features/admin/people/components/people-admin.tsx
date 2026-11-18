"use client";

import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { AddEmployeeDialog } from "./add-employee-dialog";
import { PeopleImport } from "./people-import";
import { RoleAssignments } from "./role-assignments";

export function PeopleAdmin() {
  const [adding, setAdding] = useState(false);
  return (
    <div className="flex flex-col gap-4">
      <section className="gh-panel flex flex-col gap-3" aria-label="Employees">
        <div className="flex items-center justify-between">
          <h3 className="gh-panel-title">Employees</h3>
          <div className="flex gap-2">
            <Link className="inline-flex items-center px-3 text-sm underline" href="/people">
              Open people list
            </Link>
            <Button type="button" onClick={() => setAdding(true)}>
              Add employee
            </Button>
          </div>
        </div>
        {adding && <AddEmployeeDialog onClose={() => setAdding(false)} />}
      </section>
      <PeopleImport />
      <RoleAssignments />
    </div>
  );
}
