import type { Metadata } from "next";

import { instanceSettingsItem } from "@/shell/nav";
import { StubPage } from "@/shell/stub-page";

export const metadata: Metadata = { title: instanceSettingsItem.title };

export default function Page() {
  return <StubPage item={instanceSettingsItem} />;
}
