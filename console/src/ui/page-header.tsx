import type { ReactNode } from "react";

export interface PageHeaderProps {
  title: string;
  description?: ReactNode;
  /** The page's one primary action, and any secondary ones before it, at the inline end (UX-36). */
  actions?: ReactNode;
}

/**
 * Spec 0001 UX-36: the page title (the page's one `h1`, `tabIndex={-1}` so RTE-7 can move focus to it after
 * navigation), an optional description, and the actions at the inline end. It wraps instead of overflowing.
 */
export function PageHeader({ title, description, actions }: PageHeaderProps) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
      <div className="flex min-w-0 flex-col gap-1">
        <h1 tabIndex={-1} className="text-xl font-semibold break-words outline-none">
          {title}
        </h1>
        {description && <p className="max-w-content text-fg-muted">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
