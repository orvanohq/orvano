# Third party notices

Orvano is licensed under Apache 2.0. It also ships these data files from other projects, each under a license that
allows redistribution. They are data, never code. `tools/lists/refresh.mjs` rebuilds them from their sources; update
the commit below each time you run it.

## Common password list

- File: `server/src/Orvano.Auth/Lists/common-passwords.txt` (embedded in `Orvano.Auth`, spec 0014, AC-5 and AC-7)
- Source: `Passwords/Common-Credentials/xato-net-10-million-passwords-100000.txt` from
  [SecLists](https://github.com/danielmiessler/SecLists) at commit `27c08068f849227f2fc1c8f7f00afae365957b96`
- License: SecLists is under the MIT License, Copyright (c) 2018 Daniel Miessler. The underlying data is Mark
  Burnett's ten million password list, which he released into the public domain.
- Changes: Unicode NFKC normalized, lowercased, deduplicated, and limited to entries of 8 to 256 characters.

```
MIT License

Copyright (c) 2018 Daniel Miessler

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Disposable email domain list

- File: `server/src/Orvano.Auth/Lists/disposable-domains.txt` (embedded in `Orvano.Auth`, spec 0014, AC-7 and AC-8)
- Source: `disposable_email_blocklist.conf` from
  [disposable-email-domains](https://github.com/disposable-email-domains/disposable-email-domains) at commit
  `81a8976e63879a11bf4301a28c14e0eb7a7855cf`
- License: CC0 1.0 Universal (public domain dedication),
  <https://creativecommons.org/publicdomain/zero/1.0/legalcode>
- Changes: lowercased, converted to ASCII (IDNA), deduplicated, and sorted.
