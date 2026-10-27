import { useEffect, useId, useRef, type ReactNode } from "react";
import { plural } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { useSelector } from "@tanstack/react-form";
import { useStore } from "@/shared/stores/create-store";
import { Button } from "../button";
import { Icon } from "../icon";
import { ErrorIcon } from "../icons";
import { useFormContext } from "./contexts";
import { firstMessage, formUi } from "./form-ui";
import { useUnsavedChanges } from "./unsaved-changes";

export interface FormProps {
  children: ReactNode;
}

/**
 * Spec 0003 FRM-1: the `<form>` element. It submits through the form's validators (never the browser's own
 * validation), and registers with the unsaved changes guard while its values differ from the last saved ones
 * (FRM-5, RTE-8).
 */
export function Form({ children }: FormProps) {
  const form = useFormContext();
  const store = formUi(form);
  const id = useId();
  const changed = useSelector(form.store, (state) => !state.isDefaultValue);
  useUnsavedChanges(changed);

  useEffect(() => {
    store.set((state) => ({ ...state, formId: id }));
  }, [store, id]);

  return (
    <form
      id={id}
      noValidate
      className="flex max-w-content flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        event.stopPropagation();
        // FRM-4: a second press while saving does nothing.
        if (!form.state.isSubmitting) void form.handleSubmit();
      }}
    >
      {children}
    </form>
  );
}

export interface SubmitButtonProps {
  /** Says what it does: "Create bucket", "Save changes" (never "Submit"). */
  children: ReactNode;
}

/**
 * Spec 0003 FRM-4, spec 0001 UX-8: never disabled for invalid input (pressing it shows the errors). While saving it
 * shows the busy state, which is announced, and ignores a second press. It submits its form from anywhere inside the
 * form's `AppForm`, a sheet's footer included.
 */
export function SubmitButton({ children }: SubmitButtonProps) {
  const form = useFormContext();
  const isSubmitting = useSelector(form.store, (state) => state.isSubmitting);
  const formId = useStore(formUi(form), (state) => state.formId);
  return (
    <Button variant="primary" type="submit" form={formId} isPending={isSubmitting}>
      {children}
    </Button>
  );
}

/** Moves focus to a field's control: the element with its id, or the first control inside it (a group). */
function focusControl(id: string) {
  document
    .querySelector<HTMLElement>(
      `[id="${id}"]:is(input, textarea, button), [id="${id}"] :is(input:not([type=hidden]), textarea, button)`,
    )
    ?.focus();
}

/**
 * Spec 0001 UX-8, spec 0003 FRM-4: after a failed submit it appears at the top of the form and takes focus. It says
 * how many problems there are, links each one to its field with the same words the field shows, and lists the
 * errors that name no field (from the server).
 */
export function ErrorSummary() {
  const { t } = useLingui();
  const form = useFormContext();
  const store = formUi(form);
  const revealed = useStore(store, (state) => state.revealed);
  const fields = useStore(store, (state) => state.fields);
  const failedSubmits = useStore(store, (state) => state.failedSubmits);
  const fieldMeta = useSelector(
    form.store,
    (state) => state.fieldMeta as Partial<Record<string, { errors: unknown[] }>>,
  );
  const formErrors = useSelector(form.store, (state) => state.errorMap.onSubmit as unknown);
  const ref = useRef<HTMLDivElement>(null);
  const headingId = useId();

  useEffect(() => {
    if (failedSubmits > 0) ref.current?.focus();
  }, [failedSubmits]);

  const fieldItems = [...fields]
    .filter(([name]) => revealed.has(name))
    .map(([name, entry]) => ({
      inputId: entry.inputId,
      message: firstMessage(fieldMeta[name]?.errors ?? []),
    }))
    .filter((item): item is { inputId: string; message: string } => item.message !== undefined);
  // A server error that names no field (FRM-4). Schema errors arrive keyed by field, never as a string.
  const formMessage = typeof formErrors === "string" ? formErrors : undefined;
  const count = fieldItems.length + (formMessage ? 1 : 0);

  if (failedSubmits === 0 || count === 0) return null;

  return (
    <div
      ref={ref}
      // oxlint-disable-next-line jsx-a11y/prefer-tag-over-role -- design.md _Forms_: a labeled group of links, not form controls, so a fieldset doesn't fit
      role="group"
      aria-labelledby={headingId}
      tabIndex={-1}
      className="flex flex-col gap-2 rounded-md border border-danger-text bg-danger-subtle p-3 text-fg outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-solid focus-visible:outline-focus"
    >
      <p id={headingId} className="flex items-center gap-2 font-semibold text-danger-text">
        <Icon icon={ErrorIcon} />
        {t`${plural(count, {
          one: "Fix this problem to continue",
          other: "Fix these # problems to continue",
        })}`}
      </p>
      <ul className="flex list-disc flex-col gap-1 ps-7">
        {formMessage ? <li>{formMessage}</li> : null}
        {fieldItems.map((item) => (
          <li key={item.inputId}>
            <a
              href={`#${item.inputId}`}
              className="text-danger-text underline"
              onClick={(event) => {
                event.preventDefault();
                focusControl(item.inputId);
              }}
            >
              {item.message}
            </a>
          </li>
        ))}
      </ul>
    </div>
  );
}
