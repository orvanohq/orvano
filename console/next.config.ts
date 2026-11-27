import type { NextConfig } from "next";

// The console is a static export served by orvano at /console.
// It talks to Orvano only through the public API (via the TypeScript SDK, feature 17).
const nextConfig: NextConfig = {
  output: "export",
  basePath: "/console",
  images: { unoptimized: true },
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
