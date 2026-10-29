import { msg } from "@lingui/core/macro";
import { z } from "@/shared/zod";
import { Badge, Button, CopyField, DangerZone, DangerZoneItem, SettingsCard } from "@/ui";
import { useAppForm } from "@/ui/form";
import { Cell, Column, Row, Table, TableBody, TableHeader } from "@/ui/table";
import { useGalleryText } from "../pseudo";

// The settings page pieces the data scene shows on their own and the org and account scenes show in place, so the
// spacing between them is seen inside the shell (design.md _Page anatomy_, GAL-2).

const nothingChanged = msg`Change a setting to update.`;

/**
 * A `SettingsCard` with one editable name and a read only ID. Update stays unavailable until the name changes: the
 * caller enforces FRM-5 from the form's dirty state (design.md _Data display_).
 */
export function NameSettingsCard({
  title,
  description,
  nameLabel,
  name,
  idLabel,
  id,
}: {
  title: string;
  description: string;
  nameLabel: string;
  name: string;
  idLabel: string;
  id: string;
}) {
  const g = useGalleryText();
  const form = useAppForm({
    defaultValues: { name },
    schema: z.object({ name: z.string().check(z.trim(), z.minLength(1, "Enter a name.")) }),
    onSubmitAsync: async () => {
      await new Promise((resolve) => setTimeout(resolve, 400));
      return undefined;
    },
  });
  return (
    <form.AppForm>
      <SettingsCard
        title={g(title)}
        description={g(description)}
        footer={
          <form.Subscribe selector={(state) => [state.isDirty, state.isSubmitting] as const}>
            {([isDirty, isSubmitting]) => (
              <Button
                variant="primary"
                isPending={isSubmitting}
                unavailableReason={isDirty ? undefined : nothingChanged}
                onPress={() => void form.handleSubmit()}
              >
                {g("Update")}
              </Button>
            )}
          </form.Subscribe>
        }
      >
        <form.Form>
          <form.AppField name="name">
            {(field) => <field.TextField label={g(nameLabel)} />}
          </form.AppField>
          <CopyField label={g(idLabel)} value={id} />
        </form.Form>
      </SettingsCard>
    </form.AppForm>
  );
}

export function MembersTable() {
  const g = useGalleryText();
  return (
    <Table aria-label={g("Members")} selectionMode="multiple">
      <TableHeader>
        <Column id="email" isRowHeader>
          {g("Email")}
        </Column>
        <Column id="role">{g("Role")}</Column>
      </TableHeader>
      <TableBody>
        <Row id="ada">
          <Cell>ada@example.com</Cell>
          <Cell>
            <Badge tone="info">{g("Owner")}</Badge>
          </Cell>
        </Row>
        <Row id="lin">
          <Cell>lin@example.com</Cell>
          <Cell>
            <Badge>{g("Developer")}</Badge>
          </Cell>
        </Row>
      </TableBody>
    </Table>
  );
}

/** A danger zone with a secondary and a destructive action, for any resource. */
export function ResourceDangerZone({
  transfer,
  remove,
}: {
  transfer?: { title: string; description: string };
  remove: { title: string; description: string };
}) {
  const g = useGalleryText();
  return (
    <DangerZone>
      {transfer ? (
        <DangerZoneItem
          title={g(transfer.title)}
          description={g(transfer.description)}
          action={<Button>{g(transfer.title)}</Button>}
        />
      ) : null}
      <DangerZoneItem
        title={g(remove.title)}
        description={g(remove.description)}
        action={<Button variant="danger">{g(remove.title)}</Button>}
      />
    </DangerZone>
  );
}
