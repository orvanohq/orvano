import { useLingui } from "@lingui/react/macro";
import { EmptyState } from "@/ui";

/** An operator always has an organization today; feature 51's removals can leave one with none. */
export function NoOrganization() {
  const { t } = useLingui();
  return (
    <EmptyState
      kind="not-allowed"
      title={t`No organization`}
      description={t`You aren't a member of any organization.`}
    />
  );
}
