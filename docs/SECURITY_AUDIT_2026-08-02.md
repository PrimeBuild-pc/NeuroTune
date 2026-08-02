# Pre-publication security and privacy audit — 2026-08-02

## Scope and method

- Scanned the current tracked tree and all Git object types, including 281 text
  blobs across reachable history and local unreachable objects.
- Checked for private-key headers, common provider/GitHub/cloud token formats,
  literal API-key/password assignments, credential files, local settings,
  personal Windows paths, device names, email addresses, and VM credential
  references.
- Reviewed every match by file and line without recording the matched secret or
  personal value in this report.
- Ran NuGet and npm vulnerability audits. Rust `cargo audit` is enforced in CI;
  the local binary was not installed during this audit.

## Results

| Category | Result | Disposition |
|---|---|---|
| Private keys and high-confidence provider/cloud tokens | none | clean |
| Literal credentials in tracked content/history | none | code matches were variable assignments or the random VM-password generator |
| Credential/settings files inside the repository | none | CLIXML credentials remain outside the repository under the current user's profile |
| VM credential references | present | acceptable: paths only; encrypted files and values are not tracked |
| Device-name match | false positive | `desktop-schema.json`, not a host name |
| Email-content match | false positive | the asset filename `128x128@2x.png` |
| Personal Windows path | local unreachable blobs only | not part of any branch/tag and not transferable by a normal push |
| Commit author identity | one personal Gmail address in reachable history | unresolved user decision before public visibility |
| NuGet/npm known vulnerabilities | none | clean with configured sources on 2026-08-02 |

## Publication gate

The repository must remain private until PrimeBuild chooses one of these paths:

1. accept that the historical author Gmail address will be publicly visible;
2. approve a destructive author-metadata history rewrite and coordinated force
   push; or
3. keep the repository private.

Changing Git configuration affects only future commits and does not resolve the
existing metadata. No secret rotation is required based on this audit.
