"use client";

import { useRouter } from "next/navigation";
import { Suspense, useEffect, type ReactNode } from "react";

import { useProjectId } from "./project-id";

/**
 * A project page renders nothing until its `?project=` id is valid. A missing or invalid id replaces the
 * address with `/console/`, without a history entry (spec 0003, AC-9).
 */
export function ProjectGuard({ children }: { children: ReactNode }) {
  return (
    <Suspense fallback={null}>
      <Guard>{children}</Guard>
    </Suspense>
  );
}

function Guard({ children }: { children: ReactNode }) {
  const project = useProjectId();
  const router = useRouter();

  useEffect(() => {
    if (project.status === "invalid") {
      router.replace("/");
    }
  }, [project.status, router]);

  return project.status === "valid" ? children : null;
}
