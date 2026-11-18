"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import type { AssignedRole, AssignRoleRequest, PersonSummaryDto, RoleAssignmentDto, SiteDto } from "@/lib/api/types";
import { peopleAdminApi, peopleAdminError, peopleAdminKeys } from "../api";

const ROLE_TEXT: Record<AssignedRole, string> = { TenantAdmin: "Tenant admin", SecurityStaff: "Security staff" };

export interface AssignRoleDialogProps {
  sites: readonly SiteDto[];
  searchPeople: (query: string) => Promise<PersonSummaryDto[]>;
  onAssign: (request: AssignRoleRequest) => Promise<unknown>;
  onClose: () => void;
}

/** Person search over `GET /api/v1/people`; a site scope applies only to Security staff. */
export function AssignRoleDialog({ sites, searchPeople, onAssign, onClose }: AssignRoleDialogProps) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<PersonSummaryDto[]>([]);
  const [person, setPerson] = useState<PersonSummaryDto | null>(null);
  const [role, setRole] = useState<AssignedRole>("TenantAdmin");
  const [siteId, setSiteId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function search() {
    setError(null);
    try {
      setResults(await searchPeople(query));
    } catch {
      setError("The search failed.");
    }
  }

  async function submit() {
    if (person === null) return;
    setBusy(true);
    setError(null);
    try {
      await onAssign({ personId: person.personId, role, siteId: role === "SecurityStaff" && siteId !== "" ? siteId : null });
      onClose();
    } catch (e) {
      setError(e instanceof ApiError ? peopleAdminError(e.code, e.detail) : "The role could not be assigned.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div role="dialog" aria-modal="true" aria-label="Assign role" data-testid="assign-role-dialog" className="gh-panel flex flex-col gap-3">
      <h3 className="font-medium">Assign role</h3>
      <div className="flex items-end gap-2">
        <div className="flex flex-1 flex-col gap-1">
          <Label htmlFor="role-person-search">Find employee</Label>
          <Input
            id="role-person-search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && void search()}
          />
        </div>
        <Button type="button" variant="outline" onClick={() => void search()}>
          Search
        </Button>
      </div>
      {results.length > 0 && (
        <ul className="flex max-h-40 flex-col gap-1 overflow-y-auto text-sm">
          {results.map((p) => (
            <li key={p.personId}>
              <button
                type="button"
                className={`w-full rounded px-2 py-1 text-left ${person?.personId === p.personId ? "bg-muted" : ""}`}
                aria-pressed={person?.personId === p.personId}
                onClick={() => setPerson(p)}
              >
                {p.displayName} · {p.email}
              </button>
            </li>
          ))}
        </ul>
      )}
      <div className="flex flex-col gap-1">
        <Label htmlFor="role-role">Role</Label>
        <select id="role-role" className="rounded border px-2 py-1" value={role} onChange={(e) => setRole(e.target.value as AssignedRole)}>
          <option value="TenantAdmin">{ROLE_TEXT.TenantAdmin}</option>
          <option value="SecurityStaff">{ROLE_TEXT.SecurityStaff}</option>
        </select>
      </div>
      {role === "SecurityStaff" && (
        <div className="flex flex-col gap-1">
          <Label htmlFor="role-site">Site (optional)</Label>
          <select id="role-site" className="rounded border px-2 py-1" value={siteId} onChange={(e) => setSiteId(e.target.value)}>
            <option value="">All sites</option>
            {sites.map((s) => (
              <option key={s.siteId} value={s.siteId}>
                {s.name}
              </option>
            ))}
          </select>
        </div>
      )}
      {error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      <div className="flex gap-2">
        <Button type="button" disabled={person === null || busy} onClick={() => void submit()}>
          Assign
        </Button>
        <Button type="button" variant="outline" onClick={onClose}>
          Cancel
        </Button>
      </div>
    </div>
  );
}

export function RoleAssignmentsView({
  roles,
  onRemove,
  error,
}: {
  roles: readonly RoleAssignmentDto[];
  onRemove: (id: string) => void;
  error?: string | null;
}) {
  return (
    <div className="flex flex-col gap-2">
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      <div className="gh-table-wrap">
        <table className="w-full text-left">
          <thead>
            <tr>
              <th>Person</th>
              <th>Role</th>
              <th>Scope</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {roles.map((r) => (
              <tr key={r.id} data-testid="role-row">
                <td>{r.person.displayName}</td>
                <td>{ROLE_TEXT[r.role]}</td>
                <td>{r.siteName ?? (r.role === "SecurityStaff" ? "All sites" : "Tenant")}</td>
                <td>
                  <Button type="button" variant="outline" size="sm" onClick={() => onRemove(r.id)}>
                    Remove
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export function RoleAssignments() {
  const queryClient = useQueryClient();
  const [assigning, setAssigning] = useState(false);
  const roles = useQuery({ queryKey: peopleAdminKeys.roles, queryFn: peopleAdminApi.roles });
  const sites = useQuery({ queryKey: peopleAdminKeys.sites, queryFn: peopleAdminApi.sites, enabled: assigning });
  const refresh = () => queryClient.invalidateQueries({ queryKey: peopleAdminKeys.roles });
  const remove = useMutation({
    mutationFn: peopleAdminApi.removeRole,
    onSuccess: async () => {
      toast.success("Role removed");
      await refresh();
    },
  });
  const removeError = remove.error instanceof ApiError ? peopleAdminError(remove.error.code, remove.error.detail) : null;

  return (
    <section className="flex flex-col gap-3" aria-label="Roles">
      <div className="gh-section-heading">
        <h2>Roles</h2>
        <Button type="button" onClick={() => setAssigning(true)}>
          Assign role
        </Button>
      </div>
      {assigning && (
        <AssignRoleDialog
          sites={sites.data ?? []}
          searchPeople={peopleAdminApi.searchEmployees}
          onAssign={async (body) => {
            await peopleAdminApi.assignRole(body);
            toast.success("Role assigned");
            await refresh();
          }}
          onClose={() => setAssigning(false)}
        />
      )}
      {roles.isPending ? (
        <p className="gh-loading">Loading roles…</p>
      ) : roles.isError ? (
        <p role="alert">Roles could not be loaded.</p>
      ) : (
        <RoleAssignmentsView roles={roles.data} onRemove={(id) => remove.mutate(id)} error={removeError} />
      )}
    </section>
  );
}
