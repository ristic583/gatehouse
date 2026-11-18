import { Suspense } from "react";
import { PlatformLoginView } from "@/features/platform/components/platform-login-view";

export default function PlatformLoginPage() {
  return (
    <Suspense fallback={null}>
      <PlatformLoginView />
    </Suspense>
  );
}
