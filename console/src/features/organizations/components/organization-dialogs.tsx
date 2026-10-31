import { Trans, useLingui } from "@lingui/react/macro";
import { useNavigate } from "@tanstack/react-router";
import { ApiError } from "@/shared/api/errors";
import { Button, Dialog } from "@/ui";
import { useAppForm } from "@/ui/form";
import { useCreateOrganization, useUpdateOrganization } from "../api/mutations";
import type { Organization } from "../api/queries";
import { organizationSchema, toOrganizationRequest } from "../schemas/organization";

interface DialogProps {
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
}

/** Spec 0017 CNS-5: a one field dialog; the operator lands in the new organization. */
export function NewOrganizationDialog({ isOpen, onOpenChange }: DialogProps) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const create = useCreateOrganization();
  const form = useAppForm({
    defaultValues: { name: "" },
    schema: organizationSchema,
    onSubmitAsync: async ({ value }) => {
      try {
        const organization = await create.mutateAsync(
          toOrganizationRequest(organizationSchema.parse(value)),
        );
        await navigate({
          to: "/$orgId",
          params: { orgId: organization.id },
          ignoreBlocker: true, // saved, so the guard has nothing to ask
        });
        return undefined;
      } catch {
        return { form: t`The organization wasn't created. Try again in a moment.` };
      }
    },
  });
  return (
    <Dialog
      title={t`New organization`}
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      footer={
        <form.AppForm>
          <Button slot="close">
            <Trans>Cancel</Trans>
          </Button>
          <form.SubmitButton>{t`Create organization`}</form.SubmitButton>
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

/** Spec 0017 CNS-5, ORG-3: a one field dialog. */
export function RenameOrganizationDialog({
  organization,
  onClose,
}: {
  organization: Organization;
  onClose: () => void;
}) {
  const { t } = useLingui();
  const update = useUpdateOrganization(organization);
  const form = useAppForm({
    defaultValues: { name: organization.name },
    schema: organizationSchema,
    onSubmitAsync: async ({ value }) => {
      try {
        await update.mutateAsync(toOrganizationRequest(organizationSchema.parse(value)));
        onClose();
        return undefined;
      } catch (error) {
        return {
          form:
            error instanceof ApiError && error.code === "version_conflict"
              ? t`Someone else changed this organization. Reload the page, then try again.`
              : t`The organization wasn't renamed. Try again in a moment.`,
        };
      }
    },
  });
  return (
    <Dialog
      title={t`Rename organization`}
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
