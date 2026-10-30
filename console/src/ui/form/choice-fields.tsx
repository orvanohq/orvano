import { useRef } from "react";
import { useLingui } from "@lingui/react/macro";
import {
  Button as AriaButton,
  CheckboxButton,
  CheckboxField,
  type CheckboxFieldProps,
  CheckboxGroup as AriaCheckboxGroup,
  ComboBox as AriaComboBox,
  Input,
  ListBox,
  ListBoxItem,
  RadioButton,
  RadioField,
  RadioGroup as AriaRadioGroup,
  Select as AriaSelect,
  SelectValue,
  Text,
} from "react-aria-components";
import { cx, focusRing } from "../cx";
import { Icon } from "../icon";
import { CheckIcon, DisclosureIcon, IndeterminateIcon } from "../icons";
import { Popover } from "../popover";
import {
  FieldDescription,
  FieldErrorText,
  FieldLabel,
  fieldColumn,
  inputBox,
  textInput,
  useAriaAttribute,
  useFieldWiring,
  type FieldProps,
} from "./field";
import { checkboxBox as box } from "../field-styles";

export interface Option {
  id: string;
  label: string;
  description?: string;
}

interface OptionsProps extends FieldProps {
  options: readonly Option[];
}

const option = cx(
  "flex cursor-default flex-col rounded-sm px-2 py-1.5 text-fg outline-none data-focused:bg-bg-muted data-selected:bg-accent-subtle data-selected:text-accent-subtle-fg data-disabled:text-fg-muted",
);

function Options({ options }: { options: readonly Option[] }) {
  return options.map((item) => (
    <ListBoxItem key={item.id} id={item.id} textValue={item.label} className={option}>
      <span>{item.label}</span>
      {item.description ? (
        <Text slot="description" className="text-sm text-fg-muted">
          {item.description}
        </Text>
      ) : null}
    </ListBoxItem>
  ));
}

const listBox = cx("max-h-72 min-w-(--trigger-width) overflow-y-auto p-1 outline-none");

export interface SelectProps extends OptionsProps {
  placeholder?: string;
}

/** design.md _Forms_: picks one of a short list. Arrows move, typing jumps to a match. Holds the option's id. */
export function Select({ label, description, isOptional, options, ...props }: SelectProps) {
  const field = useFieldWiring<string | undefined>();
  const triggerRef = useRef<HTMLButtonElement>(null);
  // The trigger is what holds focus, so it carries the invalid state that React Aria gives text inputs.
  useAriaAttribute(triggerRef, "aria-invalid", field.error === undefined ? undefined : "true");
  return (
    <AriaSelect
      {...props}
      id={field.inputId}
      name={field.name}
      value={field.value ?? null}
      onChange={(key) => {
        field.onChange(key === null ? undefined : String(key));
      }}
      onBlur={field.onBlur}
      isInvalid={field.error !== undefined}
      className={fieldColumn}
    >
      <FieldLabel label={label} isOptional={isOptional} />
      <FieldDescription>{description}</FieldDescription>
      <AriaButton
        ref={triggerRef}
        className={cx(
          inputBox,
          "flex items-center justify-between gap-2 text-start data-focus-visible:outline-offset-0",
          focusRing,
        )}
      >
        <SelectValue className="truncate data-placeholder:text-fg-muted" />
        <Icon icon={DisclosureIcon} />
      </AriaButton>
      <FieldErrorText>{field.error}</FieldErrorText>
      <Popover>
        <ListBox className={listBox}>
          <Options options={options} />
        </ListBox>
      </Popover>
    </AriaSelect>
  );
}

export interface ComboboxProps extends OptionsProps {
  /** The text typed, for a list the caller filters on the server; leave out to filter `options` locally. */
  inputValue?: string;
  onInputChange?: (value: string) => void;
  /** The caller is loading options for the typed text. */
  isLoading?: boolean;
  /** Shown while the box is empty, so an empty combobox reads like an empty `Select` ("Choose an owner"). */
  placeholder?: string;
}

/**
 * design.md _Forms_: picks one of a long list by typing. Shows "Loading" while the caller loads matches and "No
 * matches" when there are none. Holds the option's id.
 */
export function Combobox({
  label,
  description,
  isOptional,
  options,
  isLoading,
  placeholder,
  ...props
}: ComboboxProps) {
  const { t } = useLingui();
  const field = useFieldWiring<string | undefined>();
  return (
    <AriaComboBox
      {...props}
      id={field.inputId}
      name={field.name}
      value={field.value ?? null}
      onChange={(key) => {
        field.onChange(key === null ? undefined : String(key));
      }}
      onBlur={field.onBlur}
      isInvalid={field.error !== undefined}
      allowsEmptyCollection
      menuTrigger="focus"
      className={fieldColumn}
    >
      <FieldLabel label={label} isOptional={isOptional} />
      <FieldDescription>{description}</FieldDescription>
      <div className="relative flex">
        <Input placeholder={placeholder} className={cx(textInput, "pe-8")} />
        <AriaButton className="absolute inset-y-0 end-0 flex w-8 items-center justify-center text-fg-muted">
          <Icon icon={DisclosureIcon} />
        </AriaButton>
      </div>
      <FieldErrorText>{field.error}</FieldErrorText>
      <Popover>
        <ListBox
          className={listBox}
          aria-busy={isLoading || undefined}
          renderEmptyState={() => (
            <p className="px-2 py-1.5 text-fg-muted">{isLoading ? t`Loading` : t`No matches`}</p>
          )}
        >
          <Options options={isLoading ? [] : options} />
        </ListBox>
      </Popover>
    </AriaComboBox>
  );
}

/** One box in a 24 px target (design.md _Forms_), not bound to a form field (a dialog's "Don't ask again"). */
export function CheckboxControl({
  label,
  description,
  error,
  ...props
}: Omit<CheckboxFieldProps, "children" | "className" | "style"> & {
  label: string;
  description?: string;
  error?: string;
}) {
  return (
    <CheckboxField {...props} className="flex flex-col">
      <CheckboxButton className="group flex min-h-6 cursor-default items-start gap-2 py-1">
        {({ isSelected, isIndeterminate }) => (
          <>
            <span className="mt-0.5 flex size-4 items-center">
              <span className={box}>
                {isIndeterminate ? (
                  <Icon icon={IndeterminateIcon} />
                ) : isSelected ? (
                  <Icon icon={CheckIcon} />
                ) : null}
              </span>
            </span>
            {label}
          </>
        )}
      </CheckboxButton>
      {description ? (
        <Text slot="description" className="ps-6 text-sm text-fg-muted">
          {description}
        </Text>
      ) : null}
      <FieldErrorText>{error}</FieldErrorText>
    </CheckboxField>
  );
}

export interface CheckboxProps extends Omit<FieldProps, "isOptional" | "isReadOnly"> {
  isIndeterminate?: boolean;
}

/** design.md _Forms_: a yes or no in a form (settings that apply at once use `Switch`). Space toggles it. */
export function Checkbox({ label, description, isDisabled, isIndeterminate }: CheckboxProps) {
  const field = useFieldWiring<boolean>();
  return (
    <CheckboxControl
      id={field.inputId}
      name={field.name}
      label={label}
      description={description}
      error={field.error}
      isSelected={field.value}
      onChange={field.onChange}
      onBlur={field.onBlur}
      isDisabled={isDisabled}
      isIndeterminate={isIndeterminate}
      isInvalid={field.error !== undefined}
    />
  );
}

/** design.md _Forms_: picks any of a few options. Holds the chosen ids. */
export function CheckboxGroup({ label, description, isOptional, options, ...props }: OptionsProps) {
  const field = useFieldWiring<string[]>();
  return (
    <AriaCheckboxGroup
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
      <div className="flex flex-col">
        {options.map((item) => (
          <CheckboxControl
            key={item.id}
            value={item.id}
            label={item.label}
            description={item.description}
          />
        ))}
      </div>
      <FieldErrorText>{field.error}</FieldErrorText>
    </AriaCheckboxGroup>
  );
}

/** design.md _Forms_: picks one of a few options, all visible. Arrows move and select. */
export function RadioGroup({ label, description, isOptional, options, ...props }: OptionsProps) {
  const field = useFieldWiring<string | undefined>();
  return (
    <AriaRadioGroup
      {...props}
      id={field.inputId}
      name={field.name}
      value={field.value ?? null}
      onChange={field.onChange}
      onBlur={field.onBlur}
      isInvalid={field.error !== undefined}
      className={fieldColumn}
    >
      <FieldLabel label={label} isOptional={isOptional} />
      <FieldDescription>{description}</FieldDescription>
      <div className="flex flex-col">
        {options.map((item) => (
          <RadioField key={item.id} value={item.id} className="flex flex-col">
            <RadioButton className="group flex min-h-6 cursor-default items-start gap-2 py-1">
              <span className="mt-0.5 flex size-4 items-center">
                <span className="size-4 shrink-0 rounded-full border border-border-strong bg-bg group-data-disabled:border-border group-data-disabled:bg-bg-muted group-data-focus-visible:outline-2 group-data-focus-visible:outline-offset-2 group-data-focus-visible:outline-solid group-data-focus-visible:outline-focus group-data-invalid:border-danger-text group-data-selected:border-5 group-data-selected:border-accent" />
              </span>
              {item.label}
            </RadioButton>
            {item.description ? (
              <Text slot="description" className="ps-6 text-sm text-fg-muted">
                {item.description}
              </Text>
            ) : null}
          </RadioField>
        ))}
      </div>
      <FieldErrorText>{field.error}</FieldErrorText>
    </AriaRadioGroup>
  );
}
