import type { ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";
import type { Tone } from "./banner";
import { cx } from "./cx";
import { Icon } from "./icon";
import {
  StatusCancelledIcon,
  StatusFailedIcon,
  StatusQueuedIcon,
  StatusReadyIcon,
  StatusRunningIcon,
  type IconComponent,
} from "./icons";

const toneLooks: Record<Tone, string> = {
  neutral: cx("bg-bg-muted text-fg-muted"),
  info: cx("bg-info-subtle text-info-text"),
  success: cx("bg-success-subtle text-success-text"),
  warning: cx("bg-warning-subtle text-warning-text"),
  danger: cx("bg-danger-subtle text-danger-text"),
};

export type JobStatus = "queued" | "running" | "ready" | "failed" | "cancelled";

const statuses: Record<JobStatus, { tone: Tone; icon: IconComponent }> = {
  queued: { tone: "neutral", icon: StatusQueuedIcon },
  running: { tone: "info", icon: StatusRunningIcon },
  ready: { tone: "success", icon: StatusReadyIcon },
  failed: { tone: "danger", icon: StatusFailedIcon },
  cancelled: { tone: "neutral", icon: StatusCancelledIcon },
};

/** design.md _StatusChip statuses_, spec 0001 UX-29: a job's status, always as a label plus an icon. */
export function StatusChip({ status }: { status: JobStatus }) {
  const { t } = useLingui();
  const labels: Record<JobStatus, string> = {
    queued: t`Queued`,
    running: t`Running`,
    ready: t`Ready`,
    failed: t`Failed`,
    cancelled: t`Cancelled`,
  };
  const look = statuses[status];
  return (
    <span
      className={cx(
        "inline-flex h-6 items-center gap-1 rounded-sm px-1.5 text-sm font-medium whitespace-nowrap",
        toneLooks[look.tone],
      )}
    >
      <span
        className={cx(status === "running" && "animate-spin motion-reduce:animate-none", "flex")}
      >
        <Icon icon={look.icon} />
      </span>
      {labels[status]}
    </span>
  );
}

export interface BadgeProps {
  tone?: Tone;
  /** A count or a short label. */
  children: ReactNode;
}

/** design.md _Feedback and states_: a count or short label on a `-subtle` surface in the tone's `-text` color. */
export function Badge({ tone = "neutral", children }: BadgeProps) {
  return (
    <span
      className={cx(
        "inline-flex h-5 items-center rounded-sm px-1.5 text-xs font-medium whitespace-nowrap",
        toneLooks[tone],
      )}
    >
      {children}
    </span>
  );
}
