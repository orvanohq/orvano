import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { AccountPage } from "@/features/account/components/account-page";

export const Route = createFileRoute("/_shell/account")({
  staticData: { crumb: msg`Account` },
  component: AccountPage,
});
