/* eslint-disable lingui/no-unlocalized-strings -- CNS-6: SDK names and code, never translated */
/** Spec 0017 CNS-6: the six SDKs of the Connect panel. Plain text, and code, so never translated. */
export interface ConnectSnippet {
  id: string;
  name: string;
  install: string;
  code: (values: { url: string; projectId: string; key: string }) => string;
}

export const connectSnippets: readonly ConnectSnippet[] = [
  {
    id: "javascript",
    name: "JavaScript",
    install: "npm install @orvano/client",
    code: ({ url, projectId, key }) => `import { createClient } from "@orvano/client";

const orvano = createClient({
  url: "${url}",
  projectId: "${projectId}",
  key: "${key}",
});

console.log(await orvano.keys.current());`,
  },
  {
    id: "node",
    name: "Node.js",
    install: "npm install @orvano/server",
    code: ({ url, projectId, key }) => `import { createClient } from "@orvano/server";

const orvano = createClient({
  url: "${url}",
  projectId: "${projectId}",
  key: "${key}",
});

console.log(await orvano.keys.current());`,
  },
  {
    id: "nextjs",
    name: "Next.js",
    install: "npm install @orvano/nextjs",
    code: ({ url, projectId, key }) => `import { createClient } from "@orvano/nextjs";

const orvano = createClient({
  url: "${url}",
  projectId: "${projectId}",
  key: "${key}",
});

console.log(await orvano.keys.current());`,
  },
  {
    id: "dart",
    name: "Dart",
    install: "dart pub add orvano_client",
    code: ({ url, projectId, key }) => `import 'package:orvano_client/orvano_client.dart';

final orvano = await Orvano.open(
  url: '${url}',
  projectId: '${projectId}',
  key: '${key}',
);

print(await orvano.keys.current());`,
  },
  {
    id: "flutter",
    name: "Flutter",
    install: "flutter pub add orvano_flutter",
    code: ({ url, projectId, key }) => `import 'package:orvano_flutter/orvano_flutter.dart';

final orvano = await Orvano.open(
  url: '${url}',
  projectId: '${projectId}',
  key: '${key}',
);

print(await orvano.keys.current());`,
  },
  {
    id: "dotnet",
    name: ".NET",
    install: "dotnet add package Orvano.Server",
    code: ({ url, projectId, key }) => `using Orvano.Server;

var orvano = new OrvanoClient(new OrvanoOptions
{
    Url = "${url}",
    ProjectId = "${projectId}",
    Key = "${key}",
});

Console.WriteLine(await orvano.Keys.CurrentAsync());`,
  },
];
