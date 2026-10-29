import { Plural, Trans, useLingui } from "@lingui/react/macro";
// Spec 0015 PRM-8: React Aria 1.22 still exports its toast parts with the UNSTABLE_ prefix. This module is their only
// user, so the rest of the console sees `notify` and `ToastRegion` and never the prefix.
import {
  Text,
  UNSTABLE_Toast as AriaToast,
  UNSTABLE_ToastContent as AriaToastContent,
  UNSTABLE_ToastQueue as ToastQueue,
  UNSTABLE_ToastRegion as AriaToastRegion,
  type QueuedToast,
} from "react-aria-components";
import { createStore, useStore } from "@/shared/stores/create-store";
import { addMessage, resetMessagesForTests, type MessageTone } from "@/shared/stores/messages";
import { IconButton } from "../button";
import { cx, focusRing } from "../cx";
import { Icon } from "../icon";
import { CloseIcon, ErrorIcon, InfoIcon, SuccessIcon, type IconComponent } from "../icons";

export type { MessageTone as ToastTone } from "@/shared/stores/messages";

export interface NotifyOptions {
  tone: MessageTone;
  /** What happened, already translated (`t\`Table created\``). */
  message: string;
  /** Where it came from ("Deployments", "Copy"): kept in the history, and part of what makes two toasts identical. */
  source: string;
}

type ToastContent = NotifyOptions;

/** Spec 0001 UX-24: success (and info) toasts close after 6 s; React Aria pauses the timer on hover and focus. */
export const TOAST_TIMEOUT_MS = 6000;

const queue = new ToastQueue<ToastContent>({ maxVisibleToasts: 3 });

// How many times each visible toast was raised (UX-24 "identical toasts merge and show a count"), by toast key.
const counts = createStore<Readonly<Record<string, number>>>({});

/**
 * Spec 0015 PRM-8: the only way to raise a message. It goes into the history (the last 50), merges with an identical
 * visible toast, or joins React Aria's queue (three visible, newest on top). Nothing else announces it: React Aria's
 * toast region does. Errors stay until dismissed.
 */
export function notify(options: NotifyOptions): void {
  addMessage(options);
  const same = queue.visibleToasts.find(
    ({ content }) =>
      content.tone === options.tone &&
      content.message === options.message &&
      content.source === options.source,
  );
  // UX-24 "identical toasts merge and show a count". The merged toast replaces the old one rather than bumping it in
  // place, so its timer starts again and the region announces it again: a repeat is never silent.
  const count = same ? (counts.get()[same.key] ?? 1) + 1 : 1;
  if (same) queue.close(same.key);
  const key = queue.add(
    { ...options },
    {
      timeout: options.tone === "error" ? undefined : TOAST_TIMEOUT_MS,
      onClose: () => {
        counts.set((current) =>
          Object.fromEntries(Object.entries(current).filter(([entry]) => entry !== key)),
        );
      },
    },
  );
  if (count > 1) counts.set((current) => ({ ...current, [key]: count }));
}

/** Test only: closes every toast and forgets the history. */
export function resetToastsForTests(): void {
  queue.clear();
  counts.set({});
  resetMessagesForTests();
}

const tones: Record<MessageTone, { icon: IconComponent; edge: string; text: string }> = {
  success: { icon: SuccessIcon, edge: "border-s-success-text", text: "text-success-text" },
  error: { icon: ErrorIcon, edge: "border-s-danger-text", text: "text-danger-text" },
  info: { icon: InfoIcon, edge: "border-s-info-text", text: "text-info-text" },
};

/**
 * design.md _Feedback and states_: the toast region, at the bottom inline end (bottom left in right to left), above
 * everything but tooltips. F6 reaches it (React Aria's landmark). While toasts show, it publishes its height as
 * `--toast-inset`, which the shell's scroll padding uses so a toast never covers the focused element (SHL-8).
 * Mount it once, at the root.
 */
export function ToastRegion() {
  const { t } = useLingui();
  return (
    <AriaToastRegion
      queue={queue}
      aria-label={t`Notifications`}
      ref={publishInset}
      className="fixed end-4 bottom-4 z-(--z-toast) flex w-toast max-w-[calc(100vw-2rem)] flex-col gap-2 outline-none"
    >
      {({ toast }) => <Toast toast={toast} />}
    </AriaToastRegion>
  );
}

function Toast({ toast }: { toast: QueuedToast<ToastContent> }) {
  const { t } = useLingui();
  const count = useStore(counts, (current) => current[toast.key] ?? 1);
  const { tone: toneName, message, source } = toast.content;
  const tone = tones[toneName];
  return (
    <AriaToast
      toast={toast}
      className={cx(
        "flex items-start gap-2 rounded-md border border-s-3 bg-bg-raised p-3 text-fg shadow-sm",
        tone.edge,
        focusRing,
      )}
    >
      <span className={cx("mt-0.5", tone.text)}>
        <Icon icon={tone.icon} />
      </span>
      <AriaToastContent className="flex min-w-0 flex-1 flex-col gap-0.5">
        <Text slot="title" className="break-words">
          {message}
        </Text>
        <Text slot="description" className="text-sm text-fg-muted">
          {count > 1 ? (
            <Trans>
              {source}, <Plural value={count} one="# time" other="# times" />
            </Trans>
          ) : (
            source
          )}
        </Text>
      </AriaToastContent>
      <IconButton slot="close" icon={CloseIcon} size="sm" aria-label={t`Dismiss`} />
    </AriaToast>
  );
}

/** Keeps `--toast-inset` on `<html>` equal to the region's height plus its gap, and 0 once it's gone. */
function publishInset(element: HTMLDivElement | null) {
  if (!element) return;
  const root = document.documentElement;
  const observer = new ResizeObserver(() => {
    root.style.setProperty("--toast-inset", `${String(element.offsetHeight + 16)}px`);
  });
  observer.observe(element);
  return () => {
    observer.disconnect();
    root.style.setProperty("--toast-inset", "0px");
  };
}
