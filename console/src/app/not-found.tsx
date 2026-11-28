import type { Metadata } from "next";
import Link from "next/link";

import { notFoundTitle } from "@/shell/nav";

export const metadata: Metadata = { title: notFoundTitle };

/** Exported as 404.html; the host answers every unknown path under /console with it. No rail. */
export default function NotFound() {
  return (
    <div className="mx-auto flex w-full max-w-7xl flex-col gap-2 py-12">
      <h1 className="text-h1 text-ink">{notFoundTitle}</h1>
      <p className="text-body text-ink-muted">
        Check the address, or go back to{" "}
        <Link
          href="/"
          className="rounded-sm font-medium text-brand underline-offset-4 outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
        >
          your projects
        </Link>
        .
      </p>
    </div>
  );
}
