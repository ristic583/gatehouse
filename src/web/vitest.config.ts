import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL(".", import.meta.url)),
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    include: ["**/*.test.{ts,tsx}"],
    exclude: ["node_modules", ".next"],
    coverage: {
      provider: "v8",
      reporter: ["text-summary", "html", "json-summary", "json"],
      reportsDirectory: "../../artifacts/coverage/web",
      include: ["app/**/*.{ts,tsx}", "components/**/*.{ts,tsx}", "features/**/*.{ts,tsx}", "lib/**/*.{ts,tsx}", "proxy.ts"],
      exclude: ["**/*.test.{ts,tsx}", "**/*.d.ts"],
    },
  },
});
