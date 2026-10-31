import { cx } from "./cx";

// The field looks (design.md _Forms_), shared by src/ui/form and the controls outside it (`CopyField`, `Table`), so
// those don't pull the form library into the first load.

/** The column every field lays out in: label, help text, control, error. */
export const fieldColumn = cx("flex flex-col gap-1.5");

const inputSurface = cx(
  "w-full min-w-0 rounded-md border border-border-strong bg-bg px-2.5 text-base text-fg outline-none placeholder:text-fg-muted data-disabled:border-border data-disabled:bg-bg-muted data-disabled:text-fg-muted data-focused:outline-2 data-focused:outline-offset-0 data-focused:outline-solid data-focused:outline-focus data-invalid:border-danger-text",
);

/** Text inputs and the select and combobox buttons (design.md _Forms_). */
export const inputBox = cx(inputSurface, "h-control");

/** A text input: read only shows on a subtle surface. */
export const textInput = cx(inputBox, "read-only:bg-bg-subtle");

/** A multi line text input, as tall as its `rows`. */
export const textArea = cx(inputSurface, "py-1.5 read-only:bg-bg-subtle");

/** A checkbox's 16 px box, styled by its `CheckboxButton` (`group`) state. */
export const checkboxBox = cx(
  "flex size-4 shrink-0 items-center justify-center rounded-sm border border-border-strong bg-bg text-accent-fg group-data-disabled:border-border group-data-disabled:bg-bg-muted group-data-focus-visible:outline-2 group-data-focus-visible:outline-offset-2 group-data-focus-visible:outline-solid group-data-focus-visible:outline-focus group-data-indeterminate:border-accent group-data-indeterminate:bg-accent group-data-invalid:border-danger-text group-data-selected:border-accent group-data-selected:bg-accent",
);
