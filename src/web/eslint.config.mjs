import { defineConfig, globalIgnores } from "eslint/config";
import nextVitals from "eslint-config-next/core-web-vitals";
import nextTs from "eslint-config-next/typescript";
import prettier from "eslint-config-prettier";
import { maxLinesAllowlist, staleAllowlistEntries } from "./eslint-allowlist.mjs";

const stale = staleAllowlistEntries();
if (stale.length > 0) {
  throw new Error(`Stale entries in eslint-allowlist.mjs (remove them): ${stale.join(", ")}`);
}

// conventions.md §7: line length ≤ 140, component/page files ≤ 250 lines. Prettier owns formatting;
// max-len is re-enabled after eslint-config-prettier so overlong code that Prettier cannot wrap still fails.
const eslintConfig = defineConfig([
  prettier,
  ...nextVitals,
  ...nextTs,
  {
    rules: {
      "max-len": ["error", { code: 140, ignoreUrls: true, ignoreStrings: true, ignoreTemplateLiterals: true, ignoreRegExpLiterals: true }],
    },
  },
  // conventions.md §8: showcase code (design preview, "how it works") is not imported by product features.
  {
    files: ["features/**/*.{ts,tsx}"],
    ignores: ["features/showcase/**"],
    rules: {
      "no-restricted-imports": [
        "error",
        {
          patterns: [
            {
              group: ["@/features/showcase", "@/features/showcase/*", "**/showcase/*"],
              message: "Product features may not import showcase code.",
            },
          ],
        },
      ],
    },
  },
  {
    files: ["features/**/components/**/*.tsx", "app/**/*.tsx"],
    ignores: maxLinesAllowlist,
    rules: {
      "max-lines": ["error", { max: 250, skipBlankLines: true, skipComments: true }],
    },
  },
  // Override default ignores of eslint-config-next.
  globalIgnores([
    // Default ignores of eslint-config-next:
    ".next/**",
    ".design-preview/**",
    "out/**",
    "build/**",
    "next-env.d.ts",
  ]),
]);

export default eslintConfig;
