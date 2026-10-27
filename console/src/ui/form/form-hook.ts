import { createFormHook, type DeepKeys, type StandardSchemaV1 } from "@tanstack/react-form";
import { Combobox, Checkbox, CheckboxGroup, RadioGroup, Select } from "./choice-fields";
import { fieldContext, formContext } from "./contexts";
import { Field } from "./field";
import { ErrorSummary, Form, SubmitButton } from "./form-components";
import { firstMessage, formUi, reveal } from "./form-ui";
import { NumberField, PasswordField, TextArea, TextField } from "./text-fields";

// Spec 0003 FRM-1: the app's one form hook. Screens call `useAppForm` and render `form.AppField` with these field
// components, and `form.AppForm` with these form components; never `useForm` or a raw input.
const { useAppForm: useTanStackAppForm } = createFormHook({
  fieldContext,
  formContext,
  fieldComponents: {
    Field,
    TextField,
    PasswordField,
    TextArea,
    NumberField,
    Select,
    Combobox,
    Checkbox,
    CheckboxGroup,
    RadioGroup,
  },
  formComponents: { Form, SubmitButton, ErrorSummary },
});

/**
 * Spec 0003 FRM-4: what a submit returns when the server refuses it. Errors that name a field show at that field;
 * `form` goes to the `ErrorSummary`.
 */
export interface SubmitErrors<Values> {
  form?: string;
  fields?: Partial<Record<DeepKeys<Values>, string>>;
}

export interface AppFormOptions<Values> {
  /** The last saved values (a snapshot of the query data), or the create form's starting values (FRM-5). */
  defaultValues: Values;
  /** The form's one Zod schema (FRM-3), checked with the UX-8 timing (FRM-2). */
  schema?: StandardSchemaV1<Values, unknown>;
  /**
   * Saves: `mutateAsync(toRequest(schema.parse(value)))` (FRM-4). Return the server's validation errors, or nothing
   * when it saved. Pass `signal` on to the request.
   */
  onSubmitAsync?: (props: {
    value: Values;
    signal: AbortSignal;
  }) => Promise<SubmitErrors<Values> | undefined>;
}

/**
 * Spec 0003 FRM-1 to FRM-5: a TanStack Form with the app's field and form components. Validator timing is set here
 * once (FRM-2), so screens pass a schema, not timing. A failed submit keeps every value (FRM-6), shows each field's
 * error, and moves focus to the `ErrorSummary`. A successful one resets the form to the saved values (FRM-5).
 */
export function useAppForm<Values>({
  defaultValues,
  schema,
  onSubmitAsync,
}: AppFormOptions<Values>) {
  return useTanStackAppForm({
    defaultValues,
    // UX-8: pressing submit on an invalid form shows the errors, so it must reach onSubmitInvalid.
    canSubmitWhenInvalid: true,
    validators: {
      onChange: schema,
      onSubmitAsync: onSubmitAsync
        ? ({ value, signal }) => onSubmitAsync({ value, signal })
        : undefined,
    },
    onSubmit: ({ value, formApi }) => {
      formApi.reset(value, { keepDefaultValues: false });
    },
    onSubmitInvalid: ({ formApi }) => {
      const store = formUi(formApi);
      const invalid = [...store.get().fields.keys()].filter(
        (name) => firstMessage(formApi.getFieldMeta(name)?.errors ?? []) !== undefined,
      );
      reveal(store, invalid);
      store.set((state) => ({ ...state, failedSubmits: state.failedSubmits + 1 }));
    },
  });
}
