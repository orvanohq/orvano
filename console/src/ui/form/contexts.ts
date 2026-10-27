import { createFormHookContexts } from "@tanstack/react-form";

// Spec 0003 FRM-1: the contexts the app's one form hook is built on. Field and form components read them.
export const { fieldContext, formContext, useFieldContext, useFormContext } =
  createFormHookContexts();
