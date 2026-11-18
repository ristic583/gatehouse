import { expect, request as playwrightRequest } from "@playwright/test";

export function mailpitUrl(): string {
  return (process.env.GATEHOUSE_MAILPIT_URL ?? "http://localhost:8025").replace(/\/+$/, "");
}

interface MailpitSummary {
  ID: string;
  Created: string;
}

/** Newest message to `email` (Mailpit API v1); waits until one arrives. Returns its text and HTML. */
export async function latestMailTo(email: string, timeout = 60_000): Promise<{ text: string; html: string }> {
  const api = await playwrightRequest.newContext({ baseURL: mailpitUrl() });
  try {
    let id: string | undefined;
    await expect
      .poll(
        async () => {
          const response = await api.get(`/api/v1/search?query=${encodeURIComponent(`to:"${email}"`)}`);
          if (!response.ok()) return false;
          const body = (await response.json()) as { messages?: MailpitSummary[] };
          id = [...(body.messages ?? [])].sort((a, b) => b.Created.localeCompare(a.Created))[0]?.ID;
          return id !== undefined;
        },
        { timeout, intervals: [1000, 2000] },
      )
      .toBe(true);
    const message = (await (await api.get(`/api/v1/message/${id}`)).json()) as { Text?: string; HTML?: string };
    return { text: message.Text ?? "", html: message.HTML ?? "" };
  } finally {
    await api.dispose();
  }
}

/** The Keycloak action link (`login-actions/action-token`) of the newest mail to `email`. */
export async function actionLinkFor(email: string): Promise<string> {
  const { text, html } = await latestMailTo(email);
  const match = /https?:\/\/[^\s"'<>]+\/login-actions\/action-token\?[^\s"'<>]+/.exec(`${html}\n${text}`);
  if (match === null) throw new Error(`No Keycloak action link in the mail to ${email}.`);
  return match[0].replace(/&amp;/g, "&");
}
