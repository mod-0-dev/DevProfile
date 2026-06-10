# DevProfile

A portable **developer-environment snapshot** tool for Windows. Tick what you want
to capture from your machine, export it to a folder (OneDrive, USB stick, anywhere),
then replay it on another machine with a preview-before-apply step.

Unlike `winget export` (packages only) or chezmoi (configs only, CLI/git), DevProfile
captures **both packages and developer configs** behind a friendly GUI, offline-portable,
Windows-first.

## Solution layout

```
DevProfile.slnx
src/
  DevProfile.Core/      # provider engine — no UI dependency
    IProvider.cs        # capture / plan / apply contract
    ProfileService.cs   # orchestration + the provider registry
    SecretsCrypto.cs    # AES-256-GCM + PBKDF2 passphrase encryption
    Providers/          # one file per capture target
  DevProfile.App/       # WPF (net10.0-windows) GUI: Create + Apply tabs
tests/
  DevProfile.Core.Tests/  # xunit: crypto, parsing, plan/apply logic, orchestration
```

## What v1 captures

| Category    | Providers                                                            |
|-------------|---------------------------------------------------------------------|
| Packages    | winget, npm globals, .NET global tools                              |
| Git & Hosts | `.gitconfig`, hosts entries (merge-append, needs admin)            |
| VS Code     | `settings.json`, extension list                                     |
| Shell       | PowerShell `$PROFILE`, Windows Terminal settings, user env vars     |
| Secrets     | `.ssh` keys + `.npmrc` — **opt-in, passphrase-encrypted**           |

A profile is just a folder:

```
MyProfile/
  profile.json            # manifest: schema, date, source machine, providers included
  winget/packages.json    # reuses winget's own export format
  git-config/gitconfig
  vscode-settings/settings.json
  vscode-extensions/extensions.txt
  ...
  secrets/secrets.bin     # encrypted, only if you opt in
```

## Design principles

- **Provider model** — each capture target implements `IProvider` (Discover / Capture /
  Plan / Apply). Adding a new target is one new file; nothing else changes.
- **Plan before apply** — restore always previews a diff (install / overwrite / skip) and
  is idempotent. Overwrites are backed up as `*.devprofile.bak`.
- **Secrets never leak** — credential-looking env vars (`*TOKEN*`, `*SECRET*`, …) are
  excluded from the cleartext capture (only their names are recorded, and the Apply plan
  lists them as manual follow-ups); real secrets only travel in the encrypted bundle.
- **Profiles are untrusted input** — a bundle may arrive via OneDrive/USB, so package ids
  are validated against strict shapes before reaching winget/npm/code, the secrets zip is
  guarded against path traversal, and a profile without a valid `profile.json` manifest
  (schema `devprofile/v1`) is refused.

## Build & run

```powershell
dotnet build DevProfile.slnx
dotnet run --project src/DevProfile.App
dotnet test DevProfile.slnx
```

Requires the .NET 10 SDK and the WindowsDesktop runtime. CI builds and tests on
`windows-latest` (see `.github/workflows/ci.yml`).

## Known limitations (v1)

- Hosts-file apply needs an elevated process.
- Encrypted *env-var* secrets aren't captured yet (only `.ssh` / `.npmrc`); secret-named
  env vars are skipped (their names show up in the Apply plan as manual follow-ups).
- JetBrains settings are not yet a provider.
