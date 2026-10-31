import { useEffect, useRef, useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { Input, Label, Text, TextField } from "react-aria-components";
import { announce } from "./announcer";
import { IconButton } from "./button";
import { cx } from "./cx";
import { fieldColumn, textInput } from "./field-styles";
import { CheckIcon, CopyIcon } from "./icons";
import { isApplePlatform } from "./platform";

type CopyState = "idle" | "copied" | "manual";

/**
 * Spec 0015 PRM-9, spec 0001 UX-33: copies through `navigator.clipboard.writeText` and announces "Copied". Where the
 * clipboard is missing (plain HTTP is not a secure context) or refuses, `select` puts the value under a selection and
 * the state becomes `manual`, so the caller shows the keyboard hint, and the hint is announced; nothing then claims
 * it was copied.
 */
export function useCopy() {
  const { t } = useLingui();
  const [state, setState] = useState<CopyState>("idle");

  // The check mark shows for a moment, then the copy icon comes back.
  useEffect(() => {
    if (state !== "copied") return;
    const timer = setTimeout(() => {
      setState("idle");
    }, 2000);
    return () => {
      clearTimeout(timer);
    };
  }, [state]);

  const hint = isApplePlatform() ? t`Press ⌘C to copy` : t`Press Ctrl+C to copy`;

  async function copy(value: string, select: () => void) {
    try {
      // Without a secure context `navigator.clipboard` is undefined, so this throws and takes the fallback.
      await navigator.clipboard.writeText(value);
      setState("copied");
      announce(t`Copied`);
    } catch {
      select();
      setState("manual");
      announce(hint);
    }
  }

  return { state, copy, hint };
}

export interface CopyFieldProps {
  /** The visible label ("Endpoint", "Request ID"); the copy button is "Copy" plus this label. */
  label: string;
  value: string;
}

/** design.md _Data display_: a read only value with a copy button (IDs, endpoints, keys). */
export function CopyField({ label, value }: CopyFieldProps) {
  const { t } = useLingui();
  const input = useRef<HTMLInputElement>(null);
  const { state, copy, hint } = useCopy();
  return (
    <TextField isReadOnly value={value} className={fieldColumn}>
      <Label className="font-medium text-fg">{label}</Label>
      <div className="flex min-w-0 gap-2">
        <Input ref={input} className={cx(textInput, "font-mono")} />
        <IconButton
          variant="secondary"
          icon={state === "copied" ? CheckIcon : CopyIcon}
          aria-label={t`Copy ${label}`}
          onPress={() => {
            void copy(value, () => {
              input.current?.focus();
              input.current?.select();
            });
          }}
        />
      </div>
      {/* Always rendered, so React Aria wires it as the field's description; empty until the copy falls back. */}
      <Text slot="description" className="text-sm text-fg-muted empty:hidden">
        {state === "manual" ? hint : null}
      </Text>
    </TextField>
  );
}
