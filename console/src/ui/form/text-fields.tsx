import { useState } from "react";
import { useLingui } from "@lingui/react/macro";
import {
  Group,
  Input,
  NumberField as AriaNumberField,
  TextArea as AriaTextArea,
  TextField as AriaTextField,
} from "react-aria-components";
import { IconButton } from "../button";
import { cx } from "../cx";
import { HideIcon, ShowIcon } from "../icons";
import {
  FieldDescription,
  FieldErrorText,
  FieldLabel,
  fieldColumn,
  textInput,
  useFieldWiring,
  type FieldProps,
} from "./field";

export interface TextFieldProps extends FieldProps {
  type?: "text" | "email" | "url" | "search" | "tel";
  autoComplete?: string;
  placeholder?: string;
  maxLength?: number;
}

/** design.md _Forms_: a single line text field, wired to its form field (FRM-1, FRM-2). */
export function TextField({
  label,
  description,
  isOptional,
  type = "text",
  ...props
}: TextFieldProps) {
  const field = useFieldWiring<string>();
  return (
    <AriaTextField
      {...props}
      id={field.inputId}
      name={field.name}
      type={type}
      value={field.value}
      onChange={field.onChange}
      onBlur={field.onBlur}
      isInvalid={field.error !== undefined}
      className={fieldColumn}
    >
      <FieldLabel label={label} isOptional={isOptional} />
      <FieldDescription>{description}</FieldDescription>
      <Input className={textInput} />
      <FieldErrorText>{field.error}</FieldErrorText>
    </AriaTextField>
  );
}

export interface PasswordFieldProps extends FieldProps {
  autoComplete?: "current-password" | "new-password";
}

/** A text field whose value is hidden until its show button is pressed. */
export function PasswordField({ label, description, isOptional, ...props }: PasswordFieldProps) {
  const { t } = useLingui();
  const field = useFieldWiring<string>();
  const [isVisible, setVisible] = useState(false);
  return (
    <AriaTextField
      {...props}
      id={field.inputId}
      name={field.name}
      type={isVisible ? "text" : "password"}
      value={field.value}
      onChange={field.onChange}
      onBlur={field.onBlur}
      isInvalid={field.error !== undefined}
      className={fieldColumn}
    >
      <FieldLabel label={label} isOptional={isOptional} />
      <FieldDescription>{description}</FieldDescription>
      {/* The show button sits inside the field's end, so the field keeps the full width of its column. */}
      <div className="relative flex">
        <Input className={cx(textInput, "pe-8")} />
        <span className="absolute inset-y-0 end-0.5 flex items-center">
          <IconButton
            icon={isVisible ? HideIcon : ShowIcon}
            size="sm"
            aria-label={isVisible ? t`Hide password` : t`Show password`}
            onPress={() => {
              setVisible(!isVisible);
            }}
          />
        </span>
      </div>
      <FieldErrorText>{field.error}</FieldErrorText>
    </AriaTextField>
  );
}

export interface TextAreaProps extends FieldProps {
  placeholder?: string;
  maxLength?: number;
  rows?: number;
}

/** A multi line text field. */
export function TextArea({ label, description, isOptional, rows = 4, ...props }: TextAreaProps) {
  const field = useFieldWiring<string>();
  return (
    <AriaTextField
      {...props}
      id={field.inputId}
      name={field.name}
      value={field.value}
      onChange={field.onChange}
      onBlur={field.onBlur}
      isInvalid={field.error !== undefined}
      className={fieldColumn}
    >
      <FieldLabel label={label} isOptional={isOptional} />
      <FieldDescription>{description}</FieldDescription>
      <AriaTextArea rows={rows} className={cx(textInput, "h-auto py-1.5")} />
      <FieldErrorText>{field.error}</FieldErrorText>
    </AriaTextField>
  );
}

export interface NumberFieldProps extends FieldProps {
  minValue?: number;
  maxValue?: number;
  step?: number;
  formatOptions?: Intl.NumberFormatOptions;
}

/** A number in the active locale's format; the arrow keys step it. An empty field holds `undefined`. */
export function NumberField({ label, description, isOptional, ...props }: NumberFieldProps) {
  const field = useFieldWiring<number | undefined>();
  return (
    <AriaNumberField
      {...props}
      id={field.inputId}
      name={field.name}
      value={field.value ?? Number.NaN}
      onChange={(value) => {
        field.onChange(Number.isNaN(value) ? undefined : value);
      }}
      onBlur={field.onBlur}
      isInvalid={field.error !== undefined}
      className={fieldColumn}
    >
      <FieldLabel label={label} isOptional={isOptional} />
      <FieldDescription>{description}</FieldDescription>
      <Group className="flex">
        <Input className={textInput} />
      </Group>
      <FieldErrorText>{field.error}</FieldErrorText>
    </AriaNumberField>
  );
}
