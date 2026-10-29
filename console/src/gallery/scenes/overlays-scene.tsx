import { useState } from "react";
import { ConfirmDelete, type ConfirmDeleteProps, type LoadDependents } from "@/ui/confirm-delete";
import { useAppForm } from "@/ui/form";
import { SideSheet } from "@/ui/side-sheet";
import { Button, Dialog, DialogTrigger, Link } from "@/ui";
import { z } from "@/shared/zod";
import { useGalleryText } from "../pseudo";
import { Row, Scene, Section } from "./scene";

// Spec 0015 GAL-2: the overlays in every state, on placeholder data. The gallery imports the lazy primitives
// directly; screens load them lazily (PRM-10).

const wait = (ms: number, signal: AbortSignal) =>
  new Promise<void>((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener("abort", () => {
      clearTimeout(timer);
      reject(new Error("aborted"));
    });
  });

const counted: LoadDependents = async (signal) => {
  await wait(400, signal);
  return {
    items: [
      { kind: "table", count: 12, estimated: false },
      { kind: "row", count: 48_210, estimated: true },
    ],
  };
};
const loadingForever: LoadDependents = (signal) =>
  new Promise((_, reject) => {
    signal.addEventListener("abort", () => {
      reject(new Error("aborted"));
    });
  });
const failing: LoadDependents = async (signal) => {
  await wait(300, signal);
  throw new Error("dependents failed");
};
const deleted = async () => {
  await new Promise((resolve) => setTimeout(resolve, 500));
};
const refused = async () => {
  await new Promise((resolve) => setTimeout(resolve, 300));
  throw new Error("refused");
};

type Scenario = ConfirmDeleteProps extends infer Props
  ? Props extends ConfirmDeleteProps
    ? { label: string } & Omit<Props, "isOpen" | "onOpenChange">
    : never
  : never;

const scenarios: Scenario[] = [
  {
    label: "Tier 1, counted",
    kind: "database",
    name: "main",
    loadDependents: counted,
    onConfirm: deleted,
  },
  {
    label: "Tier 1, loading",
    kind: "project",
    name: "storefront",
    loadDependents: loadingForever,
    onConfirm: deleted,
  },
  {
    label: "Tier 2, couldn't count",
    kind: "table",
    name: "users",
    loadDependents: failing,
    onConfirm: deleted,
  },
  { label: "Tier 2, delete fails", kind: "api_key", name: "CI deploy key", onConfirm: refused },
  {
    label: "Single row",
    kind: "row",
    dontAskAgain: false,
    onDontAskAgainChange: () => undefined,
    onConfirm: deleted,
  },
  { label: "Bulk, 37 rows", kind: "rows", count: 37, onConfirm: deleted },
  { label: "Bulk, 1,200 rows", kind: "rows", count: 1200, onConfirm: deleted },
];

/** Dialogs, the side sheet with its unsaved changes question, and every ConfirmDelete state. */
export function OverlaysScene() {
  const g = useGalleryText();
  const [sheetOpen, setSheetOpen] = useState(false);
  const [confirm, setConfirm] = useState<Scenario | null>(null);

  return (
    <Scene title={g("Overlays")}>
      <Section title="Dialog">
        <Row label="sm">
          <DialogTrigger trigger={<Button>{g("Rename table")}</Button>}>
            <Dialog
              title={g("Rename table")}
              footer={
                <>
                  <Button slot="close">{g("Cancel")}</Button>
                  <Button variant="primary" slot="close">
                    {g("Rename table")}
                  </Button>
                </>
              }
            >
              <p>{g("A one or two field form, or a confirmation, fits a dialog (UX-6).")}</p>
            </Dialog>
          </DialogTrigger>
        </Row>
        <Row label="md">
          <DialogTrigger trigger={<Button>{g("Show details")}</Button>}>
            <Dialog title={g("Deployment details")} size="md">
              <p>{g("A wider dialog, 560 px. Escape closes it.")}</p>
            </Dialog>
          </DialogTrigger>
        </Row>
      </Section>

      <Section title="SideSheet">
        <Row label="modal">
          <Button
            variant="primary"
            onPress={() => {
              setSheetOpen(true);
            }}
          >
            {g("New webhook")}
          </Button>
        </Row>
        <p className="text-fg-muted">
          {g("Type in the sheet, then press Escape or")}{" "}
          <Link to="/" variant="inline">
            {g("leave this page")}
          </Link>{" "}
          {g("to see the unsaved changes question.")}
        </p>
        <WebhookSheet isOpen={sheetOpen} onOpenChange={setSheetOpen} />
      </Section>

      <Section title="ConfirmDelete">
        {scenarios.map((scenario) => (
          <Row key={g(scenario.label)} label={scenario.kind}>
            <Button
              variant="danger"
              onPress={() => {
                setConfirm(scenario);
              }}
            >
              {g(scenario.label)}
            </Button>
          </Row>
        ))}
        {confirm ? (
          <ConfirmDelete
            {...confirm}
            isOpen
            onOpenChange={(isOpen) => {
              if (!isOpen) setConfirm(null);
            }}
          />
        ) : null}
      </Section>
    </Scene>
  );
}

const webhookSchema = z.object({
  url: z.url("Enter a full URL, starting with https://."),
  secret: z.string(),
});

function WebhookSheet({
  isOpen,
  onOpenChange,
}: {
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
}) {
  const g = useGalleryText();
  const form = useAppForm({
    defaultValues: { url: "", secret: "" },
    schema: webhookSchema,
    onSubmitAsync: async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
      onOpenChange(false);
      return undefined;
    },
  });
  return (
    <form.AppForm>
      <SideSheet
        title={g("New webhook")}
        isOpen={isOpen}
        onOpenChange={onOpenChange}
        footer={
          <>
            <Button slot="close">{g("Cancel")}</Button>
            <form.SubmitButton>{g("Create webhook")}</form.SubmitButton>
          </>
        }
      >
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="url">
            {(field) => <field.TextField label={g("Endpoint URL")} type="url" />}
          </form.AppField>
          <form.AppField name="secret">
            {(field) => <field.PasswordField label={g("Signing secret")} isOptional />}
          </form.AppField>
        </form.Form>
      </SideSheet>
    </form.AppForm>
  );
}
