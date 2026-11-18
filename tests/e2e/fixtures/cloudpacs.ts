import { expect, type APIRequestContext, type APIResponse } from "@playwright/test";

export function cloudPacsUrl(): string {
  return process.env.GATEHOUSE_CLOUDPACS_SIM_URL ?? "http://localhost:5185";
}

export interface CloudPacsAssignment {
  assignmentId: string;
  groupId: string;
  externalRef: string;
  cardNumber: string | null;
  active: boolean;
  holderName: string | null;
}

/** Every access group membership the CloudPACS simulator holds (`GET /dev/assignments`). */
export async function cloudPacsAssignments(request: APIRequestContext): Promise<CloudPacsAssignment[]> {
  const response = await request.get(`${cloudPacsUrl()}/dev/assignments`);
  expect(response.ok(), `cloudpacs assignments: ${response.status()}`).toBe(true);
  return (await response.json()) as CloudPacsAssignment[];
}

function cloudPacsApiKey(): string {
  return process.env.GATEHOUSE_CLOUDPACS_API_KEY ?? "dev-cloudpacs-key";
}

/** Retries a vendor call through the simulator's injected 503/429 failures (the AppHost sets a small error rate). */
async function withChaosRetry(call: () => Promise<APIResponse>): Promise<APIResponse> {
  let response = await call();
  for (let attempt = 1; attempt < 5 && (response.status() === 503 || response.status() === 429); attempt++) {
    response = await call();
  }
  return response;
}

/** CloudPACS vendor API calls a test uses to change the PACS behind Gatehouse's back (`X-Api-Key`). */
export function cloudPacsApi(request: APIRequestContext) {
  const headers = { "X-Api-Key": cloudPacsApiKey() };
  const api = `${cloudPacsUrl()}/api`;
  return {
    async upsertCardholder(ref: string, givenName: string, familyName: string, cardNumber: string): Promise<void> {
      const response = await withChaosRetry(() =>
        request.put(`${api}/cardholders/${ref}`, { headers, data: { givenName, familyName, cardNumber, photoUrl: null } }),
      );
      expect(response.ok(), `cloudpacs upsert cardholder: ${response.status()}`).toBe(true);
    },
    async addMember(groupId: string, ref: string, cardNumber: string): Promise<void> {
      const response = await withChaosRetry(() => request.put(`${api}/access-groups/${groupId}/members/${ref}`, { headers, data: { cardNumber } }));
      expect(response.ok(), `cloudpacs add member: ${response.status()}`).toBe(true);
    },
    /** Removes a membership; a missing one is fine (cleanup). */
    async removeMember(groupId: string, ref: string): Promise<void> {
      const response = await withChaosRetry(() => request.delete(`${api}/access-groups/${groupId}/members/${ref}`, { headers }));
      expect(response.ok() || response.status() === 404, `cloudpacs remove member: ${response.status()}`).toBe(true);
    },
  };
}

/** Swipes at a CloudPACS door as a cardholder (`POST /dev/swipes`); CloudPACS decides. */
export async function cloudPacsSwipe(request: APIRequestContext, doorRef: string, externalRef: string): Promise<string> {
  const response = await request.post(`${cloudPacsUrl()}/dev/swipes`, { data: { doorRef, cardNumber: null, externalRef } });
  expect(response.ok(), `cloudpacs swipe: ${response.status()}`).toBe(true);
  return ((await response.json()) as { decision: string }).decision;
}
