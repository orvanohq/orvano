import { useState, type ReactNode } from "react";
import { ConfirmDelete, type ConfirmDeleteProps, type LoadDependents } from "@/ui/confirm-delete";
import { useAppForm } from "@/ui/form";
import { SideSheet } from "@/ui/side-sheet";
import { Button, Dialog, DialogTrigger, Link } from "@/ui";
import { z } from "@/shared/zod";

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
  const [sheetOpen, setSheetOpen] = useState(false);
  const [confirm, setConfirm] = useState<Scenario | null>(null);

  return (
    <main className="mx-auto flex max-w-content flex-col gap-8 p-6">
      <h1 className="text-xl font-semibold">Overlays</h1>

      <Section title="Dialog">
        <Row label="sm">
          <DialogTrigger trigger={<Button>Rename table</Button>}>
            <Dialog
              title="Rename table"
              footer={
                <>
                  <Button slot="close">Cancel</Button>
                  <Button variant="primary" slot="close">
                    Rename table
                  </Button>
                </>
              }
            >
              <p>A one or two field form, or a confirmation, fits a dialog (UX-6).</p>
            </Dialog>
          </DialogTrigger>
        </Row>
        <Row label="md">
          <DialogTrigger trigger={<Button>Show details</Button>}>
            <Dialog title="Deployment details" size="md">
              <p>A wider dialog, 560 px. Escape closes it.</p>
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
            New webhook
          </Button>
        </Row>
        <p className="text-fg-muted">
          Type in the sheet, then press Escape or{" "}
          <Link to="/" variant="inline">
            leave this page
          </Link>{" "}
          to see the unsaved changes question.
        </p>
        <WebhookSheet isOpen={sheetOpen} onOpenChange={setSheetOpen} />
      </Section>

      <Section title="ConfirmDelete">
        {scenarios.map((scenario) => (
          <Row key={scenario.label} label={scenario.kind}>
            <Button
              variant="danger"
              onPress={() => {
                setConfirm(scenario);
              }}
            >
              {scenario.label}
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
    </main>
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
        title="New webhook"
        isOpen={isOpen}
        onOpenChange={onOpenChange}
        footer={
          <>
            <Button slot="close">Cancel</Button>
            <form.SubmitButton>Create webhook</form.SubmitButton>
          </>
        }
      >
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="url">
            {(field) => <field.TextField label="Endpoint URL" type="url" />}
          </form.AppField>
          <form.AppField name="secret">
            {(field) => <field.PasswordField label="Signing secret" isOptional />}
          </form.AppField>
        </form.Form>
      </SideSheet>
    </form.AppForm>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold">{title}</h2>
      {children}
    </section>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <span className="w-24 shrink-0 font-mono text-sm text-fg-muted">{label}</span>
      {children}
    </div>
  );
}
