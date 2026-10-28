# 0038 — Release pipeline: tag-built drafts, provenance, owner-signed manifest

- Status: Accepted
- Date: 2026-10-28
- Task: P06-09

## Context
Guide P06-09 asks for a CI release pipeline. It should:
- use pinned dependencies;
- build, test and package x64;
- publish hashes and release notes from the changelog;
- add provenance and signing where possible;
- publish stable and beta tags to the right channel;
- create the update manifest only **after** a successful publish.

Constraints from earlier ADRs:
- **ADR 0034:** the manifest signature key is the root of trust for in-app updates. Leaking it lets an attacker
  push code to every user.
- **ADR 0032:** the app checks `releases/latest`, which GitHub never sets to a draft or a pre-release.
- **Release files:** `Publish-Layout.ps1 -Installer` already builds them (ADR 0030/0031). CI already verifies that build
  on every push.

## Decision
1. **Trigger and checks** (`.github/workflows/release.yml`).
   - Runs on a push of a tag `v*`.
   - The tag must be `v<SemVer>`. Its `X.Y.Z` must equal `<Version>` in `Directory.Build.props`.
   - `CHANGELOG.md` must have a `## [<version>]` section (`tools/Get-ReleaseNotes.ps1`); otherwise the job fails before
     building.
2. **Same gates as CI.**
   - Every action is pinned by full commit SHA; a unit test enforces this for every workflow.
   - Restore runs with `--locked-mode`.
   - Release and Debug builds with warnings as errors; all test projects in both configurations.
   - Only then does it package: portable ZIP, setup EXE, and `.sha256` files.
3. **Provenance.** `actions/attest-build-provenance` (v4.2.2, keyless Sigstore, free for public repositories) attests
   both packages. Anyone can check one with `gh attestation verify <file> --repo imamulkadir/DyanamicEye`.
4. **Drafts only; the owner signs.**
   - The workflow creates a **draft** release with the files, hashes and notes. It has no access to the signing key:
     no secret is referenced, and a unit test forbids `secrets.` and `RELEASE_SIGNING_KEY` in the release workflow.
   - The owner runs `tools/Complete-Release.ps1 -Tag vX.Y.Z -KeyPath <pem> [-Publish]` on their own PC. The script:
     - checks the release is a stable draft;
     - checks the private key matches a key pinned in `ReleaseSigningKeys`;
     - downloads the exact draft files into a new temporary folder;
     - checks each file against its `.sha256` **and** its provenance attestation;
     - signs the manifest over those bytes and uploads `DynamicIsland-release.json` and `.sig`;
     - with `-Publish`, publishes the release as latest.
   - "Manifest only after a successful publish" is therefore met: the manifest is made from the files as GitHub
     stores them, after the build, test and package steps have all passed.
5. **Channels.**
   - `vX.Y.Z` is a stable draft. Once published as latest, the app's daily check finds it.
   - `vX.Y.Z-label.N` is a pre-release draft. The app never sees it (`releases/latest` skips pre-releases), and it
     carries no manifest, so it is a manual download for testers. There is no in-app beta channel in v1.
6. **Code signing (Authenticode) stays optional** (ADR 0037). It is not part of this pipeline.

## Consequences
- A release needs two human steps: push the tag, then run `Complete-Release.ps1`. That is deliberate: the step that
  makes code trusted by every installed copy stays offline and with the owner.
- If the key ever has to move to CI, that needs a new ADR: an environment with required reviewers, a secret scoped to
  that environment, and key rotation via a new pinned key.
- The workflow cannot be validated end to end without pushing a tag. Test-matrix rows **RL1** (pre-release dry run)
  and **RL2** (first stable release) cover it.
