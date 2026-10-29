import { useEffect, useId, useRef, useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { Input, Label, Text, TextField } from "react-aria-components";
import { announce } from "./announcer";
import { Banner } from "./banner";
import { Button, IconButton } from "./button";
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
function useCopy() {
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

export interface SecretRevealProps {
  /** What the secret is ("API key", "Signing secret"). */
  label: string;
  /** The full secret, from the create response. Kept only in this component's state (PRM-7). */
  secret: string;
  /** What shows afterwards: the prefix and last 4 characters (UX-33). */
  masked: string;
  /** Called after "I've copied it", so the caller can drop the secret too. */
  onDone?: () => void;
}

/**
 * Spec 0015 PRM-7, spec 0001 UX-33: shows a new secret once, with a copy button and a warning. After "I've copied
 * it" only the masked value shows, with no copy button (never a store, storage, or the URL). A new `secret` (a key
 * rotated while this stays mounted) starts it over: the new one shows, never the old one or its mask.
 */
export function SecretReveal({ label, secret, masked, onDone }: SecretRevealProps) {
  const { t } = useLingui();
  const [shownFor, setShownFor] = useState(secret);
  const [done, setDone] = useState(false);
  const maskedRef = useRef<HTMLDivElement>(null);
  const [moveFocus, setMoveFocus] = useState(false);

  // Reset during render, not in an effect, so the stale state never paints for a frame.
  if (shownFor !== secret) {
    setShownFor(secret);
    setDone(false);
    setMoveFocus(false);
  }

  // The button that had focus is gone, so focus goes to what replaced it.
  useEffect(() => {
    if (moveFocus) maskedRef.current?.focus();
  }, [moveFocus]);

  if (done) {
    return (
      <div ref={maskedRef} tabIndex={-1} className={cx(fieldColumn, "outline-none")}>
        <span className="font-medium text-fg">{label}</span>
        <span className="font-mono text-fg">{masked}</span>
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-3">
      <Banner tone="warning" title={t`Copy this secret now`}>
        {t`You won't be able to see it again. Store it somewhere safe.`}
      </Banner>
      <CopyField label={label} value={secret} />
      <div>
        <Button
          onPress={() => {
            setDone(true);
            setMoveFocus(true);
            onDone?.();
          }}
        >
          {t`I've copied it`}
        </Button>
      </div>
    </div>
  );
}

export interface CodeSnippetProps {
  /** What the code is ("Install command"); the copy button is "Copy" plus this label. */
  label: string;
  code: string;
}

/** design.md _Data display_: plain monospace code on `bg-muted` with a copy button. No highlighting yet. */
export function CodeSnippet({ label, code }: CodeSnippetProps) {
  const { t } = useLingui();
  const region = useRef<HTMLElement>(null);
  const pre = useRef<HTMLPreElement>(null);
  const hintId = useId();
  const { state, copy, hint } = useCopy();
  return (
    <div className="flex flex-col gap-1.5">
      <div className="relative min-w-0 rounded-md bg-bg-muted">
        {/* Focusable, so the code can be scrolled sideways by keyboard; a named region, so focus lands on its label. */}
        <section
          ref={region}
          aria-label={label}
          aria-describedby={state === "manual" ? hintId : undefined}
          // oxlint-disable-next-line jsx-a11y/no-noninteractive-tabindex -- a scrollable region must be reachable by keyboard (WCAG 2.1.1)
          tabIndex={0}
          className={cx(
            "overflow-x-auto rounded-md p-3 pe-12 outline-none",
            "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-solid focus-visible:outline-focus",
          )}
        >
          <pre ref={pre} className="font-mono text-sm text-fg">
            <code>{code}</code>
          </pre>
        </section>
        <div className="absolute end-2 top-2">
          <IconButton
            size="sm"
            icon={state === "copied" ? CheckIcon : CopyIcon}
            aria-label={t`Copy ${label}`}
            onPress={() => {
              void copy(code, () => {
                if (!region.current || !pre.current) return;
                region.current.focus();
                // The code itself, not its region: a selected block would add a line break in some engines.
                window.getSelection()?.selectAllChildren(pre.current);
              });
            }}
          />
        </div>
      </div>
      {state === "manual" ? (
        <p id={hintId} className="text-sm text-fg-muted">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
