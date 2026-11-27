import type { Metadata } from "next";
import "./globals.css";

// The design system mapping (tokens, fonts, shell) arrives with feature 5.
export const metadata: Metadata = {
  title: "Orvano",
  description: "Orvano console",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}
