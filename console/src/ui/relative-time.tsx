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
}

/**
 * design.md _Data display_, spec 0015 PRM-11, spec 0001 UX-34: "3 minutes ago" up to 7 days, then a medium date, in a
 * `<time datetime>`. Hover or focus shows the exact local time and the same in UTC. It refreshes every minute.
 */
export function RelativeTime({ datetime }: RelativeTimeProps) {
  const now = useSyncExternalStore(subscribe, readClock);
  const date = typeof datetime === "string" ? new Date(datetime) : datetime;
  const exact = { dateStyle: "medium", timeStyle: "long" } as const;
  return (
    <Tooltip
      content={
        <span className="flex flex-col">
          <span>{formatDateTime(date, exact)}</span>
          <span>{formatDateTime(date, { ...exact, timeZone: UTC })}</span>
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
          {formatRelativeTime(date, new Date(now))}
        </time>
      </Focusable>
    </Tooltip>
  );
}
