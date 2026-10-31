import { useLingui } from "@lingui/react/macro";
import { CodeSnippet, SettingsCard, Tab, TabList, TabPanel, Tabs } from "@/ui";
import { connectSnippets } from "./connect-snippets";

const KEY_PLACEHOLDER = "YOUR_PUBLISHABLE_KEY";

/** Spec 0017 CNS-6, spec 0001 UX-37: the SDK picker, with each SDK's setup and a `keys.current` call. */
// oxlint-disable-next-line import/no-default-export -- React.lazy takes a default export
export default function ConnectPanel({ url, projectId }: { url: string; projectId: string }) {
  const { t } = useLingui();
  return (
    <SettingsCard title={t`Connect`}>
      <Tabs>
        <TabList aria-label={t`SDK`}>
          {connectSnippets.map((snippet) => (
            <Tab key={snippet.id} id={snippet.id}>
              {snippet.name}
            </Tab>
          ))}
        </TabList>
        {connectSnippets.map((snippet) => (
          <TabPanel key={snippet.id} id={snippet.id}>
            <div className="flex flex-col gap-4 pt-4">
              <CodeSnippet label={t`Install command`} code={snippet.install} />
              <CodeSnippet
                label={t`Setup code`}
                code={snippet.code({ url, projectId, key: KEY_PLACEHOLDER })}
              />
            </div>
          </TabPanel>
        ))}
      </Tabs>
    </SettingsCard>
  );
}
