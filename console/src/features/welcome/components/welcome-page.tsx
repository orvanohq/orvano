import { Trans } from "@lingui/react/macro";
import { AuthLayout } from "@/ui";

// A placeholder until sign in (feature 13) replaces it. It sits in AuthLayout, like the pages without the shell.
export function WelcomePage() {
  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold">
        <Trans>Orvano console</Trans>
      </h1>
      <p className="text-fg-muted">
        <Trans>The server and console are running. Screens arrive with the next features.</Trans>
      </p>
    </AuthLayout>
  );
}
