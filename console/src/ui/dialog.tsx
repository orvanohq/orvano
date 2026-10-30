import { useId, type ReactNode } from "react";
import {
  Dialog as AriaDialog,
  DialogTrigger as AriaDialogTrigger,
  Heading,
  Modal,
  ModalOverlay,
} from "react-aria-components";
import { cx } from "./cx";

export interface DialogProps {
  /** The visible title, which names the dialog. */
  title: string;
  /** `sm` 400 px, `md` 560 px (TKN-5). */
  size?: "sm" | "md";
  /**
   * `alertdialog` for confirmations (UX-23, the discard question): a press outside doesn't close it, only Cancel or
   * Escape.
   */
  role?: "dialog" | "alertdialog";
  /** Controlled use. Leave both out inside a `DialogTrigger`, which opens it from its button. */
  isOpen?: boolean;
  onOpenChange?: (isOpen: boolean) => void;
  /** Turns off Escape and, for a `dialog`, a press outside, while an action that can't be undone runs (UX-23). */
  isDismissDisabled?: boolean;
  /**
   * The message under the title, linked with `aria-describedby` so a screen reader reads it with the title wherever
   * focus lands (APG alert dialog, SRT-6). An `alertdialog` needs one: what happens and whether it can be undone.
   */
  description?: ReactNode;
  children?: ReactNode;
  /** The actions: Cancel (`slot="close"`) then the primary, at the inline end (UX-36). */
  footer?: ReactNode;
}

const sizes = {
  sm: cx("w-dialog-sm"),
  md: cx("w-dialog-md"),
} as const;

/**
 * design.md _Overlays_, spec 0001 UX-6: a modal dialog for confirmations and one or two field forms. Focus moves
 * into it, Tab stays inside, Escape closes it, and focus returns to what opened it (SRT-6). It fades and scales from
 * 98% (MOT-1; reduced motion makes it instant).
 */
export function Dialog({
  title,
  size = "sm",
  role = "dialog",
  isOpen,
  onOpenChange,
  isDismissDisabled = false,
  description,
  children,
  footer,
}: DialogProps) {
  // Linked by hand rather than through React Aria's `Text slot="description"`, which it links only for an
  // `alertdialog`: this way a plain dialog's description is read too.
  const descriptionId = useId();
  return (
    <ModalOverlay
      data-modal // SHL-10: shell shortcuts do nothing while a modal is open
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable={role === "dialog" && !isDismissDisabled}
      isKeyboardDismissDisabled={isDismissDisabled}
      // A plain scroll container: `m-auto` centers the dialog, and its auto margins fall to zero when it's taller
      // than the window, so its top stays reachable (AC-8 reflow). `items-center` would push the top out of reach.
      className="fixed inset-0 z-(--z-modal) flex overflow-y-auto bg-scrim p-4 transition-opacity duration-(--duration-base) ease-standard data-entering:opacity-0 data-exiting:opacity-0"
    >
      <Modal
        className={cx(
          "m-auto max-w-full rounded-lg border bg-bg-raised text-fg shadow-lg transition-[opacity,scale] duration-(--duration-base) ease-standard data-entering:scale-98 data-exiting:scale-98",
          sizes[size],
        )}
      >
        <AriaDialog
          role={role}
          aria-describedby={description === undefined ? undefined : descriptionId}
          className="flex flex-col outline-none"
        >
          <Heading slot="title" className="px-5 pt-5 text-lg font-semibold">
            {title}
          </Heading>
          <div className="flex flex-col gap-3 px-5 pt-3 pb-5">
            {description === undefined ? null : <p id={descriptionId}>{description}</p>}
            {children}
          </div>
          {footer ? (
            <div className="flex flex-wrap justify-end gap-2 border-t px-5 py-4">{footer}</div>
          ) : null}
        </AriaDialog>
      </Modal>
    </ModalOverlay>
  );
}

export interface DialogTriggerProps {
  /** A design system `Button` that opens the dialog. */
  trigger: ReactNode;
  /** The `Dialog`, uncontrolled. */
  children: ReactNode;
}

/** Opens its `Dialog` from a button and returns focus to it on close. */
export function DialogTrigger({ trigger, children }: DialogTriggerProps) {
  return (
    <AriaDialogTrigger>
      {trigger}
      {children}
    </AriaDialogTrigger>
  );
}
