import { useRouter, type ErrorComponentProps } from "@tanstack/react-router";
import { ErrorState } from "@/ui";

/** Spec 0003 FLT-2: a page that failed to load, inside the shell. */
export function ShellRouteError({ error }: ErrorComponentProps) {
  const router = useRouter();
  return <ErrorState error={error} onRetry={() => void router.invalidate()} />;
}
