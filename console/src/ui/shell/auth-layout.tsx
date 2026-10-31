import type { ReactNode } from "react";
import { BrandLink } from "./brand-mark";

/**
 * Spec 0015 design.md _Shell_: the frame for pages without the shell (sign in, sign up, first run setup). The brand
 * mark over a centered 400 px card on `bg-subtle`. It needs no session, so it renders before one exists.
 * `instanceName` is what this install calls itself (spec 0017 BOT-1), shown under the mark when it isn't the default.
 */
export function AuthLayout({
  children,
  instanceName,
}: {
  children: ReactNode;
  instanceName?: string;
}) {
  return (
    <div className="flex min-h-full flex-col items-center justify-center gap-6 bg-bg-subtle px-4 py-10">
      <header className="flex flex-col items-center gap-1">
        <BrandLink />
        {instanceName && instanceName !== "Orvano" ? (
          <p className="text-sm text-fg-muted">{instanceName}</p>
        ) : null}
      </header>
      <main
        id="main"
        tabIndex={-1}
        className="flex w-full max-w-auth flex-col gap-4 rounded-lg border bg-bg-raised p-6 shadow-sm outline-none"
      >
        {children}
      </main>
    </div>
  );
}
