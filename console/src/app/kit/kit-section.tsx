import type { ReactNode } from "react";

/** One kit section: an `h2`, a line on what it shows, and the component on a surface. */
export function KitSection({
  id,
  title,
  description,
  children,
}: {
  id: string;
  title: string;
  description: string;
  children: ReactNode;
}) {
  return (
    <section aria-labelledby={id} className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <h2 id={id} className="text-h2 text-ink">
          {title}
        </h2>
        <p className="text-body text-ink-muted">{description}</p>
      </div>
      <div className="rounded-lg border border-border bg-surface p-4">
        {children}
      </div>
    </section>
  );
}
