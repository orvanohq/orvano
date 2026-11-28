import { ProjectGuard } from "@/shell/project-guard";

/** Project pages read the project from `?project=` and render nothing without a valid one. */
export default function ProjectLayout({ children }: LayoutProps<"/project">) {
  return <ProjectGuard>{children}</ProjectGuard>;
}
