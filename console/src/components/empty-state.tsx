import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

/** An `h2`, one sentence, and (when there is something to do) one primary action. No display type. */
export function EmptyState({
  icon: Icon,
  title,
  children,
  action,
}: {
  icon?: LucideIcon;
  title: string;
  children: ReactNode;
  action?: ReactNode;
}) {
  return (
    <section className="flex flex-col items-center gap-3 rounded-lg border border-border bg-surface px-6 py-12 text-center">
      {Icon && (
        <span
          aria-hidden="true"
          className="flex size-10 items-center justify-center rounded-md bg-surface-subtle text-ink-muted"
        >
          <Icon className="size-5" strokeWidth={1.5} />
        </span>
      )}
      <h2 className="text-h2 text-ink">{title}</h2>
      <p className="max-w-prose text-body text-ink-muted">{children}</p>
      {action && <div className="mt-2">{action}</div>}
    </section>
  );
}
