import { Suspense, useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { consoleConfigQuery } from "@/features/console-config";
import { ConnectPanel } from "@/features/projects";
import { Button, ContentSkeleton, SecretReveal } from "@/ui";
import { useAppForm } from "@/ui/form";
import { SideSheet } from "@/ui/side-sheet";
import { isReauthRequired, useCreateApiKey, type CreatedApiKey } from "../api/mutations";
import {
  createKeySchema,
  defaultScopes,
  toCreateKeyRequest,
  type CreateKeyValues,
} from "../schemas/create-key";
import { maskedKey } from "./masked-key";
import { ScopeChecklist } from "./scope-checklist";

const startingValues: CreateKeyValues = {
  name: "",
  type: "secret",
  scopes: defaultScopes("secret"),
  expiry: "never",
};

/** Spec 0017 AKY-4, AC-13: the form in a sheet. Success replaces it with the key, shown once, and an SDK snippet. */
export function CreateKeySheet({ projectId, onClose }: { projectId: string; onClose: () => void }) {
  const { t } = useLingui();
  const { data: config } = useSuspenseQuery(consoleConfigQuery());
  const createKey = useCreateApiKey(projectId);
  // PRM-7: the key lives in this component's state, and goes when the sheet closes.
  const [created, setCreated] = useState<CreatedApiKey>();
  const form = useAppForm({
    defaultValues: startingValues,
    schema: createKeySchema,
    onSubmitAsync: async ({ value }) => {
      try {
        setCreated(await createKey.mutateAsync(toCreateKeyRequest(createKeySchema.parse(value))));
        return undefined;
      } catch (error) {
        return {
          form: isReauthRequired(error)
            ? t`The key wasn't created, because your password wasn't confirmed.`
            : t`The key wasn't created. Try again in a moment.`,
        };
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
        <div className="flex flex-col gap-4">
          <SecretReveal label={t`API key`} secret={created.key} masked={maskedKey(created)} />
          <Suspense fallback={<ContentSkeleton />}>
            <ConnectPanel
              url={config.publicUrl ?? window.location.origin}
              projectId={projectId}
              keyType={created.type}
            />
          </Suspense>
        </div>
      ) : (
        <form.AppForm>
          <form.Form>
            <form.ErrorSummary />
            <form.AppField name="name">
              {(field) => <field.TextField label={t`Name`} maxLength={128} />}
            </form.AppField>
            <form.AppField
              name="type"
              listeners={{
                onChange: ({ value }) => {
                  form.setFieldValue("scopes", defaultScopes(value));
                },
              }}
            >
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
                      description: t`Safe in client code.`,
                    },
                  ]}
                />
              )}
            </form.AppField>
            <form.AppField name="scopes">
              {(field) => (
                <field.Field label={t`Scopes`}>
                  {(control) => (
                    <fieldset
                      id={control.id}
                      aria-label={t`Scopes`}
                      aria-describedby={control["aria-describedby"]}
                    >
                      <form.Subscribe selector={(state) => state.values.type}>
                        {(type) => (
                          <ScopeChecklist
                            type={type}
                            value={control.value as string[]}
                            onChange={control.onChange}
                            onBlur={control.onBlur}
                          />
                        )}
                      </form.Subscribe>
                    </fieldset>
                  )}
                </field.Field>
              )}
            </form.AppField>
            <form.AppField name="expiry">
              {(field) => (
                <field.Select
                  label={t`Expires`}
                  options={[
                    { id: "never", label: t`Never` },
                    { id: "30", label: t`In 30 days` },
                    { id: "90", label: t`In 90 days` },
                    { id: "365", label: t`In 365 days` },
                  ]}
                />
              )}
            </form.AppField>
          </form.Form>
        </form.AppForm>
      )}
    </SideSheet>
  );
}
