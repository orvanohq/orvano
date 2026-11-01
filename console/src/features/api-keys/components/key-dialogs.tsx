import { useState, type ReactNode } from "react";
import { Trans, useLingui } from "@lingui/react/macro";
import { Button, Dialog, InlineError, SecretReveal } from "@/ui";
import { useAppForm } from "@/ui/form";
import {
  useRegenerateApiKey,
  useRenameApiKey,
  useRevokeApiKey,
  type CreatedApiKey,
} from "../api/mutations";
import type { ApiKey } from "../api/queries";
import { renameKeySchema } from "../schemas/rename-key";
import { keyFailure } from "./key-failure";
import { maskedKey } from "./masked-key";

interface KeyDialogProps {
  projectId: string;
  apiKey: ApiKey;
  onClose: () => void;
}

/** Spec 0001 UX-23's Tier 2: names the key, starts on Cancel, and a specific danger button. */
function ConfirmKey({
  title,
  description,
  action,
  isPending,
  failure,
  onConfirm,
  onClose,
}: {
  title: string;
  description: ReactNode;
  action: string;
  isPending: boolean;
  failure: unknown;
  onConfirm: () => void;
  onClose: () => void;
}) {
  const { t } = useLingui();
  return (
    <Dialog
      role="alertdialog"
      title={title}
      description={description}
      isOpen
      isDismissDisabled={isPending}
      onOpenChange={(isOpen) => {
        if (!isOpen) onClose();
      }}
      footer={
        <>
          <Button slot="close" initialFocus isDisabled={isPending}>
            {t`Cancel`}
          </Button>
          <Button variant="danger" isPending={isPending} onPress={onConfirm}>
            {action}
          </Button>
        </>
      }
    >
      {failure ? <InlineError error={failure} title={t`That didn't work`} /> : null}
    </Dialog>
  );
}

/** Spec 0017 AKY-6. */
export function RevokeKeyDialog({ projectId, apiKey, onClose }: KeyDialogProps) {
  const { t } = useLingui();
  const revoke = useRevokeApiKey(projectId);
  const { name } = apiKey;
  return (
    <ConfirmKey
      title={t`Revoke ${name}?`}
      description={t`The key stops working on its next request. You can't undo this.`}
      action={t`Revoke key`}
      isPending={revoke.isPending}
      failure={revoke.error}
      onConfirm={() => {
        revoke.mutate(apiKey.id, { onSuccess: onClose });
      }}
      onClose={onClose}
    />
  );
}

/** Spec 0017 AKY-7: says the old key stops at once, then shows the new one, once. */
export function RegenerateKeyDialog({ projectId, apiKey, onClose }: KeyDialogProps) {
  const { t } = useLingui();
  const regenerate = useRegenerateApiKey(projectId);
  // PRM-7: as in the create sheet, the key lives only in this component's state.
  const [created, setCreated] = useState<CreatedApiKey>();
  const { name } = apiKey;
  if (created) {
    return (
      <Dialog
        title={t`New key for ${name}`}
        size="md"
        isOpen
        // The old key has stopped, so a stray press mustn't lose the only sight of this one.
        isDismissDisabled
        onOpenChange={(isOpen) => {
          if (!isOpen) onClose();
        }}
        footer={<Button onPress={onClose}>{t`Done`}</Button>}
      >
        <SecretReveal label={t`API key`} secret={created.key} masked={maskedKey(created)} />
      </Dialog>
    );
  }
  return (
    <ConfirmKey
      title={t`Regenerate ${name}?`}
      description={t`The old key stops at once. The new one has the same scopes and expiry. You can't undo this.`}
      action={t`Regenerate key`}
      isPending={regenerate.isPending}
      failure={regenerate.error}
      onConfirm={() => {
        regenerate.mutate(apiKey.id, { onSuccess: setCreated });
      }}
      onClose={onClose}
    />
  );
}

/** Spec 0017 AKY-8. */
export function RenameKeyDialog({ projectId, apiKey, onClose }: KeyDialogProps) {
  const { t } = useLingui();
  const rename = useRenameApiKey(projectId, apiKey.id);
  const form = useAppForm({
    defaultValues: { name: apiKey.name },
    schema: renameKeySchema,
    onSubmitAsync: async ({ value }) => {
      try {
        await rename.mutateAsync(renameKeySchema.parse(value).name);
        onClose();
        return undefined;
      } catch (error) {
        return { form: keyFailure(error, t`The key wasn't renamed. Try again in a moment.`) };
      }
    },
  });
  return (
    <Dialog
      title={t`Rename key`}
      isOpen
      onOpenChange={(isOpen) => {
        if (!isOpen) onClose();
      }}
      footer={
        <form.AppForm>
          <Button slot="close">
            <Trans>Cancel</Trans>
          </Button>
          <form.SubmitButton>{t`Rename`}</form.SubmitButton>
        </form.AppForm>
      }
    >
      <form.AppForm>
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="name">
            {(field) => <field.TextField label={t`Name`} maxLength={128} />}
          </form.AppField>
        </form.Form>
      </form.AppForm>
    </Dialog>
  );
}
