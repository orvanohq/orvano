import type { Metadata } from "next";

import { projectsItem } from "@/shell/nav";
import { StubPage } from "@/shell/stub-page";

export const metadata: Metadata = { title: projectsItem.title };

export default function Page() {
  return <StubPage item={projectsItem} />;
}
