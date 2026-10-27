import { useId, type ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";

export interface SettingsCardProps {
  title: string;
  description?: string;
  /** The fields, usually a form from `useAppForm`. */
  children: ReactNode;
  /** The card's action at the inline end: an Update button enabled only when something changed (FRM-5). */
  footer?: ReactNode;
}

/** design.md _Data display_: one group of settings, labeled by its title. */
export function SettingsCard({ title, description, children, footer }: SettingsCardProps) {
  const titleId = useId();
  return (
    <section aria-labelledby={titleId} className="flex flex-col rounded-md border bg-bg">
      <div className="flex flex-col gap-4 p-4 lg:p-6">
        <div className="flex flex-col gap-1">
          <h2 id={titleId} className="text-lg font-semibold text-fg">
            {title}
          </h2>
          {description ? <p className="text-fg-muted">{description}</p> : null}
        </div>
        {children}
      </div>
      {footer ? (
        <div className="flex justify-end gap-2 border-t bg-bg-subtle px-4 py-3 lg:px-6">
          {footer}
        </div>
      ) : null}
    </section>
  );
}

export interface DangerZoneItemProps {
  title: string;
  description: string;
  /** The destructive button (`variant="danger"`), which opens `ConfirmDelete`. */
  action: ReactNode;
}

/**
 * design.md _Data display_, spec 0001 UX-23: the destructive actions of a settings page, apart from its primary
 * actions, behind a `danger` edge.
 */
export function DangerZone({ children }: { children: ReactNode }) {
  const { t } = useLingui();
  const titleId = useId();
  return (
    <section
      aria-labelledby={titleId}
      className="flex flex-col rounded-md border border-s-3 border-s-danger-text bg-bg"
    >
      <h2 id={titleId} className="border-b px-4 py-3 text-lg font-semibold text-fg lg:px-6">
        {t`Danger zone`}
      </h2>
      <div className="flex flex-col divide-y">{children}</div>
    </section>
  );
}

/** One destructive action in a `DangerZone`: what it does, then its button at the inline end. */
export function DangerZoneItem({ title, description, action }: DangerZoneItemProps) {
  return (
    <div className="flex flex-wrap items-center gap-x-6 gap-y-3 px-4 py-4 lg:px-6">
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <h3 className="font-semibold text-fg">{title}</h3>
        <p className="text-fg-muted">{description}</p>
      </div>
      <div className="shrink-0">{action}</div>
    </div>
  );
}
