import { useId, useState } from "react";
import { Plural, useLingui } from "@lingui/react/macro";
import { Button as AriaButton, Dialog, DialogTrigger, Heading } from "react-aria-components";
import { useMessages, type MessageTone } from "@/shared/stores/messages";
import { cx, focusRing } from "../cx";
import { Icon } from "../icon";
import { ActivityIcon, ErrorIcon, InfoIcon, SuccessIcon, type IconComponent } from "../icons";
import { Popover } from "../popover";
import { RelativeTime } from "../relative-time";

const tones: Record<MessageTone, { icon: IconComponent; text: string }> = {
  success: { icon: SuccessIcon, text: "text-success-text" },
  error: { icon: ErrorIcon, text: "text-danger-text" },
  info: { icon: InfoIcon, text: "text-info-text" },
};

/**
 * Spec 0015 AC-12, spec 0001 UX-24 and UX-29: the top bar's activity button. It opens a popover with the running jobs
 * (none until feature 46 adds the jobs list) and the session's message history, the last 50 messages newest first,
 * so a toast that closed can still be read. A dot marks messages that arrived since you last looked.
 */
export function ActivityIndicator() {
  const { t } = useLingui();
  const headingId = useId();
  const messages = useMessages();
  const newest = messages[0]?.id;
  const [seen, setSeen] = useState<string | undefined>(undefined);
  const unread = newest !== undefined && newest !== seen;

  return (
    <DialogTrigger
      // Seen on open and again on close, so messages that arrived while it was open don't bring the dot back.
      onOpenChange={() => {
        setSeen(newest);
      }}
    >
      <AriaButton
        aria-label={unread ? t`Activity, new messages` : t`Activity`}
        className={cx(
          "relative inline-flex size-control cursor-default items-center justify-center rounded-md text-fg data-hovered:bg-bg-muted data-pressed:bg-bg-muted",
          focusRing,
        )}
      >
        <Icon icon={ActivityIcon} />
        {unread && (
          <span
            aria-hidden="true"
            className="absolute end-1.5 top-1.5 size-2 rounded-full bg-accent"
          />
        )}
      </AriaButton>
      <Popover placement="bottom end">
        <Dialog aria-labelledby={headingId} className="flex w-80 max-w-full flex-col outline-none">
          <Heading id={headingId} slot="title" className="border-b px-3 py-2 font-semibold">
            {t`Activity`}
          </Heading>
          <section aria-label={t`Running jobs`} className="border-b px-3 py-2">
            <h3 className="text-xs font-semibold text-fg-muted">{t`Running jobs`}</h3>
            <p className="py-1 text-fg-muted">{t`No running jobs`}</p>
          </section>
          <section aria-label={t`Messages`} className="flex min-h-0 flex-col px-3 py-2">
            <h3 className="text-xs font-semibold text-fg-muted">
              {t`Messages`}{" "}
              {messages.length > 0 && (
                <span className="font-normal">
                  (<Plural value={messages.length} one="# message" other="# messages" />)
                </span>
              )}
            </h3>
            {messages.length === 0 ? (
              <p className="py-1 text-fg-muted">{t`No messages yet`}</p>
            ) : (
              <ul className="-mx-1 flex max-h-80 flex-col overflow-y-auto">
                {messages.map((message) => (
                  <li key={message.id} className="flex items-start gap-2 rounded-md px-1 py-1.5">
                    <span className={cx("mt-0.5", tones[message.tone].text)}>
                      <Icon icon={tones[message.tone].icon} />
                    </span>
                    <span className="flex min-w-0 flex-col">
                      <span className="break-words">{message.message}</span>
                      <span className="text-sm text-fg-muted">
                        {message.source} · <RelativeTime datetime={message.at} />
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </Dialog>
      </Popover>
    </DialogTrigger>
  );
}
