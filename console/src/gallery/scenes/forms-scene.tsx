import { useState, type ComponentType } from "react";
import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";
import { z } from "@/shared/zod";
import { useAppForm, Switch, type SwitchProps } from "@/ui/form";
import { useGalleryText } from "../pseudo";
import { useScenarioData } from "../scenario";
import { Row, Scene, Section } from "./scene";

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
  password: z
    .string()
    .check(z.minLength(1, "Enter a password."), z.minLength(8, "Use at least 8 characters.")),
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

/**
 * A form with every field type. Submit it empty to see the error summary; "taken" fails on the server. The States
 * scene shows each field's states without a submit.
 */
export function FormsScene() {
  const g = useGalleryText();
  const ownerOptions = useScenarioData(owners, []);
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
    <Scene title="Forms">
      <Section title="A form with every field">
        <form.AppForm>
          <form.Form>
            <form.ErrorSummary />
            <form.AppField name="name">
              {(field) => (
                <field.TextField
                  label={g("Bucket name")}
                  description={g("Lowercase, unique in the project.")}
                />
              )}
            </form.AppField>
            <form.AppField name="password">
              {(field) => (
                <field.PasswordField label={g("Access password")} autoComplete="new-password" />
              )}
            </form.AppField>
            <form.AppField name="description">
              {(field) => <field.TextArea label={g("Description")} isOptional />}
            </form.AppField>
            <form.AppField name="maxSize">
              {(field) => (
                <field.NumberField
                  label={g("Maximum file size")}
                  description={g("In MB.")}
                  isOptional
                />
              )}
            </form.AppField>
            <form.AppField name="region">
              {(field) => (
                <field.Select
                  label={g("Region")}
                  options={regions}
                  placeholder={g("Choose a region")}
                />
              )}
            </form.AppField>
            <form.AppField name="owner">
              {(field) => (
                <field.Combobox
                  label={g("Owner")}
                  description={g("Set Scenario to Slow or Empty for its loading and empty states.")}
                  options={ownerOptions ?? []}
                  isLoading={ownerOptions === undefined}
                  placeholder={g("Choose an owner")}
                  isOptional
                />
              )}
            </form.AppField>
            <form.AppField name="access">
              {(field) => (
                <field.RadioGroup
                  label={g("Access")}
                  options={[
                    {
                      id: "private",
                      label: g("Private"),
                      description: g("Only signed in users with a grant"),
                    },
                    { id: "public", label: g("Public read") },
                  ]}
                />
              )}
            </form.AppField>
            <form.AppField name="events">
              {(field) => (
                <field.CheckboxGroup
                  label={g("Send events for")}
                  isOptional
                  options={[
                    { id: "created", label: g("File created") },
                    { id: "deleted", label: g("File deleted") },
                  ]}
                />
              )}
            </form.AppField>
            <form.AppField name="encrypted">
              {(field) => (
                <field.Checkbox
                  label={g("Encrypt files at rest")}
                  description={g("Uses the instance key.")}
                />
              )}
            </form.AppField>
            <div className="flex justify-end gap-2">
              <form.SubmitButton>{g("Create bucket")}</form.SubmitButton>
            </div>
          </form.Form>
        </form.AppForm>
        {saved ? <p>{g(`Saved ${saved.name}.`)}</p> : null}
      </Section>

      <Section title="Switch">
        <Row label="on, off">
          <Switch
            label={g("Email me about failed jobs")}
            isSelected={notifications}
            onChange={setNotifications}
          />
        </Row>
        <Row label="unavailable">
          {/* PRM-4, design.md _Forms_: a setting you can't change stays focusable and says why. */}
          <ReasonSwitch
            label={g("Require two factor sign in")}
            isSelected={false}
            onChange={() => undefined}
            unavailableReason={onlyOwners}
          />
        </Row>
        <Row label="disabled">
          {/* A switch that can't apply at all on this install, so it has nothing to explain. */}
          <Switch
            label={g("Copy backups to a second region")}
            isSelected={false}
            onChange={() => undefined}
            isDisabled
          />
        </Row>
      </Section>
    </Scene>
  );
}

const onlyOwners = msg`Only an organization owner can change this.`;

// Spec 0015 PRM-4 (Q8): `Switch` takes `unavailableReason`, built in src/ui/form/switch.tsx by the triage's batch 4.
// Until that lands the prop is ignored and the switch shows as plain off; once it has, drop this cast.
const ReasonSwitch = Switch as ComponentType<
  SwitchProps & { unavailableReason?: MessageDescriptor }
>;

const owners = [
  { id: "ada", label: "Ada Lovelace" },
  { id: "grace", label: "Grace Hopper" },
  { id: "katherine", label: "Katherine Johnson" },
];
