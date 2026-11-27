# Scope: Storage

Buckets and files with the same permission language as the database. Part of the [Orvano scope](index.md).

## M4 (Phase 2: Core services)

### 42. Buckets & files · needs a decision
The thinnest storage thread: make a bucket, put a file in it, get it back.
**Done when:** a bucket carries a name, a max file size, allowed MIME types and a public or private flag; files can be uploaded in one request, downloaded, listed, replaced and deleted; metadata (owner, size, MIME, `SHA-256`, custom JSON) lives in Postgres and bytes on local disk; uploaded files are served as downloads with a strict CSP so they cannot run as pages.
- [ ] Design it (spec): `/architect buckets & files`

### 43. Storage permissions & signed URLs · needs a decision · Beta
Who may read and write files. A gap here leaks private files, so it gets the stricter tier.
**Done when:** bucket rules and the owner rule use the same permission engine as the database; private files need a valid session or a signed URL that expires; public buckets serve plain URLs; tests prove one user cannot read another user's private file.
- [ ] Design it (spec): `/architect storage permissions & signed urls`

### 44. Resumable uploads · needs a decision
Large files upload in chunks and survive a dropped connection.
**Done when:** files above about 10 MB upload in chunks over the tus protocol; the SDKs pick the mode on their own and resume after a dropped connection.
- [ ] Design it (spec): `/architect resumable uploads`

### 45. S3 compatible storage
A second place to keep the bytes, for people who do not want them on the server's disk.
**Done when:** an instance can store bytes in any S3 compatible store instead of local disk through config alone, with the same behavior.
- [ ] Build it: `/develop s3 compatible storage`

### 46. Image transforms · needs a decision
Resize and convert images on the way out.
**Done when:** `?w=&h=&fit=&format=&q=` returns a transformed image; results are cached on disk under a size cap; output dimensions are limited to stop abuse.
- [ ] Design it (spec): `/architect image transforms`

### 47. File column link · needs a decision
Wires the database's `file` column type to storage.
**Done when:** a `file` column references a stored file; reading the row returns a usable link to it; deleting a row can optionally delete its files.
- [ ] Design it (spec): `/architect file column link`

### 48. Storage console · needs a decision
The console pages for buckets and files.
**Done when:** the console lists buckets, browses files with previews, uploads by drag and drop, and edits bucket settings and rules.
- [ ] Design it (spec): `/architect storage console`

## Deferred
- **CDN**: files served from an edge network
- **Antivirus scanning**: check uploads for malware
- **File versioning**: keep older copies of a file
- **Customer managed keys**: encryption keys the user controls
