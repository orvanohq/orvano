import { useEffect } from 'react'

/** Sets the document title to `<Page> · <Org or project name> · Orvano` (name left out when unknown). */
export function usePageTitle(page: string, name?: string): void {
  useEffect(() => {
    document.title = [page, name, 'Orvano']
      .filter((part) => part !== undefined && part !== '')
      .join(' · ')
  }, [page, name])
}
