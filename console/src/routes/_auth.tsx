import { createFileRoute, Outlet } from "@tanstack/react-router";

// Spec 0017 CNS-1: the pages with no shell. Each renders its own AuthLayout, as `/setup` does.
export const Route = createFileRoute("/_auth")({
  component: Outlet,
});
