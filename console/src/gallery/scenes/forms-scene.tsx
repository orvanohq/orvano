import { useState, type ReactNode } from "react";
import { z } from "@/shared/zod";
import { useAppForm, Switch } from "@/ui/form";

// Spec 0015 GAL-2: every form primitive in its states, on placeholder data. Gallery strings stay out of the catalog.

const regions = [
  { id: "eu-west", label: "Europe (Frankfurt)" },
  { id: "us-east", label: "US East (Virginia)" },
  { id: "ap-south", label: "Asia Pacific (Mumbai)", description: "Higher latency from Europe" },
];

const bucketSchema = z.object({
  name: z
    .string()
    .check(
      z.trim(),
      z.minLength(1, "Enter a bucket name."),
      z.regex(/^[a-z0-9-]*$/, "Use lowercase letters, numbers, and hyphens."),
    ),
  password: z.string().check(z.minLength(8, "Use at least 8 characters.")),
  description: z.string(),
  maxSize: z.optional(
    z.number({ error: "Enter a size in MB." }).check(z.minimum(1, "Use at least 1 MB.")),
  ),
  region: z
    .optional(z.string())
    .check(z.refine((region) => region !== undefined, "Choose a region.")),
  owner: z.optional(z.string()),
  encrypted: z.boolean(),
  access: z.string(),
  events: z.array(z.string()),
});

type Bucket = z.input<typeof bucketSchema>;

const empty: Bucket = {
  name: "",
  password: "",
  description: "",
  maxSize: undefined,
  region: undefined,
  owner: undefined,
  encrypted: false,
  access: "private",
  events: [],
};

/** A form with every field type. Submit it empty to see the error summary; "taken" fails on the server. */
export function FormsScene() {
  const [saved, setSaved] = useState<Bucket | null>(null);
  const [notifications, setNotifications] = useState(true);
  const form = useAppForm({
    defaultValues: empty,
    schema: bucketSchema,
    onSubmitAsync: async ({ value }) => {
      await new Promise((resolve) => setTimeout(resolve, 600));
      if (value.name.trim() === "taken") {
        return {
          form: "The server refused the bucket.",
          fields: { name: "A bucket with this name already exists." },
        };
      }
      setSaved(value);
      return undefined;
    },
  });

  return (
    <main className="mx-auto flex max-w-content flex-col gap-8 p-6">
      <h1 className="text-xl font-semibold">Forms</h1>

      <Section title="A form with every field">
        <form.AppForm>
          <form.Form>
            <form.ErrorSummary />
            <form.AppField name="name">
              {(field) => (
                <field.TextField
                  label="Bucket name"
                  description="Lowercase, unique in the project."
                />
              )}
            </form.AppField>
            <form.AppField name="password">
              {(field) => (
                <field.PasswordField label="Access password" autoComplete="new-password" />
              )}
            </form.AppField>
            <form.AppField name="description">
              {(field) => <field.TextArea label="Description" isOptional />}
            </form.AppField>
            <form.AppField name="maxSize">
              {(field) => <field.NumberField label="Maximum file size (MB)" isOptional />}
            </form.AppField>
            <form.AppField name="region">
              {(field) => (
                <field.Select label="Region" options={regions} placeholder="Choose a region" />
              )}
            </form.AppField>
            <form.AppField name="owner">
              {(field) => <field.Combobox label="Owner" options={owners} isOptional />}
            </form.AppField>
            <form.AppField name="access">
              {(field) => (
                <field.RadioGroup
                  label="Access"
                  options={[
                    {
                      id: "private",
                      label: "Private",
                      description: "Only signed in users with a grant",
                    },
                    { id: "public", label: "Public read" },
                  ]}
                />
              )}
            </form.AppField>
            <form.AppField name="events">
              {(field) => (
                <field.CheckboxGroup
                  label="Send events for"
                  isOptional
                  options={[
                    { id: "created", label: "File created" },
                    { id: "deleted", label: "File deleted" },
                  ]}
                />
              )}
            </form.AppField>
            <form.AppField name="encrypted">
              {(field) => (
                <field.Checkbox
                  label="Encrypt files at rest"
                  description="Uses the instance key."
                />
              )}
            </form.AppField>
            <div className="flex justify-end gap-2">
              <form.SubmitButton>Create bucket</form.SubmitButton>
            </div>
          </form.Form>
        </form.AppForm>
        {saved ? <p>Saved {saved.name}.</p> : null}
      </Section>

      <Section title="Switch">
        <Switch
          label="Email me about failed jobs"
          isSelected={notifications}
          onChange={setNotifications}
        />
        <Switch
          label="Unavailable setting"
          isSelected={false}
          onChange={() => undefined}
          isDisabled
        />
      </Section>
    </main>
  );
}

const owners = [
  { id: "ada", label: "Ada Lovelace" },
  { id: "grace", label: "Grace Hopper" },
  { id: "katherine", label: "Katherine Johnson" },
];

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold">{title}</h2>
      {children}
    </section>
  );
}
