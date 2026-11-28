import type { Metadata } from "next";

import { membersItem } from "@/shell/nav";
import { StubPage } from "@/shell/stub-page";

export const metadata: Metadata = { title: membersItem.title };

export default function Page() {
  return <StubPage item={membersItem} />;
}
