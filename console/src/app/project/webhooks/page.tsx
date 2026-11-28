import type { Metadata } from "next";

import { serviceNav } from "@/shell/nav";
import { StubPage } from "@/shell/stub-page";

const item = serviceNav.webhooks;

export const metadata: Metadata = { title: item.title };

export default function Page() {
  return <StubPage item={item} />;
}
