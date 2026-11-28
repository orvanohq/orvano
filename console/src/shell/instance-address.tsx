"use client";

import { useSyncExternalStore } from "react";

const noSubscription = () => () => {};

/**
 * The instance address, read after hydration. Until then a fixed width placeholder holds its place, so the
 * prerendered HTML and the first client render match.
 */
export function InstanceAddress() {
  const host = useSyncExternalStore(
    noSubscription,
    () => window.location.host,
    () => null,
  );

  if (host === null) {
    return <span aria-hidden="true" className="inline-block h-4 w-24" />;
  }
  return (
    <span className="truncate font-mono text-mono-small text-ink-muted">
      {host}
    </span>
  );
}
