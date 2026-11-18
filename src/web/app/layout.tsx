import type { Metadata } from "next";
import { Suspense } from "react";
import { QueryProvider } from "@/components/providers/query-provider";
import { Toaster } from "@/components/ui/sonner";
import { TenantShellSlot } from "@/features/shell/components/app-shell";
import { HeaderSlot } from "@/features/shell/components/header-slot";
import { ThemeStyle } from "@/features/theming/components/theme-style";
import "./globals.css";
import "./design.css";
import "./security-design.css";

export const metadata: Metadata = {
  title: "Gatehouse",
  description: "Physical access governance",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="flex min-h-full flex-col">
        <a className="gh-skip" href="#main-content">
          Skip to content
        </a>
        <Suspense fallback={null}>
          <ThemeStyle />
        </Suspense>
        <Suspense fallback={null}>
          <TenantShellSlot>
            <HeaderSlot />
          </TenantShellSlot>
        </Suspense>
        <QueryProvider>
          <main id="main-content" className="gh-main flex flex-1 flex-col">
            {children}
          </main>
          <Toaster />
        </QueryProvider>
      </body>
    </html>
  );
}
