import type { AnyFormApi } from "@tanstack/react-form";
import { createStore, type Store } from "@/shared/stores/create-store";

// Spec 0003 FRM-2, spec 0001 UX-8: what TanStack Form doesn't track for us, per form. A field's error is shown only
// once it is "revealed": when the field loses focus after a change, or after a failed submit. Once the error clears
// it is hidden again, so the next mistake shows on the next blur, never while typing.

export interface FieldEntry {
  /** The id of the field's control, which an `ErrorSummary` link focuses. */
  inputId: string;
}

export interface FormUiState {
  revealed: ReadonlySet<string>;
  /** Fields in the order they mounted (the order on screen), for the summary. */
  fields: ReadonlyMap<string, FieldEntry>;
  /** Counts failed submits; the summary takes focus each time it grows. */
  failedSubmits: number;
  /** The `<form>` element's id, so a submit button outside it (a sheet's footer) still submits it. */
  formId: string | undefined;
}

// Keyed by the form's TanStack store: a field's `field.form` and the `useFormContext()` object are different wrappers
// around the same form, and the store is the one object they share.
const forms = new WeakMap<object, Store<FormUiState>>();

export function formUi(form: AnyFormApi): Store<FormUiState> {
  let store = forms.get(form.store);
  if (!store) {
    store = createStore<FormUiState>({
      revealed: new Set(),
      fields: new Map(),
      failedSubmits: 0,
      formId: undefined,
    });
    forms.set(form.store, store);
  }
  return store;
}

export function reveal(store: Store<FormUiState>, names: Iterable<string>): void {
  store.set((state) => ({ ...state, revealed: new Set([...state.revealed, ...names]) }));
}

export function hide(store: Store<FormUiState>, name: string): void {
  store.set((state) => {
    if (!state.revealed.has(name)) return state;
    const revealed = new Set(state.revealed);
    revealed.delete(name);
    return { ...state, revealed };
  });
}

export function registerField(
  store: Store<FormUiState>,
  name: string,
  entry: FieldEntry,
): () => void {
  store.set((state) => ({ ...state, fields: new Map(state.fields).set(name, entry) }));
  return () => {
    store.set((state) => {
      if (state.fields.get(name) !== entry) return state;
      const fields = new Map(state.fields);
      fields.delete(name);
      return { ...state, fields };
    });
  };
}

/**
 * The first message among TanStack Form's errors: a string (a server error, FRM-4) or a Standard Schema issue (the
 * Zod schema, FRM-3).
 */
export function firstMessage(errors: readonly unknown[]): string | undefined {
  for (const error of errors) {
    if (typeof error === "string" && error) return error;
    if (Array.isArray(error)) {
      const nested = firstMessage(error);
      if (nested) return nested;
    }
    if (error && typeof error === "object" && "message" in error) {
      const { message } = error;
      if (typeof message === "string" && message) return message;
    }
  }
  return undefined;
}
