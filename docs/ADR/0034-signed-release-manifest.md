# 0034 — Verified update downloads: signed release manifest and staging

- Status: Accepted
- Date: 2026-10-27
- Task: P06-05

## Context
- Guide P06-05 requires several things of a download:
  - It goes to temp staging.
  - It is checked for size and checksum **and** for trustworthy publisher/source provenance, using a pinned
    signature/public key when available.
  - The extracted files and the version are validated.
  - Without provenance, the user is sent to the manual release and nothing is run automatically.
  - Release-body HTML is never trusted as executable content.
- A checksum published in the same GitHub release only detects corruption: whoever can change the release can change
  both. Proof of origin needs a key that GitHub never holds.
- **Owner decision (2026-10-27, AskUserQuestion): signed manifest** (recommended). The alternatives offered were
  checksum-only and Authenticode (paid, needs an account).
- Constraints: free, offline-first, no new dependencies, no secrets in the app (guide §2).

## Decision
1. **Manifest** (`Core/Updates/ReleaseManifest`). Each release carries two assets:
   - `DynamicIsland-release.json`: `{"schemaVersion":1,"product":"DynamicIsland","version":"X.Y.Z","files":[{"name","size","sha256"}]}`;
   - `DynamicIsland-release.json.sig`: Base64 of an **ECDSA P-256 / SHA-256** signature (IEEE P1363 r‖s) over the
     exact manifest bytes.

   Parsing is strict:
   - the product and schema must match;
   - the version must be SemVer;
   - between 1 and 50 files;
   - safe file names only, with no duplicates;
   - sizes between 1 byte and 1 GB;
   - the hash must be 64 lowercase hex characters;
   - the manifest is at most 64 KB and the signature at most 1 KB.

   Only .NET's built-in `System.Security.Cryptography` is used.
2. **Pinned keys** (`ReleaseSigningKeys.Pinned`, Base64 SubjectPublicKeyInfo).
   - A list, so a new key can be added before the old one is dropped (rotation).
   - **Empty until the owner creates the key pair.** While it is empty, no downloader is registered and the update
     window offers the release page (ADR 0033 ManualOnly).
   - Other curves and key sizes are rejected.
3. **Download** (`Infrastructure/Updates/GitHubUpdateDownloader`), in order:
   - Fetch the manifest and signature (size-bounded) and verify the signature against a pinned key. The manifest must
     name **this release's version** and **this file with GitHub's size**. If anything fails here, the package is
     never requested and the user sees "can't be confirmed as coming from the Dynamic Island project… get it from the
     release page".
   - Check free space: the package size plus 200 MB.
   - Stream to `%LOCALAPPDATA%\DynamicIsland\updates\<version>\<file>.partial`, which is per-user and outside the
     program folder. While streaming, the downloader reports progress, honours cancel, and fails after a 30 s stall
     (no overall timeout, because files are large). It never accepts more bytes than were signed, and hashes as it goes.
   - The SHA-256 and the length must equal the signed values.
   - **Content check** (`UpdatePackageValidator`):
     - The setup file must be a Windows PE file ("MZ" + "PE\0\0").
     - The portable ZIP must contain the release layout (ADR 0030): the root launcher, `current.json` naming this
       version, and `app-<version>/DynamicIsland.exe`.
     - ZIP entries may not be rooted, contain `..`, `.` or `:`, number more than 20,000 entries, or exceed 2 GB
       uncompressed.
     - Nothing is extracted or run.
   - Rename the `.partial` file to the final name.

   Failures are reported as one plain sentence each (interrupted, mismatch, no space, couldn't save). Every failure
   deletes the partial file. Only the version being downloaded is kept in staging. `LastFailure` holds a short code for
   logs and never contains a path or URL. GitHub's redirect to its download host is followed: trust comes from the
   signature, not from the host.
4. **Keys and signing tools** (PowerShell 7, .NET built-ins):
   - `tools/New-ReleaseSigningKey.ps1` writes the private key as PKCS#8 PEM. It refuses a path inside the repository,
     refuses to overwrite an existing key, and restricts the file to the current user with `icacls`. It prints the
     public key.
   - `tools/New-ReleaseManifest.ps1 -Version -Files -KeyPath [-PublicKey]` (the environment-variable key option was removed in P07-01) writes the
     manifest and the signature. It self-verifies, and refuses a key that doesn't match the pinned public key.
   - Interop is proven by a unit test pinned to script output made with a throwaway key whose private half was deleted.
   - CI signing with a GitHub secret is P06-09.
5. **Install stays separate:** the downloader never runs anything. P06-06 adds `IUpdateInstaller`. Until then the
   window keeps offering the release page, because the flow needs both seams.

## Alternatives considered
- **Checksum files only.** These catch corruption but not a tampered release, so the guide forbids auto-install on
  them alone.
- **Authenticode / Azure Trusted Signing.** These cost money and need an account. They might be added later for
  SmartScreen, but they are not needed for provenance.
- **Sigstore / GitHub artifact attestations.** Verifying them in-app needs a large dependency and transparency-log
  access. They might be added later as a CI extra (P06-09) but are not the gate.
- **Ed25519.** It would need an extra package; .NET's built-in cryptography offers ECDSA P-256.

## Consequences
- **Owner action, once and before the first release:**
  - run `tools/New-ReleaseSigningKey.ps1`;
  - back up the private key offline;
  - give the agent the printed public key to pin.
- Losing the private key means installed copies can't verify updates until users install a version by hand. A leaked
  key means rotating: pin a new key and publish a version without the old one.
- Releases without the signed manifest can only be installed by hand (release page).
- Manual rows SG1–SG2 in `docs/TEST_MATRIX.md`.
