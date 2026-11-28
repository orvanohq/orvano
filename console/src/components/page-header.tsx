import type { ReactNode } from "react";

/** The page `h1`, its one line description in ink-muted, and the page's primary action at the right. */
export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string;
  description: string;
  actions?: ReactNode;
}) {
  return (
    <header className="flex flex-wrap items-start justify-between gap-4">
      <div className="flex min-w-0 flex-col gap-1">
        <h1 className="text-h1 text-ink">{title}</h1>
        <p className="text-body text-ink-muted">{description}</p>
      </div>
      {actions && (
        <div className="flex shrink-0 items-center gap-2">{actions}</div>
      )}
    </header>
  );
}
