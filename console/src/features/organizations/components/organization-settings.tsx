import { useState } from "react";
import { msg } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { useParams } from "@tanstack/react-router";
import { useCan } from "@/shared/access";
import { Button, CopyField, PageHeader, SettingsCard } from "@/ui";
import { organizationQuery } from "../api/queries";
import { RenameOrganizationDialog } from "./organization-dialogs";

/** Spec 0017 CNS-1, CNS-5, ORG-3: the organization's name. Members and delete come with feature 51. */
export function OrganizationSettings() {
  const { t } = useLingui();
  const { orgId } = useParams({ from: "/_shell/$orgId/settings" });
  const { data: organization } = useSuspenseQuery(organizationQuery(orgId));
  const canWrite = useCan("organization", "org.write");
  const [isRenaming, setRenaming] = useState(false);
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t`Organization settings`} />
      <SettingsCard
        title={organization.name}
        footer={
          <Button
            unavailableReason={
              canWrite ? undefined : msg`You don't have permission to rename this organization.`
            }
            onPress={() => {
              setRenaming(true);
            }}
          >
            {t`Rename`}
          </Button>
        }
      >
        <CopyField label={t`Organization ID`} value={organization.id} />
      </SettingsCard>
      {isRenaming ? (
        <RenameOrganizationDialog
          organization={organization}
          onClose={() => {
            setRenaming(false);
          }}
        />
      ) : null}
    </div>
  );
}
