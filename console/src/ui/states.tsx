import type { ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";
import { Button } from "./button";
import { CopyField } from "./copy-field";
import { Icon } from "./icon";
import {
  EmptyIcon,
  ErrorIcon,
  NoResultsIcon,
  NotAllowedIcon,
  RetryIcon,
  type IconComponent,
} from "./icons";
import { useErrorCopy } from "./error-copy";

export type EmptyStateKind = "first-use" | "no-results" | "not-allowed";

const emptyIcons: Record<EmptyStateKind, IconComponent> = {
  "first-use": EmptyIcon,
  "no-results": NoResultsIcon,
  "not-allowed": NotAllowedIcon,
};

export interface EmptyStateProps {
  /**
   * Spec 0001 UX-20: `first-use` (a short positive title, one sentence, one primary action, and the SDK quick start
   * as `children`), `no-results` (say so and offer Clear filters), or `not-allowed` (the reason and the next step).
   */
  kind: EmptyStateKind;
  title: string;
  description: string;
  /** One action: the primary action, Clear filters, or the next step. */
  action?: ReactNode;
  /** More below the action, such as the SDK quick start (UX-37). */
  children?: ReactNode;
}

/** design.md _Feedback and states_: what to do when there's nothing to show. The icon is decorative. */
export function EmptyState({ kind, title, description, action, children }: EmptyStateProps) {
  return (
    <section className="flex flex-col items-center gap-3 px-4 py-10 text-center">
      <span className="flex size-10 items-center justify-center rounded-full bg-bg-muted text-fg-muted">
        <Icon icon={emptyIcons[kind]} size={20} />
      </span>
      <h2 className="text-md font-semibold text-fg">{title}</h2>
      <p className="max-w-prose text-fg-muted">{description}</p>
      {action ? <div className="pt-1">{action}</div> : null}
      {children ? <div className="w-full max-w-content pt-3 text-start">{children}</div> : null}
    </section>
  );
}

export interface ErrorStateProps {
  /** The failure (spec 0003 FLT-1's `ApiError`, or anything thrown): its kind picks the wording (UX-22). */
  error: unknown;
  /** What failed ("Couldn't load tables"), for a plain failure. */
  title?: string;
  /** Shown for a plain failure (FLT-2: `reset()` from `useQueryErrorResetBoundary`, then `router.invalidate()`). */
  onRetry?: () => void;
  /** For "not found": the link back to the resource's list. */
  backLink?: ReactNode;
}

/**
 * design.md _Feedback and states_, spec 0001 UX-22: a page or panel that failed to load, inside the shell. It says
 * what failed and what to do, offers Retry, and shows the copyable request ID; "not found" links back to its list,
 * and "forbidden" names the role needed when the API gives it.
 */
export function ErrorState({ error, title, onRetry, backLink }: ErrorStateProps) {
  const { t } = useLingui();
  const copy = useErrorCopy(error, title);
  const kind = copy.details.kind;
  const retryable = kind !== "not_found" && kind !== "forbidden";
  return (
    <section className="flex flex-col items-center gap-3 px-4 py-10 text-center">
      <span className="flex size-10 items-center justify-center rounded-full bg-danger-subtle text-danger-text">
        <Icon icon={kind === "forbidden" ? NotAllowedIcon : ErrorIcon} size={20} />
      </span>
      <h2 className="text-md font-semibold text-fg">{copy.title}</h2>
      <p className="max-w-prose text-fg-muted">{copy.body}</p>
      {kind === "not_found" && backLink ? <div className="pt-1">{backLink}</div> : null}
      {retryable && onRetry ? (
        <div className="pt-1">
          <Button onPress={onRetry}>
            <Icon icon={RetryIcon} />
            {t`Retry`}
          </Button>
        </div>
      ) : null}
      {copy.details.requestId ? (
        <div className="w-full max-w-sm pt-3 text-start">
          <CopyField label={t`Request ID`} value={copy.details.requestId} />
        </div>
      ) : null}
    </section>
  );
}
