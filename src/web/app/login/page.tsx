import { Suspense } from "react";
import { connection } from "next/server";
import { LoginView } from "@/features/auth/components/login-view";
import { demoPersonas } from "@/lib/auth/demo-personas";
import { isDemoSignInEnabled } from "@/lib/auth/demo-sign-in";

export default function LoginPage() {
  return (
    <Suspense fallback={null}>
      <Login />
    </Suspense>
  );
}

/** Reads the environment at request time, so one image serves both the public demo and a normal install. */
async function Login() {
  await connection();
  return <LoginView demoPersonas={isDemoSignInEnabled() ? demoPersonas : undefined} />;
}
