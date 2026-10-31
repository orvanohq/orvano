import { createFileRoute } from "@tanstack/react-router";
import { requireSession } from "@/features/session";
import { ShellLayout } from "@/features/shell/components/shell-layout";

export const Route = createFileRoute("/_shell")({
  beforeLoad: requireSession, // spec 0003 RTE-9
  component: ShellLayout,
});
