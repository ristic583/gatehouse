"use client";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { ArrowRight, ArrowUpRight, Check, LockKeyhole, ShieldCheck } from "lucide-react";
import { AppTitle } from "@/features/shell/components/app-title";
import { safeReturnTo } from "@/app/auth/login/return-to";
import { ArchitecturalMark } from "@/components/architectural-mark";
import type { DemoPersona } from "@/lib/auth/demo-personas";

/** `demoPersonas` is set only on the public demo (`GATEHOUSE_DEMO_SIGN_IN`): one click signs a demo user in, no password. */
export function LoginView({ demoPersonas }: { demoPersonas?: readonly DemoPersona[] }) {
  const params = useSearchParams();
  const [submitting, setSubmitting] = useState(false);
  const error = params.get("error");
  const emailForm = (
    <form action="/auth/login" method="post" className={demoPersonas ? "gh-demo-email" : undefined} onSubmit={() => setSubmitting(true)}>
      <input type="hidden" name="returnTo" value={safeReturnTo(params.get("returnTo"))} />
      <label htmlFor="login-email">Work email</label>
      <input id="login-email" name="email" type="email" required autoComplete="email" placeholder="you@company.com" maxLength={254} />
      <button type="submit" disabled={submitting}>
        {submitting ? "Connecting securely…" : "Continue with email"}
        <ArrowRight size={18} />
      </button>
      {error && (
        <p role="alert" className="gh-login-error">
          {error === "email" ? "Enter a valid work email address." : "Your sign-in service is unavailable. Please try again shortly."}
        </p>
      )}
    </form>
  );
  return (
    <div className="gh-login">
      <section className="gh-login-story">
        <AppTitle />
        <div className="gh-login-copy">
          <p className="gh-eyebrow">PHYSICAL ACCESS. PRECISELY GOVERNED.</p>
          <h1>
            Every boundary.
            <br />
            Every decision.
            <br />
            <em>Accounted for.</em>
          </h1>
          <p>
            A precise view of your buildings, people, and access decisions. Purposeful requests. Independent approval. Confirmed access.
          </p>
        </div>
        <ArchitecturalMark />
        <div className="gh-login-story-bottom">
          <span>
            <span className="gh-small-dot" /> Access with accountability.
          </span>
          <span>
            Control begins with clarity <ArrowUpRight size={15} />
          </span>
        </div>
      </section>
      <section className="gh-login-panel">
        <div className="gh-login-top">
          <span>YOUR ACCESS WORKSPACE</span>
          <ShieldCheck size={19} />
        </div>
        <div className="gh-login-form-wrap">
          <span className="gh-login-symbol">
            <LockKeyhole size={25} strokeWidth={1.5} />
          </span>
          {demoPersonas ? (
            <>
              <p className="gh-eyebrow">PUBLIC DEMO</p>
              <h2>
                Explore Gatehouse
                <br />
                as anyone.
              </h2>
              <p>
                Pick a person. You sign in through the real identity providers of two demo companies, without a password. Demo data resets
                every night.
              </p>
              <form
                action="/auth/demo"
                method="post"
                className="gh-demo-personas"
                aria-busy={submitting}
                onSubmit={(event) => {
                  // A second click would start a second sign-in; the clicked button's value must still be sent, so no `disabled`.
                  if (submitting) event.preventDefault();
                  setSubmitting(true);
                }}
              >
                <input type="hidden" name="returnTo" value={safeReturnTo(params.get("returnTo"))} />
                {demoPersonas?.map((persona) => (
                  <button key={persona.email} type="submit" name="persona" value={persona.email}>
                    <span>
                      <b>{persona.name}</b>
                      <small>
                        {persona.role} · {persona.tenant}
                      </small>
                    </span>
                    <ArrowRight size={16} />
                  </button>
                ))}
                {submitting && <p className="gh-demo-wait">Signing in through the demo identity provider…</p>}
              </form>
              <p className="gh-demo-or">or sign in with a work email</p>
              {emailForm}
            </>
          ) : (
            <>
              <p className="gh-eyebrow">WELCOME TO GATEHOUSE</p>
              <h2>
                Your workspace
                <br />
                starts here.
              </h2>
              <p>Enter your work email to continue with your organization’s secure sign-in.</p>
              {emailForm}
            </>
          )}
          <div className="gh-login-assurance">
            <ShieldCheck size={16} />
            <span>
              Your organization verifies your identity.
              <br />
              Your password stays with your identity provider.
            </span>
          </div>
          <div className="gh-login-steps">
            <span>
              <b>1</b> Your email
            </span>
            <i />
            <span>
              <b>2</b> Secure sign-in
            </span>
            <i />
            <span>
              <Check size={14} /> You’re in
            </span>
          </div>
        </div>
        <footer>
          Gatehouse <span>Physical access, thoughtfully governed.</span>
        </footer>
      </section>
    </div>
  );
}
