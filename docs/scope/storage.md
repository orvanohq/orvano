# Epic: Storage

Files for projects, secured by the same permissions as the database. See [index.md](index.md) for the full plan.

### 20. Buckets & files · needs a decision
Create buckets with size and type limits, then upload, download, list, and delete files from every SDK, with permissions per bucket and per file. A file browser in the console.
**Done when:** a Flutter and a Next.js app upload and download files under permission rules, and you can browse and preview files in the console.
- [ ] Design it (spec): `/architect buckets & files`

### 21. Image transforms, resumable & S3 API · needs a decision
Resize, crop, and convert images on the fly, resumable uploads for large files, signed time limited URLs, and an S3 compatible API so existing tools work.
**Done when:** an image URL with size options returns a transformed image; a large upload survives a dropped connection; a standard S3 client can list and upload.
- [ ] Design it (spec): `/architect image transforms, resumable & S3 API`
