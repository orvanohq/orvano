import { useId, type ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";
import { Dialog, Heading, Modal, ModalOverlay } from "react-aria-components";
import { IconButton } from "../button";
import { CloseIcon } from "../icons";
import { confirmDiscard, UnsavedScopeContext } from "../form/unsaved-changes";

// Spec 0015 PRM-10: imported by path (`@/ui/side-sheet`) and loaded lazily where it first appears.

export interface SideSheetProps {
  /** The visible title, which names the sheet ("New bucket", "Edit webhook"). */
  title: string;
  isOpen: boolean;
  /** Called with false only after any unsaved changes inside were confirmed as discarded. */
  onOpenChange: (isOpen: boolean) => void;
  /** The body, usually one `Form` (at most 8 fields in 2 sections, UX-6). */
  children: ReactNode;
  /** The actions: Cancel (`slot="close"`, which asks first too) then the submit, at the inline end (UX-36). */
  footer?: ReactNode;
  /**
   * The search params that keep this sheet open, so the unsaved changes guard asks when one of them changes.
   * Defaults to `sheet` (spec 0003 RTE-8).
   */
  watchSearch?: readonly string[];
}

/**
 * design.md _Overlays_, spec 0001 UX-6: the modal create and edit sheet. It slides from the inline end (MOT-1; reduced
 * motion makes it instant), is 480 px wide and full width below 640 px, traps focus, and leaves the page behind
 * visible but inert. Escape, a press outside, Cancel, and the close button all ask first when a form inside has
 * unsaved changes (UX-9); the question opens over the sheet, and focus returns to the sheet if you keep editing.
 */
export function SideSheet({
  title,
  isOpen,
  onOpenChange,
  children,
  footer,
  watchSearch = ["sheet"],
}: SideSheetProps) {
  const { t } = useLingui();
  const scope = useId();

  const close = async () => {
    if (await confirmDiscard(scope)) onOpenChange(false);
  };

  return (
    <ModalOverlay
      data-modal // SHL-10: shell shortcuts do nothing while a modal is open
      isOpen={isOpen}
      onOpenChange={(open) => {
        if (open) onOpenChange(true);
        else void close();
      }}
      isDismissable
      className="fixed inset-0 z-(--z-modal) bg-scrim transition-opacity duration-(--duration-slow) ease-standard data-entering:opacity-0 data-exiting:opacity-0"
    >
      <Modal className="fixed inset-y-0 end-0 flex w-full flex-col border-s bg-bg-raised text-fg shadow-lg transition-transform duration-(--duration-slow) ease-standard data-entering:translate-x-full data-exiting:translate-x-full sm:w-sheet sm:rounded-s-lg rtl:data-entering:-translate-x-full rtl:data-exiting:-translate-x-full">
        <Dialog className="flex min-h-0 flex-1 flex-col outline-none">
          <UnsavedScopeContext value={{ id: scope, watch: watchSearch }}>
            <div className="flex items-center justify-between gap-3 border-b px-5 py-3">
              <Heading slot="title" className="text-lg font-semibold">
                {title}
              </Heading>
              <IconButton icon={CloseIcon} aria-label={t`Close`} slot="close" />
            </div>
            <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>
            {footer ? (
              <div className="flex flex-wrap justify-end gap-2 border-t px-5 py-4">{footer}</div>
            ) : null}
          </UnsavedScopeContext>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
