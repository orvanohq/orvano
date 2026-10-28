import type { ReactNode } from "react";
import { Trans, useLingui } from "@lingui/react/macro";
import { Button } from "./button";
import { cx } from "./cx";
import { Icon } from "./icon";
import {
  ErrorIcon,
  InfoIcon,
  RetryIcon,
  SuccessIcon,
  WarningIcon,
  type IconComponent,
} from "./icons";
import { useErrorCopy } from "./error-copy";

export type Tone = "neutral" | "info" | "success" | "warning" | "danger";

const tones: Record<Tone, { surface: string; edge: string; text: string; icon: IconComponent }> = {
  neutral: {
    surface: "bg-bg-muted",
    edge: "border-s-border-strong",
    text: "text-fg-muted",
    icon: InfoIcon,
  },
  info: {
    surface: "bg-info-subtle",
    edge: "border-s-info-text",
    text: "text-info-text",
    icon: InfoIcon,
  },
  success: {
    surface: "bg-success-subtle",
    edge: "border-s-success-text",
    text: "text-success-text",
    icon: SuccessIcon,
  },
  warning: {
    surface: "bg-warning-subtle",
    edge: "border-s-warning-text",
    text: "text-warning-text",
    icon: WarningIcon,
  },
  danger: {
    surface: "bg-danger-subtle",
    edge: "border-s-danger-text",
    text: "text-danger-text",
    icon: ErrorIcon,
  },
};

export interface BannerProps {
  tone?: Tone;
  /** A short summary, bold, before the text. */
  title?: string;
  children: ReactNode;
  /** One action at the inline end ("Upgrade plan", "Retry"). */
  action?: ReactNode;
}

/**
 * design.md _Feedback and states_, spec 0001 UX-24: state that affects the whole project (limits reached, service
 * degraded, offline). It stays until the state clears; the shell shows one at a time under the top bar
 * (`AppShell`'s `banner`). One of the console's allowed live regions (AC-12): `status`, or `alert` for danger.
 */
export function Banner({ tone = "info", title, children, action }: BannerProps) {
  const look = tones[tone];
  return (
    <div
      role={tone === "danger" ? "alert" : "status"}
      className={cx(
        "flex flex-wrap items-start gap-x-3 gap-y-2 border-s-3 px-4 py-3 text-fg",
        look.surface,
        look.edge,
      )}
    >
      <span className={look.text}>
        <Icon icon={look.icon} size={20} />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-0.5 pt-0.5 wrap-anywhere">
        {title ? <p className="font-semibold">{title}</p> : null}
        <div>{children}</div>
      </div>
      {action ? <div className="shrink-0">{action}</div> : null}
    </div>
  );
}

export interface InlineErrorProps {
  /** The failure; its kind picks the wording (UX-22). `children` replaces that wording. */
  error?: unknown;
  /** What failed ("Couldn't save the table"), for a plain failure. */
  title?: string;
  children?: ReactNode;
  onRetry?: () => void;
}

/**
 * design.md _Feedback and states_, spec 0001 UX-24: an error shown next to its source (a form, card, cell, or panel).
 * `role="alert"`, and it never closes by itself.
 */
export function InlineError({ error, title, children, onRetry }: InlineErrorProps) {
  const { t } = useLingui();
  const copy = useErrorCopy(error, title);
  const requestId = copy.details.requestId;
  return (
    <div
      role="alert"
      className="flex flex-wrap items-start gap-x-2 gap-y-2 rounded-md border border-s-3 border-s-danger-text bg-danger-subtle px-3 py-2 text-fg"
    >
      <span className="mt-0.5 text-danger-text">
        <Icon icon={ErrorIcon} />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-0.5 wrap-anywhere">
        <p className="font-semibold">{copy.title}</p>
        <p>{children ?? copy.body}</p>
        {requestId ? (
          <p className="text-sm text-fg-muted">
            <Trans>
              Request ID: <span className="font-mono select-all">{requestId}</span>
            </Trans>
          </p>
        ) : null}
      </div>
      {onRetry ? (
        <Button size="sm" onPress={onRetry}>
          <Icon icon={RetryIcon} />
          {t`Retry`}
        </Button>
      ) : null}
    </div>
  );
}
