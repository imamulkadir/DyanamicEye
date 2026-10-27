# 0032 — Update checks: GitHub releases/latest, schedule and trust

- Status: Accepted
- Date: 2026-10-27
- Task: P06-03

## Context
- Guide P06-03: compare SemVer (with a prerelease policy) to the installed version; stable channel; daily checks with
  local cache, jitter and backoff; GitHub `releases/latest` or a static manifest; user agent and quota awareness.
  §7: check after start when allowed and the last check is ≥ 24 h old (with jitter), fail gracefully offline; manual
  "Check now" bypasses the interval but is rate limited; never invent sizes. §5 trust boundaries: GitHub metadata is
  untrusted; HTTPS GitHub endpoints only; size limits; never execute remote content.
- Consent: the daily check is the user's onboarding/settings choice (ADR 0015); IT policy can turn self-update off
  (ADR 0031).
- Live check 2026-10-27: `GET https://api.github.com/repos/imamulkadir/DyanamicEye/releases/latest` → 404 (no
  releases yet); unauthenticated limit 60 requests per hour.

## Decision
1. **Source: GitHub REST `releases/latest`** (`Infrastructure/Updates/GitHubReleaseSource`), unauthenticated — no
   token in the app. Headers: `User-Agent: DynamicIsland/<version> (+https://github.com/<repo>)`,
   `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`, `If-None-Match` with the cached ETag
   (a 304 does not count against the limit). 15 s timeout; response ≤ 1 MB; system proxy. 404 = no release yet;
   403/429 with `X-RateLimit-Remaining: 0`/`X-RateLimit-Reset` or `Retry-After` = rate limited until then; other
   failures = unreachable. Nothing about the user or PC is sent. A static manifest is deferred to P06-05/09 (it will
   carry checksums and signatures; this task only needs "what is the latest version").
2. **Trust** (`Core/Updates/GitHubReleaseParser`): stable channel only (drafts, prereleases and prerelease tags are
   ignored; a beta channel is a later opt-in, guide §7); the tag must be SemVer; release page and every asset URL must
   be `https://github.com/<repo>/releases/…` with no user info or `..`; asset sizes positive and ≤ 1 GB; ≤ 50 assets;
   one bad field rejects the whole release. Release notes are kept as plain text (≤ 20,000 characters) and are only
   ever shown as text. The cache (`data/update-check.json`) is re-validated on load like network data.
3. **Versions** (`Core/Updates/SemanticVersion`): SemVer 2.0 precedence (prerelease below release, numeric vs
   alphanumeric identifiers, build metadata ignored); only a strictly newer release is an update.
4. **Schedule** (`Core/Updates/UpdateChecker`, `App/UpdateCheckScheduler`):
   - IT-managed → never checks, not even "Check now"; automatic checks need consent; "Check now" does not (the user
     asked in that moment);
   - automatic: first look 2 minutes after start (never slowing start), then every 24 h plus a random per-run jitter of
     0–2 h; failures back off 1 h, 2 h, 4 h … up to 24 h; GitHub's reset time is honoured for every check;
   - "Check now" again within 1 minute returns the last answer without a request;
   - not started in Safe Mode; Debug builds under the test data-root hook use an offline source, so tests never reach
     the network.
5. **UI** (until P06-04): Settings › Updates "Check for updates" is live and shows one plain sentence (latest
   version and release date, up to date, no release yet, managed, rate limited, offline); the tray item opens that
   page and checks. Downloading and installing are not offered yet.
6. **Logs:** outcome and the latest published version only (`UpdateCheckFinished`).

## Alternatives considered
- Static GitHub Pages manifest — no rate limit and room for checksums, but needs a publishing step that does not
  exist yet; P06-09 may add it, and the source interface allows switching.
- An API token to raise the rate limit — rejected: no secrets in distributed binaries (guide §2), and one check a day
  is far below 60 per hour.
- `releases` (list) with prerelease filtering — not needed for the stable channel.

## Consequences
- Release tags must be `vX.Y.Z` and published as full (non-prerelease) releases to be offered.
- Manual rows UP1–UP4 in `docs/TEST_MATRIX.md`.
