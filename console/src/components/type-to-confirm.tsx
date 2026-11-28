"use client";

import { useId, useState, type ReactNode } from "react";

import { Button } from "@/components/ui/button";
import { Field, FieldDescription, FieldLabel } from "@/components/ui/field";
import { Input } from "@/components/ui/input";

/**
 * A destructive confirmation: the danger button enables only when the resource name is typed exactly
 * (design system README, Forms).
 */
export function TypeToConfirm({
  resourceName,
  actionLabel,
  onConfirm,
  children,
}: {
  resourceName: string;
  actionLabel: string;
  onConfirm: () => void;
  children?: ReactNode;
}) {
  const id = useId();
  const [typed, setTyped] = useState("");
  const matches = typed === resourceName;

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (matches) {
          onConfirm();
          setTyped("");
        }
      }}
    >
      {children}
      <Field>
        <FieldLabel htmlFor={`${id}-name`}>
          Type <span className="font-mono text-mono">{resourceName}</span> to
          confirm
        </FieldLabel>
        <Input
          id={`${id}-name`}
          value={typed}
          onChange={(event) => setTyped(event.target.value)}
          autoComplete="off"
          spellCheck={false}
          className="font-mono"
          aria-describedby={`${id}-help`}
        />
        <FieldDescription id={`${id}-help`}>
          This can&apos;t be undone.
        </FieldDescription>
      </Field>
      <div>
        <Button type="submit" variant="destructive" disabled={!matches}>
          {actionLabel}
        </Button>
      </div>
    </form>
  );
}
