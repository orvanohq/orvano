import { useSyncExternalStore } from "react";
import { Focusable } from "react-aria-components";
import { formatDateTime, formatRelativeTime } from "@/shared/format";
import { Tooltip } from "./tooltip";

const UTC = "UTC";

/** Spec 0001 UX-34: relative times refresh every minute. */
export const RELATIVE_TIME_REFRESH_MS = 60_000;

// One clock for every RelativeTime on screen: it ticks while at least one is mounted.
const clock = {
  now: Date.now(),
  listeners: new Set<() => void>(),
  timer: undefined as ReturnType<typeof setInterval> | undefined,
};

function subscribe(listener: () => void) {
  clock.listeners.add(listener);
  if (clock.timer === undefined) {
    clock.timer = setInterval(() => {
      clock.now = Date.now();
      for (const notify of clock.listeners) notify();
    }, RELATIVE_TIME_REFRESH_MS);
  }
  return () => {
    clock.listeners.delete(listener);
    if (clock.listeners.size === 0) {
      clearInterval(clock.timer);
      clock.timer = undefined;
    }
  };
}

/** The clock's time. While it isn't ticking (nothing was mounted), a stale reading is refreshed once. */
function readClock() {
  if (clock.timer === undefined && Date.now() - clock.now > 1000) clock.now = Date.now();
  return clock.now;
}

export interface RelativeTimeProps {
  /** The moment, as a `Date` or an ISO 8601 string from the API. */
  datetime: Date | string;
  /**
   * Takes focus so the exact time shows on focus as well as hover (UX-34). Pass `false` in tables and lists, where a
   * time in every row would be a tab stop with no action: the exact time is then the element's `title`.
   */
  focusable?: boolean;
}

/**
 * design.md _Data display_, spec 0015 PRM-11, spec 0001 UX-34: "3 minutes ago" up to 7 days, then a medium date, in a
 * `<time datetime>`. Hover or focus shows the exact local time and the same in UTC. It refreshes every minute. A value
 * that isn't a date shows as it came (an em dash for an invalid `Date`), never an error.
 */
export function RelativeTime({ datetime, focusable = true }: RelativeTimeProps) {
  const now = useSyncExternalStore(subscribe, readClock);
  const date = typeof datetime === "string" ? new Date(datetime) : datetime;
  if (Number.isNaN(date.getTime())) {
    return <span>{typeof datetime === "string" && datetime !== "" ? datetime : "—"}</span>;
  }

  const exact = { dateStyle: "medium", timeStyle: "long" } as const;
  const local = formatDateTime(date, exact);
  const utc = formatDateTime(date, { ...exact, timeZone: UTC });
  const relative = formatRelativeTime(date, new Date(now));
  if (!focusable) {
    return (
      <time dateTime={date.toISOString()} title={`${local}\n${utc}`} className="whitespace-nowrap">
        {relative}
      </time>
    );
  }
  return (
    <Tooltip
      content={
        <span className="flex flex-col">
          <span>{local}</span>
          <span>{utc}</span>
        </span>
      }
    >
      <Focusable>
        <time
          dateTime={date.toISOString()}
          // oxlint-disable-next-line jsx-a11y/no-noninteractive-tabindex -- UX-34: the exact time shows on focus, so it must take focus
          tabIndex={0}
          className="rounded-sm whitespace-nowrap outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-solid focus-visible:outline-focus"
        >
          {relative}
        </time>
      </Focusable>
    </Tooltip>
  );
}
