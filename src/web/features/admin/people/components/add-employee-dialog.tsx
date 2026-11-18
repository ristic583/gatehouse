"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { peopleAdminApi, peopleAdminError } from "../api";

export function AddEmployeeDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [givenName, setGivenName] = useState("");
  const [familyName, setFamilyName] = useState("");
  const create = useMutation({
    mutationFn: peopleAdminApi.createEmployee,
    onSuccess: async () => {
      toast.success("Employee added");
      await queryClient.invalidateQueries({ queryKey: ["people"] });
      await queryClient.invalidateQueries({ queryKey: ["admin"] });
      onClose();
    },
  });
  const error = create.error instanceof ApiError ? peopleAdminError(create.error.code, create.error.detail) : create.error?.message;

  return (
    <div role="dialog" aria-modal="true" aria-label="Add employee" data-testid="add-employee-dialog" className="gh-panel">
      <form
        className="flex flex-col gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          create.mutate({
            email: email.trim(),
            givenName: givenName.trim(),
            familyName: familyName.trim(),
            managerId: null,
            timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
          });
        }}
      >
        <h3 className="font-medium">Add employee</h3>
        <div className="flex flex-col gap-1">
          <Label htmlFor="employee-email">Work email</Label>
          <Input id="employee-email" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="employee-given">Given name</Label>
          <Input id="employee-given" required value={givenName} onChange={(e) => setGivenName(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor="employee-family">Family name</Label>
          <Input id="employee-family" required value={familyName} onChange={(e) => setFamilyName(e.target.value)} />
        </div>
        {error !== undefined && (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        )}
        <div className="flex gap-2">
          <Button type="submit" disabled={create.isPending}>
            Add employee
          </Button>
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
        </div>
      </form>
    </div>
  );
}
