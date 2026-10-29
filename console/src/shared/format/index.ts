import { i18n } from "@/shared/i18n";

// Spec 0003 I18N-5: dates, times, and relative times are formatted only here, through Intl for the active locale,
// with each formatter cached per locale and options. No date library. Numbers, lists, and sizes join as screens
// need them.

const cache = new Map<string, Intl.DateTimeFormat | Intl.RelativeTimeFormat>();

function cached<Formatter extends Intl.DateTimeFormat | Intl.RelativeTimeFormat>(
  key: string,
  create: () => Formatter,
): Formatter {
  let formatter = cache.get(key) as Formatter | undefined;
  if (!formatter) {
    formatter = create();
    cache.set(key, formatter);
  }
  return formatter;
}

function locale(): string {
  return i18n.locale || "en";
}

export function formatDateTime(date: Date, options: Intl.DateTimeFormatOptions): string {
  const active = locale();
  return cached(
    `date:${active}:${JSON.stringify(options)}`,
    () => new Intl.DateTimeFormat(active, options),
  ).format(date);
}

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/** Spec 0001 UX-34: relative text ("3 minutes ago", "in 5 minutes") reaches this far; past it, a date shows. */
export const RELATIVE_LIMIT_MS = 7 * DAY;

/**
 * Spec 0015 PRM-11, spec 0001 UX-34: "now", "3 minutes ago", "yesterday", "in 2 hours" through
 * `Intl.RelativeTimeFormat` (`numeric: "auto"`) up to 7 days away, then the date at `dateStyle: "medium"`.
 */
export function formatRelativeTime(date: Date, now: Date): string {
  const diff = date.getTime() - now.getTime();
  const distance = Math.abs(diff);
  if (distance >= RELATIVE_LIMIT_MS) return formatDateTime(date, { dateStyle: "medium" });

  const active = locale();
  const relative = cached(
    `relative:${active}`,
    () => new Intl.RelativeTimeFormat(active, { numeric: "auto" }),
  );
  // Whole units only, rounded toward zero, so 59.6 minutes is "59 minutes ago" and the step to "1 hour ago" comes at
  // the hour, not 30 seconds before it.
  if (distance < MINUTE) return relative.format(0, "second");
  if (distance < HOUR) return relative.format(Math.trunc(diff / MINUTE), "minute");
  if (distance < DAY) return relative.format(Math.trunc(diff / HOUR), "hour");
  return relative.format(Math.trunc(diff / DAY), "day");
}
