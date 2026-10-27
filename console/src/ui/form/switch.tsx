import { SwitchButton, SwitchField } from "react-aria-components";

export interface SwitchProps {
  /** The visible label, after the track. */
  label: string;
  isSelected: boolean;
  onChange: (isSelected: boolean) => void;
  isDisabled?: boolean;
}

/**
 * design.md _Forms_: a setting that applies the moment it flips (not part of a form; forms use `Checkbox`). Space
 * toggles it. The track sits in a 24 px target.
 */
export function Switch({ label, ...props }: SwitchProps) {
  return (
    <SwitchField {...props}>
      <SwitchButton className="group flex min-h-6 cursor-default items-center gap-2">
        <span className="flex h-5 w-9 shrink-0 items-center rounded-full border border-border-strong bg-bg-muted p-0.5 transition-colors duration-(--duration-fast) ease-standard group-data-disabled:border-border group-data-focus-visible:outline-2 group-data-focus-visible:outline-offset-2 group-data-focus-visible:outline-solid group-data-focus-visible:outline-focus group-data-selected:border-accent group-data-selected:bg-accent">
          <span className="size-3.5 rounded-full bg-fg-muted shadow-sm transition-transform duration-(--duration-fast) ease-standard group-data-selected:translate-x-4 group-data-selected:bg-accent-fg rtl:group-data-selected:-translate-x-4" />
        </span>
        {label}
      </SwitchButton>
    </SwitchField>
  );
}
