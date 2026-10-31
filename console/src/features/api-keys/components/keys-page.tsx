import { useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { useParams } from "@tanstack/react-router";
import { Badge, Button, EmptyState, PageHeader, RelativeTime, SecretReveal } from "@/ui";
import { useAppForm } from "@/ui/form";
import { SideSheet } from "@/ui/side-sheet";
import { Cell, Column, Row, Table, TableBody, TableHeader } from "@/ui/table";
import { useCreateApiKey, type CreatedApiKey } from "../api/mutations";
import { apiKeysQuery, type ApiKey } from "../api/queries";
import { createKeySchema, SECRET_SCOPES, toCreateKeyRequest } from "../schemas/create-key";

/** Spec 0017 AKY-4, AKY-5, AC-13: a project's keys, and the sheet that makes one and shows it once. */
export function KeysPage() {
  const { t } = useLingui();
  const { projectId } = useParams({ from: "/_shell/$orgId/$projectId/settings/keys" });
  const { data } = useSuspenseQuery(apiKeysQuery(projectId));
  const [isCreating, setCreating] = useState(false);
  const create = (
    <Button
      variant="primary"
      onPress={() => {
        setCreating(true);
      }}
    >
      {t`Create API key`}
    </Button>
  );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t`API keys`} actions={data.items.length > 0 ? create : undefined} />
      {data.items.length === 0 ? (
        <EmptyState
          kind="first-use"
          title={t`Create your first key`}
          description={t`A key lets your app reach this project. It's shown only once.`}
          action={create}
        />
      ) : (
        <KeysTable keys={data.items} />
      )}
      {isCreating ? (
        <CreateKeySheet
          projectId={projectId}
          onClose={() => {
            setCreating(false);
          }}
        />
      ) : null}
    </div>
  );
}

function maskedKey(key: Pick<ApiKey, "prefix" | "last4">): string {
  return `${key.prefix}…${key.last4}`;
}

function KeysTable({ keys }: { keys: readonly ApiKey[] }) {
  const { t } = useLingui();
  return (
    <Table aria-label={t`API keys`}>
      <TableHeader>
        <Column id="name" isRowHeader>
          {t`Name`}
        </Column>
        <Column id="key">{t`Key`}</Column>
        <Column id="type">{t`Type`}</Column>
        <Column id="status">{t`Status`}</Column>
        <Column id="created">{t`Created`}</Column>
      </TableHeader>
      <TableBody>
        {keys.map((key) => (
          <Row key={key.id} id={key.id}>
            <Cell>{key.name}</Cell>
            <Cell>
              <span className="font-mono">{maskedKey(key)}</span>
            </Cell>
            <Cell>{key.type === "secret" ? t`Secret` : t`Publishable`}</Cell>
            <Cell>
              <KeyStatus status={key.status} />
            </Cell>
            <Cell>
              <RelativeTime datetime={key.createdAt} focusable={false} />
            </Cell>
          </Row>
        ))}
      </TableBody>
    </Table>
  );
}

function KeyStatus({ status }: { status: ApiKey["status"] }) {
  const { t } = useLingui();
  switch (status) {
    case "active":
      return <Badge tone="success">{t`Active`}</Badge>;
    case "expired":
      return <Badge tone="warning">{t`Expired`}</Badge>;
    default:
      return <Badge>{t`Revoked`}</Badge>;
  }
}

function CreateKeySheet({ projectId, onClose }: { projectId: string; onClose: () => void }) {
  const { t } = useLingui();
  const createKey = useCreateApiKey(projectId);
  // PRM-7: the key lives in this component's state, and goes when the sheet closes.
  const [created, setCreated] = useState<CreatedApiKey>();
  const form = useAppForm({
    defaultValues: { name: "", type: "secret", scopes: [] as string[] },
    schema: createKeySchema,
    onSubmitAsync: async ({ value }) => {
      try {
        setCreated(await createKey.mutateAsync(toCreateKeyRequest(createKeySchema.parse(value))));
        return undefined;
      } catch {
        return { form: t`The key wasn't created. Try again in a moment.` };
      }
    },
  });

  return (
    <SideSheet
      title={t`Create API key`}
      isOpen
      onOpenChange={(isOpen) => {
        if (!isOpen) onClose();
      }}
      footer={
        created ? (
          <Button onPress={onClose}>{t`Done`}</Button>
        ) : (
          <form.AppForm>
            <form.SubmitButton>{t`Create key`}</form.SubmitButton>
          </form.AppForm>
        )
      }
    >
      {created ? (
        <SecretReveal label={t`API key`} secret={created.key} masked={maskedKey(created)} />
      ) : (
        <form.AppForm>
          <form.Form>
            <form.ErrorSummary />
            <form.AppField name="name">
              {(field) => <field.TextField label={t`Name`} maxLength={128} />}
            </form.AppField>
            <form.AppField name="type">
              {(field) => (
                <field.RadioGroup
                  label={t`Type`}
                  options={[
                    {
                      id: "secret",
                      label: t`Secret`,
                      description: t`For your servers. Never put it in a browser or an app.`,
                    },
                    {
                      id: "publishable",
                      label: t`Publishable`,
                      description: t`Safe in client code. It gets every client API.`,
                    },
                  ]}
                />
              )}
            </form.AppField>
            <form.Subscribe selector={(state) => state.values.type}>
              {(type) =>
                type === "secret" ? (
                  <form.AppField name="scopes">
                    {(field) => (
                      <field.CheckboxGroup
                        label={t`Scopes`}
                        options={SECRET_SCOPES.map((scope) => ({ id: scope, label: scope }))}
                      />
                    )}
                  </form.AppField>
                ) : null
              }
            </form.Subscribe>
          </form.Form>
        </form.AppForm>
      )}
    </SideSheet>
  );
}
