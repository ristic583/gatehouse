import { connection } from "next/server";
import { ApiError, apiGet, NotSignedInError } from "@/lib/api/server-fetch";
import type { ThemeDto } from "@/lib/api/types";
import { themeCss } from "../theme-css";

/** Server component: writes the signed-in tenant's theme tokens. Signed-out pages keep the defaults. */
export async function ThemeStyle() {
  await connection();
  let theme: ThemeDto | null = null;
  try {
    theme = await apiGet<ThemeDto>("/v1/theme");
  } catch (error) {
    if (!(error instanceof NotSignedInError || error instanceof ApiError)) {
      throw error;
    }
  }
  return theme === null ? null : <style data-testid="tenant-theme">{themeCss(theme)}</style>;
}
