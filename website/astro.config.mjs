// @ts-check
import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";

// The Orvano docs site. Theming arrives with feature 6, llms.txt output with the docs features.
export default defineConfig({
  integrations: [
    starlight({
      title: "Orvano",
      social: [
        {
          icon: "github",
          label: "GitHub",
          href: "https://github.com/orvanohq/orvano",
        },
      ],
    }),
  ],
});
