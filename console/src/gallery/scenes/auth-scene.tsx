import { useState } from "react";
import { z } from "@/shared/zod";
import { AuthLayout, Button, InlineError, Link } from "@/ui";
import { useAppForm } from "@/ui/form";
import { useGalleryText } from "../pseudo";
import { useSettings } from "../settings";
import { useSceneTitle } from "./scene";

// design.md _Shell_ (pages without the shell): the sign in page's card, with its fields, its large button, the links
// under it, and the error a failed request shows (set Scenario to Fail).

const signInSchema = z.object({
  email: z.string().check(z.trim(), z.minLength(1, "Enter your email address.")),
  password: z.string().check(z.minLength(1, "Enter your password.")),
});

const serverError = { kind: "server", requestId: "req_01JB8Z4Q7W2N" };

/** AuthLayout as the sign in, sign up, and first run setup pages use it. */
export function AuthScene() {
  const g = useGalleryText();
  const { scenario } = useSettings();
  const [failed, setFailed] = useState(false);
  const [signedIn, setSignedIn] = useState(false);
  useSceneTitle("AuthLayout");
  const form = useAppForm({
    defaultValues: { email: "", password: "" },
    schema: signInSchema,
    onSubmitAsync: async () => {
      setFailed(false);
      await new Promise((resolve) => setTimeout(resolve, 600));
      if (scenario === "fail") setFailed(true);
      else setSignedIn(true);
      return undefined;
    },
  });

  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold">{g("Sign in to Orvano")}</h1>
      {failed ? (
        <InlineError
          error={serverError}
          title={g("Couldn't sign you in")}
          onRetry={() => void form.handleSubmit()}
        />
      ) : null}
      {signedIn ? <p>{g("Signed in. The gallery has no account to open.")}</p> : null}
      <form.AppForm>
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="email">
            {(field) => (
              <field.TextField label={g("Email address")} type="email" autoComplete="email" />
            )}
          </form.AppField>
          <form.AppField name="password">
            {(field) => (
              <field.PasswordField label={g("Password")} autoComplete="current-password" />
            )}
          </form.AppField>
          {/* Auth pages use large controls; SubmitButton has one size, so this is its markup at lg. */}
          <form.Subscribe selector={(state) => state.isSubmitting}>
            {(isSubmitting) => (
              <Button variant="primary" size="lg" type="submit" isPending={isSubmitting}>
                {g("Sign in")}
              </Button>
            )}
          </form.Subscribe>
        </form.Form>
      </form.AppForm>
      <div className="flex flex-wrap justify-between gap-2">
        <Link to="." variant="inline">
          {g("Forgot your password?")}
        </Link>
        <Link to="." variant="inline">
          {g("Create an account")}
        </Link>
      </div>
    </AuthLayout>
  );
}
