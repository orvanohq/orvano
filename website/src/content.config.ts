import { defineCollection } from 'astro:content'
import { docsLoader } from '@astrojs/starlight/loaders'
import { docsSchema } from '@astrojs/starlight/schema'

/** Every page of the site lives in `src/content/docs/`, generated error pages included. */
export const collections = {
  docs: defineCollection({ loader: docsLoader(), schema: docsSchema() }),
}
