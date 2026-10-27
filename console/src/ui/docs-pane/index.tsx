import { useEffect, useEffectEvent, useId, useLayoutEffect, useRef } from "react";
import { Trans, useLingui } from "@lingui/react/macro";
import { DOCS_FALLBACK_URL } from "@/shared/config";
import { IconButton } from "../button";
import { CloseIcon } from "../icons";
import { ExternalLink } from "../link";
import { Drawer } from "../shell/drawer";

// Spec 0015 PRM-10: imported by path (`@/ui/docs-pane`) and loaded lazily where it first appears.

export interface DocsPaneProps {
  isOpen: boolean;
  /** Called with false by Escape, the close button, or (as a drawer) a press outside. */
  onOpenChange: (isOpen: boolean) => void;
  /** From 64rem the pane sits beside the content; below, it is a drawer (`useIsWide`, SHL-6). */
  isWide: boolean;
  /**
   * Called once the pane has closed and is gone (a drawer after React Aria restored focus), so the shell can return
   * focus to the help button (SHL-9). Not called when the pane only moves between drawer and region.
   */
  onClosed: () => void;
}

/**
 * Spec 0015 SHL-9, spec 0001 UX-31: the help pane at the inline end. From 64rem it is a complementary region labeled
 * "Help" that takes 400 px from the content (nothing is covered); below 64rem it is a full width drawer from the inline
 * end. Opening it moves focus to its heading. Until feature 32 bundles the docs, it links to the docs fallback.
 * The shell returns focus to the help button when it closes.
 */
export function DocsPane({ isOpen, onOpenChange, isWide, onClosed }: DocsPaneProps) {
  const { t } = useLingui();
  // Read a frame after the content unmounts: by then this holds the latest `isOpen`, so moving between drawer and
  // region (still open) doesn't count as closing.
  const open = useRef(isOpen);
  useLayoutEffect(() => {
    open.current = isOpen;
  });
  const onGone = () => {
    if (!open.current) onClosed();
  };
  if (!isWide) {
    return (
      <Drawer
        isOpen={isOpen}
        onOpenChange={onOpenChange}
        aria-label={t`Help`}
        side="end"
        width="full"
      >
        <DocsContent
          onGone={onGone}
          onClose={() => {
            onOpenChange(false);
          }}
        />
      </Drawer>
    );
  }
  if (!isOpen) return null;
  return (
    <DocsContent
      region
      onGone={onGone}
      onClose={() => {
        onOpenChange(false);
      }}
    />
  );
}

function DocsContent({
  region = false,
  onClose,
  onGone,
}: {
  region?: boolean;
  onClose: () => void;
  onGone: () => void;
}) {
  const { t } = useLingui();
  const headingId = useId();
  const heading = useRef<HTMLHeadingElement>(null);
  const gone = useEffectEvent(onGone);
  const close = useEffectEvent(onClose);
  const aside = useRef<HTMLElement>(null);

  // A region beside the content gets no Escape from React Aria, so the pane handles it (SHL-9).
  useEffect(() => {
    const element = aside.current;
    if (!element) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape" || event.defaultPrevented) return;
      event.preventDefault();
      close();
    };
    element.addEventListener("keydown", onKeyDown);
    return () => {
      element.removeEventListener("keydown", onKeyDown);
    };
  }, []);

  // SHL-9: focus starts on the heading, so the pane is announced by name. When the pane closes, focus goes back in
  // the next frame, after a drawer's own focus restore.
  useEffect(() => {
    heading.current?.focus();
    return () => {
      requestAnimationFrame(() => {
        gone();
      });
    };
  }, []);

  const body = (
    <>
      <div className="flex items-center justify-between gap-3 border-b px-4 py-2">
        <h2
          id={headingId}
          ref={heading}
          tabIndex={-1}
          className="rounded-sm text-lg font-semibold outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
          {t`Help`}
        </h2>
        <IconButton icon={CloseIcon} aria-label={t`Close help`} onPress={onClose} />
      </div>
      <div className="flex flex-col gap-3 overflow-y-auto px-4 py-4">
        <p>
          <Trans>
            Guides for each service will appear here. Until then, the Orvano documentation is on
            GitHub.
          </Trans>
        </p>
        <p>
          <ExternalLink href={DOCS_FALLBACK_URL}>{t`Open the Orvano docs`}</ExternalLink>
        </p>
      </div>
    </>
  );

  if (!region) return body;
  return (
    <aside
      ref={aside}
      aria-labelledby={headingId}
      className="flex w-docs-pane shrink-0 flex-col border-s bg-bg"
    >
      {body}
    </aside>
  );
}
