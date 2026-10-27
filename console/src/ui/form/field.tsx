import { useEffect, useId, type ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";
import { useSelector } from "@tanstack/react-form";
import { Label, Text } from "react-aria-components";
import { useStore } from "@/shared/stores/create-store";
import { fieldColumn } from "../field-styles";
import { Icon } from "../icon";
import { ErrorIcon } from "../icons";
import { useFieldContext } from "./contexts";
import { firstMessage, formUi, hide, registerField, reveal } from "./form-ui";

/** The props every field component shares (UX-7). */
export interface FieldProps {
  /** The visible label, above the field. A placeholder never replaces it. */
  label: string;
  /** Help text, under the label. */
  description?: string;
  /** Marks the field "(optional)"; required is the default, so it gets no mark. */
  isOptional?: boolean;
  isDisabled?: boolean;
  isReadOnly?: boolean;
}

let isPointerDown = false;
if (typeof document !== "undefined") {
  document.addEventListener("pointerdown", () => (isPointerDown = true), true);
  for (const type of ["pointerup", "pointercancel"]) {
    document.addEventListener(type, () => (isPointerDown = false), true);
  }
}

/**
 * Runs `reveal` now, or, when a pointer press caused the blur, once the press ends. Showing the error moves what is
 * below it, so revealing mid press would move a submit button out from under the pointer and lose the click.
 */
function afterPress(run: () => void) {
  if (!isPointerDown) {
    run();
    return;
  }
  document.addEventListener(
    "pointerup",
    () => {
      setTimeout(run);
    },
    { capture: true, once: true },
  );
}

/**
 * Spec 0003 FRM-2, spec 0001 UX-8: the one place the validation timing lives. Every field component wires its
 * control through this. A field's error shows once it loses focus after a change (or after a failed submit); from
 * then on each change checks it again, and the error clears the moment the value passes.
 */
export function useFieldWiring<Value>() {
  const field = useFieldContext<Value>();
  const store = formUi(field.form);
  const inputId = useId();
  const name = field.name;
  const value = useSelector(field.store, (state) => state.value);
  const errors = useSelector(field.store, (state) => state.meta.errors as unknown[]);
  const isRevealed = useStore(store, (state) => state.revealed.has(name));

  useEffect(() => registerField(store, name, { inputId }), [store, name, inputId]);

  return {
    name,
    inputId,
    value,
    error: isRevealed ? firstMessage(errors) : undefined,
    onChange: (next: Value) => {
      field.handleChange(next);
      // The form's schema runs on every change; once the field passes, hide it until its next blur.
      if (!firstMessage(field.state.meta.errors)) hide(store, name);
    },
    onBlur: () => {
      field.handleBlur();
      if (field.state.meta.isDirty) {
        afterPress(() => {
          reveal(store, [name]);
        });
      }
    },
  };
}

/** "Name" or "Name (optional)", above the control. */
export function FieldLabel({ label, isOptional }: { label: string; isOptional?: boolean }) {
  return (
    <Label className="font-medium text-fg">
      {label}
      {isOptional ? <OptionalMark /> : null}
    </Label>
  );
}

export function FieldDescription({ children }: { children?: string }) {
  if (!children) return null;
  return (
    <Text slot="description" className="text-sm text-fg-muted">
      {children}
    </Text>
  );
}

/** The error under the control: an icon and text, never color alone (UX-8). */
export function FieldErrorText({ children }: { children?: string }) {
  if (!children) return null;
  return (
    <Text slot="errorMessage" className="flex items-start gap-1.5 text-sm text-danger-text">
      <span className="mt-0.5">
        <Icon icon={ErrorIcon} />
      </span>
      {children}
    </Text>
  );
}

export { fieldColumn, inputBox, textInput } from "../field-styles";

export interface CustomFieldProps extends FieldProps {
  /** The control, given its id, its described by ids, and whether it is invalid. */
  children: (control: {
    id: string;
    "aria-describedby": string | undefined;
    "aria-invalid": boolean | undefined;
    value: unknown;
    onChange: (value: unknown) => void;
    onBlur: () => void;
  }) => ReactNode;
}

/**
 * The frame for a control that isn't one of the field components (a feature's `RelationPicker`, say): it gets the
 * same label, help text, error, and timing.
 */
export function Field({ label, description, isOptional, children }: CustomFieldProps) {
  const wiring = useFieldWiring<unknown>();
  const descriptionId = useId();
  const errorId = useId();
  const describedBy =
    [description ? descriptionId : null, wiring.error ? errorId : null].filter(Boolean).join(" ") ||
    undefined;
  return (
    <div className={fieldColumn}>
      <label htmlFor={wiring.inputId} className="font-medium text-fg">
        {label}
        {isOptional ? <OptionalMark /> : null}
      </label>
      {description ? (
        <p id={descriptionId} className="text-sm text-fg-muted">
          {description}
        </p>
      ) : null}
      {children({
        id: wiring.inputId,
        "aria-describedby": describedBy,
        "aria-invalid": wiring.error ? true : undefined,
        value: wiring.value,
        onChange: wiring.onChange,
        onBlur: wiring.onBlur,
      })}
      {wiring.error ? (
        <p id={errorId} className="flex items-start gap-1.5 text-sm text-danger-text">
          <span className="mt-0.5">
            <Icon icon={ErrorIcon} />
          </span>
          {wiring.error}
        </p>
      ) : null}
    </div>
  );
}

function OptionalMark() {
  const { t } = useLingui();
  return <span className="font-normal text-fg-muted"> {t`(optional)`}</span>;
}
