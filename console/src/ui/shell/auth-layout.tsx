import type { ReactNode } from "react";
import { BrandLink } from "./brand-mark";

/**
 * Spec 0015 design.md _Shell_: the frame for pages without the shell (sign in, sign up, first run setup). The brand
 * mark over a centered 400 px card on `bg-subtle`. It needs no session, so it renders before one exists.
 */
export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-full flex-col items-center justify-center gap-6 bg-bg-subtle px-4 py-10">
      <header>
        <BrandLink />
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
