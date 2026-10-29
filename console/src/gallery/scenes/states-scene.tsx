import { useEffect } from "react";
import { z } from "@/shared/zod";
import { useAppForm } from "@/ui/form";
import { formUi, reveal } from "@/ui/form/form-ui";
import { useGalleryText } from "../pseudo";
import { Scene, Section } from "./scene";

// Spec 0015 GAL-2: the field family in each state, shown at rest so it can be reviewed without typing or submitting
// (design.md _Forms_).

type State = "default" | "help" | "invalid" | "read-only" | "disabled";

const states: readonly { state: State; title: string }[] = [
  { state: "default", title: "Default" },
  { state: "help", title: "With help text" },
  { state: "invalid", title: "Invalid" },
  { state: "read-only", title: "Read only" },
  { state: "disabled", title: "Disabled" },
];

const regions = [
  { id: "eu-west", label: "Europe (Frankfurt)" },
  { id: "us-east", label: "US East (Virginia)" },
];
const owners = [
  { id: "ada", label: "Ada Lovelace" },
  { id: "grace", label: "Grace Hopper" },
];

const schema = z.object({
  name: z.string().check(z.regex(/^[a-z0-9-]*$/, "Use lowercase letters, numbers, and hyphens.")),
  password: z.string().check(z.minLength(8, "Use at least 8 characters.")),
  description: z.string().check(z.maxLength(20, "Use at most 20 characters.")),
  maxSize: z.optional(z.number().check(z.minimum(1, "Use at least 1 MB."))),
  region: z
    .optional(z.string())
    .check(z.refine((value) => value !== undefined, "Choose a region.")),
  owner: z.optional(z.string()).check(z.refine((value) => value !== undefined, "Choose an owner.")),
});

type Values = z.input<typeof schema>;

const filled: Values = {
  name: "media",
  password: "correct horse battery",
  description: "Product photos",
  maxSize: 25,
  region: "eu-west",
  owner: "ada",
};

const failing: Values = {
  name: "Media Files",
  password: "short",
  description: "Photos of every product in the shop",
  maxSize: 0,
  region: undefined,
  owner: undefined,
};

/** Every field, once per state. */
export function StatesScene() {
  return (
    <Scene title="Field states">
      {states.map(({ state, title }) => (
        <Section key={state} title={title}>
          <Fields state={state} />
        </Section>
      ))}
    </Scene>
  );
}

function Fields({ state }: { state: State }) {
  const g = useGalleryText();
  const form = useAppForm({
    defaultValues: state === "invalid" ? failing : filled,
    schema,
  });

  // The invalid set checks its values and shows every error at once, as a failed submit would, without moving focus.
  useEffect(() => {
    if (state !== "invalid") return;
    const show = async () => {
      await form.validate("change");
      reveal(formUi(form), Object.keys(failing));
    };
    void show();
  }, [form, state]);

  const shared = {
    isDisabled: state === "disabled",
    isReadOnly: state === "read-only",
  };
  const help = (text: string) => (state === "help" ? g(text) : undefined);

  return (
    <form.AppForm>
      <form.Form>
        <form.AppField name="name">
          {(field) => (
            <field.TextField
              label={g("Bucket name")}
              description={help("Lowercase, unique in the project.")}
              {...shared}
            />
          )}
        </form.AppField>
        <form.AppField name="password">
          {(field) => (
            <field.PasswordField
              label={g("Access password")}
              description={help("At least 8 characters.")}
              autoComplete="new-password"
              {...shared}
            />
          )}
        </form.AppField>
        <form.AppField name="description">
          {(field) => (
            <field.TextArea
              label={g("Description")}
              description={help("Shown to your team only.")}
              isOptional
              {...shared}
            />
          )}
        </form.AppField>
        <form.AppField name="maxSize">
          {(field) => (
            <field.NumberField
              label={g("Maximum file size")}
              description={help("In MB.")}
              isOptional
              {...shared}
            />
          )}
        </form.AppField>
        <form.AppField name="region">
          {(field) => (
            <field.Select
              label={g("Region")}
              description={help("Where the files are stored.")}
              options={regions}
              placeholder={g("Choose a region")}
              {...shared}
            />
          )}
        </form.AppField>
        <form.AppField name="owner">
          {(field) => (
            <field.Combobox
              label={g("Owner")}
              description={help("Type a name to search.")}
              options={owners}
              {...shared}
            />
          )}
        </form.AppField>
      </form.Form>
    </form.AppForm>
  );
}
