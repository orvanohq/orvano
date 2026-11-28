"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useId, useState } from "react";
import { Controller, useForm } from "react-hook-form";
import { z } from "zod";

import { CodeBlock } from "@/components/code-block";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const bucketSchema = z.object({
  name: z
    .string()
    .regex(
      /^[a-z0-9-]{3,63}$/,
      "Use 3 to 63 lowercase letters, numbers or dashes.",
    ),
  region: z.enum(["local", "s3"]),
  public: z.boolean(),
});

type Bucket = z.infer<typeof bucketSchema>;

/**
 * The form pattern: labels above 36px controls, help text in small ink-muted, a Zod validated field whose error
 * shows inline, is tied to the field with aria-describedby, and is announced through a polite live region.
 */
export function BucketForm() {
  const id = useId();
  const [created, setCreated] = useState<Bucket | null>(null);
  const form = useForm<Bucket>({
    resolver: zodResolver(bucketSchema),
    defaultValues: { name: "", region: "local", public: false },
  });
  const nameError = form.formState.errors.name;

  return (
    <div className="flex flex-col gap-4">
      <form
        noValidate
        onSubmit={form.handleSubmit(setCreated)}
        className="flex max-w-md flex-col gap-4"
      >
        <FieldGroup>
          <Field data-invalid={nameError ? true : undefined}>
            <FieldLabel htmlFor={`${id}-name`}>Bucket name</FieldLabel>
            <Input
              id={`${id}-name`}
              autoComplete="off"
              spellCheck={false}
              aria-required="true"
              aria-invalid={nameError ? true : undefined}
              aria-describedby={`${id}-name-help ${id}-name-error`}
              {...form.register("name")}
            />
            <FieldDescription id={`${id}-name-help`}>
              Lowercase letters, numbers and dashes, 3 to 63 characters.
            </FieldDescription>
            <FieldError id={`${id}-name-error`}>
              {nameError?.message}
            </FieldError>
          </Field>

          <Field>
            <FieldLabel htmlFor={`${id}-region`}>Region</FieldLabel>
            <Controller
              control={form.control}
              name="region"
              render={({ field }) => (
                <Select value={field.value} onValueChange={field.onChange}>
                  <SelectTrigger
                    id={`${id}-region`}
                    className="w-full"
                    onBlur={field.onBlur}
                  >
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent position="popper">
                    <SelectGroup>
                      <SelectItem value="local">local</SelectItem>
                      <SelectItem value="s3">s3</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              )}
            />
          </Field>

          <Controller
            control={form.control}
            name="public"
            render={({ field }) => (
              <Field orientation="horizontal">
                <Checkbox
                  id={`${id}-public`}
                  checked={field.value}
                  onCheckedChange={(checked) =>
                    field.onChange(checked === true)
                  }
                  onBlur={field.onBlur}
                  aria-describedby={`${id}-public-help`}
                />
                <FieldContent>
                  <FieldLabel htmlFor={`${id}-public`}>
                    Public bucket
                  </FieldLabel>
                  <FieldDescription id={`${id}-public-help`}>
                    Anyone with the link can read files.
                  </FieldDescription>
                </FieldContent>
              </Field>
            )}
          />
        </FieldGroup>
        <div>
          <Button type="submit">Create bucket</Button>
        </div>
      </form>
      {created && (
        <div className="flex max-w-md flex-col gap-2">
          <p role="status" className="text-body text-ink">
            Your bucket is ready to create with these values.
          </p>
          <CodeBlock language="json" code={JSON.stringify(created, null, 2)} />
        </div>
      )}
    </div>
  );
}
