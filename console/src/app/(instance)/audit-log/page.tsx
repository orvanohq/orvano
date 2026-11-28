import type { Metadata } from "next";

import { auditLogItem } from "@/shell/nav";
import { StubPage } from "@/shell/stub-page";

export const metadata: Metadata = { title: auditLogItem.title };

export default function Page() {
  return <StubPage item={auditLogItem} />;
}
