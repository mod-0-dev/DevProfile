# winget manifests

Manifests for publishing DevProfile to the Windows Package Manager so users can
`winget install mod-0-dev.DevProfile`.

One folder per released version, each holding the three v1.6 manifest files
(version / installer / en-US locale). The "installer" is the GitHub release zip,
delivered as a portable exe that winget exposes via a `devprofile` command alias.

## Validate

```powershell
winget validate --manifest packaging/winget/<version>
```

## Test-install locally (optional)

Installing from a local manifest is gated behind an admin setting:

```powershell
winget settings --enable LocalManifestFiles      # run once, elevated
winget install   --manifest packaging/winget/<version>
winget uninstall mod-0-dev.DevProfile
```

## Publish to winget (microsoft/winget-pkgs)

`winget install mod-0-dev.DevProfile` only works once these manifests are merged
into the community repo. Either:

- `wingetcreate submit packaging/winget/<version>` (needs a GitHub token), or
- open a PR to `microsoft/winget-pkgs` by hand.

Submission runs automated validation (the installer is downloaded and inspected)
and then needs moderator approval.

## Cut a new version

```bash
gh release download vX.Y.Z --pattern '*.zip' --dir tmp
sha256sum tmp/*.zip
```

Copy the latest version folder, bump `PackageVersion`, the `InstallerUrl`s and the
`InstallerSha256`s, then re-validate.
