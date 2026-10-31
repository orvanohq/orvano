import { useEffect, useRef, useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { Banner } from "./banner";
import { Button } from "./button";
import { CopyField } from "./copy-field";
import { cx } from "./cx";
import { fieldColumn } from "./field-styles";

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
