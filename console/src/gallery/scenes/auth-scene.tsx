import { AuthLayout, Button } from "@/ui";

/** AuthLayout as the sign in, sign up, and first run setup pages use it. */
export function AuthScene() {
  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold">Sign in to Orvano</h1>
      <p className="text-fg-muted">
        Forms arrive with milestone 3; this shows the frame and the card.
      </p>
      <Button variant="primary" size="lg">
        Sign in
      </Button>
    </AuthLayout>
  );
}
