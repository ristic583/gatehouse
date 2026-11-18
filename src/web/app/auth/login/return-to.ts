/** Only same-site relative paths are allowed as post-login targets; anything else becomes "/". */
export function safeReturnTo(value: string | null | undefined): string {
  if (value === null || value === undefined || value === "") {
    return "/";
  }
  if (!value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\") || value.includes("://")) {
    return "/";
  }
  return value;
}
