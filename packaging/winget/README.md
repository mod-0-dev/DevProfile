# winget manifests

Manifests for publishing DevProfile to the Windows Package Manager. Two packages,
because winget allows only one portable exe per zip:

- `mod-0-dev.DevProfile` — the GUI, exposed as the `devprofile-gui` alias
- `mod-0-dev.DevProfile.CLI` — the CLI, owning the `devprofile` alias

One folder per package per released version, each holding the three v1.6 manifest
files (version / installer / en-US locale). The "installer" is the GitHub release
zip, delivered as a portable exe behind a command alias.

## Validate

```powershell
winget validate --manifest packaging/winget/<package-id>/<version>
```

## Test-install locally (optional)

Installing from a local manifest is gated behind an admin setting:

```powershell
winget settings --enable LocalManifestFiles      # run once, elevated
winget install   --manifest packaging/winget/<package-id>/<version>
winget uninstall <package-id>
```

## Publish to winget (microsoft/winget-pkgs)

`winget install mod-0-dev.DevProfile` only works once these manifests are merged
into the community repo. One PR per package version. Either:

- `wingetcreate submit -t <github-token> packaging/winget/<package-id>/<version>`, or
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
