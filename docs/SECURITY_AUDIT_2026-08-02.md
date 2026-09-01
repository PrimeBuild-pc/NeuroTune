# Repository security and privacy audit — updated 2026-09-01

## Scope and method

- Scanned the current tree and every commit reachable from local branches and
  tags for private-key headers and high-confidence GitHub, cloud, and provider
  token formats. No secret was printed during the scan.
- Enumerated author and committer identities across 46 commits, all local refs,
  the two GitHub branches, four release tags, and GitHub's advertised pull
  request refs.
- Created and verified an ignored local recovery bundle before rewriting.
- Rewrote only the personal author/committer email with `git-filter-repo`.
  Commit messages, trees, branch names, and tag names were preserved.
- Compared every rewritten branch/tag target with the backup: eight local
  branch/tag refs, zero tree or subject mismatches.
- Cloned the rewritten GitHub repository as a fresh mirror and repeated the
  identity audit against server-advertised refs.

## Results

| Category | Result | Disposition |
|---|---|---|
| Private keys and high-confidence provider/cloud tokens | none | clean |
| Literal credentials in tracked content/history | none | no rotation required by this audit |
| Current local unreachable objects | none | old unreachable personal-path blobs were removed by rewrite cleanup |
| Local author/committer metadata | noreply only | personal Gmail replaced in all 46 mapped commits |
| GitHub branches and tags | noreply only | both branches and all four release tags force-updated and verified |
| GitHub `main` ruleset | restored | active with non-fast-forward, PR/review, and required `build` rules intact |
| Open PR #10 | rewritten head/base | current head is clean; CI was retriggered by the coordinated push |
| Historical PR refs | personal email remains in PR #4–#9 heads | GitHub-managed read-only refs; normal Git push cannot rewrite them |
| Local recovery bundle | contains pre-rewrite history | ignored, local-only recovery artifact under `artifacts/` |

## Remaining GitHub-hosted copy

The normal public history is clean: a fresh clone of branches and tags contains
only `162145141+PrimeBuild-pc@users.noreply.github.com` and
`noreply@github.com`. GitHub still advertises the original heads for merged PRs
#4–#9 under read-only `refs/pull/*`; those commit objects contain the old email.

GitHub's documented removal process requires a Support request to dereference
affected PRs and clear cached views. The request should identify:

- repository `PrimeBuild-pc/NeuroTune`;
- six affected pull requests, #4 through #9;
- first changed commit `8ae56d493ad216067da9c9546863c3dd0af8f5ae`;
- rewritten root `bc4a08491757e687835792dbbbd6dd9bd1546132`;
- no orphaned LFS objects.

GitHub states that Support may decline removal of data it does not classify as
sensitive. Existing clones or forks are outside the control of this repository
and must be discarded/re-cloned or cleaned independently.
