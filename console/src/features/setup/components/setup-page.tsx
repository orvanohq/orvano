import { Trans } from "@lingui/react/macro";
import { AuthLayout } from "@/ui";

// Spec 0016 FRS-4: the first run setup page. The token step (milestone 3) and the owner account form (feature 13)
// join it here.
export function SetupPage() {
  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold">
        <Trans>Set up Orvano</Trans>
      </h1>
    </AuthLayout>
  );
}
