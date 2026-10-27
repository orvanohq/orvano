import { useLingui } from "@lingui/react/macro";
import { Label, ProgressBar } from "react-aria-components";
import { cx } from "./cx";
import { Icon } from "./icon";
import { PendingIcon } from "./icons";

export interface ProgressProps {
  /** What is in progress ("Uploading backup.sql"). */
  label: string;
  /** 0 to 100. Leave it out while the amount isn't known: the bar is indeterminate. */
  value?: number;
}

/**
 * design.md _Feedback and states_, spec 0001 UX-21: progress of a server job or an upload that takes longer than 10
 * seconds, with the percentage when it's known.
 */
export function Progress({ label, value }: ProgressProps) {
  const { t } = useLingui();
  const indeterminate = value === undefined;
  return (
    <ProgressBar
      value={value}
      isIndeterminate={indeterminate}
      // An indeterminate bar has no value text of its own; screen readers hear "Loading".
      valueLabel={indeterminate ? t`Loading` : undefined}
      className="flex w-full flex-col gap-1.5"
    >
      {({ percentage, valueText }) => (
        <>
          <div className="flex items-baseline justify-between gap-3">
            <Label className="font-medium text-fg">{label}</Label>
            {indeterminate ? null : <span className="text-sm text-fg-muted">{valueText}</span>}
          </div>
          <div className="h-1.5 w-full overflow-hidden rounded-full bg-bg-muted">
            <div
              className={cx(
                "h-full rounded-full bg-accent",
                indeterminate && "w-1/3 animate-pulse motion-reduce:animate-none",
              )}
              style={indeterminate ? undefined : { width: `${String(percentage ?? 0)}%` }}
            />
          </div>
        </>
      )}
    </ProgressBar>
  );
}

export interface SpinnerProps {
  /** What is loading ("Loading tables"): a spinner has no visible label. */
  "aria-label": string;
}

/** Spec 0001 UX-21: inside buttons and small inline controls only; content areas use a skeleton. */
export function Spinner({ "aria-label": label }: SpinnerProps) {
  return (
    <ProgressBar isIndeterminate aria-label={label} className="inline-flex text-fg-muted">
      <span className="animate-spin motion-reduce:animate-none">
        <Icon icon={PendingIcon} />
      </span>
    </ProgressBar>
  );
}
